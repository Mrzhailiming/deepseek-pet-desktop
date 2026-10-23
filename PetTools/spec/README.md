# 原 JS 规格（快照）

这几份是网页版宠物插件 `dsh-pet-plugin/lib/src/` 下的源码（deepseek-pet 仓库，提交 5390961 之上、含 2026-10-22 修的三个 bug），
原样拷过来给 PetTools 用：

- `export-data`：执行 config / constants，导出数据表 → `DeepseekPet/Assets/Resources/PetData/pet-data.json`
- `gen-traces` / `fuzz`：在 Jint 里跑 store，生成金样轨迹 / 和 C# 的 PetCore 逐步比对
- `render-art`：跑 views 的 `svgAvatar`，栅格出四种动物的立绘

**别在这里改规则。** C# 的 PetCore 和这份 JS 逐字段一致（金样测试守着）。以后要么先改这里的 JS、重新生成数据表和金样，
再改 C#；要么宣布 C# 成为规格，把这份快照和金样测试一起删掉。
