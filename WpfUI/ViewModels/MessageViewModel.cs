using System;
using System.Collections.Generic;
using System.Text;
using Caliburn.Micro;

namespace TradeApp.ViewModels
{
    public class MessageViewModel : PropertyChangedBase
    {
        public DateTime Date { get; set; }
        public string Ticker { get; set; }
        public string Text { get; set; }
        public decimal Change { get; set; }
        public decimal Volume { get; set; }
    }
}
