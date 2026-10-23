using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using BlockLauncher.Core;
using BlockLauncher.UI;
using BlockLauncher.UI.Pages;
using BlockLauncher.Update;

namespace BlockLauncher
{
    public partial class MainWindow : Window
    {
        public static MainWindow Current { get; private set; }

        readonly Dictionary<RadioButton, Stack<FrameworkElement>> _stacks = new Dictionary<RadioButton, Stack<FrameworkElement>>();
        RadioButton[] _navOrder;
        RadioButton _tab;
        int _navIndex;
        int _taskAnim;
        DispatcherTimer _toastTimer;

        public LaunchPage LaunchPage { get; private set; }
        public DownloadPage DownloadPage { get; private set; }
        public SettingsPage SettingsPage { get; private set; }
        public MorePage MorePage { get; private set; }

        public MainWindow()
        {
            Current = this;
            InitializeComponent();
            VersionTag.Text = "v" + AppInfo.VersionString;
            // 导航顺序 = 切页动画的方向依据（往右点的从右边进来）
            _navOrder = new RadioButton[] { NavLaunch, NavDownload, NavSettings, NavMore };
            StateChanged += (s, e2) => MaxButton.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
            // Esc 关任务抽屉（Windows 上很自然的一个习惯）
            PreviewKeyDown += (s, e2) =>
            {
                if (e2.Key == Key.Escape && TaskLayer.Visibility == Visibility.Visible && DialogLayer.Visibility != Visibility.Visible)
                {
                    ToggleTasks(false);
                    e2.Handled = true;
                }
            };
            TaskList.ItemsSource = TaskManager.Tasks;
            TaskManager.Tasks.CollectionChanged += (s, e) => UpdateTaskBadge();
            TaskManager.Added += t => { t.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(LauncherTask.State)) Dispatcher.BeginInvoke((Action)UpdateTaskBadge); }; UpdateTaskBadge(); };
            TaskManager.Finished += OnTaskFinished;

            LaunchPage = new LaunchPage();
            DownloadPage = new DownloadPage();
            SettingsPage = new SettingsPage();
            MorePage = new MorePage();
            _stacks[NavLaunch] = new Stack<FrameworkElement>(new FrameworkElement[] { LaunchPage });
            _stacks[NavDownload] = new Stack<FrameworkElement>(new FrameworkElement[] { DownloadPage });
            _stacks[NavSettings] = new Stack<FrameworkElement>(new FrameworkElement[] { SettingsPage });
            _stacks[NavMore] = new Stack<FrameworkElement>(new FrameworkElement[] { MorePage });

            ApplyBackground();
            NavLaunch.IsChecked = true;
            OpenStartupPage();
#if DEBUG
            if (DemoCapture.Active)
            {
                ShowActivated = false;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -6000; Top = 100;
                Loaded += async (s, e) => await DemoCapture.RunAsync();
                return;
            }
#endif
            Loaded += async (s, e) => await StartupChecksAsync();
        }

        // ---------- 导航 ----------
        void Nav_Checked(object sender, RoutedEventArgs e)
        {
            var tab = (RadioButton)sender;
            int idx = Array.IndexOf(_navOrder, tab);
            int dir = idx == _navIndex ? 0 : (idx > _navIndex ? 1 : -1);
            _navIndex = idx;
            _tab = tab;
            Show(_stacks[tab].Peek(), dir);
        }

        /// <summary>
        /// 切页。dir>0 从右侧滑入，dir<0 从左侧滑入，0 表示原地刷新（只淡入）。
        /// 位移 28px 是有讲究的：太小看不出方向，太大显得慢。
        /// </summary>
        void Show(FrameworkElement page, int dir = 0)
        {
            if (page == null) return;
            PageHost.Content = page;

            const double shift = 28;
            if (dir > 0) Motion.SlideFadeIn(page, shift, 0, Motion.Normal);
            else if (dir < 0) Motion.SlideFadeIn(page, -shift, 0, Motion.Normal);
            else Motion.SlideFadeIn(page, 0, 10, Motion.Normal);

            (page as IPage)?.OnShown();

            // 等这一帧布局跑完再级联卡片，否则 ActualHeight 还是 0，找不到要动的元素
            Dispatcher.BeginInvoke(new Action(() => Motion.StaggerCards(page)), DispatcherPriority.Loaded);
        }

        /// <summary>在当前标签页里打开子页面（有返回）</summary>
        public void Push(FrameworkElement page)
        {
            _stacks[_tab].Push(page);
            Show(page, 1);
        }

        public void Back()
        {
            var st = _stacks[_tab];
            if (st.Count > 1) st.Pop();
            Show(st.Peek(), -1);
        }

        public void GoTab(string name, FrameworkElement sub = null)
        {
            var rb = name switch { "download" => NavDownload, "settings" => NavSettings, "more" => NavMore, _ => NavLaunch };
            var st = _stacks[rb];
            while (st.Count > 1) st.Pop();
            if (rb.IsChecked == true) Show(st.Peek()); else rb.IsChecked = true;
            if (sub != null) Push(sub);
        }

        /// <summary>命令行 --page download|settings|more|versions|mods|modsearch 直接打开某页（快捷方式 / 测试用）</summary>
        void OpenStartupPage()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--page");
            if (i < 0 || i + 1 >= args.Length) return;
            switch (args[i + 1])
            {
                case "download": GoTab("download"); break;
                case "settings": GoTab("settings"); break;
                case "more": GoTab("more"); break;
                case "versions": Push(new VersionsPage()); break;
                case "instance":
                    var inst = LaunchPage.SelectedInstance();
                    if (inst != null) Push(new InstancePage(inst, i + 2 < args.Length ? args[i + 2] : null));
                    break;
                case "modsearch":
                    GoTab("download");
                    DownloadPage.OpenContent(global::BlockLauncher.Content.ContentType.Mod, LaunchPage.SelectedInstance());
                    break;
            }
        }

        // ---------- 标题栏 ----------
        void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        void Max_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        void Close_Click(object sender, RoutedEventArgs e) => Close();

        void Window_StateChanged(object sender, EventArgs e)
        {
            // 无边框窗口最大化时会溢出屏幕边缘几个像素
            Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        }

        /// <summary>Win11：让窗口用系统圆角（Win10 上静默忽略）</summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33 /* DWMWA_WINDOW_CORNER_PREFERENCE */, ref pref, sizeof(int));
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>右键顶栏弹出系统菜单（Windows 习惯）</summary>
        void Title_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            for (var d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
                if (d is System.Windows.Controls.Primitives.ButtonBase) return;
            SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));
        }

        // ---------- 拖放整合包 ----------
        string _dropPath;
        bool _dropOk;
        string _dropTitle, _dropSub;

        static string FirstDroppedFile(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            return files != null && files.Length > 0 ? files[0] : null;
        }

        /// <summary>
        /// 从拖放源允许的效果里挑一个。写死 Copy 会有坑：源端只给 Move 时，
        /// 系统认为我们要的效果它给不了，整场拖放被判无效，连 Drop 都不会来。
        /// </summary>
        static DragDropEffects PickEffect(DragDropEffects allowed)
        {
            if ((allowed & DragDropEffects.Copy) != 0) return DragDropEffects.Copy;
            if ((allowed & DragDropEffects.Move) != 0) return DragDropEffects.Move;
            if ((allowed & DragDropEffects.Link) != 0) return DragDropEffects.Link;
            return DragDropEffects.None;
        }

        /// <summary>
        /// 这个元素自己显式声明了接受拖放吗？
        /// 注意：AllowDrop 是**继承**属性，窗口上写了 True 之后整棵树读出来都是 True，
        /// 所以不能直接读 ue.AllowDrop，必须看值来源是不是 Local（XAML 里自己写的）。
        /// </summary>
        static bool OwnsAllowDrop(DependencyObject d)
        {
            if (!(d is UIElement ue) || !ue.AllowDrop) return false;
            return DependencyPropertyHelper.GetValueSource(d, UIElement.AllowDropProperty).BaseValueSource
                   == BaseValueSource.Local;
        }

        /// <summary>
        /// 是否落在已经有拖放处理的控件上（Mod 列表 / 文件列表）。
        /// 只用来决定要不要画提示层 —— 真正 "谁处理 Drop" 由冒泡路由决定，
        /// 这里判断错了顶多是提示层不显示，不会再吞掉导入。
        /// </summary>
        bool InsideChildDropTarget(object src, out string hit)
        {
            hit = null;
            var d = src as DependencyObject;
            while (d != null)
            {
                if (ReferenceEquals(d, this) || ReferenceEquals(d, Root)) return false;
                if (OwnsAllowDrop(d))
                {
                    var fe = d as FrameworkElement;
                    hit = (fe != null && !string.IsNullOrEmpty(fe.Name) ? fe.Name : "?") + " (" + d.GetType().Name + ")";
                    return true;
                }
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(d)
                    : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        bool InsideChildDropTarget(object src) => InsideChildDropTarget(src, out _);

        internal static bool ProbePack(string path, out string title, out string sub)
        {
            title = sub = null;
            var file = System.IO.Path.GetFileName(path);
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".mrpack" && ext != ".zip")
            {
                title = "这不是整合包";
                sub = $"{file}\n只支持 Modrinth（.mrpack）和 CurseForge（.zip）格式的整合包。";
                return false;
            }
            try
            {
                var info = BlockLauncher.Content.ModpackImporter.ReadInfo(path);
                title = "松开即可安装：" + (string.IsNullOrWhiteSpace(info.Name) ? System.IO.Path.GetFileNameWithoutExtension(path) : info.Name);
                sub = $"MC {info.McVersion} · {info.LoaderText} · {info.FileCount} 个文件\n会自动装好对应的游戏版本和加载器，再下载整合包里的文件。";
                return true;
            }
            catch (Exception ex)
            {
                // 没有清单但里面是现成的游戏目录（HMCL / 打包机导出的常见形态）
                if (BlockLauncher.Content.GreenPack.LooksLikeGameDir(path))
                {
                    title = "松开即可解压安装：" + System.IO.Path.GetFileNameWithoutExtension(path);
                    sub = $"{file}\n这个包没有 modrinth.index.json / manifest.json，但里面已经带了完整的游戏文件，\n会直接解压成一套独立的游戏目录，不用再下载。";
                    return true;
                }
                title = "无法导入这个文件";
                sub = file + "\n" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 显示提示层：遮罩淡入、卡片从 96% 放大并轻微上移。
        /// 只在"从无到有"这一刻播一次，拖动过程中的高频回调不会反复重播。
        /// </summary>
        void EnsureOverlayVisible()
        {
            if (DropOverlay.Visibility == Visibility.Visible) return;

            DropOverlay.Visibility = Visibility.Visible;
            DropOverlay.Opacity = 0;
            DropOverlay.BeginAnimation(OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                });

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(240);
            DropCardScale.ScaleX = DropCardScale.ScaleY = 0.96;
            DropCardShift.Y = 10;
            DropCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, dur) { EasingFunction = ease });
            DropCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, dur) { EasingFunction = ease });
            DropCardShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, dur) { EasingFunction = ease });
        }

        /// <summary>收起提示层，并把动画残留的状态复位，下次显示才能重新播</summary>
        void HideOverlay()
        {
            if (DropOverlay.Visibility != Visibility.Visible) return;

            DropOverlay.BeginAnimation(OpacityProperty, null);
            DropOverlay.Opacity = 1;
            DropCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            DropCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DropCardShift.BeginAnimation(TranslateTransform.YProperty, null);
            DropCardScale.ScaleX = DropCardScale.ScaleY = 1;
            DropCardShift.Y = 0;
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        /// <summary>按文件更新提示层内容并显示；null 表示隐藏</summary>
        internal void ShowDropState(string path)
        {
            if (path == null)
            {
                HideOverlay();
                _dropPath = null;
                return;
            }

            // 只在文件变化时读一次 zip 头，拖动过程中不做重复解析
            if (!string.Equals(path, _dropPath, StringComparison.OrdinalIgnoreCase))
            {
                _dropPath = path;
                _dropOk = ProbePack(path, out _dropTitle, out _dropSub);
            }

            DropTitle.Text = _dropTitle;
            DropSub.Text = _dropSub;
            DropIcon.Text = _dropOk ? "\uE7B8" : "\uE7BA";
            DropIcon.Foreground = (Brush)FindResource(_dropOk ? "Accent" : "Danger");
            DropFrame.Stroke = (Brush)FindResource(_dropOk ? "Accent" : "Danger");
            EnsureOverlayVisible();
        }

        /// <summary>不是文件的拖放内容（比如从压缩软件窗口里拖出来的虚拟文件）</summary>
        void ShowDropNotice(string title, string sub)
        {
            if (DropOverlay.Visibility == Visibility.Visible
                && _dropPath == null
                && string.Equals(DropTitle.Text, title, StringComparison.Ordinal))
                return; // 文案没变就不重复刷，拖动过程中这个回调调用很频繁

            _dropPath = null;
            _dropOk = false;
            _dropTitle = title;
            _dropSub = sub;
            DropTitle.Text = title;
            DropSub.Text = sub;
            DropIcon.Text = "\uE7BA";
            DropIcon.Foreground = (Brush)FindResource("Danger");
            DropFrame.Stroke = (Brush)FindResource("Danger");
            EnsureOverlayVisible();
        }

        void EvaluateDrag(DragEventArgs e)
        {
            if (InsideChildDropTarget(e.OriginalSource))
            {
                ShowDropState(null);
                // 即使让路，也要回一个可接受的效果，不然系统会把整场拖放判成"这里不收"
                e.Effects = PickEffect(e.AllowedEffects);
                return; // 子控件自己会处理，这里只负责不画提示层
            }

            var path = FirstDroppedFile(e);
            if (path == null)
            {
                // 拿不到路径常见于「从压缩软件的窗口里把文件拖出来」，
                // 这时数据是虚拟文件流、没有真实路径。直接拒绝会让用户以为软件没反应，
                // 所以照样接下这次拖放，松手后明确告诉他该怎么拖。
                ShowDropNotice("拖进来的不是文件本身",
                    "请从资源管理器里直接拖 .mrpack / .zip 文件。\n从压缩软件（WinRAR、7-Zip 等）窗口里拖出来的文件识别不了，先把压缩包另存到硬盘再拖。");
                e.Effects = PickEffect(e.AllowedEffects);
                e.Handled = true;
                return;
            }

            // 重要：不管这个包能不能装，都先把拖放接下来。
            // 目标端一旦回 None，系统会认为此处不接受，Drop 事件不会送达，
            // 用户看到的就是"拖进来毫无反应"。装不了的问题留到 Drop 之后弹提示。
            ShowDropState(path);
            e.Effects = PickEffect(e.AllowedEffects);
            e.Handled = true;
        }

        void Window_PreviewDragEnter(object sender, DragEventArgs e)
        {
            var path = FirstDroppedFile(e);
            var formats = "?";
            try { formats = string.Join(", ", e.Data.GetFormats()); } catch { }
            var child = InsideChildDropTarget(e.OriginalSource, out var hit) ? hit : "无";
            Log.Info($"[拖放] Enter 命中窗口，文件={(path ?? "(无文件路径)")}，AllowedEffects={e.AllowedEffects}，子拖放区={child}，源={e.OriginalSource?.GetType().Name ?? "null"}，格式={formats}");
            EvaluateDrag(e);
        }

        void Window_PreviewDragOver(object sender, DragEventArgs e) => EvaluateDrag(e);

        void Window_PreviewDragLeave(object sender, DragEventArgs e)
        {
            var p = e.GetPosition(this);
            if (p.X < 0 || p.Y < 0 || p.X > ActualWidth || p.Y > ActualHeight)
            {
                Log.Info("[拖放] Leave 窗口");
                HideOverlay();
                _dropPath = null;
            }
        }

        /// <summary>
        /// 冒泡的 Drop：子控件（Mod 列表、文件列表）自己处理并且标记 Handled 后，
        /// 这个处理器就不会被调用 —— 谁处理谁负责，不靠外面猜坐标。
        /// </summary>
        async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Handled) return;

            var path = _dropPath ?? FirstDroppedFile(e);
            // 缓存可能还没被 Enter/Over 填上（有的来源 Enter 直接跟 Drop），这里补一次判定
            if (path != null && !string.Equals(path, _dropPath, StringComparison.OrdinalIgnoreCase))
                _dropOk = ProbePack(path, out _dropTitle, out _dropSub);

            var ok = _dropOk && path != null && System.IO.File.Exists(path);
            var title = _dropTitle;
            var sub = _dropSub;
            HideOverlay();
            _dropPath = null;
            _dropOk = false;

            Log.Info($"[拖放] Drop 收到，path={(path ?? "(空)")}，可安装={ok}，说明={title}");

            e.Handled = true;
            if (path == null)
            {
                var formats = "?";
                try { formats = string.Join(", ", e.Data.GetFormats()); } catch { }
                Log.Warn("[拖放] 拖放数据里没有文件路径，可用格式：" + formats);
                Dialogs.Alert(title ?? "拖进来的不是文件本身", sub ?? "请从资源管理器里直接拖 .mrpack / .zip 文件。");
                return;
            }
            if (!ok)
            {
                Dialogs.Alert(title ?? "无法导入这个文件", sub ?? path);
                return;
            }

            try { await VersionsPage.ImportPackAsync(path, false); }
            catch (Exception ex)
            {
                Log.Error("[拖放] 导入失败", ex);
                Dialogs.Alert("导入整合包失败", ex.GetBaseException().Message);
            }
        }

        // ---------- 任务 ----------
        void Tasks_Click(object sender, RoutedEventArgs e) => ToggleTasks(TaskLayer.Visibility != Visibility.Visible);
        void TaskLayer_MouseDown(object sender, MouseButtonEventArgs e) => ToggleTasks(false);
        void ClearTasks_Click(object sender, RoutedEventArgs e) => TaskManager.ClearFinished();
        void CancelTask_Click(object sender, RoutedEventArgs e) => ((sender as FrameworkElement)?.DataContext as LauncherTask)?.Cancel();

        public void ToggleTasks(bool show)
        {
            NoTasks.Visibility = TaskManager.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            int token = ++_taskAnim;   // 连续点开合时，只有最后一次的收尾回调有效

            if (show)
            {
                TaskLayer.Visibility = Visibility.Visible;
                Motion.SlideFadeIn(TaskDrawer, 46, 0, 210);
                TaskScrim.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(TaskScrim.Opacity, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                return;
            }

            if (TaskLayer.Visibility != Visibility.Visible) return;

            // 收起：抽屉往右退一点 + 淡出，遮罩同时淡掉，结束才真正 Collapsed
            Motion.SlideFadeOut(TaskDrawer, 46, 0, Motion.Fast);
            var fade = new DoubleAnimation(TaskScrim.Opacity, 0, TimeSpan.FromMilliseconds(Motion.Fast));
            fade.Completed += (s, e) =>
            {
                if (token != _taskAnim) return;   // 期间又被打开过，别收
                TaskLayer.Visibility = Visibility.Collapsed;
                Motion.Reset(TaskDrawer);
                TaskScrim.Opacity = 0;
            };
            TaskScrim.BeginAnimation(OpacityProperty, fade);
        }

        void UpdateTaskBadge()
        {
            int running = TaskManager.Tasks.Count(t => t.IsActive);
            TaskCount.Text = running > 0 ? running.ToString() : "";
            NoTasks.Visibility = TaskManager.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void OnTaskFinished(LauncherTask t)
        {
            UpdateTaskBadge();
            if (t.Title.StartsWith("启动 ")) return; // 启动页自己会提示（窗口出现 / 失败对话框）
            if (t.State == TaskState.Done) Toast($"{t.Title}：完成", ToastKind.Ok);
            else if (t.State == TaskState.Failed) Toast($"{t.Title}：失败（点右上角任务查看原因）", ToastKind.Error);
        }

        // ---------- 提示 ----------
        public enum ToastKind { Info, Ok, Error }

        public void Toast(string text, ToastKind kind = ToastKind.Info)
        {
            ToastText.Text = text;
            ToastIcon.Text = kind == ToastKind.Ok ? "" : kind == ToastKind.Error ? "" : "";
            // 底板去掉之后，靠图标颜色区分成功/失败，否则光看文字不够醒目
            if (kind == ToastKind.Info) ToastIcon.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
            else ToastIcon.Foreground = new SolidColorBrush(kind == ToastKind.Ok
                ? Color.FromRgb(0x1E, 0x9E, 0x57) : Color.FromRgb(0xE0, 0x3B, 0x3B));
            ToastBox.Visibility = Visibility.Visible;
            // 从下方 10px 处滑上来，比纯淡入更有"出现"的分量
            Motion.SlideFadeIn(ToastBox, 0, 10, 200);
            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 5 : 3) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                Motion.SlideFadeOut(ToastBox, 0, 6, 180, () => ToastBox.Visibility = Visibility.Collapsed);
            };
            _toastTimer.Start();
        }

        // ---------- 对话框 ----------
        readonly Stack<object> _dialogStack = new Stack<object>();

        internal void ShowDialogContent(FrameworkElement content)
        {
            if (DialogHost.Content != null) _dialogStack.Push(DialogHost.Content);

            bool fresh = DialogLayer.Visibility != Visibility.Visible;
            DialogHost.Content = content;
            DialogLayer.Visibility = Visibility.Visible;

            if (fresh)
                DialogScrim.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(DialogScrim.Opacity, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            // 弹层从 96% 微放大进来：比纯淡入更有"浮上来"的感觉
            Motion.ScaleFadeIn(content, 0.96, 170);
        }

        internal void CloseDialogContent(FrameworkElement content)
        {
            if (DialogHost.Content != content) return;
            if (_dialogStack.Count > 0) { DialogHost.Content = _dialogStack.Pop(); return; }

            DialogHost.Content = null;
            var fade = new DoubleAnimation(DialogScrim.Opacity, 0, TimeSpan.FromMilliseconds(130));
            fade.Completed += (s, e) => DialogLayer.Visibility = Visibility.Collapsed;
            DialogScrim.BeginAnimation(OpacityProperty, fade);
        }

        // ---------- 背景 ----------
        public void ApplyBackground()
        {
            var img = Theme.LoadImage(Settings.BackgroundImage, 1920);
            BgImage.Source = img;
            BgImage.Visibility = img == null ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- 启动时 ----------
        async Task StartupChecksAsync()
        {
            _ = Task.Run(() => Mirrors.ProbeAsync());
            if (Environment.GetCommandLineArgs().Contains("--updated"))
                Toast($"已更新到 {AppInfo.VersionString}", ToastKind.Ok);

            if (Settings.AutoCheckUpdate)
            {
                try
                {
                    await Task.Delay(1500);
                    var info = await Task.Run(() => Updater.CheckAsync());
                    if (info != null && info.IsNewer) await Dialogs.ShowUpdateAsync(info);
                }
                catch (Exception ex) { Log.Warn("自动检查更新失败：" + ex.Message); }
            }
        }
    }

    /// <summary>页面显示时回调（刷新数据用）</summary>
    public interface IPage
    {
        void OnShown();
    }
}
