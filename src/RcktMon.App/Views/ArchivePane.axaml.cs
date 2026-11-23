using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using RcktMon.App.Localization;
using RcktMon.Client.Core.Studio;

namespace RcktMon.App.Views;
public sealed partial class ArchivePane: UserControl
{
    private static string L(string s)=>Localizer.Instance["History_"+s];
    private CancellationTokenSource? _life;
    private StudioClient? _client;
    private ArchiveRun[] _runs=[];
    private string? _rendered,_signature;
    private bool _sending;
    private readonly Func<StudioClient> _make;
    public ArchivePane():this(()=>new StudioClient()){}
    public ArchivePane(Func<StudioClient> make){_make=make;InitializeComponent();}
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e){base.OnAttachedToVisualTree(e);_life=new();_client=_make();_=Poll(_client,_life.Token);}
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e){_life?.Cancel();_life?.Dispose();_client?.Dispose();_client=null;_life=null;_rendered=_signature=null;base.OnDetachedFromVisualTree(e);}
    private async Task Poll(StudioClient client,CancellationToken ct)
    {
        bool loaded=false;
        while(!ct.IsCancellationRequested){
            try{
                if(!loaded){var cat=await client.ArchiveCatalogAsync(ct);ct.ThrowIfCancellationRequested();
                    Coverage.Text=$"{L("Coverage")}: {cat.First} — {cat.Last} · UTC+8";
                    FromBox.Text=string.IsNullOrWhiteSpace(FromBox.Text)?cat.First:FromBox.Text;ToBox.Text=string.IsNullOrWhiteSpace(ToBox.Text)?cat.Last:ToBox.Text;
                    string chosen=Symbol.SelectedItem as string??"*";Symbol.ItemsSource=new[]{"*"}.Concat(cat.Tickers.Select(t=>t.Ticker)).ToArray();Symbol.SelectedItem=chosen;loaded=true;}
                var runs=await client.ArchiveRunsAsync(ct);ct.ThrowIfCancellationRequested();_runs=runs;
                string sig=string.Join("|",runs.Select(r=>r.Id+r.State));if(sig!=_signature){
                    var old=(Runs.SelectedItem as Item)?.Id;_signature=sig;var items=runs.Select(r=>new Item(r.Id,$"{r.Query.Ticker} · {r.Query.From} — {r.Query.Through} · {r.Query.Rise}% · {r.Id[..6]}")).ToArray();
                    Runs.ItemsSource=items;Runs.SelectedItem=items.FirstOrDefault(x=>x.Id==old)??items.FirstOrDefault();}
                Start.IsEnabled=!_sending&&!runs.Any(r=>r.State is "books" or "ticks");
                await Display(client,ct);
            }catch(OperationCanceledException) when(ct.IsCancellationRequested){return;}
            catch(Exception ex) when(ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or ObjectDisposedException){if(!ct.IsCancellationRequested){Hint.Text=L("Unavailable");Start.IsEnabled=false;} }
            try{await Task.Delay(2400,ct);}catch(OperationCanceledException){return;}
        }
    }
    private sealed record Item(string Id,string Label){public override string ToString()=>Label;}
    private async Task Display(StudioClient c,CancellationToken ct)
    {
        var id=(Runs.SelectedItem as Item)?.Id;var r=_runs.FirstOrDefault(x=>x.Id==id);
        if(r==null){Hint.Text=L("NoRuns");Rows.ItemsSource=null;return;}
        Hint.Text=r.Message+(r.Total>0?$" · {r.Done}/{r.Total}":"");
        if(r.State!="complete"){_rendered=null;Rows.ItemsSource=null;return;}
        if(_rendered==id)return;
        var data=await c.ArchiveResultAsync(r.Id,ct);ct.ThrowIfCancellationRequested();if((Runs.SelectedItem as Item)?.Id!=id)return;_rendered=id;
        Rows.ItemsSource=data.Groups.Select(g=>{
            var label=L(g.Exit switch{"3rd_red_1m"=>"Rule1","1st_red_3m"=>"Rule2",_=>"Rule3"});
            var net=decimal.TryParse(g.Net,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n.ToString("+0.00;-0.00;0.00",CultureInfo.CurrentCulture):"—";
            return $"{label} · {g.Floor:N0} USDT     {net} USDT\n{L("Signals")}: {g.Signals} · {L("Closed")}: {g.Closed} · {L("Wins")}: {g.Wins} · {L("Unknown")}: {g.Unknown}";}).ToArray();
    }
    private void SelectRun(object? sender,SelectionChangedEventArgs e){_rendered=null;}
    private async void RunClick(object? sender,RoutedEventArgs e)
    {
        if(_sending||_client==null||_life==null)return;
        static bool N(string? text,out decimal v)=>decimal.TryParse(text?.Replace(',','.'),NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out v);
        if(!N(Low.Text,out var a)||!N(High.Text,out var b)||!N(Rise.Text,out var m)||!N(Nominal.Text,out var n)||a>=b ||
           !DateOnly.TryParseExact(FromBox.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var st)||
           !DateOnly.TryParseExact(ToBox.Text,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var en)||en<st){Hint.Text=L("Invalid");return;}
        _sending=true;Start.IsEnabled=false;
        try{await _client.StartArchiveAsync(new(FromBox.Text!,ToBox.Text!,Symbol.SelectedItem as string??"*",m,n,a,b),_life.Token);Runs.SelectedIndex=-1;_signature=null;}
        catch(HttpRequestException){Hint.Text=L("Invalid");}catch(OperationCanceledException){}finally{_sending=false;}
    }
    private void DetailsClick(object? sender,RoutedEventArgs e){try{var id=(Runs.SelectedItem as Item)?.Id;string suffix=Guid.TryParseExact(id,"N",out _)?"?run="+id:"";Process.Start(new ProcessStartInfo("http://127.0.0.1:47831/history/index.html"+suffix){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){Hint.Text=L("Unavailable");}}
}
