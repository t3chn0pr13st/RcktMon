using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RcktMon.App.ViewModels;

namespace RcktMon.App.Views;

/// <summary>Native read-only client for the local Signal Studio service. No trading dependency.</summary>
public sealed partial class StudioView : UserControl
{
    private readonly StudioViewModel _vm;
    private bool _attached, _loadedLibrary;
    public StudioView() : this(new StudioViewModel()) { }

    public StudioView(StudioViewModel model)
    {
        _vm = model;
        InitializeComponent();
        DataContext = _vm;
        ApplyLayout(false);
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width < 690);
    }

    private void ApplyLayout(bool compact)
    {
        Classes.Set("compact", compact);
        Resources["StudioLayout.Wide"] = !compact;
        Resources["StudioLayout.Narrow"] = compact;
        Resources["StudioLayout.Columns"] = compact ? 2 : 4;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true; _loadedLibrary = false;
        _vm.Start();
        MaybeLoad();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        _vm.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnMode(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender)) MaybeLoad();
    }

    private void MaybeLoad()
    {
        if (!_attached || _loadedLibrary || ModeTabs?.SelectedIndex != 1) return;
        _loadedLibrary = true;
        _ = _vm.RefreshLibraryAsync();
    }

    private async void OnLibrary(object? sender, RoutedEventArgs e) => await _vm.RefreshLibraryAsync();
    private async void OnCalculate(object? sender, RoutedEventArgs e) => await _vm.CalculateAsync();
}
