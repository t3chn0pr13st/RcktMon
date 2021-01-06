using System;
using Caliburn.Micro;
using CoreData.Interfaces;
using CoreNgine.Models;

namespace RcktMon.ViewModels
{
    public class MessageViewModel : PropertyChangedBase, IMessageModel
    {
        public DateTime Date { get; set; }
        public string Ticker { get; set; }
        public string Text { get; set; }
        public decimal Change { get; set; }
        public decimal Volume { get; set; }
    }
}
