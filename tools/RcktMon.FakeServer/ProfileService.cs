using Grpc.Core;
using RcktMon.Protocol;
using RcktMon.Protocol.V1;

namespace RcktMon.FakeServer;

/// <summary>
/// Alert profiles, held in memory for the life of the process.
/// </summary>
/// <remarks>
/// Enough of the real contract to build the settings pane against: presets to start from,
/// CRUD that round-trips, and a <c>PreviewProfile</c> that responds to the thresholds you
/// type. The preview is the reason this service is worth faking at all — a slider with no
/// feedback is guesswork, which is precisely what the pre-rewrite settings pane was.
/// </remarks>
public sealed class FakeProfileService(MarketHub hub) : ProfileService.ProfileServiceBase
{
    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, AlertProfile> Profiles = [];
    private static string _activeProfileId = string.Empty;

    /// <summary>
    /// Signals a profile at the reference thresholds fires in a day, across all groups.
    /// </summary>
    /// <remarks>
    /// Calibrated so the balanced preset lands in the range the live Telegram channels
    /// actually post — a few dozen a day. The number only has to move the right way when
    /// a threshold moves, but it also has to be believable, or the user stops reading it.
    /// </remarks>
    private const double ReferenceDailySignals = 150;

    public override Task<ListProfilesResponse> ListProfiles(ListProfilesRequest request, ServerCallContext context)
    {
        lock (Gate)
        {
            EnsureSeeded();

            var response = new ListProfilesResponse { ActiveProfileId = _activeProfileId };
            response.Profiles.AddRange(Profiles.Values.Select(Clone));
            response.Presets.AddRange(Presets().Select(Clone));
            return Task.FromResult(response);
        }
    }

    public override Task<AlertProfile> UpsertProfile(UpsertProfileRequest request, ServerCallContext context)
    {
        AlertProfile profile = request.Profile
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "profile is required"));

        lock (Gate)
        {
            EnsureSeeded();

            AlertProfile stored = Clone(profile);
            if (string.IsNullOrEmpty(stored.Id))
            {
                stored.Id = Guid.NewGuid().ToString("N");
            }

            // A preset is a starting point, never a live reference: copying it here is
            // what lets the user edit their own thresholds without the preset drifting.
            stored.PresetKey = string.Empty;

            Profiles[stored.Id] = stored;

            if (string.IsNullOrEmpty(_activeProfileId))
            {
                _activeProfileId = stored.Id;
            }

            stored.IsActive = stored.Id == _activeProfileId;
            return Task.FromResult(Clone(stored));
        }
    }

    public override Task<DeleteProfileResponse> DeleteProfile(DeleteProfileRequest request, ServerCallContext context)
    {
        lock (Gate)
        {
            EnsureSeeded();

            if (Profiles.Count <= 1)
            {
                throw new RpcException(new Status(
                    StatusCode.FailedPrecondition,
                    "Нельзя удалить последний профиль оповещений."));
            }

            Profiles.Remove(request.Id);

            if (_activeProfileId == request.Id)
            {
                _activeProfileId = Profiles.Keys.First();
            }

            return Task.FromResult(new DeleteProfileResponse());
        }
    }

    public override Task<SetActiveProfileResponse> SetActiveProfile(
        SetActiveProfileRequest request,
        ServerCallContext context)
    {
        lock (Gate)
        {
            EnsureSeeded();

            if (!Profiles.ContainsKey(request.Id))
            {
                throw new RpcException(new Status(StatusCode.NotFound, "Профиль не найден."));
            }

            _activeProfileId = request.Id;
            return Task.FromResult(new SetActiveProfileResponse());
        }
    }

    public override Task<PreviewProfileResponse> PreviewProfile(
        PreviewProfileRequest request,
        ServerCallContext context)
    {
        AlertProfile profile = request.Profile ?? new AlertProfile();

        double perDay = 0;
        var byKind = new Dictionary<SignalKind, int>();

        AlertRuleGroup[] enabled = [.. profile.Groups.Where(g => g.Enabled)];

        // Split the reference rate across the enabled groups and the three rules, so the
        // estimate tracks the thresholds rather than the number of currencies configured.
        double share = ReferenceDailySignals / (3 * Math.Max(1, enabled.Length));

        foreach (AlertRuleGroup group in enabled)
        {
            // A deterministic falling curve rather than a real backtest: the point is
            // that tightening a threshold visibly reduces the estimate, so the number
            // teaches the user what the control does.
            double day = Rate(group.MinDayPriceChange.ToDecimal(), 0.05m)
                * Rate(group.MinVolumeDeviationFromDailyAverage.ToDecimal(), 2.0m);

            double price = Rate(group.MinXMinutesPriceChange.ToDecimal(), 0.02m)
                / Math.Max(1, group.NumOfMinToCheck / 5d);

            double volume = Rate(group.MinXMinutesVolumeChange.ToDecimal(), 3.0m)
                / Math.Max(1, group.NumOfMinToCheckVol / 5d);

            Add(byKind, SignalKind.DayChange, (int)Math.Round(share * day));
            Add(byKind, SignalKind.MinutesPriceChange, (int)Math.Round(share * price));
            Add(byKind, SignalKind.MinutesVolumeChange, (int)Math.Round(share * volume));

            perDay += share * (day + price + volume);
        }

        var response = new PreviewProfileResponse
        {
            EstimatedSignalsPerDay = (int)Math.Round(perDay),
        };

        response.ByKind.AddRange(byKind.Select(pair => new PreviewBucket
        {
            Kind = pair.Key,
            Count = pair.Value,
        }));

        response.SampleHeadlines.AddRange(
            hub.Signals.Recent(3).Select(signal => signal.Headline));

        return Task.FromResult(response);
    }

    /// <summary>
    /// Maps a threshold to a relative firing rate against a reference value.
    /// </summary>
    /// <remarks>
    /// Squared falloff: alert counts drop faster than linearly as a threshold tightens,
    /// which is what makes the last percentage point of tuning feel worthwhile.
    /// </remarks>
    private static double Rate(decimal threshold, decimal reference)
    {
        if (threshold <= 0m)
        {
            return 1;
        }

        double ratio = (double)(reference / threshold);
        return Math.Clamp(ratio * ratio, 0.002, 6);
    }

    private static void Add(Dictionary<SignalKind, int> target, SignalKind kind, int count) =>
        target[kind] = target.TryGetValue(kind, out int existing) ? existing + count : count;

    private static void EnsureSeeded()
    {
        if (Profiles.Count > 0)
        {
            return;
        }

        AlertProfile balanced = Preset("balanced", "Сбалансированный", 0.05m, 2.0m, 0.02m, 3.0m, 5, 5);
        balanced.Id = Guid.NewGuid().ToString("N");
        balanced.PresetKey = string.Empty;
        balanced.Name = "Мой профиль";
        balanced.IsActive = true;

        Profiles[balanced.Id] = balanced;
        _activeProfileId = balanced.Id;
    }

    private static IReadOnlyList<AlertProfile> Presets() =>
    [
        Preset("conservative", "Консервативный", 0.07m, 3.0m, 0.03m, 5.0m, 5, 5),
        Preset("balanced", "Сбалансированный", 0.05m, 2.0m, 0.02m, 3.0m, 5, 5),
        Preset("aggressive", "Агрессивный", 0.03m, 1.2m, 0.01m, 1.5m, 3, 3),
    ];

    private static AlertProfile Preset(
        string key,
        string name,
        decimal dayChange,
        decimal dayVolume,
        decimal windowPrice,
        decimal windowVolume,
        int minutes,
        int volumeMinutes)
    {
        var profile = new AlertProfile { Id = key, Name = name, PresetKey = key };

        // One group per currency, matching how the WPF settings pane was organised. The
        // key is a free string so this can become per-exchange later without a break.
        foreach (string currency in (string[])["RUB", "USD"])
        {
            profile.Groups.Add(new AlertRuleGroup
            {
                GroupKey = currency,
                Enabled = true,
                TelegramEnabled = currency == "RUB",
                MinDayPriceChange = dayChange.ToProto(),
                MinVolumeDeviationFromDailyAverage = dayVolume.ToProto(),
                MinXMinutesPriceChange = windowPrice.ToProto(),
                MinXMinutesVolumeChange = windowVolume.ToProto(),
                NumOfMinToCheck = minutes,
                NumOfMinToCheckVol = volumeMinutes,
            });
        }

        return profile;
    }

    private static AlertProfile Clone(AlertProfile profile) => profile.Clone();
}
