# Stenographer

Stenographer is a Windows 10/11 desktop app that captures speech locally, transcribes it with Whisper.cpp, and inserts the resulting text into whatever application currently has focus.

`Everything runs offline on your machine — no cloud services, no data leaving your PC! - that was the main motivation behind building this tool.`

---
**IMPORTANT**
This is 90% vibe-coded project for personal use and learning.(GH Copilot with GPT-5 Codex + perplexity-ask + context7)
I haven't written a line of code - just designed, split to development milestones and steered the project in right direction while testing for real use.
It works well enough for me, but it is not production-ready software. Use at your own risk.
Tested with gglm-base.bin model only.
---

## Feature highlights
- **Global hotkey capture (Configurable)** – press <kbd>Ctrl</kbd> + <kbd>Shift</kbd> + <kbd>Space</kbd> to start/stop recording from anywhere.
- **Offline transcription** – executes the Whisper.cpp binaries bundled with the app; just ship the required model files.
- **Smart text insertion** – uses UI Automation when possible, falls back to clipboard + `SendInput` while restoring clipboard contents afterwards.
- **Foreground window rebinding** – remembers the window that had focus when recording began and restores focus before pasting.
- **System tray operation** – minimizes to the notification area with status balloons for recording and errors.
- **Built-in model management** – browse installed Whisper models, download new `.bin` files from the official `ggerganov/whisper.cpp` catalog, and switch the active model without leaving the app.

## System requirements
- Windows 10 or Windows 11 (x64)
- .NET 8 Desktop Runtime (included when using the self-contained publish output)
- Working microphone
- ~200 MB of free disk space for Whisper models

## Repository layout
```
Stenographer/
├── Stenographer/                 # WPF application source
│   ├── Assets/                   # Embedded audio cues
│   ├── Core/, Services/, Models/ # Audio capture, hotkeys, insertion, configuration
│   └── Stenographer.csproj       # Project file (WinExe, net8.0-windows)
├── Models/whisper.cpp/           # Whisper executable + model (copied to output)
├── setup_models.bat              # Legacy offline bootstrapper (UI can download models now)
└── Readme.md
```

## Getting started
### 1. Restore NuGet packages
```powershell
dotnet restore
```

### 2. Launch the app in development
```powershell
dotnet run --project Stenographer/Stenographer.csproj
```
Press the global hotkey to record, then release it to trigger transcription.

Once the UI loads, open **Model Management → Open Model Manager…** (or use the sidebar button) to:
- see which Whisper models are already available locally,
- fetch the latest `.bin` models from https://huggingface.co/ggerganov/whisper.cpp/tree/main,
- download a model with live progress feedback,
- and choose the active model for new recordings.

> ℹ️ `setup_models.bat` is still in the repo for air-gapped scenarios, but normal usage should rely on the in-app manager.

## Build & deployment
### One-command release packaging
```powershell
.\build_installer.ps1 [-Configuration Release] [-RuntimeIdentifier win-x64] [-VersionTag v1.0.0]
```
The script restores packages, publishes a self-contained build, stages documentation + `setup_models.bat`, and writes both a dated folder and a `.zip` archive under `dist\`. Override the parameters to align with your release naming or target runtime.

### Manual steps (if you need to customize the flow)
```powershell
dotnet build --configuration Release
dotnet publish Stenographer/Stenographer.csproj --configuration Release --runtime win-x64 --self-contained true
Compress-Archive -Path .\Stenographer\bin\Release\net8.0-windows\win-x64\publish\* -DestinationPath Stenographer-package.zip
```
The publish output lives under `Stenographer\bin\Release\net8.0-windows\win-x64\publish`. Bundle this directory when distributing the app.

## Deployment checklist
- Copy the entire publish folder to the target machine.
- Ensure `Models\\whisper.cpp\\main.exe` is present. Whisper models can be bundled ahead of time **or** downloaded after install via the in-app Model Manager.
- For completely offline deployments you can still run `setup_models.bat` once to seed the default model.

## Troubleshooting
| Scenario | Suggestions |
| --- | --- |
| Hotkey will not register | Run Stenographer as administrator or choose a different hotkey in settings. |
| No audio devices detected | Confirm microphone permissions in Windows Privacy settings; reconnect the device and reopen the app. |
| Whisper process fails | Verify that `main.exe` exists under `Models\\whisper.cpp` and install/select a model through **Model Management → Open Model Manager…** (or, offline, run `setup_models.bat`). |
| Text not inserted | Target applications with restricted input may block automation. Check the status bar to see whether clipboard fallback failed and retry with the target focused. |



