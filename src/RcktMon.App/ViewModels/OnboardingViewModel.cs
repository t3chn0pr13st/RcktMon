using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RcktMon.Client.Core.Connection;
using RcktMon.Client.Core.Security;
using RcktMon.Client.Core.Settings;
using RcktMon.Protocol.V1;

namespace RcktMon.App.ViewModels;

/// <summary>
/// First run: three steps, and not one broker token among them.
/// </summary>
/// <remarks>
/// The pre-rewrite app opened on a settings expander demanding a Tinkoff OpenAPI token, a
/// Telegram bot token, two chat ids, a chart URL template, a callback URL and a
/// third-party tools token — before anything worked at all. Market data is now subscribed
/// server-side, so onboarding needs an address, a key, and a choice of how noisy you want
/// alerts to be.
/// </remarks>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly ClientSettings _settings;
    private readonly ISecretStore _secrets;
    private readonly Action _persist;
    private readonly Action _reconnect;
    private readonly Func<ProfileClient?> _profiles;
    private readonly Action _finished;

    public OnboardingViewModel(
        ClientSettings settings,
        ISecretStore secrets,
        Action persist,
        Action reconnect,
        Func<ProfileClient?> profiles,
        Action finished)
    {
        _settings = settings;
        _secrets = secrets;
        _persist = persist;
        _reconnect = reconnect;
        _profiles = profiles;
        _finished = finished;

        _serverAddress = settings.ServerAddress;
        _accessKey = secrets.Read(SecretKeys.AccessKey) ?? string.Empty;
    }

    /// <summary>Presets offered in step three, loaded from the server once connected.</summary>
    public ObservableCollection<AlertProfileViewModel> Presets { get; } = [];

    /// <summary>0 welcome, 1 connect, 2 choose a profile.</summary>
    [ObservableProperty] private int _step;

    [ObservableProperty] private string _serverAddress;

    [ObservableProperty] private string _accessKey;

    [ObservableProperty] private ConnectionStatus _status = new(ConnectionState.Disconnected);

    [ObservableProperty] private string? _accountName;

    [ObservableProperty] private AlertProfileViewModel? _selectedPreset;

    [ObservableProperty] private string? _error;

    public bool IsWelcome => Step == 0;

    public bool IsConnect => Step == 1;

    public bool IsProfile => Step == 2;

    /// <summary>True once the handshake succeeded, which is what unlocks step three.</summary>
    public bool IsConnected => Status.IsUsable;

    partial void OnStepChanged(int value)
    {
        OnPropertyChanged(nameof(IsWelcome));
        OnPropertyChanged(nameof(IsConnect));
        OnPropertyChanged(nameof(IsProfile));

        if (value == 2)
        {
            _ = LoadPresetsAsync();
        }
    }

    partial void OnStatusChanged(ConnectionStatus value) => OnPropertyChanged(nameof(IsConnected));

    [RelayCommand]
    private void Next()
    {
        if (Step == 1)
        {
            Connect();
        }

        Step = Math.Min(2, Step + 1);
    }

    [RelayCommand]
    private void Back() => Step = Math.Max(0, Step - 1);

    /// <summary>Writes the address and key, then restarts the connection supervisor.</summary>
    [RelayCommand]
    private void Connect()
    {
        Error = null;

        string address = ServerAddress.Trim();

        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            Error = "Адрес должен быть вида https://api.example.com";
            return;
        }

        _settings.ServerAddress = address;
        _persist();

        string key = AccessKey.Trim();
        if (key.Length > 0)
        {
            _secrets.Write(SecretKeys.AccessKey, key);
        }

        _reconnect();
    }

    /// <summary>Skips the preset step; the server's default profile stays active.</summary>
    [RelayCommand]
    private void Finish()
    {
        _settings.OnboardingCompleted = true;
        _persist();
        _finished();
    }

    [RelayCommand]
    private async Task ChoosePresetAsync()
    {
        if (SelectedPreset is null || _profiles() is not { } client)
        {
            Finish();
            return;
        }

        try
        {
            AlertProfile wire = SelectedPreset.ToProto();
            wire.Id = string.Empty;
            wire.PresetKey = string.Empty;

            AlertProfile created = await client.UpsertAsync(wire, CancellationToken.None);
            await client.SetActiveAsync(created.Id, CancellationToken.None);
        }
        catch (Grpc.Core.RpcException ex)
        {
            // Not worth blocking onboarding over: the account keeps its default profile
            // and the user can pick another one in settings.
            Error = $"Профиль не сохранён: {ex.Status.Detail}";
        }

        Finish();
    }

    private async Task LoadPresetsAsync()
    {
        if (_profiles() is not { } client || Presets.Count > 0)
        {
            return;
        }

        try
        {
            ListProfilesResponse response = await client.ListAsync(CancellationToken.None);

            foreach (AlertProfile preset in response.Presets)
            {
                Presets.Add(new AlertProfileViewModel(preset, static () => { }));
            }

            SelectedPreset = Presets.Skip(1).FirstOrDefault() ?? Presets.FirstOrDefault();
        }
        catch (Grpc.Core.RpcException ex)
        {
            Error = $"Не удалось загрузить пресеты: {ex.Status.Detail}";
        }
    }
}
