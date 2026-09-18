# 工程与体验优化方案 v1

## 0. 方案定位

本方案回答一个问题：**这个项目现在最该改什么、按什么顺序改、怎么证明改对了。**

与前一份《[客户端与网页端 UI 美化方案 v1](UI-BEAUTIFICATION-PLAN.md)》的关系：那份管「界面对不对」，本份管「数据丢不丢、程序崩不崩、新手走得下去吗、性能有没有上界」。两者不重叠，UI 方案里标注「待实机验收」的项在本份第 8 节仍然保留。

**审查方法**：5 路并行只读审计（架构与代码质量、数据持久化与业务逻辑、图片管线与性能、工程化与交付、交互与无障碍），加上我自己新增的无头截图与输入模拟实测（20 张界面截图 + 3 张真实立绘截图）。所有结论都带 `文件:行号` 证据，并区分两档口径：

- **已验证**：审计逐行核对过代码，或我用工具实际跑出来（构建、界面加载、虚拟化计数、模拟点击、真实立绘截图）。
- **待实机**：需要真机、真浏览器、真显示器才能定论（帧率、触摸手感、读屏、缩放、WASM 首屏字节）。

**本次为审查新增的工具**（当前在工作区，未提交）：[tools/SmokeTest](tools/SmokeTest/Program.cs) 从「只跑布局的冒烟测试」升级为**无头截图 + 输入模拟**工具，两种模式：

```bash
# 检查模式：全界面加载、布局、绑定错误、虚拟化计数
dotnet run --project tools/SmokeTest/arknights-random-team.SmokeTest.csproj

# 截图模式：+20 张界面截图，模拟点击卡片与 Tab 焦点
dotnet run --project tools/SmokeTest/arknights-random-team.SmokeTest.csproj -- shots artifacts/ui-review

# 真实立绘模式：换成 12 名真实干员联网取图，验证卡片填充与裁切
dotnet run --project tools/SmokeTest/arknights-random-team.SmokeTest.csproj -- shots artifacts/ui-review-real real
```

**本次实测已确认的事实**（这些不再是推断）：

| 项 | 结果 |
| --- | --- |
| 三个工程构建 | 0 警告 0 错误（desktop / browser / smoke） |
| 界面加载与绑定 | 全部界面加载完成，**零绑定错误** |
| 卡片虚拟化 | 51 名干员只实例化 **19 个**卡片控件 |
| 卡片点击 | 模拟鼠标点击整卡：入池 `True → False`（交互链通） |
| 真实立绘 | 12 名干员全身立绘正常下载渲染、裁切合理、顶部遮罩下「6★/入池」可读 |
| 窄屏 | 390 宽外壳、700 宽策略编辑器单栏重排均正常 |

---

## 1. 结论摘要

**功能与视觉的完成度比预想高，真正的欠债在两条线上：「数据可能悄悄存不进去」和「一个坏文件就能让程序起不来」。** 其次是新手主路径有两处硬断点，以及图片缓存没有任何上界。

| 档位 | 条数 | 一句话 |
| --- | --- | --- |
| P0 数据安全与崩溃 | 6 | 落盘非原子、加载无兜底、越界数据能崩、15 个 `async void` 裸奔、订阅泄漏、同步失败不回滚 |
| P1 用户可见的正确性与体验断裂 | 13 | 同步文案与事实不符、新手路径断点、重试无效、状态机缺一态、焦点与无障碍基本盘缺失 |
| P2 性能与工程化 | 16 | 图片管线无内存/磁盘上界、字体撑大首屏、CI 不跑唯一的回归测试、无版本号与依赖锁定 |
| 架构类改造 | 6 | 分层反向依赖、Core 通配符编译、重复实现、静态单例阻断单测 |

**排期原则**：先修「会丢数据/会崩」，再修「会误导用户/会卡住新手」，最后做性能与工程化。理由见第 9 节。

---

## 2. P0：数据安全与崩溃（必须先做）

| # | 问题 | 证据 | 后果 | 修法 | 怎么验证 |
| --- | --- | --- | --- | --- | --- |
| P0-1 | 四处落盘都是「直接覆盖」，且 `SaveStaff` 先把旧文件 Load 回来再覆写同一路径 | [AppState.cs:246-247](AppState.cs#L246-L247) `xmlDocument.Load(StaffPath)` → [AppState.cs:271](AppState.cs#L271) `xmlDocument.Save(StaffPath)`；[AppState.cs:119](AppState.cs#L119)、[AppState.cs:223](AppState.cs#L223)、[StrategyPersistence.cs:51](Domain/StrategyPersistence.cs#L51) 的 `File.WriteAllText` | 写一半崩溃 → 文件损坏 → **下次退出保存时 Load 抛异常，从此再也存不进去**；磁盘上只剩一份坏文件 | 统一 `临时文件 → File.Replace → 保留 .bak`（[LocalImageStore.cs:100-107](Views/LocalImageStore.cs#L100-L107) 已有正确写法可抄）；Load 失败时改用全新文档而不是抛错 | 写入过程中杀进程，重启后数据完好；手工把文件截断一半，重启不崩且能继续保存 |
| P0-2 | 启动加载全程无 try/catch，损坏或越界数据直接抛异常 | [AppState.cs:131](AppState.cs#L131) `XDocument.Load` 无保护；[AppState.cs:162](AppState.cs#L162) `new Level(elite, rank)` → [Level.cs:103](Models/Level.cs#L103) `throw new ArgumentOutOfRangeException`；[StrategyPersistence.cs:21-22](Domain/StrategyPersistence.cs#L21-L22) `ReadAllText + Deserialize` 无保护；调用点 [App.axaml.cs:14](App.axaml.cs#L14) 无兜底 | `StaffList.xml` 里写个「精英阶段=3」或 JSON 被截断 → **程序启动即崩，用户无从自救** | 加载层统一 try/catch：损坏文件改名保留（`.corrupt-<时间>`）→ 降级为空池 → 界面可见告警；等级/星级在解析处夹取到合法区间 | 新增损坏数据夹具（截断 XML、`elite=3`、`star=9`、空 JSON），断言「能启动 + 有提示 + 原文件被保留」 |
| P0-3 | 星级只 `TryParse` 不校验，越界值会一路流到数组下标 | [AppState.cs:147](AppState.cs#L147) `Star = int.TryParse(...) ? star : 1`；[ConstrainedTeamPicker.cs:412](Domain/ConstrainedTeamPicker.cs#L412) `_unusedStar[s.Star]++`（数组长 7，另见 [638](Domain/ConstrainedTeamPicker.cs#L638)、[707](Domain/ConstrainedTeamPicker.cs#L707)） | 手工改过的存档里出现 `star=0/7/-3` → **生成阵容时下标越界崩溃**（[GenerateView.axaml.cs:205](Views/GenerateView.axaml.cs#L205) 无 try/catch） | 星级在加载与编辑两处都夹取到 1–6；把「星级/等级合法区间」收进 `Staff`/`Level` 的校验方法，加载、同步、UI 共用一份 | 夹具里塞 `star=9`，断言生成不再崩且数据被夹取；单测覆盖 `Level`/`Staff` 校验 |
| P0-4 | 15 个 `async void` 事件处理器全部没有 try/catch，也没有全局兜底 | `grep -c "async void"` = 15，其中带 try 的 = 0（[GenerateView.axaml.cs:129](Views/GenerateView.axaml.cs#L129)、[InputView.axaml.cs:34](Views/InputView.axaml.cs#L34)、[StaffListView.axaml.cs:160](Views/StaffListView.axaml.cs#L160)、[StrategyEditorDialog.axaml.cs:358](Views/StrategyEditorDialog.axaml.cs#L358)、[RandomStrategyView.axaml.cs:30](Views/RandomStrategyView.axaml.cs#L30) 等）；全仓无 `UnhandledException`/`TaskScheduler` 兜底 | 任何一个 await（对话框、网络、文件）抛异常 → **进程直接退出**，用户视角就是「点一下闪退」 | 统一 `SafeFireAndForget(async () => ...)` 包装（内部 catch + 用 Snackbar/Alert 报错）；再加全局未处理异常兜底把堆栈写日志 | 在测试里让对话框/存储故意抛异常，断言进程存活且有提示 |
| P0-5 | 静态事件订阅在 5 处未退订，与已退订的 3 处不一致 | 未退订：[GenerateView.axaml.cs:26-32](Views/GenerateView.axaml.cs#L26-L32)、[InputView.axaml.cs:28](Views/InputView.axaml.cs#L28)、[RandomStrategyView.axaml.cs:15](Views/RandomStrategyView.axaml.cs#L15)、[OverlayPresenter.cs:37](Views/OverlayPresenter.cs#L37)、[ListModel.cs:128-129](Domain/ListModel.cs#L128-L129)（lambda 无法退订，形成 `AppState.StaffList → ListModel → StaffListView` 常驻链）；已退订：[StaffListView.axaml.cs:37](Views/StaffListView.axaml.cs#L37)、[MainView.axaml.cs:44](Views/MainView.axaml.cs#L44)、[StrategyEditorDialog.axaml.cs:63](Views/StrategyEditorDialog.axaml.cs#L63) | 视图销毁后仍被回调（内存只增不减、潜在的重复刷新与「幽灵更新」） | 统一订阅辅助（`Subscribe(...)` 返回可释放句柄）或全部在 `DetachedFromVisualTree` 退订；`StatsChanged` 改弱事件或注入接口 | 反复创建/销毁页面，断言订阅数回到基线（可在 SmokeTest 里加计数） |
| P0-6 | 同步失败不回滚，且失败后的半成品改动仍留在内存、退出时照样落盘；同屏文案还承诺「不会覆盖已有干员」 | [OperatorSyncService.cs:153](Domain/OperatorSyncService.cs#L153)、[165-168](Domain/OperatorSyncService.cs#L165-L168) 直接改 `AppState.StaffList`；[OperatorSyncFlow.cs:29-32](Views/OperatorSyncFlow.cs#L29-L32) catch 不回滚；文案 [OperatorSyncDialog.axaml:298](Views/OperatorSyncDialog.axaml#L298)「同步不会覆盖已有干员」，而 [OperatorSyncService.cs:161-172](Domain/OperatorSyncService.cs#L161-L172) 会重写本地干员的名称/星级/职业 | 半成品数据被当成正式数据保存；用户被文案误导，信任受损 | 先合并到临时列表，全部成功再替换；失败保留原状态并给可操作提示；文案改成事实（要么真的不覆盖，要么明说会校正什么）；手工录入的同名干员不被自动「收编」 | 构造「网络中断/部分 404」的同步，断言内存与磁盘都未被改动；文案与行为一致性人工核对 |

---

## 3. P1：用户可见的正确性与体验断裂

| # | 问题 | 证据 | 修法 |
| --- | --- | --- | --- |
| P1-1 | 新手主路径断点一：同步新增的干员默认**不入池**，生成阵容时直接报错且不给跳转 | [OperatorSyncService.cs:150](Domain/OperatorSyncService.cs#L150) 默认不入池；[GenerateView.axaml.cs:138-142](Views/GenerateView.axaml.cs#L138-L142) 报错；空状态不提随机池 [GenerateView.axaml:360-361](Views/GenerateView.axaml#L360-L361) | 报错里给「去干员列表勾选」按钮；空状态补一句「需要先勾选入池」 |
| P1-2 | 新手主路径断点二：手动录入成功后**不清空表单**，第二次添加必然撞「列表中已有该干员」 | [InputView.axaml.cs:136-145](Views/InputView.axaml.cs#L136-L145)（成功后只 `PostSnack`）、[129-134](Views/InputView.axaml.cs#L129-L134) | 成功后清空名称/等级并聚焦名称框 |
| P1-3 | 图片失败结果被永久缓存，而 `Retry` 只清内存、磁盘缓存没有失效接口 → **「重试」对写坏的盘缓存永远无效** | [ArtImage.cs:136-157](Views/ArtImage.cs#L136-L157)；[LocalImageStore.cs](Views/LocalImageStore.cs) 只有 `TryReadAsync`/`WriteAsync`，无删除 API | 失败项加 TTL/负缓存上限；`LocalImageStore` 加 `Invalidate(uri)`，`Retry` 同时清内存与磁盘 |
| P1-4 | `_editing` 标志粘滞：`BeginEdit` 在可能提前 return/抛异常之前调用，复位只在 `CellEditEnded`，之后列表与计数**永久不刷新且无提示** | [StaffListView.axaml.cs:343](Views/StaffListView.axaml.cs#L343)、[380-384](Views/StaffListView.axaml.cs#L380-L384)；[ListModel.cs:405](Domain/ListModel.cs#L405) `if (_refreshing \|\| _editing) return` | `try/finally` 复位，或页面离开时强制结束编辑 |
| P1-5 | 图片状态机缺一态：「排队但还没开始下」被记成 `Loading`，于是**「尚未加载」和「加载中」在界面上无法区分**（这是我实测截图时发现的：可见区外的图会一直停在 Loading） | [ArtImage.cs:159-179](Views/ArtImage.cs#L159-L179) 设 `Loading`；[ArtImage.cs:181-201](Views/ArtImage.cs#L181-L201) 只有进入预取带才真正开始 | 增加 `Queued`（或 `Idle`）态：赋值即 `Queued`，真正开始下载才 `Loading`；四态（无图源/排队/加载中/失败）各自有视觉表达 |
| P1-6 | 策略编辑器的窄屏判断用的是「外壳宽度」而不是它自己窗口的宽度 | [StrategyEditorDialog.axaml.cs:62](Views/StrategyEditorDialog.axaml.cs#L62)、[72-99](Views/StrategyEditorDialog.axaml.cs#L72-L99)；断点只在 [MainView.axaml.cs:124](Views/MainView.axaml.cs#L124) 更新 | 改用自己的 `Bounds.Width`（或订阅自身尺寸变化） |
| P1-7 | 失败提示直接把 .NET 异常文本丢给用户 | [OperatorSyncFlow.cs:32](Views/OperatorSyncFlow.cs#L32) → [InputView.axaml.cs:51](Views/InputView.axaml.cs#L51)、[StaffListView.axaml.cs:166](Views/StaffListView.axaml.cs#L166) | 用户文案 + 「详情」（可复制）分层 |
| P1-8 | 字段校验只在下方加红字，不聚焦、不播报 | [InputView.axaml.cs:103-134](Views/InputView.axaml.cs#L103-L134)、[StaffDetailDialog.axaml.cs:54-76](Views/StaffDetailDialog.axaml.cs#L54-L76)；策略编辑器改用模态 Alert 报字段错（[StrategyEditorDialog.axaml.cs:441-627](Views/StrategyEditorDialog.axaml.cs#L441-L627)），口径不一致 | 统一：聚焦首个出错控件 + 就地红字 + 播报 |
| P1-9 | 确认弹窗没有默认/取消按钮语义，Enter 不确认，初始焦点在「取消」 | 全仓 `IsDefault`/`IsCancel` = 0 处；[ConfirmDialog.axaml.cs:19](Views/ConfirmDialog.axaml.cs#L19) | 主按钮 `IsDefault`、次按钮 `IsCancel`，并把初始焦点给主按钮（危险操作除外） |
| P1-10 | 网页叠加层没有 Tab 焦点圈禁，弹窗打开时背景仍能被 Tab/读屏访问；关闭后焦点归还不可靠 | [OverlayPresenter.cs:131-138](Views/OverlayPresenter.cs#L131-L138)（只处理 Esc）、[ModalLayer.cs:29-34](Views/ModalLayer.cs#L29-L34)（遮罩只吃指针）、[OverlayPresenter.cs:143-152](Views/OverlayPresenter.cs#L143-L152) | 打开时记录并圈禁焦点，关闭后归还到触发控件 |
| P1-11 | 窄屏抽屉无 Esc 关闭、打开后不移动焦点，导航项在 Tab 序末尾 | [MainView.axaml:88](Views/MainView.axaml#L88)、[137-256](Views/MainView.axaml#L137-L256)、[MainView.axaml.cs:202-222](Views/MainView.axaml.cs#L202-L222) | Esc 关闭 + 打开即聚焦首个导航项 + 关闭归还 |
| P1-12 | 术语与细节不统一：职阶/职业、方案/策略；11px 正文 3 处；触摸命中区 <44（行内 ⋯ 32×32、筛选 chip 24×24、卡片勾选 28 高） | [InputView.axaml.cs:117](Views/InputView.axaml.cs#L117) vs [InputView.axaml:76](Views/InputView.axaml#L76)；[RandomStrategyView.axaml:48](Views/RandomStrategyView.axaml#L48) vs [GenerateView.axaml:203](Views/GenerateView.axaml#L203)；[MainView.axaml:162](Views/MainView.axaml#L162)、[StaffPickDialog.axaml:125](Views/StaffPickDialog.axaml#L125)；[StaffListView.axaml:642](Views/StaffListView.axaml#L642)、[:350-351](Views/StaffListView.axaml#L350-L351)、[:129](Views/StaffListView.axaml#L129) | 定一份术语表；正文最小 12px；命中区补到 44×44（可用透明 padding 扩大而不改变视觉） |
| P1-13 | 图片相关的死代码与「假样式」：`AlternateSources` 全仓零引用；`dense` 类被用了 24 次却**没有任何样式定义**；若干令牌/样式/成员无消费者 | [ArtImage.cs:109-124](Views/ArtImage.cs#L109)；`dense` 见 [GenerateView.axaml:205](Views/GenerateView.axaml#L205)；死样式 [App.axaml:220](App.axaml#L220)、[253-330](App.axaml#L253-L330)；死成员 [ListModel.cs:81](Domain/ListModel.cs#L81)、[Staff.Visual.cs:85](Models/Staff.Visual.cs#L85)、[Staff.Visual.cs:116](Models/Staff.Visual.cs#L116) | 删除或补齐；`dense` 要么定义紧凑间距、要么去掉类名 |

**我实测发现的界面细节**（截图证据在 `artifacts/ui-review-new/`）：详情面板与策略编辑器的**星级下拉显示成「1」「6」而不是「1★」「6★」**（[StaffDetailDialog.axaml.cs:31](Views/StaffDetailDialog.axaml.cs#L31)、[StrategyEditorDialog.axaml:213-218](Views/StrategyEditorDialog.axaml#L213-L218)，而 [OperatorSyncDialog.axaml:243](Views/OperatorSyncDialog.axaml#L243) 已有 `★` 模板）；卡片名牌里的 `⋯` 竖向居中于两行文字之间，观感略浮（[StaffListView.axaml:727-733](Views/StaffListView.axaml#L727-L733)）；表格行 40px 头像里的职业图标在 18px 时偏小偏暗。

---

## 4. P2：性能与工程化

**图片管线**（这是全项目最需要设上界的地方）

| # | 问题 | 证据 | 修法 |
| --- | --- | --- | --- |
| P2-1 | 未命中路径会饿死其他图：全局 6 并发，候选串行回退且每个候选都要重新抢名额，单候选超时 20s，而单规格最多 12 个候选 | [ArtImage.cs:29](Views/ArtImage.cs#L29)、[:56](Views/ArtImage.cs#L56)、[:320-338](Views/ArtImage.cs#L320-L338)；[OperatorArt.cs:51](Domain/OperatorArt.cs#L51)、[:74-79](Domain/OperatorArt.cs#L74-L79)（精二一人最多 24 条 URL） | 回退尝试不占下载名额（先取字节、只在解码期限流）；单候选超时降到 5s；「全镜像皆失败」的 URL 记负缓存并降级为只试首选镜像 |
| P2-2 | 位图 LRU 按**张数**不按字节，且淘汰不 Dispose：200 张 × 512 宽立绘 ≥ 1MB/张 → **≥200MB**，与注释「避免内存一直涨」矛盾 | [ArtImage.cs:32](Views/ArtImage.cs#L32)、[:405-406](Views/ArtImage.cs#L405-L406)、[:470-474](Views/ArtImage.cs#L470-L474)、[:21](Views/ArtImage.cs#L21) | 改字节预算（如 64MB）+ 按引用计数（被可见 Image 持有的不计入预算）；WASM 用更小目标宽 |
| P2-3 | 磁盘缓存无上限、无 TTL、无清理入口：每条成功 URL 一个文件，500 名干员约 12,000 个文件 | [LocalImageStore.cs:41](Views/LocalImageStore.cs#L41)、[:55-56](Views/LocalImageStore.cs#L55-L56)；全仓 `ArtCache` 只写不删 | 容量上限 + LRU/时间淘汰；设置页给「清理图片缓存」并显示占用 |
| P2-4 | 预取带固定 240px，与注释「一屏左右」不符：立绘卡高 328px（一行都不到），表格行高 40px 时又相当于 6 行 | [ArtImage.cs:38](Views/ArtImage.cs#L38)；[StaffListView.axaml.cs:104-105](Views/StaffListView.axaml.cs#L104-L105) | 改为 `max(240, 0.5 × viewport 高度)` 或按卡高倍数 |
| P2-5 | 滚动时每张图各自处理 viewport 事件并反复取消/重建下载（CWT 写 + Rect 判定 + 新建 CTS/Task），无优先级、无事件合并 | [ArtImage.cs:187](Views/ArtImage.cs#L187)、[:240](Views/ArtImage.cs#L240)、[:267-273](Views/ArtImage.cs#L267-L273)、[:276-289](Views/ArtImage.cs#L276-L289) | 事件合并到每帧处理一次；排队按「离可见区距离」排序；滚回带内复用原 CTS |
| P2-6 | 缓存键只有 URI 没有尺寸：512 宽解码被 40/64/88px 三处缩略图共用，一张大图被小缩略图钉在缓存里 | [ArtImage.cs:53](Views/ArtImage.cs#L53)；消费端 [StaffListView.axaml:480-489](Views/StaffListView.axaml#L480-L489)、[GenerateView.axaml:315-320](Views/GenerateView.axaml#L315-L320)、[StaffDetailDialog.axaml:38-41](Views/StaffDetailDialog.axaml#L38-L41) | 键改为 `(Uri, 目标宽)`，至少给缩略图单独一档 |
| P2-7 | 小问题成串：`CareerIconUris` 每次 get 都新建列表（会触发取消+重排队）；`AlternateSources` 死代码；`RaiseArtChanged` 对 500 人各发一次无用通知；每次缓存命中都读一次环境变量且在锁内 | [Staff.Visual.cs:246](Models/Staff.Visual.cs#L246)、[:208](Models/Staff.Visual.cs#L208)；[StaffListView.axaml.cs:79-81](Views/StaffListView.axaml.cs#L79-L81)；[ArtImage.cs:44-51](Views/ArtImage.cs#L44-L51)、[:464](Views/ArtImage.cs#L464) | 按 `CareerSlug` 静态缓存（8 项）；删死代码；卡高/间距常量收敛到一处；环境变量启动读一次 |
| P2-8 | `CTS` 提前 Dispose（任务仍在用）、fire-and-forget 无异常观察、`LoadCoreAsync` 不吃 cancellation | [ArtImage.cs:295-303](Views/ArtImage.cs#L295-L303)、[:310](Views/ArtImage.cs#L310)、[:392](Views/ArtImage.cs#L392) | 先取消 → 等任务结束 → 再 Dispose；ct 透传到底 |
| P2-9 | 列表刷新全量重建：每次 `RefreshVisible` 都新建 HashSet 过滤 + 排序 + `ToList` + `SequenceEqual(500)` + `ReplaceAll`（整表 Reset）；计数属性每次 get O(n) 且有分配 | [ListModel.cs:403-437](Domain/ListModel.cs#L403-L437)、[ResettableCollection.cs:10-20](Domain/ResettableCollection.cs#L10-L20)、[ListModel.cs:257-258](Domain/ListModel.cs#L257-L258)、[:511](Domain/ListModel.cs#L511) | Reset 改最小增删 diff；选中数增量维护；「筛选=全部且无排序」时只更新计数 |

**WASM 首屏**

| # | 问题 | 证据 | 修法 |
| --- | --- | --- | --- |
| P2-10 | 内嵌中文字体 2.9MB（注释写 1.9MB），占 Core wasm 程序集的 ≈88%；`PublishTrimmed=partial` 裁不掉 avares 资源；`Avalonia.Fonts.Inter`（1.9MB）出现在浏览器产物里，而它只在桌面端用了 | 字体实测 2,917,064 B（[Core csproj](Core/arknights-random-team.Core.csproj) 注释不符）；浏览器产物 `_framework`（**旧产物，需重测**）；`WithInterFont()` 只在 [desktop/Program.cs:26](desktop/Program.cs#L26) | 按「UI 文案 + 干员名」重新子集化字体（当前是 GB2312 全字集）；把 Inter 依赖移出浏览器项目；重建后重测首屏字节 |

**工程化与交付**

| # | 问题 | 证据 | 修法 |
| --- | --- | --- | --- |
| P2-11 | CI 构建整个 sln（含 WASM）却从不安装 `wasm-tools` 工作负载 | [build.yml:37-41](.github/workflows/build.yml#L37-L41) vs [pages.yml:54-55](.github/workflows/pages.yml#L54-L55) | build.yml 补 `dotnet workload install wasm-tools`，或拆 job 单编 Browser |
| P2-12 | 唯一的界面回归测试不在 sln、CI 从不运行它 | [arknights-random-team.sln:3-14](arknights-random-team.sln#L3-L14) 只列三个工程 | 把 SmokeTest 加入 sln，并在 PR 门禁里跑（见第 7 节） |
| P2-13 | 没有版本号统一来源、没有依赖集中管理与锁定、无 `global.json`；`ItemsRepeater 12.0.0` 与 `Avalonia 12.1.1` 存在 minor skew | 四个 csproj 里 `<Version>` 全部缺省；重复的 `PackageReference`（[Core](Core/arknights-random-team.Core.csproj)、[desktop](desktop/arknights-random-team.Desktop.csproj)、[browser](browser/arknights-random-team.Browser.csproj)、[SmokeTest](tools/SmokeTest/arknights-random-team.SmokeTest.csproj)） | `Directory.Build.props` 定 `<Version>`（CI 传 `-p:Version=${tag}`）+ `Directory.Packages.props`（CPM）+ lock file + `global.json` |
| P2-14 | 无 LICENSE / THIRD_PARTY_NOTICES；内嵌字体（OFL）、方舟职业图标、运行期第三方立绘 CDN 均无声明；README 与现状漂移（自包含参数、`tools/` 说明、768 最小宽度、行尾策略都没写）；Actions 未按 SHA 固定 | 根目录无 `LICENSE*`/`THIRD_PARTY*`；[README.md:57-59](README.md#L57-L59) vs [build.yml:85](.github/workflows/build.yml#L85)；[README.md:37](README.md#L37) | 补许可与声明（这是**公开发布的法律风险**，不是洁癖）；README 补「构建/CI/发布/SmokeTest」小节；Actions 固定 SHA |
| P2-15 | 无 `.editorconfig`、无 `Directory.Build.props`、无分析器开关 → 死代码与风格漂移无人告警 | 四个 csproj 各自重复 `TargetFramework`/`Nullable`/`ImplicitUsings` | 根目录统一 + `EnableNETAnalyzers`；首次开启会有一批告警，先设 `AnalysisLevel=latest-minimum` |
| P2-16 | Core 用通配符 `Include="../**/*.cs"` 吸上级源码，Exclude 是白名单式列举（**未含 `docs/`、`artifacts/`、`.session-import/`**） | [Core csproj:43-47](Core/arknights-random-team.Core.csproj#L43-L47) | 改显式文件清单，或把源码移入 `Core/`；至少把 `docs/**` 加进 Exclude（往 docs 放一份 `.cs` 就会被静默编译） |

---

## 5. 架构类结构性改造（按收益排序）

| # | 改造 | 现状证据 | 收益 | 代价 |
| --- | --- | --- | --- | --- |
| A-1 | **把 `ListModel` 移出 `Domain`**（放 `Views` 或独立 ViewModel 层），图源统计与偏好持久化改为注入 | [ListModel.cs:113](Domain/ListModel.cs#L113)、[:116](Domain/ListModel.cs#L116)、[:129-133](Domain/ListModel.cs#L129-L133)、[:548-553](Domain/ListModel.cs#L548-L553) 直接引用 `AppState` 与 `Views.ArtImage` | 消除唯一的 Domain→Views 反向依赖；`ListModel` 可单测 | 改 DataContext 装配与 SmokeTest 引用 |
| A-2 | **统一启动加载兜底与错误通道**（与 P0-2 同源） | [AppState.cs:125-170](AppState.cs#L125-L170)、[StrategyPersistence.cs:15-24](Domain/StrategyPersistence.cs#L15-L24) | 坏文件不再等于打不开程序 | 新增错误通道与文案 |
| A-3 | **`async void` 收拢 + 全局未处理异常兜底**（与 P0-4 同源） | 15 处（见 P0-4） | 从「点一下闪退」变成「弹提示」 | 逐点替换 |
| A-4 | **订阅生命周期统一**（与 P0-5 同源） | 5 处未退订 | 切断常驻链，页面可反复创建销毁 | 引入一次性订阅辅助 |
| A-5 | **对话框元数据与骨架收敛**：标题/尺寸现在散在 3 处（`AppHost` 默认值、对话框 XAML、[WindowPresenter.cs:90-98](Views/WindowPresenter.cs#L90-L98) 的 `Shells`），未登记类型静默落到 `Unknown` | [AppHost.cs:36](AppHost.cs#L36)、[AlertDialog.axaml:24](Views/AlertDialog.axaml#L24)、[WindowPresenter.cs:90-98](Views/WindowPresenter.cs#L90-L98)、[:210](Views/WindowPresenter.cs#L210) | 新增对话框不再需要改三处 | 涉及 6 个对话框的机械改动 |
| A-6 | **重复实现收敛**：`StaffThumb` 已抽出但仍有 4 处手写图源（[StaffListView.axaml:487-496](Views/StaffListView.axaml#L487-L496)、[:578-580](Views/StaffListView.axaml#L578-L580)、[:755-758](Views/StaffListView.axaml#L755-L758)、[StaffPickDialog.axaml:107-119](Views/StaffPickDialog.axaml#L107-L119)）；数字输入三套实现；排序元数据四份平行 switch；通知模式三套并存 | 同上 | 改一处生效一处 | 一次性重构，需要回归 |

---

## 6. 分批实施计划

四批，每批都能独立交付、独立验收；**批次内不允许跳过验收标准**。

### 批次 A：数据安全（P0-1、P0-2、P0-3、P0-6）
- 范围：落盘原子化（含 `.bak`）、加载全程兜底与值域校验、星级/等级校验收进模型、同步改为「临时集合 → 成功才替换」+ 文案改事实。
- 验收：新增损坏数据夹具（截断 XML/JSON、`elite=3`、`star=9`、空文件、GBK 编码）→ 程序能启动、有提示、原文件被改名保留；写入过程中杀进程后数据完好；同步中断后内存与磁盘均未变；SmokeTest 增加「损坏数据」用例并保持零绑定错误。
- 预估：小～中（集中在 `AppState`/`StrategyPersistence`/`OperatorSyncService`）。

### 批次 B：不崩不漏（P0-4、P0-5、P1-3、P1-4、P1-5、P1-7、P1-8）
- 范围：`SafeFireAndForget` + 全局兜底；订阅统一退订；`Retry` 能失效磁盘缓存；`_editing` 用 try/finally；图片状态机补 `Queued` 态；错误文案分层；校验聚焦/播报。
- 验收：故意注入异常不闪退；反复进出页面订阅数回到基线；「重试」对写坏的缓存有效；编辑中被中断后列表仍会刷新；四态在截图里可区分（用截图工具出对照图）。
- 预估：中（跨 6~8 个文件，但每处都小）。

### 批次 C：新手路径与无障碍基本盘（P1-1、P1-2、P1-6、P1-9、P1-10、P1-11、P1-12）
- 范围：生成页空状态与报错给跳转；录入成功清表单；策略编辑器用自己的宽度断点；确认弹窗默认按钮；网页叠加层焦点圈禁与归还；抽屉 Esc/焦点；术语表与 12px 下限、命中区 44×44；顺带修我实测到的星级下拉显示。
- 验收：纯键盘走完「同步 → 列表勾选 → 生成阵容」主路径；弹窗打开后 Tab 不逃逸；390 宽下抽屉可用；截图工具出一组 1160/780/390 对照图。
- 预估：中。

### 批次 D：性能与工程化（P2 全部 + 架构 A-1~A-6）
- 顺序：先建度量（见第 7 节）→ 再改图片管线 → 再改列表刷新 → 最后做字体与 CI/架构收敛。**没有基线就不动图片调度**，否则改完无法证明是变好还是变坏。
- 验收：`perf` 子命令产出 500 名干员的 4 类指标并落盘；图片缓存有明确内存与磁盘上界；WASM 首屏字节重建后重测并记录；CI 门禁全绿；SmokeTest 进入 sln。
- 预估：大（图片管线与字体是主要工作量）。

---

## 7. 度量与门禁

### 7.1 性能测量（可直接实施）

在 [tools/SmokeTest](tools/SmokeTest/Program.cs) 加 `perf` 子命令，而不是另起工程——它已经有 headless + Skia + 截图能力：

1. **夹具**：`Seed(0/1/50/500)`；500 档必须给真实形态的 `SourceId`（否则图片管线完全不被测到），一半 `IsSelected=true`。
2. **搜索延迟**：`Stopwatch` 包住「最后一次键入 → `VisibleStaff` 更新完成」，200 轮取 P50/P95；四条路径（无筛选 / 单条件 / 命中 1 人 / 池筛选）。门槛：P95 ≤ 200ms。
3. **选池反馈**：包住一次 `IsSelected` 翻转（含无筛选与 InPool 两条路径），同时用 `GC.GetAllocatedBytesForCurrentThread()` 记录分配。门槛 ≤ 100ms，并盯住分配量是否随人数线性放大。
4. **布局与出帧（CPU 光栅，仅作相对回归）**：`Measure/Arrange + ForceRenderTimerTick + CaptureRenderedFrame` 各 100 帧，报 P50/P95/最差；**不能当 60fps 结论**。
5. **调度断言（几乎零成本）**：读现成的 `ArtImage.NetworkLoads/CacheHits/FailedLoads`——切一次视图后网络增量应 ≤ 可见+预取卡片数；反复滚动不应使失败数增长。
6. **内存上界压测**：用现成的 `ARTIMAGE_CACHE_LIMIT` 把上限压到 8/32/200，配合 `GC.GetTotalMemory(true)` 与 `dotnet-gcdump` 对比峰值。
7. **桌面实机**：`dotnet-counters monitor -p <pid>`（GC 堆/分配速率/GC 次数）；冷启动计时。
8. **WASM 首屏**：Playwright headless 打开页面，记录首屏请求与字节（确认字体/Inter/ICU/`System.Private.Xml` 是否真的在首屏），并用 `MutationObserver` 监听 `#app-loading` 加上 `hidden` 的时刻减去 `navigationStart`（无需改代码，[main.js:34-37](browser/wwwroot/main.js#L34-L37) 已有钩子）；滚动帧率用 CDP 4×/6× CPU 节流 + `Tracing` 统计丢帧。

**每次记录**：设备、平台、数据量、冷/热缓存、网络条件，写入 `docs/`（当前 `docs/` 是空目录）。

### 7.2 CI 门禁（最小可行）

| Job | 内容 | 门槛 |
| --- | --- | --- |
| `static` | `dotnet format --verify-no-changes`（需先提交 `.editorconfig`）；`dotnet build arknights-random-team.sln -c Release -warnaserror` | 0 差异 / 0 警告 |
| `smoke` | `dotnet workload install wasm-tools`；跑 `tools/SmokeTest`（先入 sln） | 退出码 0 且绑定错误 0（工具已按 `[Binding]` 计数） |
| `deps` | `dotnet list package --vulnerable --include-transitive`、`--deprecated` | High/Critical = 0 |
| `web` | 复用 pages.yml 的 publish + 站点完整性检查 | `index.html`/`main.js`/`_framework/dotnet.js` 存在；站点体积预算（如 ≤15MB） |
| 收口 | 分支保护要求上述必过；`uses:` 固定 SHA；`global.json` 固定 SDK 并加入缓存 key | — |

另加 `.github/dependabot.yml`（nuget + github-actions，周更）。

---

## 8. 明确不做 / 需要实机确认

**本方案不做**：
- 全量重写 MVVM（A-1 只移 `ListModel` 并注入依赖，不动 `GenerateView` 的交互逻辑）。
- 引入完整 DI 容器：当前规模用构造参数与一个小型订阅辅助足够。
- 换 UI 库 / 换主题体系。
- 做多语言 i18n（当前全中文硬编码，但**术语表**要做，见 P1-12）。
- 把「500 名干员实测达标」当发布门槛：先把基线与上界建起来，达标是后续目标。

**需要实机/真机才能定论**（本方案只负责把测法写清楚）：
- 60fps、GPU 光栅、`DecodeToWidth(512)` 后立绘真实高度与真实内存峰值。
- 触摸拖动与点击的手感、软键盘与 100vh、390 宽真机。
- Narrator/NVDA 播报（表单字段、表格列头、Snackbar、星级卡片名称）。
- 12 个候选全 404 时的端到端延迟，以及它对其他可见图的排队影响。
- WASM 首屏是否真的拉 `Avalonia.Fonts.Inter`/`System.Private.Xml`/ICU（现有产物早于 HEAD，必须重建后重测）。
- 150%/200% 系统缩放下的截断与 1★ 文字对比度实测。

---

## 9. 风险与取舍

1. **为什么先修数据再修性能**：数据丢失与启动崩溃不可逆，且用户不会报「丢了几条干员」，只会直接离开；性能问题是可见的、可迭代的。
2. **为什么 P0-6（同步覆盖）排进 P0**：它不是崩溃，但它让用户「以为不会丢数据」——这类文案与行为不一致的破坏力高于一个崩溃（崩溃至少让人知道出事了）。
3. **批次 A 的风险**：改落盘会碰用户已有数据文件。必须**先备份后迁移**，并让「旧格式仍可读」；`StaffList.xml` 的编码与字段兼容要在批次 A 的验收里显式测。
4. **批次 D 的风险**：图片管线是「牵一发动全身」的地方（并发、缓存、取消、可见区四者耦合）。先度量、再改动、每次只动一个变量，否则很容易用「感觉更流畅」掩盖回归。
5. **架构收敛的收益是滞后的**：A-1~A-6 不直接产生用户可见收益，但会决定后续每次改动的成本。建议在 P0/P1 清完、度量建好之后再做，避免与数据安全改动混在一个批次里互相掩盖问题。
6. **不做全量重写是刻意的**：当前代码的问题集中在边界处理与生命周期，而不是结构无法承载需求；重写会把已知问题换成未知问题。

---

## 10. 本轮（UI 方案收口）已完成项

以下条目原属 [UI-BEAUTIFICATION-PLAN.md](UI-BEAUTIFICATION-PLAN.md) 的未完成部分，本轮已实现并逐批通过构建 + 无头冒烟（零绑定错误）+ 截图核对：

| 原缺口 | 现状 | 证据 |
| --- | --- | --- |
| 圆角取值散乱（出现 4/7/20） | 收敛为 6 组令牌（控件 6、面板 8、弹层 12、焦点环 10、发丝线 2、药丸 999），56 处替换 | [App.axaml](App.axaml) |
| `dense` 类被用 24 次却无定义 | 已定义：紧凑输入 32 高、字号 13 | [App.axaml](App.axaml) |
| 间距越界（Spacing=2）；动效 100ms 低于下限 | 间距入序列；按下位移 120ms | [MainView.axaml](Views/MainView.axaml)、[App.axaml](App.axaml) |
| 正文 11px 三处 | 统一 12px | [MainView.axaml:162](Views/MainView.axaml#L162)、[StaffPickDialog.axaml](Views/StaffPickDialog.axaml) |
| 弹窗标题字号不统一（22 vs 20） | 统一 20 | [StrategyEditorDialog.axaml](Views/StrategyEditorDialog.axaml) |
| 触摸命中区 <44（⋯、chip 清除、卡片勾选、规则行） | 四处均 ≥44（视觉尺寸以负边距／外层固定格保持） | [StaffListView.axaml](Views/StaffListView.axaml)、[RandomStrategyView.axaml](Views/RandomStrategyView.axaml) |
| 表格行图块只有两态，未用共享组件 | 改用 [StaffThumb](Views/StaffThumb.axaml)，四态在表格同样成立 | [StaffListView.axaml](Views/StaffListView.axaml) |
| 图片状态机缺「排队中」 | 新增 `Queued` 态，与 加载中／失败／无图源 四态可区分（不透明度 + 重试按钮） | [ArtImage.cs](Views/ArtImage.cs)、[StaffThumb.axaml](Views/StaffThumb.axaml) |
| 列头排序只有鼠标路径 | 列头可聚焦，空格／回车触发排序，并有焦点可见样式 | [StaffListView.axaml.cs](Views/StaffListView.axaml.cs)、[App.axaml](App.axaml) |
| 叠加层无 Tab 圈禁、关闭后焦点不归还 | 打开时 `KeyboardNavigation` 循环圈禁；关闭前判断焦点归属再归还（原守卫在关闭最后一个弹窗时必然失效） | [OverlayPresenter.cs](Views/OverlayPresenter.cs) |
| 确认框无默认/取消语义 | `IsDefault` / `IsCancel` 已加（Esc 关闭、Enter 触发焦点按钮） | [ConfirmDialog.axaml](Views/ConfirmDialog.axaml) |
| 窄屏抽屉无 Esc、不移动焦点 | Esc 关闭；打开聚焦首个导航项，关闭归还菜单按钮 | [MainView.axaml.cs](Views/MainView.axaml.cs) |
| 小屏编辑面板未全屏 | 新增 `ModalContent.PreferFullScreenOnPhone`，详情与策略编辑器在 <600 档铺满 | [ModalContent.cs](Views/ModalContent.cs)、[OverlayPresenter.cs](Views/OverlayPresenter.cs) |
| 断点阈值与方案不符（1100 vs 1200） | 改为 1200；默认窗口相应提到 1240×840，保证默认仍是完整侧栏 | [AppLayout.cs](Views/AppLayout.cs)、[MainWindow.axaml](MainWindow.axaml) |
| 验收截图矩阵缺尺寸 | 补齐 1240/1160/960×700/1440×900/1024×768/768×1024/390×844 七档 | `artifacts/ui-final/` |
| §8 性能基线未建立 | 新增 `perf` 模式：500 名干员下搜索 P95 ≤ 1.5ms、选池 P95 ≤ 4.3ms、卡片网格只实例化 29 个控件；报告落盘 `artifacts/perf/` | [Program.cs](tools/SmokeTest/Program.cs) |

**仍然待实机确认**（无头环境测不出）：100%/150%/200% 系统缩放、真机触摸与读屏、GPU 帧率、WASM 首屏字节。

**两处有意保留的例外**：筛选面板内的筛选 chip 与视图切换按钮仍为 32 高——它们是桌面优先的次级控件，放大到 44 会把筛选浮层撑高到需要滚动；触摸命中区 ≥44 的要求已覆盖主操作（生成、添加、入池、批量、保存）与被点名的四处控件。
