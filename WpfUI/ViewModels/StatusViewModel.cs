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
    public class StatusViewModel : PropertyChangedBase, IHandler<StatsUpdateMessage>
    {
        public string StatusProgressText { get; set; }
        public string StatusInfoText { get; set; }
        public int StatusPercent { get; set; }
        public bool ShowStatus { get; set; }

        public IEventAggregator2 EventAggregator { get; }

        public StatusViewModel(IEventAggregator2 eventAggregator)
        {
            EventAggregator = eventAggregator;
            eventAggregator.SubscribeOnPublishedThread(this);
        }

        public Task HandleAsync(StatsUpdateMessage message, CancellationToken cancellationToken)
        {
            ShowStatus = !message.Finished;
            StatusProgressText = message.ToString();
            StatusPercent = message.Percent;
            StatusInfoText = "Загрузка исторических данных...";
            return Task.CompletedTask;
        }
    }
}
