using System;
using System.Diagnostics;
using System.IO;

namespace BlockLauncher.Core
{
    /// <summary>
    /// 便携优先：配置和数据都放在 exe 旁边的 Block Launcher 文件夹里，拷走就能用。
    /// exe 所在目录不可写（比如放进了 Program Files）时退回 %APPDATA%\Block Launcher。
    /// </summary>
    public static class Paths
    {
        /// <summary>
        /// 改名（OpenLauncher → Block Launcher）之前的目录名。
        /// 只用于一次性数据迁移和历史整合包的正向识别，新代码不要拿它当当前名字用。
        /// </summary>
        public const string LegacyAppName = "OpenLauncher";

        public static readonly string ExePath;
        public static readonly string ExeDir;
        public static readonly string DataDir;

        /// <summary>本次启动从哪个旧目录搬迁过数据；没有搬迁则为 null。</summary>
        public static string MigratedFrom { get; private set; }

        /// <summary>初始化期间的异常（迁移失败等）。此时 Log 还不能用，先存下来由 App 启动后统一输出。</summary>
        public static string InitWarning { get; private set; }

        static Paths()
        {
            ExePath = Process.GetCurrentProcess().MainModule.FileName;
            ExeDir = Path.GetDirectoryName(ExePath);

            var root = CanWrite(ExeDir) ? ExeDir : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            DataDir = Path.Combine(root, Branding.AppName);

            MigratedFrom = TryMigrate(root, DataDir);
            Directory.CreateDirectory(DataDir);
        }

        public static string Config => Path.Combine(DataDir, "config.json");
        public static string Accounts => Path.Combine(DataDir, "accounts.dat");
        public static string Cache => Ensure(Path.Combine(DataDir, "cache"));
        public static string Logs => Ensure(Path.Combine(DataDir, "logs"));
        public static string Temp => Ensure(Path.Combine(DataDir, "temp"));
        public static string JavaRuntimes => Ensure(Path.Combine(DataDir, "java"));
        public static string Tools => Ensure(Path.Combine(DataDir, "tools"));

        /// <summary>默认游戏目录：exe 旁边的 .minecraft（和 PCL 习惯一致）</summary>
        public static string DefaultGameDir => Path.Combine(ExeDir, ".minecraft");
        public static string OfficialGameDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

        public static string Ensure(string dir) { Directory.CreateDirectory(dir); return dir; }

        /// <summary>
        /// 把旧的 OpenLauncher 数据目录整体搬到新名字下。
        /// 只在"旧目录存在、新目录不存在（或是个空壳）"时动手；同一分区内是重命名，不复制、不丢数据。
        /// </summary>
        static string TryMigrate(string root, string target)
        {
            try
            {
                var legacy = Path.Combine(root, LegacyAppName);
                if (!Directory.Exists(legacy)) return null;

                if (Directory.Exists(target))
                {
                    // 新目录已存在：只有完全空的才让位（通常是上次启动到一半崩掉留下的空壳）
                    if (Directory.GetFileSystemEntries(target).Length > 0) return null;
                    Directory.Delete(target, true);
                }

                Directory.Move(legacy, target);
                return legacy;
            }
            catch (Exception ex)
            {
                InitWarning = $"旧数据目录迁移失败（{LegacyAppName} → {Branding.AppName}）：{ex.Message}";
                Debug.WriteLine(InitWarning);
                return null;
            }
        }

        static bool CanWrite(string dir)
        {
            try
            {
                var probe = Path.Combine(dir, ".ol_write_test");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }
    }
}
