using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DeepseekPet.Core
{
    /// <summary>v2 存档（多宠物）。和网页版 localStorage["dsh-pet-plugin/state"] 是同一个格式，可以直接互相导入。</summary>
    public sealed class PetSaveV2
    {
        public long SavedAt;
        public string ActivePetId;
        public List<string> Favorites = new List<string>();
        public PetCollection Pets = new PetCollection();
        public List<Egg> Eggs = new List<Egg>();
        public GlobalStats Global = new GlobalStats();
    }

    /// <summary>
    /// 存档的清洗、迁移与序列化（logic.js 的 sanitize* / migrateV1ToV2 / createFreshPetRecord / createPersistenceV2）。
    /// 外来数据逐字段查类型 + 夹范围，任何一处不对就退回兜底值，而不是整份信任。
    /// </summary>
    public sealed class PetSave
    {
        const double MaxSafeInteger = 9007199254740991;

        readonly PetData _data;
        readonly PetConfig _config;
        readonly TimeZoneInfo _zone;
        readonly IRandom _random;

        public PetSave(PetData data, PetConfig config, TimeZoneInfo zone, IRandom random)
        {
            _data = data;
            _config = config;
            _zone = zone;
            _random = random;
        }

        // ─── 清洗小工具 ───

        static bool IsNumber(JToken v)
        {
            return v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);
        }

        /// <summary>logic.js 的 numberIn：不是有限数字就用兜底值，否则向下取整再夹进区间。</summary>
        static long NumberIn(JToken value, double low, double high, long fallback)
        {
            if (!IsNumber(value)) return fallback;
            var x = value.Value<double>();
            if (double.IsNaN(x) || double.IsInfinity(x)) return fallback;
            return (long)PetRules.Clamp(Math.Floor(x), low, high);
        }

        static JToken Field(JToken obj, string key)
        {
            return obj is JObject o ? o[key] : null;
        }

        static bool IsObject(JToken v)
        {
            return v != null && (v.Type == JTokenType.Object || v.Type == JTokenType.Array);
        }

        static string StringOf(JToken v)
        {
            return v != null && v.Type == JTokenType.String ? v.Value<string>() : null;
        }

        static List<string> SanitizeIds<T>(JToken value, Dictionary<string, T> table)
        {
            var kept = new List<string>();
            if (!(value is JArray array)) return kept;
            foreach (var item in array)
            {
                var id = StringOf(item);
                if (id == null || !table.ContainsKey(id) || kept.Contains(id)) continue;
                kept.Add(id);
            }
            return kept;
        }

        DailyProgress SanitizeDaily(JToken value, long today)
        {
            if (!(value is JObject)) return DailyProgress.Empty(today);
            if (NumberIn(value["day"], 0, 1e9, -1) != today) return DailyProgress.Empty(today);
            return new DailyProgress
            {
                Day = today,
                Feeds = NumberIn(value["feeds"], 0, 1e6, 0),
                Tools = NumberIn(value["tools"], 0, 1e6, 0),
                BestCombo = NumberIn(value["bestCombo"], 0, 1e6, 0),
                Pats = NumberIn(value["pats"], 0, 1e6, 0),
                Tokens = NumberIn(value["tokens"], 0, 1e12, 0),
                Snacks = NumberIn(value["snacks"], 0, 1e6, 0),
                Done = SanitizeIds(value["done"], _data.QuestById),
            };
        }

        static CardPos SanitizePos(JToken value)
        {
            if (!(value is JObject)) return new CardPos();
            return new CardPos { Dx = NumberIn(value["dx"], -8192, 8192, 0), Dy = NumberIn(value["dy"], -8192, 8192, 0) };
        }

        SkillTable SanitizeSkills(JToken value)
        {
            var skills = new SkillTable();
            foreach (var item in _data.Skills)
            {
                var from = Field(value, item.Key);
                if (!IsObject(from))
                {
                    skills.Rows[item.Key] = new SkillRecord { Xp = 0, Level = 1, Mastery = 0 };
                    continue;
                }
                var level = NumberIn(from["level"], 1, _config.SkillMaxLevel, 1);
                skills.Rows[item.Key] = new SkillRecord
                {
                    Xp = (int)NumberIn(from["xp"], 0, PetRules.SkillNeedOf(level, _config), 0),
                    Level = (int)level,
                    Mastery = NumberIn(from["mastery"], 0, 1e9, 0),
                };
            }
            return skills;
        }

        static List<MemoryRow> SanitizeRows(JToken value, int cap)
        {
            var rows = new List<MemoryRow>();
            if (!(value is JArray array)) return rows;
            var seen = new HashSet<string>();
            foreach (var row in array)
            {
                if (!IsObject(row)) continue;
                var name = PetRules.BaseNameOf(StringOf(Field(row, "name")));
                if (name == null || !seen.Add(name)) continue;
                rows.Add(new MemoryRow(name, NumberIn(Field(row, "count"), 1, 1e9, 1)));
            }
            PetRules.StableSortDescending(rows);
            if (rows.Count > cap) rows.RemoveRange(cap, rows.Count - cap);
            return rows;
        }

        PetMemory SanitizeMemory(JToken value, long today)
        {
            var raw = IsObject(value) ? value : new JObject();
            var memory = new PetMemory();
            var from = Field(raw, "hours") as JArray;
            for (var h = 0; h < 24; h++) memory.Hours[h] = NumberIn(from != null && h < from.Count ? from[h] : null, 0, 1e9, 0);
            // bornDay 落在「有意义的过去」之外就当没记过，从今天开始数。
            var bornDay = NumberIn(Field(raw, "bornDay"), 0, 1e9, -1);
            if (bornDay <= 0 || bornDay > today) bornDay = today;
            memory.Files = SanitizeRows(Field(raw, "files"), _config.MemoryFileTop + _data.MemorySlack);
            memory.Tools = SanitizeRows(Field(raw, "tools"), _data.MemoryToolTop);
            memory.BornDay = bornDay;
            memory.Errors = NumberIn(Field(raw, "errors"), 0, 1e9, 0);
            memory.Recoveries = NumberIn(Field(raw, "recoveries"), 0, 1e9, 0);
            return memory;
        }

        PetStats SanitizePetFields(JToken pet)
        {
            var level = (int)NumberIn(Field(pet, "level"), 1, 9999, 1);
            var form = StringOf(Field(pet, "form"));
            return new PetStats
            {
                Hunger = (int)NumberIn(Field(pet, "hunger"), 0, 100, 60),
                Mood = (int)NumberIn(Field(pet, "mood"), 0, 100, 80),
                Energy = (int)NumberIn(Field(pet, "energy"), 0, 100, 75),
                Curiosity = (int)NumberIn(Field(pet, "curiosity"), 0, 100, 0),
                Pride = (int)NumberIn(Field(pet, "pride"), 0, 100, 0),
                Concern = (int)NumberIn(Field(pet, "concern"), 0, 100, 0),
                Level = level,
                Exp = (int)NumberIn(Field(pet, "exp"), 0, (double)level * _data.ExpPerLevel, 0),
                Form = PetRules.FormOf(_data, form) == null ? "" : form,
            };
        }

        /// <summary>v1 / v2 共用的进度字段，填进 record。</summary>
        void SanitizeProgressFields(JToken raw, long today, PetRecord rec)
        {
            var bySource = Field(raw, "tokensBySource");
            if (!IsObject(bySource)) bySource = new JObject();
            rec.TotalFeeds = NumberIn(Field(raw, "totalFeeds"), 0, 1e9, 0);
            rec.TotalTokens = NumberIn(Field(raw, "totalTokens"), 0, 1e12, 0);
            rec.TokensBySource = new TokensBySource
            {
                UserInput = NumberIn(Field(bySource, "user_input"), 0, 1e12, 0),
                Generation = NumberIn(Field(bySource, "generation"), 0, 1e12, 0),
                ToolResult = NumberIn(Field(bySource, "tool_result"), 0, 1e12, 0),
            };
            rec.Snacks = (int)NumberIn(Field(raw, "snacks"), 0, _config.ManualSnackMax, _config.ManualSnackMax);
            rec.Achievements = SanitizeIds(Field(raw, "achievements"), _data.AchievementById);
            rec.Daily = SanitizeDaily(Field(raw, "daily"), today);
            rec.StreakDay = NumberIn(Field(raw, "streakDay"), 0, 1e9, 0);
            rec.StreakCount = NumberIn(Field(raw, "streakCount"), 0, 1e6, 0);
            rec.Pats = NumberIn(Field(raw, "pats"), 0, 1e9, 0);
            rec.Pos = SanitizePos(Field(raw, "pos"));
            rec.LastFeedAt = NumberIn(Field(raw, "lastFeedAt"), 0, MaxSafeInteger, 0);
            rec.Skills = SanitizeSkills(Field(raw, "skills"));
            rec.Memory = SanitizeMemory(Field(raw, "memory"), today);
        }

        // ─── 新建 ───

        /// <summary>JS 的 Number.prototype.toString(36)（非负整数）。</summary>
        static string Base36(long value)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (value <= 0) return "0";
            var sb = new StringBuilder();
            while (value > 0)
            {
                sb.Insert(0, digits[(int)(value % 36)]);
                value /= 36;
            }
            return sb.ToString();
        }

        /// <summary>随机数的 36 进制小数部分取前 count 位（JS 的 Math.random().toString(36).slice(2, 2 + count)）。</summary>
        static string Base36Fraction(double fraction, int count)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            var sb = new StringBuilder();
            for (var i = 0; i < count && fraction > 0; i++)
            {
                fraction *= 36;
                var d = (int)Math.Floor(fraction);
                sb.Append(digits[d]);
                fraction -= d;
            }
            return sb.ToString();
        }

        public string NewPetId(long now)
        {
            return "pet-" + Base36(now) + "-" + Base36Fraction(_random.Next(), 4);
        }

        public string NewEggId(long now)
        {
            return "egg-" + Base36(now) + Base36Fraction(_random.Next(), 2);
        }

        /// <summary>一只刚出生的宠物（孵蛋 / 添加宠物 / 没有存档）。</summary>
        public PetRecord CreateFreshPetRecord(string speciesKey, string name, string id, long now)
        {
            if (speciesKey == null || !_data.SpeciesByKey.TryGetValue(speciesKey, out var sp)) sp = _data.SpeciesByKey["whale"];
            var today = PetRules.DayIndexOf(now, _zone);
            var skills = new SkillTable();
            foreach (var item in _data.Skills) skills.Rows[item.Key] = new SkillRecord { Xp = 0, Level = 1, Mastery = 0 };
            return new PetRecord
            {
                Id = id,
                Species = speciesKey,
                Name = string.IsNullOrEmpty(name) ? sp.Name : name,
                Avatar = sp.Avatar,
                Icon = sp.Icon,
                BornAt = now,
                Pet = new PetStats(),
                TotalFeeds = 0,
                TotalTokens = 0,
                TokensBySource = new TokensBySource(),
                Snacks = 5,
                Achievements = new List<string>(),
                Daily = DailyProgress.Empty(today),
                StreakDay = 0,
                StreakCount = 0,
                Pats = 0,
                Pos = new CardPos(),
                LastFeedAt = 0,
                Skills = skills,
                Memory = PetRules.CreateMemory(today),
                Prestige = 0,
                FormsOwned = new List<string>(),
            };
        }

        public PetSaveV2 Fresh(long now)
        {
            var save = new PetSaveV2 { SavedAt = 0, ActivePetId = "pet-0" };
            save.Pets["pet-0"] = CreateFreshPetRecord("whale", _config.PetName, "pet-0", now);
            return save;
        }

        // ─── 读 ───

        /// <summary>读一份存档文本（v2 直接清洗，v1 迁移，其余 / 读坏了都当新宠物）。</summary>
        public PetSaveV2 Load(string raw, long now)
        {
            if (!_config.Persist || raw == null) return Fresh(now);
            JToken parsed;
            try
            {
                parsed = JToken.Parse(raw);
            }
            catch (JsonException)
            {
                return Fresh(now);
            }
            if (!IsObject(parsed)) return Fresh(now);
            var v = Field(parsed, "v");
            if (IsNumber(v) && v.Value<double>() == 2) return SanitizeV2(parsed, now);
            if (IsNumber(v) && v.Value<double>() == 1) return MigrateV1(parsed, now);
            return Fresh(now);
        }

        PetRecord SanitizePetRecord(JToken raw, long now)
        {
            if (!IsObject(raw)) return null;
            var pet = Field(raw, "pet");
            if (!IsObject(pet)) return null;
            var today = PetRules.DayIndexOf(now, _zone);
            var rec = new PetRecord();
            SanitizeProgressFields(raw, today, rec);
            rec.Id = StringOf(Field(raw, "id")) ?? NewPetId(now);
            var species = StringOf(Field(raw, "species"));
            rec.Species = species != null && _data.SpeciesByKey.ContainsKey(species) ? species : "whale";
            var name = StringOf(Field(raw, "name"));
            rec.Name = !string.IsNullOrEmpty(name) ? (name.Length > 20 ? name.Substring(0, 20) : name) : "大肥鱼";
            rec.Avatar = StringOf(Field(raw, "avatar")) ?? "whale";
            rec.Icon = StringOf(Field(raw, "icon")) ?? "🐳";
            rec.BornAt = NumberIn(Field(raw, "bornAt"), 0, MaxSafeInteger, now);
            rec.Pet = SanitizePetFields(pet);
            rec.Prestige = (int)NumberIn(Field(raw, "prestige"), 0, 1000, 0);
            rec.FormsOwned = SanitizeIds(Field(raw, "formsOwned"), _data.FormByKey);
            return rec;
        }

        static GlobalStats SanitizeGlobal(JToken raw)
        {
            if (!IsObject(raw)) return new GlobalStats();
            var global = new GlobalStats
            {
                TotalTokensAllTime = NumberIn(Field(raw, "totalTokensAllTime"), 0, 1e15, 0),
                TotalFeedsAllTime = NumberIn(Field(raw, "totalFeedsAllTime"), 0, 1e12, 0),
                AchievementsUnlockedAllTime = NumberIn(Field(raw, "achievementsUnlockedAllTime"), 0, 1e6, 0),
                PetsHatched = NumberIn(Field(raw, "petsHatched"), 0, 1e6, 1),
            };
            if (Field(raw, "eggsObtained") is JArray obtained)
            {
                foreach (var item in obtained)
                {
                    var id = StringOf(item);
                    if (id != null) global.EggsObtained.Add(id);
                }
            }
            return global;
        }

        List<Egg> SanitizeEggs(JToken raw)
        {
            var result = new List<Egg>();
            if (!(raw is JArray array)) return result;
            for (var i = 0; i < array.Count && i < _data.MaxEggs; i++)
            {
                var e = array[i];
                if (!IsObject(e)) continue;
                var id = StringOf(Field(e, "id"));
                var type = StringOf(Field(e, "type"));
                if (id == null || type == null || !_data.EggTypeByKey.ContainsKey(type)) continue;
                result.Add(new Egg { Id = id, Type = type, ObtainedAt = NumberIn(Field(e, "obtainedAt"), 0, MaxSafeInteger, 0) });
            }
            return result;
        }

        PetSaveV2 SanitizeV2(JToken parsed, long now)
        {
            var save = new PetSaveV2();
            if (Field(parsed, "pets") is JObject rawPets)
            {
                var i = 0;
                foreach (var pair in rawPets)
                {
                    if (i++ >= _data.MaxPets) break;
                    var rec = SanitizePetRecord(pair.Value, now);
                    if (rec != null) save.Pets[rec.Id] = rec;
                }
            }
            if (save.Pets.Count == 0) return Fresh(now);
            var active = StringOf(Field(parsed, "activePetId"));
            save.ActivePetId = active != null && save.Pets.Contains(active) ? active : save.Pets.Ids[0];
            if (Field(parsed, "favorites") is JArray favorites)
            {
                foreach (var item in favorites)
                {
                    var id = StringOf(item);
                    if (id != null && save.Pets.Contains(id)) save.Favorites.Add(id);
                }
            }
            save.SavedAt = NumberIn(Field(parsed, "savedAt"), 0, MaxSafeInteger, 0);
            save.Eggs = SanitizeEggs(Field(parsed, "eggs"));
            save.Global = SanitizeGlobal(Field(parsed, "global"));
            return save;
        }

        /// <summary>v1（单宠物）→ v2：进度原样搬进 pet-0，全局统计从它起算。</summary>
        PetSaveV2 MigrateV1(JToken parsed, long now)
        {
            var pet = Field(parsed, "pet");
            if (!IsObject(pet)) return Fresh(now);
            var today = PetRules.DayIndexOf(now, _zone);
            var v1 = new PetRecord();
            SanitizeProgressFields(parsed, today, v1);
            var savedAt = NumberIn(Field(parsed, "savedAt"), 0, MaxSafeInteger, 0);
            v1.Pet = SanitizePetFields(pet);
            v1.Id = "pet-0";
            v1.Species = _config.PetAvatar == "whale" ? "whale" : _config.PetAvatar;
            v1.Name = _config.PetName;
            v1.Avatar = _config.PetAvatar;
            v1.Icon = _config.PetIcon;
            v1.BornAt = savedAt > 0 ? savedAt - 86400000 : now;
            v1.Prestige = 0;
            v1.FormsOwned = new List<string>();
            var save = new PetSaveV2 { SavedAt = savedAt, ActivePetId = "pet-0" };
            save.Pets["pet-0"] = v1;
            save.Global = new GlobalStats
            {
                TotalTokensAllTime = v1.TotalTokens,
                TotalFeedsAllTime = v1.TotalFeeds,
                AchievementsUnlockedAllTime = v1.Achievements.Count,
                PetsHatched = 1,
            };
            return save;
        }

        // ─── 写 ───

        /// <summary>存档正文（不含 v / savedAt）：PetCore 拿它当指纹判断「内容变没变」。</summary>
        public JObject Body(PetSaveV2 save)
        {
            var pets = new JObject();
            foreach (var id in save.Pets.Ids) pets[id] = RecordJson(save.Pets[id]);
            return new JObject
            {
                ["activePetId"] = save.ActivePetId,
                ["favorites"] = new JArray(save.Favorites),
                ["pets"] = pets,
                ["eggs"] = new JArray(save.Eggs.ConvertAll(e => (JToken)new JObject { ["id"] = e.Id, ["type"] = e.Type, ["obtainedAt"] = e.ObtainedAt })),
                ["global"] = new JObject
                {
                    ["totalTokensAllTime"] = save.Global.TotalTokensAllTime,
                    ["totalFeedsAllTime"] = save.Global.TotalFeedsAllTime,
                    ["achievementsUnlockedAllTime"] = save.Global.AchievementsUnlockedAllTime,
                    ["petsHatched"] = save.Global.PetsHatched,
                    ["eggsObtained"] = new JArray(save.Global.EggsObtained),
                },
            };
        }

        public string Serialize(JObject body, long savedAt)
        {
            var save = (JObject)body.DeepClone();
            save["v"] = _data.StateVersion;
            save["savedAt"] = savedAt;
            return save.ToString(Formatting.None);
        }

        JObject RecordJson(PetRecord r)
        {
            var skills = new JObject();
            foreach (var item in _data.Skills)
            {
                var row = r.Skills[item.Key];
                if (row != null) skills[item.Key] = new JObject { ["xp"] = row.Xp, ["level"] = row.Level, ["mastery"] = row.Mastery };
            }
            var p = r.Pet;
            var d = r.Daily;
            var m = r.Memory;
            return new JObject
            {
                ["id"] = r.Id,
                ["species"] = r.Species,
                ["name"] = r.Name,
                ["avatar"] = r.Avatar,
                ["icon"] = r.Icon,
                ["bornAt"] = r.BornAt,
                ["pet"] = new JObject
                {
                    ["hunger"] = p.Hunger, ["exp"] = p.Exp, ["level"] = p.Level, ["mood"] = p.Mood, ["energy"] = p.Energy,
                    ["curiosity"] = p.Curiosity, ["pride"] = p.Pride, ["concern"] = p.Concern, ["form"] = p.Form,
                },
                ["totalFeeds"] = r.TotalFeeds,
                ["totalTokens"] = r.TotalTokens,
                ["tokensBySource"] = new JObject
                {
                    ["user_input"] = r.TokensBySource.UserInput, ["generation"] = r.TokensBySource.Generation, ["tool_result"] = r.TokensBySource.ToolResult,
                },
                ["snacks"] = r.Snacks,
                ["achievements"] = new JArray(r.Achievements),
                ["daily"] = new JObject
                {
                    ["day"] = d.Day, ["feeds"] = d.Feeds, ["tools"] = d.Tools, ["bestCombo"] = d.BestCombo,
                    ["pats"] = d.Pats, ["tokens"] = d.Tokens, ["snacks"] = d.Snacks, ["done"] = new JArray(d.Done),
                },
                ["streakDay"] = r.StreakDay,
                ["streakCount"] = r.StreakCount,
                ["pats"] = r.Pats,
                ["pos"] = new JObject { ["dx"] = r.Pos.Dx, ["dy"] = r.Pos.Dy },
                ["lastFeedAt"] = r.LastFeedAt,
                ["skills"] = skills,
                ["memory"] = new JObject
                {
                    ["files"] = new JArray(m.Files.ConvertAll(x => (JToken)new JObject { ["name"] = x.Name, ["count"] = x.Count })),
                    ["tools"] = new JArray(m.Tools.ConvertAll(x => (JToken)new JObject { ["name"] = x.Name, ["count"] = x.Count })),
                    ["hours"] = new JArray(m.Hours),
                    ["bornDay"] = m.BornDay,
                    ["errors"] = m.Errors,
                    ["recoveries"] = m.Recoveries,
                },
                ["prestige"] = r.Prestige,
                ["formsOwned"] = new JArray(r.FormsOwned),
            };
        }
    }
}
