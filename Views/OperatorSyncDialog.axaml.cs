using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using arknights_random_team.Models;

namespace arknights_random_team.Views;

public partial class OperatorSyncDialog : ModalContent
{
    public OperatorSyncDialog()
    {
        var selected = AppState.OperatorSyncSettings.SelectedStars ?? [];
        Stars = AppOptions.Stars
            .Select(star => new SyncStarOption(star, selected.Contains(star)))
            .ToList();

        InitializeComponent();
        StarItems.ItemsSource = Stars;
        UpdateCount();
        // 轨道宽度要等首次布局完成后才有效，加载后补一次进度条宽度。
        Loaded += (_, _) =>
        {
            UpdateCount();
            ApplyNarrowLayout();
        };

        // 窄屏（320–390 档）页脚放不下「说明 + 两个按钮」：说明文字 + 92 + 172 的最小宽
        // 加上左右各 28 的内边距已经超过 320 视口的可用宽度（实测确认）。
        // 这里在窄屏隐藏说明、放开按钮最小宽并收窄内边距，保证取消与主操作都可见。
        SizeChanged += (_, _) =>
        {
            ApplyNarrowLayout();
            Dispatcher.UIThread.Post(UpdateCount, DispatcherPriority.Loaded);
        };
    }

    /// <summary>窄屏页脚适配：由控件自身宽度决定，不看外壳宽度（弹窗可能装在窄宿主上）。</summary>
    private void ApplyNarrowLayout()
    {
        if (FooterBar == null)
            return;

        var narrow = Bounds.Width > 0 && Bounds.Width < 400;
        FooterHint.IsVisible = !narrow;
        CancelButton.MinWidth = narrow ? 0 : 92;
        SyncButton.MinWidth = narrow ? 0 : 172;
        FooterBar.Padding = narrow ? new Avalonia.Thickness(12, 0) : new Avalonia.Thickness(28, 0);
    }

    public OperatorSyncSelection? Selection { get; private set; }

    public IReadOnlyList<SyncStarOption> Stars { get; }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void StarCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        ValidationText.IsVisible = false;
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (SyncCountText == null)
            return;

        var count = Stars.Count(star => star.IsChecked);
        SyncCountText.Text = $"已选 {count} / {Stars.Count}";
        // 进度条按「已选档数 / 总档数」填充轨道宽度（轨道宽度要首次布局后才有效）。
        var trackWidth = SelectionTrack.Bounds.Width;
        if (trackWidth <= 0)
            return;

        SelectionMeter.Width = trackWidth * count / Stars.Count;
    }

    private void SetAll(bool isChecked, bool clearValidation)
    {
        foreach (var star in Stars)
            star.IsChecked = isChecked;

        if (clearValidation)
            ValidationText.IsVisible = false;
        UpdateCount();
    }

    private void SelectAll_Click(object? sender, RoutedEventArgs e) => SetAll(true, true);

    private void ClearAll_Click(object? sender, RoutedEventArgs e) => SetAll(false, false);

    private void Sync_Click(object? sender, RoutedEventArgs e)
    {
        var selectedStars = Stars.Where(star => star.IsChecked).Select(star => star.Star).ToHashSet();
        if (selectedStars.Count == 0)
        {
            ValidationText.IsVisible = true;
            return;
        }

        Selection = new OperatorSyncSelection(selectedStars);
        RequestClose(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => RequestClose(false);
}

/// <summary>稀有度卡片状态；选中状态直接绑定，不依赖虚拟化后的容器。</summary>
public sealed class SyncStarOption(int star, bool isChecked) : INotifyPropertyChanged
{
    private bool _isChecked = isChecked;

    public int Star { get; } = star;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
                return;

            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record OperatorSyncSelection(IReadOnlySet<int> SelectedStars);
