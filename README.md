# motu-ui

A small Windows control panel for MOTU M-series audio interfaces (M2, M4, M6).
It talks to the hardware through the official MOTU driver library, so nothing
the device can't do is faked — the app just exposes more of it than the stock
control panel does.

![screenshot](docs/screenshot.png)

## What it does

- **Live input/output meters** — stereo LED ladders with peak hold and clip
  indication, no DAW needed to see if signal is flowing
- **Sample rate and buffer size** — apply instantly, with latency readout
- **Low latency mode** — driver-level switch that isn't exposed in the stock UI
- **WaveSRC** — the driver's sample rate converter for Windows audio
- **Presets** — two buttons (Record / Stream by default), each editable via the
  pencil next to it; stored in a JSON file next to the exe
- **Driver update check** — pulls the current version from motu.com for your
  specific model and runs the official installer if newer
- Follows the system light/dark theme, scales correctly on high-DPI displays,
  and shrinks to a compact meter strip

## Requirements

- A MOTU M2, M4, or M6 connected over USB
- Windows 10/11 with the official MOTU M-Series driver installed
  (the app loads `MSeriesLib.dll` from the driver directory)

## Build

```
dotnet build -c Release
```

Output lands in `bin\x86\Release\net10.0-windows10.0.19041.0\win-x86\`.

**Why x86:** the installed MOTU library is 32-bit, so the app must run as
x86. Everything else (Windows App SDK runtime included) is self-contained.

## Notes

- Low latency mode has no read-back in the driver API — the button shows what
  you set, not what the device reports.
- On M4/M6 the meters show the first stereo pair.
- Changing sample rate or buffer invalidates the audio endpoints for a moment;
  the meters reconnect automatically.
