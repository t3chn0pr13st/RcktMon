using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CoreData.Interfaces;
using CoreNgine.Models;
using Microsoft.AspNetCore.SignalR;

namespace APIServer.Controllers
{
    [Route( "api/[controller]" )]
    [ApiController]
    public class StocksController : ControllerBase
    {
        private readonly IHubContext<StocksHub> _hubContext;

        public StocksController(IHubContext<StocksHub> hubContext)
        {
            _hubContext = hubContext;
        }

        [HttpPost]
        public async Task<IActionResult> Post(IStockModel stockModel)
        {
            await _hubContext.Clients.All.SendAsync("stock", stockModel);
            return Ok();
        }
    }
}
