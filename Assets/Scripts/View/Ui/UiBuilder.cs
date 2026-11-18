using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>
    /// 运行时拼 uGUI 的小工具。界面整个由代码建出来，不依赖任何预制体——
    /// 布局要改时改 <see cref="BattleLayoutAsset"/> 就行，不会出现
    /// 「预制体引用丢了」「场景里的值和代码里的不一致」这类问题。
    /// </summary>
    public static class UiBuilder
    {
        private static Font _font;

        /// <summary>
        /// 找一个带中文字形的字体。uGUI 的内置字体没有 CJK，直接用会显示成方块，
        /// 所以优先从系统字体里挑，实在找不到再退回内置字体。
        /// </summary>
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;

                string[] installed = UnityEngine.Font.GetOSInstalledFontNames() ?? new string[0];
                string[] preferred =
                {
                    "Microsoft YaHei UI", "Microsoft YaHei", "微软雅黑",
                    "SimHei", "黑体", "SimSun", "宋体",
                    "Noto Sans CJK SC", "Source Han Sans SC",
                    "PingFang SC", "Hiragino Sans GB", "Arial Unicode MS",
                };

                string match = preferred.FirstOrDefault(name => installed.Contains(name));
                if (match != null) _font = UnityEngine.Font.CreateDynamicFontFromOSFont(match, 16);

                if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");

                return _font;
            }
        }

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>铺满父物体。</summary>
        public static RectTransform Stretch(this RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        public static Image CreateImage(string name, Transform parent, Color color, bool raycast = true)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text CreateText(
            string name,
            Transform parent,
            string content,
            int size = 16,
            TextAnchor anchor = TextAnchor.MiddleCenter,
            Color? color = null)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.text = content;
            text.alignment = anchor;
            text.color = color ?? Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = true;
            return text;
        }

        public static Button CreateButton(string label, Transform parent, float width, System.Action onClick)
        {
            Image background = CreateImage($"Button_{label}", parent, new Color(0.25f, 0.27f, 0.33f, 1f));
            var layout = background.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = width;
            layout.preferredHeight = 30f;

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;

            CreateText("Label", background.transform, label, 15).rectTransform.Stretch();
            if (onClick != null) button.onClick.AddListener(() => onClick());

            return button;
        }

        /// <summary>给一个 RectTransform 画一圈边框（用四条细 Image，避免依赖九宫格贴图）。</summary>
        public static void CreateBorder(RectTransform parent, Color color, float thickness = 2f)
        {
            void Edge(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta)
            {
                Image edge = CreateImage(name, parent, color, raycast: false);
                edge.rectTransform.anchorMin = anchorMin;
                edge.rectTransform.anchorMax = anchorMax;
                edge.rectTransform.offsetMin = Vector2.zero;
                edge.rectTransform.offsetMax = Vector2.zero;
                edge.rectTransform.sizeDelta = sizeDelta;
            }

            Edge("上", new Vector2(0f, 1f), Vector2.one, new Vector2(0f, thickness));
            Edge("下", Vector2.zero, new Vector2(1f, 0f), new Vector2(0f, thickness));
            Edge("左", Vector2.zero, new Vector2(0f, 1f), new Vector2(thickness, 0f));
            Edge("右", new Vector2(1f, 0f), Vector2.one, new Vector2(thickness, 0f));
        }

        /// <summary>按阵营给角色面板配色。</summary>
        public static Color TeamColor(bool isEnemy, bool down, bool crisis)
        {
            if (down) return new Color(0.18f, 0.18f, 0.2f, 0.9f);
            if (crisis) return new Color(0.55f, 0.2f, 0.2f, 0.9f);
            return isEnemy
                ? new Color(0.38f, 0.19f, 0.2f, 0.9f)
                : new Color(0.17f, 0.28f, 0.42f, 0.9f);
        }
    }
}
