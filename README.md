# 明日方舟随机阵容生成器

跨平台版本（Windows / macOS / Linux），基于 [Avalonia](https://avaloniaui.net/) 与 [Material.Avalonia](https://github.com/AvaloniaCommunity/Material.Avalonia)，由早期 WPF 项目 [Arknights-StaffRandomSelect](https://github.com/juhkff/Arknights-StaffRandomSelect) 移植而来。

加入备用干员后，可以按勾选池随机生成通关阵容，也可用「随机策略」限制稀有度、职业或特定干员人数。

## 功能

- **阵容生成**：选择策略与人数，从已勾选干员中无放回随机组队
- **录入干员**：名称、稀有度、精英等级、职业
- **干员列表**：编辑名称 / 等级 / 稀有度 / 职业，勾选是否参与随机，删除干员
- **随机策略**：固定稀有度人数、职业人数（或范围）、指定干员子集人数

干员数据保存在程序目录下的 `StaffList.xml`，策略保存在 `RandomStrategies.json`。可直接把旧项目同目录下的这两个文件拷过来继续用。

## 运行

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download)。

```bash
dotnet run --project arknights-random-team.csproj
```

发布示例：

```bash
dotnet publish -c Release -r win-x64 --self-contained false
dotnet publish -c Release -r osx-arm64 --self-contained false
dotnet publish -c Release -r linux-x64 --self-contained false
```
