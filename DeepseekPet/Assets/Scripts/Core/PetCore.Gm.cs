using System;
using System.Collections.Generic;

namespace DeepseekPet.Core
{
    // GM 控制台（调试用）：直接改数值，不走任何规则。
    public sealed partial class PetCore
    {
        void GmCommitPet(PetStats pet, bool writeRecord)
        {
            // 和 JS 一样顺手写进 activeRecord.pet（那份档案此时未必还在集合里，写了也无害）。
            if (writeRecord) _activeRecord.Pet = ProgressOf(pet);
            var patch = new Patch();
            patch.Pet.Set(pet);
            Commit(patch);
        }

        public void GmSetLevel(double n)
        {
            Update();
            var pet = _state.Pet.Clone();
            pet.Level = (int)Math.Max(1, Math.Floor(n));
            pet.Exp = 0;
            GmCommitPet(pet, true);
        }

        public void GmAddExp(double n)
        {
            Update();
            GmCommitPet(PetRules.FeedPet(_data, _state.Pet, 0, (int)Math.Max(0, Math.Floor(n))), true);
        }

        public void GmSetHunger(double n)
        {
            Update();
            var pet = _state.Pet.Clone();
            pet.Hunger = PetRules.Clamp((int)Math.Floor(n), 0, 100);
            GmCommitPet(pet, false);
        }

        public void GmSetMood(double n)
        {
            Update();
            var pet = _state.Pet.Clone();
            pet.Mood = PetRules.Clamp((int)Math.Floor(n), 0, 100);
            GmCommitPet(pet, false);
        }

        public void GmSetEnergy(double n)
        {
            Update();
            var pet = _state.Pet.Clone();
            pet.Energy = PetRules.Clamp((int)Math.Floor(n), 0, 100);
            GmCommitPet(pet, false);
        }

        public void GmFillSnacks()
        {
            Update();
            var patch = new Patch();
            patch.Snacks.Set(_config.ManualSnackMax);
            Commit(patch);
        }

        public void GmUnlockAllAchievements()
        {
            Update();
            var patch = new Patch();
            patch.Achievements.Set(_data.Achievements.ConvertAll(a => a.Id));
            Commit(patch);
        }

        public void GmGrantEggs()
        {
            Update();
            var now = Now;
            foreach (var type in _data.EggTypes)
            {
                if (_eggs.Count >= _data.MaxEggs) break;
                _eggs.Add(new Egg { Id = _save.NewEggId(now), Type = type.Key, ObtainedAt = now });
            }
            var patch = new Patch();
            patch.Eggs.Set(new List<Egg>(_eggs));
            Commit(patch);
        }

        /// <summary>模拟一口 generation（不走连击 / 挑食 / 成就，只动数值）。</summary>
        public void GmSimulateFeed(long tokens)
        {
            Update();
            var tk = tokens == 0 ? 1000 : tokens;
            var now = Now;
            var pet = SettleVitals(now);
            var food = (int)PetRules.Clamp(JsMath.Round(PetRules.FoodFromTokens(tk, _config)), _config.MinFood, _config.MaxFood);
            var prestigeBonus = 1 + _state.Prestige * _config.PrestigeExpBonus;
            var exp = (int)Math.Max(1, Math.Floor(_data.BaseExp[FeedSource.Generation] * prestigeBonus + 0.5));
            var patch = new Patch();
            patch.Pet.Set(PetRules.FeedPet(_data, pet, food, exp, _config.MoodPerFeed, -_config.EnergyPerFeed));
            patch.TotalFeeds.Set(_state.TotalFeeds + 1);
            patch.TotalTokens.Set(_state.TotalTokens + tk);
            patch.LastFeedAt.Set(now);
            Commit(patch);
        }
    }
}
