using System;

namespace CoreData.Interfaces
{
    public interface IMessageModel
    {
         DateTime Date { get; set; }
         string Ticker { get; set; }
         string Text { get; set; }
         decimal Change { get; set; }
         decimal Volume { get; set; }
    }
}
