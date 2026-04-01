# NekoGraph CLI Maintenance

This folder contains two things:

- Source: the `*.cs` files and `NekoGraph.Cli.csproj`
- Portable build: `dist/`

## Intended use

Use `dist/` when you want to copy the CLI into another project without rebuilding on that machine first.

The minimum portable set is:

- `nekograph-cli.exe`
- `nekograph-cli.dll`
- `nekograph-cli.deps.json`
- `nekograph-cli.runtimeconfig.json`

`nekograph-cli.pdb` is optional, but it is kept here for debugging.

## Rebuild and refresh dist

From the repository root:

```powershell
dotnet build Assets/Scripts/NekoGraph/Cli~/NekoGraph.Cli.csproj -c Debug
```

Then refresh `dist/` from `bin/Debug/net8.0/`:

```powershell
Copy-Item Assets/Scripts/NekoGraph/Cli~/bin/Debug/net8.0/nekograph-cli.exe Assets/Scripts/NekoGraph/Cli~/dist/ -Force
Copy-Item Assets/Scripts/NekoGraph/Cli~/bin/Debug/net8.0/nekograph-cli.dll Assets/Scripts/NekoGraph/Cli~/dist/ -Force
Copy-Item Assets/Scripts/NekoGraph/Cli~/bin/Debug/net8.0/nekograph-cli.deps.json Assets/Scripts/NekoGraph/Cli~/dist/ -Force
Copy-Item Assets/Scripts/NekoGraph/Cli~/bin/Debug/net8.0/nekograph-cli.runtimeconfig.json Assets/Scripts/NekoGraph/Cli~/dist/ -Force
Copy-Item Assets/Scripts/NekoGraph/Cli~/bin/Debug/net8.0/nekograph-cli.pdb Assets/Scripts/NekoGraph/Cli~/dist/ -Force
```

## Commit policy

Commit all source changes together with the refreshed `dist/` artifacts when the CLI behavior changes.

Do not commit:

- `bin/`
- `obj/`

## Verification

Basic smoke test:

```powershell
.\Assets\Scripts\NekoGraph\Cli~\dist\nekograph-cli.exe --version
```

Runner smoke test:

```powershell
.\Assets\Scripts\NekoGraph\Cli~\dist\nekograph-cli.exe --run --full cli_test_pack
```
