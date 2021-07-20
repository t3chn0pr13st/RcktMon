using CoreData.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CoreCodeGenerators;

namespace CoreData.Settings
{
    [Cloneable]
    public partial class SettingsContainer : ISettingsProvider, INgineSettings
    {
        #region App Settings 
        
        public string TiApiKey { get; set; }
        public string TgBotApiKey { get; set; }
        public string TgChatId { get; set; }
        public string TgChatIdRu { get; set; }
        public decimal MinDayPriceChange { get; set; }
        public decimal MinXMinutesPriceChange { get; set; }
        public decimal MinVolumeDeviationFromDailyAverage { get; set; }
        public decimal MinXMinutesVolChange { get; set; }
        public int NumOfMinToCheck { get; set; }
        public int NumOfMinToCheckVol { get; set; }
        public bool IsTelegramEnabled { get; set; }
        public bool CheckRockets { get; set; }
        public bool SubscribeInstrumentStatus { get; set; }
        public bool HideRussianStocks { get; set; }

        public bool USAQuotesEnabled { get; set; }
        public string USAQuotesURL { get; set; }
        public string USAQuotesLogin { get; set; }
        public string USAQuotesPassword { get; set; }
        public string TgArbitrageLongUSAChatId { get; set; }
        public string TgArbitrageShortUSAChatId { get; set; }
        public string ChartUrlTemplate { get; set; }
        public string IncludePattern { get; set; }
        public string ExcludePattern { get; set; }

        #endregion App Settings

        public INgineSettings LastSettings { get; protected set; }

        public INgineSettings Settings => this;

        public SettingsContainer()
        {            

        }

        public virtual INgineSettings ReadSettings()
        {
            LastSettings = this.Clone() as INgineSettings;
            return this;
        }

        public virtual void SaveSettings(INgineSettings settings)
        {

        }
    }
}
