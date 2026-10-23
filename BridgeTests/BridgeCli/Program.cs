using System;
using System.Threading;
using DeepseekPet.Bridge;
using Newtonsoft.Json;

namespace DeepseekPet.BridgeCli
{
    /// <summary>
    /// 无界面跑桥：stdout 每行一条过了闸门的事件（JSON），stderr 是桥的日志。
    /// 参数：--config 桥配置路径（默认 ~/.deepseek-pet/bridge.json）、--codex-root Codex 会话目录、
    ///       --no-codex 不追 Codex、--capture 保存原始负载。Ctrl+C 退出。
    /// </summary>
    static class Program
    {
        static int Main(string[] args)
        {
            string config = null, codexRoot = null;
            var watchCodex = true;
            var capture = false;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // 一次性命令：装 / 卸 Claude Code 钩子（和 Unity 面板上的按钮是同一个安装器）。
            //   --install-claude [--dry-run]   --uninstall-claude [--dry-run]
            var dryRun = Array.IndexOf(args, "--dry-run") >= 0;
            if (Array.IndexOf(args, "--install-claude") >= 0 || Array.IndexOf(args, "--uninstall-claude") >= 0)
            {
                var install = Array.IndexOf(args, "--install-claude") >= 0;
                var settings = ClaudeHookInstaller.DefaultSettingsPath();
                var current = System.IO.File.Exists(settings) ? System.IO.File.ReadAllText(settings) : null;
                if (dryRun)
                {
                    var bridge = BridgeConfig.LoadOrCreate(BridgeConfig.DefaultPath());
                    Console.WriteLine(install ? ClaudeHookInstaller.PreviewInstall(current, bridge) : ClaudeHookInstaller.PreviewUninstall(current));
                    return 0;
                }
                var backup = install
                    ? ClaudeHookInstaller.Install(BridgeConfig.LoadOrCreate(BridgeConfig.DefaultPath()))
                    : ClaudeHookInstaller.Uninstall();
                Console.WriteLine((install ? "已安装到 " : "已卸载 ") + settings + (backup != null ? "，原文件备份在 " + backup : ""));
                return 0;
            }
            string petSave = null;
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--config" && i + 1 < args.Length) config = args[++i];
                else if (args[i] == "--codex-root" && i + 1 < args.Length) codexRoot = args[++i];
                else if (args[i] == "--no-codex") watchCodex = false;
                else if (args[i] == "--capture") capture = true;
                // 顺便挂一只宠物（PetCore），事件喂给它，每次变化往 stderr 打一行。参数是存档路径。
                else if (args[i] == "--pet" && i + 1 < args.Length) petSave = args[++i];
            }

            using (var hub = new BridgeHub(config, codexRoot: codexRoot))
            {
                hub.Start(watchCodex);
                hub.CaptureRaw = capture;
                Console.Error.WriteLine("ready port=" + hub.Port);
                var pet = petSave == null ? null : PetConsole.Create(petSave);
                var stop = new ManualResetEventSlim();
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Set(); };
                var nextTick = DateTime.UtcNow.AddSeconds(10);
                while (!stop.Wait(50))
                {
                    while (hub.Events.TryDequeue(out var e))
                    {
                        Console.WriteLine(JsonConvert.SerializeObject(e));
                        pet?.Core.HandleEvent(e.Kind, e.Tokens, e.Output, e.Tool, e.CallId, e.File, e.Failed);
                    }
                    while (hub.Logs.TryDequeue(out var log)) Console.Error.WriteLine(log);
                    if (pet != null)
                    {
                        pet.Core.Update();
                        if (DateTime.UtcNow >= nextTick)
                        {
                            nextTick = DateTime.UtcNow.AddSeconds(10);
                            pet.Core.Tick();
                        }
                    }
                    Console.Out.Flush();
                }
                pet?.Core.Dispose();
            }
            return 0;
        }
    }
}
