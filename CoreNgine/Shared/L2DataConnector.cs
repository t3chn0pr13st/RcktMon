using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreNgine.Shared
{
    public class L2DataConnector : IDisposable
    {
        public int ReceiveBufferSize { get; set; } = 8192;

        private ClientWebSocket _clientWebSocket;

        private CancellationTokenSource _cancellationTokenSource;

        private string _l2dataToken = "test";

        public async Task ConnectAsync(string url = "ws://localhost:8090") {
            if (_clientWebSocket != null) {
                if (_clientWebSocket.State == WebSocketState.Open) return;
                else _clientWebSocket.Dispose();
            }
            _clientWebSocket = new ClientWebSocket();
            if (_cancellationTokenSource != null) _cancellationTokenSource.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            try
            {
                await _clientWebSocket.ConnectAsync(new Uri(url), _cancellationTokenSource.Token);
                await SendMessageAsync(_l2dataToken);
                await Task.Factory.StartNew(ReceiveLoop, _cancellationTokenSource.Token,
                    TaskCreationOptions.LongRunning, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                Debugger.Break();
            }
        }

        public async Task DisconnectAsync() {
            if (_clientWebSocket is null) return;
            // TODO: requests cleanup code, sub-protocol dependent.
            if (_clientWebSocket.State == WebSocketState.Open) {
                _cancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(2));
                await _clientWebSocket.CloseOutputAsync(WebSocketCloseStatus.Empty, "", CancellationToken.None);
                await _clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            }
            _clientWebSocket.Dispose();
            _clientWebSocket = null;
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        private async Task ReceiveLoop() {
            var loopToken = _cancellationTokenSource.Token;
            MemoryStream outputStream = null;
            WebSocketReceiveResult receiveResult = null;
            var buffer = new byte[ReceiveBufferSize];
            try {
                while (!loopToken.IsCancellationRequested) {
                    outputStream = new MemoryStream(ReceiveBufferSize);
                    do {
                        receiveResult = await _clientWebSocket.ReceiveAsync(buffer, _cancellationTokenSource.Token);
                        if (receiveResult.MessageType != WebSocketMessageType.Close)
                            outputStream.Write(buffer, 0, receiveResult.Count);
                    }
                    while (!receiveResult.EndOfMessage);
                    if (receiveResult.MessageType == WebSocketMessageType.Close) break;
                    outputStream.Position = 0;
                    await ResponseReceived(outputStream);
                }
            }
            catch (TaskCanceledException) { }
            finally {
                outputStream?.Dispose();
            }
        }

        public async Task SendMessageAsync(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            await _clientWebSocket.SendAsync(new ArraySegment<byte>(buffer, 0, buffer.Length),
                WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
        }

        private FileStream _l2dataLogStream = null;
        private StreamWriter _l2dataWriter = null;

        public async Task ResponseReceived(Stream inputStream) 
        {
            if (_l2dataLogStream == null)
                _l2dataLogStream = new FileStream("l2datalog.txt", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

            if (_l2dataWriter == null)
                _l2dataWriter = new StreamWriter(_l2dataLogStream);

            using (var sr = new StreamReader(inputStream))
            {
                var text = await sr.ReadToEndAsync();
                await _l2dataWriter.WriteLineAsync(text);
            }
        }

        public void Dispose()
        {
            _l2dataWriter?.Dispose();
            _l2dataLogStream.Dispose();
            DisconnectAsync().Wait();
        }
    }
}
