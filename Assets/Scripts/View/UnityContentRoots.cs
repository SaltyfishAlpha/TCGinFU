using System.Collections.Generic;
using System.IO;
using TCGinFU.Content.Loading;
using UnityEngine;

namespace TCGinFU.View
{
    /// <summary>
    /// 把 Unity 的平台目录接进 <see cref="ContentPaths"/>。
    ///
    /// <para>Content 程序集是 <c>noEngineReferences</c> 的（为了能脱离 Unity 跑 dotnet test），
    /// 碰不到 <c>Application</c>，所以这两个路径只能由表现层装进去。</para>
    ///
    /// <para><b>为什么构建产物以前读不到内容包：</b><c>ContentPaths</c> 原本只会从工作目录
    /// 逐级向上找 <c>Assets/StreamingAssets/Content</c>——这个锚点在构建产物里根本不存在，
    /// 打出来的包只能靠环境变量才找得到内容。装上 <c>streamingAssetsPath</c> 这一环之后
    /// 编辑器和构建产物走的是同一条路。</para>
    ///
    /// <para>用 <see cref="RuntimeInitializeOnLoadMethod"/> 而不是在 <c>BattleView.Start</c> 里装：
    /// 内容包的加载发生在 <c>BattleSession.Restart()</c> 里，比任何一个 <c>Start</c> 都早不了多少，
    /// 但将来多一个入口（存档、卡组编辑器）就会忘记装。挂在场景加载之前，谁先跑都一样。</para>
    /// </summary>
    internal static class UnityContentRoots
    {
        /// <summary>两个平台目录下都用这个子目录名装内容。</summary>
        public const string ContentFolder = "Content";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => ContentPaths.PlatformRoots = Roots;

        private static IEnumerable<string> Roots()
        {
            // 玩家包排在官方包前面：它要能**覆盖**官方素材，而不是只能补充。
            // 这里只影响美术查找链（ContentPaths.ResolveAll）——
            // JSON 走 ContentPaths.Resolve，它只认带 pack.json 的完整包，
            // 所以一个只放了几张图的玩家目录不会被误当成整个内容包加载
            //
            // 两条路径都按「磁盘上的普通目录」用（File.Exists / File.ReadAllBytes）。
            // 这对桌面平台成立；Android 的 streamingAssetsPath 在 apk 里，到时候要改成
            // UnityWebRequest 异步读，所以那一天来了得从这里起手
            yield return Path.Combine(Application.persistentDataPath, ContentFolder);
            yield return Path.Combine(Application.streamingAssetsPath, ContentFolder);
        }
    }
}
