using System;
using System.Collections.Generic;
using System.Text;

namespace CoreData.Models
{
    public readonly struct StatsUpdateMessage
    {
        public int Processed { get; }
        public int Total { get; }
        public bool Finished { get; }
        public int Percent => Processed * 100 / Total;
        public int NumApiRequests { get; }

        public StatsUpdateMessage(int processed, int total, bool finished, int numApiRequests)
        {
            Processed = processed;
            Total = total;
            Finished = finished;
            NumApiRequests = numApiRequests;
        }

        public override string ToString()
        {
            return $"{Processed} из {Total} ( Запросов: {NumApiRequests} )";
        }
    }

    public struct CommonInfoMessage
    {
        public int? TelegramMessageQuery { get; set; }
        public int? TotalStocksUpdatedInLastSec { get; set; }
        public int? TotalStocksUpdatedInFiveSec { get; set; }
        public int? ResubscribeAttemptsInTenMin { get; set; }
        public string StatusText { get; set; }
        public bool? SetIndeterminate { get; set; }
    }
}
