using System;
using System.IO;
using System.Linq;
using DeepseekPet.Core;

namespace DeepseekPet.BridgeCli
{
    /// <summary>
    /// --pet：在无界面的桥上挂一只 PetCore，每次状态变化往 stderr 打一行（等级 / 数值 / 气泡 / 新特效）。
    /// 数据表直接读 Unity 项目里的 Resources/PetData/pet-data.json。
    /// </summary>
    sealed class PetConsole
    {
        public PetCore Core;
        string _lastBubble;
        string _lastLine;
        readonly System.Collections.Generic.HashSet<string> _seen = new System.Collections.Generic.HashSet<string>();

        public static PetConsole Create(string savePath)
        {
            var data = PetData.Parse(File.ReadAllText(FindData()));
            var pet = new PetConsole
            {
                Core = new PetCore(data, PetConfig.Create(data), new SystemClock(), Mulberry32.FromTime(), new FileStorage(Path.GetFullPath(savePath))),
            };
            pet.Core.Changed += pet.Print;
            Console.Error.WriteLine("pet 存档 " + Path.GetFullPath(savePath));
            pet.Print();
            return pet;
        }

        static string FindData()
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var path = Path.Combine(dir.FullName, "DeepseekPet", "Assets", "Resources", "PetData", "pet-data.json");
                if (File.Exists(path)) return path;
            }
            throw new FileNotFoundException("往上找不到 DeepseekPet/Assets/Resources/PetData/pet-data.json");
        }

        void Print()
        {
            var s = Core.State;
            var p = s.Pet;
            var line = "pet Lv." + p.Level + " exp " + p.Exp + " 饥饿 " + p.Hunger + " 心情 " + p.Mood + " 精力 " + p.Energy
                + " 连击 " + s.ComboCount + " 累计 " + s.TotalFeeds + " 口 " + PetRules.FormatTokens(s.TotalTokens) + " tok"
                + " 技能 " + string.Join("/", Core.Data.Skills.Select(k => s.Skills[k.Key].Level)) + (s.Asleep ? " 💤" : "");
            if (line != _lastLine) Console.Error.WriteLine(line);
            _lastLine = line;
            if (s.Bubble != null && s.Bubble.Key != _lastBubble)
            {
                _lastBubble = s.Bubble.Key;
                Console.Error.WriteLine("pet 💬 [" + s.Bubble.Kind + "] " + s.Bubble.Text);
            }
            foreach (var e in s.Effects)
            {
                if (!_seen.Add(e.Key)) continue;
                Console.Error.WriteLine("pet " + e.Icon + " " + (e.Text ?? ("+" + e.FoodAmount + " 饱食 +" + e.ExpAmount + " 经验（" + e.Source + " " + e.Tokens + " tok）")));
            }
        }
    }
}
