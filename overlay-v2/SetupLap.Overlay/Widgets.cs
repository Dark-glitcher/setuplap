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

public abstract class WidgetWindow:Window
{
    protected readonly Grid Body=new();
    protected readonly TextBlock TitleText;
    readonly Border _frame;
    bool _edit;

    protected WidgetWindow(string title,double width,double height,double left,double top)
    {
        Title=title;Width=width;Height=height;Left=left;Top=top;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ResizeMode=ResizeMode.NoResize;
        var root=new Grid();root.RowDefinitions.Add(new RowDefinition{Height=new GridLength(28)});root.RowDefinitions.Add(new RowDefinition());
        var head=new Grid{Background=Theme.Header};
        head.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(3)});head.ColumnDefinitions.Add(new ColumnDefinition());
        head.Children.Add(new Border{Background=Theme.Orange,CornerRadius=new CornerRadius(10,0,0,0)});
        TitleText=new TextBlock{Text=title.ToUpperInvariant(),Foreground=Theme.Text,FontSize=10,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(9,0,6,0)};
        Grid.SetColumn(TitleText,1);head.Children.Add(TitleText);
        root.Children.Add(head);Grid.SetRow(Body,1);root.Children.Add(Body);
        _frame=new Border{Background=Theme.Panel,BorderBrush=Theme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),ClipToBounds=true,Child=root};
        Content=_frame;
        MouseLeftButtonDown+=(s,e)=>{if(_edit&&e.ButtonState==MouseButtonState.Pressed)try{DragMove();}catch{}};
        Loaded+=(s,e)=>SetClickThrough(!_edit);
    }
    public void SetEdit(bool edit){_edit=edit;_frame.BorderBrush=edit?Theme.Orange:Theme.Line;SetClickThrough(!edit);}
    void SetClickThrough(bool yes)
    {
        if(!IsLoaded)return;var hwnd=new WindowInteropHelper(this).Handle;int ex=GetWindowLong(hwnd,-20);
        if(yes)ex|=0x20;else ex&=~0x20;SetWindowLong(hwnd,-20,ex);
    }
    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr hWnd,int nIndex);
    [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr hWnd,int nIndex,int dwNewLong);
    public abstract void Update(OverlaySnapshot s);
}

public sealed class StandingsWindow:WidgetWindow
{
    readonly StackPanel _rows=new();
    public StandingsWindow():base("Standings",520,390,12,85){Body.Children.Add(new ScrollViewer{Content=_rows,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden});}
    public override void Update(OverlaySnapshot s)
    {
        _rows.Children.Clear();if(!s.Connected){_rows.Children.Add(Msg("WAITING FOR IRACING"));return;}
        foreach(var c in s.Standings)
        {
            var g=Row(c.IsPlayer?new SolidColorBrush(Color.FromArgb(45,255,77,23)):Brushes.Transparent);
            Add(g,Txt(c.Position.ToString(),c.IsPlayer?Theme.Orange:Theme.Text,true,TextAlignment.Center),0);
            Add(g,Txt(c.ClassPosition>0?$"C{c.ClassPosition}":"—",c.ClassBrush,true,TextAlignment.Center),1);
            Add(g,Txt(c.Number,Theme.Muted,false,TextAlignment.Center),2);
            Add(g,Txt(c.Name,Theme.Text,c.IsPlayer,TextAlignment.Left),3);
            Add(g,License(c.License),4);
            Add(g,Txt(IR(c.IRating),Theme.Text,true,TextAlignment.Right),5);
            Add(g,Txt(c.Pit?"PIT":Fmt(c.LastLap),c.Pit?Theme.Green:Theme.Muted,false,TextAlignment.Right),6);
            _rows.Children.Add(g);
        }
    }
    static Grid Row(Brush bg){var g=new Grid{Height=26,Background=bg,Margin=new Thickness(3,0,3,1)};double[]w=[30,40,40,1,58,54,66];foreach(var x in w)g.ColumnDefinitions.Add(new ColumnDefinition{Width=x==1?new GridLength(1,GridUnitType.Star):new GridLength(x)});return g;}
    static TextBlock Txt(string t,Brush b,bool bold,TextAlignment a)=>new(){Text=t,Foreground=b,FontSize=10,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center,TextAlignment=a,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(4,0,4,0)};
    static Border License(string l)=>new(){Background=LicenseBrush(l),CornerRadius=new CornerRadius(3),Margin=new Thickness(5,5,5,5),Child=new TextBlock{Text=string.IsNullOrWhiteSpace(l)?"—":l,Foreground=Brushes.White,FontSize=9,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
    static Brush LicenseBrush(string l)=>l.StartsWith("A")?new SolidColorBrush(Color.FromRgb(28,120,220)):l.StartsWith("B")?new SolidColorBrush(Color.FromRgb(35,175,75)):l.StartsWith("C")?new SolidColorBrush(Color.FromRgb(225,170,35)):l.StartsWith("D")?new SolidColorBrush(Color.FromRgb(220,90,30)):new SolidColorBrush(Color.FromRgb(100,105,115));
    static void Add(Grid g,UIElement e,int c){Grid.SetColumn(e,c);g.Children.Add(e);}
    static string IR(int n)=>n>=1000?$"{n/1000d:0.0}k":n>0?n.ToString():"—";
    static string Fmt(float s)=>s>0?$"{(int)(s/60)}:{s%60:00.000}":"—";
    static TextBlock Msg(string t)=>new(){Text=t,Foreground=Theme.Muted,Margin=new Thickness(12),FontSize=10};
}

public sealed class RelativeWindow:WidgetWindow
{
    readonly StackPanel _rows=new();
    public RelativeWindow():base("Relatives",480,305,SystemParameters.PrimaryScreenWidth-492,SystemParameters.PrimaryScreenHeight-360){Body.Children.Add(_rows);}
    public override void Update(OverlaySnapshot s)
    {
        _rows.Children.Clear();if(!s.Connected){_rows.Children.Add(new TextBlock{Text="WAITING FOR IRACING",Foreground=Theme.Muted,Margin=new Thickness(12)});return;}
        foreach(var r in s.Relatives)
        {
            var g=new Grid{Height=25,Background=r.IsPlayer?new SolidColorBrush(Color.FromArgb(50,255,77,23)):Brushes.Transparent,Margin=new Thickness(3,0,3,1)};
            double[]w=[38,40,1,56,58,68];foreach(var x in w)g.ColumnDefinitions.Add(new ColumnDefinition{Width=x==1?new GridLength(1,GridUnitType.Star):new GridLength(x)});
            Add(g,T(r.Number,Theme.Muted,false,TextAlignment.Center),0);Add(g,T(r.ClassName,r.ClassBrush,true,TextAlignment.Center),1);Add(g,T(r.Name,Theme.Text,r.IsPlayer,TextAlignment.Left),2);Add(g,T(r.License,Theme.Muted,false,TextAlignment.Center),3);Add(g,T(r.IRating>0?$"{r.IRating/1000d:0.0}k":"—",Theme.Text,true,TextAlignment.Right),4);Add(g,T(r.IsPlayer?"YOU":$"{r.GapSeconds:+0.00;-0.00}",r.IsPlayer?Theme.Green:r.GapSeconds>0?Theme.Green:Theme.Blue,true,TextAlignment.Right),5);_rows.Children.Add(g);
        }
    }
    static TextBlock T(string t,Brush b,bool bold,TextAlignment a)=>new(){Text=t,Foreground=b,FontSize=10,FontWeight=bold?FontWeights.Bold:FontWeights.Normal,VerticalAlignment=VerticalAlignment.Center,TextAlignment=a,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(3,0,3,0)};
    static void Add(Grid g,UIElement e,int c){Grid.SetColumn(e,c);g.Children.Add(e);}
}

public sealed class WeatherWindow:WidgetWindow
{
    readonly TextBlock[]_v=new TextBlock[5];
    public WeatherWindow():base("Weather",235,165,SystemParameters.PrimaryScreenWidth-247,50)
    {
        for(int i=0;i<5;i++)Body.RowDefinitions.Add(new RowDefinition());Body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(85)});Body.ColumnDefinitions.Add(new ColumnDefinition());string[]lab=["AIR","TRACK","HUMIDITY","WIND","WETNESS"];
        for(int i=0;i<5;i++){var l=new TextBlock{Text=lab[i],Foreground=Theme.Muted,FontSize=9,FontWeight=FontWeights.Bold,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(10,0,0,0)};var v=_v[i]=new TextBlock{Foreground=Theme.Text,FontSize=10,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextAlignment=TextAlignment.Right,Margin=new Thickness(0,0,10,0)};Grid.SetRow(l,i);Grid.SetRow(v,i);Grid.SetColumn(v,1);Body.Children.Add(l);Body.Children.Add(v);}
    }
    public override void Update(OverlaySnapshot s){_v[0].Text=s.Connected?$"{s.AirTemp:0.0}°C":"—";_v[1].Text=s.Connected?$"{s.TrackTemp:0.0}°C":"—";_v[2].Text=s.Connected?$"{s.Humidity*100:0}%":"—";_v[3].Text=s.Connected?$"{s.WindSpeed:0.0} m/s":"—";_v[4].Text=s.Connected?(s.TrackWetness<=0?"Dry":s.TrackWetness<2?"Damp":s.TrackWetness<5?"Wet":"Very wet"):"—";}
}

public sealed class InputsWindow:WidgetWindow
{
    readonly Canvas _c=new();readonly Queue<float>_t=new(),_b=new(),_cl=new();readonly TextBlock _speed=new(){Foreground=Theme.Text,FontSize=18,FontWeight=FontWeights.Bold};readonly TextBlock _gear=new(){Foreground=Theme.Orange,FontSize=22,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Right};
    public InputsWindow():base("Inputs",280,190,(SystemParameters.PrimaryScreenWidth-280)/2,SystemParameters.PrimaryScreenHeight-245){Body.RowDefinitions.Add(new RowDefinition{Height=new GridLength(38)});Body.RowDefinitions.Add(new RowDefinition());var top=new Grid{Margin=new Thickness(10,4,10,0)};top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(44)});top.Children.Add(_speed);Grid.SetColumn(_gear,1);top.Children.Add(_gear);Body.Children.Add(top);Grid.SetRow(_c,1);Body.Children.Add(_c);_c.SizeChanged+=(s,e)=>Draw();}
    public override void Update(OverlaySnapshot s){Push(_t,s.Throttle);Push(_b,s.Brake);Push(_cl,s.Clutch);_speed.Text=s.Connected?$"{s.SpeedKph:0} km/h":"—";_gear.Text=s.Connected?(s.Gear<0?"R":s.Gear==0?"N":s.Gear.ToString()):"—";Draw();}
    static void Push(Queue<float>q,float v){q.Enqueue(Math.Clamp(v,0,1));while(q.Count>80)q.Dequeue();}void Draw(){double w=_c.ActualWidth,h=_c.ActualHeight;if(w<20||h<20)return;_c.Children.Clear();Curve(_t,Theme.Orange,w,h);Curve(_b,Theme.Red,w,h);Curve(_cl,Theme.Blue,w,h);}void Curve(IEnumerable<float>q,Brush b,double w,double h){var a=q.ToArray();if(a.Length<2)return;var p=new Polyline{Stroke=b,StrokeThickness=2};for(int i=0;i<a.Length;i++)p.Points.Add(new Point(i/(double)79*w,h-5-a[i]*(h-12)));_c.Children.Add(p);}
}

public sealed class TrackMapWindow:WidgetWindow
{
    readonly Canvas _c=new();OverlaySnapshot? _s;public TrackMapWindow():base("Track Map",330,310,SystemParameters.PrimaryScreenWidth-342,225){Body.Children.Add(_c);_c.SizeChanged+=(s,e)=>Draw();}public override void Update(OverlaySnapshot s){_s=s;TitleText.Text=$"TRACK MAP · {s.TrackName}{(string.IsNullOrWhiteSpace(s.TrackConfig)?"":$" · {s.TrackConfig}")}".ToUpperInvariant();Draw();}
    void Draw(){if(_s is null)return;double w=_c.ActualWidth,h=_c.ActualHeight;if(w<20||h<20)return;_c.Children.Clear();if(!_s.TrackMapReady||_s.TrackPoints.Count<2){_c.Children.Add(new TextBlock{Text="LEARNING THIS LAYOUT…\nComplete a clean lap to build the map.",Foreground=Theme.Muted,FontSize=10,TextAlignment=TextAlignment.Center,Width=w,Margin=new Thickness(0,h*.35,0,0)});return;}var pts=_s.TrackPoints;var shadow=new Polyline{Stroke=new SolidColorBrush(Color.FromRgb(88,98,110)),StrokeThickness=7,StrokeLineJoin=PenLineJoin.Round};var lane=new Polyline{Stroke=new SolidColorBrush(Color.FromRgb(25,30,38)),StrokeThickness=3,StrokeLineJoin=PenLineJoin.Round};foreach(var p in pts){var q=new Point(p.X*w,p.Y*h);shadow.Points.Add(q);lane.Points.Add(q);}_c.Children.Add(shadow);_c.Children.Add(lane);foreach(var car in _s.MapCars){int i=Math.Clamp((int)Math.Round(car.lapPct*(pts.Count-1)),0,pts.Count-1);var p=pts[i];double size=car.player?18:14;var badge=new Border{Width=size,Height=size,CornerRadius=new CornerRadius(size/2),Background=car.classBrush,BorderBrush=car.player?Theme.Green:Brushes.Black,BorderThickness=new Thickness(car.player?2:1),Child=new TextBlock{Text=string.IsNullOrWhiteSpace(car.number)?"•":car.number,Foreground=Brushes.White,FontSize=car.player?7:6,FontWeight=FontWeights.Bold,TextAlignment=TextAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};Canvas.SetLeft(badge,p.X*w-size/2);Canvas.SetTop(badge,p.Y*h-size/2);_c.Children.Add(badge);}var legend=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8)};foreach(var cls in _s.Standings.GroupBy(x=>x.ClassId).Select(g=>g.First()).Take(5)){legend.Children.Add(new Border{Width=8,Height=8,Background=cls.ClassBrush,CornerRadius=new CornerRadius(4),Margin=new Thickness(0,0,4,0),VerticalAlignment=VerticalAlignment.Center});legend.Children.Add(new TextBlock{Text=string.IsNullOrWhiteSpace(cls.ClassName)?$"Class {cls.ClassId}":cls.ClassName,Foreground=Theme.Muted,FontSize=8,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center});}Canvas.SetLeft(legend,6);Canvas.SetBottom(legend,4);_c.Children.Add(legend);}
}
