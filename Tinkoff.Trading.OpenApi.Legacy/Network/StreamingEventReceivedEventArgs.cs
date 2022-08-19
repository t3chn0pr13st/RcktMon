using Tinkoff.Trading.OpenApi.Legacy.Models;

namespace Tinkoff.Trading.OpenApi.Legacy.Network
{
    public class StreamingEventReceivedEventArgs
    {
        public StreamingResponse Response { get; }

        public StreamingEventReceivedEventArgs(StreamingResponse response)
        {
            Response = response;
        }
    }
}