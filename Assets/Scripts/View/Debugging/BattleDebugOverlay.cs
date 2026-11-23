// ╔══════════════════════════════════════════════════════════════════════╗
// ║  调试专用，不是游戏功能的一部分。                                        ║
// ║                                                                      ║
// ║  要彻底移除：删掉本文件，再删掉 BattleSceneBuilder 里 AddComponent      ║
// ║  那一行即可。BattleView 不引用它，删了不影响任何功能。                   ║
// ║                                                                      ║
// ║  不移除也没关系：正式构建（非 Development Build）下它在 Awake 里直接     ║
// ║  把自己销毁，不会绘制、不会耗性能。                                      ║
// ║                                                                      ║
// ║  刻意**没有**整体包 #if：那样做会让正式包里的场景留下 missing script。   ║
// ╚══════════════════════════════════════════════════════════════════════╝

using System.Collections.Generic;
using System.Linq;
using System.Text;
using TCGinFU.Core;
using TCGinFU.Presentation;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TCGinFU.View.Debugging
{
    /// <summary>
    /// 战斗调试浮层：完整战报 + 实时状态快照。按 F1 开关。
    /// <para>
    /// 用 IMGUI 而不是 uGUI：它不往场景里留任何层级，删掉组件就彻底消失，
    /// 不会在正式界面上留下孤儿节点。
    /// </para>
    /// </summary>
    [AddComponentMenu("TCGinFU/战斗调试浮层（调试专用）")]
    [RequireComponent(typeof(BattleView))]
    public sealed class BattleDebugOverlay : MonoBehaviour
    {
        [SerializeField] private bool visibleOnStart = true;

        private const string ToggleKeyName = "F1";

        [Range(0.6f, 2f)]
        [SerializeField] private float uiScale = 1f;

        [Tooltip("浮层宽度占屏幕的比例")]
        [Range(0.2f, 0.9f)]
        [SerializeField] private float widthRatio = 0.34f;

        private BattleView _view;
        private bool _visible;
        private Vector2 _logScroll;
        private Vector2 _stateScroll;
        private bool _autoScroll = true;
        private bool _showState = true;
        private int _lastEventCount = -1;
        private string _cachedLog = "";

        private void Awake()
        {
            // 正式构建里直接消失，留着也不会碍事
            if (!Application.isEditor && !UnityEngine.Debug.isDebugBuild)
            {
                Destroy(this);
                return;
            }

            _view = GetComponent<BattleView>();
            _visible = visibleOnStart;
        }

        private void Update()
        {
            // 本工程 Active Input Handling 是 Input System Package，
            // 老的 Input.GetKeyDown 会抛异常，所以按编译期开关走对应的 API。
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame) _visible = !_visible;
#else
            if (Input.GetKeyDown(KeyCode.F1)) _visible = !_visible;
#endif
        }

        private void OnGUI()
        {
            // 没开起局时这个浮层没东西可显示，加载报告由 BattleView 整屏摊开
            if (!_visible || _view?.Session == null || !_view.Session.IsLoaded) return;

            Matrix4x4 previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * uiScale);

            float screenW = Screen.width / uiScale;
            float screenH = Screen.height / uiScale;
            float width = screenW * widthRatio;

            GUILayout.BeginArea(
                new Rect(screenW - width - 6f, 50f, width, screenH - 58f),
                GUIContent.none, GUI.skin.box);

            DrawHeader();
            if (_showState) DrawState();
            DrawLog();

            GUILayout.EndArea();
            GUI.matrix = previous;
        }

        private void DrawHeader()
        {
            BattleState state = _view.Session.State;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>调试浮层</b>（{ToggleKeyName} 开关）");
            GUILayout.FlexibleSpace();
            _showState = GUILayout.Toggle(_showState, "状态", GUILayout.Width(50));
            _autoScroll = GUILayout.Toggle(_autoScroll, "跟随", GUILayout.Width(50));
            if (GUILayout.Button("关闭", GUILayout.Width(44))) _visible = false;
            GUILayout.EndHorizontal();

            PendingDecision pending = _view.Session.Engine.Pending;
            string waiting = pending.WaitingOn.Count == 0
                ? "—"
                : string.Join("、", pending.WaitingOn.Select(id => state.Find(id)?.Name ?? id));

            GUILayout.Label(
                $"种子 {_view.Session.Seed}　第 {state.Round} 轮　{state.Phase}　命刻 {state.SceneClock}");
            GUILayout.Label($"等待：{pending.Kind}（{waiting}）");

            if (pending.Intent != null)
            {
                string source = state.Find(pending.IntentSourceId)?.Name ?? "?";
                GUILayout.Label(
                    $"待结算意图：{source} 「{pending.Intent.Label}」"
                    + $"{pending.Intent.Damage} 点{pending.Intent.Element.DisplayName()}"
                    + $" → {string.Join("、", _view.Session.Engine.CurrentIntentTargets.Select(t => t.Name))}");
            }

            GUILayout.Label($"结果：{state.Outcome}　事件数 {_view.Session.Engine.Events.Count}");
        }

        private void DrawState()
        {
            _stateScroll = GUILayout.BeginScrollView(_stateScroll, GUILayout.Height(210f));
            GUILayout.Label(BuildStateDump());
            GUILayout.EndScrollView();
        }

        private string BuildStateDump()
        {
            BattleState state = _view.Session.State;
            var builder = new StringBuilder();

            foreach (PlayerCharacter player in state.Players)
            {
                builder.AppendLine(
                    $"<b>{player.Name}</b> {(player.Row == Row.Front ? "前" : "后")}{player.Slot}"
                    + (player.IsDown ? " [倒下]" : player.IsInCrisis ? " [危机]" : ""));
                builder.AppendLine(
                    $"  HP {player.Hp}/{player.MaxHp}  MP {player.Mp}/{player.MaxMp}  AP {player.Ap}"
                    + $"  盾 物{player.Shields.Physical}(自{player.Shields.AutoPhysical})"
                    + $"/魔{player.Shields.Magical}(自{player.Shields.AutoMagical})");
                builder.AppendLine(
                    $"  手{player.Zones.Hand.Count} 组{player.Zones.Deck.Count} 弃{player.Zones.Discard.Count}"
                    + $" 充能{player.Zones.Charges.Count}/{player.Zones.Charges.Capacity}"
                    + $" 持续[{player.Zones.Continuous?.Name ?? "空"}]");

                if (player.Zones.Charges.Count > 0)
                {
                    builder.AppendLine("  充能牌：" + string.Join("、", player.Zones.Charges.Tokens
                        .Select(t => $"{t.Card.Name}(来自{state.Find(t.OwnerId)?.Name ?? t.OwnerId})")));
                }

                if (player.Transform.Active) builder.AppendLine($"  {player.Transform}");
                AppendStatuses(builder, player);
                builder.AppendLine($"  本轮伤害 {player.DamageDealtThisRound}　上轮 {player.DamageDealtLastRound}");
            }

            foreach (Enemy enemy in state.Enemies)
            {
                builder.AppendLine($"<b>{enemy.Name}</b> {enemy.Rank}" + (enemy.IsDown ? " [倒下]" : ""));
                builder.AppendLine($"  HP {enemy.Hp}/{enemy.MaxHp}  暴击抵抗 {enemy.CritResist}");
                builder.AppendLine($"  {BattleSession.AffinitySummary(enemy)}");
                AppendStatuses(builder, enemy);

                foreach (EnemyIntent intent in enemy.Intents)
                {
                    builder.AppendLine(
                        $"  意图 {intent.Label} {intent.Damage}点{intent.Element.DisplayName()} / {intent.Targeting}");
                }
            }

            builder.AppendLine("<b>本局统计</b>");
            foreach (string key in new[]
                     {
                         BattleStatKeys.CardsPlayed, BattleStatKeys.DamageDealt,
                         BattleStatKeys.CritsDealt, BattleStatKeys.SuperCritsDealt,
                         BattleStatKeys.ChargesSpent, BattleStatKeys.EnemiesDowned,
                     })
            {
                builder.AppendLine($"  {key} = {state.Stats.Team(key)}");
            }

            return builder.ToString();
        }

        private static void AppendStatuses(StringBuilder builder, Combatant combatant)
        {
            IReadOnlyList<StatusEffect> statuses = combatant.Statuses.All;
            if (statuses.Count == 0) return;
            builder.AppendLine("  状态：" + string.Join("、", statuses.Select(s => s.ToString())));
        }

        private void DrawLog()
        {
            // 战报只在事件数变化时重新拼，避免每帧拼一大串字符串
            int count = _view.Session.Engine.Events.Count;
            if (count != _lastEventCount)
            {
                _lastEventCount = count;
                _cachedLog = _view.Session.Log;
                if (_autoScroll) _logScroll.y = float.MaxValue;
            }

            GUILayout.Label("<b>战报</b>");
            _logScroll = GUILayout.BeginScrollView(_logScroll);
            GUILayout.Label(_cachedLog);
            GUILayout.EndScrollView();
        }
    }
}
