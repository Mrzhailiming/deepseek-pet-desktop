using System;
using System.Collections.Generic;

namespace DeepseekPet.Core
{
    /// <summary>
    /// 养成逻辑的唯一真相源：原 JS 的 createPetStore（dsh-pet-plugin/lib/src/store.js）逐行移植，去掉了网页专属的部分
    /// （拖拽、面板开关、名片画布、头像画法、跨标签页同步）。
    ///
    /// 用法：
    ///   - 每帧（或至少每秒）调一次 <see cref="Update"/>：定时器（气泡到点撤掉、连击超时、合并写存档 …）在这里跑；
    ///   - 每 10 秒调一次 <see cref="Tick"/>：饥饿 / 心情 / 精力随时间走、零食回格、睡着 / 小动作；
    ///   - 桥来的事件交给 <see cref="HandleEvent"/>；摸头 / 零食 / 小游戏 / 多宠物操作各有方法；
    ///   - 读 <see cref="State"/>，订阅 <see cref="Changed"/>。
    /// 不是线程安全的：都在主线程上调。
    ///
    /// 和 JS 的行为由金样轨迹逐字段比对（BridgeTests/Core.Tests），改规则请先改 JS 再重新生成轨迹 —— 或者干脆宣布
    /// C# 成为规格、删掉轨迹测试，二选一，别让两边悄悄分叉。
    /// </summary>
    public sealed partial class PetCore : IDisposable
    {
        readonly PetData _data;
        readonly PetConfig _config;
        readonly TimeZoneInfo _zone;
        readonly IClock _clock;
        readonly IRandom _random;
        readonly IPetStorage _storage;
        readonly PetSave _save;
        readonly TimerQueue _timers = new TimerQueue();

        // ─── 连击与特效 ───
        int _comboCount;
        long _comboLastTime;
        int _comboTimer;
        int _effectSeq;
        int _buffTimer;

        // ─── 数值结算（饥饿 / 心情 / 精力） ───
        long _lastRegenAt;
        double _hungerCarry;
        double _moodCarry;
        double _energyCarry;
        Dictionary<string, double> _dimCarry = NewDimCarry();
        long _snackAt;

        // ─── 气泡与台词 ───
        long _lastBubbleAt;
        int _bubbleTimer;
        long _bubbleSeq;
        bool _lastBubbleDim;
        Dictionary<string, int> _lineAt = new Dictionary<string, int>();

        // ─── 互动（摸头 / 小动作） ───
        int _lastActTimer;
        long _patAt;
        int _patRun;
        long _idleAt;
        long _idleSeq;
        int _idleTimer;
        int _idleLast = -1;

        // ─── 食性偏好 ───
        string _tasteSource;
        int _tasteCount;

        // ─── 提示与关怀 ───
        long _adviceAt;
        Dictionary<string, long> _sessionEdits = new Dictionary<string, long>();
        long _errorStreak;
        string _lastErrorTool;
        long _toolFailStreak;
        long _careAt;
        long _activeSince;
        bool _marathonSaid;
        long _comebackMs;

        // ─── 工具观察 ───
        readonly List<string> _callOrder = new List<string>();
        readonly Dictionary<string, string> _callNames = new Dictionary<string, string>();
        string _lastToolName;
        string _lastSkillUp;

        // ─── 微游戏 ───
        long _miniGameAt;

        // ─── 多宠物集合 ───
        PetCollection _collection;
        string _activePetId;
        List<string> _favorites;
        List<Egg> _eggs;
        GlobalStats _globalStats;
        /// <summary>开机 / 换宠物那一刻的档案对象（JS 的 activeRecord；之后的进度在 state 里，存档时才写回集合）。</summary>
        PetRecord _activeRecord;
        readonly long _bootAt;

        // ─── 存档 ───
        int _persistTimer;
        string _lastBody = "";

        PetState _state;
        bool _disposed;

        /// <summary>当前快照（引用稳定，直到下一次变化）。</summary>
        public PetState State => _state;

        /// <summary>每次 State 换成新的一份之后触发。</summary>
        public event Action Changed;

        public PetData Data => _data;
        public PetConfig Config => _config;
        public TimeZoneInfo Zone => _zone;

        /// <param name="storage">存档；null = 不落盘（等同 persist: false 但仍按配置判断）。</param>
        /// <param name="zone">「本地」时区（按它切日、算几点）；默认本机时区。</param>
        public PetCore(PetData data, PetConfig config, IClock clock = null, IRandom random = null, IPetStorage storage = null, TimeZoneInfo zone = null)
        {
            _data = data;
            _config = config;
            _clock = clock ?? new SystemClock();
            _random = random ?? Mulberry32.FromTime();
            _storage = storage ?? new MemoryStorage();
            _zone = zone ?? TimeZoneInfo.Local;
            _save = new PetSave(data, config, _zone, _random);

            var loaded = _save.Load(_config.Persist ? _storage.Read() : null, Now);
            _collection = loaded.Pets;
            _activePetId = loaded.ActivePetId;
            _favorites = loaded.Favorites;
            _eggs = loaded.Eggs;
            _globalStats = loaded.Global;
            _activeRecord = _collection[_activePetId];
            _bootAt = Now;
            _state = StateFromRecord(_activeRecord);

            // 离线期间也会饿：把结算起点摆在存档时刻（最多往回 offlineRegenCapMs），剩下的交给 settleVitals。
            var savedAt = loaded.SavedAt;
            if (savedAt > 0)
            {
                var offlineFrom = Math.Max(Math.Min(savedAt, _bootAt), _bootAt - _config.OfflineRegenCapMs);
                _lastRegenAt = offlineFrom;
                _snackAt = offlineFrom;
                _state.Asleep = ShouldSleep(_bootAt, _state.Pet);
                _state.Pet = SettleVitals(_bootAt);
                var away = _bootAt - Math.Min(savedAt, _bootAt);
                if (_config.OfflineSelfCare && away >= _config.SleepAfterMs)
                {
                    // 离线自理：宠物自己觅食、自己调整情绪，回来时数值不至于崩塌。
                    var pet = _state.Pet;
                    var hunger = pet.Hunger > _config.OfflineHungerCap ? _config.OfflineHungerCap : pet.Hunger;
                    var mood = pet.Mood < _config.OfflineMoodFloor ? _config.OfflineMoodFloor : pet.Mood;
                    if (hunger != pet.Hunger || mood != pet.Mood)
                    {
                        var next = pet.Clone();
                        next.Hunger = hunger;
                        next.Mood = mood;
                        _state.Pet = next;
                    }
                }
                _state.Snacks = SettleSnacks(_bootAt);
                _state.Daily = SettleDaily(_bootAt);
                if (away >= _config.CareComebackMs) _comebackMs = away;
            }
        }

        /// <summary>「现在」：定时器触发期间是它的到期时刻，否则是时钟。</summary>
        long Now => _timers.FiringAt ?? _clock.Now;

        static Dictionary<string, double> NewDimCarry()
        {
            return new Dictionary<string, double> { ["curiosity"] = 0, ["pride"] = 0, ["concern"] = 0 };
        }

        /// <summary>把到期的定时器跑掉。每帧调一次。</summary>
        public void Update()
        {
            if (_disposed) return;
            _timers.RunDue(_clock.Now);
        }

        int SetTimeout(long delayMs, Action run)
        {
            return _timers.Schedule(Now, delayMs, run);
        }

        void ClearTimeout(int id)
        {
            _timers.Cancel(id);
        }

        // ─── 档案 ⇄ 状态 ───

        PetStats CreatePet(PetRecord rec)
        {
            var saved = rec.Pet;
            return new PetStats
            {
                Name = rec.Name,
                Species = _data.SpeciesByKey.TryGetValue(rec.Species ?? "", out var sp) ? sp.Label : rec.Species,
                Avatar = rec.Avatar,
                Icon = rec.Icon,
                Mood = saved.Mood,
                Hunger = saved.Hunger,
                Energy = saved.Energy,
                Curiosity = saved.Curiosity,
                Pride = saved.Pride,
                Concern = saved.Concern,
                Exp = saved.Exp,
                Level = saved.Level,
                Form = saved.Form,
            };
        }

        PetState StateFromRecord(PetRecord rec)
        {
            _data.SpeciesByKey.TryGetValue(rec.Species ?? "", out var sp);
            return new PetState
            {
                Pet = CreatePet(rec),
                Effects = new List<PetEffect>(),
                ComboCount = 0,
                ComboMultiplier = 1,
                ComboTier = "normal",
                EatKey = 0,
                PatKey = 0,
                LastAct = null,
                IdleAct = null,
                TotalFeeds = rec.TotalFeeds,
                TotalTokens = rec.TotalTokens,
                TokensBySource = rec.TokensBySource,
                Snacks = rec.Snacks,
                Achievements = rec.Achievements,
                Daily = rec.Daily,
                StreakDay = rec.StreakDay,
                StreakCount = rec.StreakCount,
                Pats = rec.Pats,
                Skills = PetRules.CreateSkills(_data, rec.Skills),
                Memory = rec.Memory,
                Pos = rec.Pos,
                LastFeedAt = rec.LastFeedAt == 0 ? _bootAt : Math.Min(rec.LastFeedAt, _bootAt),
                Asleep = false,
                Bubble = null,
                Buff = null,
                ActivePetId = _activePetId,
                PetCount = _collection.Count,
                Favorites = new List<string>(_favorites),
                Eggs = new List<Egg>(_eggs),
                PetOrder = PetRules.ComputePetOrder(_collection, _favorites),
                HatchingEgg = null,
                MiniGame = null,
                Talent = sp?.Talent ?? "",
                Prestige = rec.Prestige,
                FormsOwned = rec.FormsOwned != null ? new List<string>(rec.FormsOwned) : new List<string>(),
            };
        }

        PetRecord ExtractRecordFromState()
        {
            var pet = _state.Pet;
            var stored = _collection[_activePetId];
            return new PetRecord
            {
                Id = _activePetId,
                Species = stored.Species,
                Name = pet.Name,
                Avatar = stored.Avatar,
                Icon = pet.Icon,
                BornAt = stored.BornAt,
                Pet = ProgressOf(pet),
                TotalFeeds = _state.TotalFeeds,
                TotalTokens = _state.TotalTokens,
                TokensBySource = _state.TokensBySource,
                Snacks = _state.Snacks,
                Achievements = _state.Achievements,
                Daily = _state.Daily,
                StreakDay = _state.StreakDay,
                StreakCount = _state.StreakCount,
                Pats = _state.Pats,
                Pos = _state.Pos,
                LastFeedAt = _state.LastFeedAt,
                Skills = _state.Skills,
                Memory = _state.Memory,
                Prestige = _state.Prestige,
                FormsOwned = _state.FormsOwned,
            };
        }

        /// <summary>只留进度字段的一份（名字 / 种族归档案管）。</summary>
        static PetStats ProgressOf(PetStats pet)
        {
            return new PetStats
            {
                Hunger = pet.Hunger, Exp = pet.Exp, Level = pet.Level, Mood = pet.Mood, Energy = pet.Energy,
                Curiosity = pet.Curiosity, Pride = pet.Pride, Concern = pet.Concern, Form = pet.Form,
            };
        }

        void SaveActiveToCollection()
        {
            _collection[_activePetId] = ExtractRecordFromState();
        }

        void ResetTransientState()
        {
            _comboCount = 0;
            _comboLastTime = 0;
            // 和 JS 一样只丢句柄、不撤定时器：旧的连击超时到点还会跑一次（对新宠物来说就是把连击清零）。
            _comboTimer = 0;
            _lastRegenAt = 0;
            _hungerCarry = 0;
            _moodCarry = 0;
            _energyCarry = 0;
            _dimCarry = NewDimCarry();
            _snackAt = 0;
            _lastBubbleAt = 0;
            if (_bubbleTimer != 0) ClearTimeout(_bubbleTimer);
            _bubbleTimer = 0;
            _bubbleSeq = 0;
            _lastBubbleDim = false;
            _lineAt = new Dictionary<string, int>();
            if (_lastActTimer != 0) ClearTimeout(_lastActTimer);
            _lastActTimer = 0;
            _patAt = 0;
            _patRun = 0;
            _idleAt = 0;
            _idleSeq = 0;
            if (_idleTimer != 0) ClearTimeout(_idleTimer);
            _idleTimer = 0;
            _idleLast = -1;
            if (_buffTimer != 0) ClearTimeout(_buffTimer);
            _buffTimer = 0;
            _tasteSource = null;
            _tasteCount = 0;
            _adviceAt = 0;
            _sessionEdits = new Dictionary<string, long>();
            _errorStreak = 0;
            _lastErrorTool = null;
            _toolFailStreak = 0;
            _careAt = 0;
            _activeSince = 0;
            _marathonSaid = false;
            _comebackMs = 0;
            _lastToolName = null;
            _lastSkillUp = null;
            _miniGameAt = 0;
        }

        void ScheduleLastActClear(long delayMs)
        {
            if (_lastActTimer != 0) ClearTimeout(_lastActTimer);
            _lastActTimer = SetTimeout(delayMs, () =>
            {
                _lastActTimer = 0;
                if (_state.LastAct != null) Commit(new Patch { LastAct = { Has = true, Value = null } }, true);
            });
        }

        // ─── 存档（createPersistenceV2）───

        void SchedulePersist()
        {
            if (!_config.Persist || _persistTimer != 0) return;
            _persistTimer = SetTimeout(_config.SaveDebounceMs, () =>
            {
                _persistTimer = 0;
                WriteSave();
            });
        }

        void WriteSave()
        {
            if (_persistTimer != 0)
            {
                ClearTimeout(_persistTimer);
                _persistTimer = 0;
            }
            SaveActiveToCollection();
            var body = _save.Body(new PetSaveV2
            {
                ActivePetId = _activePetId,
                Favorites = _favorites,
                Pets = _collection,
                Eggs = _eggs,
                Global = _globalStats,
            });
            var fingerprint = body.ToString(Newtonsoft.Json.Formatting.None);
            if (fingerprint == _lastBody) return;
            try
            {
                _storage.Write(_save.Serialize(body, Now));
                _lastBody = fingerprint;
            }
            catch (System.IO.IOException)
            {
                // 磁盘满 / 被占用：宠物照样能玩，只是这一次没存下来，下次变化时再试。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        /// <summary>立刻落盘（退出前调）。内容没变就不写。</summary>
        public void Flush()
        {
            if (_config.Persist) WriteSave();
        }

        /// <summary>落盘并停掉所有定时器。之后这个实例不再变化。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            if (_comboTimer != 0) ClearTimeout(_comboTimer);
            _comboTimer = 0;
            if (_bubbleTimer != 0) ClearTimeout(_bubbleTimer);
            _bubbleTimer = 0;
            if (_buffTimer != 0) ClearTimeout(_buffTimer);
            _buffTimer = 0;
            if (_idleTimer != 0) ClearTimeout(_idleTimer);
            _idleTimer = 0;
            if (_lastActTimer != 0) ClearTimeout(_lastActTimer);
            _lastActTimer = 0;
            Flush();
            _timers.Clear();
            _persistTimer = 0;
            _disposed = true;
            Changed = null;
        }

        /// <summary>发布一次新的顶层状态。</summary>
        void Commit(Patch patch, bool skipSave = false)
        {
            _state = patch.ApplyTo(_state);
            if (!skipSave) SchedulePersist();
            Changed?.Invoke();
        }

        /// <summary>整份换掉状态（换宠物 / 孵蛋 / 改名），并安排一次存档。</summary>
        void Replace(PetState next)
        {
            _state = next;
            SchedulePersist();
            Changed?.Invoke();
        }

        // ─── 随时间走的数值 ───

        /// <summary>
        /// 结算「从上次结算到现在」的饥饿 / 心情 / 精力 / 情绪三维，返回结算后的 pet（没变是原引用）。
        /// 惰性结算：都走「取整 + 留小数余额」，三条共用一个锚点 _lastRegenAt。
        /// </summary>
        PetStats SettleVitals(long now)
        {
            if (_lastRegenAt == 0)
            {
                _lastRegenAt = now;
                return _state.Pet;
            }
            var minutes = (now - _lastRegenAt) / 60000.0;
            _lastRegenAt = now;
            if (minutes <= 0) return _state.Pet;
            var pet = _state.Pet;
            var hunger = pet.Hunger;
            var mood = pet.Mood;
            var energy = pet.Energy;
            if (_config.HungerRegenPerMin > 0)
            {
                var hungerRate = _state.Asleep ? _config.HungerRegenPerMin * _config.SleepHungerFactor : _config.HungerRegenPerMin;
                var sp = SpeciesOfActive();
                if (sp != null && sp.HungerBonus > 0) hungerRate *= 1 - sp.HungerBonus;
                _hungerCarry += minutes * hungerRate;
                var whole = Math.Floor(_hungerCarry);
                if (whole >= 1)
                {
                    _hungerCarry -= whole;
                    hunger = (int)Math.Min(100, hunger + whole);
                }
            }
            if (_config.VitalsEnabled)
            {
                // 心情按「结算前」的饥饿判断饿不饿：这一段空闲里它就是这么饿着的。
                var moodRate = _config.MoodDecayPerMin
                    + (pet.Hunger >= _config.HungryAt ? _config.MoodDropPerMinHungry : 0)
                    + (pet.Hunger >= 100 ? _config.MoodCrashPerMinStarving : 0);
                _moodCarry += minutes * moodRate;
                var moodWhole = Math.Floor(_moodCarry);
                if (moodWhole >= 1)
                {
                    _moodCarry -= moodWhole;
                    mood = (int)Math.Max(0, mood - moodWhole);
                }
                var energyRate = _config.EnergyRegenPerMin * (_state.Asleep ? _config.EnergySleepFactor : 1);
                _energyCarry += minutes * energyRate;
                var energyWhole = Math.Floor(_energyCarry);
                if (energyWhole >= 1)
                {
                    _energyCarry -= energyWhole;
                    energy = (int)Math.Min(100, energy + energyWhole);
                }
            }
            Dictionary<string, int> dims = null;
            if (_config.MoodDimsEnabled && _config.MoodDimDecayPerMin > 0)
            {
                foreach (var dim in _data.MoodDims)
                {
                    var key = dim.Key;
                    var current = pet.Dim(key);
                    if (current <= 0)
                    {
                        _dimCarry[key] = 0;
                        continue;
                    }
                    _dimCarry[key] += minutes * _config.MoodDimDecayPerMin;
                    var whole = Math.Floor(_dimCarry[key]);
                    if (whole < 1) continue;
                    _dimCarry[key] -= whole;
                    var value = (int)Math.Max(0, current - whole);
                    if (value == current) continue;
                    if (dims == null) dims = new Dictionary<string, int>();
                    dims[key] = value;
                }
            }
            if (hunger == pet.Hunger && mood == pet.Mood && energy == pet.Energy && dims == null) return pet;
            var next = pet.Clone();
            next.Hunger = hunger;
            next.Mood = mood;
            next.Energy = energy;
            if (dims != null)
            {
                foreach (var pair in dims) next.SetDim(pair.Key, pair.Value);
            }
            return next;
        }

        SpeciesDef SpeciesOfActive()
        {
            var species = _collection[_activePetId]?.Species;
            return species != null && _data.SpeciesByKey.TryGetValue(species, out var sp) ? sp : null;
        }

        /// <summary>该不该在睡：从最后一次被喂 / 被摸算起；困着（精力低）门槛砍半。</summary>
        bool ShouldSleep(long now, PetStats pet)
        {
            if (!_config.SleepEnabled) return false;
            var threshold = _config.VitalsEnabled && pet.Energy < _config.LowEnergyAt ? _config.SleepAfterMs / 2.0 : _config.SleepAfterMs;
            return now - _state.LastFeedAt >= threshold;
        }

        PetBuff SettleBuff(long now)
        {
            if (_state.Buff == null) return null;
            return _state.Buff.Until > now ? _state.Buff : null;
        }

        DailyProgress SettleDaily(long now)
        {
            var today = PetRules.DayIndexOf(now, _zone);
            if (_state.Daily.Day == today) return _state.Daily;
            return DailyProgress.Empty(today);
        }

        /// <summary>「今天来喂过了」：昨天也来过就 +1，断了从 1 数起。</summary>
        void BumpStreak(long now, out long streakDay, out long streakCount)
        {
            var today = PetRules.DayIndexOf(now, _zone);
            streakDay = today;
            if (_state.StreakDay == today)
            {
                streakCount = _state.StreakCount;
                return;
            }
            streakCount = _state.StreakDay == today - 1 ? _state.StreakCount + 1 : 1;
        }

        /// <summary>结算回了几格零食（只动锚点，由调用方决定要不要 commit）。</summary>
        int SettleSnacks(long now)
        {
            var max = _config.ManualSnackMax;
            if (_config.ManualSnackRegenMs <= 0) return max;
            if (_state.Snacks >= max)
            {
                _snackAt = now;
                return _state.Snacks;
            }
            if (_snackAt == 0)
            {
                _snackAt = now;
                return _state.Snacks;
            }
            var gained = (long)Math.Floor((now - _snackAt) / (double)_config.ManualSnackRegenMs);
            if (gained <= 0) return _state.Snacks;
            _snackAt += gained * _config.ManualSnackRegenMs;
            var next = (int)Math.Min(max, _state.Snacks + gained);
            if (next >= max) _snackAt = now;
            return next;
        }

        // ─── 多宠物 ───

        public PetCollection Collection => _collection;
        public IReadOnlyList<Egg> EggList => _eggs;
        public GlobalStats GlobalStats => _globalStats;
        public bool IsFavorite => _favorites.Contains(_activePetId);

        /// <summary>按展示顺序换到下一只（direction &gt; 0）或上一只。</summary>
        public void SwitchPet(int direction)
        {
            Update();
            var order = _state.PetOrder;
            if (order.Count <= 1) return;
            var idx = order.IndexOf(_activePetId);
            var next = direction > 0 ? order[(idx + 1) % order.Count] : order[(idx - 1 + order.Count) % order.Count];
            SwitchTo(next);
        }

        /// <summary>
        /// 直接换到某一只（面板里点名字用）。网页版只有上一只 / 下一只；这里复用同一套换法，规则上没有新东西。
        /// </summary>
        public void SwitchPetTo(string id)
        {
            Update();
            if (!_collection.Contains(id)) return;
            SwitchTo(id);
        }

        void SwitchTo(string next)
        {
            if (next == _activePetId) return;
            SaveActiveToCollection();
            _activePetId = next;
            _activeRecord = _collection[_activePetId];
            ResetTransientState();
            Replace(StateFromRecord(_activeRecord));
        }

        public void ToggleFavorite()
        {
            Update();
            var idx = _favorites.IndexOf(_activePetId);
            if (idx >= 0) _favorites.RemoveAt(idx);
            else _favorites.Add(_activePetId);
            var patch = new Patch();
            patch.Favorites.Set(new List<string>(_favorites));
            patch.PetOrder.Set(PetRules.ComputePetOrder(_collection, _favorites));
            Commit(patch);
        }

        public void StartHatch(string eggId)
        {
            Update();
            var egg = _eggs.Find(e => e.Id == eggId);
            if (egg == null) return;
            var patch = new Patch();
            patch.HatchingEgg.Set(egg);
            Commit(patch, true);
        }

        public void CancelHatch()
        {
            Update();
            var patch = new Patch();
            patch.HatchingEgg.Set(null);
            Commit(patch, true);
        }

        string ResolveEggSpecies(string eggType)
        {
            if (eggType == null || !_data.EggTypeByKey.TryGetValue(eggType, out var et)) return "whale";
            if (et.Species == "random") return _data.Species[(int)Math.Floor(_random.Next() * _data.Species.Count)].Key;
            return et.Species;
        }

        /// <summary>孵化 StartHatch 选中的那颗蛋，并换到新宠物。</summary>
        public bool ConfirmHatch(string name)
        {
            Update();
            var egg = _state.HatchingEgg;
            if (egg == null) return false;
            if (_collection.Count >= _data.MaxPets) return false;
            var eggIdx = _eggs.FindIndex(e => e.Id == egg.Id);
            if (eggIdx < 0) return false;
            var now = Now;
            var species = ResolveEggSpecies(egg.Type);
            var newId = _save.NewPetId(now);
            var record = _save.CreateFreshPetRecord(species, name, newId, now);
            _eggs.RemoveAt(eggIdx);
            SaveActiveToCollection();
            _collection[newId] = record;
            _globalStats.PetsHatched += 1;
            _activePetId = newId;
            _activeRecord = record;
            ResetTransientState();
            var next = StateFromRecord(_activeRecord);
            next.HatchingEgg = null;
            Replace(next);
            return true;
        }

        /// <summary>直接添加一只（不经过蛋）。</summary>
        public bool AddPet(string speciesKey, string name)
        {
            Update();
            if (_collection.Count >= _data.MaxPets) return false;
            if (speciesKey == null || !_data.SpeciesByKey.TryGetValue(speciesKey, out var sp)) return false;
            var now = Now;
            var newId = _save.NewPetId(now);
            var record = _save.CreateFreshPetRecord(speciesKey, string.IsNullOrEmpty(name) ? sp.Name : name, newId, now);
            SaveActiveToCollection();
            _collection[newId] = record;
            _globalStats.PetsHatched += 1;
            _activePetId = newId;
            _activeRecord = record;
            ResetTransientState();
            Replace(StateFromRecord(_activeRecord));
            return true;
        }

        /// <summary>给当前宠物改名（1-20 字，空名忽略）。</summary>
        public bool RenamePet(string name)
        {
            Update();
            var trimmed = (name ?? "").Trim();
            if (trimmed.Length > 20) trimmed = trimmed.Substring(0, 20);
            if (trimmed == "") return false;
            SaveActiveToCollection();
            _activeRecord = _collection[_activePetId];
            _activeRecord.Name = trimmed;
            Replace(StateFromRecord(_activeRecord));
            return true;
        }
    }
}
