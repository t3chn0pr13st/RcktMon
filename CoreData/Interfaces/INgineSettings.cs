namespace CoreData.Interfaces
{
    public interface INgineSettings
    {
        string TiApiKey { get; set; }
        string TgBotApiKey { get; set; }
        string TgChatId { get; set; }
        decimal MinDayPriceChange { get; set; }
        decimal MinTenMinutesPriceChange { get; set; }
        decimal MinVolumeDeviationFromDailyAverage { get; set; }
        bool IsTelegramEnabled { get; set; }
    }
}
