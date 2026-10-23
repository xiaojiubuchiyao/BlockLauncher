using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using BlockLauncher.Accounts;
using BlockLauncher.Core;
using BlockLauncher.UI;
using BlockLauncher.Update;

namespace BlockLauncher
{
    public partial class App : Application
    {
        static App()
        {
            // 与 App.config 相同的长路径开关；写在代码里，单独拷走 exe 也生效
            AppContext.SetSwitch("Switch.System.IO.UseLegacyPathHandling", false);
            AppContext.SetSwitch("Switch.System.IO.BlockLongPaths", false);
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUiException;
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Error("未处理的异常", ex.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Warn("未观察的任务异常：" + ex.Exception?.GetBaseException().Message); ex.SetObserved(); };

            TaskManager.Dispatcher = Dispatcher;
            Log.Info($"{Branding.AppName} {AppInfo.VersionString} 启动，系统 {Util.OsVersion} {Util.OsArch}，数据目录 {Paths.DataDir}");
            if (Paths.MigratedFrom != null)
                Log.Info($"已把旧数据目录 {Paths.MigratedFrom} 迁移为 {Paths.DataDir}");
            if (Paths.InitWarning != null)
                Log.Warn(Paths.InitWarning);
            Updater.CleanupOld();
            Settings.Load();
            try { Settings.PruneGameFolders(); } catch (Exception ex) { Log.Warn("清理失效的游戏目录失败：" + ex.Message); }
            try { AccountStore.Load(); } catch (Exception ex) { Log.Error("读取账号失败", ex); }
            Theme.Apply(Settings.ThemeColor);
#if DEBUG
            DemoCapture.Init(e.Args);
#endif

            var win = new MainWindow();
            MainWindow = win;
            win.Show();

            ImportFromCommandLine(e.Args);
        }

        /// <summary>
        /// 支持把整合包路径当参数传进来（Block Launcher.exe 某某.mrpack），
        /// 走的是和拖放完全相同的导入逻辑。拖放出问题时这是可用的旁路。
        /// </summary>
        static void ImportFromCommandLine(string[] args)
        {
            if (args == null) return;
            foreach (var raw in args)
            {
                if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("--")) continue;
                var path = raw.Trim().Trim('"');
                try
                {
                    if (!System.IO.File.Exists(path)) continue;
                    var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                    if (ext != ".mrpack" && ext != ".zip") continue;
                }
                catch { continue; }

                Log.Info("[命令行] 收到整合包参数：" + path);
                RunImport(path);
                return;
            }
        }

        static async void RunImport(string path)
        {
            try { await UI.Pages.VersionsPage.ImportPackAsync(path, false); }
            catch (Exception ex)
            {
                Log.Error("[命令行] 导入失败", ex);
                try { Dialogs.Alert("导入整合包失败", ex.GetBaseException().Message); } catch { }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Settings.SaveNow();
            base.OnExit(e);
        }

        void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("界面异常", e.Exception);
            e.Handled = true;
            try { Dialogs.Alert("出错了", e.Exception.GetBaseException().Message + "\n\n详细信息已写入启动器日志。"); }
            catch { MessageBox.Show(e.Exception.ToString(), Branding.AppName); }
        }
    }
}
