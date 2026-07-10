# Jimmy

Jimmy is a Windows companion application for WSJT-X that makes FT8 and FT4 operation accessible to blind amateur radio operators. It works alongside WSJT-X and provides an accessible, keyboard-driven operating experience with screen readers.

## Key Features

- **Keyboard operation, accessible with screen readers** — actions are reachable without a mouse, with concise speech output.
- **Accessible FT8/FT4 station selection and reply workflow** — select and respond to stations from the keyboard.
- **Call queue management** — includes filtering and prioritization options for incoming calls.
- **Award tracking with live "still needed" indicators** — supports common award programs, highlighting relevant stations as they're heard.
- **Logbook and lookup features** — includes local logging, callsign lookup, and related operating tools.
- **Integrates with QRZ, Club Log, LoTW, and PSK Reporter** — supports integration with these services for logging, confirmations, and spotting.
- **Appearance and display options** — offers alternate color themes and an advanced display layout for additional detail.

## Project Status

Jimmy is a hobby project under active development. Features, award definitions, and behavior may change as the project evolves.

## Background

Jimmy began as **Tilly**, created by Andy WM8Q. Tilly provided the foundation for accessible FT8/FT4 operation with keyboard control, audio feedback, queue handling, and WSJT-X UDP integration. Jimmy is a modified and expanded continuation of that work, with substantial accessibility, UI, queue, and workflow changes.

## Requirements

- Windows 10 or 11
- .NET Framework 4.7.2
- **Andy WM8Q's modified WSJT-X — standard/unmodified WSJT-X is not supported.**

### Accepted WSJT-X builds

- 2.7.0 rev 204
- 3.0.0-rc1 rev 102
- 3.0.0-rc1 rev 103

Download Andy's modified WSJT-X from:
https://github.com/avantol/WSJT-X_3.0.0/releases/latest

## Getting Started

1. Install one of the [accepted modified WSJT-X builds](#accepted-wsjt-x-builds) above — Jimmy will not connect to a standard WSJT-X install.
2. Install the latest `Jimmy.msi` from [Releases](https://github.com/jimr9/Jimmy/releases/latest) (see [Installation](#installation) below).
3. Start WSJT-X. Under **File | Settings | Reporting**, make sure **Accept UDP requests** is checked. Jimmy uses WSJT-X's standard UDP port (2237) by default, so no further setup is normally needed.
4. Start Jimmy. It connects to WSJT-X automatically once UDP requests are enabled.

## Installation

**Jimmy requires Andy WM8Q's modified WSJT-X (see [Requirements](#requirements) above) — it does not work with standard/unmodified WSJT-X.** Install a supported WSJT-X build first.

Download the latest `Jimmy.msi` from the [Releases](https://github.com/jimr9/Jimmy/releases/latest) page and run it.

## Building from Source

Open `Jimmy.sln` in Visual Studio 2022 and build in Release mode.

To build the installer: `wix build -o Release\Jimmy.msi Jimmy.wxs` (from `Setup_WiX\`)

## More Information

- Discussion group: https://groups.io/g/tilly-beta/topics

## Acknowledgements

Jimmy is built on **Tilly**, the original accessible WSJT-X companion application created by **Andy WM8Q**. Andy's work on Tilly — including the UDP integration, audio feedback system, keyboard control model, and queue handling — made this project possible. Thank you, Andy.

## License

Jimmy is based on Tilly, which was released under GPL-3.0. See the [LICENSE](LICENSE) file for details.
