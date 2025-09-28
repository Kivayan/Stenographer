using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using Stenographer.Core;
using Stenographer.Models;
using Stenographer.Services;

namespace Stenographer;

public partial class MainWindow : Window
{
    private static readonly List<LanguageOption> SupportedLanguages = new()
    {
        new LanguageOption("Auto detect", string.Empty),
        new LanguageOption("English (en)", "en"),
        new LanguageOption("Polish (pl)", "pl"),
        new LanguageOption("Spanish (es)", "es"),
        new LanguageOption("German (de)", "de"),
        new LanguageOption("French (fr)", "fr"),
        new LanguageOption("Italian (it)", "it"),
        new LanguageOption("Portuguese (pt)", "pt"),
        new LanguageOption("Russian (ru)", "ru"),
        new LanguageOption("Chinese (zh)", "zh"),
        new LanguageOption("Japanese (ja)", "ja"),
        new LanguageOption("Korean (ko)", "ko"),
    };

    private readonly AudioCapture _audioCapture;
    private readonly List<MMDevice> _audioDevices = new();
    private readonly WhisperService _whisperService;
    private readonly HotkeyManager _hotkeyManager;
    private readonly ConfigurationService _configurationService;
    private readonly TextInsertion _textInsertion;
    private readonly WindowManager _windowManager;
    private const string StartSoundFileName = "record_start.wav";
    private const string StopSoundFileName = "record_stop.wav";
    private readonly SoundPlayer _startSoundPlayer;
    private readonly SoundPlayer _stopSoundPlayer;
    private IntPtr _targetWindowHandle = IntPtr.Zero;
    private string _targetWindowTitle = string.Empty;
    private string _targetProcessName = string.Empty;
    private HotkeySettings _currentHotkey;
    private LanguageOption _currentLanguageOption;
    private bool _suppressLanguageSelectionChange;
    private bool _isListeningForHotkey;
    private HwndSource _windowSource;
    private IntPtr _windowHandle;
    private Brush _defaultHotkeyStatusBrush;
    private Brush _defaultLanguageStatusBrush;
    private bool _isRecording;
    private bool _isTranscribing;
    private string _currentRecordingPath = string.Empty;
    private bool _cleanupInvoked;

    public MainWindow()
    {
        InitializeComponent();
        _audioCapture = new AudioCapture();
        _whisperService = new WhisperService();
        _configurationService = new ConfigurationService();
        _textInsertion = new TextInsertion();
        _windowManager = new WindowManager();
        _hotkeyManager = new HotkeyManager();
        _startSoundPlayer = CreateSoundPlayer(StartSoundFileName);
        _stopSoundPlayer = CreateSoundPlayer(StopSoundFileName);

        _audioCapture.RecordingComplete += OnRecordingComplete;
        _hotkeyManager.HotkeyPressed += OnGlobalHotkeyPressed;
        _hotkeyManager.HotkeyReleased += OnGlobalHotkeyReleased;

        _configurationService.Load();
        _currentHotkey = (
            _configurationService.Configuration?.Hotkey ?? HotkeySettings.CreateDefault()
        ).Clone();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        TestButton.IsEnabled = ResolveSampleAudioPath() != null;

        _defaultHotkeyStatusBrush = HotkeyStatusText.Foreground;
        _defaultLanguageStatusBrush = LanguageStatusText.Foreground;
        InitializeLanguageSelection();
        UpdateHotkeyDisplay();
        RestoreHotkeyHint();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        _windowHandle = helper.Handle;
        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(_hotkeyManager.HotkeyHook);

        if (!TryRegisterHotkey(_currentHotkey))
        {
            HotkeyStatusText.Text =
                "Failed to register the saved hotkey. Click Change to set a new one.";
            HotkeyStatusText.Foreground = Brushes.OrangeRed;
        }
        else
        {
            RestoreHotkeyHint();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        CleanupResources();
        base.OnClosed(e);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshAudioDevices();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        CleanupResources();
    }

    private void RefreshAudioDevices()
    {
        DisposeAudioDevices();

        try
        {
            var devices = _audioCapture.GetAvailableDevices();

            foreach (var device in devices)
            {
                _audioDevices.Add(device);
            }

            DeviceComboBox.ItemsSource = _audioDevices;
            DeviceComboBox.SelectedIndex = _audioDevices.Count > 0 ? 0 : -1;
            DeviceComboBox.IsEnabled = _audioDevices.Count > 0;

            RecordButton.Content = "Start Recording";
            RecordButton.IsEnabled = _audioDevices.Count > 0;
            RecordingFileText.Text =
                _audioDevices.Count > 0
                    ? "Ready to record. Press Start Recording."
                    : "No input devices detected.";

            ResultTextBox.Text =
                _audioDevices.Count > 0 ? "Recording not started." : "Waiting for an audio device.";

            StatusText.Text =
                _audioDevices.Count > 0 ? "Microphone ready" : "Connect a microphone to begin";
            StatusText.Foreground = _audioDevices.Count > 0 ? Brushes.Green : Brushes.OrangeRed;

            TestButton.IsEnabled =
                !_isRecording && !_isTranscribing && ResolveSampleAudioPath() != null;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Device error: {ex.Message}";
            StatusText.Foreground = Brushes.Red;
            RecordButton.IsEnabled = false;
            TestButton.IsEnabled = ResolveSampleAudioPath() != null;
        }
    }

    private void InitializeLanguageSelection()
    {
        _suppressLanguageSelectionChange = true;

        LanguageComboBox.ItemsSource = SupportedLanguages;

        var savedCode = _configurationService.Configuration?.TranscriptionLanguage ?? string.Empty;
        var selected =
            SupportedLanguages.FirstOrDefault(option =>
                string.Equals(option.Code, savedCode, StringComparison.OrdinalIgnoreCase)
            ) ?? SupportedLanguages[0];

        LanguageComboBox.SelectedItem = selected;
        _currentLanguageOption = selected;

        _suppressLanguageSelectionChange = false;

        RestoreLanguageHint();
    }

    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            StopRecording();
        }
        else
        {
            StartRecording();
        }
    }

    private void EditHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        BeginHotkeyListening();
    }

    private void ResetHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyHotkeySelection(
            HotkeySettings.CreateDefault(),
            persist: true,
            showSuccessMessage: true
        );
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageSelectionChange)
        {
            return;
        }

        if (LanguageComboBox.SelectedItem is not LanguageOption selected)
        {
            return;
        }

        _currentLanguageOption = selected;

        var normalizedCode = string.IsNullOrWhiteSpace(selected.Code)
            ? string.Empty
            : selected.Code.Trim().ToLowerInvariant();

        _configurationService.Configuration.TranscriptionLanguage = normalizedCode;

        try
        {
            _configurationService.Save();
            RestoreLanguageHint();
        }
        catch (Exception ex)
        {
            RestoreLanguageHint(
                $"Language saved for this session, but couldn't be persisted: {ex.Message}",
                Brushes.OrangeRed
            );
        }
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            StatusText.Text = "Stop the current recording before running the sample test.";
            StatusText.Foreground = Brushes.OrangeRed;
            return;
        }

        if (_isTranscribing)
        {
            StatusText.Text = "Transcription already in progress.";
            StatusText.Foreground = Brushes.OrangeRed;
            return;
        }

        var samplePath = ResolveSampleAudioPath();
        if (string.IsNullOrEmpty(samplePath) || !File.Exists(samplePath))
        {
            StatusText.Text = "Sample audio file not found in the TestAudio folder.";
            StatusText.Foreground = Brushes.Red;
            RecordingFileText.Text = "Add a sample audio file (e.g. test.mp3) to TestAudio.";
            TestButton.IsEnabled = false;
            return;
        }

        RecordingFileText.Text = $"Sample file: {samplePath}";
        ResultTextBox.Text = string.Empty;

        await TranscribeAndDisplayAsync(
            samplePath,
            deleteAfterProcessing: false,
            contextLabel: "sample audio",
            initialMessage: $"Sample file: {samplePath}"
        );
    }

    private void StartRecording()
    {
        if (_isTranscribing)
        {
            StatusText.Text = "Please wait for the current transcription to finish.";
            StatusText.Foreground = Brushes.OrangeRed;
            return;
        }

        if (_audioDevices.Count == 0)
        {
            StatusText.Text = "No audio devices available.";
            StatusText.Foreground = Brushes.Red;
            return;
        }

        var deviceIndex = DeviceComboBox.SelectedIndex;
        if (deviceIndex < 0 || deviceIndex >= _audioDevices.Count)
        {
            deviceIndex = -1; // fallback to default device
        }

        EnsureTargetWindowCaptured();

        try
        {
            DeviceComboBox.IsEnabled = false;
            _audioCapture.StartCapture(deviceIndex);
            _isRecording = true;
            _currentRecordingPath = string.Empty;
            TestButton.IsEnabled = false;

            RecordButton.Content = "Stop Recording";
            StatusText.Text = "Recording... Press Stop Recording when finished.";
            StatusText.Foreground = Brushes.Red;
            RecordingFileText.Text = "Recording in progress...";
            ResultTextBox.Text = string.Empty;
        }
        catch (Exception ex)
        {
            _isRecording = false;
            StatusText.Text = $"Capture error: {ex.Message}";
            StatusText.Foreground = Brushes.Red;
            DeviceComboBox.IsEnabled = _audioDevices.Count > 0;
            TestButton.IsEnabled = ResolveSampleAudioPath() != null;
        }
    }

    private void StopRecording()
    {
        try
        {
            _audioCapture.StopCapture();
            RecordButton.IsEnabled = false;
            StatusText.Text = "Finishing recording...";
            StatusText.Foreground = Brushes.DarkOrange;
            RecordingFileText.Text = "Processing recording...";
            TestButton.IsEnabled = false;
        }
        catch (Exception ex)
        {
            _isRecording = false;
            RecordButton.Content = "Start Recording";
            RecordButton.IsEnabled = true;
            StatusText.Text = $"Stop failed: {ex.Message}";
            StatusText.Foreground = Brushes.Red;
            RecordingFileText.Text = "Recording cancelled due to error.";
            DeviceComboBox.IsEnabled = _audioDevices.Count > 0;
            TestButton.IsEnabled = ResolveSampleAudioPath() != null;
        }
    }

    private void OnRecordingComplete(string filePath)
    {
        Dispatcher.InvokeAsync(() => HandleRecordingCompletionAsync(filePath));
    }

    private async Task HandleRecordingCompletionAsync(string filePath)
    {
        _isRecording = false;
        _currentRecordingPath = filePath;

        RecordButton.Content = "Start Recording";
        DeviceComboBox.IsEnabled = _audioDevices.Count > 0;

        if (!File.Exists(filePath))
        {
            RecordButton.IsEnabled = _audioDevices.Count > 0;
            StatusText.Text = "Recording complete, but the file could not be found.";
            StatusText.Foreground = Brushes.OrangeRed;
            RecordingFileText.Text = "Recording file missing.";
            ResultTextBox.Text = string.Empty;
            TestButton.IsEnabled = ResolveSampleAudioPath() != null;
            return;
        }

        RecordingFileText.Text = $"Saved to: {filePath}";

        await TranscribeAndDisplayAsync(
            filePath,
            deleteAfterProcessing: true,
            contextLabel: "recording",
            initialMessage: $"Processing recording: {filePath}"
        );

        _currentRecordingPath = string.Empty;
    }

    private async Task TranscribeAndDisplayAsync(
        string audioFilePath,
        bool deleteAfterProcessing,
        string contextLabel,
        string initialMessage = null
    )
    {
        _isTranscribing = true;

        RecordButton.IsEnabled = false;
        DeviceComboBox.IsEnabled = false;
        TestButton.IsEnabled = false;

        if (!string.IsNullOrWhiteSpace(initialMessage))
        {
            RecordingFileText.Text = initialMessage;
        }

        StatusText.Text = $"Transcribing {contextLabel}...";
        StatusText.Foreground = Brushes.SteelBlue;
        ResultTextBox.Text = "Processing transcription...";

        try
        {
            var transcription = await _whisperService.TranscribeAsync(
                audioFilePath,
                GetSelectedLanguageCode()
            );

            var trimmedTranscription = transcription?.Trim();

            if (string.IsNullOrWhiteSpace(trimmedTranscription))
            {
                ResultTextBox.Text = "(No transcription returned.)";
                StatusText.Text =
                    $"Transcription completed for {contextLabel}, but no text was produced.";
                StatusText.Foreground = Brushes.OrangeRed;
                ResetTargetWindowContext();
            }
            else
            {
                ResultTextBox.Text = trimmedTranscription;
                await TryInsertTranscriptionAsync(trimmedTranscription).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            ResultTextBox.Text = string.Empty;
            StatusText.Text = $"Transcription error: {ex.Message}";
            StatusText.Foreground = Brushes.Red;
            ResetTargetWindowContext();
        }
        finally
        {
            if (deleteAfterProcessing)
            {
                try
                {
                    if (File.Exists(audioFilePath))
                    {
                        File.Delete(audioFilePath);
                    }

                    RecordingFileText.Text = "Transcription complete. Temporary file removed.";
                }
                catch
                {
                    RecordingFileText.Text = "Transcription complete. Failed to delete temp file.";
                }
            }
            else
            {
                RecordingFileText.Text = $"Sample file: {audioFilePath}";
            }

            _isTranscribing = false;

            if (!_isRecording && _audioDevices.Count > 0)
            {
                RecordButton.IsEnabled = true;
                DeviceComboBox.IsEnabled = true;
            }

            TestButton.IsEnabled = ResolveSampleAudioPath() != null;
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_isListeningForHotkey)
        {
            HandleHotkeyCapture(e);
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnGlobalHotkeyPressed()
    {
        Dispatcher.Invoke(() =>
        {
            if (_isListeningForHotkey || _isRecording)
            {
                return;
            }

            PlaySoundCue(_startSoundPlayer);
            CaptureActiveWindowContext();
            StartRecording();

            if (_isRecording)
            {
                HotkeyStatusText.Text = "Recording while hotkey is held. Release to stop.";
                HotkeyStatusText.Foreground = Brushes.SteelBlue;
            }
            else
            {
                RestoreHotkeyHint();
            }
        });
    }

    private void OnGlobalHotkeyReleased()
    {
        Dispatcher.Invoke(() =>
        {
            if (_isListeningForHotkey)
            {
                return;
            }

            if (_isRecording)
            {
                PlaySoundCue(_stopSoundPlayer);
                StopRecording();
            }

            RestoreHotkeyHint();
        });
    }

    private void BeginHotkeyListening()
    {
        if (_isListeningForHotkey)
        {
            return;
        }

        _isListeningForHotkey = true;
        _hotkeyManager.UnregisterHotkeys();
        SetHotkeyButtonsEnabled(false);

        HotkeyTextBox.Text = "(press keys...)";
        HotkeyStatusText.Text = "Press the desired key combination, or press Esc to cancel.";
        HotkeyStatusText.Foreground = Brushes.SteelBlue;

        HotkeyTextBox.Focus();
    }

    private void HandleHotkeyCapture(KeyEventArgs e)
    {
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _isListeningForHotkey = false;
            UpdateHotkeyDisplay();
            RestoreHotkeyHint("Hotkey change cancelled.");
            SetHotkeyButtonsEnabled(true);
            TryRegisterHotkey(_currentHotkey);
            return;
        }

        if (IsModifierKey(key))
        {
            HotkeyStatusText.Text = "Press a non-modifier key to complete the shortcut.";
            HotkeyStatusText.Foreground = Brushes.DarkOrange;
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var newHotkey = new HotkeySettings { Key = key, Modifiers = modifiers };

        ApplyHotkeySelection(newHotkey);
    }

    private bool TryRegisterHotkey(HotkeySettings settings)
    {
        if (_windowHandle == IntPtr.Zero)
        {
            return true;
        }

        return _hotkeyManager.RegisterHotkey(_windowHandle, settings.Modifiers, settings.Key);
    }

    private void ApplyHotkeySelection(
        HotkeySettings newHotkey,
        bool persist = true,
        bool showSuccessMessage = true
    )
    {
        var previousHotkey = _currentHotkey?.Clone() ?? HotkeySettings.CreateDefault();

        var registrationSucceeded = TryRegisterHotkey(newHotkey);

        if (!registrationSucceeded)
        {
            HotkeyStatusText.Text = "Couldn't register that hotkey. Try another combination.";
            HotkeyStatusText.Foreground = Brushes.Red;

            TryRegisterHotkey(previousHotkey);
            UpdateHotkeyDisplay();
            _isListeningForHotkey = false;
            SetHotkeyButtonsEnabled(true);
            return;
        }

        _currentHotkey = newHotkey.Clone();
        UpdateHotkeyDisplay();
        _isListeningForHotkey = false;

        if (persist)
        {
            _configurationService.Configuration.Hotkey = _currentHotkey.Clone();
            try
            {
                _configurationService.Save();
            }
            catch (Exception ex)
            {
                HotkeyStatusText.Text =
                    $"Hotkey saved for this session, but couldn't be persisted: {ex.Message}";
                HotkeyStatusText.Foreground = Brushes.OrangeRed;
                SetHotkeyButtonsEnabled(true);
                return;
            }
        }

        if (showSuccessMessage)
        {
            HotkeyStatusText.Text =
                $"Global hotkey set to {HotkeySettings.Format(_currentHotkey)}.";
            HotkeyStatusText.Foreground = Brushes.Green;
        }
        else
        {
            RestoreHotkeyHint();
        }

        SetHotkeyButtonsEnabled(true);
    }

    private void UpdateHotkeyDisplay()
    {
        HotkeyTextBox.Text = HotkeySettings.Format(_currentHotkey);
    }

    private void RestoreHotkeyHint(string message = null)
    {
        HotkeyStatusText.Text =
            message ?? $"Press and hold {HotkeySettings.Format(_currentHotkey)} to record.";
        HotkeyStatusText.Foreground = _defaultHotkeyStatusBrush;
    }

    private void RestoreLanguageHint(string message = null, Brush overrideBrush = null)
    {
        if (LanguageStatusText == null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message =
                _currentLanguageOption == null
                || string.IsNullOrWhiteSpace(_currentLanguageOption.Code)
                    ? "Whisper will auto-detect the language."
                    : $"Language set to {_currentLanguageOption.DisplayName}.";
        }

        LanguageStatusText.Text = message;
        LanguageStatusText.Foreground = overrideBrush ?? _defaultLanguageStatusBrush;
    }

    private void CaptureActiveWindowContext()
    {
        var handle = _windowManager.GetForegroundWindowHandle();

        if (_windowManager.IsWindowHandleValid(handle))
        {
            _targetWindowHandle = handle;
            _targetWindowTitle = _windowManager.GetWindowTitle(handle);
            _targetProcessName = _windowManager.GetProcessName(handle);
        }
        else
        {
            ResetTargetWindowContext();
        }
    }

    private void EnsureTargetWindowCaptured()
    {
        if (_windowManager.IsWindowHandleValid(_targetWindowHandle))
        {
            return;
        }

        CaptureActiveWindowContext();
    }

    private void ResetTargetWindowContext()
    {
        _targetWindowHandle = IntPtr.Zero;
        _targetWindowTitle = string.Empty;
        _targetProcessName = string.Empty;
    }

    private async Task TryInsertTranscriptionAsync(string transcription)
    {
        if (string.IsNullOrWhiteSpace(transcription))
        {
            ResetTargetWindowContext();
            return;
        }

        var targetHandle = _targetWindowHandle;
        var targetTitle = _targetWindowTitle;
        var targetProcess = _targetProcessName;

        if (!_windowManager.IsWindowHandleValid(targetHandle))
        {
            targetHandle = _windowManager.GetForegroundWindowHandle();
            targetTitle = _windowManager.GetWindowTitle(targetHandle);
            targetProcess = _windowManager.GetProcessName(targetHandle);
        }

        var destination = !string.IsNullOrWhiteSpace(targetTitle)
            ? targetTitle
            : (!string.IsNullOrWhiteSpace(targetProcess) ? targetProcess : "active window");

        var windowIsValid = _windowManager.IsWindowHandleValid(targetHandle);
        var focusSucceeded = !windowIsValid || _windowManager.TryFocusWindow(targetHandle);

        if (!focusSucceeded)
        {
            StatusText.Text = $"Couldn't focus {destination}. Text not inserted.";
            StatusText.Foreground = Brushes.OrangeRed;
            ResetTargetWindowContext();
            return;
        }

        if (windowIsValid)
        {
            await Task.Delay(180).ConfigureAwait(true);
        }

        StatusText.Text = $"Inserting text into {destination}...";
        StatusText.Foreground = Brushes.SteelBlue;

        try
        {
            var method = _textInsertion.InsertText(transcription);

            if (method == TextInsertion.InsertionMethod.None)
            {
                var reason = string.IsNullOrWhiteSpace(_textInsertion.LastDiagnosticMessage)
                    ? "Clipboard insertion failed."
                    : _textInsertion.LastDiagnosticMessage;
                StatusText.Text = $"No text inserted into {destination}: {reason}";
                StatusText.Foreground = Brushes.OrangeRed;
                return;
            }

            var methodLabel = method switch
            {
                TextInsertion.InsertionMethod.UiAutomation => "(UI Automation)",
                TextInsertion.InsertionMethod.Clipboard => "(Clipboard paste)",
                _ => string.Empty,
            };

            StatusText.Text = string.IsNullOrEmpty(methodLabel)
                ? $"Inserted into {destination}."
                : $"Inserted into {destination} {methodLabel}.";
            StatusText.Foreground = Brushes.Green;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Insertion failed: {ex.Message}";
            StatusText.Foreground = Brushes.OrangeRed;
        }
        finally
        {
            ResetTargetWindowContext();
        }
    }

    private string GetSelectedLanguageCode()
    {
        var code = _currentLanguageOption?.Code;
        return string.IsNullOrWhiteSpace(code) ? string.Empty : code;
    }

    private static SoundPlayer CreateSoundPlayer(string fileName)
    {
        try
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var candidatePaths = new[]
            {
                Path.Combine(baseDirectory, "Assets", "Audio", fileName),
                Path.Combine(baseDirectory, "Resources", fileName),
            };

            var fullPath = candidatePaths.FirstOrDefault(File.Exists);

            if (string.IsNullOrEmpty(fullPath))
            {
                return null;
            }

            var player = new SoundPlayer(fullPath);
            player.LoadAsync();
            return player;
        }
        catch
        {
            return null;
        }
    }

    private static void PlaySoundCue(SoundPlayer player)
    {
        if (player == null)
        {
            return;
        }

        try
        {
            player.Play();
        }
        catch { }
    }

    private void SetHotkeyButtonsEnabled(bool enabled)
    {
        EditHotkeyButton.IsEnabled = enabled;
        ResetHotkeyButton.IsEnabled = enabled;
    }

    private static bool IsModifierKey(Key key)
    {
        return key
            is Key.LeftCtrl
                or Key.RightCtrl
                or Key.LeftAlt
                or Key.RightAlt
                or Key.LeftShift
                or Key.RightShift
                or Key.LWin
                or Key.RWin;
    }

    private string ResolveSampleAudioPath()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[] { "test.mp3", "test.wav", "jfk.mp3", "jfk.wav" };

        foreach (var fileName in candidates)
        {
            var candidatePath = Path.Combine(baseDirectory, "TestAudio", fileName);
            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        return null;
    }

    private void CleanupResources()
    {
        if (_cleanupInvoked)
        {
            return;
        }

        _cleanupInvoked = true;
        ResetTargetWindowContext();

        try
        {
            if (_isRecording)
            {
                _audioCapture.StopCapture();
            }
        }
        catch
        {
            // ignored
        }

        _audioCapture.RecordingComplete -= OnRecordingComplete;
        _audioCapture.Dispose();
        DisposeAudioDevices();

        _isTranscribing = false;
        TestButton.IsEnabled = ResolveSampleAudioPath() != null;

        if (_windowSource != null)
        {
            _windowSource.RemoveHook(_hotkeyManager.HotkeyHook);
            _windowSource = null;
        }

        _hotkeyManager.HotkeyPressed -= OnGlobalHotkeyPressed;
        _hotkeyManager.HotkeyReleased -= OnGlobalHotkeyReleased;
        _hotkeyManager.Dispose();
    }

    private void DisposeAudioDevices()
    {
        foreach (var device in _audioDevices)
        {
            try
            {
                device.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        _audioDevices.Clear();
    }

    private sealed class LanguageOption
    {
        public LanguageOption(string displayName, string code)
        {
            DisplayName = displayName;
            Code = code ?? string.Empty;
        }

        public string DisplayName { get; }
        public string Code { get; }

        public override string ToString()
        {
            return DisplayName;
        }
    }
}
