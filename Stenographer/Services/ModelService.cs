using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Stenographer.Models;

namespace Stenographer.Services;

public class ModelService : IDisposable
{
    private static readonly Uri ModelListEndpoint = new(
        "https://huggingface.co/api/models/ggerganov/whisper.cpp?expand=siblings"
    );
    private readonly HttpClient _httpClient;
    private readonly string _modelDirectory;
    private bool _disposed;

    public ModelService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(100) }) { }

    internal ModelService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        _modelDirectory = Path.Combine(baseDirectory, "Models", "whisper.cpp");
    }

    public string ModelDirectory => _modelDirectory;

    public IReadOnlyList<WhisperLocalModel> GetInstalledModels()
    {
        Directory.CreateDirectory(_modelDirectory);
        var files = Directory.EnumerateFiles(
            _modelDirectory,
            "*.bin",
            SearchOption.TopDirectoryOnly
        );
        var models = new List<WhisperLocalModel>();

        foreach (var file in files)
        {
            try
            {
                models.Add(new WhisperLocalModel(file));
            }
            catch
            {
                // Skip files we cannot read
            }
        }

        models.Sort(
            (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase)
        );
        return models;
    }

    public async Task<IReadOnlyList<WhisperRemoteModel>> FetchRemoteModelsAsync(
        CancellationToken cancellationToken = default
    )
    {
        using var response = await _httpClient
            .GetAsync(ModelListEndpoint, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument
            .ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (
            !document.RootElement.TryGetProperty("siblings", out var siblingsElement)
            || siblingsElement.ValueKind != JsonValueKind.Array
        )
        {
            return Array.Empty<WhisperRemoteModel>();
        }

        var results = new List<WhisperRemoteModel>();

        foreach (var element in siblingsElement.EnumerateArray())
        {
            if (!element.TryGetProperty("rfilename", out var fileNameElement))
            {
                continue;
            }

            var fileName = fileNameElement.GetString();

            if (
                string.IsNullOrWhiteSpace(fileName)
                || !fileName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
            )
            {
                continue;
            }

            long? sizeBytes = null;
            if (
                element.TryGetProperty("size", out var sizeElement)
                && sizeElement.TryGetInt64(out var sizeValue)
            )
            {
                sizeBytes = sizeValue;
            }

            var downloadUri = new Uri(
                $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{Uri.EscapeDataString(fileName)}?download=1"
            );
            results.Add(new WhisperRemoteModel(fileName, sizeBytes, downloadUri));
        }

        results.Sort(
            (a, b) => string.Compare(a.FileName, b.FileName, StringComparison.OrdinalIgnoreCase)
        );
        return results;
    }

    public async Task DownloadModelAsync(
        WhisperRemoteModel model,
        IProgress<ModelDownloadProgress> progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        Directory.CreateDirectory(_modelDirectory);
        var destinationPath = Path.Combine(_modelDirectory, model.FileName);

        using var response = await _httpClient
            .GetAsync(
                model.DownloadUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            )
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;

        await using var remoteStream = await response
            .Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None
        );

        var buffer = new byte[81920];
        long totalBytesRead = 0;
        int bytesRead;

        while (
            (
                bytesRead = await remoteStream
                    .ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false)
            ) > 0
        )
        {
            await fileStream
                .WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);
            totalBytesRead += bytesRead;
            progress?.Report(new ModelDownloadProgress(totalBytesRead, contentLength));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }
}
