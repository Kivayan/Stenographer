# Instrukcje dla AI - Budowa Systemu Transkrypcji Głosowej Windows

## Opis Aplikacji

**VoiceTranscriber** to aplikacja desktopowa dla Windows, która umożliwia błyskawiczną transkrypcję mowy na tekst z automatycznym wstawianiem do dowolnej aplikacji. System działa w tle i aktywuje się globalnym skrótem klawiszowym.

### Główne funkcjonalności:
- **Real-time voice capture** - przechwytywanie audio z mikrofonu po naciśnięciu hotkey
- **Local AI transcription** - przetwarzanie przez lokalny model Whisper.cpp (bez internetu)
- **Universal text insertion** - automatyczne wstawianie transkrypcji do aktywnego pola tekstowego w dowolnej aplikacji (Notepad, Word, VS Code, przeglądarki, etc.)
- **Background operation** - działa w system tray, gotowa do użycia w każdej chwili
- **Privacy-focused** - wszystkie dane pozostają lokalnie, zero transmisji sieciowej

### Workflow użytkownika:
1. Użytkownik pracuje w dowolnej aplikacji (np. pisze email w Gmail)
2. Naciśka **Ctrl+Shift+Space** (globalny hotkey)
3. Mówi do mikrofonu (np. "Dodaj spotkanie na jutro o 15:00")
4. Naciśka ponownie **Ctrl+Shift+Space** aby zatrzymać nagrywanie
5. Aplikacja automatycznie transkrybuje audio i wstawia tekst: "Dodaj spotkanie na jutro o 15:00" w miejsce kursora
6. Użytkownik kontynuuje pracę z wstawionym tekstem

### Przypadki użycia:
- **Dyktowanie długich tekstów** zamiast wpisywania
- **Szybkie notatki** podczas spotkań/rozmów telefonicznych
- **Accessibility** dla osób z problemami motorycznymi
- **Wielojęzyczna transkrypcja** (Whisper obsługuje 90+ języków)
- **Coding assistance** - dyktowanie komentarzy w kodzie

***

## Założenia Techniczne (Uproszczona Wersja)
- **Platforma:** Windows 10/11
- **Główna technologia:** C# + WPF
- **Model ASR:** Whisper.cpp (tylko ten na początek)
- **Audio API:** WASAPI dla real-time capture[1][2]
- **Text Injection:** Windows SendInput API[3][4]

***

## MILESTONE 1: Podstawowa Struktura Projektu
**Cel:** Stworzenie działającej aplikacji WPF z podstawową strukturą

### 1.1 Struktura projektu
```
VoiceTranscriber/
├── VoiceTranscriber.sln
├── VoiceTranscriber/
│   ├── VoiceTranscriber.csproj
│   ├── App.xaml
│   ├── MainWindow.xaml
│   ├── Core/
│   │   ├── AudioCapture.cs
│   │   ├── WhisperService.cs
│   │   └── TextInsertion.cs
│   ├── Services/
│   │   ├── HotkeyManager.cs
│   │   ├── WindowManager.cs
│   │   └── ConfigurationService.cs
│   ├── Models/
│   │   └── TranscriptionResult.cs
│   └── Utils/
│       └── AudioUtils.cs
├── Models/                    # Folder dla modeli
│   └── whisper.cpp/          # Whisper modele i executable
└── TestAudio/               # Folder na test MP3
    └── test.mp3            # Plik testowy od użytkownika
```

### 1.2 Dependencies (NuGet packages)
```xml
<PackageReference Include="NAudio" Version="2.2.1" />
<PackageReference Include="NAudio.WinMM" Version="2.2.1" />
<PackageReference Include="Microsoft.Xaml.Behaviors.Wpf" Version="1.1.122" />
<PackageReference Include="System.Management" Version="8.0.0" />
```

### 1.3 Test Milestone 1
- Aplikacja się uruchamia
- MainWindow wyświetla podstawowy UI z napisem "VoiceTranscriber Ready"
- Wszystkie klasy są zdefiniowane z pustymi metodami
- **Test:** `dotnet build` się wykonuje bez błędów

***

## MILESTONE 2: Audio Capture Module
**Cel:** Implementacja przechwytywania audio z mikrofonu przez WASAPI[2][5][1]

### 2.1 AudioCapture.cs implementation
```csharp
using NAudio.Wave;
using NAudio.CoreAudioApi;

public class AudioCapture
{
    private WasapiCapture audioCapture;
    private WaveFileWriter waveWriter;
    private string tempAudioFile;
    private bool isRecording;

    public event Action<string> RecordingComplete; // Zwraca path do audio file

    public void StartCapture(int deviceIndex = -1)
    {
        tempAudioFile = Path.GetTempFileName() + ".wav";

        // WASAPI implementation dla real-time capture
        var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        audioCapture = new WasapiCapture(device);
        waveWriter = new WaveFileWriter(tempAudioFile, audioCapture.WaveFormat);

        audioCapture.DataAvailable += OnDataAvailable;
        audioCapture.RecordingStopped += OnRecordingStopped;

        isRecording = true;
        audioCapture.StartRecording();
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
    }

    private void OnRecordingStopped(object sender, StoppedEventArgs e)
    {
        waveWriter?.Close();
        waveWriter?.Dispose();
        audioCapture?.Dispose();

        RecordingComplete?.Invoke(tempAudioFile);
    }

    public void StopCapture()
    {
        if (isRecording)
        {
            isRecording = false;
            audioCapture?.StopRecording();
        }
    }

    public List<MMDevice> GetAvailableDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
    }
}
```

### 2.2 Test Milestone 2
- Aplikacja wykrywa dostępne mikrofony i wyświetla je w dropdown
- Przycisk "Start Recording" rozpoczyna nagrywanie (zmienia się na "Stop Recording")
- Po zatrzymaniu zapisuje audio do pliku WAV
- **Test:** Nagraj 5-sekundowy audio, sprawdź czy plik WAV jest prawidłowy w Windows Media Player

***

## MILESTONE 3: Whisper.cpp Integration
**Cel:** Integracja z Whisper.cpp przez proces subprocess[6][7][8]

### 3.1 WhisperService.cs implementation
```csharp
using System.Diagnostics;

public class WhisperService
{
    private readonly string whisperExecutablePath;
    private readonly string modelPath;

    public WhisperService()
    {
        whisperExecutablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "whisper.cpp", "main.exe");
        modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "whisper.cpp", "ggml-base.bin");
    }

    public async Task<string> TranscribeAsync(string audioFilePath)
    {
        if (!File.Exists(whisperExecutablePath))
            throw new FileNotFoundException($"Whisper executable not found: {whisperExecutablePath}");

        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Whisper model not found: {modelPath}");

        var processInfo = new ProcessStartInfo
        {
            FileName = whisperExecutablePath,
            Arguments = $"-m \"{modelPath}\" -f \"{audioFilePath}\" --output-txt --no-timestamps",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(whisperExecutablePath)
        };

        using var process = Process.Start(processInfo);

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new Exception($"Whisper process failed: {error}");

        // Whisper tworzy plik .txt z tym samym nazwą co input audio
        var txtFile = Path.ChangeExtension(audioFilePath, ".txt");
        if (File.Exists(txtFile))
        {
            var transcription = await File.ReadAllTextAsync(txtFile);
            File.Delete(txtFile); // Cleanup
            return transcription.Trim();
        }

        return output.Trim();
    }
}
```

### 3.2 Whisper Setup Requirements
W folderze `Models/whisper.cpp/` umieść:
- `main.exe` - whisper.cpp executable dla Windows
- `ggml-base.bin` - model Whisper base (~142MB)[9]

### 3.3 Basic UI dla testowania
```xaml
<!-- MainWindow.xaml -->
<Grid>
    <StackPanel Margin="20">
        <TextBlock Text="VoiceTranscriber" FontSize="24" FontWeight="Bold" HorizontalAlignment="Center"/>

        <ComboBox Name="DeviceComboBox" Margin="0,20,0,10" DisplayMemberPath="FriendlyName"/>

        <Button Name="RecordButton" Content="Start Recording" Click="RecordButton_Click"
                Height="40" FontSize="16" Margin="0,10"/>

        <Button Name="TestButton" Content="Test with MP3" Click="TestButton_Click"
                Height="30" Margin="0,10"/>

        <TextBlock Name="StatusText" Text="Ready" FontWeight="Bold" Margin="0,20,0,10"/>

        <ScrollViewer Height="200" Margin="0,10">
            <TextBox Name="ResultTextBox" TextWrapping="Wrap" IsReadOnly="True"
                     Background="LightGray" Padding="10"/>
        </ScrollViewer>
    </StackPanel>
</Grid>
```

### 3.4 Test Milestone 3
- Przycisk "Test with MP3" ładuje test.mp3 i wykonuje transkrypcję
- Rezultat wyświetla się w TextBox
- Status pokazuje progress: "Ready" → "Transcribing..." → "Complete"
- **Test:** Aplikacja poprawnie transkrybuje test.mp3 i wyświetla tekst

***

## MILESTONE 4: Global Hotkey System
**Cel:** Implementacja globalnych skrótów klawiszowych[10][11][12]

### 4.1 HotkeyManager.cs implementation
```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;

public class HotkeyManager
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HOTKEY_ID = 9000;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_SPACE = 0x20;

    private IntPtr windowHandle;
    private bool isRecording = false;

    public event Action StartRecording;
    public event Action StopRecording;

    public bool RegisterHotkeys(IntPtr hwnd)
    {
        windowHandle = hwnd;
        // Register Ctrl+Shift+Space
        return RegisterHotKey(hwnd, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_SPACE);
    }

    public void UnregisterHotkeys()
    {
        if (windowHandle != IntPtr.Zero)
        {
            UnregisterHotKey(windowHandle, HOTKEY_ID);
        }
    }

    public IntPtr HotkeyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            if (isRecording)
            {
                StopRecording?.Invoke();
                isRecording = false;
            }
            else
            {
                StartRecording?.Invoke();
                isRecording = true;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }
}
```

### 4.2 Integration z MainWindow
```csharp
public partial class MainWindow : Window
{
    private HotkeyManager hotkeyManager;
    private AudioCapture audioCapture;
    private WhisperService whisperService;

    public MainWindow()
    {
        InitializeComponent();
        InitializeServices();
    }

    private void InitializeServices()
    {
        hotkeyManager = new HotkeyManager();
        audioCapture = new AudioCapture();
        whisperService = new WhisperService();

        hotkeyManager.StartRecording += OnHotkeyStartRecording;
        hotkeyManager.StopRecording += OnHotkeyStopRecording;
        audioCapture.RecordingComplete += OnRecordingComplete;

        // Load available devices
        DeviceComboBox.ItemsSource = audioCapture.GetAvailableDevices();
        DeviceComboBox.SelectedIndex = 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        var hwnd = helper.Handle;

        // Register global hotkey
        if (!hotkeyManager.RegisterHotkeys(hwnd))
        {
            MessageBox.Show("Failed to register global hotkey Ctrl+Shift+Space");
        }

        // Hook dla hotkey messages
        var source = HwndSource.FromHwnd(hwnd);
        source.AddHook(hotkeyManager.HotkeyHook);
    }

    private void OnHotkeyStartRecording()
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Recording... (Press Ctrl+Shift+Space to stop)";
            StatusText.Foreground = Brushes.Red;
        });

        audioCapture.StartCapture();
    }

    private void OnHotkeyStopRecording()
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Processing...";
            StatusText.Foreground = Brushes.Orange;
        });

        audioCapture.StopCapture();
    }

    private async void OnRecordingComplete(string audioFilePath)
    {
        try
        {
            var transcription = await whisperService.TranscribeAsync(audioFilePath);

            Dispatcher.Invoke(() =>
            {
                ResultTextBox.Text = transcription;
                StatusText.Text = "Ready";
                StatusText.Foreground = Brushes.Green;
            });

            // Cleanup temp file
            File.Delete(audioFilePath);
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"Error: {ex.Message}";
                StatusText.Foreground = Brushes.Red;
            });
        }
    }
}
```

### 4.3 Test Milestone 4
- Przytrzymanie Ctrl+Shift+Space rozpoczyna nagrywanie (status zmienia się na czerwony)
- Zwolnienie kombinacji zatrzymuje nagrywanie i rozpoczyna transkrypcję
- Hotkey działa globalnie (nawet gdy aplikacja nie ma focus)
- Z listy Language możesz zostawić Auto detect lub wskazać konkretny język (kod ISO-639-1), który zostanie przekazany do whisper.cpp
- **Test:** Otwórz Notepad, przytrzymaj Ctrl+Shift+Space podczas nagrywania, puść skrót i sprawdź czy transkrypcja pojawia się w aplikacji

***

## MILESTONE 5: Text Insertion System
**Cel:** Automatyczne wstawianie tekstu do aktywnej aplikacji[4][13][3]

### 5.1 TextInsertion.cs implementation
```csharp
using System.Runtime.InteropServices;

public class TextInsertion
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;

    public InsertionMethod InsertText(string text)
    {
        if (TryInsertWithUIAutomation(text))
        {
            return InsertionMethod.UiAutomation;
        }

        return InsertViaClipboard(text) ? InsertionMethod.Clipboard : InsertionMethod.None;
    }

    private static bool TryInsertWithUIAutomation(string text)
    {
        var element = AutomationElement.FocusedElement;
        if (element == null)
        {
            return false;
        }

        if (
            element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObj)
            && valuePatternObj is ValuePattern valuePattern
            && !valuePattern.Current.IsReadOnly
            && string.IsNullOrEmpty(valuePattern.Current.Value)
        )
        {
            valuePattern.SetValue(text);
            return true;
        }

        return false;
    }

    private bool InsertViaClipboard(string text)
    {
        var previousClipboard = string.Empty;
        var hadClipboard = false;

        try
        {
            if (Clipboard.ContainsText())
            {
                previousClipboard = Clipboard.GetText();
                hadClipboard = true;
            }
        }
        catch
        {
            hadClipboard = false;
        }

        Clipboard.SetDataObject(text, true);

        var inputs = new[]
        {
            CreateVirtualKeyInput(VK_CONTROL, keyUp: false),
            CreateVirtualKeyInput(VK_V, keyUp: false),
            CreateVirtualKeyInput(VK_V, keyUp: true),
            CreateVirtualKeyInput(VK_CONTROL, keyUp: true),
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());

        Task.Delay(2000).ContinueWith(_ =>
        {
            try
            {
                if (hadClipboard)
                {
                    Clipboard.SetText(previousClipboard);
                }
                else
                {
                    Clipboard.Clear();
                }
            }
            catch
            {
            }
        });

        return true;
    }

    private static INPUT CreateVirtualKeyInput(ushort virtualKey, bool keyUp)
    {
        return new INPUT
        {
            type = INPUT_KEYBOARD,
            union = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0u,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
    }
}
```

> **Jak działa wstawianie tekstu:** Aplikacja najpierw próbuje UI Automation (`ValuePattern.SetValue`) wyłącznie wtedy, gdy pole docelowe udostępnia wzorzec i jest puste. Dzięki temu nie nadpisujemy istniejącej zawartości. Jeżeli UI Automation nie jest dostępne, aplikacja kopiuje tekst do schowka, wysyła `Ctrl+V` przez `SendInput`, a po ~2 sekundach przywraca poprzednią zawartość schowka (albo go czyści, jeśli wcześniej był pusty).
>
> **Diagnostyka opcjonalna:** Jeśli w przyszłości zajdzie potrzeba analizy problemów z wklejaniem, można tymczasowo wymusić ścieżkę schowka i włączyć logowanie (`System.Diagnostics.Debug.WriteLine`) w `TextInsertion.cs`, aby obserwować status `SetDataObject`, wyniki `SendInput` oraz ostatnie kody błędów Win32.

### 5.2 WindowManager.cs implementation
```csharp
public class WindowManager
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public string GetActiveWindowTitle()
    {
        var hwnd = GetForegroundWindow();
        var text = new StringBuilder(256);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    public string GetActiveProcessName()
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out uint processId);

        try
        {
            var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }
}
```

### 5.3 Integration z Main Workflow
```csharp
private async void OnRecordingComplete(string audioFilePath)
{
    try
    {
        var transcription = await whisperService.TranscribeAsync(audioFilePath);

        Dispatcher.Invoke(() =>
        {
            ResultTextBox.Text = transcription;
            StatusText.Text = "Inserting text...";
            StatusText.Foreground = Brushes.Blue;
        });

        // Get info about active window dla logowania
        var activeWindow = windowManager.GetActiveWindowTitle();
        var activeProcess = windowManager.GetActiveProcessName();

        // Insert text do aktywnej aplikacji
        textInsertion.InsertText(transcription);

        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"Inserted to: {activeProcess}";
            StatusText.Foreground = Brushes.Green;
        });

        // Cleanup
        File.Delete(audioFilePath);
    }
    catch (Exception ex)
    {
        Dispatcher.Invoke(() =>
        {
            StatusText.Text = $"Error: {ex.Message}";
            StatusText.Foreground = Brushes.Red;
        });
    }
}
```

- Przed rozpoczęciem nagrania zapamiętywane jest aktywne okno (tytuł + proces). Po zakończeniu transkrypcji aplikacja próbuje ponownie je uaktywnić i dopiero wtedy wstrzykuje tekst, dzięki czemu notatnik / edytor otrzymuje treść nawet gdy VoiceTranscriber pozostaje w tle.

### 5.4 Test Milestone 5
- Otwórz Notepad, ustaw kursor w pustym dokumencie
- Naciśnij Ctrl+Shift+Space, nagraj "Hello world", naciśnij ponownie Ctrl+Shift+Space
- Tekst "Hello world" automatycznie pojawia się w Notepad
- Sprawdź, czy hotkey trzymany w innym oknie (np. Gmail w Chrome) nadal nagrywa i wstawia tekst, nawet jeśli aplikacja VoiceTranscriber nie ma fokusu
- **Test:** Sprawdź z różnymi aplikacjami (Word, VS Code, Chrome/Gmail, WhatsApp Web)

***

## MILESTONE 6: System Tray & Background Operation
**Cel:** Aplikacja działa w tle jako system tray application

### 6.1 System Tray Implementation
```csharp
// Add to MainWindow.xaml.cs
private System.Windows.Forms.NotifyIcon notifyIcon;

private void InitializeSystemTray()
{
    notifyIcon = new System.Windows.Forms.NotifyIcon();
    notifyIcon.Icon = new System.Drawing.Icon("icon.ico"); // Add app icon
    notifyIcon.Text = "VoiceTranscriber - Ready";
    notifyIcon.Visible = true;

    // Context menu
    var contextMenu = new System.Windows.Forms.ContextMenuStrip();
    contextMenu.Items.Add("Show", null, (s, e) => ShowWindow());
    contextMenu.Items.Add("Exit", null, (s, e) => Application.Current.Shutdown());
    notifyIcon.ContextMenuStrip = contextMenu;

    notifyIcon.DoubleClick += (s, e) => ShowWindow();
}

private void ShowWindow()
{
    Show();
    WindowState = WindowState.Normal;
    Activate();
}

protected override void OnStateChanged(EventArgs e)
{
    if (WindowState == WindowState.Minimized)
    {
        Hide();
        notifyIcon.ShowBalloonTip(1000, "VoiceTranscriber",
            "Application minimized to tray. Use Ctrl+Shift+Space for voice input.",
            System.Windows.Forms.ToolTipIcon.Info);
    }
    base.OnStateChanged(e);
}
```

### 6.2 Enhanced Status Updates
```csharp
private void UpdateTrayStatus(string status, bool isRecording = false)
{
    var icon = isRecording ? "🔴" : "🎤";
    notifyIcon.Text = $"VoiceTranscriber - {status}";

    // Optional: Show balloon tip dla ważnych eventów
    if (isRecording)
    {
        notifyIcon.ShowBalloonTip(1000, "Recording",
            "Voice recording started. Press Ctrl+Shift+Space to stop.",
            System.Windows.Forms.ToolTipIcon.Info);
    }
}
```

### 6.3 Test Milestone 6
- Aplikacja minimalizuje się do system tray
- Ikona w tray pokazuje status (ready/recording)
- Right-click na ikonie pokazuje menu (Show/Exit)
- Hotkey działa nawet gdy aplikacja jest zminimalizowana
- **Test:** Zminimalizuj aplikację, użyj Ctrl+Shift+Space, sprawdź czy transkrypcja działa

***

## MILESTONE 7: Build & Deployment
**Cel:** Przygotowanie aplikacji do dystrybucji

### 7.1 Project Configuration
```xml
<!-- VoiceTranscriber.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
        <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <UseWindowsForms>true</UseWindowsForms>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <PublishSingleFile>false</PublishSingleFile>
    <ApplicationIcon>icon.ico</ApplicationIcon>
    <AssemblyTitle>VoiceTranscriber</AssemblyTitle>
    <AssemblyDescription>Local voice-to-text transcription tool</AssemblyDescription>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="NAudio" Version="2.2.1" />
    <PackageReference Include="NAudio.WinMM" Version="2.2.1" />
  </ItemGroup>

  <ItemGroup>
        <PackageReference Include="Microsoft.Xaml.Behaviors.Wpf" Version="1.1.122" />
    <Content Include="Models\**\*" CopyToOutputDirectory="PreserveNewest" />
    <Content Include="icon.ico" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

### 7.2 Deployment Package Structure
```
VoiceTranscriber-v1.0/
├── VoiceTranscriber.exe           # Main executable
├── VoiceTranscriber.dll           # Main assembly
├── NAudio.dll                     # Dependencies
├── NAudio.WinMM.dll
├── icon.ico
├── Models/
│   └── whisper.cpp/
│       ├── main.exe               # Whisper executable
│       └── ggml-base.bin          # Whisper model
├── TestAudio/
│   └── sample.mp3                 # Sample audio dla testów
├── README.md                      # User instructions
└── setup_models.bat              # Model download script
```

### 7.3 Model Download Script (setup_models.bat)
```batch
@echo off
echo VoiceTranscriber - Model Setup
echo ================================

if not exist "Models\whisper.cpp" mkdir "Models\whisper.cpp"

echo Downloading Whisper executable...
powershell -Command "Invoke-WebRequest -Uri 'https://github.com/ggerganov/whisper.cpp/releases/download/v1.4.2/whisper-bin-x64.zip' -OutFile 'whisper-temp.zip'"

echo Extracting Whisper...
powershell -Command "Expand-Archive -Path 'whisper-temp.zip' -DestinationPath 'Models\whisper.cpp' -Force"
del whisper-temp.zip

echo Downloading Whisper base model...
powershell -Command "Invoke-WebRequest -Uri 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin' -OutFile 'Models\whisper.cpp\ggml-base.bin'"

echo Setup complete! You can now run VoiceTranscriber.exe
pause
```

### 7.4 User README.md
```markdown
# VoiceTranscriber

Local voice-to-text transcription tool for Windows.

## Features
- Global hotkey activation (Ctrl+Shift+Space)
- Real-time voice transcription using Whisper AI
- Automatic text insertion into any application
- Works completely offline (no internet required)
- System tray operation

## Setup
1. Run `setup_models.bat` to download required AI models
2. Launch `VoiceTranscriber.exe`
3. Application will minimize to system tray

## Usage
1. Place cursor in any text field (Notepad, Word, browser, etc.)
2. Press `Ctrl+Shift+Space` to start recording
3. Speak clearly into your microphone
4. Press `Ctrl+Shift+Space` again to stop and transcribe
5. Text will be automatically inserted at cursor position

## System Requirements
- Windows 10/11
- Microphone
- ~200MB free space for AI models
- .NET 8 Runtime (included in package)

## Troubleshooting
- If hotkey doesn't work, try running as Administrator
- Check microphone permissions in Windows Settings
- Ensure antivirus isn't blocking the application
```

### 7.5 Build Commands
```bash
# Development build
dotnet build --configuration Release

# Publish dla deployment
dotnet publish --configuration Release --runtime win-x64 --self-contained true

# Create deployment package
7z a VoiceTranscriber-v1.0.zip bin\Release\net8.0-windows\win-x64\publish\*
```

### 7.6 Test Milestone 7
- Package się tworzy bez błędów
- setup_models.bat poprawnie ściąga Whisper
- Aplikacja działa na czystej maszynie Windows
- Wszystkie funkcjonalności działają po deployment
- **Test:** Deployment na nowej maszynie, pełny workflow test

***

## Final Testing Protocol

### Integration Tests
1. **Audio Chain Test:** Mikrofon → Capture → WAV File → Whisper → Text
2. **Cross-App Test:** Test z 5+ różnymi aplikacjami (Notepad, Word, Chrome, VS Code, Discord)
3. **Performance Test:** Measure latency dla każdego kroku pipeline
4. **Stress Test:** 50 kolejnych transkrypcji bez restartu aplikacji
5. **Multi-language Test:** Test z językami polskim, angielskim, niemieckim

### User Acceptance Criteria
- ✅ Hotkey response time < 500ms
- ✅ Recording quality - clear audio capture bez dropout
- ✅ Transcription accuracy > 85% dla czystego audio w języku angielskim/polskim
- ✅ Memory usage < 300MB podczas idle, < 800MB podczas transcription
- ✅ Compatible z Windows 10/11 (both x64)
- ✅ Works with major applications: Office, browsers, IDE, chat aplikacje
- ✅ Graceful error handling dla missing models, mic issues, permissions

### Input Requirements
- **test.mp3** - 10-30 second audio file z czystą mową w języku polskim lub angielskim
- **Models auto-download** - setup script automatycznie ściąga Whisper.cpp i base model

System zapewnia kompletną funkcjonalność voice-to-text z lokalnym przetwarzaniem, optymalizowany pod codzienne użytkowanie na Windows.

