# motu-ui

[![release](https://img.shields.io/github/v/release/dvinerlove/motu-ui)](https://github.com/dvinerlove/motu-ui/releases/latest)
[![license](https://img.shields.io/github/license/dvinerlove/motu-ui)](LICENSE)
[![downloads](https://img.shields.io/github/downloads/dvinerlove/motu-ui/total)](https://github.com/dvinerlove/motu-ui/releases)

An unofficial open-source control panel for MOTU M2, M4, and M6 audio
interfaces on Windows — with live meters, a loopback monitor, and editable
presets the stock panel doesn't have.

It talks to the hardware through the official MOTU driver library — every
control is a real driver call, nothing is faked.

![demo](docs/demo.gif)

## Download

Grab `motu-ui-v1.0.0-win-x86.zip` from
[Releases](https://github.com/dvinerlove/motu-ui/releases) — self-contained,
no .NET needed. Unzip and run `motu-ui.exe`.

## What it does

- **Live input/output meters** — stereo LED ladders with peak hold and clip
  indication, no DAW needed to see if signal is flowing
- **Loopback meter** — a third ladder showing the loopback capture endpoint,
  i.e. exactly what OBS and other recorders hear
- **Test tone** — the speaker button plays a 440 Hz signal through the
  device, so you can watch OUT and LOOP light up and confirm loopback
  works for your stream/recording
- **Sample rate and buffer size** — apply instantly, with latency readout
- **Low latency mode** — the driver switch the stock panel calls "Use lowest
  latency safety offsets"; here it's one click
- **WaveSRC** — the driver's sample rate converter for Windows audio
- **Presets** — two buttons (Record / Stream by default), each editable via the
  pencil next to it; stored in a JSON file next to the exe
- **Driver update check** — pulls the current version from motu.com for your
  specific model and runs the official installer if newer
- Follows the system light/dark theme, scales correctly on high-DPI displays,
  and resizes down to a compact window

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

## motu-ui vs the stock M-Series panel

| | Stock MOTU panel | motu-ui |
|---|---|---|
| Sample rate | yes | yes |
| Buffer size | yes | yes, with latency readout |
| Live I/O meters | no | yes — peak hold + clip |
| Loopback monitor | no | yes — what OBS hears |
| Test tone | no | yes — one-click signal check |
| Low latency mode | yes ("safety offsets") | one click |
| WaveSRC | yes | one click, tooltip explains |
| Presets | no | editable |
| Driver update check | no | per your model, from motu.com |
| Window | fixed size | Mica, theme-aware, resizable |

Screenshots: [preset editing](docs/preset-edit.png) ·
[driver update check](docs/update-check.png)

## FAQ

**Is there a mixer for the MOTU M2?**
No — the M2, M4, and M6 have no DSP mixing hardware, so no tool can offer a
software mixer for them. motu-ui is a control and status panel: it shows
live meters and exposes every setting the driver actually has.

**Is there software for the MOTU M2 besides the official panel?**
Yes — motu-ui is a third-party alternative. Other third-party MOTU tools
target the older AVB interfaces (UltraLite AVB, 1248, 16A), which M-series
units are not.

**What is WaveSRC on a MOTU interface?**
Wave Sample Rate Conversion — the driver-level resampler for normal Windows
(WAVE) audio. On: apps can run at any sample rate and the driver converts.
Off: apps must match the device rate exactly, for a bit-exact path.

**Does motu-ui replace the MOTU driver?**
No. It uses the official M-Series driver library, so the driver must be
installed. Think of it as a better front panel for the driver you already
have.

**Does it work on the M4 or M6?**
Yes — the app reads the connected model and adjusts the update check and
channel layout. The meters currently show the first stereo pair on
multi-channel models.

## Notes

- Low latency mode has no read-back in the driver API — the button shows what
  you set, not what the device reports.
- On M4/M6 the meters show the first stereo pair.
- Changing sample rate or buffer invalidates the audio endpoints for a moment;
  the meters reconnect automatically.
