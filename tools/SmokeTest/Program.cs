using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Controls.Primitives;
using Avalonia.Logging;
using Avalonia.Threading;
using arknights_random_team;
using arknights_random_team.Domain;
using arknights_random_team.Models;
using arknights_random_team.Views;

namespace arknights_random_team.SmokeTest;

/// <summary>
/// 无头冒烟测试：在没有显示器的环境里把各个界面真正实例化并跑一遍布局，
/// 用来抓「编译通过、一打开就崩」的问题——缺资源、绑定路径写错、虚拟化容器用法不对等。
///
/// 它不替代实机验收：这里看不到画面，也不测帧率、裁切和触摸手感。
/// 运行：dotnet run --project tools/SmokeTest/arknights-random-team.SmokeTest.csproj
/// </summary>
internal static class Program
{
    private static readonly List<string> Warnings = [];

    private static async Task<int> Main()
    {
        Logger.Sink = new CollectingSink();

        using var session = HeadlessUnitTestSession.StartNew(typeof(App));
        await session.Dispatch(RunChecks, CancellationToken.None);

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
        Step("设置对话框宿主");
        AppHost.Presenter = new OverlayPresenter(new ModalLayer());

        Step("填充测试数据");
        SeedData();

        // 1. 外壳：侧栏 + 页头 + 页面宿主。
        // 外壳只建一个实例：同一实例上缩放，正好覆盖 AppLayout 的宽屏/抽屉切换。
        var shell = new MainView();
        Host("MainView（外壳 1160）", shell, 1160, 820);
        Host("MainView（窄屏 780）", shell, 780, 760);
        Host("MainView（回到 1160）", shell, 1160, 820);

        // 2. 干员列表：表格 / 头像 / 立绘 / 紧凑行高 / 虚拟化网格。
        Step("创建 StaffListView");
        var list = new StaffListView();
        Host("StaffListView（表格）", list, 1160, 820);
        if (list.DataContext is ListModel model)
        {
            Step("切换紧凑表格");
            model.IsCompactTable = true;
            PumpCurrent();

            Step("切换头像卡片");
            model.IsCardView = true;
            PumpCurrent();
            Check("StaffListView（头像卡片）", list, 1160, 820);
            ReportRealizedCards(list);

            Step("切换立绘卡片");
            model.UsePortrait = true;
            PumpCurrent();
            Check("StaffListView（立绘卡片）", list, 1160, 820);

            Step("切回表格并跑一次搜索");
            model.IsCompactTable = false;
            model.IsCardView = false;
            PumpCurrent();

            model.SearchText = "测试";
            PumpCurrent();
            model.SearchText = "";
            PumpCurrent();
        }
        else
        {
            Fail("StaffListView 的 DataContext 不是 ListModel");
        }

        // 3. 详情 / 编辑面板（新入口）与共享图块组件。
        Step("创建 StaffDetailDialog");
        var staff = AppState.StaffList.FirstOrDefault();
        if (staff is not null)
            Host("StaffDetailDialog", new StaffDetailDialog(staff), 440, 560);

        // 4. 生成页：结果卡片复用共享图块。
        Step("创建 GenerateView");
        Host("GenerateView", new GenerateView(), 1160, 820);

        // 5. 策略页：规则较多时的按需展开。
        Step("创建 RandomStrategyView");
        AppState.Strategies.Add(BuildStrategy(ruleCount: 5));
        Host("RandomStrategyView", new RandomStrategyView(), 1160, 820);

        // 6. 策略编辑器：宽屏两栏与窄屏单栏两条路径。
        Step("创建策略编辑器（宽屏）");
        var definition = BuildStrategy(ruleCount: 2);
        AppLayout.Update(1160);
        Host("StrategyEditorDialog（宽屏）", new StrategyEditorDialog(definition), 960, 740);
        Step("创建策略编辑器（窄屏）");
        AppLayout.Update(700);
        Host("StrategyEditorDialog（窄屏）", new StrategyEditorDialog(definition), 700, 740);
        AppLayout.Update(1160);

        // 7. 录入页。
        Step("创建 InputView");
        Host("InputView", new InputView(), 1160, 820);
    }

    private static void Step(string what)
    {
        Console.WriteLine($"… {what}");
        Console.Out.Flush();
    }

    /// <summary>50 名常规干员 + 长名称 + 无图源的手动录入干员，覆盖两类边界。</summary>
    private static void SeedData()
    {
        AppState.StaffList.Clear();
        for (var i = 0; i < 50; i++)
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

    /// <summary>
    /// 直接把控件量一遍并排布。无头环境里窗口（TopLevel）的布局依赖渲染时钟，
    /// 换内容或改尺寸后不一定立刻重排；这里要验证的是「界面能不能加载并完成布局」，
    /// 直接 Measure/Arrange 更确定，也更能暴露控件自身的布局问题。
    /// </summary>
    private static readonly Window Shell = new();
    private static Control? _current;
    private static double _currentWidth = 1160;
    private static double _currentHeight = 820;

    private static void Host(string label, Control content, double width, double height)
    {
        // 模仿浏览器叠加层的约束：内容比可用宽度大时按可用宽度压回来，
        // 否则固定 960 宽的对话框在窄屏测试里量不出真实结果。
        if (!double.IsNaN(content.Width) && content.Width > width)
            content.MaxWidth = width;
        if (!double.IsNaN(content.Height) && content.Height > height)
            content.MaxHeight = height;

        _current = content;
        _currentWidth = width;
        _currentHeight = height;

        Shell.Width = width;
        Shell.Height = height;
        Shell.Content = content;
        if (!Shell.IsVisible)
            Shell.Show();

        PumpCurrent();
        Check(label, content, width, height);
    }

    /// <summary>
    /// 报告卡片网格实际实例化了多少个卡片控件。
    /// 这是虚拟化是否生效的直接证据：总数 51 张，若实例化数量明显更少
    /// 说明只创建了可见区（含预取区）的卡片；等于总数则说明没虚拟化。
    /// </summary>
    private static void ReportRealizedCards(StaffListView list)
    {
        var repeater = list.FindControl<ItemsRepeater>("StaffCards");
        var total = AppState.StaffList.Count;
        var realized = repeater?.Children.Count() ?? -1;
        Console.WriteLine($"ℹ️  卡片网格：干员 {total} 名，实际实例化 {realized} 个卡片控件");
    }

    /// <summary>对当前内容重排一次（视图模式切换后调用）。</summary>
    private static void PumpCurrent()
    {
        Dispatcher.UIThread.RunJobs();
        if (_current is null)
            return;

        _current.Measure(new Size(_currentWidth, _currentHeight));
        _current.Arrange(new Rect(0, 0, _currentWidth, _currentHeight));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Check(string label, Control content, double width, double height)
    {
        var bounds = content.Bounds;
        if (bounds.Width > 0 && bounds.Height > 0)
            Console.WriteLine($"✅ {label}：{bounds.Width:0}×{bounds.Height:0}");
        else
            Console.WriteLine($"⚠️  {label}：布局尺寸为 0（期望 {width:0}×{height:0}）");
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
