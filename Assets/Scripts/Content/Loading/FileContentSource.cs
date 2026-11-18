using System;
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

        public bool Exists => File.Exists(Path.Combine(Root, "pack.json"));

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

        /// <summary>可用它覆盖查找结果（CI、或把内容包放在别处时用）。</summary>
        public const string EnvironmentVariable = "TCGINFU_CONTENT_DIR";

        /// <summary>
        /// 按「显式参数 → 环境变量 → 从工作目录和程序集目录逐级向上找」的顺序定位。
        ///
        /// 之所以要向上找而不是写死：Unity 里跑 EditMode 测试、Unity 外跑 dotnet test、
        /// 以及构建产物里跑，三者的工作目录完全不同。找 <c>Assets/StreamingAssets/Content</c>
        /// 这个锚点比让每个调用方各自拼路径可靠。
        /// </summary>
        public static string Resolve(string explicitRoot = null)
        {
            if (!string.IsNullOrEmpty(explicitRoot)) return explicitRoot;

            string fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (!string.IsNullOrEmpty(fromEnv)) return fromEnv;

            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                string found = SearchUpwards(start);
                if (found != null) return found;
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
                if (File.Exists(Path.Combine(candidate, "pack.json"))) return candidate;
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
