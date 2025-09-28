# Copilot Instructions

## Project purpose
- `Stenographer` is a .NET 8 WPF desktop app that captures speech locally and inserts text into the currently focused Windows application.
- All speech recognition runs offline through `whisper.cpp` binaries located under `Models/whisper.cpp` and copied by the project file.
- The public README still references the legacy name "VoiceTranscriber"; the actual namespace, assembly, and runtime layout use "Stenographer".

## Architecture map
- `MainWindow.xaml.cs` coordinates device selection, hotkey lifecycle, recording state, Whisper calls, and post-transcription insertion feedback in the UI.
- `Core/AudioCapture.cs` wraps `WasapiCapture`, writes temporary WAV files, and raises `RecordingComplete` on a background thread; it prefers the default communications endpoint when no device index is provided.
- `Core/WhisperService.cs` resamples audio to 16 kHz mono via `MediaFoundationResampler`, shells out to `main.exe`, and cleans up temporary `.txt` outputs and intermediate WAV files.
- `Core/TextInsertion.cs` first attempts UI Automation injection, then falls back to clipboard-driven `SendInput`; clipboard contents are restored asynchronously, and diagnostics live in `LastDiagnosticMessage`.
- `Services/HotkeyManager.cs` combines `RegisterHotKey` with a low-level keyboard hook so you receive both press and release events (`HotkeyPressed`/`HotkeyReleased`).
- `Services/WindowManager.cs` caches the foreground HWND before recording and restores focus via `AttachThreadInput` + `SetForegroundWindow` before pasting.
- `Services/ConfigurationService.cs` persists `%APPDATA%\Stenographer\config.json` (migrating legacy `%APPDATA%\Stengorapher`) containing `Hotkey` and `TranscriptionLanguage` settings.

## Whisper integration
- Ship `main.exe` and `ggml-base.bin` under `Models/whisper.cpp`; `Stenographer.csproj` marks the entire folder as `Content` with `CopyToOutputDirectory=PreserveNewest`.
- `TranscribeAndDisplayAsync` forwards the selected language code so Whisper omits `-l` when "Auto detect" (empty string) is chosen.
- Whisper failures surface as `InvalidOperationException` with stderr content; missing binaries throw `FileNotFoundException` before the process starts.

## Recording and hotkeys
- The default shortcut is `Ctrl+Shift+Space` (`HotkeySettings.CreateDefault()`); when reassigning, registration failures leave the status text orange/red and revert to the prior combo.
- Holding the hotkey plays `Assets/Audio/record_start.wav`, begins capture, and latches the foreground window; releasing plays `record_stop.wav`, stops capture, and queues transcription.
- Device selection in `MainWindow` is driven by the live `MMDevice` list; disable the combo while recording to avoid disposing active devices.

## Text insertion flow
- `TryInsertTranscriptionAsync` retries the saved HWND and, if invalid, falls back to the current foreground window before attempting insertion.
- UI Automation only succeeds on editable, empty elements; otherwise clipboard paste occurs (`InsertionMethod.Clipboard`), typically after a 180 ms focus delay.
- Status updates surface which path succeeded, making it easy to debug clipboard restrictions or refused focus changes.

## Build, run, and verify
- Restore/build with `dotnet restore` followed by `dotnet run --project Stenographer/Stenographer.csproj` on Windows (Media Foundation must be available for the resampler).
- There are no automated tests; validate changes by recording real audio, ensuring transcription text appears in the UI, and confirming insertion into a target like Notepad while watching `StatusText` feedback.
