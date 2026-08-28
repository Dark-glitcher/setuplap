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

public sealed record DriverInfo(
    int CarIdx, string Name, string Number, string ClassName, int ClassId,
    int IRating, string License, string ClassColor, string LicenseColor);

public sealed record CarRow(
    int CarIdx, int Position, int ClassPosition, int Lap, float LapPct, float EstTime,
    float BestLap, float LastLap, bool Pit, string Name, string Number, string ClassName,
    int ClassId, int IRating, string License, Brush ClassBrush, bool IsPlayer);

public sealed record RelativeRow(
    int CarIdx, string Name, string Number, string ClassName, int IRating, string License,
    Brush ClassBrush, double GapSeconds, bool IsPlayer);

public sealed class OverlaySnapshot
{
    public bool Connected { get; init; }
    public string TrackName { get; init; } = "Waiting for iRacing";
    public string TrackConfig { get; init; } = "";
    public float Throttle { get; init; }
    public float Brake { get; init; }
    public float Clutch { get; init; }
    public float SpeedKph { get; init; }
    public int Gear { get; init; }
    public int CarLeftRight { get; init; }
    public float AirTemp { get; init; }
    public float TrackTemp { get; init; }
    public float Humidity { get; init; }
    public float WindSpeed { get; init; }
    public float TrackWetness { get; init; }
    public IReadOnlyList<CarRow> Standings { get; init; } = [];
    public IReadOnlyList<RelativeRow> Relatives { get; init; } = [];
    public IReadOnlyList<Point> TrackPoints { get; init; } = [];
    public bool TrackMapReady { get; init; }
    public IReadOnlyList<(int carIdx,float lapPct,bool player,Brush classBrush,string number)> MapCars { get; init; } = [];
}
