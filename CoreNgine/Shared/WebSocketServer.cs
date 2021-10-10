using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using CoreData;

using Newtonsoft.Json.Linq;

using vtortola.WebSockets;
using vtortola.WebSockets.Rfc6455;

namespace CoreNgine.Shared
{
    public class WebSocketServer : IDisposable
    {
        private WebSocketListener _server;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private IPAddress _ip;
        private int _port;

        public ConcurrentDictionary<WebSocket, WebSocket> ClientSockets { get; } = new ConcurrentDictionary<WebSocket, WebSocket>();

        public WebSocketServer(IPAddress listenAddress, int listenPort)
        {
            _ip = listenAddress;
            _port = listenPort;
        }

        public WebSocketServer() : this(IPAddress.Loopback, 51337) { }

        public async Task<Exception> Start()
        {
            try
            {
                if (_server != null)
                {
                    _cancellationTokenSource.Cancel();
                    await _server.StopAsync();
                    _server.Dispose();
                    ClientSockets.Clear();
                }
                var options = new WebSocketListenerOptions();
                options.Standards.RegisterRfc6455();
                _server = new WebSocketListener(new IPEndPoint(_ip, _port), options);
                _cancellationTokenSource = new CancellationTokenSource();
                await _server.StartAsync();
                _ = Task.Factory.StartLongRunningTask(async () => await ClientListenerLoop(), _cancellationTokenSource.Token);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public async Task SendAll(string message)
        {
            var token = _cancellationTokenSource.Token;
            foreach (var client in ClientSockets.Keys.ToList())
            {
                try
                {
                    await client.WriteStringAsync(message, token);
                }
                catch
                {
                    ClientSockets.TryRemove(client, out _);
                }
            }
        }

        public void Stop()
        {
            _cancellationTokenSource.Cancel();
        }

        private async Task ClientListenerLoop()
        {
            var token = _cancellationTokenSource.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var ws = await _server.AcceptWebSocketAsync(token);
                    if (ws != null)
                    {
                        ClientSockets[ws] = ws;
                        _ = Task.Factory.StartLongRunningTask(async () => await HandleConnectionAsync(ws), token);
                    }
                }
                catch
                {

                }
            }
        }

        private async Task HandleConnectionAsync(WebSocket clientSocket)
        {
            var token = _cancellationTokenSource.Token;
            try
            {
                while (clientSocket.IsConnected && !token.IsCancellationRequested)
                {
                    var text = await clientSocket.ReadStringAsync(token);
                    // client message processing can be added here later
                }
            }
            catch
            {

            }
        }

        public void Dispose()
        {
            if (_server != null)
                _server.Dispose();
        }
    }
}
