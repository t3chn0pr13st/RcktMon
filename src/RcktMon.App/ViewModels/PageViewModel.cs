using CommunityToolkit.Mvvm.ComponentModel;
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
public abstract partial class PageViewModel : ObservableObject
{
    protected PageViewModel(string key, string title, string iconKey)
    {
        Key = key;
        Title = title;
        IconKey = iconKey;
    }

    /// <summary>Stable identifier, persisted as the start page.</summary>
    public string Key { get; }

    /// <summary>Rail label and page heading.</summary>
    public string Title { get; }

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

    /// <summary>Called on every drain pass, whether or not the page is visible.</summary>
    /// <remarks>
    /// Deliberately also when hidden: rail badges and the dashboard's "signals today"
    /// have to stay right on a page the user is not looking at. Pages that do expensive
    /// work check <see cref="IsActive"/> themselves.
    /// </remarks>
    public virtual void Drained(in DrainResult result)
    {
    }
}
