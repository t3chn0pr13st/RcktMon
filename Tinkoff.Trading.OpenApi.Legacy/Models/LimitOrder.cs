namespace Tinkoff.Trading.OpenApi.Legacy.Models
{
    public class LimitOrder
    {
        public string Figi { get; }
        public int Lots { get; }
        public OperationType Operation { get; }
        public decimal Price { get; }
        public string BrokerAccountId { get; }

        public LimitOrder(string figi, int lots, OperationType operation, decimal price, string brokerAccountId = null)
        {
            Figi = figi;
            Lots = lots;
            Operation = operation;
            Price = price;
            BrokerAccountId = brokerAccountId;
        }
    }
}
