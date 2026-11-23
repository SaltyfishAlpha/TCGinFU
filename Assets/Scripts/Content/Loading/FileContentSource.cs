using System;
using System.Collections.Generic;
using System.IO;

namespace TCGinFU.Content.Loading
{
    /// <summary>
    /// 从磁盘目录读内容包。用的是 BCL 的 <c>System.IO</c>，不是 <c>UnityEngine</c>，
    /// 所以 <c>noEngineReferences</c> 依然成立，<c>dotnet test</c> 也能直接用。
    ///
    /// <see cref="ContentLoader"/> 本身不碰文件系统，只认 <c>Func&lt;string, string&gt;</c>；
    /// 本类就是把一个目录包装成那个委托。
    /// </summary>
    public sealed class FileContentSource
    {
        public FileContentSource(string rootDirectory)
        {
            Root = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
        }

        public string Root { get; }

        public bool Exists => File.Exists(Path.Combine(Root, ContentPaths.PackManifest));

        /// <summary>读不到返回 null —— <see cref="ContentLoader"/> 会把它记成一条「文件不存在」。</summary>
        public string Read(string relativePath)
        {
            string full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(full) ? File.ReadAllText(full) : null;
        }

        public Func<string, string> Reader => Read;
    }

    /// <summary>定位内容包根目录。</summary>
    public static class ContentPaths
    {
        /// <summary>仓库内的内容包相对路径。</summary>
        public const string RepoRelative = "Assets/StreamingAssets/Content";

        /// <summary>内容包的清单文件。一个目录有没有它，决定它是不是一个**完整的包**。</summary>
        public const string PackManifest = "pack.json";

        /// <summary>可用它覆盖查找结果（CI、或把内容包放在别处时用）。</summary>
        public const string EnvironmentVariable = "TCGINFU_CONTENT_DIR";

        /// <summary>
        /// 平台相关的候选目录，优先级从高到低。
        ///
        /// <para>本程序集是 <c>noEngineReferences</c> 的，拿不到
        /// <c>Application.persistentDataPath</c> / <c>streamingAssetsPath</c>，
        /// 所以由表现层在启动时把它们装进来（见 <c>UnityContentRoots</c>）。
        /// 不装也能跑——Unity 外的 <c>dotnet test</c> 靠下面的逐级向上找。</para>
        /// </summary>
        public static Func<IEnumerable<string>> PlatformRoots { get; set; }

        /// <summary>
        /// 按优先级列出所有**存在的**候选目录，高优先级在前。
        ///
        /// <para>和 <see cref="Resolve"/> 的区别：这里不要求目录是个完整的包。
        /// 玩家自制包可以只放几张图、不带 <c>pack.json</c>，
        /// 美术素材的查找链需要看到它（见 <c>CardArtLibrary</c>）。</para>
        /// </summary>
        public static IReadOnlyList<string> ResolveAll(string explicitRoot = null)
        {
            var roots = new List<string>();

            void Add(string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!Directory.Exists(path)) return;
                if (!roots.Contains(path)) roots.Add(path);
            }

            Add(explicitRoot);
            Add(Environment.GetEnvironmentVariable(EnvironmentVariable));

            if (PlatformRoots != null)
            {
                foreach (string path in PlatformRoots()) Add(path);
            }

            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                Add(SearchUpwards(start));
            }

            return roots;
        }

        /// <summary>
        /// 定位**一个完整内容包**的根目录（JSON 加载用）。
        /// 顺序：显式参数 → 环境变量 → 平台目录 → 从工作目录和程序集目录逐级向上找。
        ///
        /// <para>显式参数与环境变量是**独占**的：指到哪就是哪，不带 <c>pack.json</c> 也原样返回。
        /// 否则 CI 把变量指错目录时，会被悄悄地换成仓库里的包，错误要到很久以后才暴露出来。</para>
        ///
        /// <para>之所以还要逐级向上找：Unity 里跑 EditMode 测试、Unity 外跑 dotnet test、
        /// 以及构建产物里跑，三者的工作目录完全不同。构建产物靠的是平台目录那一环
        /// （<c>streamingAssetsPath</c>），向上找 <c>Assets/StreamingAssets/Content</c>
        /// 在构建产物里是找不到的。</para>
        /// </summary>
        public static string Resolve(string explicitRoot = null)
        {
            if (!string.IsNullOrEmpty(explicitRoot)) return explicitRoot;

            string fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

            foreach (string root in ResolveAll())
            {
                if (File.Exists(Path.Combine(root, PackManifest))) return root;
            }

            return null;
        }

        private static string SearchUpwards(string start)
        {
            if (string.IsNullOrEmpty(start)) return null;

            var dir = new DirectoryInfo(start);
            while (dir != null)
            {
                string candidate = Path.Combine(
                    dir.FullName, RepoRelative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(Path.Combine(candidate, PackManifest))) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        /// <summary>
        /// 加载默认内容包（即迁移过来的「命刻之战」）。找不到目录时返回带错误的 report，不抛异常。
        /// </summary>
        public static ContentDatabase LoadDefault(out LoadReport report, string explicitRoot = null)
        {
            report = new LoadReport();
            var db = new ContentDatabase();

            string root = Resolve(explicitRoot);
            if (root == null)
            {
                report.Errors.Add(
                    $"找不到内容包目录（{RepoRelative}）。可设环境变量 {EnvironmentVariable} 指定。");
                return db;
            }

            var source = new FileContentSource(root);
            ContentLoader.LoadPack(db, source.Reader, report);
            return db;
        }
    }
}
