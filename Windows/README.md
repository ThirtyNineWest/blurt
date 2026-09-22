# Blurt for Windows

A C#/.NET 10 WPF port of Blurt, the AssemblyAI dictation app, for Windows 11. It dictates
through the same AssemblyAI request and pipeline rules as the macOS app, adapted to WASAPI,
`WH_KEYBOARD_LL`, UI Automation, and the Win32 clipboard.

## Build

```powershell
dotnet build Windows/Blurt.sln -warnaserror
```

## Test

```powershell
dotnet test Windows/Blurt.sln
```

## Run

```powershell
dotnet run --project Windows/src/Blurt.App
```

## Publish

```powershell
scripts/windows/publish.ps1 [-Configuration Release] [-SelfContained]
```

Framework-dependent builds land in `Windows/publish/fd`; self-contained, single-file builds
land in `Windows/publish/sc`.
