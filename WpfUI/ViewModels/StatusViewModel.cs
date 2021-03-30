using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreData.Models;
using CoreNgine.Infra;

namespace RcktMon.ViewModels
{
    public class StatusViewModel : PropertyChangedBase, IHandler<StatsUpdateMessage>, IHandler<CommonInfoMessage>
    {
        public string StatusProgressText { get; set; }
        public string StatusInfoText { get; set; }
        public int StatusPercent { get; set; }
        public bool ShowStatus { get; set; } = true;

        public int TelegramQueryDepth { get; set; }
        public int StocksUpdatedIn1Sec { get;set; }
        public int StocksUpdatedIn5Sec { get; set; }
        public int ResubscriptionAttemptsInTenMin { get; set; }

        public IEventAggregator2 EventAggregator { get; }

        public StatusViewModel(IEventAggregator2 eventAggregator)
        {
            EventAggregator = eventAggregator;
            eventAggregator.SubscribeOnPublishedThread(this);
        }

        public Task HandleAsync(StatsUpdateMessage message, CancellationToken cancellationToken)
        {
            //ShowStatus = !message.Finished;
            StatusProgressText = message.ToString();
            StatusPercent = message.Percent;
            StatusInfoText = "Загрузка исторических данных...";
            if (message.Finished)
            {
                StatusInfoText = "Загрузка истории завершена.";
            }
            return Task.CompletedTask;
        }

        public Task HandleAsync(CommonInfoMessage message, CancellationToken cancellationToken)
        {
            if (message.TelegramMessageQuery.HasValue)
                TelegramQueryDepth = message.TelegramMessageQuery.Value;
            if (message.TotalStocksUpdatedInFiveSec.HasValue)
                StocksUpdatedIn5Sec = message.TotalStocksUpdatedInFiveSec.Value;
            if (message.TotalStocksUpdatedInLastSec.HasValue)
                StocksUpdatedIn1Sec = message.TotalStocksUpdatedInLastSec.Value;
            if (message.ResubscribeAttemptsInTenMin.HasValue)
                ResubscriptionAttemptsInTenMin = message.ResubscribeAttemptsInTenMin.Value;
            return Task.CompletedTask;
        }
    }
}
