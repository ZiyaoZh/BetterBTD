using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using BetterBTD.Models.Tools;
using BetterBTD.Services.Tools;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BetterBTD.ViewModels;

public sealed partial class Btd6SaveViewerWindowViewModel : ObservableObject
{
    private const int SearchResultLimit = 100;
    private readonly LocalizationService _localizationService;
    private readonly Btd6SaveViewerService _saveViewerService;
    private readonly List<Btd6SaveJsonNodeViewModel> _flatNodes = [];

    [ObservableProperty]
    private string _windowTitle = string.Empty;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _errorText = string.Empty;

    [ObservableProperty]
    private string _rawJsonText = string.Empty;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _hasDocument;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private Btd6SaveJsonNodeViewModel? _selectedNode;

    public Btd6SaveViewerWindowViewModel()
        : this(LocalizationService.Instance, Btd6SaveViewerService.Instance)
    {
    }

    internal Btd6SaveViewerWindowViewModel(
        LocalizationService localizationService,
        Btd6SaveViewerService saveViewerService)
    {
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
        _saveViewerService = saveViewerService ?? throw new ArgumentNullException(nameof(saveViewerService));

        SummaryItems = [];
        JsonNodes = [];
        SearchResults = [];

        OpenFileCommand = new RelayCommand(OpenFile, CanRunFileAction);
        ReloadCommand = new RelayCommand(Reload, CanReload);
        ExportJsonCommand = new RelayCommand(ExportJson, CanExportJson);
        CopySelectedNodeCommand = new RelayCommand(CopySelectedNode, CanCopySelectedNode);

        RefreshLocalizedContent();
    }

    public ObservableCollection<Btd6SaveSummaryItem> SummaryItems { get; }

    public ObservableCollection<Btd6SaveJsonNodeViewModel> JsonNodes { get; }

    public ObservableCollection<Btd6SaveSearchResultViewModel> SearchResults { get; }

    public IRelayCommand OpenFileCommand { get; }

    public IRelayCommand ReloadCommand { get; }

    public IRelayCommand ExportJsonCommand { get; }

    public IRelayCommand CopySelectedNodeCommand { get; }

    public string OpenFileText => _localizationService.T("Tools.SaveViewer.OpenFile");

    public string ReloadText => _localizationService.T("Tools.SaveViewer.Reload");

    public string ExportJsonText => _localizationService.T("Tools.SaveViewer.ExportJson");

    public string CopySelectedText => _localizationService.T("Tools.SaveViewer.CopySelected");

    public string LoadedFileLabel => _localizationService.T("Tools.SaveViewer.LoadedFile");

    public string NoFileText => _localizationService.T("Tools.SaveViewer.NoFile");

    public string DisplayFilePath => string.IsNullOrWhiteSpace(FilePath) ? NoFileText : FilePath;

    public string SummaryTabText => _localizationService.T("Tools.SaveViewer.Tab.Summary");

    public string TreeTabText => _localizationService.T("Tools.SaveViewer.Tab.Tree");

    public string RawJsonTabText => _localizationService.T("Tools.SaveViewer.Tab.RawJson");

    public string SearchPlaceholderText => _localizationService.T("Tools.SaveViewer.SearchPlaceholder");

    public string SearchResultsText => string.Format(
        _localizationService.T("Tools.SaveViewer.SearchResults"),
        SearchResults.Count);

    public string SelectedNodeTitle => _localizationService.T("Tools.SaveViewer.SelectedNode");

    public string SelectedNodePathLabel => _localizationService.T("Tools.SaveViewer.Node.Path");

    public string SelectedNodeTypeLabel => _localizationService.T("Tools.SaveViewer.Node.Type");

    public string SelectedNodeValueLabel => _localizationService.T("Tools.SaveViewer.Node.Value");

    public string EmptyStateTitle => _localizationService.T("Tools.SaveViewer.Empty.Title");

    public string EmptyStateDescription => _localizationService.T("Tools.SaveViewer.Empty.Description");

    public string StatusReadyText => _localizationService.T("Tools.SaveViewer.Status.Ready");

    partial void OnSearchTextChanged(string value)
    {
        RefreshSearchResults();
    }

    partial void OnFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(DisplayFilePath));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OpenFileCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        ExportJsonCommand.NotifyCanExecuteChanged();
        CopySelectedNodeCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasDocumentChanged(bool value)
    {
        ReloadCommand.NotifyCanExecuteChanged();
        ExportJsonCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedNodeChanged(Btd6SaveJsonNodeViewModel? value)
    {
        CopySelectedNodeCommand.NotifyCanExecuteChanged();
    }

    public void SetSelectedNode(Btd6SaveJsonNodeViewModel? node)
    {
        SelectedNode = node;
    }

    private void RefreshLocalizedContent()
    {
        WindowTitle = _localizationService.T("Tools.SaveViewer.WindowTitle");
        StatusText = _localizationService.T("Tools.SaveViewer.Status.Empty");

        OnPropertyChanged(nameof(OpenFileText));
        OnPropertyChanged(nameof(ReloadText));
        OnPropertyChanged(nameof(ExportJsonText));
        OnPropertyChanged(nameof(CopySelectedText));
        OnPropertyChanged(nameof(LoadedFileLabel));
        OnPropertyChanged(nameof(NoFileText));
        OnPropertyChanged(nameof(DisplayFilePath));
        OnPropertyChanged(nameof(SummaryTabText));
        OnPropertyChanged(nameof(TreeTabText));
        OnPropertyChanged(nameof(RawJsonTabText));
        OnPropertyChanged(nameof(SearchPlaceholderText));
        OnPropertyChanged(nameof(SearchResultsText));
        OnPropertyChanged(nameof(SelectedNodeTitle));
        OnPropertyChanged(nameof(SelectedNodePathLabel));
        OnPropertyChanged(nameof(SelectedNodeTypeLabel));
        OnPropertyChanged(nameof(SelectedNodeValueLabel));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
        OnPropertyChanged(nameof(StatusReadyText));
    }

    private bool CanRunFileAction()
    {
        return !IsBusy;
    }

    private bool CanReload()
    {
        return !IsBusy && HasDocument && !string.IsNullOrWhiteSpace(FilePath);
    }

    private bool CanExportJson()
    {
        return !IsBusy && HasDocument && !string.IsNullOrWhiteSpace(RawJsonText);
    }

    private bool CanCopySelectedNode()
    {
        return !IsBusy && SelectedNode is not null;
    }

    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = _localizationService.T("Tools.SaveViewer.OpenFileFilter"),
            Title = _localizationService.T("Tools.SaveViewer.OpenFileTitle")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        LoadFile(dialog.FileName);
    }

    private void Reload()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            return;
        }

        LoadFile(FilePath);
    }

    private void ExportJson()
    {
        if (!HasDocument || string.IsNullOrWhiteSpace(RawJsonText))
        {
            return;
        }

        var defaultName = string.IsNullOrWhiteSpace(FileName)
            ? "Profile.json"
            : $"{Path.GetFileNameWithoutExtension(FileName)}.json";

        var dialog = new SaveFileDialog
        {
            Filter = _localizationService.T("Tools.SaveViewer.ExportJsonFilter"),
            FileName = defaultName,
            Title = _localizationService.T("Tools.SaveViewer.ExportJsonTitle")
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, RawJsonText);
            StatusText = string.Format(_localizationService.T("Tools.SaveViewer.Status.Exported"), dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(_localizationService.T("Tools.SaveViewer.Error.ExportFailed"), ex.Message));
        }
    }

    private void CopySelectedNode()
    {
        if (SelectedNode is null)
        {
            return;
        }

        Clipboard.SetText($"{SelectedNode.Path}{Environment.NewLine}{SelectedNode.ValuePreview}");
        StatusText = _localizationService.T("Tools.SaveViewer.Status.Copied");
    }

    private void LoadFile(string path)
    {
        IsBusy = true;
        ClearError();
        StatusText = _localizationService.T("Tools.SaveViewer.Status.Loading");

        try
        {
            var document = _saveViewerService.Load(path);
            ApplyDocument(document);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or Newtonsoft.Json.JsonException)
        {
            SetError(string.Format(_localizationService.T("Tools.SaveViewer.Error.LoadFailed"), ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyDocument(Btd6SaveDocument document)
    {
        FilePath = document.FilePath;
        FileName = document.FileName;
        RawJsonText = document.FormattedJson;
        HasDocument = true;
        SelectedNode = null;

        SummaryItems.Clear();
        foreach (var item in BuildSummaryItems(document))
        {
            SummaryItems.Add(item);
        }

        JsonNodes.Clear();
        _flatNodes.Clear();

        var root = new Btd6SaveJsonNodeViewModel("$", "$", document.Root, 0);
        JsonNodes.Add(root);
        Flatten(root, _flatNodes);

        RefreshSearchResults();
        StatusText = string.Format(
            _localizationService.T("Tools.SaveViewer.Status.Loaded"),
            FormatBytes(document.JsonSizeBytes),
            _flatNodes.Count.ToString("N0", CultureInfo.CurrentCulture));
    }

    private IEnumerable<Btd6SaveSummaryItem> BuildSummaryItems(Btd6SaveDocument document)
    {
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.Platform"),
            Value = $"{document.PlatformId} ({document.PlatformName})"
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.Sku"),
            Value = document.SavedBySkuId is null
                ? document.SavedBySkuName
                : $"{document.SavedBySkuId} ({document.SavedBySkuName})"
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.GameVersion"),
            Value = document.SavedByGameVersion
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.Rank"),
            Value = document.Rank
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.Xp"),
            Value = document.Xp
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.MonkeyMoney"),
            Value = document.MonkeyMoney
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.Trophies"),
            Value = document.Trophies
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.OwnerId"),
            Value = document.OwnerId
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.TimeStamp"),
            Value = document.TimeStamp
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.JsonSize"),
            Value = FormatBytes(document.JsonSizeBytes)
        };
        yield return new Btd6SaveSummaryItem
        {
            Label = _localizationService.T("Tools.SaveViewer.Summary.FileSize"),
            Value = FormatBytes(document.FileSizeBytes)
        };
    }

    private void RefreshSearchResults()
    {
        SearchResults.Clear();

        var query = SearchText.Trim();
        if (query.Length < 2 || _flatNodes.Count == 0)
        {
            OnPropertyChanged(nameof(SearchResultsText));
            return;
        }

        foreach (var node in _flatNodes.Where(node => Matches(node, query)).Take(SearchResultLimit))
        {
            SearchResults.Add(new Btd6SaveSearchResultViewModel
            {
                Path = node.Path,
                Type = node.Type,
                ValuePreview = node.ValuePreview
            });
        }

        OnPropertyChanged(nameof(SearchResultsText));
    }

    private void ClearError()
    {
        HasError = false;
        ErrorText = string.Empty;
    }

    private void SetError(string message)
    {
        HasError = true;
        ErrorText = message;
        StatusText = message;
    }

    private static bool Matches(Btd6SaveJsonNodeViewModel node, string query)
    {
        return node.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               node.ValuePreview.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static void Flatten(Btd6SaveJsonNodeViewModel node, ICollection<Btd6SaveJsonNodeViewModel> target)
    {
        target.Add(node);
        foreach (var child in node.Children)
        {
            Flatten(child, target);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes:N0} B";
        }

        var kib = bytes / 1024d;
        if (kib < 1024)
        {
            return $"{kib:N1} KiB";
        }

        return $"{kib / 1024d:N1} MiB";
    }
}
