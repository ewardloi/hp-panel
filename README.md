# HP Panel

HP Panel is a Windows system-tray app for controlling supported HP laptop performance modes and viewing device and battery information.

## Features

- Switch between the thermal modes exposed by the system BIOS, including Performance, Balanced, Cool, Quiet, and Power Saver when supported.
- Optionally synchronize HP thermal modes with the Windows power mode and follow changes to the Windows power mode.
- View battery charge, health, capacity, charge rate, cycle count, and related details when reported by the device.
- View device, firmware, and hardware information.
- Change BIOS settings exposed by the device.
- Open the panel and switch or cycle modes using configurable global hotkeys. The default hotkeys are `Ctrl+Alt+P` to open or close the panel and `Ctrl+Alt+M` to cycle modes.
- Start with Windows and restore the last selected mode at startup.

Available modes and information depend on the laptop, BIOS, and Windows hardware interfaces.

## Requirements

- Windows.
- The .NET 10 SDK to build the project.
- An HP computer whose BIOS exposes the HP WMI interface and supports thermal mode control for mode switching. Some features may be unavailable on other devices or BIOS versions.
- Administrator privileges may be required for BIOS operations.

## Build and run

From the project directory, run:

```powershell
dotnet restore HpPanel.csproj
dotnet build HpPanel.csproj -c Debug
dotnet run --project HpPanel.csproj
```

HP Panel runs in the notification area. Click its tray icon to open the panel; right-click it for application options.

## Command-line options

```text
HpPanel.exe --tray           Start hidden in the notification area
HpPanel.exe --autostart on   Enable startup with Windows
HpPanel.exe --autostart off  Disable startup with Windows
```

The `--autostart` command creates or removes a Windows Task Scheduler task for the current user.

## Settings and logs

Settings are stored in `%APPDATA%\HpPanel\settings.json`. Temporary files and crash logs are stored under `%TEMP%\HpPanel`.