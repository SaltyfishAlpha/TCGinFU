using TCGinFU.Presentation;
using UnityEngine;

namespace TCGinFU.Sandbox
{
    /// <summary>
    /// 运行时战斗沙盒。挂在场景里任意一个 GameObject 上，按下 Play 就能打。
    /// 用 OnGUI 而不是 uGUI——沙盒的目的是验规则，不该为它先搭一套 Canvas 层级。
    /// </summary>
    [AddComponentMenu("TCGinFU/战斗沙盒")]
    public sealed class BattleSandbox : MonoBehaviour
    {
        [Tooltip("随机种子。同一个种子会重现完全相同的一局（洗牌与骰点都确定性）")]
        [SerializeField] private int seed = 20251117;

        [Range(1, 4)]
        [SerializeField] private int partySize = 2;

        [SerializeField] private BattleScenario enemy = BattleScenario.哥布林;

        [Tooltip("界面整体缩放，4K 屏上可以调大")]
        [Range(0.6f, 2.5f)]
        [SerializeField] private float uiScale = 1.2f;

        private BattleSession _session;

        private void Start()
        {
            _session = new BattleSession { Seed = seed, PartySize = partySize, EnemyPreset = enemy };
            _session.Restart();
        }

        private void OnGUI()
        {
            if (_session == null) return;

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * uiScale);

            float width = Screen.width / uiScale;
            float height = Screen.height / uiScale;

            GUILayout.BeginArea(new Rect(8, 8, width - 16, height - 16));
            SandboxGui.Draw(_session, width - 16, height - 16);
            GUILayout.EndArea();

            GUI.matrix = previous;
        }
    }
}
