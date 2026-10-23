using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BlockLauncher.Core;
using BlockLauncher.Game;

namespace BlockLauncher.Content
{
    /// <summary>
    /// 无清单的「绿色整合包」：直接把压缩包摊开成一份可运行的 .minecraft。
    ///
    /// 国内不少整合包（HMCL / 整合包打包机导出的）没有 manifest.json，
    /// 解开就是 versions、mods、config 一整套现成的东西。这种包不需要下载任何东西，
    /// 解压到独立目录、认出里面的版本号就能直接启动。
    ///
    /// 判定条件很硬：必须在压缩包里找到 versions/&lt;版本&gt;/&lt;版本&gt;.json，
    /// 否则不算绿色整合包，交回给标准导入流程报错。
    /// </summary>
    public static class GreenPack
    {
        static readonly Regex VersionJson =
            new Regex(@"(?:^|/)versions/([^/]+)/[^/]+\.json$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static string Normalize(string name) => (name ?? "").Replace('\\', '/');

        /// <summary>
        /// 找出 versions 所在的相对前缀（"" 表示压缩包根目录就是这个游戏目录）。
        /// 取最浅的一层，这样「外面套了一层文件夹」「里面多包了一层 .minecraft」都能自动剥掉。
        /// </summary>
        static string DetectPrefix(ZipArchive zip)
        {
            string best = null;
            foreach (var e in zip.Entries)
            {
                var name = Normalize(e.FullName);
                var m = VersionJson.Match(name);
                if (!m.Success) continue;
                var prefix = name.Substring(0, m.Index);
                if (best == null || prefix.Length < best.Length) best = prefix;
            }
            return best;
        }

        /// <summary>只读判断：这个 zip 摊开是不是一份现成的游戏目录</summary>
        public static bool LooksLikeGameDir(string zipPath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                    return DetectPrefix(zip) != null;
            }
            catch { return false; }
        }

        /// <summary>解压成独立游戏目录，返回识别到的版本 id</summary>
        public static Task<string> InstallAsync(GameRepo repo, string zipPath, LauncherTask task)
        {
            var ct = task?.Token ?? default(System.Threading.CancellationToken);
            if (repo == null) throw new ArgumentNullException(nameof(repo));

            return Task.Run(() =>
            {
                task?.Report(0, "读取压缩包…");
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    var prefix = DetectPrefix(zip)
                        ?? throw new ContentException("无法识别的整合包：找不到 modrinth.index.json、CurseForge 的 manifest.json，也没有现成的 versions 目录");

                    var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
                    if (entries.Count == 0) throw new ContentException("压缩包是空的");

                    int done = 0;
                    foreach (var e in entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        var name = Normalize(e.FullName);
                        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        var rel = name.Substring(prefix.Length);
                        if (rel.Length == 0) continue;

                        var target = PathGuard.Resolve(repo.Root, rel);
                        if (target == null) { Log.Warn("跳过文件名不合法的条目：" + name); continue; }

                        // 路径穿越判断通过后再建目录，避免在奇怪的位置留垃圾
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        try { e.ExtractToFile(target, true); }
                        catch (Exception ex) { Log.Warn($"解压失败 {rel}：{ex.Message}"); }

                        done++;
                        if ((done & 31) == 0)
                            task?.Report(0.9 * done / entries.Count, $"解压 {rel}");
                    }
                }

                task?.Report(0.95, "查找可启动的版本…");
                var id = PickVersion(repo);
                if (id == null)
                    throw new ContentException("解压完成，但没找到可以启动的版本（缺少 versions\\<版本>\\<版本>.json）");

                Log.Info($"绿色整合包解压完成，识别到版本 {id}（{repo.Root}）");
                task?.Report(1, "整合包解压完成");
                return id;
            }, ct);
        }

        /// <summary>挑一个最像正式游戏版本的 id</summary>
        static string PickVersion(GameRepo repo)
        {
            if (!Directory.Exists(repo.Versions)) return null;
            var ids = Directory.GetDirectories(repo.Versions)
                .Select(Path.GetFileName)
                .Where(id => !string.IsNullOrEmpty(id) && File.Exists(repo.VersionJson(id)))
                .ToList();
            if (ids.Count == 0) return null;

            return ids
                .OrderByDescending(id => Regex.IsMatch(id, @"^\d+\.\d+(\.\d+)?$") ? 2 : 1)
                .ThenByDescending(id => File.Exists(repo.VersionJar(id)) ? 1 : 0)
                .First();
        }
    }
}
