# 第三方资源与许可

| 资源 | 用在哪 | 许可 | 来源 |
|---|---|---|---|
| Twemoji 图形 | `DeepseekPet/Assets/Resources/Emoji/*.png`（彩色 emoji 图标；由 `PetTools fetch-emoji` 从 SVG 栅格出来） | [CC-BY 4.0](https://creativecommons.org/licenses/by/4.0/) | Copyright 2020 Twitter, Inc and other contributors；Twemoji 社区维护版 [jdecked/twemoji](https://github.com/jdecked/twemoji)，npm 包 `@twemoji/svg@15.0.0` |
| Newtonsoft.Json | 桥、PetCore、PetTools | MIT | Unity 包 `com.unity.nuget.newtonsoft-json` / NuGet |
| Jint | PetTools（只在开发时运行原 JS，不进桌宠程序） | BSD-2-Clause | NuGet `Jint` |
| Svg.Skia / SkiaSharp | PetTools（栅格化 SVG，不进桌宠程序） | MIT | NuGet `Svg.Skia` |

鲸鱼娘立绘（`DeepseekPet/Assets/Characters/Sprite/Resources/WhaleSprites`）是 AI 生成的同人形象；四种动物形态（`FormSprites`）由网页版插件的矢量几何（`PetTools/spec/art.js`）栅格出来。两者都随本项目一同提供。

以后接 Live2D（Cubism SDK）、Spine（spine-unity）、UniVRM 等，在这里登记它们的许可（Cubism 和 Spine 的运行时许可有发行限制，上 Steam 前要核对）。
