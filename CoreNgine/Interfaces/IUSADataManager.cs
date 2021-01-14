using System;
using System.Collections.Generic;
using System.Text;
using CoreData.Models;

namespace CoreNgine.Interfaces
{
    public interface IUSADataManager
    {
        IDictionary<string, QuoteData> QuoteDatas { get; }
        void SubscribeTicker(string ticker);
        void UnsubscribeTicker(string ticker);
        void Run();
        void Stop();
    }
}
