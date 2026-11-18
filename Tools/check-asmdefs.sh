#!/usr/bin/env bash
#
# 按 asmdef 的真实程序集边界编译整个工程，不启动 Unity。
#
# 为什么需要它：把所有 .cs 塞进一个 csproj 编译只能证明「C# 语法对」，
# 会把程序集边界整个抹掉——asmdef 少写一个 references 这种错误一个都抓不到。
# （实际发生过：TCGinFU.Sandbox.Editor 漏了 TCGinFU.Presentation，
#   单工程编译全绿，Unity 里直接报 CS0234。）
#
# 实现上刻意**不用 ProjectReference**：MSBuild 的 P2P 引用是传递的，
# A→B→C 时 A 不写 C 也能编过；而 Unity 的 asmdef 引用不传递，用到谁就必须写谁。
# 所以这里按依赖顺序逐个编译，各自只引用**显式列出**的那几个 dll，
# 与 Unity 的行为一致。
#
# 用法：bash Tools/check-asmdefs.sh
#
set -u

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="${TMPDIR:-/tmp}/tcginfu-asmdef-check"
BIN="$OUT/bin"
SCRIPT_ASSEMBLIES="$REPO/Library/ScriptAssemblies"

UNITY_MANAGED="${UNITY_MANAGED:-}"
if [[ -z "$UNITY_MANAGED" ]]; then
    # 从 Unity 生成的 csproj 里扒出本机 Unity 的 Managed 目录
    UNITY_MANAGED="$(grep -ohm1 'HintPath>[^<]*UnityEngine.CoreModule.dll' "$REPO"/*.csproj 2>/dev/null \
        | head -1 | sed 's|HintPath>||; s|[\\/]UnityEngine[\\/]UnityEngine.CoreModule.dll||' | tr '\\' '/')"
fi

if [[ -z "$UNITY_MANAGED" || ! -d "$UNITY_MANAGED" ]]; then
    echo "找不到 Unity 的 Managed 目录。"
    echo "请先在 Unity 里打开过一次工程（会生成 .csproj），或手动指定："
    echo "  UNITY_MANAGED='D:/Unity Hub/Editor/<版本>/Editor/Data/Managed' bash Tools/check-asmdefs.sh"
    exit 1
fi

if [[ ! -d "$SCRIPT_ASSEMBLIES" ]]; then
    echo "找不到 $SCRIPT_ASSEMBLIES —— 包的程序集（UnityEngine.UI 等）需要 Unity 先编译一次。"
    exit 1
fi

rm -rf "$OUT"; mkdir -p "$BIN"

# Git Bash 的 /d/... 路径 MSBuild 不认（会当成 C:\d\...），统一转成 Windows 路径
win_path() {
    if command -v cygpath >/dev/null 2>&1; then cygpath -w "$1"; else echo "$1"; fi
}

# 包提供的程序集 → dll。不在表里的外部引用会提示并跳过。
package_dll() {
    case "$1" in
        UnityEngine.UI|Unity.InputSystem|UnityEngine.TestRunner|UnityEditor.TestRunner)
            echo "$SCRIPT_ASSEMBLIES/$1.dll" ;;
        *) echo "" ;;
    esac
}

json_string() { sed -n "s/.*\"$2\"[[:space:]]*:[[:space:]]*\"\([^\"]*\)\".*/\1/p" "$1" | head -1; }
json_bool()   { sed -n "s/.*\"$2\"[[:space:]]*:[[:space:]]*\(true\|false\).*/\1/p" "$1" | head -1; }
json_refs() {
    # 空数组要单独处理：`"references": [],` 一行里同时有 [ 和 ]，
    # 用 sed 范围会一路吃到下一行的 ] 把 includePlatforms 也捞进来
    grep -q '"references"[[:space:]]*:[[:space:]]*\[[[:space:]]*\]' "$1" && return 0

    sed -n '/"references"[[:space:]]*:[[:space:]]*\[/,/\]/p' "$1" \
        | grep -oE '"[^"]+"' | grep -v '^"references"$' | tr -d '"'
}

mapfile -t ASMDEFS < <(find "$REPO/Assets" -name "*.asmdef" | sort)
echo "发现 ${#ASMDEFS[@]} 个 asmdef"

declare -A FILE_OF REFS_OF
NAMES=()
for f in "${ASMDEFS[@]}"; do
    name="$(json_string "$f" name)"
    NAMES+=("$name")
    FILE_OF["$name"]="$f"
    REFS_OF["$name"]="$(json_refs "$f" | tr '\n' ' ')"
done

is_internal() { [[ -n "${FILE_OF[$1]:-}" ]]; }

generate_csproj() {
    local name="$1" f="${FILE_OF[$1]}" dir
    dir="$(dirname "$f")"
    local proj_dir="$OUT/$name"
    mkdir -p "$proj_dir"
    local csproj="$proj_dir/$name.csproj"
    local no_engine; no_engine="$(json_bool "$f" noEngineReferences)"

    {
        echo '<Project Sdk="Microsoft.NET.Sdk">'
        echo '  <PropertyGroup>'
        echo '    <TargetFramework>netstandard2.1</TargetFramework>'
        echo '    <LangVersion>9.0</LangVersion>'
        echo '    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
        echo '    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>'
        echo "    <AssemblyName>$name</AssemblyName>"
        echo '    <NoWarn>CS0649;CS0414</NoWarn>'
        echo '    <DefineConstants>$(DefineConstants);UNITY_2021_1_OR_NEWER;UNITY_EDITOR;ENABLE_INPUT_SYSTEM;UNITY_INCLUDE_TESTS</DefineConstants>'
        echo '  </PropertyGroup>'

        echo '  <ItemGroup>'
        # 一个 .cs 属于「目录是它最长前缀」的那个 asmdef。
        # 必须按最长前缀判断：Sandbox/Editor 嵌在 Sandbox 里，
        # 简单地「属于别的 asmdef 目录就跳过」会把 Editor 自己的文件也跳掉，
        # 结果该程序集一个文件都不编译、永远是绿的（这个坑踩过）。
        local count=0
        while IFS= read -r cs; do
            local owner="" other od
            for other in "${ASMDEFS[@]}"; do
                od="$(dirname "$other")"
                if [[ "$cs" == "$od"/* ]] && [[ ${#od} -gt ${#owner} ]]; then owner="$od"; fi
            done
            if [[ "$owner" == "$dir" ]]; then
                echo "    <Compile Include=\"$(win_path "$cs")\" />"
                count=$((count + 1))
            fi
        done < <(find "$dir" -name "*.cs" | sort)
        echo '  </ItemGroup>'

        [[ $count -eq 0 ]] && echo "⚠ $name 没有收到任何 .cs，检查结果不可信" >&2

        echo '  <ItemGroup>'
        if [[ "$no_engine" != "true" ]]; then
            local m
            for m in UnityEngine.CoreModule UnityEngine.IMGUIModule UnityEngine.TextRenderingModule \
                     UnityEngine.UIModule UnityEngine.InputLegacyModule UnityEngine.PhysicsModule \
                     UnityEngine.AnimationModule UnityEngine.JSONSerializeModule; do
                [[ -f "$UNITY_MANAGED/UnityEngine/$m.dll" ]] && \
                    echo "    <Reference Include=\"$m\"><HintPath>$(win_path "$UNITY_MANAGED/UnityEngine/$m.dll")</HintPath></Reference>"
            done
            echo "    <Reference Include=\"UnityEngine\"><HintPath>$(win_path "$UNITY_MANAGED/UnityEngine/UnityEngine.dll")</HintPath></Reference>"
            echo "    <Reference Include=\"UnityEditor\"><HintPath>$(win_path "$UNITY_MANAGED/UnityEditor.dll")</HintPath></Reference>"
        fi

        local ref dll
        for ref in ${REFS_OF[$name]}; do
            if is_internal "$ref"; then
                # 直接引用已编译好的 dll——dll 引用不传递，这正是 asmdef 的语义
                echo "    <Reference Include=\"$ref\"><HintPath>$(win_path "$BIN/$ref.dll")</HintPath></Reference>"
            else
                dll="$(package_dll "$ref")"
                if [[ -n "$dll" && -f "$dll" ]]; then
                    echo "    <Reference Include=\"$ref\"><HintPath>$(win_path "$dll")</HintPath></Reference>"
                else
                    echo "⚠ $name 引用了 $ref，脚本没有它的 dll 映射，已跳过" >&2
                fi
            fi
        done
        echo '  </ItemGroup>'

        [[ "$name" == *Tests* ]] && \
            echo '  <ItemGroup><PackageReference Include="NUnit" Version="3.14.0" /></ItemGroup>'

        echo '</Project>'
    } > "$csproj"

    echo "$csproj"
}

# 按依赖顺序编译：某个程序集的内部依赖全部就绪后才轮到它
declare -A BUILT
REMAINING=("${NAMES[@]}")
FAILED=0
echo

while [[ ${#REMAINING[@]} -gt 0 ]]; do
    PROGRESS=0
    NEXT=()

    for name in "${REMAINING[@]}"; do
        ready=1
        for ref in ${REFS_OF[$name]}; do
            if is_internal "$ref" && [[ -z "${BUILT[$ref]:-}" ]]; then ready=0; break; fi
        done

        if [[ $ready -eq 0 ]]; then NEXT+=("$name"); continue; fi

        csproj="$(generate_csproj "$name")"
        if output="$(dotnet build "$csproj" -v q --nologo -o "$(win_path "$BIN")" 2>&1)"; then
            echo "  ✓ $name"
        else
            FAILED=1
            echo "  ✗ $name"
            echo "$output" | grep -oE "error [A-Z]+[0-9]+: .*\]" | sed 's/ \[.*\]$//' | sort -u | head -8 | sed 's/^/      /'
        fi

        BUILT["$name"]=1
        PROGRESS=1
    done

    if [[ $PROGRESS -eq 0 ]]; then
        echo "  ✗ 依赖成环，剩余：${REMAINING[*]}"
        FAILED=1
        break
    fi

    REMAINING=("${NEXT[@]+${NEXT[@]}}")
done

echo
if [[ $FAILED -eq 0 ]]; then
    echo "全部通过：asmdef 的引用关系与源码一致。"
else
    echo "有程序集编译失败。CS0234/CS0246 多半是某个 asmdef 少写了 references。"
    exit 1
fi
