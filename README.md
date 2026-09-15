# 明日方舟随机阵容生成器

跨平台版本（Windows / macOS / Linux），基于 [Avalonia](https://avaloniaui.net/) 与 [Material.Avalonia](https://github.com/AvaloniaCommunity/Material.Avalonia)，由早期 WPF 项目 [Arknights-StaffRandomSelect](https://github.com/juhkff/Arknights-StaffRandomSelect) 移植而来。

加入备用干员后，可以按勾选池随机生成通关阵容，也可用「随机策略」限制稀有度、职业或特定干员人数。

## 功能

- **阵容生成**：选择策略与人数，从已勾选干员中无放回随机组队
- **录入干员**：名称、稀有度、精英等级、职业
- **干员同步**：从国服游戏数据按所选稀有度追加正式干员
- **干员列表**：编辑名称 / 等级 / 稀有度 / 职业，勾选是否参与随机，删除干员
- **随机策略**：固定稀有度人数、职业人数（或范围）、指定干员子集人数

干员数据保存在程序目录下的 `StaffList.xml`，策略保存在 `RandomStrategies.json`，同步设置保存在 `OperatorSyncSettings.json`。可直接把旧项目同目录下的 `StaffList.xml` 和 `RandomStrategies.json` 拷过来继续用。

## 干员同步

在「干员录入」中点击「从数据库同步干员」，可在子窗口多选需要同步的稀有度，所选稀有度会保存供下次使用。默认数据源为 [Kengxxiao/ArknightsGameData](https://github.com/Kengxxiao/ArknightsGameData) 的国服游戏数据，GitHub Raw 不可用时会自动尝试 jsDelivr 镜像。

同步只处理所选稀有度、八大职业中已经可获得的正式干员，并始终追加到现有列表。新干员会按稀有度设置为可达到的满级，但默认不勾选，不会直接进入随机池；已存在的干员会被跳过新增，只校正来源标识、名称、稀有度和职业，绝不覆盖本地等级与勾选状态。手工录入的其它干员会保留，也不会因远端缺失而删除本地数据。

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
