using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBTD.ViewModels;
using Wpf.Ui.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace BetterBTD.Views.Windows;

public partial class ScriptExecutionWindow : FluentWindow
{
    private const int HotkeyIdBase = 0xB10;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private static readonly uint[] HotkeyModifiers =
    [
        0,
        ModShift,
        ModControl,
        ModAlt,
        ModWin,
        ModShift | ModControl,
        ModShift | ModAlt,
        ModShift | ModWin,
        ModControl | ModAlt,
        ModControl | ModWin,
        ModAlt | ModWin,
        ModShift | ModControl | ModAlt,
        ModShift | ModControl | ModWin,
        ModShift | ModAlt | ModWin,
        ModControl | ModAlt | ModWin,
        ModShift | ModControl | ModAlt | ModWin
    ];

    private int _lastLogTextLength;
    private HwndSource? _hwndSource;
    private bool _isGlobalHotkeyRegistered;
    private readonly HashSet<int> _registeredHotkeyIds = [];
    private readonly HashSet<uint> _registeredHotkeyModifiers = [];

    public ScriptExecutionWindow(ScriptExecutionWindowViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        InitializeComponent();
        DataContext = viewModel;
        DataContextChanged += OnDataContextChanged;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
        TryRegisterGlobalHotkey();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is ScriptExecutionWindowViewModel viewModel)
        {
            viewModel.HandleWindowClosing();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        ReleaseGlobalHotkey();
        DataContextChanged -= OnDataContextChanged;

        if (DataContext is INotifyPropertyChanged currentViewModel)
        {
            currentViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }

        SourceInitialized -= OnSourceInitialized;
        Closing -= OnClosing;
        Closed -= OnClosed;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldViewModel)
        {
            oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        if (e.NewValue is INotifyPropertyChanged newViewModel)
        {
            newViewModel.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ScriptExecutionWindowViewModel.FocusedStep), StringComparison.Ordinal))
        {
            return;
        }

        if (sender is not ScriptExecutionWindowViewModel viewModel)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (viewModel.FocusedStep is not null)
            {
                ScrollItemIntoPreferredView(SequenceListBox, viewModel.FocusedStep);
            }
        }, DispatcherPriority.Background);
    }

    private void OnLogTextBoxLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        _lastLogTextLength = textBox.Text.Length;
        textBox.CaretIndex = textBox.Text.Length;
        textBox.ScrollToEnd();
    }

    private void OnLogTextBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not WpfTextBox textBox)
        {
            return;
        }

        var shouldAutoScroll = !textBox.IsKeyboardFocusWithin ||
                               (textBox.SelectionLength == 0 && textBox.CaretIndex >= _lastLogTextLength);

        _lastLogTextLength = textBox.Text.Length;

        if (!shouldAutoScroll)
        {
            return;
        }

        textBox.CaretIndex = textBox.Text.Length;
        textBox.ScrollToEnd();
    }

    private void OnSequenceListBoxSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox listBox || listBox.SelectedItem is null)
        {
            return;
        }

        _ = Dispatcher.BeginInvoke(() =>
        {
            if (listBox.SelectedItem is null)
            {
                return;
            }

            ScrollItemIntoPreferredView(listBox, listBox.SelectedItem);
        }, DispatcherPriority.Background);
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsF10KeyEvent(e))
        {
            return;
        }

        if (_isGlobalHotkeyRegistered &&
            _registeredHotkeyModifiers.Contains(ToNativeModifiers(Keyboard.Modifiers)))
        {
            return;
        }

        e.Handled = true;
        await ToggleExecutionAsync();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registeredHotkeyIds.Contains(wParam.ToInt32()))
        {
            handled = true;
            _ = Dispatcher.InvokeAsync(ToggleExecutionAsync);
        }

        return IntPtr.Zero;
    }

    private void TryRegisterGlobalHotkey()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(Key.F10);
        _registeredHotkeyIds.Clear();
        _registeredHotkeyModifiers.Clear();

        var firstErrorCode = 0;

        for (var index = 0; index < HotkeyModifiers.Length; index++)
        {
            var hotkeyId = HotkeyIdBase + index;
            var modifiers = HotkeyModifiers[index];
            if (RegisterHotKey(handle, hotkeyId, modifiers, virtualKey))
            {
                _registeredHotkeyIds.Add(hotkeyId);
                _registeredHotkeyModifiers.Add(modifiers);
                continue;
            }

            if (firstErrorCode == 0)
            {
                firstErrorCode = Marshal.GetLastWin32Error();
            }
        }

        _isGlobalHotkeyRegistered = _registeredHotkeyIds.Count > 0;
        if (_isGlobalHotkeyRegistered)
        {
            return;
        }

        System.Windows.MessageBox.Show(
            this,
            $"F10 global hotkey registration failed for all modifier combinations (Win32: {firstErrorCode}). The hotkey may already be in use by another application.",
            "BetterBTD",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);
    }

    private void ReleaseGlobalHotkey()
    {
        if (!_isGlobalHotkeyRegistered)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            foreach (var hotkeyId in _registeredHotkeyIds)
            {
                _ = UnregisterHotKey(handle, hotkeyId);
            }
        }

        _registeredHotkeyIds.Clear();
        _registeredHotkeyModifiers.Clear();
        _isGlobalHotkeyRegistered = false;
    }

    private async Task ToggleExecutionAsync()
    {
        if (DataContext is not ScriptExecutionWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.StopCommand.CanExecute(null))
        {
            viewModel.StopCommand.Execute(null);
            return;
        }

        if (viewModel.StartCommand.CanExecute(null))
        {
            await viewModel.StartCommand.ExecuteAsync(null);
        }
    }

    private static void ScrollItemIntoPreferredView(ListBox listBox, object item)
    {
        listBox.UpdateLayout();
        listBox.ScrollIntoView(item);
        listBox.UpdateLayout();

        if (listBox.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container)
        {
            return;
        }

        var scrollViewer = FindDescendant<ScrollViewer>(listBox);
        if (scrollViewer is null || scrollViewer.ViewportHeight <= 0)
        {
            return;
        }

        var itemTop = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
        var desiredTop = Math.Max(0, (scrollViewer.ViewportHeight - container.ActualHeight) / 2d);
        var targetOffset = Math.Clamp(
            scrollViewer.VerticalOffset + itemTop - desiredTop,
            0,
            scrollViewer.ScrollableHeight);

        if (Math.Abs(targetOffset - scrollViewer.VerticalOffset) > 0.5d)
        {
            scrollViewer.ScrollToVerticalOffset(targetOffset);
        }
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T target)
            {
                return target;
            }

            if (FindDescendant<T>(child) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static bool IsF10KeyEvent(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        return e.Key == Key.F10 ||
               e.SystemKey == Key.F10 ||
               e.ImeProcessedKey == Key.F10;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        var nativeModifiers = 0u;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            nativeModifiers |= ModAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            nativeModifiers |= ModControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            nativeModifiers |= ModShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            nativeModifiers |= ModWin;
        }

        return nativeModifiers;
    }
}
