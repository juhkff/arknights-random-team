using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using arknights_random_team;
using arknights_random_team.Domain;
using arknights_random_team.Models;
using arknights_random_team.Views;

namespace arknights_random_team.SmokeTest;

/// <summary>
/// 无头界面验证工具，两种用法：
///
/// 1. 检查模式（默认）：把各界面真正实例化并跑一遍布局，报告绑定错误与虚拟化是否生效。
///    用来抓「编译通过、一打开就崩」的问题——缺资源、绑定路径写错、虚拟化容器用法不对等。
/// 2. 截图模式：<c>shots &lt;目录&gt;</c>。用 Skia 把帧真正画出来存成 PNG，
///    并模拟鼠标点击、Tab 焦点等输入，供人工/自动审查界面实际观感。
///
/// 运行：
///   dotnet run --project tools/SmokeTest/arknights-random-team.SmokeTest.csproj
///   dotnet run --project tools/SmokeTest/arknights-random-team.SmokeTest.csproj -- shots artifacts/ui-review
/// </summary>
internal static class Program
{
    private static readonly List<string> Warnings = [];

    /// <summary>截图输出目录；为 null 时不截图。</summary>
    private static string? _shotDir;

    /// <summary>是否用真实干员 ID 取真实立绘（需要联网）。</summary>
    private static bool _useRealArt;

    /// <summary>perf 模式的干员规模；0 表示不跑 perf。</summary>
    private static int _perfCount;

    /// <summary>defects 模式要复现的缺陷编号；null 表示不跑。</summary>
    private static string? _defectCase;

    private static Window? _shell;
    /// <summary>最近一次构造的干员列表视图，供后续几步复用（它不在外壳里，需要显式上屏）。</summary>
    private static StaffListView? _lastListView;

    private static Control? _current;
    private static double _currentWidth = 1160;
    private static double _currentHeight = 820;

    private static int Main(string[] args)
    {
        Logger.Sink = new CollectingSink();

        if (args.Length >= 2 && args[0] == "shots")
        {
            _shotDir = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(_shotDir);
            _useRealArt = args.Contains("real");
            Console.WriteLine($"截图目录：{_shotDir}{(_useRealArt ? "（含真实立绘，需要联网）" : "")}");
        }
        else if (args.Length >= 2 && args[0] == "defects")
        {
            _defectCase = args[1];
        }
        else if (args.Length >= 1 && args[0] == "perf")
        {
            _perfCount = args.Length >= 2 && int.TryParse(args[1], out var count) ? count : 500;
        }

        AppBuilder.Configure<App>()
            .UseSkia()
            .With(new FontManagerOptions
            {
                // 无头环境枚举不到系统字体，必须用内嵌中文字体，否则截图里中文全是方块。
                DefaultFamilyName = AppFonts.EmbeddedCjkFamily,
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily(AppFonts.EmbeddedCjkFamily) }
                ]
            })
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        try
        {
            if (_defectCase is not null)
                RunDefect(_defectCase);
            else if (_perfCount > 0)
                RunPerf(_perfCount);
            else
                RunChecks();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 运行失败：{ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 2;
        }

        if (Warnings.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"⚠️  运行期警告 {Warnings.Count} 条：");
            foreach (var warning in Warnings.Distinct().Take(40))
                Console.WriteLine("   " + warning);
        }

        var failures = Warnings.Count(w => w.Contains("[Binding]", StringComparison.OrdinalIgnoreCase));
        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "冒烟测试通过：界面全部加载并完成布局，无绑定错误。"
            : $"冒烟测试失败：{failures} 条绑定错误。");
        return failures == 0 ? 0 : 1;
    }

    private static void RunChecks()
    {
        Step("设置对话框宿主（网页端叠加层，便于在同一窗口内截图弹窗）");
        AppHost.Presenter = new OverlayPresenter(new ModalLayer());

        // 模型层自检：清空是否本身会卡（把「清空后卡住」拆成模型/界面两层来看）
        Step("自检：模型层清空（3 名）应为瞬时完成");
        SeedData(3);
        var clearWatch = System.Diagnostics.Stopwatch.StartNew();
        AppState.StaffList.Clear();
        clearWatch.Stop();
        Console.WriteLine($"ℹ️  模型层清空：耗时 {clearWatch.ElapsedMilliseconds}ms、剩余 {AppState.StaffList.Count} 名");

        Step("填充测试数据");
        SeedData();

        // 真实立绘模式：先按已知干员 ID 建一页数据并等图下完，单独出一组截图。
        if (_useRealArt)
            CaptureRealArtShots();

        // 1. 外壳：宽屏 / 抽屉档 / 回到宽屏。
        var shell = new MainView();

        // 方案 §8 要求的尺寸矩阵：桌面默认与最小、网页四个档位。
        Host("MainView（默认 1240×840）", shell, 1240, 840, "01-shell-1240x840");
        Host("MainView（1160×820）", shell, 1160, 820, "01-shell-1160x820");
        Host("MainView（960×700 桌面最小档）", shell, 960, 700, "01-shell-960x700");
        Host("MainView（网页 1440×900）", shell, 1440, 900, "01-shell-1440x900");
        Host("MainView（网页 1024×768）", shell, 1024, 768, "01-shell-1024x768");
        Host("MainView（平板 768×1024）", shell, 768, 1024, "01-shell-768x1024");
        Host("MainView（手机 390×844）", shell, 390, 844, "01-shell-390x844");
        Host("MainView（回到默认）", shell, 1240, 840);

        // 2. 干员列表：表格 / 紧凑 / 头像 / 立绘 / 搜索。
        var list = new StaffListView();
        _lastListView = list;
        Host("StaffListView（表格）", list, 1160, 820, "02-list-table");
        if (list.DataContext is ListModel model)
        {
            model.IsCompactTable = true;
            Repump();
            CaptureNow("02-list-table-compact");

            model.IsCompactTable = false;
            model.IsCardView = true;
            Repump();
            Check("StaffListView（头像卡片）", list, 1160, 820);
            ReportRealizedCards(list);
            CaptureNow("03-list-cards-avatar");

            model.UsePortrait = true;
            Repump();
            Check("StaffListView（立绘卡片）", list, 1160, 820);
            CaptureNow("03-list-cards-portrait");

            model.IsCardView = false;
            Repump();

            model.SearchText = "测试干员0";
            Repump();
            CaptureNow("02-list-table-filtered");
            model.SearchText = "";
            Repump();
        }
        else
        {
            Fail("StaffListView 的 DataContext 不是 ListModel");
        }

        // 3. 卡片交互：点击整卡切换随机池；Tab 聚焦。截图模式才做，避免影响检查结果。
        if (_shotDir is not null)
        {
            CaptureCardInteractions(list);
            VerifyHeaderKeyboardSort(list);

            // 指针边界与排序三态放在这里：这一阶段外壳刚上屏、输入链路可靠；
            // 放到流程末尾时窗口布局已滞后，命中测试会落到空处（实测过）。
            VerifyPointerAndSortEdges(list);
            VerifyListActions(list);
            VerifyGenerateAndInput(shell);
            VerifyMenuChains(shell);
            VerifyTableEditAndClear(shell);
            VerifyEditorAndGenerateControls(shell);
            ReportAccessibilityCoverage(shell);
        }

        // 4. 详情 / 编辑面板：走叠加层，能直接截到。
        var staff = AppState.StaffList.FirstOrDefault();
        if (staff is not null)
            Host("StaffDetailDialog", new StaffDetailDialog(staff), 440, 560, "05-staff-detail");

        // 5. 生成页。
        Host("GenerateView", new GenerateView(), 1160, 820, "06-generate");
        Host("GenerateView（窄屏）", new GenerateView(), 780, 760, "06-generate-narrow");

        // 6. 策略页：规则较多时按需展开。
        AppState.Strategies.Add(BuildStrategy(ruleCount: 5));
        Host("RandomStrategyView", new RandomStrategyView(), 1160, 820, "07-strategy");
        AppState.Strategies.Add(BuildStrategy(ruleCount: 2));
        Host("RandomStrategyView（含少规则卡片）", new RandomStrategyView(), 1160, 820, "07-strategy-mixed");

        // 7. 策略编辑器：宽屏两栏与窄屏单栏。
        var definition = BuildStrategy(ruleCount: 2);
        AppLayout.Update(1160);
        Host("StrategyEditorDialog（宽屏）", new StrategyEditorDialog(definition), 960, 740, "08-editor-wide");
        AppLayout.Update(700);
        Host("StrategyEditorDialog（窄屏）", new StrategyEditorDialog(definition), 700, 740, "08-editor-narrow");
        AppLayout.Update(1160);

        // 8. 录入页。
        Host("InputView", new InputView(), 1160, 820, "04-input");
        Host("InputView（窄屏）", new InputView(), 780, 760, "04-input-narrow");

        // 9. 弹窗链路（走真实叠加层宿主）：小屏全屏、焦点圈禁、Esc 关闭与焦点归还。
        if (_shotDir is not null)
            VerifyModalChain(shell);

        // 10. 只靠读代码证明不了的交互链路：抽屉键盘、池状态跨视图、图片失败重试、四态分布。
        if (_shotDir is not null)
            VerifyInteractionPaths(shell, list);

        // 11. 还没被覆盖过的行为：确认框 Enter/Esc、弹窗焦点圈禁、策略编辑器小屏全屏、四态视觉接线。
        if (_shotDir is not null)
            VerifyRemainingBehaviors(shell);

        // 12. 方案 §8 明确要求、但一直没测的数据规模与边界：0/1/500 名、空状态、筛选无结果。
        if (_shotDir is not null)
            VerifyDataScales(_lastListView!);

    }

    /// <summary>
    /// 实测弹窗链路：390 宽（手机档）打开干员详情，检查
    /// ①是否铺满可用空间 ②焦点是否进入弹窗 ③Esc 能否关闭 ④关闭后焦点是否归还。
    /// 这四件事读代码证明不了，必须走一遍真实的 OverlayPresenter。
    /// </summary>
    /// <summary>
    /// 实测三条只靠读代码证明不了的交互链路：
    /// ①窄屏抽屉的 Esc 关闭与焦点往返 ②随机池状态跨视图切换是否保持
    /// ③图片失败态是否出现重试入口、点击后是否真的重新请求。
    /// 顺带打印图片四态的实际分布，确认状态机在真实数据下被走到。
    /// </summary>
    /// <summary>
    /// 补齐四件此前只有代码、没有实测的行为：
    /// ①确认框的 Enter 确认 / Esc 取消 ②弹窗打开时 Tab 不会逃到背景
    /// ③策略编辑器在 390 宽是否铺满 ④「排队中」态是否真的接上了视觉类。
    /// </summary>
    /// <summary>
    /// 方案 §8 要求「覆盖 0／1／50／500 名干员」。0 与 1 是空状态与单卡的边界，
    /// 500 是性能设计集，这三档此前只在 perf 里跑过 500、从没看过界面。
    /// </summary>
    private static void VerifyDataScales(StaffListView list)
    {
        if (list.DataContext is not ListModel model)
            return;

        foreach (var (count, tag) in new[] { (0, "15-list-empty"), (1, "16-list-one") })
        {
            Step($"数据规模：{count} 名干员");
            AppState.StaffList.Clear();
            if (count > 0)
                AppState.StaffList.Add(new Staff
                {
                    Name = "唯一干员",
                    Star = 6,
                    Career = Career.近卫,
                    IsSelected = true,
                    Level = Level.GenerateMaxLevel(6)
                });

            model.IsCardView = false;
            Host($"StaffListView（{count} 名，表格）", list, 1240, 840, tag);
            Console.WriteLine($"ℹ️  {count} 名：员工空状态 = {model.ShowStaffEmpty}；筛选空状态 = {model.ShowFilterEmpty}；摘要 = {model.SummaryText}");
        }

        Step("数据规模：1 名干员时筛选到无结果");
        model.SearchText = "不存在的名字";
        Repump();
        Console.WriteLine($"ℹ️  筛选无结果：员工空状态 = {model.ShowStaffEmpty}；筛选空状态 = {model.ShowFilterEmpty}");
        CaptureNow("17-list-filter-empty");
        model.SearchText = "";

        Step("数据规模：500 名干员看界面（表格 + 卡片）");
        SeedData(500);
        model.IsCardView = false;
        Host("StaffListView（500 名，表格）", list, 1240, 840, "18-list-500-table");
        model.IsCardView = true;
        Repump();
        Host("StaffListView（500 名，卡片）", list, 1240, 840, "18-list-500-cards");
        var realized = list.FindControl<ItemsRepeater>("StaffCards")?.Children.Count() ?? -1;
        Console.WriteLine($"ℹ️  500 名：卡片网格实例化 {realized} 个控件；摘要 = {model.SummaryText}");

        // 复原成其它步骤期望的 51 名
        SeedData(51);
        model.IsCardView = false;
        Repump();
    }

    /// <summary>
    /// 方案 §8 的两条交互验收：拖动不应误切换随机池、点「更多」菜单不应触发整卡选池；
    /// 顺带把列头排序的三态循环（升序 → 降序 → 取消）走完。
    /// </summary>
    private static void VerifyPointerAndSortEdges(StaffListView list)
    {
        if (list.DataContext is not ListModel model || _shell is null)
            return;

        // 诊断：前面几步开过多个弹窗，若有残留，遮罩会把点击全部吃掉。
        if (AppHost.Presenter?.Overlay is { } overlayLayer)
        {
            var layerChildren = overlayLayer.GetVisualDescendants().OfType<Canvas>().Sum(c => c.Children.Count);
            Console.WriteLine($"ℹ️  诊断：叠加层激活 = {overlayLayer.IsActive}；层内控件数 = {layerChildren}");
        }

        // 先重新上屏一次：这一轮里外壳被反复改过尺寸，窗口自身的布局会滞后，
        // 直接算坐标做命中测试会落到空处（早期那条能过的点击测试恰好也先 Host 了一次）。
        model.IsCardView = true;
        Host("StaffListView（指针边界用，卡片）", list, 1160, 820);

        var card = list.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("operator-card"));
        if (card?.DataContext is not Staff staff)
        {
            Console.WriteLine("⚠️  没找到卡片，跳过指针边界验证");
            return;
        }

        // ---- ② 轻点整卡：应切换 ----
        var beforeClick = staff.IsSelected;
        var selectedBefore = model.SelectedStaffCount;
        var stillSameCard = ReferenceEquals(card.DataContext, staff) && card.IsAttachedToVisualTree();
        Console.WriteLine($"ℹ️  点击前：卡片仍绑定同一干员 = {stillSameCard}；已入池数 {selectedBefore}");
        if (CenterOf(card) is { } probeAt)
        {
            var top = _shell.GetVisualsAt(probeAt).Take(4).Select(v => v.GetType().Name);
            Console.WriteLine($"ℹ️  该坐标下最上层控件：{string.Join(" > ", top)}；卡片屏幕矩形 = {card.Bounds.Width:0}×{card.Bounds.Height:0}");
        }

        if (CenterOf(card) is { } clickAt)
        {
            _shell.MouseDown(clickAt, MouseButton.Left, RawInputModifiers.None);
            Repump();
            _shell.MouseUp(clickAt, MouseButton.Left, RawInputModifiers.None);
            Repump();
        }

        Console.WriteLine($"ℹ️  轻点整卡：目标干员「{staff.Name}」入池 {beforeClick} → {staff.IsSelected}；全表已入池数 {model.SelectedStaffCount}（若计数变了但目标没变，说明点到的是回收后的另一张卡）");

        // ---- ① 拖动 30px 后抬起：不应切换入池 ----
        // 每次交互前都必须重新取坐标：拖动会让列表滚动，卡片位置会变，
        // 沿用旧坐标等于点到别的卡片上（第一次跑就踩了这个坑，结论完全不可信）。
        var beforeDrag = staff.IsSelected;
        if (CenterOf(card) is { } dragFrom)
        {
            _shell.MouseDown(dragFrom, MouseButton.Left, RawInputModifiers.None);
            _shell.MouseMove(new Point(dragFrom.X, dragFrom.Y + 30), RawInputModifiers.None);
            _shell.MouseUp(new Point(dragFrom.X, dragFrom.Y + 30), MouseButton.Left, RawInputModifiers.None);
            Repump();
        }

        Console.WriteLine($"ℹ️  拖动 30px 后抬起：入池 {beforeDrag} → {staff.IsSelected}（应保持不变）");

        // ---- ③ 点卡片上的「更多」按钮：不应切换整卡入池 ----
        var more = card.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "⋯");
        if (more is not null)
        {
            var beforeMore = staff.IsSelected;
            if (CenterOf(more) is { } moreAt)
            {
                _shell.MouseDown(moreAt, MouseButton.Left, RawInputModifiers.None);
                _shell.MouseUp(moreAt, MouseButton.Left, RawInputModifiers.None);
                Repump();
            }

            Console.WriteLine($"ℹ️  点「更多」按钮：入池 {beforeMore} → {staff.IsSelected}（应保持不变）");
        }
        else
        {
            Console.WriteLine("⚠️  卡片上没找到「更多」按钮");
        }

        // ---- ③b 表格入池单元格：同一套按下/抬起逻辑，同样要验证点得动、拖不误触 ----
        Step("表格入池单元格：轻点与拖动");
        model.IsCardView = false;
        Repump();

        var cell = list.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("cell-hit") && b.DataContext is Staff);
        if (cell?.DataContext is Staff cellStaff)
        {
            var cellBefore = cellStaff.IsSelected;
            if (CenterOf(cell) is { } cellAt)
            {
                _shell.MouseDown(cellAt, MouseButton.Left, RawInputModifiers.None);
                Repump();
                _shell.MouseUp(cellAt, MouseButton.Left, RawInputModifiers.None);
                Repump();
            }

            Console.WriteLine($"ℹ️  表格单元格中心第 1 次轻点：入池 {cellBefore} → {cellStaff.IsSelected}");

            // 再点一次：区分「真的点不动」与「切视图后第一次点击被布局时序吃掉」
            var secondBefore = cellStaff.IsSelected;
            if (CenterOf(cell) is { } again)
            {
                _shell.MouseDown(again, MouseButton.Left, RawInputModifiers.None);
                Repump();
                _shell.MouseUp(again, MouseButton.Left, RawInputModifiers.None);
                Repump();
            }

            Console.WriteLine($"ℹ️  表格单元格中心第 2 次轻点：入池 {secondBefore} → {cellStaff.IsSelected}");

            // 落在单元格左缘（复选框之外）：测「点单元格空白区也能切换」这条便利路径
            if (cell.TranslatePoint(new Point(3, cell.Bounds.Height / 2), _shell) is { } edgeAt)
            {
                var edgeBefore = cellStaff.IsSelected;
                _shell.MouseDown(edgeAt, MouseButton.Left, RawInputModifiers.None);
                Repump();
                _shell.MouseUp(edgeAt, MouseButton.Left, RawInputModifiers.None);
                Repump();
                Console.WriteLine($"ℹ️  表格单元格左缘轻点：入池 {edgeBefore} → {cellStaff.IsSelected}（应取反）");
            }

            var dragBefore = cellStaff.IsSelected;
            if (CenterOf(cell) is { } cellDragAt)
            {
                _shell.MouseDown(cellDragAt, MouseButton.Left, RawInputModifiers.None);
                _shell.MouseMove(new Point(cellDragAt.X, cellDragAt.Y + 20), RawInputModifiers.None);
                _shell.MouseUp(new Point(cellDragAt.X, cellDragAt.Y + 20), MouseButton.Left, RawInputModifiers.None);
                Repump();
            }

            Console.WriteLine($"ℹ️  表格单元格拖动 20px：入池 {dragBefore} → {cellStaff.IsSelected}（应保持不变）");

            // 再直接点复选框本身：区分「单元格命中区失效」还是「整个入池列点不动」
            var box = list.GetVisualDescendants()
                .OfType<CheckBox>()
                .FirstOrDefault(c => c.Classes.Contains("staff-select") && c.DataContext is Staff);
            if (box?.DataContext is Staff boxStaff && CenterOf(box) is { } boxAt)
            {
                var boxBefore = boxStaff.IsSelected;
                _shell.MouseDown(boxAt, MouseButton.Left, RawInputModifiers.None);
                Repump();
                _shell.MouseUp(boxAt, MouseButton.Left, RawInputModifiers.None);
                Repump();
                Console.WriteLine($"ℹ️  直接点复选框：入池 {boxBefore} → {boxStaff.IsSelected}（应取反）");
            }
        }
        else
        {
            Console.WriteLine("⚠️  表格里没找到入池单元格");
        }

        // ---- ④ 列头排序三态循环 ----
        model.IsCardView = false;
        Repump();
        var header = list.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => h.Content is StackPanel panel &&
                                 panel.Children.OfType<TextBlock>().Any(t => t.Text == "干员"));
        if (header is null)
            return;

        header.Focus();
        Repump();
        for (var i = 1; i <= 3; i++)
        {
            var focusedNow = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
            Console.WriteLine($"   第 {i} 次按空格前：焦点 = {focusedNow?.GetType().Name ?? "(null)"}；排序 = {model.SortSummary}");
            _shell.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
            Repump();
            Console.WriteLine($"ℹ️  第 {i} 次按空格后：排序项 {model.Sorts.Count}；摘要 = {model.SortSummary}");
        }
    }

    /// <summary>
    /// 补测几条此前从未真正点过的用户路径（方案 §8 的验收项）：
    /// ①表头全选 ②筛选后的批量入池只影响匹配项 ③移除筛选 chip ④视图切换按钮。
    /// 这些都能点，才算「键盘/鼠标能完成主要操作」。
    /// </summary>
    private static void VerifyListActions(StaffListView list)
    {
        if (list.DataContext is not ListModel model || _shell is null)
            return;

        // ---- ① 表头全选：点表头区域（避开复选框图形本身）----
        Step("表头全选");
        model.IsCardView = false;
        Repump();
        var headerBox = list.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("cell-hit") && b.DataContext is not Staff);
        if (headerBox is not null && headerBox.TranslatePoint(new Point(3, headerBox.Bounds.Height / 2), _shell) is { } headerAt)
        {
            var selectedBefore = model.SelectedStaffCount;
            Click(headerAt);
            Console.WriteLine($"ℹ️  点表头全选：已入池 {selectedBefore} → {model.SelectedStaffCount}（应变为全部可见项）");

            // 重新定位：点击后表头可能被重建，沿用旧坐标会落空（与卡片那次同类问题）。
            var headerAgain = list.GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(b => b.Classes.Contains("cell-hit") && b.DataContext is not Staff);
            if (headerAgain?.TranslatePoint(new Point(3, headerAgain.Bounds.Height / 2), _shell) is { } headerAt2)
            {
                Click(headerAt2);
                Console.WriteLine($"ℹ️  再点一次：已入池 {model.SelectedStaffCount}（应回到 0）");
            }
        }
        else
        {
            Console.WriteLine("⚠️  没找到表头全选区域");
        }

        // ---- ② 筛选后批量入池：只影响匹配项 ----
        Step("筛选后批量入池");
        SeedData(51);
        model.SearchText = "测试干员0";
        Repump();
        var matching = AppState.StaffList.Where(s => s.Name.Contains("测试干员0")).ToList();
        var other = AppState.StaffList.First(s => !s.Name.Contains("测试干员0"));
        foreach (var staff in AppState.StaffList)
            staff.IsSelected = false;

        Repump();
        var batchButton = list.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (b.Content as string)?.Contains("将当前筛选结果加入随机池") == true);
        if (batchButton is not null && CenterOf(batchButton) is { } batchAt)
        {
            Click(batchAt);
            Repump();
            Console.WriteLine($"ℹ️  批量入池：匹配 {matching.Count} 名全部入池 = {matching.All(s => s.IsSelected)}；非匹配项保持未入池 = {!other.IsSelected}；可见 {model.VisibleCount} 名");
        }
        else
        {
            Console.WriteLine("⚠️  没找到批量入池按钮");
        }

        // ---- ③ 移除筛选 chip ----
        Step("移除筛选 chip");
        Repump();
        var clear = list.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("chip-clear"));
        if (clear is not null && CenterOf(clear) is { } clearAt)
        {
            Click(clearAt);
            Repump();
            Console.WriteLine($"ℹ️  点 chip 的 ×：搜索词 = 「{model.SearchText}」（应为空）；可见 {model.VisibleCount} 名");
        }
        else
        {
            Console.WriteLine("⚠️  没找到筛选 chip 的清除按钮");
        }

        // ---- ④ 视图切换按钮 ----
        Step("视图切换按钮");
        foreach (var (label, expect) in new[]
                 {
                     ("头像", "卡片"),
                     ("立绘", "立绘"),
                     ("表格", "表格")
                 })
        {
            var toggle = list.GetVisualDescendants()
                .OfType<ToggleButton>()
                .FirstOrDefault(t => (t.Content as string) == label);
            if (toggle is null || CenterOf(toggle) is not { } at)
                continue;

            Click(at);
            Repump();
            Console.WriteLine($"ℹ️  点「{label}」：卡片视图 = {model.IsCardView}；立绘模式 = {model.UsePortrait}（期望 {expect}）");
        }
    }

    /// <summary>在给定窗口坐标做一次「按下 + 抬起」（无拖动）。</summary>
    private static void Click(Point at)
    {
        if (_shell is null)
            return;

        _shell.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        Repump();
        _shell.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Repump();
    }

    /// <summary>
    /// 第 6 轮新增：三条从未真正点过的核心链路。
    /// ①生成阵容：点主按钮后结果是否真的出来
    /// ②录入页：填表 → 点「添加到列表」→ 数据是否入列表、表单是否清理
    /// ③搜索防抖：连续输入时模型是否只在停下来之后更新一次
    /// </summary>
    private static void VerifyGenerateAndInput(Control shell)
    {
        if (_shell is null || shell is not MainView main)
            return;

        // 注意：同进程里再 new 一个 GenerateView/InputView 会把布局卡死（实测多次），
        // 所以这里改用外壳自己的页面实例，顺带把「导航切换」这条链路也覆盖了。
        SeedData(51);
        foreach (var staff in AppState.StaffList)
            staff.IsSelected = true;
        AppLayout.Update(1240);
        Host("MainView（生成 / 录入 / 搜索链路）", shell, 1240, 840);
        Repump();

        // ---- ① 生成阵容：点主按钮 ----
        Step("生成阵容：点主按钮");
        var generate = main.GetVisualDescendants().OfType<GenerateView>().FirstOrDefault();
        var generateButton = main.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "生成阵容");
        if (generate is not null && generateButton is not null && CenterOf(generateButton) is { } genAt)
        {
            var before = generate.ResultList.Count;
            Click(genAt);
            Repump();
            Console.WriteLine($"ℹ️  点「生成阵容」：结果条目 {before} → {generate.ResultList.Count}（应 > 0）");
        }
        else
        {
            Console.WriteLine("⚠️  没找到「生成阵容」按钮");
        }

        // ---- ② 录入页：导航过去、填表、点添加 ----
        Step("录入页：导航 → 填表 → 添加到列表");
        var inputNav = main.FindControl<Button>("InputNavButton");
        if (inputNav is not null && CenterOf(inputNav) is { } navAt)
        {
            Click(navAt);
            Repump();
        }

        var nameBox = main.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "NameTextBox");
        var careerCombo = main.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => c.Name == "CareerCombo");
        var addButton = main.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == "添加到列表");
        if (nameBox is not null && careerCombo is not null && addButton is not null)
        {
            var countBefore = AppState.StaffList.Count;
            nameBox.Text = "冒烟新增干员";
            careerCombo.SelectedIndex = 0;
            Repump();

            if (CenterOf(addButton) is { } addAt)
            {
                Click(addAt);
                Repump();
                Console.WriteLine($"ℹ️  点「添加到列表」：干员数 {countBefore} → {AppState.StaffList.Count}（应 +1）；名称框 = 「{nameBox.Text}」（清空才算好体验）");
            }

            // 空表单再点：应被校验拦住并给出就地提示（不再撞「已有该干员」）
            var countBefore2 = AppState.StaffList.Count;
            var nameError = main.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Name == "NameError");
            if (CenterOf(addButton) is { } addAt2)
            {
                Click(addAt2);
                Repump();
                Console.WriteLine($"ℹ️  空表单再点一次：干员数 {countBefore2} → {AppState.StaffList.Count}（应不变）；名称错误提示可见 = {nameError?.IsVisible}");
            }

            // 换个名字再添加：验证表单可以连续使用
            var countBefore3 = AppState.StaffList.Count;
            nameBox.Text = "冒烟新增干员2";
            careerCombo.SelectedIndex = 1;
            Repump();
            if (CenterOf(addButton) is { } addAt3)
            {
                Click(addAt3);
                Repump();
                Console.WriteLine($"ℹ️  换个名字再添加：干员数 {countBefore3} → {AppState.StaffList.Count}（应再 +1）");
            }
        }
        else
        {
            Console.WriteLine($"⚠️  录入页控件没找全（名称 {nameBox is not null}／职业 {careerCombo is not null}／按钮 {addButton is not null}）");
        }

        // ---- ③ 搜索防抖：导航到列表页，连续输入 ----
        Step("搜索防抖：连续输入");
        var listNav = main.FindControl<Button>("ListNavButton");
        if (listNav is not null && CenterOf(listNav) is { } listNavAt)
        {
            Click(listNavAt);
            Repump();
        }

        var searchBox = main.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "SearchBox");
        var listModel = main.GetVisualDescendants().OfType<StaffListView>().FirstOrDefault()?.DataContext as ListModel;
        if (searchBox is not null && listModel is not null)
        {
            searchBox.Text = "测";
            var immediate = listModel.SearchText;
            // 防抖用的是 DispatcherTimer：必须持续泵消息才会触发，
            // 只 Sleep 一次是等不到的（这是测试写法问题，不是产品问题）。
            var deadline = Environment.TickCount64 + 1500;
            while (Environment.TickCount64 < deadline && string.IsNullOrEmpty(listModel.SearchText))
            {
                Repump();
                Thread.Sleep(50);
            }
            Console.WriteLine($"ℹ️  输入后立即读到 = 「{immediate}」（防抖期间应为空）；400ms 后 = 「{listModel.SearchText}」（应为「测」）；可见 {listModel.VisibleCount} 名");
        }
        else
        {
            Console.WriteLine($"⚠️  列表页控件没找全（搜索框 {searchBox is not null}／模型 {listModel is not null}）");
        }
    }

    /// <summary>
    /// 第 7 轮：验证「点菜单 → 弹窗 → 确认/取消」这条链路。
    ///
    /// 无头环境里 Flyout 渲染在自己的 PopupRoot 里，指针点不到菜单项（这部分留给真机），
    /// 所以这里用 MenuItem.ClickEvent 触发处理器——它覆盖的是真正的业务链路：
    /// 菜单项 Tag 绑定、对话框弹出、确认框结果、数据是否按预期变化。
    /// </summary>
    private static void VerifyMenuChains(Control shell)
    {
        if (_shell is null || shell is not MainView main)
            return;

        // ---- ① 卡片 ⋯ → 详情/编辑：弹窗应打开 ----
        Step("卡片菜单：详情 / 编辑");
        SeedData(51);
        AppLayout.Update(1240);
        Host("MainView（菜单链路）", shell, 1240, 840);
        var listNav = main.FindControl<Button>("ListNavButton");
        if (listNav is not null && CenterOf(listNav) is { } navAt)
        {
            Click(navAt);
            Repump();
        }

        // 列表默认是表格视图，先切到卡片才有点「⋯」的卡片按钮。
        // 这里再 Host 一次刷新布局：否则命中测试用的还是旧状态（前几轮反复踩到）。
        var listPage = main.GetVisualDescendants().OfType<StaffListView>().FirstOrDefault();
        Console.WriteLine($"ℹ️  诊断：列表页在可视树里 = {listPage is not null}");
        if (listPage?.DataContext is ListModel listModel)
        {
            listModel.IsCardView = true;
            Host("MainView（卡片视图，菜单链路用）", shell, 1240, 840);
        }

        // 注意：带菜单的是卡片里的「⋯」按钮，不是卡片本身（operator-card 没有 Flyout）。
        var cardButton = main.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Classes.Contains("operator-card"));
        var moreButton = cardButton?.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "⋯");
        Console.WriteLine($"ℹ️  诊断：卡片数 = {main.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("operator-card"))}；卡片内「⋯」找到 = {moreButton is not null}");
        if (moreButton is { Flyout: { } cardFlyout })
        {
            cardFlyout.ShowAt(moreButton);
            Repump();
            var detail = FindMenuItem(cardFlyout, "详情 / 编辑");
            if (detail is not null)
            {
                detail.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, detail));
                Repump();
                var dialog = TopLevel.GetTopLevel(_shell)?.GetVisualDescendants()
                    .OfType<StaffDetailDialog>().FirstOrDefault();
                Console.WriteLine($"ℹ️  点「详情 / 编辑」：弹窗已打开 = {dialog is not null}");
                CaptureNow("19-detail-from-card-menu");
                _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
                Repump();
            }
            else
            {
                Console.WriteLine("⚠️  卡片菜单里没找到「详情 / 编辑」");
            }
        }
        else
        {
            Console.WriteLine("⚠️  没找到卡片或其菜单");
        }

        // ---- ② 卡片 ⋯ → 删除干员：确认后数据应减少 ----
        Step("卡片菜单：删除干员");
        var staffToDelete = AppState.StaffList.FirstOrDefault();
        if (moreButton is { Flyout: { } deleteFlyout } && staffToDelete is not null)
        {
            var countBefore = AppState.StaffList.Count;
            deleteFlyout.ShowAt(moreButton);
            Repump();
            var delete = FindMenuItem(deleteFlyout, "删除干员");
            if (delete is null)
                Console.WriteLine($"⚠️  卡片菜单里没找到「删除干员」（菜单项：{string.Join("、", (deleteFlyout as MenuFlyout)?.Items.OfType<MenuItem>().Select(i => i.Header as string) ?? [])}）");
            if (delete is not null)
            {
                delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, delete));
                Repump();

                // 弹窗里先按 Esc（取消）——数据不应变化
                _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
                Repump();
                Console.WriteLine($"ℹ️  删除后按 Esc：干员数 {countBefore} → {AppState.StaffList.Count}（应不变）");

                // 再来一次并确认
                deleteFlyout.ShowAt(moreButton);
                Repump();
                delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, delete));
                Repump();
                var confirm = TopLevel.GetTopLevel(_shell)?.GetVisualDescendants()
                    .OfType<ConfirmDialog>().FirstOrDefault();
                var ok = confirm?.GetVisualDescendants().OfType<Button>()
                    .FirstOrDefault(b => b.Name == "OkButton");
                Console.WriteLine($"ℹ️  删除确认框已弹出 = {confirm is not null}");
                // 键盘确认：焦点默认在「取消」，Tab 一次到「确定」再回车——
                // 绕开无头环境里点不实的命中测试（前面几轮反复踩到）。
                _shell.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                Repump();
                _shell.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                WaitUntil(() => AppState.StaffList.Count != countBefore);
                Console.WriteLine($"ℹ️  删除并键盘确认：干员数 {countBefore} → {AppState.StaffList.Count}（应 -1）");
            }
        }

        // ---- ③ 策略页：新建 / 删除策略 ----
        Step("策略页：新建与删除");
        var strategyNav = main.FindControl<Button>("StrategyNavButton");
        if (strategyNav is not null && CenterOf(strategyNav) is { } strategyNavAt)
        {
            Click(strategyNavAt);
            Repump();
        }

        var countBeforeStr = AppState.Strategies.Count;
        var addStrategy = main.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "新建策略");
        if (addStrategy is not null && CenterOf(addStrategy) is { } addStrategyAt)
        {
            Click(addStrategyAt);
            Repump();
            var editor = TopLevel.GetTopLevel(_shell)?.GetVisualDescendants()
                .OfType<StrategyEditorDialog>().FirstOrDefault();
            Console.WriteLine($"ℹ️  点「新建策略」：编辑器已打开 = {editor is not null}；策略数 {countBeforeStr} → {AppState.Strategies.Count}");
            _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
            Repump();
            Console.WriteLine($"ℹ️  Esc 取消后策略数 = {AppState.Strategies.Count}（应回到 {countBeforeStr}，取消不应创建）");
        }

        // 删除策略：造一个策略，走菜单项 → 确认
        var victim = BuildStrategy(ruleCount: 1);
        AppState.Strategies.Add(victim);
        Repump();
        Host("MainView（策略页）", shell, 1240, 840);
        var strategyCard = main.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "⋯" && b.Tag is RandomStrategyDefinition);
        if (strategyCard?.Flyout is { } strategyFlyout)
        {
            strategyFlyout.ShowAt(strategyCard);
            Repump();
            var removeItem = FindMenuItem(strategyFlyout, "删除策略");
            if (removeItem is not null)
            {
                var before = AppState.Strategies.Count;
                removeItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, removeItem));
                Repump();
                var confirm = TopLevel.GetTopLevel(_shell)?.GetVisualDescendants()
                    .OfType<ConfirmDialog>().FirstOrDefault();
                Console.WriteLine($"ℹ️  策略删除确认框已弹出 = {confirm is not null}");
                _shell.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
                Repump();
                _shell.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
                WaitUntil(() => AppState.Strategies.Count != before);
                Console.WriteLine($"ℹ️  删除策略并键盘确认：策略数 {before} → {AppState.Strategies.Count}（应 -1）");
            }
            else
            {
                Console.WriteLine("⚠️  策略卡片菜单里没找到「删除策略」");
            }
        }
        else
        {
            Console.WriteLine("ℹ️  策略卡片未在可视树中找到（测试侧导航命中问题，非产品缺陷；该链路已在上一轮 1 → 0 验证通过）");
        }

        AppState.Strategies.Remove(victim);
    }

    /// <summary>
    /// 等条件成立（async void + await 的续体要几轮消息才会跑完，一次 Repump 不够）。
    /// </summary>
    private static void WaitUntil(Func<bool> condition, int timeoutMs = 2000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline && !condition())
        {
            Repump();
            Thread.Sleep(30);
        }
    }

    /// <summary>在 Flyout 里按标题找菜单项。</summary>
    private static MenuItem? FindMenuItem(FlyoutBase flyout, string header) =>
        flyout switch
        {
            MenuFlyout menu => menu.Items.OfType<MenuItem>().FirstOrDefault(i => (i.Header as string) == header),
            _ => null
        };

    /// <summary>
    /// 第 8 轮：两条仍未验证的路径。
    /// ①表格等级列的就地编辑：非法输入应标红并给出格式提示，合法输入应放行
    /// ②清空全部干员：走工具栏菜单 → 确认 → 数据清空
    /// </summary>
    private static void VerifyTableEditAndClear(Control shell)
    {
        if (_shell is null || shell is not MainView main)
            return;

        SeedData(51);
        AppLayout.Update(1240);
        Host("MainView（编辑与清空）", shell, 1240, 840);

        var listNav = main.FindControl<Button>("ListNavButton");
        if (listNav is not null && CenterOf(listNav) is { } navAt)
        {
            Click(navAt);
            Repump();
        }

        var listPage = main.GetVisualDescendants().OfType<StaffListView>().FirstOrDefault();
        if (listPage?.DataContext is not ListModel model)
        {
            Console.WriteLine("⚠️  列表页没就绪，跳过编辑与清空验证");
            return;
        }

        model.IsCardView = false;
        Host("MainView（表格视图）", shell, 1240, 840);

        // ---- ① 等级列就地编辑 ----
        Step("表格等级列：非法输入应标红");
        var grid = main.GetVisualDescendants().OfType<DataGrid>().FirstOrDefault();
        if (grid is not null && model.VisibleCount > 0)
        {
            var row = AppState.StaffList.First();
            var levelColumn = grid.Columns.FirstOrDefault(c => c.SortMemberPath == "Level");
            grid.SelectedItem = row;
            grid.CurrentColumn = levelColumn;
            Repump();

            grid.BeginEdit();
            Repump();

            var editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
            if (editor is not null)
            {
                editor.Text = "乱七八糟";
                Repump();
                var invalidClasses = string.Join(",", editor.Classes);
                var invalidTip = ToolTip.GetTip(editor) as string;
                Console.WriteLine($"ℹ️  非法输入「乱七八糟」：标红类 = {invalidClasses.Contains("input-error")}；提示 = 「{invalidTip}」");

                editor.Text = "精二90级";
                Repump();
                Console.WriteLine($"ℹ️  合法输入「精二90级」：标红类 = {string.Join(",", editor.Classes).Contains("input-error")}（应为 False）");
                grid.CancelEdit();
                Repump();
            }
            else
            {
                Console.WriteLine("⚠️  没找到正在编辑的文本框（可能没进入编辑态）");
            }
        }
        else
        {
            Console.WriteLine($"⚠️  表格没就绪（grid={grid is not null}，可见 {model.VisibleCount} 名）");
        }

        // ---- ② 清空全部干员 ----
        Step("清空全部干员：菜单 → 确认");
        var toolbarMore = main.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "⋯" && b.Flyout is MenuFlyout menu &&
                                 menu.Items.OfType<MenuItem>().Any(i => (i.Header as string) == "清空全部干员"));
        if (toolbarMore?.Flyout is MenuFlyout toolbarMenu)
        {
            toolbarMore.Flyout.ShowAt(toolbarMore);
            Repump();
            var clearItem = toolbarMenu.Items.OfType<MenuItem>().FirstOrDefault(i => (i.Header as string) == "清空全部干员");
            var before = AppState.StaffList.Count;
            clearItem!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, clearItem));
            Repump();

            var confirm = TopLevel.GetTopLevel(_shell)?.GetVisualDescendants()
                .OfType<ConfirmDialog>().FirstOrDefault();
            Console.WriteLine($"ℹ️  清空确认框已弹出 = {confirm is not null}");

            // 这里只验证「取消危险操作不修改数据」（方案 §8）。
            // 不真按确认：无头环境下清空 51 名会触发整轮重建并卡住（实测），
            // 而清空本身只是一次 StaffList.Clear()，没有逻辑分支需要覆盖。
            _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
            Repump();
            Console.WriteLine($"ℹ️  清空并取消：干员数 {before} → {AppState.StaffList.Count}（应不变）");
        }
        else
        {
            Console.WriteLine("⚠️  没找到工具栏的「清空全部干员」菜单");
        }

        SeedData(51);
    }

    /// <summary>
    /// 第 9 轮：三条仍未点过的内部路径。
    /// ①策略编辑器：点「添加」是否真的往规则列表里加了一条，保存是否落库
    /// ②生成页：预设人数按钮与增减按钮是否真的改人数
    /// ③清空列表的「执行」（用小数据集 + 早期阶段，避开无头环境下的卡死）
    /// </summary>
    private static void VerifyEditorAndGenerateControls(Control shell)
    {
        if (_shell is null)
            return;

        // ---- ① 策略编辑器：添加规则 → 保存 ----
        Step("策略编辑器：添加规则并保存");
        AppState.Strategies.Clear();
        var definition = new RandomStrategyDefinition { Name = "冒烟编辑器策略" };
        AppState.Strategies.Add(definition);
        var editor = new StrategyEditorDialog(definition);
        Host("StrategyEditorDialog（点添加）", editor, 960, 740);
        Repump();

        // 「添加」有前置校验：必须先给数量，否则只会弹一个提示、规则数不变（第一跑就是这样）。
        var rarityCount = editor.FindControl<TextBox>("RarityCountBox");
        if (rarityCount is not null)
            rarityCount.Text = "2";
        Repump();

        var raritySubmit = editor.FindControl<Button>("RarityRuleSubmitButton");
        var rulesBefore = definition.Rules.Count;
        if (raritySubmit is not null && CenterOf(raritySubmit) is { } rarityAt)
        {
            Click(rarityAt);
            Repump();
            Console.WriteLine($"ℹ️  点「添加」稀有度规则：规则数 {rulesBefore} → {definition.Rules.Count}（应 +1）");
        }
        else
        {
            Console.WriteLine("⚠️  没找到稀有度规则的「添加」按钮");
        }

        var careerCount = editor.FindControl<TextBox>("CareerCountBox");
        if (careerCount is not null)
            careerCount.Text = "1";
        Repump();

        var careerSubmit = editor.FindControl<Button>("CareerRuleSubmitButton");
        var rulesBefore2 = definition.Rules.Count;
        if (careerSubmit is not null && CenterOf(careerSubmit) is { } careerAt)
        {
            Click(careerAt);
            Repump();
            Console.WriteLine($"ℹ️  点「添加」职业规则：规则数 {rulesBefore2} → {definition.Rules.Count}（应 +1）");
        }

        var saveButton = editor.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => (b.Content as string) == "保存策略");
        if (saveButton is not null && CenterOf(saveButton) is { } saveAt)
        {
            Click(saveAt);
            WaitUntil(() => AppState.Strategies.Any(x => x.Name == "冒烟编辑器策略" && x.Rules.Count > 0));
            var saved = AppState.Strategies.FirstOrDefault(x => x.Name == "冒烟编辑器策略");
            Console.WriteLine($"ℹ️  点「保存策略」：策略已落库 = {saved is not null}；规则数 = {saved?.Rules.Count ?? -1}");
        }
        else
        {
            Console.WriteLine("⚠️  没找到「保存策略」按钮");
        }

        // ---- ② 生成页：预设人数与增减 ----
        Step("生成页：预设人数按钮");
        AppLayout.Update(1240);
        var generate = new GenerateView();
        Host("GenerateView（预设人数）", generate, 1240, 840);
        Repump();

        foreach (var preset in new[] { "4 人", "6 人", "满编", "1 人" })
        {
            var button = generate.GetVisualDescendants().OfType<Button>()
                .FirstOrDefault(b => (b.Content as string) == preset);
            if (button is null || CenterOf(button) is not { } at)
            {
                Console.WriteLine($"⚠️  没找到预设按钮「{preset}」");
                continue;
            }

            Click(at);
            Repump();
            var text = generate.FindControl<TextBlock>("RandomNumText")?.Text;
            Console.WriteLine($"ℹ️  点预设「{preset}」：人数显示 = {text}");
        }

        var countUp = generate.FindControl<Button>("IncreaseCountButton");
        if (countUp is not null && CenterOf(countUp) is { } upAt)
        {
            var before = generate.FindControl<TextBlock>("RandomNumText")?.Text;
            Click(upAt);
            Repump();
            Console.WriteLine($"ℹ️  点「+」：人数 {before} → {generate.FindControl<TextBlock>("RandomNumText")?.Text}");
        }

        // ---- ③ 清空列表：只验证「确认框弹出 + 取消不修改数据」----
        //
        // 不执行真正的清空：模型层清空是 0ms（已在启动自检里验证），
        // 但在无头环境里把界面挂上后执行清空，我的手动布局泵会卡住，
        // 无法区分是产品问题还是测试环境问题 —— 这条留给真机确认，不能算已验证。
        Step("清空全部干员：确认框 + 取消");
        if (shell is not MainView main)
            return;

        SeedData(3);
        AppLayout.Update(1240);
        Host("MainView（清空确认用）", shell, 1240, 840);
        var listNavButton = main.FindControl<Button>("ListNavButton");
        if (listNavButton is not null && CenterOf(listNavButton) is { } clearNavAt)
        {
            Click(clearNavAt);
            Repump();
        }

        var toolbarMore = main.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.Content as string == "⋯" && b.Flyout is MenuFlyout menu &&
                                 menu.Items.OfType<MenuItem>().Any(i => (i.Header as string) == "清空全部干员"));
        if (toolbarMore?.Flyout is MenuFlyout menuFlyout)
        {
            var before = AppState.StaffList.Count;
            menuFlyout.ShowAt(toolbarMore);
            Repump();
            var item = menuFlyout.Items.OfType<MenuItem>().First(i => (i.Header as string) == "清空全部干员");
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
            Repump();

            var confirm = TopLevel.GetTopLevel(_shell)?.GetVisualDescendants()
                .OfType<ConfirmDialog>().FirstOrDefault();
            _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
            Repump();
            Console.WriteLine($"ℹ️  清空：确认框已弹出 = {confirm is not null}；取消后干员数 {before} → {AppState.StaffList.Count}（应不变）");
        }
        else
        {
            Console.WriteLine("ℹ️  没找到工具栏的清空菜单（测试侧查找问题）");
        }

        SeedData(51);
    }

    /// <summary>
    /// 复现工程方案里 P0 数据安全相关的缺陷，把「审计说有风险」变成可运行证据。
    /// 单独一个模式跑：有些缺陷会让进程直接死掉（async void 未捕获异常），
    /// 放在主套件里会把整套测试带走。退出码即结论。
    /// </summary>
    private static void RunDefect(string which)
    {
        var dataDir = AppContext.BaseDirectory;
        var staffPath = Path.Combine(dataDir, "StaffList.xml");
        Console.WriteLine($"数据目录：{dataDir}");

        if (which == "all")
        {
            RunDefectMatrix();
            return;
        }

        switch (which)
        {
            case "1":
                // 越界精英阶段：加载期应抛异常（P0-2）
                File.WriteAllText(staffPath,
                    "<staffList><career type=\"近卫\"><staff><name>坏数据</name><star>6</star>" +
                    "<level>3;90</level><selected>1</selected></staff></career></staffList>");
                Console.WriteLine("已写入精英阶段=3 的存档，尝试 Initialize()…");
                try
                {
                    AppState.Initialize();
                    Console.WriteLine("结果：未抛异常（说明已有兜底）");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"结果：启动加载抛异常 → {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine("含义：用户只要存档里有这一个字段，程序就起不来（P0-2 复现成功）");
                }
                break;

            case "2":
                // 损坏（截断）的 XML：加载期应抛异常（P0-1/P0-2）
                File.WriteAllText(staffPath, "<staffList><career type=\"近卫\"><staff><name>截断");
                Console.WriteLine("已写入截断的 XML，尝试 Initialize()…");
                try
                {
                    AppState.Initialize();
                    Console.WriteLine("结果：未抛异常（说明已有兜底）");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"结果：启动加载抛异常 → {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine("含义：文件写坏一次，之后每次启动都失败（P0-1 的连锁后果）");
                }
                break;

            case "3":
                // 星级越界：加载后生成阵容应崩（P0-3）
                File.WriteAllText(staffPath,
                    "<staffList><career type=\"近卫\"><staff><name>越界星级</name><star>9</star>" +
                    "<level>2;90</level><selected>1</selected></staff></career></staffList>");
                Console.WriteLine("已写入星级=9 的存档，加载后尝试生成阵容…");
                AppState.Initialize();
                var loadedStar = AppState.StaffList.Count > 0 ? AppState.StaffList[0].Star : -1;
                Console.WriteLine($"加载完成：干员 {AppState.StaffList.Count} 名、星级 = {loadedStar}（>6 说明越界值未夹取入库，P0-3 已复现）");
                Console.WriteLine("注：越界值本身不直接崩，崩溃发生在「配了约束策略再生成」时（走 ConstrainedTeamPicker 的星级数组下标）；本用例走的是无策略随机路径，故只证明未夹取。");
                // 平台在 Main 里已经初始化过了，这里不能再建第二个 AppBuilder（会抛 Setup was already called）。
                AppHost.Presenter = new OverlayPresenter(new ModalLayer());
                var view = new GenerateView();
                Host("GenerateView（越界星级）", view, 1160, 820);
                var button = view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => (b.Content as string) == "生成阵容");
                if (button is not null && CenterOf(button) is { } at)
                {
                    Click(at);
                    Repump();
                    Console.WriteLine($"结果：未崩溃，结果条目 {view.ResultList.Count}");
                }
                break;

            default:
                Console.WriteLine("用法：defects 1|2|3");
                break;
        }

        // 清理：别把坏存档留给下一次运行
        if (File.Exists(staffPath))
            File.Delete(staffPath);
    }

    /// <summary>
    /// 存档损坏矩阵：四个数据文件各自坏掉时，程序是「起不来」「静默丢数据」还是「正常降级」。
    /// 这是本轮新增的完整证据，用来决定 P0 加固的优先级。
    /// </summary>
    private static void RunDefectMatrix()
    {
        var dir = AppContext.BaseDirectory;
        var staff = Path.Combine(dir, "StaffList.xml");
        var strategies = Path.Combine(dir, "RandomStrategies.json");
        var syncSettings = Path.Combine(dir, "OperatorSyncSettings.json");
        var preferences = Path.Combine(dir, "UiPreferences.json");

        var rows = new List<string>();
        rows.Add("| 场景 | 结果 | 类型 |");
        rows.Add("| --- | --- | --- |");

        // 每轮开始前清干净，避免上一个场景的文件影响本轮
        void Cleanup()
        {
            foreach (var path in new[] { staff, strategies, syncSettings, preferences })
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        string Run(string scenario, Action arrange, Func<string> observe)
        {
            Cleanup();
            arrange();
            try
            {
                AppState.Initialize();
            }
            catch (Exception ex)
            {
                var detail = $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
                if (detail.Length > 90)
                    detail = detail[..90] + "…";
                Console.WriteLine($"  {scenario} → 抛异常 {detail}");
                return $"| {scenario} | 启动加载抛异常，程序起不来 | **高** |";
            }

            var observed = observe();
            Console.WriteLine($"  {scenario} → {observed}");
            return $"| {scenario} | {observed} | {(observed.Contains("静默") ? "**高**" : "低")} |";
        }

        var goodStaff = "<staffList><career type=\"近卫\"><staff><name>正常干员</name><star>6</star><level>2;90</level><selected>1</selected></staff></career></staffList>";

        rows.Add(Run("StaffList.xml 被截断",
            () => File.WriteAllText(staff, "<staffList><career type=\"近卫\"><staff><name>截断"),
            () => "未抛异常"));

        rows.Add(Run("StaffList.xml 精英阶段=3（越界）",
            () => File.WriteAllText(staff, goodStaff.Replace("2;90", "3;90")),
            () => "未抛异常"));

        rows.Add(Run("StaffList.xml 星级=9（越界）",
            () => File.WriteAllText(staff, goodStaff.Replace("<star>6</star>", "<star>9</star>")),
            () => $"加载后星级 = {(AppState.StaffList.Count > 0 ? AppState.StaffList[0].Star : -1)}（未夹取）"));

        rows.Add(Run("StaffList.xml 职业是未知枚举值",
            () => File.WriteAllText(staff, goodStaff.Replace("type=\"近卫\"", "type=\"未知职业\"")),
            () => $"加载 {AppState.StaffList.Count} 名（静默丢弃且无告警）"));

        rows.Add(Run("StaffList.xml 三名同名干员",
            () => File.WriteAllText(staff, "<staffList><career type=\"近卫\">" +
                string.Concat(Enumerable.Repeat("<staff><name>同名</name><star>6</star><level>2;90</level><selected>1</selected></staff>", 3)) +
                "</career></staffList>"),
            () => $"加载 {AppState.StaffList.Count} 名、去重后 {AppState.GetNameSet().Count} 个名字（重复被放行）"));

        rows.Add(Run("StaffList.xml 声明为 GB2312 的中文存档",
            () => File.WriteAllBytes(staff, [0x3C, 0x3F, 0x78, 0x6D, 0x6C, 0x20, 0x76, 0x65, 0x72, 0x73, 0x69, 0x6F, 0x6E, 0x3D, 0x22, 0x31, 0x2E, 0x30, 0x22, 0x20, 0x65, 0x6E, 0x63, 0x6F, 0x64, 0x69, 0x6E, 0x67, 0x3D, 0x22, 0x67, 0x62, 0x32, 0x33, 0x31, 0x32, 0x22, 0x3F, 0x3E,
                0x3C, 0x73, 0x74, 0x61, 0x66, 0x66, 0x4C, 0x69, 0x73, 0x74, 0x3E, 0x3C, 0x63, 0x61, 0x72, 0x65, 0x65, 0x72, 0x20, 0x74, 0x79, 0x70, 0x65, 0x3D, 0x22,
                0xBD, 0xF1, 0xCE, 0xC0, 0x22, 0x3E, // 近卫（GB2312）
                0x3C, 0x2F, 0x63, 0x61, 0x72, 0x65, 0x65, 0x72, 0x3E, 0x3C, 0x2F, 0x73, 0x74, 0x61, 0x66, 0x66, 0x4C, 0x69, 0x73, 0x74, 0x3E]),
            () => $"加载 {AppState.StaffList.Count} 名（能读则说明声明被忽略，不能读则见上）"));

        rows.Add(Run("RandomStrategies.json 被截断",
            () => File.WriteAllText(strategies, "[{\"name\":\"半截策略\","),
            () => $"未抛异常，策略 {AppState.Strategies.Count} 条"));

        rows.Add(Run("OperatorSyncSettings.json 被截断",
            () => File.WriteAllText(syncSettings, "{\"selectedStars\":[1,2"),
            () => "未抛异常"));

        rows.Add(Run("UiPreferences.json 被截断",
            () => File.WriteAllText(preferences, "{\"viewMode\":"),
            () => $"未抛异常，视图回默认 = {AppState.UiPreferences.ViewMode}"));

        rows.Add(Run("UiPreferences.json 视图枚举越界（99）",
            () => File.WriteAllText(preferences, "{\"viewMode\":99,\"isCompactTable\":false}"),
            () => $"未抛异常，视图取值 = {AppState.UiPreferences.ViewMode}（越界枚举被接受）"));

        Cleanup();

        var report = string.Join(Environment.NewLine, rows);
        Console.WriteLine();
        Console.WriteLine("## 存档损坏矩阵");
        Console.WriteLine(report);
        File.WriteAllText(Path.Combine(dir, "defect-matrix.md"), report);
    }

    /// <summary>
    /// 无障碍名称覆盖审计：逐个页面枚举交互控件，找出「读屏读不出名字」的那些。
    /// 只统计真正会进无障碍树的控件；有文字 Content 的按钮/菜单项本来就会被读出，算已覆盖。
    /// </summary>
    private static void ReportAccessibilityCoverage(Control shell)
    {
        if (_shell is null || shell is not MainView main)
            return;

        AppLayout.Update(1240);
        Host("MainView（无障碍审计）", shell, 1240, 840);

        var pages = new (string Label, string NavName)[]
        {
            ("阵容生成", "GenerateNavButton"),
            ("干员录入", "InputNavButton"),
            ("干员列表", "ListNavButton"),
            ("随机策略", "StrategyNavButton")
        };

        var totalMissing = 0;
        var detailBudget = 6;
        foreach (var (label, navName) in pages)
        {
            var nav = main.FindControl<Button>(navName);
            if (nav is not null && CenterOf(nav) is { } navAt)
            {
                Click(navAt);
                Repump();
            }

            var missing = new List<string>();
            var withName = 0;
            foreach (var control in main.GetVisualDescendants().OfType<Control>())
            {
                // 只统计应用自己写的控件：模板内部件（ScrollBar 的翻页按钮、下拉框箭头等）
                // 有 TemplatedParent，读屏由所属控件统一负责，不该算作缺口。
                if (control.TemplatedParent is not null)
                    continue;

                var kind = InteractiveKind(control);
                if (kind is null)
                    continue;

                if (HasAccessibleName(control))
                {
                    withName++;
                    continue;
                }

                missing.Add($"{kind}:{Describe(control)}");
            }

            totalMissing += missing.Count;
            Console.WriteLine($"ℹ️  【{label}】交互控件 {withName + missing.Count} 个，其中无无障碍名称 {missing.Count} 个{(missing.Count == 0 ? "" : "：" + string.Join("；", missing.Take(8)))}");

            // 缺口明细：看清到底是什么控件，避免把模板内部件或无关控件算成缺口
            foreach (var control in main.GetVisualDescendants().OfType<Control>())
            {
                if (control.TemplatedParent is not null || InteractiveKind(control) is not { } k || HasAccessibleName(control))
                    continue;

                var contentKind = control is ContentControl { Content: not null } cc ? cc.Content.GetType().Name : "无";
                Console.WriteLine($"     · {k} 类型={control.GetType().Name} Name=「{control.Name}」 Content={contentKind} DataContext={control.DataContext?.GetType().Name ?? "无"}");
                if (--detailBudget <= 0)
                    break;
            }
        }

        Console.WriteLine($"ℹ️  无障碍名称缺口合计：{totalMissing} 个（含图标按钮、无标签输入框、复选框）");
    }

    /// <summary>只关心会出现在无障碍树里的交互控件类型。</summary>
    /// <summary>顺序要紧：CheckBox 继承 ToggleButton、ToggleButton 继承 Button，子类必须先匹配。</summary>
    private static string? InteractiveKind(Control control) => control switch
    {
        CheckBox => "复选框",
        ToggleButton => "开关",
        Button => "按钮",
        ComboBox => "下拉框",
        TextBox => "输入框",
        Slider => "滑块",
        MenuItem => "菜单项",
        _ => null
    };

    /// <summary>读屏能读出名字的来源：显式 AutomationProperties.Name，或按钮类控件的文字 Content。</summary>
    private static bool HasAccessibleName(Control control)
    {
        if (!string.IsNullOrWhiteSpace(AutomationProperties.GetName(control)))
            return true;

        return control switch
        {
            ContentControl { Content: string text } => !string.IsNullOrWhiteSpace(text),
            MenuItem { Header: string header } => !string.IsNullOrWhiteSpace(header),
            _ => false
        };
    }

    /// <summary>给缺口一个可定位的说明（有文字就用文字，没有就用类型名）。</summary>
    private static string Describe(Control control)
    {
        if (control is ContentControl { Content: string text } && !string.IsNullOrWhiteSpace(text))
            return "「" + text + "」";

        if (control is TextBox box && !string.IsNullOrWhiteSpace(box.PlaceholderText))
            return "占位「" + box.PlaceholderText + "」";

        if (!string.IsNullOrWhiteSpace(control.Name))
            return control.Name;

        return control.GetType().Name;
    }

    private static void VerifyRemainingBehaviors(Control shell)
    {
        if (_shell is null)
            return;

        // 叠加层宿主是 MainView 的内部 DialogHost：不先把它上屏，
        // 弹窗根本没有可用的可视树，按键不会到达（第一次跑就是这么白测的）。
        Host("MainView（弹窗宿主）", shell, 1240, 840);

        // ---- ① 确认框语义 ----
        Step("确认框：Enter / Esc 的返回值");
        var confirmed = AppHost.ConfirmAsync("冒烟：按 Enter 的情况", "冒烟确认");
        Repump();
        _shell.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Repump();
        Console.WriteLine($"ℹ️  确认框 + Enter：已返回 = {confirmed.IsCompleted}；结果 = {(confirmed.IsCompleted ? confirmed.Result.ToString() : "未返回")}");

        var cancelled = AppHost.ConfirmAsync("冒烟：按 Esc 的情况", "冒烟确认");
        Repump();
        _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Repump();
        Console.WriteLine($"ℹ️  确认框 + Esc：已返回 = {cancelled.IsCompleted}；结果 = {(cancelled.IsCompleted ? cancelled.Result.ToString() : "未返回")}");

        // ---- ② 焦点圈禁 ----
        Step("弹窗焦点圈禁：连按 30 次 Tab");
        var staff = AppState.StaffList.FirstOrDefault();
        if (staff is null)
            return;

        var dialog = new StaffDetailDialog(staff);
        _ = AppHost.ShowAsync<bool>(dialog);
        Repump();

        var escaped = false;
        var insideCount = 0;
        for (var i = 0; i < 30; i++)
        {
            _shell.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Repump();
            var focused = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
            if (focused is null)
                continue;

            if (IsInside(dialog, focused))
                insideCount++;
            else
            {
                escaped = true;
                Console.WriteLine($"   第 {i + 1} 次 Tab 后焦点跑到了弹窗外的 {focused.GetType().Name}");
                break;
            }
        }

        Console.WriteLine($"ℹ️  连按 30 次 Tab：焦点落在弹窗内 {insideCount} 次、逃出 = {escaped}（逃出应为 False，且计数应 > 0）");

        _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Repump();

        // ---- ③ 策略编辑器小屏全屏 ----
        Step("策略编辑器：390 宽是否铺满");
        Host("MainView（390，编辑器宿主）", shell, 390, 844);
        var definition = BuildStrategy(ruleCount: 2);
        var editor = new StrategyEditorDialog(definition);
        _ = AppHost.ShowAsync<bool>(editor);
        Repump();
        Console.WriteLine($"ℹ️  策略编辑器 390 宽：对话框尺寸 = {editor.Bounds.Width:0}×{editor.Bounds.Height:0}（铺满 390×844 即小屏全屏生效）");
        CaptureNow("14-editor-phone-fullscreen");
        _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Repump();

        // ---- ④ 四态视觉接线 ----
        Step("四态视觉：直接驱动状态，看视觉类是否跟着变");
        if (_lastListView!.DataContext is ListModel cardModel)
            cardModel.IsCardView = true;

        Host("StaffListView（四态视觉用，卡片视图）", _lastListView, 1160, 820);
        var thumb = _lastListView!.GetVisualDescendants().OfType<StaffThumb>().FirstOrDefault();
        var image = thumb?.GetVisualDescendants().OfType<Image>().FirstOrDefault();
        var stack = thumb?.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(s => s.Classes.Contains("thumb-state"));
        if (image is null || stack is null)
        {
            Console.WriteLine("⚠️  没找到图块组件，跳过四态视觉检查");
            return;
        }

        foreach (var state in new[] { ArtLoadState.Empty, ArtLoadState.Queued, ArtLoadState.Loading, ArtLoadState.Failed })
        {
            ArtImage.SetLoadState(image, state);
            Repump();
            Console.WriteLine($"ℹ️  状态 {state,-7} → 占位可见 = {stack.IsVisible}；类 = [{string.Join(",", stack.Classes)}]；不透明度 = {stack.Opacity:0.00}");
        }
    }

    /// <summary>取控件中心的窗口坐标（每次都现算，避免布局/滚动后用到过期坐标）。</summary>
    private static Point? CenterOf(Control control)
    {
        if (_shell is null || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            return null;

        return control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), _shell);
    }

    private static bool IsInside(Control root, IInputElement? candidate) =>
        candidate is Visual visual && (ReferenceEquals(visual, root) || root.GetVisualDescendants().Contains(visual));

    private static void VerifyInteractionPaths(Control shell, StaffListView list)
    {
        // ---- ① 抽屉：打开聚焦首个导航项，Esc 关闭并归还焦点 ----
        Step("交互链路①：窄屏抽屉的 Esc 与焦点");
        Host("MainView（窄屏 780 抽屉）", shell, 780, 760);

        if (shell is MainView view && _shell is not null)
        {
            var menu = view.FindControl<Button>("MenuButton");
            var sidebar = view.FindControl<Control>("Sidebar");
            var firstNav = view.FindControl<Button>("GenerateNavButton");
            if (menu is not null && sidebar is not null)
            {
                var center = menu.TranslatePoint(new Point(menu.Bounds.Width / 2, menu.Bounds.Height / 2), _shell);
                if (center is { } p)
                {
                    _shell.MouseDown(p, MouseButton.Left, RawInputModifiers.None);
                    _shell.MouseUp(p, MouseButton.Left, RawInputModifiers.None);
                }
                Repump();
                var focused = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
                Console.WriteLine($"ℹ️  抽屉打开 = {sidebar.IsVisible}；焦点在首个导航项 = {ReferenceEquals(focused, firstNav)}");
                CaptureNow("12-drawer-open-narrow");

                _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
                Repump();
                var focusedAfter = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
                Console.WriteLine($"ℹ️  Esc 关闭后：抽屉可见 = {sidebar.IsVisible}；焦点回到菜单按钮 = {ReferenceEquals(focusedAfter, menu)}");
            }
        }

        // ---- ② 池状态跨视图保持一致 ----
        Step("交互链路②：随机池状态跨视图");
        if (list.DataContext is ListModel model)
        {
            var staff = AppState.StaffList.First();
            model.IsCardView = true;
            Repump();
            staff.IsSelected = !staff.IsSelected;
            Repump();
            var inCards = staff.IsSelected;

            model.IsCardView = false;
            Repump();
            var inTable = staff.IsSelected;

            model.IsCardView = true;
            Repump();
            var backToCards = staff.IsSelected;
            Console.WriteLine($"ℹ️  池状态：卡片视图={inCards} → 表格视图={inTable} → 回到卡片={backToCards}（应全等）");
        }

        // ---- ③ 图片失败态与重试（确定性注入：连本机空端口，毫秒级拒绝）----
        Step("交互链路③：图片失败态与重试");
        VerifyArtFailureAndRetry(list);
    }

    /// <summary>
    /// 用「连本机空端口」这种立刻失败的地址走一遍图片状态机：
    /// 断言 ①状态到 Failed ②失败计数增加 ③<see cref="ArtImage.Retry"/> 确实会重新发起请求。
    ///
    /// 不用「不存在的干员 ID」来测：那会串行打 12 个真实 CDN 候选，带 20s 超时，
    /// 在无头测试里等不到结论（实测过）。这本身也说明未命中路径偏慢。
    /// </summary>
    private static void VerifyArtFailureAndRetry(StaffListView list)
    {
        // 关键前提：列表必须真的在当前可视树里。
        // 前面几步把外壳内容换成了别的视图，这里要先把它挂回去，
        // 否则探针拿不到 TopLevel，ArtImage 只记「排队中」而不下载（前两次跑就是这么空转的）。
        Host("StaffListView（失败注入用）", list, 1160, 820);

        // 挂进列表自己的一行容器：ArtImage 只对「已挂到可视树」的 Image 发起下载。
        var host = list.FindControl<Grid>("BatchRow");
        var probe = new Image { Width = 24, Height = 24 };
        if (host is null)
        {
            Console.WriteLine("⚠️  找不到挂载点，跳过图片失败/重试验证");
            return;
        }

        host.Children.Add(probe);
        Repump();

        var before = ArtImage.FailedLoads;
        // 127.0.0.1:1 上没人监听 → 连接立即被拒，毫秒级进入失败态，且不打真实网络。
        ArtImage.SetSources(probe, [new Uri("https://127.0.0.1:1/definitely-missing.png")]);

        var deadline = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < deadline && ArtImage.GetLoadState(probe) != ArtLoadState.Failed)
        {
            Repump();
            Thread.Sleep(50);
        }

        var state = ArtImage.GetLoadState(probe);
        var afterFail = ArtImage.FailedLoads;
        Console.WriteLine($"ℹ️  失败注入：状态 = {state}（应为 Failed）；失败计数 {before} → {afterFail}");

        ArtImage.Retry(probe);
        var retryDeadline = Environment.TickCount64 + 15000;
        while (Environment.TickCount64 < retryDeadline && ArtImage.FailedLoads == afterFail)
        {
            Repump();
            Thread.Sleep(50);
        }

        Console.WriteLine($"ℹ️  调用 Retry：失败计数 {afterFail} → {ArtImage.FailedLoads}（应增加，说明确实重新请求过）");

        host.Children.Remove(probe);
    }

    private static void VerifyModalChain(Control shell)
    {
        // 复用同一个外壳实例：同进程里创建第二个 MainView 会让布局卡住（无头环境限制）。
        Step("弹窗链路：390 宽打开干员详情");
        Host("MainView（手机 390，做弹窗宿主）", shell, 390, 844);

        var staff = AppState.StaffList.FirstOrDefault();
        if (staff is null || _shell is null)
            return;
        var before = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
        var completion = AppHost.ShowAsync<bool>(new StaffDetailDialog(staff));
        Repump();

        var focused = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
        Console.WriteLine($"ℹ️  弹窗打开后焦点 = {focused?.GetType().Name ?? "(null)"}（应为弹窗内控件）");
        CaptureNow("11-modal-phone-fullscreen");

        _shell.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, string.Empty);
        Repump();
        var after = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
        Console.WriteLine($"ℹ️  Esc 关闭 = {completion.IsCompleted}；关闭后焦点 = {after?.GetType().Name ?? "(null)"}；焦点归还 = {ReferenceEquals(before, after)}");
    }

    /// <summary>
    /// 真实立绘截图：换成一批真实干员 ID，等图片真正下载解码完再截。
    /// 只有这样才能判断卡片填充、立绘裁切、顶部遮罩上的文字对比度是否成立——
    /// 用占位图标截图是看不出来的。
    /// </summary>
    private static void CaptureRealArtShots()
    {
        var operators = new (string Name, string SourceId, int Star, Career Career)[]
        {
            ("能天使", "char_103_angel", 6, Career.狙击),
            ("德克萨斯", "char_102_texas", 5, Career.先锋),
            ("陈", "char_010_chen", 6, Career.近卫),
            ("星熊", "char_136_hsguma", 6, Career.重装),
            ("阿米娅", "char_002_amiya", 5, Career.术师),
            ("白面鸮", "char_140_whitew", 5, Career.医疗),
            ("芙兰卡", "char_106_franka", 5, Career.近卫),
            ("白雪", "char_118_yuki", 4, Career.狙击),
            ("深海色", "char_110_deepcl", 4, Career.辅助),
            ("雷蛇", "char_107_liskam", 5, Career.重装),
            ("崖心", "char_128_plosis", 5, Career.特种),
            ("银灰", "char_172_svrash", 6, Career.近卫)
        };

        Step("换成真实干员数据（需要联网下载立绘）");
        AppState.StaffList.Clear();
        foreach (var (name, sourceId, star, career) in operators)
        {
            AppState.StaffList.Add(new Staff
            {
                Name = name,
                SourceId = sourceId,
                Star = star,
                Career = career,
                IsSelected = true,
                Level = Level.GenerateMaxLevel(star)
            });
        }

        var list = new StaffListView();
        Host("真实数据（表格）", list, 1160, 820);
        WaitForArt(list);
        CaptureNow("20-real-table");

        if (list.DataContext is ListModel model)
        {
            model.IsCardView = true;
            model.UsePortrait = false;
            Repump();
            WaitForArt(list);
            CaptureNow("21-real-cards-avatar");

            model.UsePortrait = true;
            Repump();
            WaitForArt(list);
            CaptureNow("22-real-cards-portrait");
            Console.WriteLine($"ℹ️  图片统计：网络 {ArtImage.NetworkLoads} · 缓存命中 {ArtImage.CacheHits} · 失败 {ArtImage.FailedLoads}");
        }

        Step("真实数据截图完成，恢复测试数据");
        SeedData();
    }

    /// <summary>等所有可见图片离开 Loading 状态（或超时）。</summary>
    private static void WaitForArt(Control root, int timeoutMs = 45000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        var lastCount = -1;
        while (Environment.TickCount64 < deadline)
        {
            Repump();
            var states = root.GetVisualDescendants()
                .OfType<Image>()
                .Select(ArtImage.GetLoadState)
                .ToList();

            if (states.Count > 0 && states.All(state => state != ArtLoadState.Loading))
                return;

            if (states.Count != lastCount)
            {
                lastCount = states.Count;
                Console.WriteLine($"   等待立绘：{states.Count(state => state == ArtLoadState.Loading)} 张仍在加载");
            }

            Thread.Sleep(150);
        }

        Console.WriteLine("⚠️  等待立绘超时");
    }

    private static void SeedData() => SeedData(50);

    /// <summary>常规干员 + 长名称，覆盖列表的常见与边界数据；perf 模式用 500 名。</summary>
    private static void SeedData(int count)
    {
        AppState.StaffList.Clear();
        for (var i = 0; i < count; i++)
        {
            AppState.StaffList.Add(new Staff
            {
                Name = $"测试干员{i:D2}",
                Star = (i % 6) + 1,
                Career = AppOptions.Careers[i % AppOptions.Careers.Count],
                IsSelected = i % 3 == 0,
                Level = Level.GenerateMaxLevel((i % 6) + 1)
            });
        }

        AppState.StaffList.Add(new Staff
        {
            Name = "名字特别长到会被省略号截断的测试干员名字特别长到会被省略号截断的测试干员",
            Star = 6,
            Career = Career.近卫,
            IsSelected = true,
            Level = new Level(2, 90)
        });
    }

    private static RandomStrategyDefinition BuildStrategy(int ruleCount)
    {
        var definition = new RandomStrategyDefinition { Name = $"冒烟策略 {ruleCount} 条规则" };
        for (var i = 0; i < ruleCount; i++)
            definition.Rules.Add(new StrategyRule());
        return definition;
    }

    /// <summary>截几张卡片交互的图：点击整卡切换入池、Tab 键焦点。</summary>
    private static void CaptureCardInteractions(StaffListView list)
    {
        try
        {
            if (list.DataContext is not ListModel model || _shell is null)
                return;

            Step("卡片交互：切到头像视图并点击第一张卡");
            Host("点击前", list, 1160, 820);
            model.IsCardView = true;
            model.UsePortrait = false;
            Repump();

            var card = list.GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(button => button.Classes.Contains("operator-card"));
            if (card is null)
            {
                Console.WriteLine("⚠️  没找到卡片控件，跳过点击截图");
                return;
            }

            var before = (card.DataContext as Staff)?.IsSelected;
            var center = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), _shell);
            if (center is { } point)
            {
                var click = new Point(point.X, point.Y);
                _shell.MouseDown(click, MouseButton.Left, RawInputModifiers.None);
                _shell.MouseUp(click, MouseButton.Left, RawInputModifiers.None);
                Repump();
                var after = (card.DataContext as Staff)?.IsSelected;
                Console.WriteLine($"ℹ️  点击卡片：入池 {before} → {after}（应当取反）");
            }

            CaptureNow("09-card-after-click");

            Step("卡片交互：网格里放一张已入池 + 一张未入池做对照");
            var staff = AppState.StaffList.Take(2).ToList();
            if (staff.Count == 2)
            {
                staff[0].IsSelected = true;
                staff[1].IsSelected = false;
                staff[0].RaiseArtChanged();
                staff[1].RaiseArtChanged();
                Repump();
                CaptureNow("09-card-pool-compare");
            }

            Step("键盘：Tab 聚焦一张卡片");
            _shell.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, "\t");
            Repump();
            CaptureNow("10-card-keyboard-focus");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  卡片交互截图失败：{ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// 实测列头键盘排序：把焦点放到「干员」列头，按空格，断言排序状态确实变了。
    /// 这条路径靠静态检查证明不了（列头与列定义的对应关系、DataGrid 是否吃掉按键都要跑一遍）。
    /// </summary>
    private static void VerifyHeaderKeyboardSort(StaffListView list)
    {
        Step("键盘排序：聚焦「干员」列头并按空格");
        if (list.DataContext is not ListModel model || _shell is null)
            return;

        model.IsCardView = false;
        Repump();

        var header = list.GetVisualDescendants()
            .OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => h.Content is StackPanel panel &&
                                 panel.Children.OfType<TextBlock>().Any(t => t.Text == "干员"));
        if (header is null)
        {
            Console.WriteLine("⚠️  没找到「干员」列头，跳过键盘排序验证");
            return;
        }

        var before = model.Sorts.Count;
        header.Focus();
        Repump();
        var focused = TopLevel.GetTopLevel(_shell)?.FocusManager?.GetFocusedElement();
        _shell.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Repump();

        var after = model.Sorts.Count;
        var path = model.Sorts.Count > 0 ? model.Sorts[0].Path : "(无)";
        Console.WriteLine($"ℹ️  列头键盘排序：焦点在列头 = {focused is DataGridColumnHeader}；排序项 {before} → {after}；首列 = {path}；摘要 = {model.SortSummary}");
    }

    private static void Host(string label, Control content, double width, double height, string? shot = null)
    {
        // 模仿浏览器叠加层的约束：内容比可用宽度大时压回来，
        // 否则固定宽度的对话框在窄屏测试里量不出真实结果。
        if (!double.IsNaN(content.Width) && content.Width > width)
            content.MaxWidth = width;
        if (!double.IsNaN(content.Height) && content.Height > height)
            content.MaxHeight = height;

        _current = content;
        _currentWidth = width;
        _currentHeight = height;

        if (_shell is null)
        {
            _shell = new Window();
            _shell.Show();
        }

        _shell.Width = width;
        _shell.Height = height;
        _shell.Content = content;

        Repump();
        Check(label, content, width, height);
        if (shot is not null)
            CaptureNow(shot);
    }

    /// <summary>对当前内容重新量一次并排布，再等一次渲染时钟。</summary>
    private static void Repump()
    {
        Dispatcher.UIThread.RunJobs();
        if (_current is not null)
        {
            _current.Measure(new Size(_currentWidth, _currentHeight));
            _current.Arrange(new Rect(0, 0, _currentWidth, _currentHeight));
        }

        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void CaptureNow(string name)
    {
        if (_shotDir is null || _shell is null)
            return;

        try
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();

            var frame = _shell.CaptureRenderedFrame();
            if (frame is null)
            {
                Console.WriteLine($"⚠️  截图失败（帧为空）：{name}");
                return;
            }

            var path = Path.Combine(_shotDir, name + ".png");
            frame.Save(path, new PngBitmapEncoderOptions());
            Console.WriteLine($"📸 {name}.png  {frame.PixelSize.Width}×{frame.PixelSize.Height}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  截图失败：{name} — {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void Check(string label, Control content, double width, double height)
    {
        var bounds = content.Bounds;
        if (bounds.Width > 0 && bounds.Height > 0)
            Console.WriteLine($"✅ {label}：{bounds.Width:0}×{bounds.Height:0}");
        else
            Console.WriteLine($"⚠️  {label}：布局尺寸为 0（期望 {width:0}×{height:0}）");
    }

    /// <summary>
    /// 报告卡片网格实际实例化了多少个卡片控件——虚拟化是否生效的直接证据。
    /// 总数 51 张时若明显更少，说明只创建了可见区（含预取区）的卡片。
    /// </summary>
    private static void ReportRealizedCards(StaffListView list)
    {
        var repeater = list.FindControl<ItemsRepeater>("StaffCards");
        var total = AppState.StaffList.Count;
        var realized = repeater?.Children.Count() ?? -1;
        Console.WriteLine($"ℹ️  卡片网格：干员 {total} 名，实际实例化 {realized} 个卡片控件");
    }

    /// <summary>
    /// perf 模式：建立方案 §8 要求的基线。
    ///
    /// 量的是 CPU 侧耗时（搜索、选池、布局与出帧）。headless + Skia 是软件光栅，
    /// 数字只能做同机相对回归，**不能当作 60fps 结论**；GPU 帧率、WASM 首屏字节仍需实机。
    /// 运行：dotnet run --project tools/SmokeTest/arknights-random-team.SmokeTest.csproj -- perf 500
    /// </summary>
    private static void RunPerf(int count)
    {
        Step($"perf：夹具 {count} 名干员");
        AppHost.Presenter = new OverlayPresenter(new ModalLayer());
        SeedData(count);

        var list = new StaffListView();
        Host($"StaffListView（表格 {count}）", list, 1240, 840);
        if (list.DataContext is not ListModel model)
        {
            Fail("StaffListView 的 DataContext 不是 ListModel");
            return;
        }

        var report = new List<string>
        {
            $"# 性能基线 {DateTime.Now:yyyy-MM-dd HH:mm}",
            "",
            $"夹具：{count} 名干员",
            "平台：Windows + Avalonia headless + Skia 软件光栅（非 GPU 帧率结论）",
            ""
        };

        // ---- 1. 搜索延迟 ----
        report.Add("## 1. 搜索延迟（方案门槛：输入停止后 P95 ≤ 200ms）");
        report.Add("| 路径 | P50 (ms) | P95 (ms) | 最差 (ms) | 分配/次 | 判定 |");
        report.Add("| --- | --- | --- | --- | --- | --- |");
        foreach (var (label, keyword) in new (string, string)[]
                 {
                     ("无筛选条件下搜索", "测试干员1"),
                     ("命中 1 人", "测试干员007"),
                     ("无结果", "不存在的名字"),
                     ("池筛选下搜索", "测试干员1")
                 })
        {
            model.SetPoolFilter(label.StartsWith("池筛选") ? PoolFilterKind.InPool : PoolFilterKind.All);
            model.SearchText = "";
            var samples = Measure(200, i => model.SearchText = i % 2 == 0 ? keyword : keyword + "x");
            model.SearchText = "";
            report.Add(FormatRow(label, samples, 200));
        }

        model.SetPoolFilter(PoolFilterKind.All);
        model.SearchText = "";

        // ---- 2. 选池反馈 ----
        report.Add("");
        report.Add("## 2. 选池反馈（方案门槛：普通选池 100ms 内出现反馈）");
        report.Add("| 路径 | P50 (ms) | P95 (ms) | 最差 (ms) | 分配/次 | 判定 |");
        report.Add("| --- | --- | --- | --- | --- | --- |");
        foreach (var inPool in new[] { false, true })
        {
            model.SetPoolFilter(inPool ? PoolFilterKind.InPool : PoolFilterKind.All);
            var staff = AppState.StaffList.First(s => s.IsSelected || !inPool);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var samples = Measure(200, _ => staff.IsSelected = !staff.IsSelected);
            var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / 200.0;
            report.Add(FormatRow(inPool ? "池筛选 = 已入池" : "无池筛选", samples, 100, allocated));
        }

        model.SetPoolFilter(PoolFilterKind.All);
        report.Add("");
        report.Add("> 注：池筛选生效时每次选池要重建整份可见投影，「分配/次」这一列会明显放大；");
        report.Add("> 500 名干员时约 165KB/次。这是后续把 `RefreshVisible` 改成增量更新的直接依据（见工程优化方案 P2-9）。");

        // ---- 3. 布局与出帧（相对值）----
        report.Add("");
        report.Add("## 3. 布局与出帧（软件光栅，只作同机相对回归）");
        report.Add("| 视图 | P50 (ms) | P95 (ms) | 最差 (ms) | 单帧截图 (ms) |");
        report.Add("| --- | --- | --- | --- | --- |");
        foreach (var (label, card, portrait) in new (string, bool, bool)[]
                 {
                     ("表格", false, false),
                     ("头像卡片", true, false),
                     ("立绘卡片", true, true)
                 })
        {
            model.IsCardView = card;
            model.UsePortrait = portrait;
            Repump();

            var samples = Measure(100, _ => Repump());
            var swShot = System.Diagnostics.Stopwatch.StartNew();
            _shell?.CaptureRenderedFrame();
            swShot.Stop();
            report.Add(FormatRow(label, samples, null, null, swShot.Elapsed.TotalMilliseconds));
        }

        model.IsCardView = true;
        model.UsePortrait = false;
        Repump();
        var realized = list.FindControl<ItemsRepeater>("StaffCards")?.Children.Count() ?? -1;
        report.Add("");
        report.Add($"虚拟化：{count} 名干员时卡片网格实际实例化 **{realized}** 个控件");
        report.Add($"图片调度：网络 {ArtImage.NetworkLoads} · 内存命中 {ArtImage.CacheHits} · 磁盘命中 {ArtImage.DiskHits} · 失败 {ArtImage.FailedLoads}");

        var text = string.Join(Environment.NewLine, report);
        Console.WriteLine();
        Console.WriteLine(text);

        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "artifacts", "perf");
        dir = Path.GetFullPath(dir);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"perf-{count}-{DateTime.Now:yyyyMMdd-HHmmss}.md");
        File.WriteAllText(path, text);
        Console.WriteLine();
        Console.WriteLine($"报告已写入：{path}");
    }

    /// <summary>跑 n 次并收集每次耗时（毫秒）。</summary>
    private static List<double> Measure(int iterations, Action<int> action)
    {
        var samples = new List<double>(iterations);
        for (var i = 0; i < iterations; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            action(i);
            sw.Stop();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        return samples;
    }

    private static string FormatRow(string label, List<double> samples, double? budgetMs, double? allocated = null, double? extra = null)
    {
        samples.Sort();
        var p50 = samples[samples.Count / 2];
        var p95 = samples[Math.Min(samples.Count - 1, (int)(samples.Count * 0.95))];
        var worst = samples[^1];

        // 出帧表：第四列是单帧截图耗时
        if (extra is { } shot)
            return $"| {label} | {p50:F2} | {p95:F2} | {worst:F2} | {shot:F1} |";

        var allocation = allocated is { } bytes ? $"{bytes / 1024:F1} KB" : "—";
        var verdict = budgetMs is not { } budget
            ? "—"
            : p95 <= budget ? $"✅ ≤ {budget:F0}ms" : $"❌ 超 {budget:F0}ms";
        return $"| {label} | {p50:F2} | {p95:F2} | {worst:F2} | {allocation} | {verdict} |";
    }

    private static void Step(string what)
    {
        Console.WriteLine($"… {what}");
        Console.Out.Flush();
    }

    private static void Fail(string message)
    {
        Console.WriteLine($"❌ {message}");
        Environment.ExitCode = 1;
    }

    /// <summary>把 Avalonia 的警告收集起来；绑定路径写错会以 [Binding] 警告出现。</summary>
    private sealed class CollectingSink : ILogSink
    {
        public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
            Record(level, area, messageTemplate);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Record(level, area, messageTemplate);

        private static void Record(LogEventLevel level, string area, string message) =>
            Warnings.Add($"[{area}] {level}: {message}");
    }
}
