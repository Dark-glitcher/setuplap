# SetupLap Overlay V2 — iRacing

V2 is aimed at the denser information style used by serious iRacing overlays while keeping SetupLap's own visual design.

## V2 changes
- Standings now shows overall position, class position, car number, driver, licence/SR string, iRating and last lap.
- Relatives now shows class, licence, iRating and time gap.
- Track map supports multiclass fields. Cars are coloured by iRacing's class colour and labelled with car numbers.
- Red Bull Ring GP has a built-in real circuit outline rather than the V1 generic fallback.
- Unknown circuits no longer show a fake oval. The map says "Learning this layout" until enough geometry is learned from your driving.
- Smaller, denser weather and inputs widgets.
- Cleaner default layout and stronger highlighting for your own car.

## Test
1. Extract the release ZIP.
2. Start iRacing in Borderless or Windowed mode.
3. Run `SetupLapOverlay.exe`.
4. Join a live session.
5. Click **EDIT LAYOUT** to position the widgets.
6. Click **LOCK LAYOUT** before driving.

If it stays on Waiting for iRacing, check `Documents\iRacing\app.ini` and make sure `irsdkEnableMem=1`.

SetupLap is independent and is not affiliated with or endorsed by iRacing.com Motorsport Simulations.
