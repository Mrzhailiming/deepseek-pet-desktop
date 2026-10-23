using System;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;

namespace DeepseekPet.Bridge
{
    /// <summary>
    /// 桥的端口与口令，落在固定位置 `~/.deepseek-pet/bridge.json`。
    ///
    /// 不放 Unity 的 persistentDataPath：那个目录跟着公司名 / 产品名走，而 dsh 转发插件、
    /// Claude 钩子安装器、PetHookRelay 都得能找到同一份端口和口令。
    /// 口令首次启动时随机生成；只为挡住「本机别的网页 / 程序顺手 POST 进来」，不是强安全边界
    /// （监听本来就只绑回环）。
    /// </summary>
    public sealed class BridgeConfig
    {
        public const int DefaultPort = 47823;

        [JsonProperty("port")] public int Port = DefaultPort;
        [JsonProperty("token")] public string Token = "";

        /// <summary>~/.deepseek-pet，整个桌宠在 Unity 之外的落脚点。</summary>
        public static string HomeDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".deepseek-pet");
        }

        public static string DefaultPath()
        {
            return Path.Combine(HomeDir(), "bridge.json");
        }

        /// <summary>读配置；没有或读坏了就新建一份（新口令）并写回。</summary>
        public static BridgeConfig LoadOrCreate(string path)
        {
            BridgeConfig config = null;
            try
            {
                if (File.Exists(path)) config = JsonConvert.DeserializeObject<BridgeConfig>(File.ReadAllText(path));
            }
            catch (JsonException)
            {
                config = null;
            }
            catch (IOException)
            {
                config = null;
            }
            if (config != null && !string.IsNullOrEmpty(config.Token) && config.Port > 0 && config.Port < 65536)
            {
                return config;
            }
            config = new BridgeConfig { Port = config != null && config.Port > 0 ? config.Port : DefaultPort, Token = NewToken() };
            config.Save(path);
            return config;
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(this, Formatting.Indented));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static string NewToken()
        {
            var bytes = new byte[24];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
