using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
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
    static string LogPath
    {
        get
        {
            var d=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SetupLap");
            Directory.CreateDirectory(d);
            return Path.Combine(d,"overlay-v2-crash.log");
        }
    }

    static void Log(Exception ex,string source)
    {
        try{File.AppendAllText(LogPath,$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\r\n{ex}\r\n\r\n");}catch{}
    }

    [STAThread] public static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException+=(s,e)=>{if(e.ExceptionObject is Exception ex)Log(ex,"AppDomain");};

        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
        app.DispatcherUnhandledException+=(s,e)=>
        {
            Log(e.Exception,"Dispatcher");
            MessageBox.Show($"SetupLap Overlay hit an error and recovered.\n\nA crash log was written to:\n{LogPath}","SetupLap Overlay",MessageBoxButton.OK,MessageBoxImage.Warning);
            e.Handled=true;
        };

        try
        {
            var service=new TelemetryService();
            var widgets=new List<WidgetWindow>{new StandingsWindow(),new RelativeWindow(),new WeatherWindow(),new InputsWindow(),new TrackMapWindow(),new RadarWindow()};

            // Create every widget HWND once, then make it invisible until telemetry is live.
            // This is more stable than repeatedly creating/showing transparent topmost windows.
            foreach(var w in widgets)
            {
                w.Opacity=0;
                w.IsHitTestVisible=false;
                w.Show();
            }

            new ControlWindow(service,widgets).Show();
            app.Run();
        }
        catch(Exception ex)
        {
            Log(ex,"Main");
            MessageBox.Show($"SetupLap Overlay could not start.\n\nCrash log:\n{LogPath}","SetupLap Overlay",MessageBoxButton.OK,MessageBoxImage.Error);
        }
    }
}
