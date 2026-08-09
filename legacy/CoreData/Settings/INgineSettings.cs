using CoreData.Interfaces;
using System;
using System.Collections.Generic;

namespace CoreData.Settings
{
    public interface INgineSettings : ICloneable
    {
        string TiApiKey { get; set; }
        string TgBotApiKey { get; set; }
        string TgChatId { get; set; }
        string TgChatIdRu { get; set; }
        decimal MinDayPriceChange { get; set; }
        decimal MinXMinutesPriceChange { get; set; }
        int NumOfMinToCheck { get; set; }
        int NumOfMinToCheckVol { get; set; }
        decimal MinVolumeDeviationFromDailyAverage { get; set; }
        decimal MinXMinutesVolChange { get; set; }
        bool IsTelegramEnabled { get; set; }
        string TgCallbackUrl { get; set; }
        string KvtToken { get; set; }
        bool CheckRockets { get; set; }
        bool SubscribeInstrumentStatus { get; set; }
        bool HideRussianStocks { get; set; }
        bool USAQuotesEnabled { get; set; }
        string USAQuotesURL { get; set; }
        string USAQuotesLogin { get; set; }
        string USAQuotesPassword { get; set; }
        string TgArbitrageLongUSAChatId { get; set; }
        string TgArbitrageShortUSAChatId { get; set; }
        string ChartUrlTemplate { get; set; }
        string IncludePattern { get; set; }
        string ExcludePattern { get; set; }
    }

    public interface IAssetGroupSettings
    {
        bool IsSubscriptionEnabled { get; set; }
        bool IsTelegramEnabled { get; set; }
        string Currency { get; set; }
        string CurrencyDisplay { get;set; }
        decimal MinDayPriceChange { get; set; }
        decimal MinXMinutesPriceChange { get; set; }
        int NumOfMinToCheck { get; set; }
        int NumOfMinToCheckVol { get; set; }
        decimal MinVolumeDeviationFromDailyAverage { get; set; }
        decimal MinXMinutesVolChange { get; set; }
        string IncludePattern { get; set; }
        string ExcludePattern { get; set; }
        string ChartUrlTemplate { get; set; }
    }

    public interface ISettingsProvider
    {
        Dictionary<string, AssetGroupSettingsModel> AssetGroupSettingsByCurrency { get; set; }
        INgineSettings LastSettings { get; }
        INgineSettings Settings { get; }
        INgineSettings ReadSettings();
        IAssetGroupSettings GetSettingsForStock(IStockModel stock);
        void SaveSettings(INgineSettings settings);
    }
}
