using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Stenographer.Core;

public class AudioCapture : IDisposable
{
    private readonly object _syncRoot = new();
    private readonly MMDeviceEnumerator _deviceEnumerator = new();

    private WasapiCapture _audioCapture;
    private WaveFileWriter _waveWriter;
    private MMDevice _activeDevice;
    private string _tempAudioFile;
    private bool _isRecording;
    private bool _disposed;

    public event Action<string> RecordingComplete;

    public bool IsRecording => _isRecording;

    public void StartCapture(int deviceIndex = -1)
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (_isRecording)
            {
                throw new InvalidOperationException("Audio capture is already in progress.");
            }

            _activeDevice?.Dispose();
            _activeDevice = ResolveDevice(deviceIndex);

            _audioCapture = new WasapiCapture(_activeDevice)
            {
                ShareMode = AudioClientShareMode.Shared,
            };

            _tempAudioFile = Path.Combine(
                Path.GetTempPath(),
                $"Stenographer_{DateTime.UtcNow:yyyyMMddHHmmssfff}.wav"
            );

            _waveWriter = new WaveFileWriter(_tempAudioFile, _audioCapture.WaveFormat);
            _audioCapture.DataAvailable += OnDataAvailable;
            _audioCapture.RecordingStopped += OnRecordingStopped;

            try
            {
                _audioCapture.StartRecording();
                _isRecording = true;
            }
            catch
            {
                CleanupCapture();
                throw;
            }
        }
    }

    public void StopCapture()
    {
        ThrowIfDisposed();

        lock (_syncRoot)
        {
            if (!_isRecording || _audioCapture == null)
            {
                return;
            }

            try
            {
                _isRecording = false;
                _audioCapture.StopRecording();
            }
            catch
            {
                CleanupCapture();
                throw;
            }
        }
    }

    public List<MMDevice> GetAvailableDevices()
    {
        ThrowIfDisposed();

        return _deviceEnumerator
            .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .ToList();
    }

    private MMDevice ResolveDevice(int deviceIndex)
    {
        var devices = _deviceEnumerator.EnumerateAudioEndPoints(
            DataFlow.Capture,
            DeviceState.Active
        );

        if (devices.Count == 0)
        {
            throw new InvalidOperationException("No active audio capture devices were found.");
        }

        if (deviceIndex >= 0)
        {
            if (deviceIndex >= devices.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deviceIndex),
                    "Selected audio device index is out of range."
                );
            }

            return devices[deviceIndex];
        }

        try
        {
            return _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return devices[0];
        }
    }

    private void OnDataAvailable(object sender, WaveInEventArgs e)
    {
        lock (_syncRoot)
        {
            if (_waveWriter != null)
            {
                _waveWriter.Write(e.Buffer, 0, e.BytesRecorded);
                _waveWriter.Flush();
            }
        }
    }

    private void OnRecordingStopped(object sender, StoppedEventArgs e)
    {
        string completedFile;

        lock (_syncRoot)
        {
            completedFile = _tempAudioFile;
            _tempAudioFile = null;

            CleanupCapture();
        }

        if (e.Exception != null)
        {
            if (!string.IsNullOrWhiteSpace(completedFile) && File.Exists(completedFile))
            {
                try
                {
                    File.Delete(completedFile);
                }
                catch
                {
                    // ignored
                }
            }

            return;
        }

        var handler = RecordingComplete;
        if (!string.IsNullOrWhiteSpace(completedFile) && handler != null)
        {
            handler(completedFile);
        }
    }

    private void CleanupCapture()
    {
        if (_audioCapture != null)
        {
            _audioCapture.DataAvailable -= OnDataAvailable;
            _audioCapture.RecordingStopped -= OnRecordingStopped;
            _audioCapture.Dispose();
            _audioCapture = null;
        }

        if (_waveWriter != null)
        {
            _waveWriter.Dispose();
            _waveWriter = null;
        }

        if (_activeDevice != null)
        {
            _activeDevice.Dispose();
            _activeDevice = null;
        }

        _isRecording = false;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            lock (_syncRoot)
            {
                CleanupCapture();
                _deviceEnumerator.Dispose();
            }
        }

        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioCapture));
        }
    }
}
