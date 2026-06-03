using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BetterBTD.Services;
using BetterBTD.ViewModels;

namespace BetterBTD.Views.Pages;

public partial class MyScriptsPageView : Page
{
    private const double PageMaxHeightOffset = 56d;
    private readonly AppDialogService _appDialogService = AppDialogService.Instance;
    private readonly ManagedScriptLibraryService _managedScriptLibraryService = ManagedScriptLibraryService.Instance;
    private Window? _hostWindow;

    public MyScriptsPageView()
    {
        InitializeComponent();
        DataContext = new MyScriptsPageViewModel(LocalizationService.Instance);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void EditSelectedScriptButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedScriptInEditor();
    }

    private void RunSelectedScriptButton_OnClick(object sender, RoutedEventArgs e)
    {
        RunSelectedScript();
    }

    private void OpenSelectedScriptInEditor()
    {
        if (!TryResolveSelectedScriptFilePath(out var filePath))
        {
            return;
        }

        if (Window.GetWindow(this) is not MainWindow mainWindow)
        {
            ShowOpenScriptError(LocalizationService.Instance.T("Library.Dialog.OpenError.NavigationUnavailable"));
            return;
        }

        mainWindow.NavigateToScriptEditor(filePath);
    }

    private void RunSelectedScript()
    {
        if (!TryResolveSelectedScriptFilePath(out var filePath))
        {
            return;
        }

        _ = ScriptEditorPageViewModel.TryRunScriptFromExternal(filePath, LocalizationService.Instance);
    }

    private bool TryResolveSelectedScriptFilePath(out string filePath)
    {
        if (DataContext is not MyScriptsPageViewModel viewModel || viewModel.SelectedScript is null)
        {
            filePath = string.Empty;
            return false;
        }

        if (_managedScriptLibraryService.TryGetManagedScriptFilePath(viewModel.SelectedScript.ScriptId, out filePath))
        {
            return true;
        }

        ShowOpenScriptError(LocalizationService.Instance.T("Library.Dialog.OpenError.ScriptMissing"));
        filePath = string.Empty;
        return false;
    }

    private void ShowOpenScriptError(string message)
    {
        var localizationService = LocalizationService.Instance;
        _ = _appDialogService.Show(new AppDialogRequest
        {
            Title = localizationService.T("Editor.File.OpenError.Title"),
            Message = string.Format(localizationService.T("Editor.File.OpenError.Message"), message),
            PrimaryButtonText = localizationService.T("Editor.Dialog.Ok")
        });
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hostWindow = Window.GetWindow(this);
        if (_hostWindow is null)
        {
            if (DataContext is MyScriptsPageViewModel detachedViewModel)
            {
                await detachedViewModel.EnsureInitializedAsync();
            }

            return;
        }

        _hostWindow.SizeChanged += OnHostWindowSizeChanged;
        UpdateMaxHeightFromHostWindow();

        if (DataContext is MyScriptsPageViewModel viewModel)
        {
            await viewModel.EnsureInitializedAsync();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hostWindow is null)
        {
            return;
        }

        _hostWindow.SizeChanged -= OnHostWindowSizeChanged;
        _hostWindow = null;
    }

    private void OnHostWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateMaxHeightFromHostWindow();
    }

    private void UpdateMaxHeightFromHostWindow()
    {
        if (_hostWindow is null)
        {
            return;
        }

        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(UpdateMaxHeightFromHostWindow, DispatcherPriority.Loaded);
            return;
        }

        var maxHeight = _hostWindow.ActualHeight - PageMaxHeightOffset;
        MaxHeight = maxHeight > 0 ? maxHeight : 0;
    }
}
