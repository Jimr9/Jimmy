# Jimmy

Jimmy is a Windows companion application for WSJT-X, designed to make FT8 and FT4 operation accessible to blind amateur radio operators.

## Background

Jimmy began as **Tilly**, created by Andy WM8Q. Tilly provided the foundation for accessible FT8/FT4 operation with keyboard control, audio feedback, queue handling, and WSJT-X UDP integration. Jimmy is a modified and expanded continuation of that work, with substantial accessibility, UI, queue, and workflow changes.

## Requirements

- Windows 10 or 11
- .NET Framework 4.7.2
- Andy WM8Q's modified WSJT-X (standard/unmodified WSJT-X is not supported)

### Accepted WSJT-X builds

- 2.7.0 rev 204
- 3.0.0-rc1 rev 102
- 3.0.0-rc1 rev 103

Download Andy's modified WSJT-X from:
https://github.com/avantol/WSJT-X_3.0.0/releases/latest

## Installation

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
