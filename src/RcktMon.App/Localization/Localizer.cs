using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Resources;

namespace RcktMon.App.Localization;

/// <summary>
/// One translated string, as a property a binding can watch.
/// </summary>
/// <remarks>
/// XAML binds to <see cref="Value"/> rather than to an observable. An observable-backed
/// binding carries a single subscription, so two controls created from the same markup —
/// which happens the moment a page is navigated away from and back, because the template
/// is re-applied — cannot both follow it. A plain property with change notification has no
/// such limit.
/// </remarks>
public sealed class LocalizedText : INotifyPropertyChanged
{
    private string _value = string.Empty;

    internal LocalizedText(string key) => Key = key;

    /// <summary>Resource key this text came from.</summary>
    public string Key { get; }

    /// <summary>The translation, in the current interface language.</summary>
    public string Value
    {
        get => _value;
        internal set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Value;
}

/// <summary>One selectable interface language.</summary>
/// <param name="Code">BCP-47 tag, or empty for "follow the system".</param>
/// <param name="Name">Name of the language, written in that language.</param>
public readonly record struct LanguageOption(string Code, string Name);

/// <summary>
/// The single source of translated text, and the thing bindings watch for a language
/// change.
/// </summary>
/// <remarks>
/// <para>
/// XAML binds rather than reads: <c>{l:Localize Nav_Monitor}</c> resolves to the shared
/// <see cref="LocalizedText"/> for that key, so switching the language updates every bound
/// string in the window at once. Restarting the app to change language is a poor
/// experience for a setting that lives two clicks away.
/// </para>
/// <para>
/// C# reads the same resources through the generated <c>Strings</c> class, which resolves
/// against <see cref="CultureInfo.CurrentUICulture"/> — kept in step by
/// <see cref="Use"/>.
/// </para>
/// </remarks>
public sealed class Localizer
{
    private readonly BehaviorSubject<CultureInfo> _culture = new(CultureInfo.CurrentUICulture);

    // One instance per key, shared by every control bound to it.
    private readonly ConcurrentDictionary<string, LocalizedText> _texts = new(StringComparer.Ordinal);

    /// <summary>Languages the app ships. Each is named in its own language, never translated.</summary>
    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(string.Empty, "System"),
        new("en", "English"),
        new("ru", "Русский"),
    ];

    public static Localizer Instance { get; } = new();

    /// <summary>
    /// The underlying resource set.
    /// </summary>
    /// <remarks>
    /// Exposed because the generated <c>Strings</c> class is internal, and the parity
    /// tests need to enumerate both languages from outside the assembly.
    /// </remarks>
    public static ResourceManager Resources => Strings.ResourceManager;

    /// <summary>The culture text is currently resolved against.</summary>
    public CultureInfo Culture => _culture.Value;

    /// <summary>Translated text for a key, or the key itself when it is missing.</summary>
    /// <remarks>
    /// Returning the key rather than throwing keeps a missing translation to a visible
    /// blemish instead of a crash, and makes the gap obvious in a screenshot.
    /// </remarks>
    public string this[string key] =>
        Resources.GetString(key, Culture) ?? key;

    /// <summary>The text for a key, now and after every language change.</summary>
    /// <remarks>For C#. XAML binds to <see cref="Text"/> instead; see its remarks.</remarks>
    public IObservable<string> Observe(string key) =>
        _culture.Select(culture => Resources.GetString(key, culture) ?? key);

    /// <summary>The bindable translation for a key.</summary>
    public LocalizedText Text(string key) =>
        _texts.GetOrAdd(key, k => new LocalizedText(k) { Value = this[k] });

    /// <summary>
    /// Switches the interface language for the whole process.
    /// </summary>
    /// <param name="code">BCP-47 tag, or empty to follow the operating system.</param>
    /// <remarks>
    /// Only <see cref="CultureInfo.CurrentUICulture"/> is forced. The formatting culture
    /// is left alone deliberately: someone reading an English interface in Moscow still
    /// wants their own number and date conventions, and money formatting is not a
    /// translation decision.
    /// </remarks>
    public void Use(string? code)
    {
        CultureInfo culture = Resolve(code);

        if (Equals(culture, Culture))
        {
            return;
        }

        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;

        // Push to the C# subscribers, then update every bindable string in place.
        _culture.OnNext(culture);

        foreach (LocalizedText text in _texts.Values)
        {
            text.Value = Resources.GetString(text.Key, culture) ?? text.Key;
        }
    }

    /// <summary>
    /// Resolves a stored preference to a culture the resources actually cover.
    /// </summary>
    /// <remarks>
    /// Anything that is not Russian falls back to English, which is the neutral resource
    /// set. A Russian-speaking user gets Russian without configuring anything; everyone
    /// else gets English rather than a half-translated screen.
    /// </remarks>
    private static CultureInfo Resolve(string? code)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            try
            {
                return CultureInfo.GetCultureInfo(code);
            }
            catch (CultureNotFoundException)
            {
                // Fall through to the system culture below.
            }
        }

        return CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru"
            ? CultureInfo.GetCultureInfo("ru")
            : CultureInfo.GetCultureInfo("en");
    }
}
