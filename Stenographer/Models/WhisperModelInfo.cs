using System;
using System.Globalization;
using System.IO;

namespace Stenographer.Models;

public sealed class WhisperLocalModel
{
    public WhisperLocalModel(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "Model file path cannot be null or empty.",
                nameof(filePath)
            );
        }

        FilePath = Path.GetFullPath(filePath);
        FileName = Path.GetFileName(FilePath);

        try
        {
            var info = new FileInfo(FilePath);
            FileSize = info.Exists ? info.Length : 0;
        }
        catch
        {
            FileSize = 0;
        }
    }

    public string FileName { get; }

    public string FilePath { get; }

    public long FileSize { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(FileName) ? "Unknown model" : FileName;

    public string GetFormattedSize() => FileSize <= 0 ? "(size unknown)" : FormatBytes(FileSize);

    public string FormattedSize => GetFormattedSize();

    public override string ToString() => DisplayName;

    internal static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{size:0.##} {units[unitIndex]}");
    }
}

public sealed class WhisperRemoteModel
{
    public WhisperRemoteModel(string fileName, long? sizeBytes, Uri downloadUri)
    {
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        SizeBytes = sizeBytes;
        DownloadUri = downloadUri ?? throw new ArgumentNullException(nameof(downloadUri));
    }

    public string FileName { get; }

    public long? SizeBytes { get; }

    public Uri DownloadUri { get; }

    public string DisplayName => string.IsNullOrWhiteSpace(FileName) ? "Unknown model" : FileName;

    public string GetFormattedSize() =>
        SizeBytes is > 0 ? WhisperLocalModel.FormatBytes(SizeBytes.Value) : "(size unknown)";

    public string FormattedSize => GetFormattedSize();

    public override string ToString() => DisplayName;
}

public readonly struct ModelDownloadProgress
{
    public ModelDownloadProgress(long bytesReceived, long? totalBytes)
    {
        BytesReceived = bytesReceived;
        TotalBytes = totalBytes;
    }

    public long BytesReceived { get; }

    public long? TotalBytes { get; }

    public double? Percentage =>
        TotalBytes is > 0 ? (double)BytesReceived / TotalBytes.Value * 100 : null;
}
