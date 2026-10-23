using System;
using System.Collections.Generic;

namespace DeepseekPet.Core
{
    /// <summary>时钟：epoch 毫秒。测试里换成手拨的假钟。</summary>
    public interface IClock
    {
        long Now { get; }
    }

    public sealed class SystemClock : IClock
    {
        public long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>[0, 1) 的随机数（JS 的 Math.random）。只用在宠物 id、蛋 id、彩虹蛋的种族上。</summary>
    public interface IRandom
    {
        double Next();
    }

    /// <summary>mulberry32：和 PetTools 注入 Jint 的 Math.random 是同一个算法，种子相同序列就相同。</summary>
    public sealed class Mulberry32 : IRandom
    {
        uint _state;

        public Mulberry32(uint seed)
        {
            _state = seed;
        }

        public static Mulberry32 FromTime()
        {
            return new Mulberry32(unchecked((uint)DateTime.UtcNow.Ticks));
        }

        public double Next()
        {
            unchecked
            {
                _state += 0x6D2B79F5;
                var t = _state;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    /// <summary>
    /// 存档的读写（JS 里的 localStorage["dsh-pet-plugin/state"]）。内容是 JSON 字符串，格式与网页版相同。
    /// Unity 侧的实现负责原子写和备份。
    /// </summary>
    public interface IPetStorage
    {
        /// <summary>没有存档时返回 null。</summary>
        string Read();
        void Write(string json);
    }

    /// <summary>存在内存里的存档（测试 / 关了持久化时用）。</summary>
    public sealed class MemoryStorage : IPetStorage
    {
        public string Content;

        public MemoryStorage(string content = null)
        {
            Content = content;
        }

        public string Read()
        {
            return Content;
        }

        public void Write(string json)
        {
            Content = json;
        }
    }

    /// <summary>
    /// JS 的 setTimeout，但不自己跑：由 PetCore.Update(now) 把到期的按（到期时刻, 序号）依次触发，
    /// 触发时「当前时刻」就是该定时器的到期时刻 —— 和 PetTools 里给 Jint 的假定时器同一个规矩，两边才可比。
    /// </summary>
    internal sealed class TimerQueue
    {
        struct Entry
        {
            public int Id;
            public long At;
            public Action Run;
        }

        readonly List<Entry> _entries = new List<Entry>();
        int _seq;

        /// <summary>正在触发的定时器的到期时刻；不在触发中时为 null。</summary>
        public long? FiringAt { get; private set; }

        public int Schedule(long now, long delayMs, Action run)
        {
            _seq += 1;
            _entries.Add(new Entry { Id = _seq, At = now + (delayMs > 0 ? delayMs : 0), Run = run });
            return _seq;
        }

        public void Cancel(int id)
        {
            if (id == 0) return;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == id)
                {
                    _entries.RemoveAt(i);
                    return;
                }
            }
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public void RunDue(long now)
        {
            for (;;)
            {
                var at = -1;
                for (var i = 0; i < _entries.Count; i++)
                {
                    var e = _entries[i];
                    if (e.At > now) continue;
                    if (at < 0 || e.At < _entries[at].At || (e.At == _entries[at].At && e.Id < _entries[at].Id)) at = i;
                }
                if (at < 0) break;
                var due = _entries[at];
                _entries.RemoveAt(at);
                FiringAt = due.At;
                try
                {
                    due.Run();
                }
                finally
                {
                    FiringAt = null;
                }
            }
        }
    }
}
