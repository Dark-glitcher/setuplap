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

public sealed class AppSettings{public bool EditMode{get;set;}public Dictionary<string,double[]>Windows{get;set;}=[];}

public sealed class ControlWindow:Window
{
    readonly List<WidgetWindow>_widgets;readonly TextBlock _status;readonly Button _edit;AppSettings _settings;readonly TelemetryService _service;
    public ControlWindow(TelemetryService service,List<WidgetWindow>widgets)
    {
        _service=service;_widgets=widgets;_settings=Load();Title="SetupLap Overlay V2";Width=400;Height=510;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        Background=new SolidColorBrush(Color.FromRgb(10,14,20));Foreground=Theme.Text;
        var root=new StackPanel{Margin=new Thickness(22)};
        var brand=new TextBlock{Text="SETUPLAP",FontSize=30,FontWeight=FontWeights.Black};root.Children.Add(brand);
        root.Children.Add(new TextBlock{Text="iRacing Overlay · V2 test build",Foreground=Theme.Muted,Margin=new Thickness(0,0,0,15)});
        _status=new TextBlock{Text="● WAITING FOR IRACING",Foreground=Theme.Red,FontWeight=FontWeights.Bold,Margin=new Thickness(0,0,0,15)};root.Children.Add(_status);
        root.Children.Add(new TextBlock{Text="WIDGETS",Foreground=Theme.Orange,FontSize=10,FontWeight=FontWeights.Bold});
        foreach(var w in widgets){var cb=new CheckBox{Content=w.Title,IsChecked=true,Foreground=Theme.Text,Margin=new Thickness(0,5,0,5)};cb.Checked+=(s,e)=>w.Show();cb.Unchecked+=(s,e)=>w.Hide();root.Children.Add(cb);}
        _edit=new Button{Content="EDIT LAYOUT",Height=38,Margin=new Thickness(0,16,0,8),Background=Theme.Orange,Foreground=Brushes.White,BorderThickness=new Thickness(0)};_edit.Click+=(s,e)=>SetEdit(!_settings.EditMode);root.Children.Add(_edit);
        root.Children.Add(new TextBlock{Text="V2: driver licence + iRating, class positions, class-coloured multiclass map markers, exact Red Bull Ring GP map, compact layout.",TextWrapping=TextWrapping.Wrap,Foreground=Theme.Muted,FontSize=10,Margin=new Thickness(0,8,0,14)});
        root.Children.Add(new TextBlock{Text="Use Borderless/Windowed iRacing. In race mode, lock the layout to make widgets click-through.",TextWrapping=TextWrapping.Wrap,Foreground=Theme.Blue,FontSize=10});
        Content=root;Closing+=(s,e)=>{Save();Application.Current.Shutdown();};ApplyPositions();SetEdit(_settings.EditMode);
        service.Updated+=s=>Dispatcher.BeginInvoke(()=>{_status.Text=s.Connected?$"● LIVE · {s.TrackName}":"● WAITING FOR IRACING";_status.Foreground=s.Connected?Theme.Green:Theme.Red;foreach(var w in widgets)if(w.IsVisible)w.Update(s);});
        foreach(var w in widgets)w.LocationChanged+=(s,e)=>Save();
    }
    void SetEdit(bool e){_settings.EditMode=e;foreach(var w in _widgets)w.SetEdit(e);_edit.Content=e?"LOCK LAYOUT":"EDIT LAYOUT";_edit.Background=e?Theme.Green:Theme.Orange;Save();}
    string PathName{get{var d=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SetupLap");Directory.CreateDirectory(d);return System.IO.Path.Combine(d,"overlay-v2-settings.json");}}
    AppSettings Load(){try{return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(PathName))??new();}catch{return new();}}
    void Save(){try{foreach(var w in _widgets)_settings.Windows[w.Title]=[w.Left,w.Top];File.WriteAllText(PathName,JsonSerializer.Serialize(_settings,new JsonSerializerOptions{WriteIndented=true}));}catch{}}
    void ApplyPositions(){foreach(var w in _widgets)if(_settings.Windows.TryGetValue(w.Title,out var p)&&p.Length>=2){w.Left=p[0];w.Top=p[1];}}
}
