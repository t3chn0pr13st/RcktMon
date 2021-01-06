using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Tinkoff.Trading.OpenApi.Models;
using Tinkoff.Trading.OpenApi.Network;

namespace CoreNgine.Data
{
    public class BrokerAction
    {
        public Func<Connection, Task> Action { get; private set; }
        public string Description { get; private set; }

        public BrokerAction(Func<Connection, Task> action, string description)
        {
            Action = action;
            Description = description;
        }
    }
}
