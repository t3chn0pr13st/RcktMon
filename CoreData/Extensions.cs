using System;
using System.Globalization;

namespace CoreData
{
    public static class PriceExt
    {
        public static string FormatDecimal(this decimal val) => val.ToString(CultureInfo.InvariantCulture);

        public static string FormatPercent(this decimal val) => val.ToString("P2", CultureInfo.InvariantCulture);


        public static string FormatPrice(this decimal price, string currency)
        {
            string mod = "";
            price = Math.Round(price, 2);
            if (price > 1_000_000_000)
            {
                mod = "G";
                price /= 1_000_000_000;
                price = Math.Round(price, 4);
            }
            else if (price > 1_000_000)
            {
                mod = "M";
                price /= 1_000_000;
                price = Math.Round(price, 3);
            }
            else if (price > 10000)
            {
                mod = "k";
                price /= 1000;
                price = Math.Round(price, 2);
            }

            var priceStr = price.ToString(CultureInfo.InvariantCulture);
            switch (currency.ToUpper())
            {
                case "RUB":
                    return $"{priceStr}{mod} rub.";
                case "USD":
                    return $"${priceStr}{mod}";
                case "EUR":
                    return $"€{priceStr}{mod}";
                default:
                    return $"{priceStr}{mod} {currency} ";
            }
        }

    }
}
