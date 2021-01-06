import { HubConnectionBuilder } from '@aspnet/signalr';
import React, { useState, useEffect } from 'react';
import StocksGrid from './StocksGrid';
import MessagesGrid from './MessagesGrid';
import Loading from './Loading';

const Home = () => {

  const [stocks, setStocks] = useState(null);
  const [messages, setMessages] = useState(null);
  const [hubConnection, setHubConnection] = useState(null);

  useEffect(() => {
    
    const createHubConnection = async () => {
      const hubConnect = new HubConnectionBuilder()
        .withUrl("https://dev.technopriest.ru/stockshub")
        .build();
      try {
        await hubConnect.start();
        console.log('Connection successful.');

        hubConnect.on('stocks', stocks => {
          setStocks(stocks);
        });

        hubConnect.on('messages', messages => {
          setMessages(messages);
        })

        await hubConnect.send('stocks');
        await hubConnect.send('messages');

      }
      catch (err)
      {
        console.log('Connection failed: ' + err);
      }
      setHubConnection(hubConnect);
    }

    createHubConnection();

  }, []);

  if (hubConnection == null)
    return <Loading message="Connecting to hub..." />

  if (stocks == null && messages == null)
    return <Loading message="Loading data..." />

  return (
    <>
      { stocks && <StocksGrid stocks={stocks} /> }
      { messages && <MessagesGrid messages={messages} /> }
    </>
  )
}

export default Home;