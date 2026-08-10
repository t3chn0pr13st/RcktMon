using Avalonia.Controls;
using Avalonia.Controls.Templates;
using CommunityToolkit.Mvvm.ComponentModel;
using RcktMon.App.Localization;

namespace RcktMon.App;

/// <summary>
/// Resolves a view model to its view by name.
/// </summary>
/// <remarks>
/// Ten lines replacing Caliburn.Micro's convention engine. <c>FooViewModel</c> in
/// <c>RcktMon.App.ViewModels</c> renders as <c>FooView</c> in <c>RcktMon.App.Views</c>; a
/// missing view shows up as a red placeholder rather than an empty pane, because a page
/// that silently renders nothing is the hardest kind of wiring bug to spot.
/// </remarks>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? data)
    {
        if (data is null)
        {
            return new TextBlock { Text = Strings.Common_NoData };
        }

        string name = data.GetType().FullName!
            .Replace("ViewModels", "Views", StringComparison.Ordinal)
            .Replace("ViewModel", "View", StringComparison.Ordinal);

        Type? type = Type.GetType(name);

        return type is not null && Activator.CreateInstance(type) is Control control
            ? control
            : new TextBlock
            {
                Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, Strings.Common_MissingView, name),
                Foreground = Avalonia.Media.Brushes.OrangeRed,
                Margin = new Avalonia.Thickness(24),
            };
    }

    public bool Match(object? data) => data is ObservableObject;
}
