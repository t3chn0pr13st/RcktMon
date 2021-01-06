import React from 'react'

const MessagesGrid = ({messages}) => {
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
                    <div className="th">Изменение</div>
                </th>
                <th>
                    <div className="th">Объем</div>
                </th>
                <th>
                    <div className="th">Сообщение</div>
                </th>
            </tr>
        </thead>
        <tbody>
            {messages.map(msg => 
                <tr key={msg.ticker} id={msg.ticker} className="" data-symbol-id={msg.ticker}>
                    <td>
                        <div className="src-modules-Market-containers-components-MarketTable-Row-styles-logoName-GctSK">
                            <div className="src-modules-Market-containers-components-MarketTable-Row-styles-shortName-196hq">{msg.ticker}</div>
                        </div>
                    </td>
                    <td>
                        <div>{msg.change}</div>
                    </td>
                    <td>
                        <div>{msg.volume}</div>
                    </td>
                    <td>
                        <div>{msg.text}</div>
                    </td>
                </tr>
            )}
        </tbody>
        </table>
    )
}

export default MessagesGrid
