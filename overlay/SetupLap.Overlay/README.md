# SetupLap Overlay v0.1

First test build for iRacing.

Included widgets:
- Track map: learns an approximate circuit shape from your live heading/speed while you drive, then plots the field by lap distance.
- Standings
- Relatives
- Weather
- Inputs / pedal traces

## Run
1. Use Windows 10/11 64-bit.
2. Run iRacing in Borderless or Windowed display mode.
3. Start `SetupLapOverlay.exe`.
4. Join an iRacing session.
5. Use **EDIT LAYOUT** to drag widgets, then **LOCK LAYOUT** for click-through racing mode.

If the app stays on "Waiting for iRacing", check `Documents\iRacing\app.ini` and ensure:
`irsdkEnableMem=1`

## Notes
This is a first real-world test build. The track map starts with a simple fallback shape and learns the track as you drive. Standings/relatives depend on the data iRacing exposes in the active session.

Telemetry stays on your PC. The app reads iRacing's published shared-memory telemetry only.

Third-party:
- irsdkSharp (MIT)
- YamlDotNet (MIT)

SetupLap is not affiliated with or endorsed by iRacing.com Motorsport Simulations.
