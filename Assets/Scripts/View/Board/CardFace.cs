using UnityEngine;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>
    /// 贴在 3D 卡牌正面的文字层。
    /// <para>
    /// 用世界空间 Canvas 而不是 TextMeshPro：TMP 要一份内嵌中文字形的字体资产，
    /// 做不出来也没法在运行时生成；uGUI 的 Text 配动态系统字体可以直接显示中文。
    /// 它不参与点击——拾取走卡牌本身的 BoxCollider + PhysicsRaycaster。
    /// </para>
    /// </summary>
    public sealed class CardFace : MonoBehaviour
    {
        public RectTransform Root { get; private set; }

        /// <summary>
        /// 在卡牌正面建一块文字层。<paramref name="sizePx"/> 是卡牌的设计像素尺寸。
        /// </summary>
        public static CardFace Create(Transform parent, Vector2 sizePx, float zOffsetPx)
        {
            var go = new GameObject("Face", typeof(RectTransform), typeof(Canvas));
            go.transform.SetParent(parent, false);

            var rect = (RectTransform)go.transform;
            rect.sizeDelta = sizePx;
            rect.localPosition = new Vector3(0f, 0f, zOffsetPx);
            rect.localRotation = Quaternion.identity;
            rect.localScale = Vector3.one;

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            // 盖在卡身网格之上；网格是不透明的，UI 默认走透明队列，天然画在后面
            canvas.sortingOrder = 1;

            var face = go.AddComponent<CardFace>();
            face.Root = rect;
            return face;
        }

        public Text AddText(
            string name,
            string content,
            int fontSize,
            TextAnchor anchor,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color? color = null)
        {
            Text text = UiBuilder.CreateText(name, Root, content, fontSize, anchor, color);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = new Vector2(4f, 2f);
            rect.offsetMax = new Vector2(-4f, -2f);

            // 描边：卡面贴上插画之后，底色不再可控——浅色卡图上的白字会直接糊掉。
            // 比「在文字后面铺一层暗色条」好的地方是它不挡画面，而且深浅两种卡图都管用。
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.4f, -1.4f);

            return text;
        }

        public Image AddImage(string name, Color color, Vector2 anchorMin, Vector2 anchorMax)
        {
            Image image = UiBuilder.CreateImage(name, Root, color, raycast: false);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return image;
        }

        public void SetVisible(bool visible) => Root.gameObject.SetActive(visible);
    }
}
