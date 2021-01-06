using System;
using CoreData.Interfaces;

namespace CoreData.Models
{
    public class MessageModel : IMessageModel
    {
        public DateTime Date { get; set; }
        public string Ticker { get; set; }
        public string Text { get; set; }
        public decimal Change { get; set; }
        public decimal Volume { get; set; }
    }
}
