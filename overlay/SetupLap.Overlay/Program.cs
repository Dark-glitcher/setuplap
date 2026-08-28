using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using irsdkSharp;
using YamlDotNet.Serialization;

namespace SetupLapOverlay;

public sealed record DriverInfo(int CarIdx, string Name, string Number, string ClassName);
public sealed record CarRow(int CarIdx, int Position, int ClassPosition, int Lap, float LapPct, float EstTime, float BestLap, float LastLap, bool Pit, string Name, string Number, string ClassName, bool IsPlayer);
public sealed record RelativeRow(int CarIdx, string Name, string Number, string ClassName, double GapSeconds, bool IsPlayer);

public sealed class OverlaySnapshot
{
    public bool Connected { get; init; }
    public string TrackName { get; init; } = "Waiting for iRacing";
    public int PlayerCarIdx { get; init; } = -1;
    public float Throttle { get; init; }
    public float Brake { get; init; }
    public float Clutch { get; init; }
    public float Steering { get; init; }
    public float SpeedKph { get; init; }
    public int Gear { get; init; }
    public float AirTemp { get; init; }
    public float TrackTemp { get; init; }
    public float Humidity { get; init; }
    public float WindSpeed { get; init; }
    public float WindDir { get; init; }
    public float TrackWetness { get; init; }
    public IReadOnlyList<CarRow> Standings { get; init; } = [];
    public IReadOnlyList<RelativeRow> Relatives { get; init; } = [];
    public IReadOnlyList<Point> TrackPoints { get; init; } = [];
    public IReadOnlyList<(int carIdx,float lapPct,bool player)> MapCars { get; init; } = [];
}

public sealed class TrackLearner
{
    private const int Bins = 180;
    private readonly Point?[] _points = new Point?[Bins];
    private double _x, _y;
    private double _lastSessionTime = double.NaN;
    private float _lastLapPct = -1;

    public void Reset()
    {
        Array.Clear(_points);
        _x = _y = 0;
        _lastSessionTime = double.NaN;
        _lastLapPct = -1;
    }

    public void Update(double sessionTime, float lapPct, float speedMps, float yawNorth)
    {
        if (lapPct < 0 || lapPct > 1 || !double.IsFinite(sessionTime)) return;
        if (double.IsNaN(_lastSessionTime))
        {
            _lastSessionTime = sessionTime;
            _lastLapPct = lapPct;
            return;
        }

        var dt = Math.Clamp(sessionTime - _lastSessionTime, 0, 0.2);
        _lastSessionTime = sessionTime;
        if (_lastLapPct > 0.92f && lapPct < 0.08f) { _x = 0; _y = 0; }
        _x += speedMps * Math.Sin(yawNorth) * dt;
        _y -= speedMps * Math.Cos(yawNorth) * dt;
        _lastLapPct = lapPct;

        int bin = Math.Clamp((int)Math.Round(lapPct * (Bins - 1)), 0, Bins - 1);
        if (_points[bin] is null) _points[bin] = new Point(_x, _y);
        else
        {
            var old = _points[bin]!.Value;
            _points[bin] = new Point(old.X * 0.8 + _x * 0.2, old.Y * 0.8 + _y * 0.2);
        }
    }

    public IReadOnlyList<Point> GetNormalised()
    {
        var available = _points.Where(p=>p.HasValue).ToList();
        if (available.Count < 25)
            return Enumerable.Range(0, Bins).Select(i => { double a=i/(double)Bins*Math.PI*2; return new Point(Math.Cos(a)*0.45+0.5,Math.Sin(a)*0.34+0.5); }).ToList();

        var raw = new Point[Bins];
        for (int i=0;i<Bins;i++)
        {
            if (_points[i].HasValue) { raw[i] = _points[i]!.Value; continue; }
            int left=i,right=i;
            while(left>=0&&!_points[left].HasValue)left--;
            while(right<Bins&&!_points[right].HasValue)right++;
            if(left>=0&&right<Bins){double t=(i-left)/(double)(right-left);var a=_points[left]!.Value;var b=_points[right]!.Value;raw[i]=new Point(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
            else if(left>=0)raw[i]=_points[left]!.Value;else if(right<Bins)raw[i]=_points[right]!.Value;
        }
        double minX=raw.Min(p=>p.X),maxX=raw.Max(p=>p.X),minY=raw.Min(p=>p.Y),maxY=raw.Max(p=>p.Y);
        double w=Math.Max(1,maxX-minX),h=Math.Max(1,maxY-minY),scale=0.86/Math.Max(w,h),cx=(minX+maxX)/2,cy=(minY+maxY)/2;
        return raw.Select(p=>new Point((p.X-cx)*scale+0.5,(p.Y-cy)*scale+0.5)).ToList();
    }
}

public sealed class TelemetryService
{
    private readonly IRacingSDK _sdk = new();
    private readonly Dictionary<int,DriverInfo> _drivers = [];
    private readonly TrackLearner _track = new();
    private int _lastSessionUpdate = -1;
    private long _lastTick;
    private string _trackName = "Waiting for iRacing";
    public event Action<OverlaySnapshot>? Updated;

    public TelemetryService()
    {
        _sdk.OnConnected += () => _track.Reset();
        _sdk.OnDisconnected += () => Updated?.Invoke(new OverlaySnapshot());
        _sdk.OnDataChanged += OnData;
    }

    private static T Get<T>(IRacingSDK sdk,string name,T fallback=default!)
    {
        try{var v=sdk.GetData(name);if(v is T t)return t;if(v is null)return fallback;return (T)Convert.ChangeType(v,typeof(T),CultureInfo.InvariantCulture);}catch{return fallback;}
    }
    private static T[] Arr<T>(IRacingSDK sdk,string name){try{return sdk.GetData(name) as T[]??[];}catch{return[];}}

    private void OnData()
    {
        var now=Stopwatch.GetTimestamp();
        if(_lastTick!=0&&(now-_lastTick)/(double)Stopwatch.Frequency<0.05)return;
        _lastTick=now;if(!_sdk.IsConnected())return;
        try
        {
            int update=_sdk.Header?.SessionInfoUpdate??-1;
            if(update!=_lastSessionUpdate){_lastSessionUpdate=update;ParseSession(_sdk.GetSessionInfo());}
            int player=Get(_sdk,"PlayerCarIdx",-1);
            var lapPct=Arr<float>(_sdk,"CarIdxLapDistPct");var pos=Arr<int>(_sdk,"CarIdxPosition");var classPos=Arr<int>(_sdk,"CarIdxClassPosition");var laps=Arr<int>(_sdk,"CarIdxLap");var est=Arr<float>(_sdk,"CarIdxEstTime");var best=Arr<float>(_sdk,"CarIdxBestLapTime");var last=Arr<float>(_sdk,"CarIdxLastLapTime");var pit=Arr<bool>(_sdk,"CarIdxOnPitRoad");
            float speed=Get(_sdk,"Speed",0f);float playerPct=player>=0&&player<lapPct.Length?lapPct[player]:-1;double st=Get(_sdk,"SessionTime",0d);float yaw=Get(_sdk,"YawNorth",Get(_sdk,"Yaw",0f));_track.Update(st,playerPct,speed,yaw);
            var cars=new List<CarRow>();int n=new[]{lapPct.Length,pos.Length,laps.Length}.DefaultIfEmpty(0).Max();
            for(int i=0;i<n;i++)
            {
                int p=i<pos.Length?pos[i]:0;float lp=i<lapPct.Length?lapPct[i]:-1;if(p<=0&&lp<0)continue;_drivers.TryGetValue(i,out var d);
                cars.Add(new CarRow(i,p,i<classPos.Length?classPos[i]:0,i<laps.Length?laps[i]:0,lp,i<est.Length?est[i]:0,i<best.Length?best[i]:0,i<last.Length?last[i]:0,i<pit.Length&&pit[i],d?.Name??$"Car {i}",d?.Number??"",d?.ClassName??"",i==player));
            }
            var standings=cars.Where(c=>c.Position>0).OrderBy(c=>c.Position).Take(20).ToList();
            var snap=new OverlaySnapshot{Connected=true,TrackName=_trackName,PlayerCarIdx=player,Throttle=Get(_sdk,"Throttle",0f),Brake=Get(_sdk,"Brake",0f),Clutch=Get(_sdk,"Clutch",0f),Steering=Get(_sdk,"SteeringWheelAngle",0f),SpeedKph=speed*3.6f,Gear=Get(_sdk,"Gear",0),AirTemp=Get(_sdk,"AirTemp",0f),TrackTemp=Get(_sdk,"TrackTempCrew",Get(_sdk,"TrackTemp",0f)),Humidity=Get(_sdk,"RelativeHumidity",0f),WindSpeed=Get(_sdk,"WindVel",0f),WindDir=Get(_sdk,"WindDir",0f),TrackWetness=Get(_sdk,"TrackWetness",0f),Standings=standings,Relatives=BuildRelatives(cars,player),TrackPoints=_track.GetNormalised(),MapCars=cars.Where(c=>c.LapPct>=0).Select(c=>(c.CarIdx,c.LapPct,c.IsPlayer)).ToList()};
            Updated?.Invoke(snap);
        }catch{}
    }

    private IReadOnlyList<RelativeRow> BuildRelatives(List<CarRow> cars,int playerIdx)
    {
        var me=cars.FirstOrDefault(c=>c.CarIdx==playerIdx);if(me is null)return[];
        double lapTime=me.BestLap>20?me.BestLap:cars.Where(c=>c.BestLap>20).Select(c=>(double)c.BestLap).DefaultIfEmpty(90).Min();
        var rows=new List<RelativeRow>{new(playerIdx,me.Name,me.Number,me.ClassName,0,true)};
        foreach(var c in cars.Where(c=>c.CarIdx!=playerIdx&&c.LapPct>=0)){double gap=c.EstTime-me.EstTime;if(gap>lapTime/2)gap-=lapTime;if(gap<-lapTime/2)gap+=lapTime;rows.Add(new(c.CarIdx,c.Name,c.Number,c.ClassName,gap,false));}
        var ahead=rows.Where(r=>!r.IsPlayer&&r.GapSeconds>0).OrderBy(r=>r.GapSeconds).Take(3).OrderByDescending(r=>r.GapSeconds);
        var behind=rows.Where(r=>!r.IsPlayer&&r.GapSeconds<0).OrderByDescending(r=>r.GapSeconds).Take(3).OrderByDescending(r=>r.GapSeconds);
        return ahead.Concat(rows.Where(r=>r.IsPlayer)).Concat(behind).ToList();
    }

    private void ParseSession(string? yaml)
    {
        if(string.IsNullOrWhiteSpace(yaml))return;
        try
        {
            var des=new DeserializerBuilder().IgnoreUnmatchedProperties().Build();var root=des.Deserialize<Dictionary<object,object>>(yaml);_drivers.Clear();
            if(root.TryGetValue("WeekendInfo",out var wi)&&wi is Dictionary<object,object>w)_trackName=Str(w,"TrackDisplayName",Str(w,"TrackName","iRacing"));
            if(root.TryGetValue("DriverInfo",out var di)&&di is Dictionary<object,object>d&&d.TryGetValue("Drivers",out var ds)&&ds is List<object>list)
                foreach(var obj in list){if(obj is not Dictionary<object,object>m)continue;int idx=Int(m,"CarIdx",-1);if(idx<0)continue;_drivers[idx]=new DriverInfo(idx,Str(m,"UserName",$"Car {idx}"),Str(m,"CarNumber",""),Str(m,"CarClassShortName",""));}
        }catch{}
    }
    private static string Str(Dictionary<object,object>m,string k,string f)=>m.TryGetValue(k,out var v)&&v is not null?v.ToString()??f:f;
    private static int Int(Dictionary<object,object>m,string k,int f)=>m.TryGetValue(k,out var v)&&int.TryParse(v?.ToString(),out var x)?x:f;
}

public static class Theme
{
    public static readonly Brush Panel=new SolidColorBrush(Color.FromArgb(235,16,22,30));public static readonly Brush Line=new SolidColorBrush(Color.FromArgb(65,255,255,255));public static readonly Brush Text=new SolidColorBrush(Color.FromRgb(242,245,248));public static readonly Brush Muted=new SolidColorBrush(Color.FromRgb(151,163,177));public static readonly Brush Orange=new SolidColorBrush(Color.FromRgb(255,77,23));public static readonly Brush Green=new SolidColorBrush(Color.FromRgb(140,255,0));public static readonly Brush Blue=new SolidColorBrush(Color.FromRgb(22,140,255));public static readonly Brush Red=new SolidColorBrush(Color.FromRgb(255,70,70));
}

public abstract class WidgetWindow:Window
{
    protected readonly Border Frame;protected readonly Grid RootGrid;protected readonly TextBlock HeaderText;private bool _edit;
    protected WidgetWindow(string title,double width,double height,double left,double top)
    {
        Title=title;Width=width;Height=height;Left=left;Top=top;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ResizeMode=ResizeMode.NoResize;
        RootGrid=new Grid();RootGrid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(34)});RootGrid.RowDefinitions.Add(new RowDefinition());
        HeaderText=new TextBlock{Text=title.ToUpperInvariant(),Foreground=Theme.Text,FontSize=12,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,8,0)};
        var header=new Grid{Background=new SolidColorBrush(Color.FromArgb(150,25,30,38))};header.Children.Add(HeaderText);header.Children.Add(new Border{Width=3,Background=Theme.Orange,HorizontalAlignment=HorizontalAlignment.Left});Grid.SetRow(header,0);RootGrid.Children.Add(header);
        Frame=new Border{Background=Theme.Panel,BorderBrush=Theme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(7),Child=RootGrid};Content=Frame;
        MouseLeftButtonDown+=(s,e)=>{if(_edit&&e.ButtonState==MouseButtonState.Pressed)try{DragMove();}catch{}};Loaded+=(s,e)=>ApplyClickThrough(!_edit);
    }
    protected void SetBody(UIElement el){Grid.SetRow(el,1);RootGrid.Children.Add(el);}public void SetEdit(bool e){_edit=e;ApplyClickThrough(!e);Frame.BorderBrush=e?Theme.Orange:Theme.Line;}
    private void ApplyClickThrough(bool yes){if(!IsLoaded)return;var hwnd=new WindowInteropHelper(this).Handle;int ex=GetWindowLong(hwnd,-20);if(yes)ex|=0x20;else ex&=~0x20;SetWindowLong(hwnd,-20,ex);}
    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr hWnd,int nIndex);[DllImport("user32.dll")]static extern int SetWindowLong(IntPtr hWnd,int nIndex,int dwNewLong);public abstract void Update(OverlaySnapshot s);
}

public sealed class StandingsWindow:WidgetWindow
{
    readonly StackPanel _rows=new();public StandingsWindow():base("Standings",390,430,18,90){SetBody(new ScrollViewer{Content=_rows,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden});}
    public override void Update(OverlaySnapshot s){_rows.Children.Clear();if(!s.Connected){_rows.Children.Add(P("WAITING FOR IRACING"));return;}foreach(var c in s.Standings){var g=new Grid{Height=28,Background=c.IsPlayer?new SolidColorBrush(Color.FromArgb(48,255,77,23)):Brushes.Transparent,Margin=new Thickness(4,1,4,0)};g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(34)});g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(42)});g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(62)});Add(g,new TextBlock{Text=c.Position.ToString(),Foreground=c.IsPlayer?Theme.Orange:Theme.Text,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center},0);Add(g,new TextBlock{Text=c.Number,Foreground=Theme.Muted,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center},1);Add(g,new TextBlock{Text=c.Name,Foreground=Theme.Text,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis},2);Add(g,new TextBlock{Text=c.Pit?"PIT":Fmt(c.LastLap),Foreground=c.Pit?Theme.Green:Theme.Muted,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,0,8,0)},3);_rows.Children.Add(g);}}
    static void Add(Grid g,UIElement e,int c){Grid.SetColumn(e,c);g.Children.Add(e);}static string Fmt(float s)=>s>0?$"{(int)(s/60)}:{s%60:00.000}":"—";static TextBlock P(string t)=>new(){Text=t,Foreground=Theme.Muted,Margin=new Thickness(14),FontSize=12};
}

public sealed class RelativeWindow:WidgetWindow
{
    readonly StackPanel _rows=new();public RelativeWindow():base("Relatives",360,260,18,540){SetBody(_rows);}public override void Update(OverlaySnapshot s){_rows.Children.Clear();if(!s.Connected){_rows.Children.Add(new TextBlock{Text="WAITING FOR IRACING",Foreground=Theme.Muted,Margin=new Thickness(14)});return;}foreach(var r in s.Relatives){var g=new Grid{Height=30,Margin=new Thickness(4,1,4,0),Background=r.IsPlayer?new SolidColorBrush(Color.FromArgb(55,255,77,23)):Brushes.Transparent};g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(48)});g.ColumnDefinitions.Add(new ColumnDefinition());g.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(82)});var num=new TextBlock{Text=r.Number,Foreground=r.IsPlayer?Theme.Orange:Theme.Muted,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Center};var name=new TextBlock{Text=r.Name,Foreground=Theme.Text,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};var gap=new TextBlock{Text=r.IsPlayer?"YOU":$"{r.GapSeconds:+0.00;-0.00}s",Foreground=r.GapSeconds>=0?Theme.Green:Theme.Blue,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,0,10,0)};Grid.SetColumn(num,0);Grid.SetColumn(name,1);Grid.SetColumn(gap,2);g.Children.Add(num);g.Children.Add(name);g.Children.Add(gap);_rows.Children.Add(g);}}
}

public sealed class WeatherWindow:WidgetWindow
{
    readonly Grid _grid=new();readonly TextBlock[] _vals=new TextBlock[6];readonly string[] _labels=["AIR","TRACK","HUMIDITY","WIND","WETNESS","CIRCUIT"];
    public WeatherWindow():base("Weather",310,225,SystemParameters.PrimaryScreenWidth-330,90){for(int i=0;i<6;i++)_grid.RowDefinitions.Add(new RowDefinition());_grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(100)});_grid.ColumnDefinitions.Add(new ColumnDefinition());for(int i=0;i<6;i++){var l=new TextBlock{Text=_labels[i],Foreground=Theme.Muted,FontSize=10,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,0,0)};var v=_vals[i]=new TextBlock{Text="—",Foreground=Theme.Text,FontSize=13,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,0,12,0)};Grid.SetRow(l,i);Grid.SetRow(v,i);Grid.SetColumn(v,1);_grid.Children.Add(l);_grid.Children.Add(v);}SetBody(_grid);}public override void Update(OverlaySnapshot s){_vals[0].Text=s.Connected?$"{s.AirTemp:0.0} °C":"—";_vals[1].Text=s.Connected?$"{s.TrackTemp:0.0} °C":"—";_vals[2].Text=s.Connected?$"{s.Humidity*100:0}%":"—";_vals[3].Text=s.Connected?$"{s.WindSpeed:0.0} m/s":"—";_vals[4].Text=s.Connected?Wet(s.TrackWetness):"—";_vals[5].Text=s.Connected?s.TrackName:"Waiting";}static string Wet(float x)=>x switch{<=0=>"Dry",<2=>"Damp",<5=>"Wet",_=>"Very wet"};
}

public sealed class InputsWindow:WidgetWindow
{
    readonly Canvas _canvas=new();readonly Queue<float> _t=new(),_b=new(),_c=new();readonly TextBlock _speed=new(){Foreground=Theme.Text,FontSize=22,FontWeight=FontWeights.Bold};readonly TextBlock _gear=new(){Foreground=Theme.Orange,FontSize=28,FontWeight=FontWeights.Bold};
    public InputsWindow():base("Inputs",330,250,SystemParameters.PrimaryScreenWidth-350,SystemParameters.PrimaryScreenHeight-300){var g=new Grid();g.RowDefinitions.Add(new RowDefinition{Height=new GridLength(48)});g.RowDefinitions.Add(new RowDefinition());var top=new Grid{Margin=new Thickness(12,6,12,0)};top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(60)});top.Children.Add(_speed);Grid.SetColumn(_gear,1);top.Children.Add(_gear);Grid.SetRow(top,0);g.Children.Add(top);Grid.SetRow(_canvas,1);g.Children.Add(_canvas);SetBody(g);_canvas.SizeChanged+=(s,e)=>Redraw();}
    public override void Update(OverlaySnapshot s){Push(_t,s.Throttle);Push(_b,s.Brake);Push(_c,s.Clutch);_speed.Text=s.Connected?$"{s.SpeedKph:0} km/h":"— km/h";_gear.Text=s.Connected?(s.Gear<0?"R":s.Gear==0?"N":s.Gear.ToString()):"—";Redraw();}
    void Push(Queue<float>q,float v){q.Enqueue(Math.Clamp(v,0,1));while(q.Count>90)q.Dequeue();}void Redraw(){double w=_canvas.ActualWidth,h=_canvas.ActualHeight;if(w<20||h<20)return;_canvas.Children.Clear();Draw(_t,Theme.Orange,w,h);Draw(_b,Theme.Red,w,h);Draw(_c,Theme.Blue,w,h);Legend("THR",Theme.Orange,8);Legend("BRK",Theme.Red,52);Legend("CLU",Theme.Blue,96);}void Draw(IEnumerable<float>q,Brush brush,double w,double h){var a=q.ToArray();if(a.Length<2)return;var p=new Polyline{Stroke=brush,StrokeThickness=2};for(int i=0;i<a.Length;i++)p.Points.Add(new Point(i/(double)89*w,h-8-a[i]*(h-34)));_canvas.Children.Add(p);}void Legend(string s,Brush b,double x){var t=new TextBlock{Text=s,Foreground=b,FontSize=9,FontWeight=FontWeights.Bold};Canvas.SetLeft(t,x);Canvas.SetTop(t,5);_canvas.Children.Add(t);}
}

public sealed class TrackMapWindow:WidgetWindow
{
    readonly Canvas _canvas=new();OverlaySnapshot? _last;public TrackMapWindow():base("Track Map",330,330,(SystemParameters.PrimaryScreenWidth-330)/2,90){SetBody(_canvas);_canvas.SizeChanged+=(s,e)=>Draw();}public override void Update(OverlaySnapshot s){_last=s;HeaderText.Text=$"TRACK MAP · {s.TrackName}".ToUpperInvariant();Draw();}
    void Draw(){if(_last is null)return;double w=_canvas.ActualWidth,h=_canvas.ActualHeight;if(w<30||h<30)return;_canvas.Children.Clear();var pts=_last.TrackPoints;if(pts.Count<2)return;var line=new Polyline{Stroke=new SolidColorBrush(Color.FromRgb(120,130,142)),StrokeThickness=7,StrokeLineJoin=PenLineJoin.Round};foreach(var p in pts)line.Points.Add(new Point(p.X*w,p.Y*h));line.Points.Add(new Point(pts[0].X*w,pts[0].Y*h));_canvas.Children.Add(line);var inner=new Polyline{Stroke=new SolidColorBrush(Color.FromRgb(28,34,42)),StrokeThickness=3,StrokeLineJoin=PenLineJoin.Round};foreach(var p in line.Points)inner.Points.Add(p);_canvas.Children.Add(inner);foreach(var c in _last.MapCars){int idx=Math.Clamp((int)Math.Round(c.lapPct*(pts.Count-1)),0,pts.Count-1);var p=pts[idx];var dot=new Ellipse{Width=c.player?12:7,Height=c.player?12:7,Fill=c.player?Theme.Orange:Theme.Text,Stroke=c.player?Theme.Green:null,StrokeThickness=c.player?1.5:0};Canvas.SetLeft(dot,p.X*w-dot.Width/2);Canvas.SetTop(dot,p.Y*h-dot.Height/2);_canvas.Children.Add(dot);}var note=new TextBlock{Text="Track shape learns from your driving",Foreground=Theme.Muted,FontSize=9};Canvas.SetLeft(note,8);Canvas.SetBottom(note,6);_canvas.Children.Add(note);}
}

public sealed class AppSettings{public bool EditMode{get;set;}public Dictionary<string,double[]>Windows{get;set;}=[];}

public sealed class ControlWindow:Window
{
    readonly TelemetryService _service;readonly List<WidgetWindow> _widgets;readonly TextBlock _status;readonly Button _edit;AppSettings _settings;
    public ControlWindow(TelemetryService service,List<WidgetWindow>widgets){_service=service;_widgets=widgets;_settings=Load();Title="SetupLap Overlay";Width=390;Height=500;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=new SolidColorBrush(Color.FromRgb(10,14,20));Foreground=Theme.Text;var root=new StackPanel{Margin=new Thickness(22)};root.Children.Add(new TextBlock{FontSize=30,FontWeight=FontWeights.Black,Text="SETUPLAP",Foreground=Theme.Text});root.Children.Add(new TextBlock{Text="iRacing Overlay · v0.1 test build",Foreground=Theme.Muted,Margin=new Thickness(0,0,0,18)});_status=new TextBlock{Text="● WAITING FOR IRACING",Foreground=Theme.Red,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,18)};root.Children.Add(_status);root.Children.Add(new TextBlock{Text="WIDGETS",Foreground=Theme.Orange,FontSize=10,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,6)});foreach(var w in _widgets){var cb=new CheckBox{Content=w.Title,IsChecked=true,Margin=new Thickness(0,5,0,5),Foreground=Theme.Text};cb.Checked+=(s,e)=>w.Show();cb.Unchecked+=(s,e)=>w.Hide();root.Children.Add(cb);}_edit=new Button{Content="EDIT LAYOUT",Height=38,Margin=new Thickness(0,18,0,8),Background=Theme.Orange,Foreground=Brushes.White,BorderThickness=new Thickness(0)};_edit.Click+=(s,e)=>SetEdit(!_settings.EditMode);root.Children.Add(_edit);root.Children.Add(new TextBlock{Text="Edit mode lets you drag widgets. Lock mode makes them click-through so they do not interfere with iRacing.",TextWrapping=TextWrapping.Wrap,Foreground=Theme.Muted,FontSize=11,Margin=new Thickness(0,0,0,16)});var reset=new Button{Content="RESET WIDGET POSITIONS",Height=34,Background=new SolidColorBrush(Color.FromRgb(25,30,38)),Foreground=Theme.Text,BorderBrush=Theme.Line};reset.Click+=(s,e)=>ResetPositions();root.Children.Add(reset);root.Children.Add(new TextBlock{Text="Tip: run iRacing in Borderless or Windowed mode for overlays to appear above the sim.",TextWrapping=TextWrapping.Wrap,Foreground=Theme.Blue,FontSize=11,Margin=new Thickness(0,18,0,0)});Content=root;Closing+=(s,e)=>{Save();Application.Current.Shutdown();};ApplySavedPositions();SetEdit(_settings.EditMode);_service.Updated+=s=>Dispatcher.BeginInvoke(()=>OnSnapshot(s));foreach(var w in _widgets)w.LocationChanged+=(s,e)=>Save();}
    void OnSnapshot(OverlaySnapshot s){_status.Text=s.Connected?$"● LIVE · {s.TrackName}":"● WAITING FOR IRACING";_status.Foreground=s.Connected?Theme.Green:Theme.Red;foreach(var w in _widgets)if(w.IsVisible)w.Update(s);}void SetEdit(bool e){_settings.EditMode=e;foreach(var w in _widgets)w.SetEdit(e);_edit.Content=e?"LOCK LAYOUT":"EDIT LAYOUT";_edit.Background=e?Theme.Green:Theme.Orange;Save();}
    void ResetPositions(){double sw=SystemParameters.PrimaryScreenWidth,sh=SystemParameters.PrimaryScreenHeight;var p=new[]{(18d,90d),(18d,540d),(sw-330,90d),(sw-350,sh-300d),((sw-330)/2,90d)};for(int i=0;i<_widgets.Count;i++){_widgets[i].Left=p[i].Item1;_widgets[i].Top=p[i].Item2;}Save();}
    void ApplySavedPositions(){foreach(var w in _widgets)if(_settings.Windows.TryGetValue(w.Title,out var p)&&p.Length>=2){w.Left=p[0];w.Top=p[1];}}
    string SettingsPath{get{var d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SetupLap");Directory.CreateDirectory(d);return Path.Combine(d,"overlay-settings.json");}}AppSettings Load(){try{return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))??new();}catch{return new();}}void Save(){try{foreach(var w in _widgets)_settings.Windows[w.Title]=[w.Left,w.Top];File.WriteAllText(SettingsPath,JsonSerializer.Serialize(_settings,new JsonSerializerOptions{WriteIndented=true}));}catch{}}
}

public static class Program
{
    [STAThread]public static void Main(){var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var service=new TelemetryService();var widgets=new List<WidgetWindow>{new StandingsWindow(),new RelativeWindow(),new WeatherWindow(),new InputsWindow(),new TrackMapWindow()};foreach(var w in widgets)w.Show();var control=new ControlWindow(service,widgets);control.Show();app.Run();}
}
