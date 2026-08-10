using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace RcktMon.App.Localization;

/// <summary>
/// Resolves a resource key in XAML: <c>Text="{l:Localize Nav_Monitor}"</c>.
/// </summary>
/// <remarks>
/// Returns a binding rather than a string so the text follows a language change while the
/// window is open. The alternative — resolving once at load — means every translated
/// string in the app is stale until restart, which is exactly the kind of thing that makes
/// a language setting feel broken.
///
/// The binding source is a shared per-key object rather than an observable, so that two
/// controls built from the same markup — which happens as soon as a page is navigated away
/// from and back — both follow the language.
/// </remarks>
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key) => Key = key;

    /// <summary>Resource key, as it appears in <c>Strings.resx</c>.</summary>
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocalizedText.Value))
        {
            Source = Localizer.Instance.Text(Key),
            Mode = BindingMode.OneWay,
        };
}
