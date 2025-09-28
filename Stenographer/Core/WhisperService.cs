using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Stenographer.Core;

public class WhisperService
{
    private readonly string _whisperExecutablePath;
    private readonly string _modelDirectory;
    private string _modelPath;
    private string _modelFileName;

    public WhisperService(string modelFileName = null)
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _whisperExecutablePath = Path.Combine(baseDirectory, "Models", "whisper.cpp", "main.exe");
        _modelDirectory = Path.Combine(baseDirectory, "Models", "whisper.cpp");
        Directory.CreateDirectory(_modelDirectory);
        UpdateModelPath(modelFileName);
    }

    public string CurrentModelFileName => _modelFileName;

    public string CurrentModelPath => _modelPath;

    public void SetModelFile(string modelFileName)
    {
        UpdateModelPath(modelFileName);
    }

    private void UpdateModelPath(string modelFileName)
    {
        var resolvedFileName = string.IsNullOrWhiteSpace(modelFileName)
            ? "ggml-base.bin"
            : modelFileName.Trim();

        if (Path.IsPathRooted(resolvedFileName))
        {
            _modelFileName = Path.GetFileName(resolvedFileName);
            _modelPath = resolvedFileName;
        }
        else
        {
            _modelFileName = resolvedFileName;
            _modelPath = Path.Combine(_modelDirectory, resolvedFileName);
        }
    }

    public Task<string> TranscribeAsync(string audioFilePath, string languageCode = "")
    {
        if (string.IsNullOrWhiteSpace(audioFilePath))
        {
            throw new ArgumentException("Audio file path cannot be empty.", nameof(audioFilePath));
        }

        if (!File.Exists(_whisperExecutablePath))
        {
            throw new FileNotFoundException(
                $"Whisper executable not found at '{_whisperExecutablePath}'.",
                _whisperExecutablePath
            );
        }

        if (!File.Exists(_modelPath))
        {
            throw new FileNotFoundException(
                $"Whisper model not found at '{_modelPath}'.",
                _modelPath
            );
        }

        if (!File.Exists(audioFilePath))
        {
            throw new FileNotFoundException(
                "Audio file not found for transcription.",
                audioFilePath
            );
        }

        return Task.Run(async () =>
        {
            var preparedFilePath = PrepareAudioForWhisper(audioFilePath);
            var shouldDeletePreparedFile = !Path.GetFullPath(preparedFilePath)
                .Equals(Path.GetFullPath(audioFilePath), StringComparison.OrdinalIgnoreCase);

            var arguments = new List<string>
            {
                $"-m \"{_modelPath}\"",
                $"-f \"{preparedFilePath}\"",
                "--output-txt",
                "--no_timestamps",
            };

            if (!string.IsNullOrWhiteSpace(languageCode))
            {
                arguments.Add($"-l {languageCode.Trim()}");
            }

            var processInfo = new ProcessStartInfo
            {
                FileName = _whisperExecutablePath,
                Arguments = string.Join(" ", arguments),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory =
                    Path.GetDirectoryName(_whisperExecutablePath)
                    ?? AppDomain.CurrentDomain.BaseDirectory,
            };

            try
            {
                using var process = new Process
                {
                    StartInfo = processInfo,
                    EnableRaisingEvents = true,
                };
                process.Start();

                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync())
                    .ConfigureAwait(false);

                if (process.ExitCode != 0)
                {
                    var error = await errorTask.ConfigureAwait(false);
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(error)
                            ? "Whisper transcription process failed without error output."
                            : error.Trim()
                    );
                }

                var transcriptionText = await TryReadTranscriptionFileAsync(
                        preparedFilePath,
                        audioFilePath
                    )
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(transcriptionText))
                {
                    return transcriptionText.Trim();
                }

                var output = await outputTask.ConfigureAwait(false);
                return output.Trim();
            }
            finally
            {
                if (shouldDeletePreparedFile && File.Exists(preparedFilePath))
                {
                    try
                    {
                        File.Delete(preparedFilePath);
                    }
                    catch
                    {
                        // ignore cleanup failures
                    }
                }
            }
        });
    }

    private static async Task<string> TryReadTranscriptionFileAsync(params string[] audioFilePaths)
    {
        var candidateSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in audioFilePaths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            candidateSet.Add(Path.ChangeExtension(path, ".txt"));
            candidateSet.Add(path + ".txt");
        }

        var candidates = new List<string>(candidateSet);

        const int maxAttempts = 10;
        const int delayMilliseconds = 100;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            foreach (var candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(candidate))
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(candidate).ConfigureAwait(false);
                        return content ?? string.Empty;
                    }
                    finally
                    {
                        try
                        {
                            File.Delete(candidate);
                        }
                        catch
                        {
                            // best-effort cleanup
                        }
                    }
                }
            }

            if (attempt < maxAttempts - 1)
            {
                await Task.Delay(delayMilliseconds).ConfigureAwait(false);
            }
        }

        return string.Empty;
    }

    private static string PrepareAudioForWhisper(string audioFilePath)
    {
        try
        {
            if (IsWhisperCompatibleWave(audioFilePath))
            {
                return audioFilePath;
            }

            var targetPath = Path.Combine(
                Path.GetTempPath(),
                $"{Path.GetFileNameWithoutExtension(audioFilePath)}_{Guid.NewGuid():N}_16k.wav"
            );

            using var reader = new AudioFileReader(audioFilePath);
            var targetFormat = new WaveFormat(16000, 16, 1);
            using var resampler = new MediaFoundationResampler(reader, targetFormat)
            {
                ResamplerQuality = 60,
            };

            WaveFileWriter.CreateWaveFile(targetPath, resampler);

            return targetPath;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Failed to prepare audio file for whisper transcription.",
                ex
            );
        }
    }

    private static bool IsWhisperCompatibleWave(string audioFilePath)
    {
        if (
            !string.Equals(
                Path.GetExtension(audioFilePath),
                ".wav",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return false;
        }

        try
        {
            using var reader = new WaveFileReader(audioFilePath);
            var format = reader.WaveFormat;
            return format.SampleRate == 16000
                && format.Channels == 1
                && format.Encoding == WaveFormatEncoding.Pcm
                && format.BitsPerSample == 16;
        }
        catch
        {
            return false;
        }
    }
}
