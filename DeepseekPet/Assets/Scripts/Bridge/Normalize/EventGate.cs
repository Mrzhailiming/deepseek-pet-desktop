using System.Collections.Generic;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 喂食闸门，照抄 dsh 插件 store.js 的 createFeedGate：
    ///   1. 只放「刚发生」的事件：now - ts 超过 freshnessMs 就丢（重启后补读、翻历史时不会把宠物撑爆）；
    ///   2. 同一个 id 只放一次。
    /// 去重表是两代交替的集合：每过一个新鲜度窗口换一代，旧的那代整个扔掉 ——
    /// 超出窗口的事件本来就过不了第 1 关，不用再记着它们。
    /// 线程安全：HTTP 线程和 Codex 轮询线程都会调。
    /// </summary>
    public sealed class EventGate
    {
        readonly long _freshnessMs;
        readonly object _lock = new object();
        HashSet<string> _current = new HashSet<string>();
        HashSet<string> _previous = new HashSet<string>();
        long _generationStartedAt = long.MinValue;

        public EventGate(long freshnessMs = 30000)
        {
            _freshnessMs = freshnessMs;
        }

        /// <summary>这条事件该不该放进去。放了就记下它的 id。</summary>
        public bool Admit(PetEvent e, long now)
        {
            if (e == null || string.IsNullOrEmpty(e.Id)) return false;
            if (now - e.Ts > _freshnessMs) return false;
            lock (_lock)
            {
                if (_generationStartedAt == long.MinValue) _generationStartedAt = now;
                if (now - _generationStartedAt > _freshnessMs)
                {
                    _previous = _current;
                    _current = new HashSet<string>();
                    _generationStartedAt = now;
                }
                if (_current.Contains(e.Id) || _previous.Contains(e.Id)) return false;
                _current.Add(e.Id);
                return true;
            }
        }
    }
}
