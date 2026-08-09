using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.App.ViewModels;

/// <summary>
/// An editable view over one <see cref="AlertRuleGroup"/>.
/// </summary>
/// <remarks>
/// <para>
/// The wire type stores fixed-point <c>Decimal</c> messages and fractions; people think in
/// percentages and multiples. This wrapper is where that translation lives, once, instead
/// of in a converter per input box.
/// </para>
/// <para>
/// Percent properties are the fraction times 100, so 5 in the box means 0.05 on the wire.
/// The pre-rewrite settings pane stored raw fractions in the text boxes and left users to
/// work out that 0.05 meant five percent.
/// </para>
/// </remarks>
public sealed partial class AlertRuleGroupViewModel : ObservableObject
{
    private readonly Action _changed;

    public AlertRuleGroupViewModel(AlertRuleGroup group, Action changed)
    {
        _changed = changed;

        GroupKey = group.GroupKey;
        _enabled = group.Enabled;
        _telegramEnabled = group.TelegramEnabled;
        _dayPricePercent = group.MinDayPriceChange.ToDecimal() * 100m;
        _dayVolumeMultiple = group.MinVolumeDeviationFromDailyAverage.ToDecimal();
        _windowPricePercent = group.MinXMinutesPriceChange.ToDecimal() * 100m;
        _windowVolumeMultiple = group.MinXMinutesVolumeChange.ToDecimal();
        _minutes = group.NumOfMinToCheck;
        _volumeMinutes = group.NumOfMinToCheckVol;
        _includePattern = group.IncludePattern;
        _excludePattern = group.ExcludePattern;
    }

    /// <summary>Currency today; a free string so the grouping can change later.</summary>
    public string GroupKey { get; }

    [ObservableProperty] private bool _enabled;

    [ObservableProperty] private bool _telegramEnabled;

    /// <summary>Whole-day move that arms the day rule, in percent.</summary>
    [ObservableProperty] private decimal _dayPricePercent;

    /// <summary>Day volume as a multiple of the monthly average.</summary>
    [ObservableProperty] private decimal _dayVolumeMultiple;

    /// <summary>Move inside the rolling window that fires the price rule, in percent.</summary>
    [ObservableProperty] private decimal _windowPricePercent;

    /// <summary>Window volume as a multiple of the average.</summary>
    [ObservableProperty] private decimal _windowVolumeMultiple;

    /// <summary>Width of the price window, in minutes.</summary>
    [ObservableProperty] private int _minutes;

    /// <summary>Width of the volume window, in minutes.</summary>
    [ObservableProperty] private int _volumeMinutes;

    [ObservableProperty] private string _includePattern = string.Empty;

    [ObservableProperty] private string _excludePattern = string.Empty;

    public AlertRuleGroup ToProto() => new()
    {
        GroupKey = GroupKey,
        Enabled = Enabled,
        TelegramEnabled = TelegramEnabled,
        MinDayPriceChange = (DayPricePercent / 100m).ToProto(),
        MinVolumeDeviationFromDailyAverage = DayVolumeMultiple.ToProto(),
        MinXMinutesPriceChange = (WindowPricePercent / 100m).ToProto(),
        MinXMinutesVolumeChange = WindowVolumeMultiple.ToProto(),
        NumOfMinToCheck = Minutes,
        NumOfMinToCheckVol = VolumeMinutes,
        IncludePattern = IncludePattern,
        ExcludePattern = ExcludePattern,
    };

    partial void OnEnabledChanged(bool value) => _changed();

    partial void OnTelegramEnabledChanged(bool value) => _changed();

    partial void OnDayPricePercentChanged(decimal value) => _changed();

    partial void OnDayVolumeMultipleChanged(decimal value) => _changed();

    partial void OnWindowPricePercentChanged(decimal value) => _changed();

    partial void OnWindowVolumeMultipleChanged(decimal value) => _changed();

    partial void OnMinutesChanged(int value) => _changed();

    partial void OnVolumeMinutesChanged(int value) => _changed();

    partial void OnIncludePatternChanged(string value) => _changed();

    partial void OnExcludePatternChanged(string value) => _changed();
}

/// <summary>An editable alert profile: a name and one rule group per currency.</summary>
public sealed partial class AlertProfileViewModel : ObservableObject
{
    public AlertProfileViewModel(AlertProfile profile, Action changed)
    {
        Id = profile.Id;
        PresetKey = profile.PresetKey;
        _name = profile.Name;
        _isActive = profile.IsActive;

        Groups = [.. profile.Groups.Select(g => new AlertRuleGroupViewModel(g, changed))];
        _changed = changed;
    }

    private readonly Action _changed;

    public string Id { get; }

    /// <summary>Non-empty on the server's presets, which are copied rather than edited.</summary>
    public string PresetKey { get; }

    /// <summary>True for a preset offered during onboarding.</summary>
    public bool IsPreset => PresetKey.Length > 0;

    public ObservableCollection<AlertRuleGroupViewModel> Groups { get; }

    [ObservableProperty] private string _name;

    [ObservableProperty] private bool _isActive;

    /// <summary>One-line summary used on the preset cards.</summary>
    public string Summary
    {
        get
        {
            AlertRuleGroupViewModel? first = Groups.FirstOrDefault();
            return first is null
                ? "Нет правил"
                : $"День от {first.DayPricePercent:0.#}% · {first.WindowPricePercent:0.#}% за {first.Minutes} мин";
        }
    }

    public AlertProfile ToProto()
    {
        var profile = new AlertProfile
        {
            Id = Id,
            Name = Name,
            IsActive = IsActive,
            PresetKey = PresetKey,
        };

        profile.Groups.AddRange(Groups.Select(g => g.ToProto()));
        return profile;
    }

    partial void OnNameChanged(string value) => _changed();
}
