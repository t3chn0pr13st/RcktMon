using CoreData.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CoreNgine
{
    public class InvestingTickerLoader
    {
        public string USATickersFileName { get; set; } = "investing-equities.json";

        public IDictionary<string, InvestingEquity> TickersUSA { get; private set; }

        public async Task<bool> LoadUSATickers()
        {
            if ( File.Exists( USATickersFileName ) )
            {
                try
                {
                    var text = await File.ReadAllTextAsync(USATickersFileName);
                    TickersUSA = JsonConvert.DeserializeObject<Dictionary<string, InvestingEquity>>(text);
                    return true;
                }
                catch (Exception ex )
                {
                    Debug.WriteLine($"Failed to deserialize investing stocks: {ex.Message}");
                    return false;
                }
            }
            else
            {
                TickersUSA = await DownloadUSATickers();
                try
                {
                    await File.WriteAllTextAsync(USATickersFileName, JsonConvert.SerializeObject(TickersUSA));
                    return true;
                } 
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to serialize investing stocks: {ex.Message}");
                    return false;
                }
                finally
                {                    
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
        }

        public async Task<IDictionary<string, InvestingEquity>> DownloadUSATickers()
        {
            var tickers = new ConcurrentDictionary<string, InvestingEquity>();
            var startDate = DateTime.Now;

            await Task.WhenAll( new[] { 95, 50, 1, 2 }.Select( exchangeId => LoadData( new[] { 5 }, new[] { exchangeId }, tickers, -1 ) ) );

            Debug.WriteLine( $"Loaded {tickers.Count} items in {DateTime.Now.Subtract( startDate )}." );

            return tickers;
        }

        private async Task LoadData( int[] countryIds, int[] exchangeIds, IDictionary<string, InvestingEquity> tickers, int maxConcurrentRequests = 16 )
        {
            foreach ( var countryId in countryIds )
            {
                foreach ( var exchangeId in exchangeIds )
                {
                    int totalRows = 0, totalPages = 0, prevCount = tickers.Count;
                    Debug.WriteLine( $"[{DateTime.Now.ToLongTimeString()}] Loading first page of data for country [ {countryId} ] exchange [ {(Exchange)exchangeId} ]..." );

                    var (totalCount, readCount) = await LoadPage( countryId, exchangeId, 1, tickers );
                    if ( totalCount > 0 )
                    {
                        totalPages = (int)Math.Ceiling( (double)totalCount / readCount );

                        var startDate = DateTime.Now;

                        Debug.WriteLine( $"[{startDate.ToLongTimeString()}] Beginning to load {totalPages - 1} pages ({totalRows} stocks)..." );

                        if ( maxConcurrentRequests > 0 )
                        {
                            using ( var concurrencySemaphore = new SemaphoreSlim( maxConcurrentRequests ) )
                            {
                                List<Task> tasks = new List<Task>();
                                foreach ( int p in Enumerable.Range( 2, totalPages - 1 ) )
                                {
                                    int pageNum = p;
                                    var t = Task.Run( async () =>
                                     {
                                         await concurrencySemaphore.WaitAsync();
                                         try
                                         {
                                             await LoadPage( countryId, exchangeId, pageNum, tickers );
                                         }
                                         finally
                                         {
                                             concurrencySemaphore.Release();
                                         }
                                     } );
                                    tasks.Add( t );
                                }
                                await Task.WhenAll( tasks );
                            }
                        }
                        else
                        {
                            // способ ниже запустит параллельно сколько сможет (по числу ядер в системе) - но нельзя задать сколько именно выполнять параллельно
                            await Task.WhenAll( Enumerable.Range( 2, totalPages - 1 ).Select( page => LoadPage( countryId, exchangeId, page, tickers ) ) );
                        }

                        Debug.WriteLine( $"[{DateTime.Now.ToLongTimeString()}] {totalPages - 1} pages was loaded. Elapsed: {DateTime.Now.Subtract( startDate )}" );
                    }
                }
            }
        }

        private async Task<(int totalCount, int readCount)> LoadPage( int countryId, int exchangeId, int pageNum, IDictionary<string, InvestingEquity> dic )
            => await LoadPage( new[] { countryId }, new[] { exchangeId }, pageNum, dic );

        private async Task<(int totalCount, int readCount)> LoadPage( int[] countryIds, int[] exchangeIds, int pageNum, IDictionary<string, InvestingEquity> dic )
        {
            var client = new RestClient("https://ru.investing.com/stock-screener/Service/SearchStocks");
            client.Options.MaxTimeout = -1;
            client.Options.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/89.0.4389.90 Safari/537.36";

            var request = new RestRequest() { Method = Method.Post };
            request.AddHeader( "X-Requested-With", "XMLHttpRequest" );
            request.AddHeader( "Content-Type", "application/x-www-form-urlencoded" );

            countryIds.ToList().ForEach( cid => request.AddParameter( "country[]", cid ) );
            request.AddParameter( "sector", "7,5,21,12,3,16,8,17,13,9,1,19,6,18,15,20,14,23,2,4,10,11,22" );
            request.AddParameter( "industry", "81,56,110,59,119,41,120,68,67,88,124,125,51,72,147,136,47,12,144,8,50,111,2,151,71,9,105,69,45,117,156,46,13,94,102,95,58,100,101,87,31,106,6,38,112,150,79,107,30,77,131,130,149,160,113,165,28,158,5,103,163,170,60,18,26,137,135,44,35,53,166,48,141,49,142,143,55,129,126,139,169,114,153,78,7,86,10,164,132,1,34,154,3,127,146,115,11,121,162,62,16,108,24,20,54,33,83,29,152,76,133,167,37,90,85,82,104,22,14,17,109,19,43,140,89,145,96,57,84,118,93,171,27,74,97,4,73,36,42,98,65,70,40,99,39,92,122,75,66,63,21,159,25,155,64,134,157,128,61,148,32,138,91,116,123,52,23,15,80,168,161" );
            request.AddParameter( "equityType", "ORD,DRC,Preferred,Unit,ClosedEnd,REIT,ELKS,OpenEnd,Right,ParticipationShare,CapitalSecurity,PerpetualCapitalSecurity,GuaranteeCertificate,IGC,Warrant,SeniorNote,Debenture,ETF,ADR,ETC,ETN" );
            exchangeIds.ToList().ForEach( eid => request.AddParameter( "exchange[]", eid ) );
            request.AddParameter( "pn", pageNum );
            request.AddParameter( "order[col]", "viewData.symbol" );
            request.AddParameter( "order[dir]", "a" );

            var response = await client.ExecuteAsync( request );

            Debug.WriteLine( $"Exchange [{string.Join( ", ", exchangeIds )}] page {pageNum} was loaded by thread {Thread.CurrentThread.ManagedThreadId}." );

            int objCount = 0, totalCount = 0;

            if ( response.StatusCode == System.Net.HttpStatusCode.OK )
            {
                var pageData = JObject.Parse( response.Content );
                totalCount = (int)pageData["totalCount"];
                foreach ( var item in pageData["hits"] )
                {
                    objCount++;
                    try
                    {
                        var ticker = new InvestingEquity()
                        {
                            TickerId = long.Parse( item["pair_ID"].ToString() ),
                            Ticker = (string)item["stock_symbol"],
                            Name = (string)item["name_trans"],
                            ExchangeId = (int)item["exchange_ID"],
                            ExchangeName = (string)item["exchange_trans"],
                            IndustryName = (string)item["industry_trans"],
                            SectorName = (string)item["sector_trans"],
                            SecurityType = (string)item["security_type"],
                            Country = (string)item["viewData"]["flag"]
                        };
                        if ( !string.IsNullOrWhiteSpace( ticker.SectorName ) )
                            ticker.SectorId = (int)item["sector_id"];
                        if ( !string.IsNullOrWhiteSpace( ticker.IndustryName ) )
                            ticker.IndustryId = (int)item["industry_id"];

                        if ( dic.TryGetValue( ticker.Ticker, out var oldTicker ) )
                        {
                            if ( oldTicker.ExchangeId != 2 && ( ticker.ExchangeId < oldTicker.ExchangeId || ticker.ExchangeId == 2 ) )
                            {
                                Debug.WriteLine( $"Ticker {oldTicker} was replaced by {ticker}" );
                                dic[ticker.Ticker] = ticker;
                            }
                        }
                        else
                            dic[ticker.Ticker] = ticker;
                    }
                    catch ( Exception ex )
                    {
                        Debug.WriteLine( $"Ticker has error in data: {ex.Message}\r\nData: {item}" );
                    }
                }
            }
            else
            {
                Debug.WriteLine( $"There was an error while processing the request: {response.StatusCode} {response.StatusDescription}" );
            }
            return (totalCount, objCount);
        }
    }
}
