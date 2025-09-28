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
├── setup_models.bat              # Downloads Whisper binaries/models
└── Readme.md
```

## Getting started
### 1. Restore NuGet packages
```powershell
dotnet restore
```

### 2. Download Whisper binaries & base model
Run the scripted setup from the repository root:
```powershell
setup_models.bat
```
The script downloads the latest Windows `main.exe` build of Whisper.cpp and the `ggml-base.bin` model into `Models\\whisper.cpp`.

### 3. Launch the app in development
```powershell
dotnet run --project Stenographer/Stenographer.csproj
```
Press the global hotkey to record, then release it to trigger transcription.

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
- Ensure `Models\\whisper.cpp\\main.exe` and `Models\\whisper.cpp\\ggml-base.bin` are present (they are included automatically when using the publish output).
- Run `setup_models.bat` on fresh installs if the models are not already bundled.

## Troubleshooting
| Scenario | Suggestions |
| --- | --- |
| Hotkey will not register | Run Stenographer as administrator or choose a different hotkey in settings. |
| No audio devices detected | Confirm microphone permissions in Windows Privacy settings; reconnect the device and reopen the app. |
| Whisper process fails | Verify that `main.exe` and `ggml-base.bin` exist under `Models\\whisper.cpp`. Re-run `setup_models.bat` if necessary. |
| Text not inserted | Target applications with restricted input may block automation. Check the status bar to see whether clipboard fallback failed and retry with the target focused. |



