using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>
    /// 把「位置被重新赋值」变成一段滑行。
    ///
    /// <para>棋盘上的角色卡是按前后排分到不同 <see cref="ZoneView"/> 下的，走位时会被
    /// 重新挂到另一个 Zone、直接赋一个新的 localPosition——玩家看到的是瞬移，
    /// 根本意识不到自己动过。这里在赋值的瞬间记下**世界坐标**，再把它换算回新父节点下的
    /// 局部坐标作为起点插值，所以跨父节点的移动也能连贯地滑过去。</para>
    ///
    /// <para>首次摆放不动画：刚创建的视图从父节点原点滑到位毫无意义，只会让开局一片乱飞。</para>
    /// </summary>
    public sealed class SlideTween : MonoBehaviour
    {
        /// <summary>滑行时长。够看清「他从那边挪过来了」，又不至于让人等。</summary>
        public const float Duration = 0.22f;

        private const float MinDistance = 0.0001f;

        private Vector3 _from;
        private Vector3 _to;
        private float _elapsed;
        private bool _running;
        private bool _placedOnce;

        /// <summary>
        /// 摆到指定的局部姿态。<paramref name="animate"/> 为 false 时直接落位。
        /// </summary>
        public static void Apply(Transform item, Vector3 localPosition, Quaternion localRotation, bool animate)
        {
            if (!animate)
            {
                item.localPosition = localPosition;
                item.localRotation = localRotation;
                item.localScale = Vector3.one;
                return;
            }

            SlideTween tween = item.GetComponent<SlideTween>();
            if (tween == null) tween = item.gameObject.AddComponent<SlideTween>();
            tween.MoveTo(localPosition, localRotation);
        }

        private void MoveTo(Vector3 localPosition, Quaternion localRotation)
        {
            // 必须在改 localPosition **之前**取世界坐标：此刻父节点可能已经换过了，
            // 但 transform 还停在旧位置上，这就是玩家眼里的「出发点」
            Vector3 worldBefore = transform.position;

            transform.localRotation = localRotation;
            transform.localScale = Vector3.one;
            _to = localPosition;

            if (!_placedOnce)
            {
                _placedOnce = true;
                transform.localPosition = localPosition;
                _running = false;
                return;
            }

            Transform parent = transform.parent;
            Vector3 fromLocal = parent != null ? parent.InverseTransformPoint(worldBefore) : worldBefore;

            if ((fromLocal - localPosition).sqrMagnitude < MinDistance)
            {
                transform.localPosition = localPosition;
                _running = false;
                return;
            }

            _from = fromLocal;
            _elapsed = 0f;
            _running = true;
            transform.localPosition = fromLocal;
        }

        private void Update()
        {
            if (!_running) return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / Duration);

            // 缓出：起步快、落位稳，看着像「挪过去站定」而不是匀速平移
            transform.localPosition = Vector3.Lerp(_from, _to, 1f - (1f - t) * (1f - t));

            if (t >= 1f) _running = false;
        }
    }
}
