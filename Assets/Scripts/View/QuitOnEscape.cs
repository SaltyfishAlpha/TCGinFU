using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TCGinFU.View
{
    /// <summary>
    /// 按 Esc 退出。构建产物默认是全屏无边框的，没有这个就只能用任务管理器关掉。
    /// <para>编辑器里按 Esc 则是退出 Play 模式，方便一致地测试。</para>
    /// </summary>
    [AddComponentMenu("TCGinFU/按 Esc 退出")]
    public sealed class QuitOnEscape : MonoBehaviour
    {
        private void Update()
        {
            if (EscapePressed()) AppExit.Quit();
        }

        private static bool EscapePressed()
        {
            // 本工程 Active Input Handling 设的是 Input System Package，
            // 老的 Input.GetKeyDown 会直接抛异常
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Escape);
#endif
        }
    }
}
