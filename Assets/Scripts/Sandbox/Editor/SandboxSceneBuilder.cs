using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TCGinFU.Sandbox.Editor
{
    /// <summary>
    /// 生成一个可直接 Play 的战斗沙盒场景。
    /// 场景内容整个由代码建出来，所以不用把 .unity 文件的 GUID 纳入版本管理也能随时重建。
    /// </summary>
    public static class SandboxSceneBuilder
    {
        public const string ScenePath = "Assets/Scenes/BattleSandbox.unity";

        [MenuItem("TCGinFU/生成战斗沙盒场景")]
        public static void GenerateAndOpen()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var sandbox = new GameObject("BattleSandbox");
            sandbox.AddComponent<BattleSandbox>();
            Undo.RegisterCreatedObjectUndo(sandbox, "创建战斗沙盒");

            string directory = Path.GetDirectoryName(ScenePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"场景保存失败：{ScenePath}");
                return;
            }

            AssetDatabase.Refresh();
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath));
            Debug.Log($"已生成战斗沙盒场景：{ScenePath}　按 Play 即可开打。");
        }
    }
}
