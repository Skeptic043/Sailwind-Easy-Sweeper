# Building Easy Sweeper

## Requirements

- Windows with PowerShell 7 and the .NET 10 SDK (the tests target .NET 10).
- A local Sailwind installation with BepInEx 5.

Game and loader assemblies are build references only. They are not included in this repository or its packages.

Two MSBuild properties point at those folders:

- `SailwindManaged` is the game's `Sailwind_Data\Managed` folder. Default: `C:\Steam Games\steamapps\common\Sailwind\Sailwind_Data\Managed`
- `BepInExCore` is the `BepInEx\core` folder. Default: `%LOCALAPPDATA%\SailwindModSynchronizer\packs\default\instance\BepInEx\core`

## Build

From the repository folder:

```powershell
dotnet build src -c Release -p:SailwindManaged='C:\Games\Sailwind\Sailwind_Data\Managed' -p:BepInExCore='C:\Games\Sailwind\BepInEx\core'
```

Use the paths for your own installation or mod profile. The output is `src/bin/Release/netstandard2.0/EasySweeper.dll`.

## Test

```powershell
dotnet run --project tests -c Release -p:SailwindManaged='C:\Games\Sailwind\Sailwind_Data\Managed' -p:BepInExCore='C:\Games\Sailwind\BepInEx\core'
```

The checks cover the stroke timing, confirm the game members the patch relies on still exist in your installed `Assembly-CSharp.dll`, and check the built plugin's identity. Any failure makes the command fail.

## Package

```powershell
pwsh -File ./Package.ps1 -SailwindManaged 'C:\Games\Sailwind\Sailwind_Data\Managed' -BepInExCore 'C:\Games\Sailwind\BepInEx\core'
```

This builds the mod and writes `artifacts/EasySweeper-<version>.zip` with `BepInEx/plugins/EasySweeper/`, `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md` and `LICENSE`.
