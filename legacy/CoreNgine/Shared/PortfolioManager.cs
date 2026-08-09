using CoreData.Interfaces;
using CoreNgine.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreNgine.Shared
{
    public class PortfolioManager
    {
        private readonly StocksManager _stocksManager;

        public PortfolioManager(StocksManager stocksManager)
        {
            _stocksManager = stocksManager;
        }

        public void GetPortfolio()
        {
            
        }
    }
}
