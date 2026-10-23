# 开发说明

给想改代码的人：目录结构、事件桥怎么接三家、养成逻辑怎么和原 JS 保持一致、窗口是怎么做透明和穿透的。用法看 [README](../README.md)。

## 来历

这个项目最早是 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 网页界面里的宠物插件（deepseek-pet 仓库的 `dsh-pet-plugin`），后来做成了独立的 Windows 桌宠，分了四步：

- **P0 事件桥**（完成）：Claude Code / Codex / dsh 三家的事件实时、不重复地进到 Unity。
- **P1 养成逻辑**（完成）：原网页版的整套养成规则（连击、食物、经验、心情 / 精力 / 睡眠、情绪三维、技能 / 精通、记忆、提示、关怀、每日任务、成就、进阶 / 进化、蛋、多宠物、轮回、小游戏）移植成纯 C# 的 `PetCore`，和原 JS 逐字段一致。
- **P2 桌宠表现**（完成）：透明置顶的小窗口 + 立绘角色 + 气泡 / 特效 / 状态条 / 右键菜单。角色渲染走 `ICharacterView`，以后换 Live2D / Spine / 3D 只是多一个实现。
- **P3 其余系统的界面**（完成）：四种动物形态的立绘、彩色 emoji、宠物面板（状态 / 技能 / 成就 / 任务 / 蛋 / 宠物）、小游戏、托盘图标。

## 目录

```
./
  DeepseekPet/                        Unity 2022.3 项目
    Assets/Scripts/Bridge/            事件桥（纯 C#，不引用 UnityEngine）
    Assets/Scripts/Core/              养成逻辑 PetCore（纯 C#，不引用 UnityEngine）
    Assets/Scripts/App/               BridgeHost（桥 + F1 面板）、PetHost（宠物 + F2 面板）
    Assets/Resources/PetData/         pet-data.json：数据表，PetTools 生成，别手改
    Assets/Editor/                    Tools / 桌宠 / 发测试事件
  BridgeTests/                        dotnet 侧（不开 Unity 也能编译、测试）
    Bridge/ Core/                     用 netstandard2.1 + C# 9 编译同一份源码（对齐 Unity 的 API 和语言版本）
    Bridge.Tests/                     dotnet test BridgeTests/Bridge.Tests
    Core.Tests/                       dotnet test BridgeTests/Core.Tests（含金样回放，Traces/*.json.gz）
    BridgeCli/                        无界面跑桥：dotnet run --project BridgeTests/BridgeCli
  PetTools/                           用 Jint（纯 C# 的 JS 解释器）执行原 JS，见下
```

## 三家怎么接

| 来源 | 怎么到桥 | 要做什么 |
|---|---|---|
| Claude Code | `~/.claude/settings.json` 里的 http 钩子 POST 到 `/hook/claude-code`；token 用量从 transcript 读 | F1 面板点「连接 Claude Code」（会先备份 settings.json），新开的会话生效 |
| Codex | 桥自己追 `~/.codex/sessions/` 下的会话文件 | 什么都不用做 |
| dsh | deepseek-pet 仓库里的 `dsh-pet-plugin`：它的 node 半订阅 `session/event`，POST 到 `/event` | 在 dsh 里装上那个插件 |

端口和口令在 `~/.deepseek-pet/bridge.json`，桥第一次启动时生成；三方都读这一份。

## 宠物（PetCore）

PetHost 把桥的事件按网页版 `plugin.js` 的路由喂给 PetCore：`tool_call` 只长技能、记记忆，`tool_result` 先看成败再喂，`user_input` / `generation` 直接喂。每帧 `Update()` 跑定时器，每 10 秒 `Tick()` 结算随时间走的数值。

| 文件 | 内容 |
|---|---|
| `~/.deepseek-pet/pet-save.json` | 存档。格式和网页版 `localStorage["dsh-pet-plugin/state"]` 相同；写 `.tmp` 再替换，上一版留在 `.bak` |
| `~/.deepseek-pet/pet-config.json` | 可选的调参，格式同网页版的 `dsh-pet-plugin/config`（只收和默认值同类型的字段） |

**从网页版搬家**：在 dsh 网页的开发者工具里执行 `copy(localStorage["dsh-pet-plugin/state"])`，再点 F2 面板的「从剪贴板导入网页存档」。原存档会另存为 `pet-save.json.before-import`。

不开 Unity 也能养：`BridgeCli --pet <存档路径>`，宠物的每次变化打在 stderr 上。

### 和原 JS 怎么保持一致

原 JS（网页版插件 `dsh-pet-plugin/lib/src` 下的 config / art / constants / logic / store，快照在 [`PetTools/spec/`](../PetTools/spec/)）是规格，C# 是逐行移植。`PetTools` 在 Jint 里直接跑原 JS（想对着插件的最新源码跑，加 `--spec <目录>`）：

```
dotnet run --project PetTools -- export-data   # 数据表 → Assets/Resources/PetData/pet-data.json
dotnet run --project PetTools -- gen-traces    # 预设剧本 → BridgeTests/Core.Tests/Traces/*.json.gz（金样）
dotnet run --project PetTools -- fuzz --seeds 60 --steps 2000   # 随机剧本，JS 和 C# 同步跑、逐步比对，不落盘
```

- 剧本是「像真人在用」的一串操作：一轮轮 agent 活动、几秒到一天多的空闲、摸头、零食、小游戏、孵蛋、换宠物、轮回、GM 改数值、关掉再打开（离线结算）。时间、定时器、随机数（mulberry32）、时区两边都是注入的，所以结果可以逐位比对。
- 比对的是每一步之后的完整状态：数值、连击、气泡的文案、每条特效、技能、记忆、任务、成就、蛋、多宠物、全局统计，以及最后写出的存档。
- 2026-10-22：8 个预设剧本（约 7900 步）和 60 个随机剧本（12 万步、6 个时区）全部一致。

**要改规则**：先改 JS，跑 `export-data` + `gen-traces`，再改 C# 直到 `dotnet test BridgeTests/Core.Tests` 通过。等 P4 删掉网页宠物之后，就由 C# 成为规格，届时把金样测试一起删掉。

带函数的表（成就判定、任务进度、蛋里程碑、称号、动态台词 / 提示 / 闲聊模板）在 `PetTables.cs` 里手写，按 id 对上 pet-data.json；JS 那边多了 / 少了一条，PetData 加载时直接报错。

移植时顺手修了原 JS 的三个 bug（JS 也一起改了，金样按修好的行为生成）：

1. 新宠物的每日进度少了 `pats / tokens / snacks` 三个字段，算出 NaN，「摸头 5 次」这类任务第一口饭就自动完成。
2. 新宠物的技能初始等级是 0，第一次用工具就弹「技能 · Lv.1」。
3. 改名会用开机那一刻的档案重建状态，把这期间攒的进度全部回滚。

## 桌宠窗口（P2）

```
Assets/Scripts/Platform/          DesktopWindow：全项目唯一调 Win32 的地方（只在 Windows 播放器里生效）
Assets/Scripts/Presentation/      ICharacterView（角色接口）、PetPresenter（状态 → 画面、鼠标、窗口）、气泡 / 特效 / 菜单
Assets/Scripts/Characters/Sprite/ SpriteCharacterView：现有立绘（Assets/Characters/Sprite/Resources/WhaleSprites，4 档 × 7 表情）
Assets/Editor/PetBuild.cs         打包：Tools / 桌宠 / 打包 Windows → Build/DeepseekPet.exe
```

- 窗口只有宠物那么大（300×380，按 DPI 缩放），默认在主屏右下角，拖宠物就是拖窗口，位置记在 `~/.deepseek-pet/window.json`。没有铺满全屏：全屏大小的无边框窗口会被 Windows 当成全屏应用（勿扰、任务栏行为都会变），每帧也要画一整屏。
- 透明：DWM 把边框扩进客户区 + 相机清成 alpha 0；Player 设置必须关掉 DXGI flip model、只用 D3D11（PetBuild 会钉死这些设置）。UI 用预乘 alpha 的材质（`PetUIPremultiplied.shader`），否则字边发虚。
- 点击穿透：鼠标不在角色（按立绘的 alpha 判断）和菜单上时整窗加 `WS_EX_TRANSPARENT`；点它不抢焦点（`WS_EX_NOACTIVATE`），摸一下宠物不会打断正在打字的编辑器。
- 操作：单击摸头；按住拖动换位置；右键菜单（喂零食、摸头、换下一只、调试面板、退出）；悬停显示状态条。打开「调试面板」时窗口放大到能摆下 F1 / F2 两个面板。
- 没动静时降到 12 帧，有动画 / 气泡 / 悬停时 30 帧；每 3 秒重新压一次置顶。
- 排查：设环境变量 `DEEPSEEK_PET_TRACE=1` 再启动，每秒往 `%USERPROFILE%\AppData\LocalLow\deepseek-pet\DeepseekPet\Player.log` 打一行光标 / 窗口 / 命中状态。

## P3：面板、形态、emoji、托盘

- **四种动物的立绘**：`PetTools render-art` 在 Jint 里跑网页版的 `svgAvatar`（`art.js` 的矢量几何），照着 styles.js 处理摸头时的笑眼，再用 Svg.Skia 栅格成 512px PNG（6 种表情 + 3 张情绪三维的脸），放在 `Assets/Characters/Sprite/Resources/FormSprites`。进化成代码猫 / 探索狐 / 调试虫 / 文鸟，或者孵出来的就是这几个种族，都用这一套；动物在平常状态下会换上情绪三维的脸。
- **彩色 emoji**：`PetTools fetch-emoji` 从 npm 拿 `@twemoji/svg`（校验 sha512），只取用到的那些（pet-data.json 里的图标字段 + 代码里写的几个），栅格成 96px PNG 放进 `Assets/Resources/Emoji`。图标位置（食物、通知、状态条、菜单、面板）画图；台词气泡里没有 emoji。许可见 [THIRD_PARTY.md](../THIRD_PARTY.md)。
- **宠物面板**：右键菜单 / 托盘菜单「宠物面板」。窗口往左扩出一块，宠物留在原地。六页：状态（数值、累计、换形态、小游戏、轮回 —— 轮回要点两下确认）、技能（等级 / 精通 / 记忆）、成就、今日任务、蛋（按种类叠放，一键孵化）、宠物（切换、收藏、直接添加）。
- **小游戏**：几样食物摆在宠物框里，按顺序点中当前那个（一跳一跳的那个），超时判输 —— 规则和网页版一样。
- **托盘图标**：🐳，左右键都弹菜单：显示 / 隐藏桌宠、宠物面板、调试面板、退出。资源管理器重启后会自己加回来。
- 诊断：`DEEPSEEK_PET_TRACE=panel` 启动后自己翻一遍面板六页、开一局小游戏，每一步存一张 `~/.deepseek-pet/trace-*.png`。

### 高 DPI 与多显示器（2026-10-23 实测）

桌宠窗口是「按显示器感知 DPI」的（Windows 报的感知级别是 2），窗口大小、界面缩放、点中判定都乘上所在显示器的缩放；拖到 DPI 不同的屏上会按新的缩放重算窗口大小（右下角不动）。

- 这台机器只有一块 1920×1080 / 100% 的屏。缩放用诊断开关 `DEEPSEEK_PET_SCALE=1.5`（只替换「系统报的 DPI」这一步）测了 150% / 200%：窗口 450×570 / 600×760，面板六页、小游戏的排版都等比放大、字是按放大后的字号重新栅格的。
- 记住的位置落在一块已经不存在的屏上（x=3000 / x=-1500）、屏幕上方 / 下方：都拉回最近那块屏的工作区里。左右允许探出四分之一，上下不许（头顶是气泡、底下是状态条）。
- 宠物靠在屏幕左边时打开面板：窗口往右让，面板不会跑到屏幕外。
- 没测：真的把系统缩放改成 150%、真的第二块屏。

还没做：改名（要接中文输入法，面板里先没放）、名片图（网页版用 canvas 画的那张）。

## 在编辑器里跑

1. Unity Hub 打开 `DeepseekPet`，随便开一个空场景，点 Play。左上角是桥的面板（F1），右边是宠物面板（F2）。编辑器里不改窗口（不会把 Unity 编辑器自己变透明），Game 视图里就是宠物。
2. 右键宠物点「连接 Claude Code」（或 F1 面板上的按钮），然后新开一个 Claude Code 会话随便问点什么；跑一次 `codex`；在 dsh 里跑一个会话。
3. 宠物面板里的数值跟着动、气泡和特效流水在刷，就是通了。

装 / 卸 Claude 钩子也可以不开 Unity：`BridgeCli --install-claude` / `--uninstall-claude`（加 `--dry-run` 只打印改完的 settings.json）。

## 已知限制

- Claude Code 不给 `SessionStart` 跑 http 钩子，所以没有「会话开始」事件。
- Claude 的 http 钩子是同步的（只有 command 钩子能异步）：桥收到就回 204，正常只耽误几毫秒。桥没开时连接被拒、不阻塞也不报错（2026-10-22 真机确认）。
- Codex 续接旧会话时，codex.exe 一直开着会话文件，Windows 不更新它的修改时间：桥按文件长度判断有没有新内容，并扫整个 sessions 目录。
- 子 agent（Task）自己的生成暂时不计：它们写在单独的 transcript 里。
- 同一台机器只跑一份桌宠（命名 Mutex）；第二份既不起桥也不起宠物，免得两份抢同一个存档。`BridgeCli --pet` 不受这个限制，别和 Unity 共用同一个存档文件。

## 发布包与一键接入

- `scripts/package.ps1`：Unity 打包 → `dist/deepseek-pet-desktop-<VERSION>-win64.zip`。zip 里带上 `scripts/zip/` 下的东西（`连接AI助手.cmd` / `断开AI助手.cmd` / `tools/connect.ps1` / 使用说明），以及 dsh 插件包（默认取旁边 deepseek-pet 仓库 `dist/` 里最新的 `dsh-pet-plugin-*.tgz`，先在那边跑 `node pack.mjs`）。
- `tools/connect.ps1`：Claude Code 交给桌宠程序自己的安装器（`DeepseekPet.exe -batchmode -nographics --connect-claude --result <文件>`，见 `App/CommandLine.cs`，和右键菜单是同一份代码）；Codex 只检查目录；dsh 找到 `dsh` 命令就 `dsh plugin --profile web add <tgz>`。最后启动桌宠。
- Player 设置里关掉了 Unity 的「强制单实例」：它会把带参数的第二次启动转给已经开着的那份，一次性命令就跑不成了。两份桌宠的问题由 BridgeHost 的 Mutex 挡住，第二份自己退出。
- 脚本是 UTF-8 带 BOM 的（Windows PowerShell 5 不带 BOM 会按 GBK 读，中文会乱）；`.cmd` 里只放 ASCII。
