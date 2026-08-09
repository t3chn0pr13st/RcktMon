namespace RcktMon.FakeServer;

/// <summary>
/// Plausible ticker and company names for the synthetic universe.
/// </summary>
/// <remarks>
/// Real MOEX tickers for the first few dozen, then generated ones. Using recognisable
/// names matters more than it sounds: column widths, Cyrillic rendering and sorting all
/// behave differently against realistic strings than against <c>TICKER0001</c>.
/// </remarks>
internal static class TickerCatalog
{
    private static readonly (string Ticker, string Name)[] Known =
    [
        ("SBER", "Сбер Банк"),
        ("GAZP", "Газпром"),
        ("LKOH", "ЛУКОЙЛ"),
        ("GMKN", "ГМК Норильский никель"),
        ("YDEX", "Яндекс"),
        ("ROSN", "Роснефть"),
        ("NVTK", "НОВАТЭК"),
        ("TATN", "Татнефть"),
        ("MTSS", "МТС"),
        ("MGNT", "Магнит"),
        ("CHMF", "Северсталь"),
        ("NLMK", "НЛМК"),
        ("PLZL", "Полюс"),
        ("ALRS", "АЛРОСА"),
        ("MOEX", "Московская Биржа"),
        ("VTBR", "Банк ВТБ"),
        ("SNGS", "Сургутнефтегаз"),
        ("IRAO", "Интер РАО"),
        ("RUAL", "РУСАЛ"),
        ("PHOR", "ФосАгро"),
        ("MAGN", "ММК"),
        ("AFLT", "Аэрофлот"),
        ("HYDR", "РусГидро"),
        ("FEES", "ФСК Россети"),
        ("TRNFP", "Транснефть"),
        ("RTKM", "Ростелеком"),
        ("AFKS", "АФК Система"),
        ("PIKK", "ПИК"),
        ("SMLT", "Самолет"),
        ("OZON", "Озон"),
        ("FIVE", "Икс 5"),
        ("POSI", "Позитив Текнолоджиз"),
        ("SGZH", "Сегежа"),
        ("UPRO", "Юнипро"),
        ("MSNG", "Мосэнерго"),
        ("BSPB", "Банк Санкт-Петербург"),
        ("CBOM", "МКБ"),
        ("LSRG", "ЛСР"),
        ("RASP", "Распадская"),
        ("ENPG", "ЭН+ ГРУП"),
    ];

    private static readonly string[] Sectors =
    [
        "Финансы", "Энергетика", "Металлы и добыча", "Технологии", "Потребительский",
        "Телеком", "Транспорт", "Недвижимость", "Химия", "Здравоохранение",
    ];

    public static string At(int index) =>
        index < Known.Length
            ? Known[index].Ticker
            : $"SYN{index:D4}";

    public static string NameFor(string ticker, int index) =>
        index < Known.Length
            ? Known[index].Name
            : $"Синтетик {ticker}";

    public static string SectorFor(int index) => Sectors[index % Sectors.Length];
}
