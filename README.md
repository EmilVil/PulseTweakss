# PULSE v0.1

PULSE is a native WPF utility for Windows 10 and Windows 11. It reads your system and lets you queue changes, preview them, and decide which changes to apply. It starts with standard user permissions.

## Build and run

Install the [.NET 8 SDK for Windows](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) or use Visual Studio with the **.NET desktop development** workload. From the project directory in PowerShell, run:

```powershell
dotnet build Pulse.sln -c Release
dotnet run --project Pulse.csproj -c Release
```

To produce a standalone Windows x64 release:

```powershell
dotnet publish Pulse.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o artifacts/PULSE-win-x64
```

Open `artifacts/PULSE-win-x64/Pulse.exe`. The ordinary build output at `bin/Release/net8.0-windows/Pulse.exe` requires the .NET 8 Desktop Runtime. The supplied logo PNG is embedded from `Assets`. The sidebar displays the monitor artwork from that file.

## What works in v0.1

| Page | Controls |
| --- | --- |
| Overview | Windows build and edition, CPU model, display adapter names, installed RAM, CPU use, physical memory use, and uptime. Usage samples refresh every two seconds while Overview is visible. |
| Optimization | Read, preview, change, and restore Windows client-area animations. |
| Debloat | Detect and remove optional current-user Store packages from a fixed list, including Copilot, Clipchamp, Solitaire, Microsoft News, Weather, and Tips. Only removable packages reported by Windows are listed. PULSE records the removal in history. |
| Gaming | Change Game Mode directly on Windows 11. Choose an installed power plan and adjust client-area animations. Open supported Windows pages for Game Mode on Windows 10, Captures, Startup apps, and visual effects. |
| Privacy | Change supported current-user policies for suggestions and tailored experiences. Open Windows pages for advertising ID, suggested content, and personalized offers. Copilot and Recall links appear when the corresponding app or feature is detected. |
| Customization | Show or hide known file extensions in File Explorer. |
| Advanced | Switch between installed power plans after reviewing device-wide effects on battery and heat. Available plans depend on the device and its Windows configuration. |
| Restore | Inspect outcomes and saved original values. Restore one setting at a time, starting with its most recent change. Export JSON with detected settings, preferences, pending selections, and history. |
| Settings | Persist live monitoring, page transition, and last selected profile preferences. |

Safe queues file extensions. Balanced queues file extensions and Game Mode where available. Gaming queues Game Mode and fewer animations. Custom lets you choose individual changes. Profiles only queue available changes that differ from the detected state.

## Applying and restoring changes

1. Select changes using the row switches or load a profile.
2. Select **Apply changes** to inspect the current and proposed values.
3. Confirm the preview to write the changes. Read the outcome in **Restore**.
4. To undo a reversible operation, select **Restore original** and review the confirmation.

PULSE saves the original value before attempting each write and verifies the result with a fresh read. A batch can partially succeed. If a value changed after the preview, refresh and review the new value. If a write is interrupted, the saved original remains in the local journal and **Restore** shows that the entry needs review. On restore, PULSE preserves a value that differs from both the saved original and the value it applied.

Per-user data lives in `%LOCALAPPDATA%\PULSE`. An export is a readable snapshot. App removal may discard local app data. Reinstallation, where available, uses Microsoft Store. Windows Settings shortcuts open Windows’ own controls, and changes made there are managed by Windows.

Windows limits certain privacy policies by edition and build. Organization-managed settings can also take precedence. Power plan restoration requires the original plan to remain installed.

## Verify

```powershell
dotnet run --project Tests/Pulse.Tests.csproj -c Release
dotnet run --project Tests/Pulse.Tests.csproj -c Release -- (Join-Path $PWD 'obj/overview-check.png')
```

The first command runs the journal, settings, and export tests with simulated tweak providers. The second also opens the WPF window, reads hardware and system status, checks navigation and search, exercises selection and preview cancellation, and captures Overview and Gaming. The tests use a temporary PULSE data directory and leave Windows settings untouched.

## Planned

- Direct, reversible controls for more Windows Settings features as reliable interfaces become available.
- Import and validation of user-created profiles.
- An installer and signed release builds.

## Windows references

- [Windows Settings registry reference](https://learn.microsoft.com/en-us/windows/apps/develop/settings/settings-windows-11) and [Windows Settings page URIs](https://learn.microsoft.com/en-us/windows/apps/develop/launch/launch-settings).
- [Windows animation API](https://learn.microsoft.com/en-us/windows/win32/winauto/client-area-animation) and [File Explorer settings API](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shgetsetsettings).
- [Windows power scheme APIs](https://learn.microsoft.com/en-us/windows/win32/power/managing-power-schemes).
- [Windows Experience policies](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience) and [Windows privacy settings](https://support.microsoft.com/en-us/windows/privacy/general-privacy-settings-in-windows).
- [AppX package inventory](https://learn.microsoft.com/en-us/powershell/module/appx/get-appxpackage) and [current-user AppX removal](https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage).
