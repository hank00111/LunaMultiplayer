# Building LMP from source

This branch tracks upstream `LunaMultiplayer/master` and adds the minimum glue
to build from a clean clone on a local dev machine.

## Prerequisites

- **.NET 10 SDK** (10.0.100+) — see `global.json`
- **KSP 1.12.5** installation (Steam or retail) — KSP DLLs are not shipped in
  the upstream repo and must be copied from your local KSP install
- **PowerShell 5.1+** (Windows 10/11 has this built in) or **PowerShell 7+**

## One-time setup

1. Edit `Scripts/SetDirectories.bat` and set `KSPPATH` (and optionally `KSPPATH2`)
   to your KSP 1.12.5 install directory. Example:
   ```
   SET KSPPATH=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
   ```
   Note: `SetDirectories.bat` is tracked with `git update-index --skip-worktree`,
   so local edits won't appear in `git status`.

2. Populate `External/KSPLibraries/` with KSP + Unity DLLs:
   ```powershell
   .\Scripts\SetupKSPLibs.ps1
   ```
   Or pass an explicit path:
   ```powershell
   .\Scripts\SetupKSPLibs.ps1 -KspPath "D:\Games\Kerbal Space Program"
   ```

## Build

```batch
Scripts\build-lmp-projects.bat --release
```

This builds `LmpClient`, `Server`, and `MasterServer` with `dotnet build`.
Output lands in each project's `bin\Release\` directory.

## Install to KSP GameData (optional)

```batch
Scripts\CopyToKSPDirectory.bat Release
```

Copies plugins, resources, localization, and PartSync XML to
`%KSPPATH%\GameData\LunaMultiplayer\` and `%KSPPATH%\GameData\000_Harmony\`.

## Publish Server as self-contained executable (optional)

```powershell
dotnet publish Server\Server.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o .\publish\Server
```

## Notes

- Upstream uses a password-protected `External/KSPLibraries/KSPLibraries.7z` in
  CI (`ZIPPASSWORD` env var). Local contributors use `Scripts/SetupKSPLibs.ps1` instead.
- KSP DLLs copied by `SetupKSPLibs.ps1` are covered by `.gitignore` via the
  `External/KSPLibraries/*.dll` pattern — they never appear in `git status`.
