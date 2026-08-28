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

public sealed class TelemetryService
{
    readonly IRacingSDK _sdk=new();
    readonly Dictionary<int,DriverInfo> _drivers=[];
    readonly TrackMapProvider _map=new();
    int _lastSessionUpdate=-1;
    long _lastTick;
    string _track="Waiting for iRacing",_config="";

    public event Action<OverlaySnapshot>? Updated;

    public TelemetryService()
    {
        _sdk.OnConnected+=()=>_map.Reset();
        _sdk.OnDisconnected+=()=>Updated?.Invoke(new OverlaySnapshot());
        _sdk.OnDataChanged+=OnData;
    }

    static T Get<T>(IRacingSDK sdk,string name,T fallback=default!)
    {
        try{var v=sdk.GetData(name);if(v is T t)return t;if(v is null)return fallback;return (T)Convert.ChangeType(v,typeof(T),CultureInfo.InvariantCulture);}
        catch{return fallback;}
    }
    static T[] Arr<T>(IRacingSDK sdk,string name){try{return sdk.GetData(name) as T[]??[];}catch{return [];}}

    void OnData()
    {
        var now=Stopwatch.GetTimestamp();
        if(_lastTick!=0&&(now-_lastTick)/(double)Stopwatch.Frequency<.05)return;
        _lastTick=now;
        if(!_sdk.IsConnected())return;
        try
        {
            int update=_sdk.Header?.SessionInfoUpdate??-1;
            if(update!=_lastSessionUpdate){_lastSessionUpdate=update;ParseSession(_sdk.GetSessionInfo());}

            int player=Get(_sdk,"PlayerCarIdx",-1);
            var pct=Arr<float>(_sdk,"CarIdxLapDistPct");
            var pos=Arr<int>(_sdk,"CarIdxPosition");
            var cpos=Arr<int>(_sdk,"CarIdxClassPosition");
            var laps=Arr<int>(_sdk,"CarIdxLap");
            var est=Arr<float>(_sdk,"CarIdxEstTime");
            var best=Arr<float>(_sdk,"CarIdxBestLapTime");
            var last=Arr<float>(_sdk,"CarIdxLastLapTime");
            var pit=Arr<bool>(_sdk,"CarIdxOnPitRoad");
            var classes=Arr<int>(_sdk,"CarIdxClass");

            float speed=Get(_sdk,"Speed",0f);
            float playerPct=player>=0&&player<pct.Length?pct[player]:-1;
            _map.Update(Get(_sdk,"SessionTime",0d),playerPct,speed,Get(_sdk,"YawNorth",Get(_sdk,"Yaw",0f)));
            var (trackPoints,mapReady)=_map.Get(_track,_config);

            int n=new[]{pct.Length,pos.Length,laps.Length}.Max();
            var cars=new List<CarRow>();
            for(int i=0;i<n;i++)
            {
                int p=i<pos.Length?pos[i]:0;float lp=i<pct.Length?pct[i]:-1;
                if(p<=0&&lp<0)continue;
                _drivers.TryGetValue(i,out var d);
                int classId=i<classes.Length?classes[i]:d?.ClassId??0;
                var classBrush=Theme.FromHex(d?.ClassColor,ClassFallback(classId));
                cars.Add(new(
                    i,p,i<cpos.Length?cpos[i]:0,i<laps.Length?laps[i]:0,lp,
                    i<est.Length?est[i]:0,i<best.Length?best[i]:0,i<last.Length?last[i]:0,
                    i<pit.Length&&pit[i],d?.Name??$"Car {i}",d?.Number??"",d?.ClassName??"",
                    classId,d?.IRating??0,d?.License??"",classBrush,i==player));
            }

            var standings=cars.Where(c=>c.Position>0).OrderBy(c=>c.Position).Take(30).ToList();
            var relatives=BuildRelatives(cars,player);

            Updated?.Invoke(new OverlaySnapshot{
                Connected=true,TrackName=_track,TrackConfig=_config,
                Throttle=Get(_sdk,"Throttle",0f),Brake=Get(_sdk,"Brake",0f),Clutch=Get(_sdk,"Clutch",0f),
                SpeedKph=speed*3.6f,Gear=Get(_sdk,"Gear",0),
                AirTemp=Get(_sdk,"AirTemp",0f),TrackTemp=Get(_sdk,"TrackTempCrew",Get(_sdk,"TrackTemp",0f)),
                Humidity=Get(_sdk,"RelativeHumidity",0f),WindSpeed=Get(_sdk,"WindVel",0f),
                TrackWetness=Get(_sdk,"TrackWetness",0f),
                Standings=standings,Relatives=relatives,TrackPoints=trackPoints,TrackMapReady=mapReady,
                MapCars=cars.Where(c=>c.LapPct>=0).Select(c=>(c.CarIdx,c.LapPct,c.IsPlayer,c.ClassBrush,c.Number)).ToList()
            });
        }catch{}
    }

    static Brush ClassFallback(int classId)
    {
        Brush[] colors=[Theme.Blue,Theme.Orange,Theme.Green,new SolidColorBrush(Color.FromRgb(185,110,255)),new SolidColorBrush(Color.FromRgb(0,210,190))];
        return colors[Math.Abs(classId)%colors.Length];
    }

    IReadOnlyList<RelativeRow> BuildRelatives(List<CarRow> cars,int playerIdx)
    {
        var me=cars.FirstOrDefault(c=>c.CarIdx==playerIdx);if(me is null)return[];
        double lapTime=me.BestLap>20?me.BestLap:cars.Where(c=>c.BestLap>20).Select(c=>(double)c.BestLap).DefaultIfEmpty(90).Min();
        var rows=new List<RelativeRow>{new(me.CarIdx,me.Name,me.Number,me.ClassName,me.IRating,me.License,me.ClassBrush,0,true)};
        foreach(var c in cars.Where(c=>c.CarIdx!=playerIdx&&c.LapPct>=0))
        {
            double gap=c.EstTime-me.EstTime;if(gap>lapTime/2)gap-=lapTime;if(gap<-lapTime/2)gap+=lapTime;
            rows.Add(new(c.CarIdx,c.Name,c.Number,c.ClassName,c.IRating,c.License,c.ClassBrush,gap,false));
        }
        var ahead=rows.Where(r=>!r.IsPlayer&&r.GapSeconds>0).OrderBy(r=>r.GapSeconds).Take(5).OrderByDescending(r=>r.GapSeconds);
        var behind=rows.Where(r=>!r.IsPlayer&&r.GapSeconds<0).OrderByDescending(r=>r.GapSeconds).Take(5).OrderByDescending(r=>r.GapSeconds);
        return ahead.Concat(rows.Where(r=>r.IsPlayer)).Concat(behind).ToList();
    }

    void ParseSession(string? yaml)
    {
        if(string.IsNullOrWhiteSpace(yaml))return;
        try
        {
            var des=new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
            var root=des.Deserialize<Dictionary<object,object>>(yaml);
            _drivers.Clear();
            if(root.TryGetValue("WeekendInfo",out var wi)&&wi is Dictionary<object,object>w)
            {
                _track=Str(w,"TrackDisplayName",Str(w,"TrackName","iRacing"));
                _config=Str(w,"TrackConfigName","");
            }
            if(root.TryGetValue("DriverInfo",out var di)&&di is Dictionary<object,object>d&&d.TryGetValue("Drivers",out var ds)&&ds is List<object>list)
            {
                foreach(var obj in list)
                {
                    if(obj is not Dictionary<object,object>m)continue;
                    int idx=Int(m,"CarIdx",-1);if(idx<0)continue;
                    _drivers[idx]=new(idx,Str(m,"UserName",$"Car {idx}"),Str(m,"CarNumber",""),Str(m,"CarClassShortName",""),
                        Int(m,"CarClassID",0),Int(m,"IRating",0),Str(m,"LicString",""),Str(m,"CarClassColor",""),Str(m,"LicColor",""));
                }
            }
        }catch{}
    }
    static string Str(Dictionary<object,object>m,string k,string f)=>m.TryGetValue(k,out var v)&&v is not null?v.ToString()??f:f;
    static int Int(Dictionary<object,object>m,string k,int f)=>m.TryGetValue(k,out var v)&&int.TryParse(v?.ToString(),out var x)?x:f;
}
