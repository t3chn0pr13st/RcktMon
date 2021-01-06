import React from 'react'

const StocksGrid = ( { stocks } ) => {
    return (
        <table className="src-modules-Market-containers-styles-table-3XTI7 table tableSortable">
        <thead>
            <tr>
                <th>
                    <div className="th">
                        <div>Инструмент</div>
                    </div>
                </th>
                <th>
                    <div className="th">Валюта</div>
                </th>
                <th>
                    <div className="th">Цена открытия</div>
                </th>
                <th>
                    <div className="th">Текущая цена</div>
                </th>
                <th>
                    <div className="th">Изменение</div>
                </th>
                <th>
                    <div className="th">Обновление</div>
                </th>
                <th>
                    <div className="th">Статус</div>
                </th>
            </tr>
        </thead>
        <tbody>
            {stocks.map(stock => 
                <tr key={stock.ticker} id={stock.ticker} className="" data-symbol-id={stock.ticker}>
                    <td>
                        <div className="src-modules-Market-containers-components-MarketTable-Row-styles-logoName-GctSK">
                            <div className="src-modules-Market-containers-components-MarketTable-Row-styles-logo-3NQRG" 
                            style={ { backgroundImage: 'url(https://static.tinkoff.ru/brands/traiding/' + stock.isin + 'x160.png)' } }></div>
                            <div className="src-modules-Market-containers-components-MarketTable-Row-styles-shortName-196hq">{stock.ticker}</div>
                            <div className="src-modules-Market-containers-components-MarketTable-Row-styles-fullName-25O_s">
                                <div className="src-modules-Market-containers-components-MarketTable-Row-styles-description-2BaLi">{stock.name}</div>
                                <div className="src-modules-Market-containers-components-MarketTable-Row-styles-shadow-2MlRJ"></div>
                            </div>
                        </div>
                    </td>
                    <td>
                        <div>{stock.currency}</div>
                    </td>
                    <td>
                        <div>{stock.todayOpenF}</div>
                    </td>
                    <td>
                        <div>{stock.priceF}</div>
                    </td>
                    <td>
                        <div>{stock.dayChangeF}</div>
                    </td>
                    <td>
                        <div>{new Date(stock.lastUpdate).toLocaleTimeString("ru-RU")}</div>
                    </td>
                    <td>
                        <div>{stock.status}</div>
                    </td>
                </tr>
            )}
        </tbody>
        </table>
    )
}

export default StocksGrid
