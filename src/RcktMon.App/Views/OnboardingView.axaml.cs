using Avalonia.Controls;
using Avalonia.Interactivity;
using RcktMon.App.ViewModels;

namespace RcktMon.App.Views;

public partial class OnboardingView : UserControl
{
    public OnboardingView() => InitializeComponent();

    /// <summary>
    /// Records which preset radio button is selected.
    /// </summary>
    /// <remarks>
    /// Radio buttons inside an <c>ItemsControl</c> have no selection model to bind to, so
    /// the checked item is pushed to the view model rather than pulled from a collection.
    /// </remarks>
    private void OnPresetChecked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, DataContext: AlertProfileViewModel preset } &&
            DataContext is OnboardingViewModel model)
        {
            model.SelectedPreset = preset;
        }
    }
}
