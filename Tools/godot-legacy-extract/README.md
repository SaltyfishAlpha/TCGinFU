# 老版本（Godot）数据提取

《最终物语·命刻之战》v1.0.0 的源码已丢失，真实卡牌数据是从发行版可执行文件里反编译出来的。
本目录保留**全过程的工具与产物**，保证迁移结果可复现、可对账。

设计说明见 [Assets/docs/data_architecture.md](../../Assets/docs/data_architecture.md) §1。

## 怎么跑

需要 Node 18+（用到 `zlib.zstdDecompressSync`，Node 22 起可用；本机实测 v24）。

```bash
# ① 从自包含 exe 里剥出内嵌 PCK
node extract_pck.js "<老游戏>/runtime/game_bin.exe" ./pck

# ② 把 PCK 里的 .gdc 反编译回 .gd
node gdc_decompile.js ./pck/scripts ./src
```

`src/` 里是已经跑出来的结果（9 个文件、3992 行），可以直接看，不必重跑。

## 原理

**PCK**：Godot 4 的自包含导出把资源包粘在 exe 尾部，末 12 字节是
`[int64 pck_size][magic "GDPC"]`。本例 `pack_format = 2`、`pack_flags = 2`
（`PACK_REL_FILEBASE`），所以每个文件的真实偏移是 `pck起点 + file_base + entry.offset`
——漏掉 `pck起点` 这一项会读出一堆零字节，是这里唯一容易踩的坑。

**`.gdc`**：Godot 4.3 起 `.gdc` 不是字节码，而是**词法 token 流**
（magic `GDSC`，version 100，zstd 压缩）。布局：

```
u32 identifier_count / constant_count / token_line_count / _unused / token_count
identifiers[]  u32 len + len 个 u32，每个字符 XOR 0xb6b6b6b6
constants[]    encode_variant() 流
line map[]     (u32 token_index, u32 line)   × token_line_count
column map[]   (u32 token_index, u32 column) × token_line_count
tokens[]       (u32 type_and_payload, u32 line) × token_count
               type = v & 0x7f，payload = v >> 8（标识符表或常量表的下标）
```

因为标识符表与常量表都完整保留，反编译结果在**语义上与原始源码等价**，
只丢失注释和空行（token 流里本来就不存）。缩进从 column map 还原（制表符宽 4）。

token 枚举顺序取自 `godotengine/godot@4.4.1-stable` 的
`modules/gdscript/gdscript_tokenizer.h`。校验方式：`main.gd` 还原出
`extends Control` 与 `func _ready() -> void:`，`EXTENDS=59` / `FUNC=60` / `VAR=70` /
`VOID=71` / `COLON=83` / `FORWARD_ARROW=85` 逐项对得上。

## 文件

| 文件 | 迁移里的角色 |
| ----- | ----- |
| `src/card_database.gd` | **主要来源**：49 张卡全部字段 + 2 份主卡组配方 + 4 份形态组配方 |
| `src/boss_database.gd` | **主要来源**：11 个首领意图 + 两阶段意图池 |
| `src/battle_state.gd` | 角色/敌人面板数值、命刻与阶段机制 |
| `src/card_resolver.gd` | 每张卡的特殊结算逻辑（本轮留桩，未迁移） |
| `src/battle_ui.gd` 等 | 老版表现层，仅供参考，不迁移 |
