using System;
using System.Collections.Generic;
using System.Globalization;

namespace DeepseekPet.Core
{
    // 喂食与成长：喂食 / 工具观察 / 零食 / 摸头 / 空闲结算，以及挂在它们上面的成就、每日任务、技能、进化、蛋。
    public sealed partial class PetCore
    {
        static string N(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        void ExpireCombo()
        {
            _comboTimer = 0;
            _comboCount = 0;
            _comboLastTime = 0;
            var patch = new Patch();
            patch.ComboCount.Set(0);
            patch.ComboMultiplier.Set(1);
            patch.ComboTier.Set("normal");
            Commit(patch);
        }

        void DropEffect(string key)
        {
            var kept = _state.Effects.FindAll(e => e.Key != key);
            if (kept.Count == _state.Effects.Count) return;
            var patch = new Patch();
            patch.Effects.Set(kept);
            Commit(patch);
        }

        void AddEffect(Patch patch, PetEffect effect, List<PetEffect> baseList)
        {
            var list = new List<PetEffect>(baseList) { effect };
            patch.Effects.Set(list);
            SetTimeout(_config.EffectTtlMs, () => DropEffect(effect.Key));
        }

        /// <summary>追加一条通知类特效（进阶 / 成就 / 任务 / 摸头 …）；关了特效时什么都不做。</summary>
        PetEffect AppendNotice(Patch patch, string icon, string text, string source, string tier)
        {
            if (!_config.EffectsEnabled) return null;
            _effectSeq += 1;
            var effect = new PetEffect
            {
                Key = "fx" + N(_effectSeq), Icon = icon, Text = text, FoodAmount = 0, ExpAmount = 0, Tokens = 0,
                FoodTier = "large", Source = source, ComboCount = 0, ComboMultiplier = 1, Tier = tier,
            };
            AddEffect(patch, effect, patch.Effects.Or(_state.Effects));
            return effect;
        }

        void AppendMilestones(Patch patch, PetStats before, PetStats after)
        {
            foreach (var ml in _data.MilestoneLevels)
            {
                if (before.Level < ml.Level && after.Level >= ml.Level)
                {
                    AppendNotice(patch, ml.Icon, "里程碑 · Lv." + N(ml.Level) + " " + ml.Label, "milestone", "epic");
                    Say(patch, "milestone", true);
                }
            }
        }

        /// <summary>这一口把等级顶过了形态档就补一条进阶特效；没跨档但升了级就说一句。</summary>
        void AppendEvolve(Patch patch, PetStats before, PetStats after)
        {
            var stage = PetRules.WhaleStageOf(_data, after.Level);
            if (stage == PetRules.WhaleStageOf(_data, before.Level))
            {
                if (after.Level > before.Level && !CareHolds(patch)) Say(patch, "levelup", true);
                if (after.Level > before.Level) BumpDim(patch, "pride", _config.PridePerLevelup);
                if (after.Level > before.Level) AppendMorph(patch);
                if (after.Level > before.Level) AppendMilestones(patch, before, after);
                return;
            }
            AppendNotice(patch, _data.EvolveIcon, "进阶 · " + stage.Label, "evolve", "epic");
            Say(patch, "evolve", true);
            BumpDim(patch, "pride", _config.PridePerWin);
            AppendMorph(patch);
            AppendMilestones(patch, before, after);
        }

        /// <summary>够格就当场分化成对应形态（喂食和工具调用两条路都要查）。</summary>
        FormDef AppendMorph(Patch patch)
        {
            var pet = patch.Pet.Or(_state.Pet);
            var skills = patch.Skills.Or(_state.Skills);
            var form = PetRules.EvolveTargetOf(_data, pet, skills, _config);
            if (form == null) return null;
            var next = pet.Clone();
            next.Form = form.Key;
            patch.Pet.Set(next);
            var owned = patch.FormsOwned.Or(_state.FormsOwned) ?? new List<string>();
            if (!owned.Contains(form.Key)) patch.FormsOwned.Set(new List<string>(owned) { form.Key });
            AppendNotice(patch, form.Icon, "进化 · " + form.Label, "morph", "epic");
            Say(patch, "morph", true);
            BumpDim(patch, "pride", _config.PridePerWin);
            return form;
        }

        AchievementStats StatsOf(Patch patch, PetStats pet, int comboCount, string foodTier)
        {
            var forms = patch.FormsOwned.Or(_state.FormsOwned);
            return new AchievementStats
            {
                Data = _data,
                Pet = pet,
                TotalFeeds = patch.TotalFeeds.Or(_state.TotalFeeds),
                TotalTokens = patch.TotalTokens.Or(_state.TotalTokens),
                TokensBySource = patch.TokensBySource.Or(_state.TokensBySource),
                Pats = patch.Pats.Or(_state.Pats),
                StreakCount = patch.StreakCount.Or(_state.StreakCount),
                Skills = patch.Skills.Or(_state.Skills),
                Memory = patch.Memory.Or(_state.Memory),
                ComboCount = comboCount,
                FoodTier = foodTier,
                FormsOwnedCount = forms == null ? 0 : forms.Count,
            };
        }

        /// <summary>逐条查成就：达标的就地解锁，飘徽章、送零食（不给经验，免得连锁升级）。</summary>
        int CheckAchievements(Patch patch, PetStats pet, int comboCount = 0, string foodTier = null)
        {
            if (!_config.AchievementsEnabled) return 0;
            var owned = patch.Achievements.Or(_state.Achievements);
            var stats = StatsOf(patch, pet, comboCount, foodTier);
            var gained = new List<AchievementDef>();
            foreach (var item in _data.Achievements)
            {
                if (owned.Contains(item.Id)) continue;
                if (!item.Test(stats, _config)) continue;
                gained.Add(item);
            }
            if (gained.Count == 0) return 0;
            var next = new List<string>(owned);
            foreach (var item in gained) next.Add(item.Id);
            patch.Achievements.Set(next);
            if (_config.AchievementSnacks > 0)
            {
                var snacks = patch.Snacks.Or(_state.Snacks);
                patch.Snacks.Set(Math.Min(_config.ManualSnackMax, snacks + gained.Count * _config.AchievementSnacks));
            }
            foreach (var item in gained) AppendNotice(patch, item.Icon, "成就 · " + item.Label, "achieve", "epic");
            Say(patch, "achieve", true);
            BumpDim(patch, "pride", gained.Count * _config.PridePerWin);
            return gained.Count;
        }

        /// <summary>推进当天的任务进度，达成即结算（经验 + 零食 + 特效）。返回结算完奖励的 pet。</summary>
        PetStats ApplyDaily(Patch patch, PetStats pet, long now, long feeds = 0, long tools = 0, long combo = 0, long pats = 0, long tokens = 0, long snacksDelta = 0)
        {
            if (!_config.DailyEnabled) return pet;
            var b = SettleDaily(now);
            var daily = new DailyProgress
            {
                Day = b.Day,
                Feeds = b.Feeds + feeds,
                Tools = b.Tools + tools,
                BestCombo = Math.Max(b.BestCombo, combo),
                Pats = b.Pats + pats,
                Tokens = b.Tokens + tokens,
                Snacks = b.Snacks + snacksDelta,
                Done = b.Done,
            };
            var finished = new List<QuestDef>();
            foreach (var quest in PetRules.PickDailyQuests(_data, daily.Day))
            {
                if (daily.Done.Contains(quest.Id)) continue;
                if (quest.Of(daily) < quest.Goal) continue;
                finished.Add(quest);
            }
            var next = pet;
            if (finished.Count > 0)
            {
                daily.Done = new List<string>(daily.Done);
                foreach (var quest in finished) daily.Done.Add(quest.Id);
                if (_config.DailyQuestExp > 0) next = PetRules.FeedPet(_data, pet, 0, finished.Count * _config.DailyQuestExp);
                if (_config.DailyQuestSnacks > 0)
                {
                    var snacks = patch.Snacks.Or(_state.Snacks);
                    patch.Snacks.Set(Math.Min(_config.ManualSnackMax, snacks + finished.Count * _config.DailyQuestSnacks));
                }
                foreach (var quest in finished) AppendNotice(patch, _data.QuestIcon, "任务达成 · " + quest.Label, "quest", "gold");
                Say(patch, "quest", true);
            }
            patch.Daily.Set(daily);
            return next;
        }

        /// <summary>给一条技能加经验（种族天赋加成），升级 / 精通都飘一条通知。</summary>
        void ApplySkill(Patch patch, string key, long xp)
        {
            if (!_config.SkillsEnabled || xp <= 0) return;
            var sp = SpeciesOfActive();
            var bonus = sp != null && sp.SkillBonus.TryGetValue(key, out var b) ? b : 0;
            var adjusted = bonus > 0 ? (long)Math.Max(1, JsMath.Round(xp * (1 + bonus))) : xp;
            var baseSkills = patch.Skills.Or(_state.Skills);
            var result = PetRules.GainSkill(baseSkills, key, adjusted, _config);
            if (result.Skills == baseSkills) return;
            patch.Skills.Set(result.Skills);
            foreach (var up in result.Ups)
            {
                var item = _data.SkillById[up.Key];
                _lastSkillUp = up.Key;
                AppendNotice(patch, item.Icon, "技能 · " + item.Label + " Lv." + N(up.Value), "skill", "gold");
            }
            if (result.Ups.Count > 0)
            {
                Say(patch, "skill", true);
                BumpDim(patch, "pride", _config.PridePerWin);
            }
            if (result.MasteryUps.Count > 0)
            {
                foreach (var up in result.MasteryUps)
                {
                    var item = _data.SkillById[up.Key];
                    var stars = PetRules.MasteryStarsOf(up.Value * _config.MasteryXpPerTier, _config);
                    AppendNotice(patch, "⭐", "精通 · " + item.Label + " " + stars, "mastery", "gold");
                }
                Say(patch, "skill", true);
                BumpDim(patch, "pride", _config.PridePerWin);
            }
        }

        /// <summary>patch 里那份可改的记忆（第一次调用时从当前状态拷一份）。</summary>
        PetMemory MemoryOf(Patch patch)
        {
            if (!patch.Memory.Has) patch.Memory.Set(_state.Memory.Clone());
            return patch.Memory.Value;
        }

        List<Egg> DoCheckEggMilestones()
        {
            var gained = new List<Egg>();
            foreach (var m in _data.EggMilestones)
            {
                if (_globalStats.EggsObtained.Contains(m.Id)) continue;
                if (!m.Test(_globalStats)) continue;
                _globalStats.EggsObtained.Add(m.Id);
                var now = Now;
                var egg = new Egg { Id = _save.NewEggId(now), Type = m.Egg, ObtainedAt = now };
                if (_eggs.Count < _data.MaxEggs) _eggs.Add(egg);
                gained.Add(egg);
            }
            if (gained.Count > 0)
            {
                var patch = new Patch();
                patch.Eggs.Set(new List<Egg>(_eggs));
                Commit(patch);
            }
            return gained;
        }

        /// <summary>查一遍蛋里程碑（喂食时会自动查）；返回这次得到的蛋。</summary>
        public List<Egg> CheckEggMilestones()
        {
            Update();
            return DoCheckEggMilestones();
        }

        // ─── 入口 ───

        /// <summary>
        /// 桥来的一条统一事件（PetEvent 的几个字段）。路由照抄网页版 plugin.js：
        /// tool_call 只观察（长技能、记记忆）；tool_result 先看成败再喂；user_input / generation 直接喂；其余忽略。
        /// </summary>
        public void HandleEvent(string kind, long tokens, long output, string tool, string callId, string file, bool failed)
        {
            switch (kind)
            {
                case "tool_call":
                    if (!string.IsNullOrEmpty(tool)) ObserveTool(callId ?? "", tool, file);
                    break;
                case "tool_result":
                    ObserveToolResult(callId ?? "", failed);
                    Feed(FeedSource.ToolResult, tokens, 0);
                    break;
                case "user_input":
                    Feed(FeedSource.UserInput, tokens, 0);
                    break;
                case "generation":
                    Feed(FeedSource.Generation, tokens, output);
                    break;
            }
        }

        /// <summary>空闲时的低频结算（每 10 秒一次）：数值、零食、每日进度、BUFF、睡眠、小动作。</summary>
        public void Tick()
        {
            Update();
            var now = Now;
            var pet = SettleVitals(now);
            var snacks = SettleSnacks(now);
            var daily = SettleDaily(now);
            var buff = SettleBuff(now);
            var asleep = ShouldSleep(now, pet);
            var patch = new Patch();
            if (pet != _state.Pet) patch.Pet.Set(pet);
            if (snacks != _state.Snacks) patch.Snacks.Set(snacks);
            if (daily != _state.Daily) patch.Daily.Set(daily);
            if (buff != _state.Buff) patch.Buff.Set(buff);
            if (asleep != _state.Asleep)
            {
                patch.Asleep.Set(asleep);
                if (asleep) Say(patch, "sleep", true);
            }
            // 饿了 / 难过只在刚跨过线那一次说，而且醒着才说。
            if (patch.Pet.Has && !asleep)
            {
                if (_state.Pet.Hunger < _config.HungryAt && pet.Hunger >= _config.HungryAt)
                {
                    Say(patch, "hungry", true);
                    BumpDim(patch, "concern", _config.ConcernPerWorry);
                }
                else if (_config.VitalsEnabled && _state.Pet.Mood >= _config.MoodSadAt && pet.Mood < _config.MoodSadAt)
                {
                    Say(patch, "sad", false);
                }
            }
            IdlePlay(patch, now, asleep);
            if (patch.Any) Commit(patch);
        }

        /// <summary>
        /// 喂一次食：结算数值 → 走连击 → 算食物量 / 经验 → 更新宠物 → 加特效，顺带推进每日任务、连续到访、成就、
        /// 暴食 BUFF、进阶 / 进化、蛋。output 是这一步模型写了多少 token（只有 generation 有，「表达」技能用）。
        /// </summary>
        public void Feed(string source, long tokens, long output)
        {
            Update();
            var now = Now;
            var pet = SettleVitals(now);
            // 连击（createComboTracker）
            if (now - _comboLastTime > _config.ComboWindowMs) _comboCount = 0;
            _comboCount = Math.Min(_comboCount + 1, _config.MaxCombo);
            _comboLastTime = now;
            var count = _comboCount;
            var multiplier = PetRules.MultiplierOf(count);
            var buff = SettleBuff(now);
            var favorite = false;
            var bored = false;
            var variety = false;
            double taste = 1;
            if (_config.PickyEnabled)
            {
                if (source == _tasteSource)
                {
                    _tasteCount += 1;
                }
                else
                {
                    variety = _tasteSource != null;
                    _tasteSource = source;
                    _tasteCount = 1;
                }
                favorite = source == _config.FavoriteSource;
                bored = _tasteCount > _config.BoredomAfter;
                if (favorite) taste *= _config.FavoriteBonus;
                if (variety) taste *= _config.VarietyBonus;
            }
            var frenzy = buff != null && buff.Kind == "frenzy";
            // 主项来自 token 量级，连击只加一个 0..+5 的常数。
            var food = (int)PetRules.Clamp(
                JsMath.Round(PetRules.FoodFromTokens(tokens, _config) * taste * (frenzy ? _config.FrenzyFoodFactor : 1)) + Math.Floor(count / 2.0),
                _config.MinFood,
                _config.MaxFood);
            var prestigeBonus = 1 + _state.Prestige * _config.PrestigeExpBonus;
            var expFactor = multiplier * PetRules.VitalFactorOf(pet, _config) * (frenzy ? _config.FrenzyExpFactor : 1) * prestigeBonus;
            var exp = (int)(Math.Max(1, Math.Floor(_data.BaseExp[source] * expFactor + 0.5))
                + Math.Min(2, JsMath.Round(0.3 * JsMath.Log2(1 + tokens / 60.0))));
            var tier = PetRules.TierOf(count);
            var foodTier = PetRules.FoodTierOf(tokens);
            var bySource = _state.TokensBySource.Clone();
            bySource[source] += tokens;

            var patch = new Patch();
            patch.Pet.Set(PetRules.FeedPet(_data, pet, food, exp,
                _config.VitalsEnabled ? _config.MoodPerFeed : 0,
                _config.VitalsEnabled ? -_config.EnergyPerFeed : 0));
            patch.ComboCount.Set(count);
            patch.ComboMultiplier.Set(multiplier);
            patch.ComboTier.Set(tier);
            patch.EatKey.Set(_state.EatKey + 1);
            patch.LastAct.Set("eat");
            patch.IdleAct.Set(null);
            patch.TotalFeeds.Set(_state.TotalFeeds + 1);
            patch.TotalTokens.Set(_state.TotalTokens + tokens);
            patch.TokensBySource.Set(bySource);
            patch.Snacks.Set(SettleSnacks(now));
            if (_config.EffectsEnabled)
            {
                _effectSeq += 1;
                AddEffect(patch, new PetEffect
                {
                    Key = "fx" + N(_effectSeq), Icon = _data.FoodIcon[source][foodTier], Text = null,
                    FoodAmount = food, ExpAmount = exp, Tokens = tokens, FoodTier = foodTier, Source = source,
                    ComboCount = count, ComboMultiplier = multiplier, Tier = tier,
                }, _state.Effects);
            }
            // 顶到满连击就开一段暴食 BUFF；期间再顶到满就续期。
            var opened = false;
            if (_config.FrenzyEnabled && count >= _config.MaxCombo)
            {
                opened = !frenzy;
                patch.Buff.Set(new PetBuff { Kind = "frenzy", Until = now + _config.FrenzyMs });
                if (_buffTimer != 0) ClearTimeout(_buffTimer);
                _buffTimer = SetTimeout(_config.FrenzyMs, () =>
                {
                    _buffTimer = 0;
                    if (_state.Buff != null && _state.Buff.Until <= Now)
                    {
                        var clear = new Patch();
                        clear.Buff.Set(null);
                        Commit(clear, true);
                    }
                });
            }
            else if (buff != _state.Buff)
            {
                patch.Buff.Set(buff);
            }
            // 一次只说一句：开 BUFF > 大餐 > 吃饱 > 困了 > 换口味 > 连击升档 > 最爱 > 腻了 > 日常。
            var line = source;
            var after = patch.Pet.Value;
            if (opened) line = "frenzy";
            else if (foodTier == "feast") line = "feast";
            else if (after.Hunger == 0 && pet.Hunger > 0) line = "full";
            else if (_config.VitalsEnabled && pet.Energy >= _config.LowEnergyAt && after.Energy < _config.LowEnergyAt) line = "tired";
            else if (variety) line = "variety";
            else if (tier != _state.ComboTier && tier != "normal") line = "combo";
            else if (favorite) line = "favorite";
            else if (bored) line = "bored";
            // 关怀排在日常台词前面：它半小时才够格说一次。
            CareFor(patch, now);
            Say(patch, line, opened);
            Wake(patch, now);
            // 「表达」技能：generation 每 800 输出 token 1 点（单条最多 8）；用户打字每条 1 点。
            if (source == FeedSource.Generation) ApplySkill(patch, "writing", Math.Min(8, (long)Math.Floor(output / 800.0)));
            else if (source == FeedSource.UserInput) ApplySkill(patch, "writing", 1);
            BumpStreak(now, out var streakDay, out var streakCount);
            patch.StreakDay.Set(streakDay);
            patch.StreakCount.Set(streakCount);
            // 任务奖励也给经验，所以要在比进阶之前结算掉。
            patch.Pet.Set(ApplyDaily(patch, patch.Pet.Value, now, feeds: 1, tools: source == FeedSource.ToolResult ? 1 : 0, combo: count, tokens: tokens));
            CheckAchievements(patch, patch.Pet.Value, count, foodTier);
            AppendEvolve(patch, pet, patch.Pet.Value);
            _globalStats.TotalTokensAllTime += tokens;
            _globalStats.TotalFeedsAllTime += 1;
            if (patch.Achievements.Has && patch.Achievements.Value.Count > _state.Achievements.Count)
            {
                _globalStats.AchievementsUnlockedAllTime += patch.Achievements.Value.Count - _state.Achievements.Count;
            }
            Commit(patch);
            ScheduleLastActClear(500);
            DoCheckEggMilestones();
            // 随机掉蛋：里程碑之外的惊喜通道。「随机」是按累计喂食次数散列出来的，同一串事件结果相同。
            if (_config.EggDropEnabled && _eggs.Count < _data.MaxEggs)
            {
                var dropChance = _config.EggDropBaseChance + count * _config.EggDropComboBonus;
                var seed = JsMath.ToUint32(_state.TotalFeeds * 2654435761.0);
                seed ^= seed >> 16;
                seed = JsMath.Imul(seed, 0x85ebca6b);
                seed ^= seed >> 13;
                var roll = seed / 4294967296.0;
                if (roll < dropChance)
                {
                    var pool = new[] { "ocean", "forest", "code", "spark", "circuit" };
                    var dropNow = Now;
                    var egg = new Egg { Id = _save.NewEggId(dropNow), Type = pool[seed % (uint)pool.Length], ObtainedAt = dropNow };
                    _eggs.Add(egg);
                    var type = _data.EggTypeByKey.TryGetValue(egg.Type, out var et) ? et : null;
                    var drop = new Patch();
                    drop.Eggs.Set(new List<Egg>(_eggs));
                    _effectSeq += 1;
                    AddEffect(drop, new PetEffect
                    {
                        Key = "fx" + N(_effectSeq), Icon = "🥚", Text = "掉落 · " + (type != null ? type.Label : "蛋"),
                        FoodAmount = 0, ExpAmount = 0, Tokens = 0, FoodTier = "large", Source = "egg_drop",
                        ComboCount = 0, ComboMultiplier = 1, Tier = "gold",
                    }, _state.Effects);
                    Commit(drop);
                }
            }
            if (_comboTimer != 0) ClearTimeout(_comboTimer);
            _comboTimer = SetTimeout(_config.ComboWindowMs, ExpireCombo);
        }

        /// <summary>
        /// 看一眼这次工具调用：长技能、记进记忆、必要时提一句。**不喂食**，也不叫醒它。
        /// </summary>
        public void ObserveTool(string callId, string name, string file)
        {
            Update();
            var now = Now;
            _lastToolName = name;
            if (!string.IsNullOrEmpty(callId))
            {
                // 保持插入序，满了就丢最早的那条（JS 的 Map）。
                if (_callOrder.Count >= _data.CallNameCap)
                {
                    _callNames.Remove(_callOrder[0]);
                    _callOrder.RemoveAt(0);
                }
                if (!_callNames.ContainsKey(callId)) _callOrder.Add(callId);
                _callNames[callId] = name;
            }
            var patch = new Patch();
            // 好奇心在记忆被改之前算：新面孔的判定问的是「记忆里还没有它」。
            var fresh = 0;
            if (_config.MoodDimsEnabled)
            {
                if (PetRules.SkillOf(_data, name) == "research") fresh += _config.CuriosityPerResearch;
                if (_config.MemoryEnabled)
                {
                    if (!PetRules.HasRow(_state.Memory.Tools, name)) fresh += _config.CuriosityPerNew;
                    if (file != null && !PetRules.HasRow(_state.Memory.Files, file)) fresh += _config.CuriosityPerNew;
                }
            }
            ApplySkill(patch, PetRules.SkillOf(_data, name), 1);
            if (_config.MemoryEnabled)
            {
                var memory = MemoryOf(patch);
                memory.Tools = PetRules.BumpCount(memory.Tools, name, _data.MemoryToolTop);
                if (file != null) memory.Files = PetRules.BumpCount(memory.Files, file, _config.MemoryFileTop + _data.MemorySlack);
                memory.Hours[PetRules.LocalHourOf(now, _zone)] += 1;
            }
            if (file != null && _data.EditTools.Contains(name))
            {
                _sessionEdits.TryGetValue(file, out var edits);
                var times = edits + 1;
                _sessionEdits[file] = times;
                if (_config.AdviceRepeatEditAt > 0 && times >= _config.AdviceRepeatEditAt && times % _config.AdviceRepeatEditAt == 0)
                {
                    Advise(patch, PetTables.RepeatEdit(file, times), "coding", now);
                }
            }
            if (!patch.Bubble.Has) ChatMemory(patch, name, file, now);
            BumpDim(patch, "curiosity", fresh);
            CheckAchievements(patch, patch.Pet.Or(_state.Pet));
            AppendMorph(patch);
            if (patch.Any) Commit(patch);
        }

        /// <summary>看一眼这次工具结果成没成：数错误连击、记「克服过的困难」。**不喂食**（喂食由 Feed 走）。</summary>
        public void ObserveToolResult(string callId, bool failed)
        {
            Update();
            var now = Now;
            string name = null;
            if (!string.IsNullOrEmpty(callId) && _callNames.TryGetValue(callId, out name))
            {
                _callNames.Remove(callId);
                _callOrder.Remove(callId);
            }
            var patch = new Patch();
            if (failed)
            {
                // 同一个工具连着挂（该换思路）和各种工具轮着挂（该看看上一条错误）分开数。
                _toolFailStreak = name != null && name == _lastErrorTool ? _toolFailStreak + 1 : 1;
                _errorStreak += 1;
                _lastErrorTool = name;
                if (_config.MemoryEnabled) MemoryOf(patch).Errors += 1;
                if (_config.AdviceErrorStreakAt > 0 && _errorStreak >= _config.AdviceErrorStreakAt && _errorStreak % _config.AdviceErrorStreakAt == 0)
                {
                    Advise(patch, PetTables.ErrorStreak(_errorStreak), "debug", now);
                }
                else if (name != null && _toolFailStreak >= 2)
                {
                    Advise(patch, PetTables.ToolFlaky(name, _toolFailStreak), "debug", now);
                }
                BumpDim(patch, "concern", _config.ConcernPerError);
            }
            else
            {
                // 同一个工具刚报过错、这次成了 = 跨过了一道坎。
                if (_config.MemoryEnabled && _lastErrorTool != null && name != null && name == _lastErrorTool)
                {
                    MemoryOf(patch).Recoveries += 1;
                    Advise(patch, PetTables.Recovered(name), "debug", now);
                    BumpDim(patch, "pride", _config.PridePerWin);
                }
                _errorStreak = 0;
                _toolFailStreak = 0;
                _lastErrorTool = null;
            }
            CheckAchievements(patch, patch.Pet.Or(_state.Pet));
            if (patch.Any) Commit(patch);
        }

        /// <summary>主动喂一口零食：消耗一格库存，换固定的食物量与一点经验。不进连击、不进 token 统计。</summary>
        public bool Snack()
        {
            Update();
            if (!_config.ManualFeedEnabled) return false;
            var now = Now;
            var pet = SettleVitals(now);
            var snacks = SettleSnacks(now);
            if (snacks <= 0)
            {
                if (pet != _state.Pet)
                {
                    var p = new Patch();
                    p.Pet.Set(pet);
                    Commit(p);
                }
                return false;
            }
            var food = _config.ManualFeedFood;
            var exp = _config.ManualFeedExp;
            var patch = new Patch();
            patch.Pet.Set(PetRules.FeedPet(_data, pet, food, exp, _config.VitalsEnabled ? _config.MoodPerFeed : 0, 0));
            patch.EatKey.Set(_state.EatKey + 1);
            patch.LastAct.Set("eat");
            patch.IdleAct.Set(null);
            patch.TotalFeeds.Set(_state.TotalFeeds + 1);
            patch.Snacks.Set(snacks - 1);
            if (_config.EffectsEnabled)
            {
                _effectSeq += 1;
                AddEffect(patch, new PetEffect
                {
                    Key = "fx" + N(_effectSeq), Icon = _data.SnackIcon, Text = null, FoodAmount = food, ExpAmount = exp,
                    Tokens = 0, FoodTier = "small", Source = "manual", ComboCount = 0, ComboMultiplier = 1, Tier = "normal",
                }, _state.Effects);
            }
            Say(patch, "snack", false);
            Wake(patch, now);
            BumpStreak(now, out var streakDay, out var streakCount);
            patch.StreakDay.Set(streakDay);
            patch.StreakCount.Set(streakCount);
            patch.Pet.Set(ApplyDaily(patch, patch.Pet.Value, now, feeds: 1, snacksDelta: 1));
            CheckAchievements(patch, patch.Pet.Value);
            AppendEvolve(patch, pet, patch.Pet.Value);
            Commit(patch);
            ScheduleLastActClear(500);
            return true;
        }

        /// <summary>摸一下头：涨心情、把它叫醒、飘爱心。有冷却；连着摸换一套更黏人的台词。</summary>
        public bool Pat()
        {
            Update();
            if (!_config.PatEnabled) return false;
            var now = Now;
            if (now - _patAt < _config.PatCooldownMs) return false;
            _patRun = _patAt != 0 && now - _patAt <= _config.PatCooldownMs * 3 ? _patRun + 1 : 1;
            _patAt = now;
            var pet = SettleVitals(now);
            var patch = new Patch();
            patch.Pet.Set(PetRules.FeedPet(_data, pet, 0, 0, _config.VitalsEnabled ? _config.MoodPerPat : 0, 0));
            patch.Pats.Set(_state.Pats + 1);
            patch.PatKey.Set(_state.PatKey + 1);
            patch.LastAct.Set("pat");
            patch.IdleAct.Set(null);
            AppendNotice(patch, _data.PatIcon, "摸摸头", "pat", "normal");
            Say(patch, _patRun >= 3 ? "pat_more" : "pat", true);
            Wake(patch, now);
            patch.Pet.Set(ApplyDaily(patch, patch.Pet.Value, now, pats: 1));
            CheckAchievements(patch, patch.Pet.Value);
            Commit(patch);
            ScheduleLastActClear(500);
            return true;
        }

        // ─── 微游戏 ───

        static readonly string[] MiniGameIcons = { "🥕", "🐟", "🍖", "🌽", "🍗", "🍤", "🍲" };

        public bool StartMiniGame()
        {
            Update();
            if (!_config.MiniGameEnabled) return false;
            var now = Now;
            if (_miniGameAt != 0 && now - _miniGameAt < _config.MiniGameCooldownMs) return false;
            if (_state.MiniGame != null) return false;
            _miniGameAt = now;
            var game = new MiniGame { Current = 0, StartedAt = now, Deadline = now + _config.MiniGameTimeoutMs };
            for (var i = 0; i < _config.MiniGameTargets; i++)
            {
                var seed = JsMath.ToUint32(now + i * 7919.0);
                seed ^= seed >> 16;
                seed = JsMath.Imul(seed, 0x85ebca6b);
                seed ^= seed >> 13;
                game.Targets.Add(new MiniGameTarget
                {
                    Id = i, Icon = MiniGameIcons[seed % (uint)MiniGameIcons.Length], X = 10 + seed % 60, Y = 10 + (seed >> 8) % 50, Caught = false,
                });
            }
            var patch = new Patch();
            patch.MiniGame.Set(game);
            Commit(patch, true);
            return true;
        }

        void ClearMiniGameLater(long delayMs)
        {
            SetTimeout(delayMs, () =>
            {
                var clear = new Patch();
                clear.MiniGame.Set(null);
                Commit(clear, true);
            });
        }

        /// <summary>点中了一个目标（必须按顺序点当前那个，超时算输）。</summary>
        public void HitMiniGameTarget(int targetId)
        {
            Update();
            var game = _state.MiniGame;
            if (game == null || game.Finished) return;
            if (targetId != game.Current) return;
            var now = Now;
            if (now > game.Deadline)
            {
                var lost = game.Clone();
                lost.Finished = true;
                lost.Success = false;
                var p = new Patch();
                p.MiniGame.Set(lost);
                Commit(p, true);
                ClearMiniGameLater(1500);
                return;
            }
            var nextTarget = game.Current + 1;
            var next = game.Clone();
            next.Targets = game.Targets.ConvertAll(t =>
            {
                if (t.Id != targetId) return t;
                var caught = t.Clone();
                caught.Caught = true;
                return caught;
            });
            next.Current = nextTarget;
            if (nextTarget >= game.Targets.Count)
            {
                var pet = SettleVitals(now);
                next.Finished = true;
                next.Success = true;
                next.Score = game.Targets.Count;
                var patch = new Patch();
                patch.MiniGame.Set(next);
                patch.Pet.Set(PetRules.FeedPet(_data, pet, 0, _config.MiniGameExpReward, _config.MiniGameMoodReward, 0));
                if (_config.MiniGameSnackReward > 0) patch.Snacks.Set(Math.Min(_config.ManualSnackMax, _state.Snacks + _config.MiniGameSnackReward));
                AppendNotice(patch, "🎮", "小游戏通关！+" + N(_config.MiniGameExpReward) + "⭐", "minigame", "gold");
                Say(patch, "proud", true);
                BumpDim(patch, "pride", _config.PridePerWin);
                Commit(patch);
                ClearMiniGameLater(2000);
            }
            else
            {
                next.Deadline = now + _config.MiniGameTimeoutMs;
                var patch = new Patch();
                patch.MiniGame.Set(next);
                Commit(patch, true);
            }
        }

        public void FailMiniGame()
        {
            Update();
            var game = _state.MiniGame;
            if (game == null || game.Finished) return;
            var pet = SettleVitals(Now);
            var lost = game.Clone();
            lost.Finished = true;
            lost.Success = false;
            var patch = new Patch();
            patch.MiniGame.Set(lost);
            patch.Pet.Set(PetRules.FeedPet(_data, pet, 0, 0, 5, 0));
            Say(patch, "sad", false);
            Commit(patch);
            ClearMiniGameLater(1500);
        }

        // ─── 轮回与形态 ───

        /// <summary>轮回：等级 / 技能等级归零（精通保留），换一份永久经验加成。</summary>
        public bool Prestige()
        {
            Update();
            if (!_config.PrestigeEnabled) return false;
            var pet = _state.Pet;
            if (pet.Level < _config.PrestigeMinLevel) return false;
            var newPrestige = _state.Prestige + 1;
            var resetPet = pet.Clone();
            resetPet.Mood = 80;
            resetPet.Hunger = 60;
            resetPet.Energy = 75;
            resetPet.Curiosity = 0;
            resetPet.Pride = 0;
            resetPet.Concern = 0;
            resetPet.Exp = 0;
            resetPet.Level = 1;
            resetPet.Form = "";
            var resetSkills = new SkillTable();
            foreach (var item in _data.Skills)
            {
                var old = _state.Skills[item.Key];
                resetSkills.Rows[item.Key] = new SkillRecord { Xp = 0, Level = 1, Mastery = old != null ? old.Mastery : 0 };
            }
            var patch = new Patch();
            patch.Pet.Set(resetPet);
            patch.Skills.Set(resetSkills);
            patch.Prestige.Set(newPrestige);
            patch.Snacks.Set(_config.ManualSnackMax);
            _collection[_activePetId].Prestige = newPrestige;
            var tierText = newPrestige == 1 ? " · 解锁轮回徽章" : newPrestige == 2 ? " · 解锁金名" : newPrestige == 3 ? " · 解锁辉光" : "";
            AppendNotice(patch, "🔄", "轮回 · 第 " + N(newPrestige) + " 世 · 经验+" + N((long)JsMath.Round(newPrestige * _config.PrestigeExpBonus * 100)) + "%" + tierText, "prestige", "epic");
            Say(patch, "evolve", true);
            BumpDim(patch, "pride", _config.PridePerWin * 2);
            Commit(patch);
            return true;
        }

        /// <summary>换成收集过的另一种进化形态。</summary>
        public bool SwitchForm(string formKey)
        {
            Update();
            if (_state.FormsOwned == null || !_state.FormsOwned.Contains(formKey)) return false;
            if (PetRules.FormOf(_data, formKey) == null) return false;
            var pet = _state.Pet.Clone();
            pet.Form = formKey;
            var patch = new Patch();
            patch.Pet.Set(pet);
            Commit(patch);
            return true;
        }

        /// <summary>此刻的长相：进化形态，或按等级的鲸鱼档。</summary>
        public string LookKey
        {
            get
            {
                var form = PetRules.FormOf(_data, _state.Pet.Form);
                return form != null ? form.Key : PetRules.WhaleStageOf(_data, _state.Pet.Level).Key;
            }
        }

        public string LookLabel
        {
            get
            {
                var form = PetRules.FormOf(_data, _state.Pet.Form);
                return form != null ? form.Label : PetRules.WhaleStageOf(_data, _state.Pet.Level).Label;
            }
        }
    }
}
