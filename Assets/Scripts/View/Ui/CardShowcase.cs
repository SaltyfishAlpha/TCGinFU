using System.Collections.Generic;
using TCGinFU.Core;
using TCGinFU.Presentation;
using UnityEngine;
using UnityEngine.UI;

namespace TCGinFU.View
{
    /// <summary>
    /// 出牌演出：谁打出一张牌，就在画面左侧把那张牌的卡面亮出来一会儿。
    ///
    /// <para>实体桌游里这一步是自然发生的——牌被翻到桌子中央，所有人都看得见。
    /// 电子版如果只在战报里多一行字，玩家根本来不及意识到「首领刚刚打了什么」，
    /// 等反应过来伤害已经结算完了。首领行动尤其需要这一下。</para>
    ///
    /// <para>卡面来自 <see cref="CardPreviewStage"/>——和详情面板左侧是同一套机制，
    /// 但**各用一座棚子**：共用的话，打牌时正好开着详情面板就会互相抢那张 RenderTexture。</para>
    ///
    /// <para>特效还没有。这里只负责「亮出来、停一下、收回去」，
    /// 等特效系统到位时在 <see cref="Play"/> 里挂上就行。</para>
    /// </summary>
    public sealed class CardShowcase : MonoBehaviour
    {
        private const float CardHeight = 400f;
        private const float FadeIn = 0.12f;
        private const float Hold = 1.05f;
        private const float FadeOut = 0.25f;

        /// <summary>
        /// 队列上限。一次敌人阶段可能连续结算好几条意图，
        /// 排太长会让演出拖到玩家早就该操作的时候——宁可丢掉中间几张。
        /// </summary>
        private const int MaxQueued = 4;

        private readonly Queue<(CardDefinition Card, string Actor)> _queue = new();

        private CardPreviewStage _stage;
        private CanvasGroup _group;
        private RawImage _image;
        private Text _caption;
        private float _elapsed;
        private bool _playing;

        public static CardShowcase Create(Transform parent, CardPreviewStage stage)
        {
            var go = new GameObject("CardShowcase", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(parent, false);

            var showcase = go.AddComponent<CardShowcase>();
            showcase._stage = stage;
            showcase._group = go.GetComponent<CanvasGroup>();
            // 整块演出都不吃点击：它是在你出牌之后弹出来的，挡住操作就成了负担
            showcase._group.blocksRaycasts = false;
            showcase._group.interactable = false;
            showcase._group.alpha = 0f;

            float cardWidth = CardHeight / BattleLayoutAsset.PokerAspect;

            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(cardWidth, CardHeight + 28f);
            rect.anchoredPosition = new Vector2(36f, 40f);

            var imageGo = new GameObject("Card", typeof(RectTransform), typeof(RawImage));
            imageGo.transform.SetParent(rect, false);

            showcase._image = imageGo.GetComponent<RawImage>();
            showcase._image.texture = stage.Output;
            showcase._image.raycastTarget = false;

            RectTransform imageRect = showcase._image.rectTransform;
            imageRect.anchorMin = new Vector2(0f, 0f);
            imageRect.anchorMax = new Vector2(1f, 1f);
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = new Vector2(0f, -28f);
            UiBuilder.CreateBorder(imageRect, new Color(1f, 1f, 1f, 0.3f));

            showcase._caption = UiBuilder.CreateText(
                "Caption", rect, "", 15, TextAnchor.LowerCenter, new Color(1f, 0.92f, 0.72f));
            showcase._caption.rectTransform.anchorMin = new Vector2(0f, 1f);
            showcase._caption.rectTransform.anchorMax = new Vector2(1f, 1f);
            showcase._caption.rectTransform.pivot = new Vector2(0.5f, 1f);
            showcase._caption.rectTransform.sizeDelta = new Vector2(0f, 26f);
            showcase._caption.rectTransform.anchoredPosition = Vector2.zero;
            showcase._caption.raycastTarget = false;

            go.SetActive(false);
            return showcase;
        }

        /// <summary>
        /// 排队亮一张牌。同一批结算里打出多张时按顺序一张张播。
        /// </summary>
        public void Enqueue(CardDefinition card, string actorName)
        {
            if (card == null) return;

            // 满了就丢最早的那张：玩家更关心刚刚发生的事，不是三秒前的
            if (_queue.Count >= MaxQueued) _queue.Dequeue();
            _queue.Enqueue((card, actorName));

            gameObject.SetActive(true);
        }

        private void Update()
        {
            if (!_playing)
            {
                if (_queue.Count == 0) { Stop(); return; }

                (CardDefinition card, string actor) = _queue.Dequeue();
                Play(card, actor);
                return;
            }

            _elapsed += Time.deltaTime;
            _group.alpha = AlphaAt(_elapsed);

            if (_elapsed >= FadeIn + Hold + FadeOut) _playing = false;
        }

        private void Play(CardDefinition card, string actorName)
        {
            _stage.ShowCard(card, costText: BattleSession.CostText(card));
            _caption.text = string.IsNullOrEmpty(actorName) ? "打出" : $"{actorName} 打出";

            _elapsed = 0f;
            _playing = true;
            _group.alpha = 0f;
        }

        private static float AlphaAt(float t)
        {
            if (t < FadeIn) return t / FadeIn;
            if (t < FadeIn + Hold) return 1f;
            return Mathf.Clamp01(1f - (t - FadeIn - Hold) / FadeOut);
        }

        /// <summary>播完了就收摊，顺带关掉摄影棚，预览相机不再空转。</summary>
        private void Stop()
        {
            _group.alpha = 0f;
            _stage.Hide();
            gameObject.SetActive(false);
        }

        /// <summary>重开一局时把没播完的清掉，别让上一局的牌飘到新一局里。</summary>
        public void Clear()
        {
            _queue.Clear();
            _playing = false;
            Stop();
        }
    }
}
