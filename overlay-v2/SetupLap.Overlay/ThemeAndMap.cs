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

public static class Theme
{
    public static readonly Brush Panel = new SolidColorBrush(Color.FromArgb(222, 13, 18, 25));
    public static readonly Brush Header = new SolidColorBrush(Color.FromArgb(230, 18, 24, 33));
    public static readonly Brush Line = new SolidColorBrush(Color.FromArgb(60,255,255,255));
    public static readonly Brush Text = new SolidColorBrush(Color.FromRgb(239,243,247));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(145,157,171));
    public static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(255,77,23));
    public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(140,255,0));
    public static readonly Brush Blue = new SolidColorBrush(Color.FromRgb(35,146,255));
    public static readonly Brush Red = new SolidColorBrush(Color.FromRgb(255,84,84));
    public static Brush FromHex(string? text, Brush fallback)
    {
        try
        {
            if(string.IsNullOrWhiteSpace(text)) return fallback;
            var s=text.Trim();
            if(s.StartsWith("0x",StringComparison.OrdinalIgnoreCase)) s="#"+s[2..];
            if(!s.StartsWith("#")) s="#"+s;
            return (Brush)new BrushConverter().ConvertFromString(s)!;
        } catch { return fallback; }
    }
}

public sealed class TrackMapProvider
{
    private readonly LearnedTrack _learned = new();
    private static readonly Point[] RedBullRing =
    [
        new(.68,.78), new(.59,.80), new(.49,.82), new(.41,.82), new(.36,.78),
        new(.31,.69), new(.25,.59), new(.19,.49), new(.12,.38), new(.07,.28),
        new(.05,.20), new(.08,.15), new(.18,.14), new(.31,.16), new(.47,.20),
        new(.58,.21), new(.64,.23), new(.63,.29), new(.58,.35), new(.51,.39),
        new(.43,.39), new(.36,.37), new(.30,.37), new(.27,.42), new(.28,.49),
        new(.33,.56), new(.39,.57), new(.46,.50), new(.53,.47), new(.62,.47),
        new(.72,.47), new(.82,.47), new(.90,.49), new(.94,.55), new(.96,.64),
        new(.94,.70), new(.86,.73), new(.77,.75), new(.68,.78)
    ];
    public void Reset()=>_learned.Reset();
    public void Update(double sessionTime,float lapPct,float speedMps,float yawNorth)=>_learned.Update(sessionTime,lapPct,speedMps,yawNorth);
    public (IReadOnlyList<Point> points,bool ready) Get(string track,string config)
    {
        string key=$"{track} {config}".ToLowerInvariant();
        if(key.Contains("red bull ring") || key.Contains("spielberg")) return (Resample(RedBullRing,220),true);
        var learned=_learned.GetNormalised(); return (learned,_learned.Ready);
    }
    private static IReadOnlyList<Point> Resample(Point[] src,int count)
    {
        var seg=new double[src.Length]; double total=0;
        for(int i=1;i<src.Length;i++){total+=(src[i]-src[i-1]).Length;seg[i]=total;}
        var result=new List<Point>(count);
        for(int k=0;k<count;k++)
        {
            double d=total*k/(count-1); int i=1; while(i<seg.Length-1 && seg[i]<d)i++;
            double prev=seg[i-1], next=seg[i], t=next>prev?(d-prev)/(next-prev):0; var a=src[i-1];var b=src[i];
            result.Add(new Point(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t));
        }
        return result;
    }
}

public sealed class LearnedTrack
{
    const int Bins=240; readonly Point?[] _points=new Point?[Bins]; double _x,_y,_lastTime=double.NaN; float _lastPct=-1; int _count;
    public bool Ready=>_count>Bins*.70;
    public void Reset(){Array.Clear(_points);_x=_y=0;_lastTime=double.NaN;_lastPct=-1;_count=0;}
    public void Update(double time,float pct,float speed,float yaw)
    {
        if(pct<0||pct>1||!double.IsFinite(time))return;
        if(double.IsNaN(_lastTime)){_lastTime=time;_lastPct=pct;return;}
        double dt=Math.Clamp(time-_lastTime,0,.12);_lastTime=time;
        if(_lastPct>.94f&&pct<.06f){_x=0;_y=0;}
        _x+=speed*Math.Sin(yaw)*dt;_y-=speed*Math.Cos(yaw)*dt;_lastPct=pct;
        int bin=Math.Clamp((int)Math.Round(pct*(Bins-1)),0,Bins-1);
        if(!_points[bin].HasValue){_points[bin]=new Point(_x,_y);_count++;}
        else {var p=_points[bin]!.Value;_points[bin]=new Point(p.X*.9+_x*.1,p.Y*.9+_y*.1);}
    }
    public IReadOnlyList<Point> GetNormalised()
    {
        if(!Ready)return [];
        var raw=new Point[Bins];
        for(int i=0;i<Bins;i++)
        {
            if(_points[i].HasValue){raw[i]=_points[i]!.Value;continue;}
            int l=i-1,r=i+1;while(l>=0&&!_points[l].HasValue)l--;while(r<Bins&&!_points[r].HasValue)r++;
            if(l>=0&&r<Bins){double t=(i-l)/(double)(r-l);var a=_points[l]!.Value;var b=_points[r]!.Value;raw[i]=new Point(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
            else raw[i]=l>=0?_points[l]!.Value:_points[r]!.Value;
        }
        double minX=raw.Min(p=>p.X),maxX=raw.Max(p=>p.X),minY=raw.Min(p=>p.Y),maxY=raw.Max(p=>p.Y);
        double w=Math.Max(1,maxX-minX),h=Math.Max(1,maxY-minY),scale=.86/Math.Max(w,h),cx=(minX+maxX)/2,cy=(minY+maxY)/2;
        return raw.Select(p=>new Point((p.X-cx)*scale+.5,(p.Y-cy)*scale+.5)).ToList();
    }
}
