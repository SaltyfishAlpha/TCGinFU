using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>退出程序。编辑器里退出 Play 模式，构建产物里退出进程。</summary>
    public static class AppExit
    {
        public static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
