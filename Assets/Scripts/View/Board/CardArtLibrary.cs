using System.Collections.Generic;
using System.IO;
using TCGinFU.Content.Loading;
using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>
    /// 把内容包里的美术标识（<c>CardDefinition.ArtId</c> 等）解析成实际贴图。
    ///
    /// <para><b>为什么要有这一层：</b>Core 与 Content 都是 <c>noEngineReferences</c> 的，
    /// 碰不到 <see cref="Texture2D"/>。所以数据层只存一个字符串 id，
    /// 由表现层决定它从哪来。</para>
    ///
    /// <para><b>查找顺序：内容包目录 → 包体内的 Resources。</b>
    /// 卡面和卡牌效果、卡面文字一样，是**内容**，玩家应该能自己换。
    /// 所以正式的卡图走内容包的 <c>art/</c> 目录（见 <see cref="PackArtFolder"/>），
    /// 和描述它的 JSON 放在一起、跟着包走。
    /// <see cref="ContentPaths.ResolveAll"/> 把玩家包排在官方包前面，
    /// 于是玩家不改官方文件也能换掉官方卡的图。</para>
    ///
    /// <para>包体内的 <c>Resources/Art/</c> 只留**兜底素材**：
    /// 牌背和按类型的几张占位图。它们必须在任何内容包都没装的情况下也存在，
    /// 所以不能跟着包走。</para>
    ///
    /// <para><b>加载的是 <see cref="Texture2D"/> 而不是 <see cref="Sprite"/>：</b>
    /// 图片按 Unity 默认的 <c>Default</c> 类型导入时根本没有 Sprite 子资产，
    /// <c>Resources.Load&lt;Sprite&gt;</c> 会一声不响地返回 null。
    /// 卡面本来就是贴在网格上的，要的就是纹理，不必强迫美术改导入设置。</para>
    ///
    /// <para><b>解析不到返回 null</b>，调用方退回占位色块。
    /// 美术没到位是常态，不该让缺一张图就报错或留个粉色方块。</para>
    /// </summary>
    public static class CardArtLibrary
    {
        /// <summary>包体内兜底素材在 Resources 下的根目录。</summary>
        public const string ResourceRoot = "Art/";

        /// <summary>内容包里放美术素材的子目录，和 <c>cards/</c>、<c>scripts/</c> 平级。</summary>
        public const string PackArtFolder = "art";

        // 内容包里的图是裸文件，没有 Unity 的导入设置，所以扩展名得自己试。
        // 顺序即优先级：同名时 png 赢，它能带透明通道
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg" };

        // 缺图是常态，所以**连「没找到」也要缓存**——
        // 否则每帧 Refresh 都会对同一个不存在的路径重新发起一次查找
        private static readonly Dictionary<string, Texture2D> _cache = new();

        // Resources 里的贴图归 Unity 管，包里读出来的不归——Clear() 要能分清哪些该自己销毁
        private static readonly HashSet<Texture2D> _fromPack = new();

        /// <summary>
        /// 内容包目录，优先级从高到低。由表现层在开局时从
        /// <see cref="ContentPaths.ResolveAll"/> 装进来；没装就只有包体内的兜底图可用。
        /// </summary>
        public static IReadOnlyList<string> SearchRoots { get; set; }

        /// <summary>
        /// 按类型的兜底图（内容包 pack.json 的 <c>artFallback</c>）。
        /// 单卡还没有专属卡图时用它——一套 6 张的占位图就能让整副牌都有画面，
        /// 不必等 49 张卡图齐了才看得出效果。
        /// </summary>
        public static IReadOnlyDictionary<string, string> Fallbacks { get; set; }

        public static Texture2D Resolve(string artId)
        {
            if (string.IsNullOrEmpty(artId)) return null;

            if (_cache.TryGetValue(artId, out Texture2D cached)) return cached;

            Texture2D texture = LoadFromPacks(artId) ?? Resources.Load<Texture2D>(ResourceRoot + artId);
            _cache[artId] = texture;
            return texture;
        }

        /// <summary>
        /// 从内容包目录里读图。读的是裸文件，所以不走 Unity 的导入管线——
        /// 没有 mipmap、没有平台压缩。卡图这个量级（几十张、只贴在面片上）不值得为此建资源管线，
        /// 真要优化是以后换 Addressables 的事。
        /// </summary>
        private static Texture2D LoadFromPacks(string artId)
        {
            if (SearchRoots == null || !IsSafeRelativePath(artId)) return null;

            string relative = artId.Replace('/', Path.DirectorySeparatorChar);

            foreach (string root in SearchRoots)
            {
                foreach (string extension in Extensions)
                {
                    string full = Path.Combine(root, PackArtFolder, relative + extension);
                    if (!File.Exists(full)) continue;

                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                    {
                        name = artId,
                        // 默认的 Repeat 会让卡面边缘透出对侧的像素，贴在面片上一眼就看得见
                        wrapMode = TextureWrapMode.Clamp,
                    };

                    if (texture.LoadImage(File.ReadAllBytes(full)))
                    {
                        _fromPack.Add(texture);
                        return texture;
                    }

                    // 文件在但解不开（截断、改了扩展名的假 png……）——
                    // 当成这张图不存在，继续往兜底链走，别让一张坏图把整局卡住
                    Debug.LogWarning($"内容包里的图读不出来，已跳过：{full}");
                    Object.Destroy(texture);
                }
            }

            return null;
        }

        /// <summary>
        /// 挡住 <c>../</c> 和绝对路径。美术标识来自内容包的 JSON，
        /// 而内容包是玩家可以互相分发的东西——不该让一个包靠写路径读到内容目录外面去。
        /// </summary>
        private static bool IsSafeRelativePath(string artId)
            => !artId.Contains("..") && !Path.IsPathRooted(artId);

        /// <summary>
        /// 兜底链里的最后一环。走到这儿还没图，说明内容包自己没配全。
        /// </summary>
        public const string LastResortKey = "back";

        /// <summary>
        /// 解析卡图：专属卡图 → 按类型兜底 → 最后一环。
        ///
        /// <para><b>刻意不返回 null。</b>之前「解析不到就退回纯色块」看着稳妥，
        /// 实际效果是界面上零星几张纯色卡混在有图的卡里，看起来像渲染坏了而不是「图还没画」。
        /// 一路兜到底，缺图就统一显示成牌背那张——至少是一致的。</para>
        /// </summary>
        public static Texture2D ResolveWithFallback(string artId, string fallbackKey)
        {
            Texture2D texture = Resolve(artId);
            if (texture != null) return texture;

            texture = ResolveFallback(fallbackKey);
            if (texture != null) return texture;

            return ResolveFallback(LastResortKey);
        }

        private static Texture2D ResolveFallback(string fallbackKey)
        {
            if (Fallbacks == null || fallbackKey == null) return null;

            return Fallbacks.TryGetValue(fallbackKey, out string artId) ? Resolve(artId) : null;
        }

        // Image/RawImage 之类的 uGUI 组件要的是 Sprite，棋盘上的网格要的是 Texture。
        // 同一张图两种包装各缓存一份，省得每次 Refresh 都 Sprite.Create 一个新的
        private static readonly Dictionary<Texture2D, Sprite> _sprites = new();

        /// <summary>同 <see cref="ResolveWithFallback"/>，但包成 uGUI 用的 <see cref="Sprite"/>。</summary>
        public static Sprite ResolveSprite(string artId, string fallbackKey)
        {
            Texture2D texture = ResolveWithFallback(artId, fallbackKey);
            if (texture == null) return null;

            if (_sprites.TryGetValue(texture, out Sprite cached) && cached != null) return cached;

            var sprite = Sprite.Create(
                texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f));

            _sprites[texture] = sprite;
            return sprite;
        }

        /// <summary>换内容包时清缓存，别让上一个包的图留在下一个包里。</summary>
        public static void Clear()
        {
            // 包里读出来的贴图是 new 出来的，不归 Resources 管，丢掉引用不会释放显存
            foreach (Texture2D texture in _cache.Values)
            {
                if (texture != null && _fromPack.Contains(texture)) Object.Destroy(texture);
            }

            _fromPack.Clear();
            _cache.Clear();
            _sprites.Clear();
        }

        /// <summary>这张图在不在。供内容校验工具列出「卡面声明了但美术没给」的清单。</summary>
        public static bool Has(string artId) => Resolve(artId) != null;
    }
}
