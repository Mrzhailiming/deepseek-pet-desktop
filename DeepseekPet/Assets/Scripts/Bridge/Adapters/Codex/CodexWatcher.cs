using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 追 Codex 的会话文件（~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl），不需要改 Codex 的任何配置。
    ///
    /// **按文件大小而不是修改时间判断「有没有新内容」**：codex.exe 会一直开着正在写的会话文件，
    /// Windows 上这种文件的 LastWriteTime 不会随写入更新（实测续接昨天的会话写了 2 万字节，
    /// 修改时间还停在昨天），只有长度是准的。
    ///
    /// - 启动时记下所有会话文件的长度，不读内容（历史不回放）；
    /// - 之后哪个文件变长了，就从上次记下的长度开始读；启动后新出现的文件从头读；
    /// - 续接的可能是好几天前的会话，所以要看整棵 sessions 目录：今天 / 昨天的目录每次 Poll 都看，
    ///   整棵树每 FullScanEvery 次 Poll 扫一遍（Poll 默认 500ms 一次 → 2s 一遍）。
    /// 轮询而不是 FileSystemWatcher：Mono 下递归监听不可靠，而且同样拿不到「被一直开着的文件」的写入通知。
    /// Poll 只在一个线程上调（BridgeHub 的轮询定时器）。
    /// </summary>
    public sealed class CodexWatcher
    {
        const int FullScanEvery = 4;

        static readonly Regex SessionIdInName = new Regex(@"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\.jsonl$", RegexOptions.IgnoreCase);

        readonly string _root;
        /// <summary>每个会话文件上次看到的长度。</summary>
        readonly Dictionary<string, long> _sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        /// <summary>正在追的文件。很久没变长的会被收起来（_sizes 里还记着长度）。</summary>
        readonly Dictionary<string, Tracked> _tracked = new Dictionary<string, Tracked>(StringComparer.OrdinalIgnoreCase);
        readonly TimeSpan _idleForget;
        bool _primed;
        int _polls;

        sealed class Tracked
        {
            public JsonlTail Tail;
            public CodexRolloutParser Parser;
            public DateTime LastGrowth;
        }

        public CodexWatcher(string sessionsRoot = null, TimeSpan? idleForget = null)
        {
            _root = sessionsRoot ?? DefaultRoot();
            _idleForget = idleForget ?? TimeSpan.FromMinutes(30);
        }

        public static string DefaultRoot()
        {
            var home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (string.IsNullOrEmpty(home))
            {
                home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            }
            return Path.Combine(home, "sessions");
        }

        public string Root => _root;
        public int TrackedFiles => _tracked.Count;

        /// <summary>扫一遍，返回新读到的事件。第一次调用只记下现有文件的长度，不产出事件。</summary>
        public List<PetEvent> Poll(DateTime nowLocal)
        {
            var events = new List<PetEvent>();
            var full = !_primed || _polls % FullScanEvery == 0;
            _polls++;
            foreach (var path in Candidates(nowLocal, full))
            {
                long size;
                try
                {
                    size = new FileInfo(path).Length;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                var known = _sizes.TryGetValue(path, out var last);
                _sizes[path] = size;
                if (!_primed) continue;
                if (known && size <= last && !_tracked.ContainsKey(path)) continue;

                if (!_tracked.TryGetValue(path, out var tracked))
                {
                    // 新文件从头读（首行 session_meta 里有会话 id）；变长的老文件从上次的长度接着读。
                    var name = Path.GetFileName(path);
                    var m = SessionIdInName.Match(name);
                    tracked = new Tracked
                    {
                        Tail = new JsonlTail(path, known ? last : 0),
                        Parser = new CodexRolloutParser(m.Success ? m.Groups[1].Value : Path.GetFileNameWithoutExtension(name)),
                    };
                    _tracked[path] = tracked;
                }
                var lines = tracked.Tail.ReadNewLines();
                if (lines.Count > 0) tracked.LastGrowth = nowLocal;
                foreach (var line in lines) events.AddRange(tracked.Parser.Parse(line));
            }
            _primed = true;
            Forget(nowLocal);
            return events;
        }

        /// <summary>要看的会话文件：快扫只看今天和昨天的日期目录，全扫看整棵树。</summary>
        IEnumerable<string> Candidates(DateTime nowLocal, bool full)
        {
            var result = new List<string>();
            try
            {
                if (!Directory.Exists(_root)) return result;
                if (full)
                {
                    result.AddRange(Directory.GetFiles(_root, "rollout-*.jsonl", SearchOption.AllDirectories));
                    return result;
                }
                foreach (var day in new[] { nowLocal.Date, nowLocal.Date.AddDays(-1) })
                {
                    var dir = Path.Combine(_root, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
                    if (Directory.Exists(dir)) result.AddRange(Directory.GetFiles(dir, "rollout-*.jsonl"));
                }
                // 正在追的（可能是好几天前续接的）每次都看。
                foreach (var path in _tracked.Keys) if (!result.Contains(path)) result.Add(path);
            }
            catch (IOException)
            {
                // 目录在变：下一轮再说。
            }
            catch (UnauthorizedAccessException)
            {
            }
            return result;
        }

        /// <summary>很久没变长的文件不再每轮都看（会话结束了）；长度还记着，再变长时接着读。</summary>
        void Forget(DateTime nowLocal)
        {
            var stale = new List<string>();
            foreach (var pair in _tracked)
            {
                if (pair.Value.LastGrowth != default && nowLocal - pair.Value.LastGrowth > _idleForget) stale.Add(pair.Key);
                else if (pair.Value.LastGrowth == default) pair.Value.LastGrowth = nowLocal;
            }
            foreach (var path in stale) _tracked.Remove(path);
        }
    }
}
