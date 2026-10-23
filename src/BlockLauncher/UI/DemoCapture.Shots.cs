#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlockLauncher.Accounts;
using BlockLauncher.Content;
using BlockLauncher.Core;
using BlockLauncher.Game;
using BlockLauncher.UI.Pages;
using BlockLauncher.Update;

namespace BlockLauncher.UI
{
    public static partial class DemoCapture
    {
        static async Task<bool> Until(Func<bool> cond, int timeoutMs = 20000, int poll = 200)
        {
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                bool ok; try { ok = cond(); } catch { ok = false; }
                if (ok) return true;
                await Task.Delay(poll);
            }
            Log.Warn("采集：等待超时");
            return false;
        }

        static bool Want(string part) => Script == "all" || Script.Split(',').Contains(part);

        static (string, FrameworkElement) M(string k, FrameworkElement e) => (k, e);

        static void CloseDialog()
        {
            if (W.DialogHost.Content is FrameworkElement fe) W.CloseDialogContent(fe);
        }

        static int ImagesLoaded(DependencyObject root) => FindAll<Image>(root, i => i.Source != null && i.ActualWidth > 30).Count;

        public static async Task RunAsync()
        {
            try
            {
                W.Width = 1100; W.Height = 690;
                Settings.AfterLaunch = "keep";
                await Settle(1200);
                var repo = GameRepo.Current;
                var fabric = new Instance(repo, "1.20.1-Fabric_0.19.5");
                Settings.SelectedVersion = fabric.Id;

                // 账号：离线 Steve + 默认皮肤头像
                var acc = AccountStore.All.FirstOrDefault(a => a.Name == "Steve") ?? OfflineAuth.Create("Steve");
                AccountStore.AddOrUpdate(acc);
                AccountStore.Selected = acc;
                BitmapSource head = null;
                var skin = Path.Combine(OutDir, "steve.png");
                if (File.Exists(skin)) head = Skins.RenderHead(File.ReadAllBytes(skin), 64);
                void avatar()
                {
                    var lp = W.LaunchPage;
                    lp.RefreshAccount();
                    if (head != null) { lp.Avatar.Source = head; lp.AvatarFallback.Visibility = Visibility.Collapsed; }
                }

                // ---------- 启动页 ----------
                if (Want("launch"))
                {
                    W.GoTab("launch");
                    W.LaunchPage.RefreshVersion();
                    await Until(() => W.LaunchPage.LatestRelease.Visibility == Visibility.Visible, 30000);
                    avatar();
                    await Settle(600);
                    avatar();
                    var lp = W.LaunchPage;
                    Snap("launch", M("launch", lp.LaunchButton), M("avatar", (FrameworkElement)lp.Avatar.Parent), M("accounts", ButtonByText(lp, "切换 / 管理账号")),
                        M("versions", ButtonByText(lp, "版本选择")), M("vsettings", ButtonByText(lp, "版本设置")), M("latest", lp.LatestRelease),
                        M("nav_launch", W.NavLaunch), M("nav_download", W.NavDownload), M("nav_settings", W.NavSettings), M("nav_more", W.NavMore),
                        M("tasks", W.TasksButton), M("almanac", (FrameworkElement)lp.GoodList));
                }

                // ---------- 账号 ----------
                if (Want("accounts"))
                {
                    W.GoTab("launch"); avatar(); await Settle();
                    _ = AccountDialogs.ManageAsync();
                    await Settle(700);
                    var dh = W.DialogHost;
                    Snap("accounts", M("offline", ButtonByText(dh, "离线")), M("ms", ButtonByText(dh, "微软正版")), M("ygg", ButtonByText(dh, "外置登录")), M("done", ButtonByText(dh, "完成")));
                    Click(ButtonByText(dh, "外置登录"));
                    await Settle(700);
                    Snap("ygg", M("server", Find<TextBox>(W.DialogHost)), M("login", ButtonByText(W.DialogHost, "登录")));
                    Click(ButtonByText(W.DialogHost, "取消"));
                    await Settle(500);
                    Click(ButtonByText(W.DialogHost, "完成"));
                    await Settle(400);
                }

                // ---------- 版本选择 ----------
                if (Want("versions"))
                {
                    W.GoTab("launch");
                    var vp = new VersionsPage();
                    W.Push(vp);
                    await Until(() => FindAll<ListBoxItem>(vp.Versions).Count >= 3, 15000);
                    await Settle(700);
                    var items = FindAll<ListBoxItem>(vp.Versions);
                    Snap("versions", items.Select((it, i) => M("v" + i, (FrameworkElement)it)).Concat(new[] { M("folders", (FrameworkElement)vp.Folders), M("import", ButtonByText(vp, "导入整合包")) }).ToArray());
                    W.Back();
                }

                // ---------- 下载：版本列表 ----------
                if (Want("dllist"))
                {
                    W.GoTab("download");
                    var dp = W.DownloadPage;
                    dp.NavGame.IsChecked = true;
                    var gv = (GameListView)dp.Host.Content;
                    await Until(() => gv.Loading.Visibility == Visibility.Collapsed && gv.List.Items.Count > 5, 40000);
                    await Settle(700);
                    var first = FindAll<ListBoxItem>(gv.List).FirstOrDefault();
                    Snap("dl_list", M("first", first), M("install", first != null ? Find<Button>(first) : null), M("pills", (FrameworkElement)gv.FRelease.Parent), M("nav_game", dp.NavGame), M("nav_mod", dp.NavMod), M("nav_pack", dp.NavPack));
                    var sv = ScrollerOf(gv.List);
                    await Seq("dl_scroll", 24, async i => { sv.ScrollToVerticalOffset(900 * Ease(i / 23.0)); await Settle(60); });
                    sv.ScrollToVerticalOffset(0);
                }

                // ---------- 安装向导：逐个加载器 ----------
                if (Want("install"))
                {
                    var list = await VersionList.GetMinecraftAsync();
                    var mc = list.First(v => v.Id == "1.21.11");
                    W.GoTab("download");
                    var ip = new GameInstallPage(mc);
                    W.Push(ip);
                    await Settle(800);
                    (string, FrameworkElement)[] marks() => new[] { M("p_none", (FrameworkElement)ip.LNone), M("p_fabric", ip.LFabric), M("p_forge", ip.LForge), M("p_neo", ip.LNeo), M("p_quilt", ip.LQuilt),
                        M("combo", ip.LoaderVersions), M("install", ip.InstallButton), M("api", ip.WithApi), M("of", ip.OptiFineRow), M("name", ip.NameBox) };
                    Snap("inst_none", marks());
                    foreach (var (rb, key) in new[] { (ip.LFabric, "fabric"), (ip.LForge, "forge"), (ip.LNeo, "neoforge"), (ip.LQuilt, "quilt") })
                    {
                        rb.IsChecked = true;
                        await Until(() => ip.LoaderVersions.Items.Count > 0 || (ip.LoaderStatus.Text ?? "").Contains("失败") || (ip.LoaderStatus.Text ?? "").Contains("不支持"), 40000);
                        await Settle(500);
                        Snap("inst_" + key, marks());
                    }
                    ip.LForge.IsChecked = true;
                    await Until(() => ip.LoaderVersions.Items.Count > 0, 30000);
                    ip.WithOptiFine.IsChecked = true;
                    ip.GetType().GetMethod("WithOptiFine_Click", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(ip, new object[] { ip.WithOptiFine, new RoutedEventArgs() });
                    await Until(() => ip.OptiFineVersions.Items.Count > 0 || (ip.OptiFineStatus.Text ?? "").Length > 0 && !(ip.OptiFineStatus.Text ?? "").Contains("获取中"), 40000);
                    await Settle(600);
                    Snap("inst_forge_optifine", marks());
                    ip.LFabric.IsChecked = true;
                    await Until(() => ip.LoaderVersions.Items.Count > 0, 30000);
                    await Settle(500);
                    Snap("inst_fabric_final", marks());
                    W.Back();
                }

                // ---------- 真实下载进度（装 26.3 几秒后取消） ----------
                if (Want("tasks"))
                {
                    W.GoTab("launch"); avatar();
                    var t = TaskManager.Start("安装 26.3", async task => { await Installer.InstallAsync(new InstallRequest { Repo = repo, McVersion = "26.3", Name = "26.3" }, task); });
                    await Until(() => t.Speed > 0, 60000);
                    W.ToggleTasks(true);
                    await Settle(400);
                    await Seq("tasks_dl", 28, async i => { await Task.Delay(450); });
                    t.Cancel();
                    await Until(() => !t.IsActive, 30000);
                    W.ToggleTasks(false);
                    TaskManager.ClearFinished();
                    Util.TryDeleteDir(repo.VersionDir("26.3"));
                }

                // ---------- Mod 搜索 ----------
                ContentProject sodium = null;
                if (Want("mods"))
                {
                    W.GoTab("download");
                    W.DownloadPage.OpenContent(ContentType.Mod, fabric);
                    var cv = (ContentSearchView)W.DownloadPage.Host.Content;
                    await Until(() => cv.Results.Items.Count > 3, 40000);
                    await Until(() => ImagesLoaded(cv.Results) >= 4, 40000);
                    await Settle(800);
                    (string, FrameworkElement)[] cm() => new[] { M("query", (FrameworkElement)cv.Query), M("search", ButtonByText(cv, "搜索")), M("src_mr", cv.SrcModrinth), M("src_cf", cv.SrcCurse),
                        M("ver", cv.GameVersion), M("loader", cv.Loader), M("target", cv.Target), M("r0", FindAll<ListBoxItem>(cv.Results).FirstOrDefault()) };
                    Snap("mod_list", cm());
                    cv.Query.Text = "钠";
                    await Settle(200);
                    Snap("mod_typed", cm());
                    Click(ButtonByText(cv, "搜索"));
                    await Until(() => cv.Results.Items.Count > 0 && cv.Results.Items.Cast<ContentProject>().Any(p => p.Slug == "sodium"), 40000);
                    await Until(() => ImagesLoaded(cv.Results) >= 2, 30000);
                    await Settle(800);
                    Snap("mod_sodium", cm());
                    sodium = cv.Results.Items.Cast<ContentProject>().FirstOrDefault(p => p.Slug == "sodium");

                    if (sodium != null)
                    {
                        var pp = new ProjectPage(sodium, ContentType.Mod, fabric, "1.20.1", "fabric");
                        W.Push(pp);
                        await Until(() => pp.Files.Items.Count > 0, 40000);
                        await Settle(900);
                        var dl = FindAll<Button>(pp.Files, b => (b.Content as string) == "下载").FirstOrDefault();
                        Snap("project", M("download", dl), M("files", (FrameworkElement)pp.Files), M("title", pp.PTitle));
                        if (dl != null)
                        {
                            Click(dl);
                            await Settle(350);
                            Snap("project_click", M("download", dl));
                            var mt = TaskManager.Tasks.FirstOrDefault(x => x.Title.Contains("Sodium"));
                            await Until(() => mt == null || !mt.IsActive, 90000);
                            W.ToggleTasks(true);
                            await Settle(500);
                            Snap("project_done");
                            W.ToggleTasks(false);
                        }
                        W.Back();
                    }
                }

                // ---------- Mod 管理 + 检查更新 ----------
                if (Want("manage"))
                {
                    // 装一个旧版 Sodium，演示“有更新”
                    try
                    {
                        var files = await ContentService.Modrinth.GetFilesAsync("sodium", "1.20.1", "fabric", CancellationToken.None);
                        var old = files.Where(f => f.ReleaseType == "release").Skip(2).FirstOrDefault();
                        if (old != null)
                        {
                            var tt = new LauncherTask();
                            await ContentService.InstallAsync(old, fabric.ModsDir, false, "1.20.1", "fabric", tt);
                        }
                    }
                    catch (Exception ex) { Log.Warn("采集：准备旧版 Sodium 失败 " + ex.Message); }
                    W.GoTab("launch");
                    var ip = new InstancePage(fabric, "mods");
                    W.Push(ip);
                    await Until(() => ip.ModList.Items.Count > 0, 20000);
                    await Settle(900);
                    Snap("mods_list", M("check", ip.CheckUpdatesBtn), M("download", ButtonByText(ip, "下载 Mod")), M("list", (FrameworkElement)ip.ModList), M("tab_mods", ip.TabMods),
                        M("m0", FindAll<ListBoxItem>(ip.ModList).FirstOrDefault()));
                    Click(ip.CheckUpdatesBtn);
                    await Until(() => ip.UpdateAllBtn.Visibility == Visibility.Visible || ip.CheckUpdatesBtn.IsEnabled, 60000);
                    await Until(() => ip.CheckUpdatesBtn.IsEnabled, 60000);
                    await Settle(900);
                    var withUpd = FindAll<ListBoxItem>(ip.ModList).FirstOrDefault(li => (li.DataContext as LocalMod)?.HasUpdate == true);
                    Snap("mods_update", M("check", ip.CheckUpdatesBtn), M("updall", ip.UpdateAllBtn), M("upd", withUpd));
                    // 版本设置页
                    ip.TabSettings.IsChecked = true;
                    await Settle(700);
                    Snap("inst_settings", M("isolation", ip.Isolation), M("java", ip.JavaBox), M("mem", ip.MemMode));
                    W.Back();
                }

                // ---------- 崩溃分析 ----------
                if (Want("crash"))
                {
                    W.GoTab("launch"); avatar(); await Settle();
                    var s = new GameSession { Instance = fabric, StartTime = DateTime.Now, Java = JavaManager.Known.FirstOrDefault() };
                    s.WindowShown = true;
                    foreach (var line in new[]
                    {
                        "[12:03:41] [main/INFO]: Loading Minecraft 1.20.1 with Fabric Loader 0.19.5",
                        "[12:03:44] [main/INFO]: Loading 38 mods:",
                        "[12:03:52] [Render thread/ERROR]: Mixin apply failed fancyhud.mixins.json:client.MixinInGameHud -> net.minecraft.class_329: org.spongepowered.asm.mixin.injection.throwables.InvalidInjectionException: Critical injection failure: @Inject annotation on renderHotbar could not find any targets matching 'renderHotbar'",
                        "org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError: An unexpected critical error was encountered",
                        "Caused by: org.spongepowered.asm.mixin.throwables.MixinApplyError: Mixin [fancyhud.mixins.json:client.MixinInGameHud] from phase [DEFAULT] in config [fancyhud.mixins.json] FAILED during APPLY",
                    }) s.AddLine(line);
                    _ = CrashDialog.ShowAsync(s, 1);
                    await Until(() => W.DialogHost.Content != null, 10000);
                    await Settle(900);
                    Snap("crash", M("export", ButtonByText(W.DialogHost, "导出错误报告")), M("log", ButtonByText(W.DialogHost, "查看日志")));
                    CloseDialog();
                    await Settle(300);
                }

                // ---------- 设置 ----------
                if (Want("settings"))
                {
                    W.GoTab("settings");
                    var sp = W.SettingsPage;
                    foreach (var sec in new[] { "launch", "java", "download", "account", "update" })
                    {
                        sp.ScrollTo(sec);
                        await Settle(700);
                        Snap("set_" + sec, M("mirrors", (FrameworkElement)sp.MirrorList), M("keys", (FrameworkElement)sp.KeyList), M("check", sp.CheckUpdateBtn),
                            M("gamesrc", sp.GameSource), M("modsrc", sp.ModSource), M("threads", sp.Threads), M("javalist", (FrameworkElement)sp.JavaList), M("msid", sp.MsClientId));
                    }
                }

                // ---------- 主题色 ----------
                if (Want("theme"))
                {
                    W.GoTab("launch"); avatar(); await Settle();
                    int k = 0;
                    foreach (var (name, hex) in Theme.Presets)
                    {
                        Theme.Apply(hex);
                        await Settle(250);
                        Snap($"theme_{k++:00}");
                    }
                    Theme.Apply(Settings.ThemeColor);
                    W.GoTab("settings");
                    W.SettingsPage.ScrollTo("ui");
                    await Settle(700);
                    Snap("set_ui", M("presets", (FrameworkElement)W.SettingsPage.ThemePresets));
                }

                // ---------- 更新提示 ----------
                if (Want("update"))
                {
                    W.GoTab("launch"); avatar(); await Settle();
                    _ = Dialogs.ShowUpdateAsync(new UpdateInfo
                    {
                        Version = "0.2.0", Size = 768_000, Signer = "12c461c38f08483a", FoundAt = "https://cdn.jsdelivr.net/gh/xiaojiubuchiyao/BlockLauncher@main/release/update.json",
                        Notes = "· 新增 EasyTier 联机\n· CurseForge 指纹查更新\n· 修复若干问题", Urls = { "https://example.invalid/BlockLauncher.exe" }, Sha256 = "00",
                    });
                    await Settle(900);
                    Snap("update_dialog", M("ok", ButtonByText(W.DialogHost, "立即更新")));
                    CloseDialog();
                    await Settle(300);
                }

                // ---------- 更多 ----------
                if (Want("more"))
                {
                    W.GoTab("more");
                    await Settle(700);
                    Snap("more");
                }

                // ---------- 整合包（最后做：要真实下载几十个文件） ----------
                if (Want("pack"))
                {
                    W.GoTab("download");
                    W.DownloadPage.OpenContent(ContentType.Modpack, null);
                    var cv = (ContentSearchView)W.DownloadPage.Host.Content;
                    await Until(() => cv.Results.Items.Count > 3, 40000);
                    await Until(() => ImagesLoaded(cv.Results) >= 4, 40000);
                    await Settle(800);
                    Snap("pack_list", M("r0", FindAll<ListBoxItem>(cv.Results).FirstOrDefault()));
                    cv.Query.Text = "Fabulously Optimized";
                    Click(ButtonByText(cv, "搜索"));
                    await Until(() => cv.Results.Items.Cast<ContentProject>().Any(p => p.Slug == "fabulously-optimized"), 40000);
                    await Until(() => ImagesLoaded(cv.Results) >= 1, 20000);
                    await Settle(700);
                    Snap("pack_search", M("r0", FindAll<ListBoxItem>(cv.Results).FirstOrDefault()));
                    var fo = cv.Results.Items.Cast<ContentProject>().FirstOrDefault(p => p.Slug == "fabulously-optimized");
                    if (fo != null)
                    {
                        var pp = new ProjectPage(fo, ContentType.Modpack, null, "1.20.1", null);
                        W.Push(pp);
                        await Until(() => pp.Files.Items.Count > 0, 40000);
                        await Settle(900);
                        var dl = FindAll<Button>(pp.Files, b => (b.Content as string) == "下载").FirstOrDefault();
                        Snap("pack_project", M("download", dl));
                        if (dl != null)
                        {
                            Click(dl);
                            await Settle(800);
                            var box = Find<TextBox>(W.DialogHost);
                            if (box != null) box.Text = "Fabulously Optimized";
                            Snap("pack_name", M("ok", ButtonByText(W.DialogHost, "确定")));
                            Click(ButtonByText(W.DialogHost, "确定"));
                            await Settle(1500);
                            var pt = TaskManager.Tasks.FirstOrDefault(x => x.Title.Contains("整合包"));
                            await Seq("pack_import", 40, async i => { await Task.Delay(1500); });
                            await Until(() => pt == null || !pt.IsActive, 600000);
                            await Settle(600);
                            Snap("pack_done");
                            W.ToggleTasks(false);
                        }
                        W.Back();
                    }
                }

                // ---------- 空状态按钮命中测试（回归用：空列表不能挡住提示按钮） ----------
                if (Want("hit"))
                {
                    var prevFolder = Settings.SelectedGameFolder;
                    var tmpDir = Path.Combine(OutDir, "empty-mc");
                    Directory.CreateDirectory(tmpDir);
                    Settings.SelectedGameFolder = tmpDir;

                    W.GoTab("launch");
                    var vp = new VersionsPage();
                    W.Push(vp);
                    await Until(() => vp.Empty.Visibility == Visibility.Visible, 15000);
                    await Settle(600);
                    Snap("hit_empty", M("godl", ButtonByText(vp.Empty, "去下载一个")));

                    var btn = ButtonByText(vp.Empty, "去下载一个");
                    var lines = new System.Collections.Generic.List<string>();

                    (string desc, bool onButton) probe(string tag)
                    {
                        Root.UpdateLayout();
                        if (btn == null) return (tag + "找不到按钮", false);
                        var pt = btn.TransformToAncestor(Root).Transform(new Point(btn.ActualWidth / 2, btn.ActualHeight / 2));
                        var res = VisualTreeHelper.HitTest(Root, pt);
                        var hitEl = res?.VisualHit;
                        DependencyObject d = hitEl;
                        while (d != null && !(d is System.Windows.Controls.Primitives.ButtonBase)) d = VisualTreeHelper.GetParent(d);
                        return ($"{tag}按钮中心=({pt.X:0.#},{pt.Y:0.#}) 命中={hitEl?.GetType().Name ?? "null"} 向上取到={d?.GetType().Name ?? "null"} 命中按钮本身={(d == btn ? "是" : "否")}", d == btn);
                    }

                    var now = probe("修复后：");
                    lines.Add(now.desc);

                    // 对照组：还原旧写法的层级（列表压在空状态之上）
                    if (btn != null)
                    {
                        Panel.SetZIndex(vp.Empty, -1);
                        vp.Versions.Visibility = Visibility.Visible;
                        var old = probe("旧写法：");
                        lines.Add(old.desc);
                        Panel.SetZIndex(vp.Empty, 1);
                    }

                    lines.Add($"列表可见性={vp.Versions.Visibility} 空状态={vp.Empty.Visibility}");

                    // 真实点击（走命中测试路径：用 AutomationPeer 触发，而不是 RaiseEvent）
                    if (btn != null)
                    {
                        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(btn);
                        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                        await Settle(900);
                        lines.Add("点击后当前页=" + (W.PageHost.Content?.GetType().Name ?? "null"));
                    }

                    File.WriteAllText(Path.Combine(OutDir, "hittest.txt"), string.Join(Environment.NewLine, lines));
                    Settings.SelectedGameFolder = prevFolder;
                    W.GoTab("launch");
                }

                // ---------- 「所有源」：两个源并行搜索合并 ----------
                if (Want("srcall"))
                {
                    W.GoTab("download");
                    W.DownloadPage.OpenContent(ContentType.Mod, fabric);
                    var cv = (ContentSearchView)W.DownloadPage.Host.Content;
                    await Settle(300);
                    cv.SrcAll.IsChecked = true;
                    await Until(() => cv.Results.Items.Count > 5, 90000);
                    await Settle(1500);
                    var found = cv.Results.Items.Cast<ContentProject>().ToList();
                    File.WriteAllText(Path.Combine(OutDir, "srcall.txt"),
                        "共 " + found.Count + " 条\n" +
                        string.Join("\n", found.GroupBy(p => p.Source).Select(g => g.Key + " = " + g.Count())) + "\n---前 10 条---\n" +
                        string.Join("\n", found.Take(10).Select(p => p.SourceText + " | " + p.Title + " | " + p.DownloadsText)));
                    Snap("src_all", M("all", cv.SrcAll), M("mr", cv.SrcModrinth), M("cf", cv.SrcCurse),
                        M("r0", FindAll<ListBoxItem>(cv.Results).FirstOrDefault()), M("r1", FindAll<ListBoxItem>(cv.Results).Skip(1).FirstOrDefault()));
                }

                // ---------- 加载态：不确定进度条（检查是否溢出轨道） ----------
                if (Want("loading"))
                {
                    W.GoTab("download");
                    var dpl = W.DownloadPage;
                    dpl.NavGame.IsChecked = true;
                    var gvl = (GameListView)dpl.Host.Content;
                    await Until(() => gvl.List.Items.Count > 5, 40000);
                    gvl.List.Visibility = Visibility.Collapsed;
                    gvl.Loading.Visibility = Visibility.Visible;
                    await Settle(60);
                    Snap("loading_a");
                    await Settle(400);
                    Snap("loading_b");
                    await Settle(400);
                    Snap("loading_c");
                }

                // ---------- 拖放整合包提示层 ----------
                if (Want("drop"))
                {
                    var dir = Paths.Ensure(Path.Combine(Paths.Temp, "dropdemo"));
                    var good = Path.Combine(dir, "SamplePack.mrpack");
                    var bad = Path.Combine(dir, "NotAPack.zip");
                    const string index = "{\"formatVersion\":1,\"game\":\"minecraft\",\"versionId\":\"1.2.0\","
                        + "\"name\":\"冒险整合包 Adventure Lite\",\"summary\":\"demo\",\"files\":[],"
                        + "\"dependencies\":{\"minecraft\":\"1.21.1\",\"fabric-loader\":\"0.16.5\"}}";
                    if (File.Exists(good)) File.Delete(good);
                    if (File.Exists(bad)) File.Delete(bad);
                    using (var z = System.IO.Compression.ZipFile.Open(good, System.IO.Compression.ZipArchiveMode.Create))
                    using (var w = new StreamWriter(z.CreateEntry("modrinth.index.json").Open()))
                        w.Write(index);
                    using (var z = System.IO.Compression.ZipFile.Open(bad, System.IO.Compression.ZipArchiveMode.Create))
                    using (var w = new StreamWriter(z.CreateEntry("readme.txt").Open()))
                        w.Write("not a modpack");

                    W.GoTab("launch");
                    W.ShowDropState(good);
                    await Settle(400);
                    Snap("drop_ok");
                    W.ShowDropState(bad);
                    await Settle(300);
                    Snap("drop_bad");
                    W.ShowDropState(null);
                }

                // ---------- 整合包独立目录命名规则 ----------
                if (Want("packdir"))
                {
                    var beforeFolders = Settings.GameFolders;
                    var beforeSel = Settings.SelectedGameFolder;

                    string a, b, c;
                    var pa = PackLibrary.Create("冒险整合包 Adventure", out a);
                    var pb = PackLibrary.Create("冒险整合包 Adventure", out b);
                    var pc = PackLibrary.Create("bad/name:with*chars?", out c);

                    var lines = new[]
                    {
                        "root      = " + Settings.PacksRoot,
                        "first     = " + pa,
                        "           exists=" + Directory.Exists(pa),
                        "second    = " + pb,
                        "           exists=" + Directory.Exists(pb),
                        "sanitized = " + pc,
                        "           exists=" + Directory.Exists(pc),
                        "current   = " + Settings.SelectedGameFolder,
                        "folders   = " + string.Join(" | ", Settings.GameFolders.Select(f => f.Name)),
                    };
                    File.WriteAllText(Path.Combine(OutDir, "packdir.txt"), string.Join("\n", lines));

                    // 还原，别把测试目录留在配置里
                    Settings.GameFolders = beforeFolders;
                    Settings.SelectedGameFolder = beforeSel;
                    Util.TryDeleteDir(Path.GetDirectoryName(Path.GetDirectoryName(pa)));
                    Util.TryDeleteDir(Path.GetDirectoryName(Path.GetDirectoryName(pb)));
                    Util.TryDeleteDir(Path.GetDirectoryName(Path.GetDirectoryName(pc)));
                    await Settle(150);
                }

                // ---------- 导出整合包 ----------
                if (Want("export"))
                {
                    W.GoTab("launch");
                    var ip = new InstancePage(fabric);
                    W.Push(ip);
                    ip.TabExport.IsChecked = true;
                    ip.PackName.Text = "我的生存整合包";
                    ip.PackVersion.Text = "1.0.0";
                    await Settle(800);
                    Snap("inst_export", M("export", ButtonByText(ip, "导出…")), M("tab", ip.TabExport), M("name", ip.PackName));
                    W.Back();
                }

                // ---------- 过渡动画：采样动画中间值，验证"真的在动" ----------
                if (Want("motion"))
                {
                    // 截图一张要 1 秒多，采不到 190ms 的动画；直接读动画属性的中间值最可靠
                    var probe = new System.Text.StringBuilder();
                    async Task Sample(string tag, int count, int stepMs)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            await Task.Delay(stepMs);
                            var pg = W.PageHost.Content as FrameworkElement;
                            var pgX = (pg?.RenderTransform as TranslateTransform)?.X;
                            probe.AppendLine($"{tag} +{i * stepMs}ms  page.opacity={(pg?.Opacity.ToString("0.00") ?? "-")}  page.x={(pgX.HasValue ? pgX.Value.ToString("0.0") : "-")}  drawer.opacity={W.TaskDrawer.Opacity:0.00}  drawer.x={W.TaskDrawerShift.X:0.0}  toast.opacity={W.ToastBox.Opacity:0.00}  toast.y={((W.ToastBox.RenderTransform as TranslateTransform)?.Y ?? 0):0.0}");
                        }
                    }

                    W.GoTab("launch"); avatar(); await Settle(500);

                    // 1) 切页：启动 → 更多（应该从 +28px 滑到 0，透明度 0 → 1）
                    W.NavMore.IsChecked = true;
                    await Sample("页面切到「更多」", 7, 30);
                    await Settle(400);
                    Snap("anim_page_settled");

                    // 2) 任务抽屉（应该从 +46px 退到 0）
                    W.ToggleTasks(true);
                    await Sample("抽屉展开", 6, 35);
                    await Settle(400);
                    Snap("anim_drawer_open");
                    W.ToggleTasks(false);
                    await Settle(400);

                    // 3) 提示条（应该从 +10px 滑上来）
                    W.Toast("过渡动画自检");
                    await Sample("提示条弹出", 6, 40);
                    await Settle(400);

                    // 4) 返回上一页（应该从 -28px 滑入）
                    W.NavLaunch.IsChecked = true;
                    await Settle(300);
                    W.Push(new VersionsPage());
                    await Settle(500);
                    W.Back();
                    await Sample("返回上一页", 6, 30);

                    File.WriteAllText(Path.Combine(OutDir, "motion_probe.txt"), probe.ToString());
                    W.GoTab("launch");
                    await Settle(400);
                }

                // ---------- 设置分区：点左边只显示对应内容 ----------
                if (Want("setsec"))
                {
                    W.GoTab("settings");
                    var sp = W.SettingsPage;
                    var report = new System.Text.StringBuilder();
                    foreach (var sec in new[] { "launch", "java", "download", "account", "ui", "update" })
                    {
                        sp.ScrollTo(sec);
                        await Settle(650);
                        report.AppendLine($"[{sec}] 可见的分区 = " + string.Join(", ", new[]
                        {
                            ("launch", sp.SecLaunch), ("java", sp.SecJava), ("download", sp.SecDownload),
                            ("account", sp.SecAccount), ("ui", sp.SecUi), ("update", sp.SecUpdate),
                        }.Where(t => t.Item2.Visibility == Visibility.Visible).Select(t => t.Item1)));
                        Snap("setsec_" + sec);
                    }
                    File.WriteAllText(Path.Combine(OutDir, "setsec_report.txt"), report.ToString());
                    W.GoTab("launch");
                    await Settle(300);
                }

                // ---------- 加载器版本列表（NeoForge） ----------
                if (Want("neoforge"))
                {
                    var report = new System.Text.StringBuilder();
                    foreach (var mcId in new[] { "1.21.1", "1.20.1" })
                    {
                        var page = new GameInstallPage(new McVersionEntry { Id = mcId, Type = "release" });
                        W.Push(page);
                        await Settle(500);
                        page.LNeo.IsChecked = true;
                        await Until(() => page.LoaderStatus.Text != "获取中…", 40000);
                        await Settle(500);
                        var items = page.LoaderVersions.ItemsSource as List<LoaderVersionEntry>;
                        report.AppendLine($"[{mcId}] status={page.LoaderStatus.Text}  count={items?.Count ?? 0}  selected={(page.LoaderVersions.SelectedItem as LoaderVersionEntry)?.Version}  可安装={page.InstallButton.IsEnabled}");
                        Snap("neoforge_" + mcId.Replace('.', '_'));
                        W.Back();
                        await Settle(400);
                    }
                    File.WriteAllText(Path.Combine(OutDir, "neoforge_report.txt"), report.ToString());
                }

                // ---------- 启动页「我的世界黄历」：抓 MC百科 + 3 小时缓存 + 手动刷新 ----------
                if (Want("almanac"))
                {
                    var cache = Path.Combine(Paths.Cache, "almanac.json");
                    if (File.Exists(cache)) File.Delete(cache);

                    var report = new System.Text.StringBuilder();
                    W.GoTab("launch"); avatar();
                    var lp = W.LaunchPage;

                    var t0 = DateTime.Now;
                    var r1 = await Almanac.GetAsync(false);
                    report.AppendLine($"冷拉（无缓存）：{(DateTime.Now - t0).TotalMilliseconds:0}ms Fresh={r1.Fresh} " +
                                      $"日期=「{r1.DayText}」宜={r1.Good.Count} 忌={r1.Bad.Count}");
                    report.AppendLine($"  缓存={File.Exists(cache)} 大小={(File.Exists(cache) ? new FileInfo(cache).Length : 0)}B");
                    foreach (var e in r1.Good) report.AppendLine($"  宜 · {e.Title} —— {e.Text}");
                    foreach (var e in r1.Bad) report.AppendLine($"  忌 · {e.Title} —— {e.Text}");

                    // 解析是否干净：不该残留标签或未解码的实体
                    var dirty = r1.Good.Concat(r1.Bad)
                        .Where(e => (e.Title + e.Text).IndexOfAny(new[] { '<', '>' }) >= 0
                                    || (e.Title + e.Text).Contains("&amp;") || (e.Title + e.Text).Contains("&lt;"))
                        .ToList();
                    report.AppendLine($"  HTML 残留条目={dirty.Count}（0 才正常）");

                    await lp.LoadAlmanacAsync(false);
                    await Settle(700);
                    report.AppendLine($"界面渲染：宜={lp.GoodList.Items.Count} 忌={lp.BadList.Items.Count} " +
                                      $"时间戳=「{lp.AlmanacStamp.Text}」提示={lp.AlmanacHint.Visibility} 刷新可用={lp.AlmanacRefresh.IsEnabled}");

                    // 3 小时内应命中缓存，不再发请求（缓存只存到秒，比较到秒即可）
                    var t1 = DateTime.Now;
                    var r2 = await Almanac.GetAsync(false);
                    var sameCache = Math.Abs((r2.FetchedAt - r1.FetchedAt).TotalSeconds) < 1;
                    report.AppendLine($"缓存有效期内再拉：{(DateTime.Now - t1).TotalMilliseconds:0}ms 用的是同一份={sameCache} Stamp=「{r2.Stamp}」Fresh={r2.Fresh}");

                    // 把缓存时间改成 4 小时前，应判定过期并重新联网
                    try
                    {
                        var node = JNode.Parse(File.ReadAllText(cache));
                        node.Set("fetched", DateTime.Now.AddHours(-4).ToString("yyyy-MM-dd HH:mm:ss"));
                        node.SaveFile(cache);
                        var t2 = DateTime.Now;
                        var r3 = await Almanac.GetAsync(false);
                        report.AppendLine($"缓存改到 4 小时前：{(DateTime.Now - t2).TotalMilliseconds:0}ms 重新联网={r3.FetchedAt > r2.FetchedAt}");
                    }
                    catch (Exception ex) { report.AppendLine("过期测试失败：" + ex.Message); }

                    var t3 = DateTime.Now;
                    var r4 = await Almanac.GetAsync(true);
                    report.AppendLine($"force 刷新：{(DateTime.Now - t3).TotalMilliseconds:0}ms 宜={r4.Good.Count} 忌={r4.Bad.Count}");

                    var sv = ScrollerOf(lp);
                    sv?.ScrollToEnd();
                    await Settle(700);
                    Snap("almanac", M("refresh", lp.AlmanacRefresh), M("open", ButtonByText(lp, "去百科看看")),
                         M("good", (FrameworkElement)lp.GoodList), M("bad", (FrameworkElement)lp.BadList));

                    // 点「刷新」按钮：force=true，必须重新联网
                    var t4 = DateTime.Now;
                    Click(lp.AlmanacRefresh);
                    await Until(() => lp.AlmanacRefresh.IsEnabled, 30000);
                    await Settle(500);
                    report.AppendLine($"点「刷新」按钮：{(DateTime.Now - t4).TotalMilliseconds:0}ms " +
                                      $"宜={lp.GoodList.Items.Count} 忌={lp.BadList.Items.Count} 时间戳=「{lp.AlmanacStamp.Text}」");

                    // 切走再回来：仍应命中缓存，卡片不闪空
                    W.GoTab("more");
                    await Settle(400);
                    W.GoTab("launch");
                    await Until(() => lp.AlmanacRefresh.IsEnabled, 8000);
                    await Settle(400);
                    report.AppendLine($"切走再回来：宜={lp.GoodList.Items.Count} 忌={lp.BadList.Items.Count} 时间戳=「{lp.AlmanacStamp.Text}」");

                    File.WriteAllText(Path.Combine(OutDir, "almanac_report.txt"), report.ToString());
                }

                // ---------- 账户文件：改名换了熵值，旧文件要安全降级，新文件要能读回 ----------
                if (Want("accountsfile"))
                {
                    var report = new System.Text.StringBuilder();
                    var path = Paths.Accounts;
                    report.AppendLine($"账户文件 = {path}");
                    if (File.Exists(path))
                    {
                        var b = File.ReadAllBytes(path);
                        report.AppendLine($"  旧文件：大小={b.Length} 魔数={(b.Length >= 4 ? System.Text.Encoding.ASCII.GetString(b, 0, 4) : "?")}");
                    }
                    else report.AppendLine("  没有旧文件");

                    // 1) 旧格式（改名前的魔数 + 旧熵值）应解不开，但不能崩：备份 + 空列表
                    AccountStore.Load();
                    report.AppendLine($"第 1 次加载（旧格式）：账户数={AccountStore.Count}");

                    // 2) 新建一个离线账户，按新熵值保存
                    AccountStore.AddOrUpdate(new Account
                    {
                        Type = AccountType.Offline,
                        Name = "新格式测试",
                        Uuid = Util.OfflineUuid("新格式测试"),
                    });
                    var raw = File.ReadAllBytes(path);
                    report.AppendLine($"保存后：大小={raw.Length} 魔数={System.Text.Encoding.ASCII.GetString(raw, 0, 4)}");

                    // 3) 重新从磁盘读：新文件必须解得开
                    AccountStore.Load();
                    report.AppendLine($"第 2 次加载（新格式）：账户数={AccountStore.Count} " +
                                      $"明细={string.Join(" / ", AccountStore.All.Select(a => a.Name + ":" + a.TypeText))}");

                    // 4) 再存再读，确认幂等
                    AccountStore.Save();
                    AccountStore.Load();
                    report.AppendLine($"再存再读：账户数={AccountStore.Count}");

                    File.WriteAllText(Path.Combine(OutDir, "accountsfile_report.txt"), report.ToString());
                }

                // ---------- 底部轻提示：不要胶囊底板，三种状态各截一张 ----------
                if (Want("toast"))
                {
                    W.GoTab("launch"); avatar(); await Settle(900);
                    var sv2 = ScrollerOf(W.LaunchPage);
                    sv2?.ScrollToEnd();
                    await Settle(400);

                    W.Toast("先选择或安装一个版本");
                    await Settle(500);
                    Snap("toast_info", M("icon", W.ToastIcon), M("text", W.ToastText));

                    W.Toast("整合包导入完成", MainWindow.ToastKind.Ok);
                    await Settle(450);
                    Snap("toast_ok", M("icon", W.ToastIcon), M("text", W.ToastText));

                    W.Toast("下载失败（点右上角任务查看原因）", MainWindow.ToastKind.Error);
                    await Settle(450);
                    Snap("toast_err", M("icon", W.ToastIcon), M("text", W.ToastText));

                    // 换到深一点的页面上，确认深色字仍然看得清
                    W.Toast("先选择或安装一个版本");
                    W.GoTab("download");
                    await Settle(700);
                    Snap("toast_dark_bg", M("icon", W.ToastIcon), M("text", W.ToastText));
                    W.GoTab("launch");
                    await Settle(400);
                }

                // ---------- 更新清单签名验证（配合本地 HTTP 服务器） ----------
                if (Want("sigcheck"))
                {
                    W.GoTab("settings");
                    var sp = W.SettingsPage;
                    sp.ScrollTo("update");
                    await Settle(800);
                    sp.CheckUpdateBtn.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    await Until(() => sp.UpdateStatus.Text != "正在从各个更新源拉取签名清单…", 45000, 300);
                    await Settle(400);
                    File.WriteAllText(Path.Combine(OutDir, "sigcheck.txt"),
                        "status=" + sp.UpdateStatus.Text + "\n弹窗=" + (W.DialogHost.Content != null));
                    Snap("sigcheck");
                }

                // ---------- 启动游戏（录游戏画面由外部脚本完成） ----------
                if (Want("play"))
                {
                    W.GoTab("launch"); avatar(); await Settle();
                    fabric.SetSetting("width", 1600);
                    fabric.SetSetting("height", 900);
                    _ = W.LaunchPage.LaunchAsync(fabric);
                    await Seq("launching", 30, async i => { await Task.Delay(300); }, () => new[] { M("launch", (FrameworkElement)W.LaunchPage.LaunchButton) });
                    await Until(() => W.LaunchPage.RunningCard.Visibility == Visibility.Visible, 120000);
                    await Seq("running", 40, async i => { await Task.Delay(1200); });
                    File.WriteAllText(Path.Combine(OutDir, "game_started.txt"), DateTime.Now.ToString("O"));
                    // 外部脚本录完游戏画面会写 game_recorded.txt
                    await Until(() => File.Exists(Path.Combine(OutDir, "game_recorded.txt")), 300000, 1000);
                    foreach (var s in LaunchService.Running.ToList()) s.Kill();
                    await Settle(1500);
                }
            }
            catch (Exception ex)
            {
                Log.Error("采集失败", ex);
                File.WriteAllText(Path.Combine(OutDir, "error.txt"), ex.ToString());
            }
            File.WriteAllText(Path.Combine(OutDir, "done.txt"), "ok");
            Application.Current.Shutdown();
        }

        static double Ease(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
    }
}
#endif
