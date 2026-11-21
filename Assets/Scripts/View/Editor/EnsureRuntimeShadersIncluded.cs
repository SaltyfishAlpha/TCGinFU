using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TCGinFU.View.Editor
{
    /// <summary>
    /// 保证运行时 <c>Shader.Find</c> 要用的 shader 被打进构建。
    /// <para>
    /// 桌面上的卡牌材质是运行时 new 出来的，**没有任何资产引用那个 shader**，
    /// 所以 Unity 构建时会把它剥离。结果是编辑器里一切正常、Build 出来区域底板、
    /// 选中环、高亮全部消失——只在构建产物里复现，极难查。
    /// </para>
    /// <para>
    /// 挂成构建前置回调而不是只给个菜单项：这种事只要有一次忘了做就会重现，
    /// 而症状又不指向原因。让它在每次 Build 时自动发生。
    /// </para>
    /// </summary>
    public sealed class EnsureRuntimeShadersIncluded : IPreprocessBuildWithReport
    {
        /// <summary>运行时按名字查找、因而需要显式保留的 shader。</summary>
        private static readonly string[] RequiredShaders = { CardMeshFactory.ShaderName };

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) => Apply(logWhenAlreadyOk: false);

        [MenuItem("TCGinFU/检查：运行时 shader 是否会被打进构建", priority = 20)]
        public static void ApplyFromMenu() => Apply(logWhenAlreadyOk: true);

        private static void Apply(bool logWhenAlreadyOk)
        {
            Object settings = AssetDatabase
                .LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")
                .FirstOrDefault();

            if (settings == null)
            {
                Debug.LogError("[构建] 读不到 ProjectSettings/GraphicsSettings.asset，无法检查 shader 保留。");
                return;
            }

            var serialized = new SerializedObject(settings);
            SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null)
            {
                Debug.LogError("[构建] GraphicsSettings 里找不到 m_AlwaysIncludedShaders。");
                return;
            }

            int added = 0;

            foreach (string name in RequiredShaders)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError($"[构建] 工程里找不到 shader「{name}」，构建出来的画面会不对。");
                    continue;
                }

                bool present = Enumerable.Range(0, list.arraySize)
                    .Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as Shader)
                    .Any(s => s == shader);

                if (present) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                added++;
                Debug.Log($"[构建] 已把「{name}」加入 Always Included Shaders，否则它会被剥离。");
            }

            if (added > 0)
            {
                serialized.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
            }
            else if (logWhenAlreadyOk)
            {
                Debug.Log($"[构建] 运行时 shader 已全部在 Always Included Shaders 里（共 {RequiredShaders.Length} 个），无需改动。");
            }
        }
    }
}
