using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeepseekPet.Core
{
    // 气泡与台词：日常台词、情绪三维、提示（advise）、关怀与闲聊（chat）、睡醒、闲下来的小动作。
    public sealed partial class PetCore
    {
        /// <summary>
        /// 说出一句给定的话。普通场合受 bubbleMinGapMs 限流；force 留给进阶 / 成就 / 摸头这些大事。
        /// 例外：占着位子的是情绪三维那几句而来的是提示时，不算限流。
        /// </summary>
        bool EmitBubble(Patch patch, string kind, string text, bool force)
        {
            if (!_config.BubbleEnabled) return false;
            var now = Now;
            var isDim = _data.DimLineKinds.Contains(kind);
            var yields = _lastBubbleDim && kind == "advice";
            if (!force && !yields && now - _lastBubbleAt < _config.BubbleMinGapMs) return false;
            _lastBubbleAt = now;
            _lastBubbleDim = isDim;
            _bubbleSeq += 1;
            var bubble = new PetBubble { Key = "b" + _bubbleSeq.ToString(CultureInfo.InvariantCulture), Kind = kind, Text = text };
            patch.Bubble.Set(bubble);
            if (_bubbleTimer != 0) ClearTimeout(_bubbleTimer);
            _bubbleTimer = SetTimeout(_config.BubbleTtlMs, () =>
            {
                _bubbleTimer = 0;
                // 期间可能已经换了一句：只撤自己那一条。
                if (_state.Bubble != null && _state.Bubble.Key == bubble.Key)
                {
                    var clear = new Patch();
                    clear.Bubble.Set(null);
                    Commit(clear, true);
                }
            });
            return true;
        }

        DynamicLineContext BuildDynamicContext()
        {
            var memory = _state.Memory ?? PetRules.CreateMemory(PetRules.DayIndexOf(Now, _zone));
            return new DynamicLineContext
            {
                Data = _data,
                Pet = _state.Pet,
                TotalFeeds = _state.TotalFeeds,
                TotalTokens = _state.TotalTokens,
                TogetherDays = PetRules.TogetherDaysOf(memory, Now, _zone),
                Pats = _state.Pats,
                StreakCount = _state.StreakCount,
                Memory = memory,
                LastToolName = _lastToolName,
                LastSkillUp = _lastSkillUp,
                Skills = _state.Skills,
                Prestige = _state.Prestige,
            };
        }

        /// <summary>
        /// 说一句这个场合的台词：挑哪句走 pickLineIndex（确定性伪随机）；每 dynamicLinesEvery 次试一句动态台词，
        /// 条件不满足就回退静态池。下标在**说出来之后**才记下。
        /// </summary>
        bool Say(Patch patch, string kind, bool force)
        {
            if (!_data.BubbleLines.TryGetValue(kind, out var lines)) return false;
            if (PetTables.DynamicLines.TryGetValue(kind, out var dynamic) && dynamic.Length > 0
                && _config.DynamicLinesEvery > 0 && (_bubbleSeq + 1) % _config.DynamicLinesEvery == 0)
            {
                var ctx = BuildDynamicContext();
                var dIdx = PetRules.PickLineIndex(dynamic.Length, _bubbleSeq + 1, -1);
                var text = dynamic[dIdx](ctx);
                if (text != null)
                {
                    if (!EmitBubble(patch, kind, text, force)) return false;
                    _lastSkillUp = null;
                    return true;
                }
            }
            var last = _lineAt.TryGetValue(kind, out var l) ? l : -1;
            var at = PetRules.PickLineIndex(lines.Length, _bubbleSeq + 1, last);
            if (!EmitBubble(patch, kind, lines[at], force)) return false;
            _lineAt[kind] = at;
            return true;
        }

        /// <summary>推一维情绪；越过 moodDimAt 的那一次顺手说一句（不强插）。</summary>
        void BumpDim(Patch patch, string key, int amount)
        {
            if (!_config.MoodDimsEnabled || amount <= 0) return;
            var pet = patch.Pet.Or(_state.Pet);
            var before = pet.Dim(key);
            var value = PetRules.Clamp(before + amount, 0, 100);
            if (value == before) return;
            var next = pet.Clone();
            next.SetDim(key, value);
            patch.Pet.Set(next);
            if (before >= _config.MoodDimAt || value < _config.MoodDimAt) return;
            foreach (var dim in _data.MoodDims)
            {
                if (dim.Key == key) Say(patch, dim.Line, false);
            }
        }

        /// <summary>这份 patch 里是不是已经躺了一句关怀（它半小时才够格说一次，别被顶掉）。</summary>
        bool CareHolds(Patch patch)
        {
            return patch.Bubble.Has && patch.Bubble.Value != null && _data.CareKinds.Contains(patch.Bubble.Value.Kind);
        }

        /// <summary>说一句真的有用的话：对应技能够高、提示冷却过了、气泡也没被限流，三道都过才说。</summary>
        bool Advise(Patch patch, string text, string skillKey, long now)
        {
            if (!_config.AdviceEnabled) return false;
            var skill = patch.Skills.Or(_state.Skills)[skillKey];
            if (skill == null || skill.Level < _config.AdviceMinSkillLevel) return false;
            if (_adviceAt != 0 && now - _adviceAt < _config.AdviceCooldownMs) return false;
            if (!EmitBubble(patch, "advice", text, false)) return false;
            _adviceAt = now;
            return true;
        }

        /// <summary>主动搭一句闲话（关怀 / 引用记忆），走 careCooldownMs 那道长冷却。text 为 null 时从台词池挑。</summary>
        bool Chat(Patch patch, string kind, string text, long now)
        {
            if (!_config.CareEnabled) return false;
            if (_careAt != 0 && now - _careAt < _config.CareCooldownMs) return false;
            var said = text == null ? Say(patch, kind, false) : EmitBubble(patch, kind, text, false);
            if (said) _careAt = now;
            return said;
        }

        /// <summary>干活干到该歇了没有：好久不见 &gt; 久坐 &gt; 深夜。挂在喂食上（人正在干活时说才有意义）。</summary>
        void CareFor(Patch patch, long now)
        {
            if (_comebackMs > 0)
            {
                if (Chat(patch, "comeback", PetTables.Comeback(PetRules.AwayTextOf(_comebackMs)), now)) _comebackMs = 0;
                return;
            }
            if (_activeSince == 0 || now - _state.LastFeedAt >= _config.SleepAfterMs)
            {
                _activeSince = now;
                _marathonSaid = false;
            }
            if (!_marathonSaid && now - _activeSince >= _config.CareMarathonMs)
            {
                if (Chat(patch, "marathon", null, now))
                {
                    _marathonSaid = true;
                    BumpDim(patch, "concern", _config.ConcernPerWorry);
                }
                return;
            }
            if (PetRules.IsNightHour(PetRules.LocalHourOf(now, _zone), _config.CareNightFrom, _config.CareNightTo))
            {
                if (Chat(patch, "night", null, now)) BumpDim(patch, "concern", _config.ConcernPerWorry);
            }
        }

        /// <summary>引用记忆搭一句话：按「有辨识度」的优先级逐条试，实际上半小时最多听见一句。</summary>
        void ChatMemory(Patch patch, string name, string file, long now)
        {
            if (!_config.MemoryEnabled) return;
            var memory = patch.Memory.Or(_state.Memory);
            var pet = patch.Pet.Or(_state.Pet);
            if (file != null && memory.Files.Count > 0 && memory.Files[0].Name == file)
            {
                if (memory.Files[0].Count >= 5 && Chat(patch, "chat", PetTables.FavoriteFile(file), now)) return;
            }
            if (file != null && _sessionEdits.TryGetValue(file, out var edits))
            {
                if (edits >= 4 && edits % 4 == 0)
                {
                    if (Chat(patch, "chat", PetTables.FileAgain(file, edits), now)) return;
                }
            }
            MemoryRow tool = null;
            foreach (var row in memory.Tools)
            {
                if (row.Name == name) tool = row;
            }
            if (tool != null && tool.Count >= 10 && tool.Count % 10 == 0)
            {
                if (Chat(patch, "chat", PetTables.ToolHabit(tool.Name, tool.Count), now)) return;
            }
            if (_state.StreakCount >= 3 && _state.StreakCount % 3 == 0)
            {
                if (Chat(patch, "chat", PetTables.Streak(_state.StreakCount), now)) return;
            }
            var days = PetRules.TogetherDaysOf(memory, now, _zone);
            if (days >= 7 && days % 7 == 0)
            {
                if (Chat(patch, "chat", PetTables.Together(days), now)) return;
            }
            var skills = patch.Skills.Or(_state.Skills);
            foreach (var item in _data.Skills)
            {
                var sk = skills[item.Key];
                if (sk != null && sk.Level >= 4 && sk.Level % 2 == 0)
                {
                    if (Chat(patch, "chat", PetTables.SkillNote(item.Label, sk.Level), now)) return;
                    break;
                }
            }
            if (pet.Level >= 10 && pet.Level % 10 == 0)
            {
                if (Chat(patch, "chat", PetTables.Growth(pet.Level), now)) return;
            }
            if (_state.TotalTokens >= 100000 && _state.TotalTokens % 100000 < 3000)
            {
                if (Chat(patch, "chat", PetTables.TokensMilestone(_state.TotalTokens), now)) return;
            }
            if (file != null && memory.Files.Count > 0)
            {
                foreach (var row in memory.Files)
                {
                    if (row.Name == file && row.Count >= 20 && row.Count % 10 == 0)
                    {
                        if (Chat(patch, "chat", PetTables.FileExpert(file, row.Count), now)) return;
                        break;
                    }
                }
            }
            if (memory.Recoveries >= 10 && memory.Recoveries % 25 == 0)
            {
                if (Chat(patch, "chat", PetTables.Resilience(memory.Errors, memory.Recoveries), now)) return;
            }
            var range = PetRules.BusyHoursOf(memory.Hours);
            if (range != null)
            {
                var hour = PetRules.LocalHourOf(now, _zone);
                var from = int.Parse(range.Split('-')[0], CultureInfo.InvariantCulture);
                if (PetRules.IsNightHour(hour, from, (from + 2) % 24)) Chat(patch, "chat", PetTables.BusyHour(range), now);
            }
            if (_state.Prestige > 0) Chat(patch, "prestige_chat", null, now);
        }

        /// <summary>闲下来演一个小动作：没睡、安静够久、离上一个小动作也够久。</summary>
        void IdlePlay(Patch patch, long now, bool asleep)
        {
            if (!_config.IdleActEnabled || asleep) return;
            if (_state.IdleAct != null) return;
            if (now - _state.LastFeedAt < _config.IdleActQuietMs) return;
            if (_idleAt != 0 && now - _idleAt < _config.IdleActEveryMs) return;
            _idleAt = now;
            _idleSeq += 1;
            var at = PetRules.PickLineIndex(_data.IdleActs.Length, _idleSeq, _idleLast);
            _idleLast = at;
            patch.IdleAct.Set(_data.IdleActs[at]);
            if (_idleTimer != 0) ClearTimeout(_idleTimer);
            _idleTimer = SetTimeout(_config.IdleActMs, () =>
            {
                _idleTimer = 0;
                if (_state.IdleAct != null)
                {
                    var clear = new Patch();
                    clear.IdleAct.Set(null);
                    Commit(clear, true);
                }
            });
        }

        /// <summary>任何一次互动都会把它叫醒；从睡着被叫醒时打个哈欠（相处 ≥ 3 天换成带天数的那句）。</summary>
        void Wake(Patch patch, long now)
        {
            patch.LastFeedAt.Set(now);
            if (!_state.Asleep) return;
            patch.Asleep.Set(false);
            if (CareHolds(patch)) return;
            if (_config.MemoryEnabled)
            {
                var days = PetRules.TogetherDaysOf(_state.Memory, now, _zone);
                if (days >= 3)
                {
                    EmitBubble(patch, "wake", "又见面了，这是第 " + days.ToString(CultureInfo.InvariantCulture) + " 天…（打哈欠）", true);
                    return;
                }
            }
            Say(patch, "wake", true);
        }
    }
}
