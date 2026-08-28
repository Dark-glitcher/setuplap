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

public static class Program
{
    [STAThread] public static void Main()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var service=new TelemetryService();
        var widgets=new List<WidgetWindow>{new StandingsWindow(),new RelativeWindow(),new WeatherWindow(),new InputsWindow(),new TrackMapWindow(),new RadarWindow()};
        foreach(var w in widgets)w.Show();
        new ControlWindow(service,widgets).Show();
        app.Run();
    }
}
