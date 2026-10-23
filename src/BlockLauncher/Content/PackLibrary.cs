using System;
using System.IO;
using System.Linq;
using System.Text;
using BlockLauncher.Core;

namespace BlockLauncher.Content
{
    /// <summary>
    /// 整合包存放规则：每个整合包一个独立的游戏目录，互不干扰。
    ///
    ///   &lt;软件所在目录&gt;\整合包\&lt;整合包名&gt;\.minecraft\versions\&lt;版本&gt;\...
    ///
    /// 这样每个包的 Mod、存档、配置、日志都躺在自己文件夹里，删掉整个文件夹就等于卸载干净。
    /// 根目录可在 设置 → 下载 → 整合包安装位置 里改。
    /// </summary>
    public static class PackLibrary
    {
        public static string Root => Settings.PacksRoot;

        /// <summary>确保根目录可用；装在 Program Files 之类不可写的位置时自动退到数据目录</summary>
        static string EnsureRoot()
        {
            var root = Settings.PacksRoot;
            try
            {
                Directory.CreateDirectory(root);
                return root;
            }
            catch (Exception ex)
            {
                var fallback = Path.Combine(Paths.DataDir, "整合包");
                Log.Warn($"整合包目录 {root} 不可写（{ex.Message}），改用 {fallback}");
                Directory.CreateDirectory(fallback);
                Settings.PacksRoot = fallback;
                return fallback;
            }
        }

        /// <summary>把整合包名变成安全的文件夹名（去掉非法字符、限制长度）</summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "整合包";
            var bad = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (var c in name.Trim())
                sb.Append(Array.IndexOf(bad, c) >= 0 ? '_' : c);
            var s = sb.ToString().Trim().TrimEnd('.');
            if (s.Length > 48) s = s.Substring(0, 48).Trim();
            return string.IsNullOrWhiteSpace(s) ? "整合包" : s;
        }

        /// <summary>
        /// 为整合包新建独立游戏目录、登记到游戏目录列表并切换过去。
        /// 返回 .minecraft 的完整路径；重名时自动加 (2)、(3)。
        /// </summary>
        public static string Create(string packName, out string folderName)
        {
            Directory.CreateDirectory(EnsureRoot());

            var baseName = SafeName(packName);
            folderName = baseName;
            var dir = Path.Combine(Root, folderName);
            for (int i = 2; Directory.Exists(dir); i++)
            {
                folderName = baseName + " (" + i + ")";
                dir = Path.Combine(Root, folderName);
            }

            var dotMc = Path.Combine(dir, ".minecraft");
            Directory.CreateDirectory(dotMc);

            var list = Settings.GameFolders;
            if (!list.Any(f => string.Equals(f.Path, dotMc, StringComparison.OrdinalIgnoreCase)))
                list.Add(new GameFolder { Name = folderName, Path = dotMc });
            Settings.GameFolders = list;
            Settings.SelectedGameFolder = dotMc;

            Log.Info($"整合包目录：{dotMc}");
            return dotMc;
        }
    }
}
