using UnityEngine;
using UnityEngine.EventSystems;

namespace TCGinFU.View
{
    /// <summary>
    /// 左键长按识别。卡牌和角色卡共用同一套手感——
    /// 两边各写一遍迟早会调出两个不同的阈值，玩家会觉得「角色卡比较难按出来」。
    ///
    /// <para>长按和拖拽抢的是同一个手势，所以这里必须能被拖拽**取消**：
    /// 一旦开始拖，长按就作废，否则拖一张牌拖久一点就会弹出详情挡住视线。</para>
    /// </summary>
    public sealed class LongPress
    {
        /// <summary>按住多久算长按。0.35s 是「明显比点击久、又不至于等得不耐烦」的区间。</summary>
        public const float Threshold = 0.35f;

        private bool _pressing;
        private float _pressedAt;
        private Vector2 _origin;
        private bool _fired;

        /// <summary>长按达成时触发。参数是按下时的屏幕坐标。</summary>
        public System.Action<Vector2> Triggered { get; set; }

        /// <summary>这一次按压是否已经弹过详情——用来吞掉随后的 Click，免得又触发一次选中。</summary>
        public bool Consumed => _fired;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left) return;

            _pressing = true;
            _fired = false;
            _pressedAt = Time.unscaledTime;
            _origin = eventData.position;
        }

        public void OnPointerUp() => _pressing = false;

        /// <summary>开始拖拽时调用：拖拽优先，长按作废。</summary>
        public void Cancel()
        {
            _pressing = false;
            _fired = false;
        }

        /// <summary>
        /// 每帧调一次。到时就触发，并且只触发一次。
        ///
        /// <para>这里刻意**不**自己判断指针有没有移动：手势一旦变成拖拽，
        /// 拖拽方会调 <see cref="Cancel"/>。让两套逻辑各判一次迟早会打架——
        /// 出现「拖出去了还是弹了详情」或者「轻微抖一下就按不出详情」。</para>
        /// </summary>
        public void Tick()
        {
            if (!_pressing || _fired) return;
            if (Time.unscaledTime - _pressedAt < Threshold) return;

            _fired = true;
            _pressing = false;
            Triggered?.Invoke(_origin);
        }
    }
}
