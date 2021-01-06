using System;

namespace CoreData
{
    public static class PriceExt
    {
        public static string FormatPrice(this decimal price, string currency)
        {
            string mod = "";
            price = Math.Round(price, 2);
            if (price > 1_000_000_000)
            {
                mod = " млрд.";
                price /= 1_000_000_000;
                price = Math.Round(price, 4);
            }
            else if (price > 1_000_000)
            {
                mod = " млн.";
                price /= 1_000_000;
                price = Math.Round(price, 3);
            }
            else if (price > 10000)
            {
                mod = " тыс.";
                price /= 1000;
                price = Math.Round(price, 2);
            }

            switch (currency.ToUpper())
            {
                case "RUB":
                    return $"{price}{mod} руб.";
                case "USD":
                    return $"${price}{mod}";
                default:
                    return $"{currency} {price}{mod}";
            }
        }

    }
}
