using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TCGinFU.View.Editor
{
    /// <summary>
    /// 生成正式战斗场景与默认布局资产。
    /// 场景里只有一个挂着 <see cref="BattleView"/> 的物体，界面全在运行时按布局资产建出来——
    /// 所以场景文件几乎是空的，不会成为版本冲突的重灾区。
    /// </summary>
    public static class BattleSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/Battle.unity";
        public const string LayoutPath = "Assets/Settings/BattleLayout.asset";

        [MenuItem("TCGinFU/生成战斗场景", priority = 0)]
        public static void GenerateAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            BattleLayoutAsset layout = LoadOrCreateLayout();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var go = new GameObject("BattleView");
            var view = go.AddComponent<BattleView>();

            // layout 是私有 [SerializeField]，用 SerializedObject 赋值，避免为了建场景把字段开成 public
            var serialized = new SerializedObject(view);
            SerializedProperty property = serialized.FindProperty("layout");
            if (property != null)
            {
                property.objectReferenceValue = layout;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("BattleView 上找不到 layout 字段，场景里需要手动拖一下布局资产。");
            }

            EnsureDirectory(ScenePath);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"场景保存失败：{ScenePath}");
                return;
            }

            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log($"已生成战斗场景 {ScenePath}，布局资产 {LayoutPath}。按 Play 开打；改布局直接改那个资产。");
        }

        [MenuItem("TCGinFU/重建默认布局资产", priority = 1)]
        public static void RegenerateLayout()
        {
            BattleLayoutAsset existing = AssetDatabase.LoadAssetAtPath<BattleLayoutAsset>(LayoutPath);
            if (existing != null
                && !EditorUtility.DisplayDialog(
                    "重建布局",
                    $"{LayoutPath} 已存在，重建会覆盖你改过的区域配置。继续？",
                    "覆盖", "取消"))
            {
                return;
            }

            BattleLayoutAsset fresh = BattleLayoutAsset.CreateDefault();

            if (existing != null)
            {
                EditorUtility.CopySerialized(fresh, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(fresh);
            }
            else
            {
                EnsureDirectory(LayoutPath);
                AssetDatabase.CreateAsset(fresh, LayoutPath);
            }

            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<BattleLayoutAsset>(LayoutPath));
            Debug.Log($"已重建默认布局：{LayoutPath}");
        }

        private static BattleLayoutAsset LoadOrCreateLayout()
        {
            BattleLayoutAsset layout = AssetDatabase.LoadAssetAtPath<BattleLayoutAsset>(LayoutPath);
            if (layout != null) return layout;

            layout = BattleLayoutAsset.CreateDefault();
            EnsureDirectory(LayoutPath);
            AssetDatabase.CreateAsset(layout, LayoutPath);
            AssetDatabase.SaveAssets();
            return layout;
        }

        private static void EnsureDirectory(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }
        }
    }
}
