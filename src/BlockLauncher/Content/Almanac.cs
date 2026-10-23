using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BlockLauncher.Core;

namespace BlockLauncher.Content
{
    /// <summary>黄历里的一条「宜」或「忌」</summary>
    public class AlmanacEntry
    {
        /// <summary>做的事情，如「探索下界要塞」</summary>
        public string Title { get; set; }
        /// <summary>结果说明，如「会发现许多地狱疣」</summary>
        public string Text { get; set; }
    }

    public class AlmanacResult
    {
        /// <summary>「10 月 23 日 星期五」</summary>
        public string DayText { get; set; }
        public List<AlmanacEntry> Good { get; set; } = new List<AlmanacEntry>();
        public List<AlmanacEntry> Bad { get; set; } = new List<AlmanacEntry>();
        /// <summary>「今天 16:09 更新」</summary>
        public string Stamp { get; set; }
        /// <summary>false = 这次没连上，内容是上次缓存</summary>
        public bool Fresh { get; set; }
        public bool Empty => Good.Count == 0 && Bad.Count == 0;
        public DateTime FetchedAt { get; set; }
    }

    /// <summary>
    /// 我的世界黄历：抓 MC百科的 /tools/almanacs/，解析「宜 / 忌」两组。
    /// 那个页面是 PHP 服务端直出的静态 HTML（没有 JSON 接口），所以这里按结构解析。
    /// 缓存 3 小时：一是省流量，二是 MC百科自己说同一页上午下午可能不一样，
    /// 缓存太久会看到一天前的内容，太短又每次进页面都发请求。
    /// </summary>
    public static class Almanac
    {
        public const string PageUrl = "https://www.mcmod.cn/tools/almanacs/";

        static readonly TimeSpan MaxAge = TimeSpan.FromHours(3);

        static string CachePath => Path.Combine(Paths.Cache, "almanac.json");

        // <div class="title">探索下界要塞</div>  <div class="text">会发现许多地狱疣</div>
        static readonly Regex EntryRx = new Regex(
            "<div class=\"title\">(?<t>.*?)</div>\\s*<div class=\"text\">(?<x>.*?)</div>",
            RegexOptions.Singleline | RegexOptions.Compiled);

        static readonly Regex DateRx = new Regex(
            @"今天是：\s*(?<y>\d{4})\s*年\s*(?<m>\d{1,2})\s*月\s*(?<d>\d{1,2})\s*日",
            RegexOptions.Compiled);

        static readonly Regex WeekRx = new Regex(@"(星期[一二三四五六日天])", RegexOptions.Compiled);

        /// <param name="force">忽略缓存，强制联网</param>
        public static async Task<AlmanacResult> GetAsync(bool force = false, CancellationToken ct = default)
        {
            if (!force)
            {
                var cached = ReadCache();
                if (cached != null && IsFresh(cached)) return cached;
            }

            try
            {
                // mirror=false：mcmod.cn 不在镜像表里，直连就是最快的那条
                var html = await Http.GetStringAsync(PageUrl, ct, null, false, 15, LooksLikeAlmanac).ConfigureAwait(false);
                var result = Parse(html);
                if (result == null || result.Empty) throw new FormatException("页面结构变了，没找到宜忌条目");

                result.FetchedAt = DateTime.Now;
                result.Fresh = true;
                result.Stamp = StampOf(result.FetchedAt);
                Save(result);
                return result;
            }
            catch (Exception ex)
            {
                Log.Warn("黄历获取失败：" + ex.Message);
                var stale = ReadCache();
                if (stale != null)
                {
                    // 保留原来的「今天 16:45 更新」，由界面另起一行说明这是旧内容
                    stale.Fresh = false;
                    return stale;
                }
                return new AlmanacResult { Fresh = false, Stamp = "暂时没连上" };
            }
        }

        static bool LooksLikeAlmanac(string html)
            => html != null && html.IndexOf("almanacs_main", StringComparison.OrdinalIgnoreCase) >= 0;

        static bool IsFresh(AlmanacResult r)
            => r.FetchedAt.Date == DateTime.Today && DateTime.Now - r.FetchedAt < MaxAge;

        // ---------- 解析 ----------
        static AlmanacResult Parse(string html)
        {
            var main = Slice(html, "almanacs_main", "ClassBlock_2") ?? html;
            var goodBlock = Slice(main, "class=\"good\"", "class=\"bad\"");
            var badBlock = Slice(main, "class=\"bad\"", null);

            var result = new AlmanacResult
            {
                DayText = DayTextOf(main),
                Good = EntriesOf(goodBlock),
                Bad = EntriesOf(badBlock),
            };
            return result;
        }

        /// <summary>截取 from 与 to 之间的片段；to 为 null 时取到末尾（最多 8000 字符）</summary>
        static string Slice(string s, string from, string to)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = s.IndexOf(from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            int end;
            if (to == null) end = Math.Min(s.Length, i + 8000);
            else
            {
                int j = s.IndexOf(to, i + from.Length, StringComparison.OrdinalIgnoreCase);
                end = j < 0 ? Math.Min(s.Length, i + 8000) : j;
            }
            return s.Substring(i, end - i);
        }

        static List<AlmanacEntry> EntriesOf(string block)
        {
            var list = new List<AlmanacEntry>();
            if (string.IsNullOrEmpty(block)) return list;
            foreach (Match m in EntryRx.Matches(block))
            {
                var title = Clean(m.Groups["t"].Value);
                var text = Clean(m.Groups["x"].Value);
                if (string.IsNullOrEmpty(title)) continue;
                list.Add(new AlmanacEntry { Title = title, Text = text });
                if (list.Count >= 6) break;   // 站点一般给 4 条，多留两条余量
            }
            return list;
        }

        static string DayTextOf(string main)
        {
            var m = DateRx.Match(main);
            var w = WeekRx.Match(main);
            string week = w.Success ? " " + w.Groups[1].Value : "";
            if (m.Success)
            {
                var mo = int.Parse(m.Groups["m"].Value);
                var d = int.Parse(m.Groups["d"].Value);
                return $"{mo} 月 {d} 日" + week;
            }
            return DateTime.Now.ToString("M 月 d 日") + week;
        }

        /// <summary>去标签 + 解实体 + 压空白</summary>
        static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = Regex.Replace(s, "<br\\s*/?>", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<[^>]+>", "");
            s = WebUtility.HtmlDecode(s);
            return Regex.Replace(s, "\\s+", " ").Trim();
        }

        // ---------- 缓存 ----------
        static string StampOf(DateTime at)
        {
            var day = at.Date == DateTime.Today ? "今天" : at.ToString("MM-dd");
            return day + " " + at.ToString("HH:mm") + " 更新";
        }

        static void Save(AlmanacResult r)
        {
            try
            {
                JNode.Obj()
                    .Set("fetched", r.FetchedAt.ToString("yyyy-MM-dd HH:mm:ss"))
                    .Set("day", r.DayText)
                    .Set("good", ArrOf(r.Good))
                    .Set("bad", ArrOf(r.Bad))
                    .SaveFile(CachePath);
            }
            catch (Exception ex) { Log.Warn("黄历写入缓存失败：" + ex.Message); }
        }

        static JNode ArrOf(List<AlmanacEntry> list)
        {
            var arr = JNode.Arr();
            foreach (var e in list) arr.Add(JNode.Obj().Set("title", e.Title).Set("text", e.Text));
            return arr;
        }

        static AlmanacResult ReadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var node = JNode.Parse(File.ReadAllText(CachePath));
                if (!DateTime.TryParse(node["fetched"].Str, out var at)) return null;
                var r = new AlmanacResult
                {
                    FetchedAt = at,
                    DayText = node["day"].Str,
                    Good = ReadEntries(node["good"]),
                    Bad = ReadEntries(node["bad"]),
                    // 缓存里的内容本身是有效的：Fresh 表示「数据可用」，不是「这次联网成功」。
                    // 只有真正联网失败回落到旧缓存时，才由 GetAsync 把它改成 false。
                    Fresh = true,
                    Stamp = StampOf(at),
                };
                return r.Empty ? null : r;
            }
            catch { return null; }
        }

        static List<AlmanacEntry> ReadEntries(JNode arr)
        {
            var list = new List<AlmanacEntry>();
            foreach (var n in arr)
                list.Add(new AlmanacEntry { Title = n["title"].Str, Text = n["text"].Str });
            return list;
        }
    }
}
