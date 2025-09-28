using System;

namespace Stenographer.Models;

public class TranscriptionResult
{
    public string Text { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string SourceFilePath { get; set; } = string.Empty;
}
