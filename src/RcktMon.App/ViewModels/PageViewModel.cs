using CommunityToolkit.Mvvm.ComponentModel;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Market;

namespace RcktMon.App.ViewModels;

/// <summary>
/// One destination in the left rail.
/// </summary>
/// <remarks>
/// Pages are created once at startup and kept alive, so switching back to the monitor
/// restores its scroll position, selection and filters. The shell drives them: it drains
/// the stores on a single timer and hands each page the result, rather than every page
/// owning a timer of its own.
/// </remarks>
public abstract partial class PageViewModel : ObservableObject, IDisposable
{
    private readonly IDisposable _titleSubscription;

    protected PageViewModel(string key, string titleKey, string iconKey)
    {
        Key = key;
        TitleKey = titleKey;
        IconKey = iconKey;

        // Subscribed rather than read once, so the rail relabels itself when the language
        // changes instead of waiting for a restart.
        _titleSubscription = Localizer.Instance.Observe(titleKey).Subscribe(text => Title = text);
    }

    /// <summary>Stable identifier, persisted as the start page.</summary>
    public string Key { get; }

    /// <summary>Resource key of the rail label.</summary>
    public string TitleKey { get; }

    /// <summary>Rail label and page heading, in the current interface language.</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>Resource key of the rail icon geometry.</summary>
    public string IconKey { get; }

    /// <summary>Optional count shown next to the rail label.</summary>
    [ObservableProperty] private int _badge;

    /// <summary>True when there is a count worth showing.</summary>
    public bool HasBadge => Badge > 0;

    partial void OnBadgeChanged(int value) => OnPropertyChanged(nameof(HasBadge));

    /// <summary>True while this is the visible page.</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>Called when the page becomes visible.</summary>
    public virtual void Activated()
    {
    }

    /// <summary>
    /// Called after the interface language changes.
    /// </summary>
    /// <remarks>
    /// Bound resource strings update themselves; this is for text a page has already
    /// composed — a formatted sentence, or a list built through a converter.
    /// </remarks>
    public virtual void LanguageChanged()
    {
    }

    /// <summary>Called on every drain pass, whether or not the page is visible.</summary>
    /// <remarks>
    /// Deliberately also when hidden: rail badges and the dashboard's "signals today"
    /// have to stay right on a page the user is not looking at. Pages that do expensive
    /// work check <see cref="IsActive"/> themselves.
    /// </remarks>
    public virtual void Drained(in DrainResult result)
    {
    }

    public virtual void Dispose() => _titleSubscription.Dispose();
}
