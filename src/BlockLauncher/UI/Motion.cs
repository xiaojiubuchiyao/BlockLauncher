using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace BlockLauncher.UI
{
    /// <summary>
    /// 全站过渡动效。规则：
    /// 1) 只动 Opacity / TranslateTransform / ScaleTransform，绝不碰 Width、Margin 这类触发布局的属性；
    /// 2) 时长 130~240ms，位移不超过 30px —— 快，但不飘；
    /// 3) 进场用 EaseOut（起步快、收尾稳），退场用 EaseIn，这是"顺滑"的来源；
    /// 4) 成组出现的卡片做 40ms 级联，眼睛能读出层次，但总时长不拖。
    /// </summary>
    public static class Motion
    {
        public const int Fast = 130;
        public const int Normal = 190;
        public const int Slow = 240;

        static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });
        static readonly IEasingFunction EaseIn = Freeze(new CubicEase { EasingMode = EasingMode.EaseIn });
        static readonly IEasingFunction EaseInOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseInOut });

        static IEasingFunction Freeze(Freezable f)
        {
            f.Freeze();
            return (IEasingFunction)f;
        }

        /// <summary>位移 + 淡入。xy 是"从哪个位置来"，配合 EaseOut 就是滑入。</summary>
        public static void SlideFadeIn(FrameworkElement el, double fromX = 0, double fromY = 0,
                                       int duration = Normal, int delay = 0,
                                       IEasingFunction ease = null)
        {
            if (el == null) return;
            var tt = ReuseTranslate(el, fromX, fromY);
            el.BeginAnimation(UIElement.OpacityProperty, null);   // 先摘掉上一轮残留的动画，避免闪一帧全不透明
            el.Opacity = 0;

            var begin = TimeSpan.FromMilliseconds(delay);
            var span = TimeSpan.FromMilliseconds(duration);
            var ez = ease ?? EaseOut;

            el.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, span) { BeginTime = begin, EasingFunction = ez });
            tt.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(fromX, 0, span) { BeginTime = begin, EasingFunction = ez });
            tt.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(fromY, 0, span) { BeginTime = begin, EasingFunction = ez });
        }

        /// <summary>
        /// 尽量复用元素上已有的 TranslateTransform（XAML 里可能已经声明过一个并起了名字），
        /// 直接 new 一个会把原对象顶掉 —— 别处再引用那个名字就会读到永远为 0 的孤立对象。
        /// </summary>
        static TranslateTransform ReuseTranslate(FrameworkElement el, double fromX, double fromY)
        {
            var tt = el.RenderTransform as TranslateTransform;
            if (tt == null && el.RenderTransform is TransformGroup g)
                foreach (var c in g.Children)
                    if (c is TranslateTransform found) { tt = found; break; }

            if (tt == null)
            {
                tt = new TranslateTransform();
                el.RenderTransform = tt;
            }

            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.BeginAnimation(TranslateTransform.YProperty, null);
            tt.X = fromX;
            tt.Y = fromY;
            return tt;
        }

        /// <summary>缩放 + 淡入（对话框、卡片弹层）</summary>
        public static void ScaleFadeIn(FrameworkElement el, double fromScale = 0.96,
                                       int duration = Normal, int delay = 0)
        {
            if (el == null) return;
            var st = new ScaleTransform(fromScale, fromScale);
            el.RenderTransformOrigin = new Point(0.5, 0.5);
            el.RenderTransform = st;
            el.Opacity = 0;

            var begin = TimeSpan.FromMilliseconds(delay);
            var span = TimeSpan.FromMilliseconds(duration);

            el.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, span) { BeginTime = begin, EasingFunction = EaseOut });
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(fromScale, 1, span) { BeginTime = begin, EasingFunction = EaseOut });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(fromScale, 1, span) { BeginTime = begin, EasingFunction = EaseOut });
        }

        /// <summary>淡出并位移，结束后回调（用于抽屉、提示条收起）</summary>
        public static void SlideFadeOut(FrameworkElement el, double toX = 0, double toY = 0,
                                        int duration = Fast, Action done = null)
        {
            if (el == null) { done?.Invoke(); return; }
            var tt = el.RenderTransform as TranslateTransform;
            if (tt == null)
            {
                tt = new TranslateTransform();
                el.RenderTransform = tt;
            }

            var span = TimeSpan.FromMilliseconds(duration);
            var fade = new DoubleAnimation(el.Opacity, 0, span) { EasingFunction = EaseIn, FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (s, e) =>
            {
                Reset(el);
                done?.Invoke();
            };
            el.BeginAnimation(UIElement.OpacityProperty, fade);
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(tt.X, toX, span) { EasingFunction = EaseIn });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(tt.Y, toY, span) { EasingFunction = EaseIn });
        }

        /// <summary>停掉动画并把不透明度 / 位移复位，保证下次进场还是从头播</summary>
        public static void Reset(FrameworkElement el)
        {
            if (el == null) return;
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.Opacity = 1;
            if (el.RenderTransform is TranslateTransform tt)
            {
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.BeginAnimation(TranslateTransform.YProperty, null);
                tt.X = 0;
                tt.Y = 0;
            }
        }

        /// <summary>按住时的轻微缩小：0.97，松手回弹。手感上的"按到了"主要靠它。</summary>
        public static void Press(FrameworkElement el)
        {
            if (el == null) return;
            var st = el.RenderTransform as ScaleTransform;
            if (st == null)
            {
                st = new ScaleTransform(1, 1);
                el.RenderTransformOrigin = new Point(0.5, 0.5);
                el.RenderTransform = st;
            }
            var span = TimeSpan.FromMilliseconds(90);
            st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, span) { EasingFunction = EaseOut });
            st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, span) { EasingFunction = EaseOut });
        }

        public static void Release(FrameworkElement el)
        {
            if (el?.RenderTransform is ScaleTransform st)
            {
                var span = TimeSpan.FromMilliseconds(160);
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, span) { EasingFunction = EaseOut });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, span) { EasingFunction = EaseOut });
            }
        }

        /// <summary>
        /// 页面内容级联入场：找到页面里第一个滚动容器，把它里面的一级卡片依次淡入。
        /// 最多 5 项、步进 40ms —— 再多就变成等动画，反而慢。
        /// </summary>
        public static void StaggerCards(DependencyObject page, int step = 40, int max = 5)
        {
            var panel = FindScrollContent(page);
            if (panel == null) return;

            int i = 0;
            foreach (UIElement child in panel.Children)
            {
                var fe = child as FrameworkElement;
                if (fe == null || fe.Visibility != Visibility.Visible) continue;
                if (!(fe is Border) && !(fe is Grid) && !(fe is StackPanel)) continue;
                if (fe.ActualHeight <= 0) continue;

                SlideFadeIn(fe, 0, 12, Normal, i * step);
                if (++i >= max) break;
            }
            if (i > 0) Core.Log.Info($"[动效] 级联入场 {i} 项");
        }

        static Panel FindScrollContent(DependencyObject root)
        {
            if (root == null) return null;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer sv && sv.Content is Panel p && p.Children.Count > 0)
                    return p;
                var found = FindScrollContent(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
