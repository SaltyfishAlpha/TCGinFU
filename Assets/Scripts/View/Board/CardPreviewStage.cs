using TCGinFU.Core;
using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>
    /// 一座离屏「摄影棚」：把一张真正的 3D 卡牌拍进 <see cref="RenderTexture"/>，
    /// 让 uGUI 用 <c>RawImage</c> 显示。详情面板左侧的卡图、以及出牌演出都用它。
    ///
    /// <para><b>为什么不直接把 3D 卡摆在界面旁边：</b>
    /// <c>HudCanvas</c> 是 <c>ScreenSpaceOverlay</c>，永远盖在所有相机的渲染结果之上，
    /// 而详情面板还带一层 alpha 0.62 的全屏遮幕。3D 卡无论摆在哪都会被那层遮幕吃掉。
    /// 走 RenderTexture 之后层级问题就不存在了——它只是一张图。</para>
    ///
    /// <para><b>为什么不另写一个 2D 版卡面：</b>卡面上的名字、费用、类型是会变的
    /// （「先声夺人」卡面印 5 MP，第 1 轮打出是 0 MP），版式也还在动。
    /// 这里直接复用棋盘上那套 <see cref="CardView"/> / <see cref="CombatantView"/>，
    /// 卡面以后怎么改，详情页里跟着一起改，不会有两套版式对不上的问题。</para>
    /// </summary>
    public sealed class CardPreviewStage : MonoBehaviour
    {
        /// <summary>
        /// 摄影棚专用层（<c>ProjectSettings/TagManager.asset</c> 里的 31 号层 <c>CardPreview</c>）。
        /// 预览相机只拍这一层，桌面相机把这一层剔掉，两边互相看不见。
        /// </summary>
        public const int PreviewLayer = 31;

        /// <summary>棚里那张卡的尺寸（设计像素）。比棋盘上的卡大，因为要拍成一张大图。</summary>
        public static readonly Vector2 CardSizePx =
            new(380f, 380f * BattleLayoutAsset.PokerAspect);

        /// <summary>取景余量。1 = 卡刚好顶满画面，留一点边看着才不挤。</summary>
        private const float Margin = 1.06f;

        // 棚子搭在天上，离棋盘足够远。剔除层已经让两台相机互不干扰，
        // 但桌面的 PhysicsRaycaster 是打真实射线的，离远点省得哪天手滑点到棚里的卡
        private static readonly Vector3 StageOrigin = new(0f, 100000f, 0f);

        private Camera _camera;
        private CardView _card;
        private CombatantView _combatant;

        /// <summary>拍出来的图。挂给 <c>RawImage.texture</c>。</summary>
        public RenderTexture Output { get; private set; }

        public static CardPreviewStage Create(Transform parent, int pixelHeight = 720)
        {
            var go = new GameObject("CardPreviewStage");
            go.transform.SetParent(parent, false);
            // 挂在 BattleView 底下只是为了跟着它一起销毁；棚子自己的朝向和缩放不跟父节点走，
            // 否则哪天 BattleView 被摆歪了，拍出来的卡也跟着歪
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.transform.position = StageOrigin;

            var stage = go.AddComponent<CardPreviewStage>();

            int pixelWidth = Mathf.RoundToInt(pixelHeight / BattleLayoutAsset.PokerAspect);
            stage.Output = new RenderTexture(pixelWidth, pixelHeight, depth: 24, RenderTextureFormat.ARGB32)
            {
                name = "CardPreview",
                antiAliasing = 2,
            };

            stage._camera = stage.BuildCamera(go.transform);

            // 棚里常驻一张卡和一张角色卡，每次换内容只是重新 Bind，不反复建销毁
            stage._card = CardView.Create(go.transform, context: null);
            stage._combatant = CombatantView.Create(go.transform, CardSizePx, withChargeStack: false);

            go.SetActive(false);
            return stage;
        }

        private Camera BuildCamera(Transform parent)
        {
            var cameraGo = new GameObject("PreviewCamera", typeof(Camera));
            cameraGo.transform.SetParent(parent, false);

            // 和桌面相机同一个朝向约定：待在 −Z 侧朝 +Z 看。
            // 放到 +Z 侧的话面片全变成背面，拍出来是一片漆黑（见 CardMeshFactory.TowardCamera）
            cameraGo.transform.localPosition = new Vector3(0f, 0f, -CardSizePx.y);
            cameraGo.transform.localRotation = Quaternion.identity;

            var camera = cameraGo.GetComponent<Camera>();
            camera.orthographic = true;
            // 棚子没缩放，所以世界单位就是设计像素
            camera.orthographicSize = CardSizePx.y * 0.5f * Margin;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = CardSizePx.y * 2f;
            camera.cullingMask = 1 << PreviewLayer;
            camera.targetTexture = Output;
            // 排在桌面相机前面渲染，否则 RawImage 这一帧拿到的还是上一张卡
            camera.depth = -10f;
            camera.allowHDR = false;
            camera.allowMSAA = true;

            // 刻意清成不透明的实色，而不是靠 alpha 透出底下的面板：
            // 透明通道能不能穿过 RenderTexture 取决于渲染管线的配置，
            // 而「左边一块和面板同色的卡图区」本来就是要的效果，不值得为此赌管线行为
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.11f, 0.14f, 1f);

            return camera;
        }

        // ──────────────────────────── 换内容 ────────────────────────────

        /// <param name="faceDown">
        /// 背面朝上的牌（充能区）在详情里也只给卡背。桌面上看不到的信息，
        /// 点开详情同样不该看到。
        /// </param>
        public void ShowCard(CardDefinition definition, bool faceDown = false, string costText = null)
        {
            if (definition == null) { Hide(); return; }

            gameObject.SetActive(true);
            _combatant.Root.gameObject.SetActive(false);
            _card.Root.gameObject.SetActive(true);

            _card.BindDefinition(definition, faceDown, CardSizePx, costText);
            SetLayerRecursively(_card.Root, PreviewLayer);
        }

        public void ShowCombatant(Combatant combatant, bool isEnemy)
        {
            if (combatant == null) { Hide(); return; }

            gameObject.SetActive(true);
            _card.Root.gameObject.SetActive(false);
            _combatant.Root.gameObject.SetActive(true);

            _combatant.Bind(combatant, isEnemy, selected: false);
            SetLayerRecursively(_combatant.Root, PreviewLayer);
        }

        /// <summary>收起棚子。整个节点关掉，预览相机也就不再每帧渲染。</summary>
        public void Hide() => gameObject.SetActive(false);

        /// <summary>
        /// 整棵子树换层。<see cref="CardView.BindDefinition"/> 不会新建对象，
        /// 所以每次换内容其实只是在重复确认；但这点开销换的是「以后往卡面上加东西
        /// 不用记得同步改这里」，值。
        /// </summary>
        private static void SetLayerRecursively(Transform root, int layer)
        {
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++) SetLayerRecursively(root.GetChild(i), layer);
        }

        private void OnDestroy()
        {
            if (Output == null) return;

            // RenderTexture 是非托管资源，丢引用不会释放显存
            Output.Release();
            Destroy(Output);
        }
    }
}
