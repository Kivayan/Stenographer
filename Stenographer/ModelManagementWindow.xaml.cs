using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Stenographer.Core;
using Stenographer.Models;
using Stenographer.Services;

namespace Stenographer;

public partial class ModelManagementWindow : Window
{
    private readonly ModelService _modelService;
    private readonly ConfigurationService _configurationService;
    private readonly WhisperService _whisperService;
    private CancellationTokenSource _downloadCancellationSource;
    private List<WhisperLocalModel> _installedModels = new();
    private List<WhisperRemoteModel> _remoteModels = new();
    private bool _isLoadingRemote;
    private bool _isDownloading;
    private Button _setActiveModelButton;
    private Button _openFolderButton;
    private Button _downloadModelButton;
    private Button _refreshRemoteButton;

    public ModelManagementWindow(
        ModelService modelService,
        ConfigurationService configurationService,
        WhisperService whisperService
    )
    {
        _modelService = modelService ?? throw new ArgumentNullException(nameof(modelService));
        _configurationService =
            configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _whisperService = whisperService ?? throw new ArgumentNullException(nameof(whisperService));

        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    public event EventHandler<string> ActiveModelChanged;
    public event EventHandler ModelsChanged;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _setActiveModelButton = FindName("SetActiveModelButton") as Button;
        _openFolderButton = FindName("OpenFolderButton") as Button;
        _downloadModelButton = FindName("DownloadModelButton") as Button;
        _refreshRemoteButton = FindName("RefreshRemoteButton") as Button;

        LoadInstalledModels();
        _ = LoadRemoteModelsAsync();
        UpdateButtons();
    }

    private void OnClosed(object sender, EventArgs e)
    {
        CancelOngoingDownload();
    }

    private void LoadInstalledModels()
    {
        _installedModels = new List<WhisperLocalModel>(_modelService.GetInstalledModels());
        InstalledModelsListView.ItemsSource = _installedModels;

        var activeModel =
            _configurationService.Configuration?.SelectedModelFileName ?? string.Empty;
        var selection = _installedModels.FirstOrDefault(model =>
            string.Equals(model.FileName, activeModel, StringComparison.OrdinalIgnoreCase)
        );

        InstalledModelsListView.SelectedItem = selection;
        UpdateButtons();
    }

    private async Task LoadRemoteModelsAsync()
    {
        if (_isLoadingRemote)
        {
            return;
        }

        _isLoadingRemote = true;
        StatusTextBlock.Text = "Loading remote model catalog...";

        try
        {
            var remote = await _modelService.FetchRemoteModelsAsync();
            _remoteModels = new List<WhisperRemoteModel>(remote);
            RemoteModelsListView.ItemsSource = _remoteModels;
            StatusTextBlock.Text =
                _remoteModels.Count == 0
                    ? "No remote models found."
                    : $"Loaded {_remoteModels.Count} remote models.";
        }
        catch (Exception ex)
        {
            _remoteModels = new List<WhisperRemoteModel>();
            RemoteModelsListView.ItemsSource = _remoteModels;
            StatusTextBlock.Text = $"Failed to load remote models: {ex.Message}";
        }
        finally
        {
            _isLoadingRemote = false;
            UpdateButtons();
        }
    }

    private void SetActiveModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (InstalledModelsListView.SelectedItem is not WhisperLocalModel model)
        {
            StatusTextBlock.Text = "Select an installed model to activate.";
            return;
        }

        ApplyActiveModel(model);
    }

    private void ApplyActiveModel(WhisperLocalModel model)
    {
        if (model == null)
        {
            return;
        }

        _whisperService.SetModelFile(model.FileName);
        _configurationService.Configuration.SelectedModelFileName = model.FileName;

        try
        {
            _configurationService.Save();
            StatusTextBlock.Text = $"Active model set to {model.FileName}.";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text =
                $"Model set for this session, but couldn't save configuration: {ex.Message}";
        }

        ActiveModelChanged?.Invoke(this, model.FileName);
    }

    private async void DownloadModelButton_Click(object sender, RoutedEventArgs e)
    {
        if (RemoteModelsListView.SelectedItem is not WhisperRemoteModel remoteModel)
        {
            StatusTextBlock.Text = "Select a remote model to download.";
            return;
        }

        var destinationPath = Path.Combine(_modelService.ModelDirectory, remoteModel.FileName);

        if (File.Exists(destinationPath))
        {
            var overwrite = MessageBox.Show(
                this,
                $"{remoteModel.FileName} already exists. Overwrite?",
                "Overwrite model",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (overwrite != MessageBoxResult.Yes)
            {
                StatusTextBlock.Text = "Download cancelled.";
                return;
            }
        }

        await DownloadModelAsync(remoteModel);
    }

    private async Task DownloadModelAsync(WhisperRemoteModel remoteModel)
    {
        CancelOngoingDownload();
        _downloadCancellationSource = new CancellationTokenSource();

        try
        {
            _isDownloading = true;
            UpdateButtons();

            DownloadProgressBar.Visibility = Visibility.Visible;
            DownloadProgressBar.IsIndeterminate = remoteModel.SizeBytes is null;
            DownloadProgressBar.Value = 0;
            StatusTextBlock.Text = $"Downloading {remoteModel.FileName}...";

            var progress = new Progress<ModelDownloadProgress>(progressValue =>
            {
                if (progressValue.Percentage is double percent)
                {
                    DownloadProgressBar.IsIndeterminate = false;
                    DownloadProgressBar.Maximum = 100;
                    DownloadProgressBar.Value = Math.Max(0, Math.Min(100, percent));
                    StatusTextBlock.Text = $"Downloading {remoteModel.FileName}: {percent:0.0}%";
                }
                else
                {
                    DownloadProgressBar.IsIndeterminate = true;
                    StatusTextBlock.Text = $"Downloading {remoteModel.FileName}...";
                }
            });

            await _modelService.DownloadModelAsync(
                remoteModel,
                progress,
                _downloadCancellationSource.Token
            );

            StatusTextBlock.Text = $"Downloaded {remoteModel.FileName}.";
            DownloadProgressBar.Visibility = Visibility.Collapsed;

            LoadInstalledModels();
            ModelsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "Download cancelled.";
            DownloadProgressBar.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Download failed: {ex.Message}";
            DownloadProgressBar.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _isDownloading = false;
            UpdateButtons();
            CancelOngoingDownload();
        }
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        var path = _modelService.ModelDirectory;
        if (!Directory.Exists(path))
        {
            StatusTextBlock.Text = "Model directory not found.";
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true }
            );
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"Unable to open folder: {ex.Message}";
        }
    }

    private void RefreshRemoteButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadRemoteModelsAsync();
    }

    private void CancelOngoingDownload()
    {
        if (_downloadCancellationSource == null)
        {
            return;
        }

        if (!_downloadCancellationSource.IsCancellationRequested)
        {
            _downloadCancellationSource.Cancel();
        }

        _downloadCancellationSource.Dispose();
        _downloadCancellationSource = null;
    }

    private void UpdateButtons()
    {
        if (!IsLoaded)
        {
            return;
        }

        var hasInstalledSelection = InstalledModelsListView.SelectedItem is WhisperLocalModel;
        var hasRemoteSelection = RemoteModelsListView.SelectedItem is WhisperRemoteModel;

        if (_setActiveModelButton != null)
        {
            _setActiveModelButton.IsEnabled = hasInstalledSelection && !_isDownloading;
        }

        if (_openFolderButton != null)
        {
            _openFolderButton.IsEnabled = !_isDownloading;
        }

        if (_downloadModelButton != null)
        {
            _downloadModelButton.IsEnabled =
                hasRemoteSelection && !_isDownloading && !_isLoadingRemote;
        }

        if (_refreshRemoteButton != null)
        {
            _refreshRemoteButton.IsEnabled = !_isDownloading && !_isLoadingRemote;
        }
    }

    private void InstalledModelsListView_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e
    )
    {
        UpdateButtons();
    }

    private void RemoteModelsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
    }
}
