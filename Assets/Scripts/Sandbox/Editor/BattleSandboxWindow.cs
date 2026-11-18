using TCGinFU.Presentation;
using UnityEditor;
using UnityEngine;

namespace TCGinFU.Sandbox.Editor
{
    /// <summary>
    /// 编辑器内的战斗沙盒，**不需要进 Play 模式、不需要场景**。
    /// 菜单：TCGinFU → 战斗沙盒。
    /// </summary>
    public sealed class BattleSandboxWindow : EditorWindow
    {
        private BattleSession _session;

        [MenuItem("TCGinFU/战斗沙盒")]
        public static void Open()
        {
            BattleSandboxWindow window = GetWindow<BattleSandboxWindow>("战斗沙盒");
            window.minSize = new Vector2(980, 640);
            window.Show();
        }

        private void OnEnable() => _session ??= new BattleSession();

        private void OnGUI()
        {
            if (_session == null) return;

            SandboxGui.Draw(_session, position.width, position.height);

            // IMGUI 只在有事件时重绘，拖滚动条之外的状态变化容易画不出来，这里强制跟手
            if (Event.current.type == EventType.MouseUp) Repaint();
        }
    }
}
