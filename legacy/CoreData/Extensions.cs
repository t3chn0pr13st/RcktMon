using System;
using System.Globalization;

namespace CoreData
{
    public static class PriceExt
    {
        public static string FormatDecimal(this decimal val) => val.ToString(CultureInfo.InvariantCulture);

        public static string FormatPercent(this decimal val) => val.ToString("P2", CultureInfo.InvariantCulture).Replace(" ", "");

        public static string FormatNumber(this decimal price, bool sixSigns = false)
        {
            string mod = "";
            if (price < 0.1m)
                sixSigns = true;

            price = Math.Round(price, sixSigns ? 6 : 2);
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

            return $"{price.ToString(CultureInfo.InvariantCulture)}{mod}";
        }

        public static string FormatPrice(this decimal price, string currency, bool sixSignsPrecision = false)
        {
            var priceStr = price.FormatNumber(sixSignsPrecision);
            switch (currency.ToUpper())
            {
                case "RUB":
                    return $"{priceStr} rub.";
                case "USD":
                    return $"${priceStr}";
                case "EUR":
                    return $"€{priceStr}";
                default:
                    return $"{priceStr}{currency} ";
            }
        }

    }
}
