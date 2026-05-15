using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;

namespace BetterBTD.Views.Windows;

public sealed class CapturableWindow
{
    public CapturableWindow(nint handle, string title, string processName, ImageSource? icon)
    {
        Handle = handle;
        Title = title;
        ProcessName = processName;
        Icon = icon;
    }

    public nint Handle { get; }

    public string Title { get; }

    public string ProcessName { get; }

    public ImageSource? Icon { get; }

    public string DisplayName => $"{Title} ({ProcessName})";
}

public partial class PickerWindow : FluentWindow
{
    private const int GwlExstyle = -20;
    private const int WmGetIcon = 0x007F;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const int GclHicon = -14;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExLayered = 0x00080000L;
    private const long WsExNoRedirectionBitmap = 0x00200000L;

    private readonly bool _isCaptureTest;
    private bool _isSelected;

    public PickerWindow(bool isCaptureTest = false)
    {
        InitializeComponent();
        _isCaptureTest = isCaptureTest;
        Loaded += OnLoaded;
    }

    public bool TryPickCaptureTarget(Window? owner, out CapturableWindow selectedWindow)
    {
        if (owner is not null)
        {
            Owner = owner;
        }

        _ = ShowDialog();
        selectedWindow = WindowList.SelectedItem as CapturableWindow ?? null!;
        return _isSelected && selectedWindow is not null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PickerTitleBar.Title = _isCaptureTest ? "选择测试窗口" : "选择捕获窗口";
        Title = PickerTitleBar.Title;
        RefreshWindows();
    }

    private void RefreshWindows()
    {
        var windows = new List<CapturableWindow>();
        var ownerHandle = new WindowInteropHelper(this).Handle;
        var preferredTitle = ConfigurationService.Instance.Current.MaskWindowTargetTitle;

        _ = EnumWindows((hWnd, _) =>
        {
            if (hWnd == ownerHandle || !IsWindowVisible(hWnd))
            {
                return true;
            }

            var exStyle = GetWindowLongPtr(hWnd, GwlExstyle).ToInt64();
            if ((exStyle & (WsExToolWindow | WsExLayered | WsExNoRedirectionBitmap)) != 0)
            {
                return true;
            }

            var title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            GetWindowThreadProcessId(hWnd, out var processId);
            var processName = TryGetProcessName(processId);
            windows.Add(new CapturableWindow(hWnd, title, processName, GetWindowIcon(hWnd)));
            return true;
        }, nint.Zero);

        var orderedWindows = windows
            .OrderByDescending(window => string.Equals(window.Title, preferredTitle, StringComparison.OrdinalIgnoreCase))
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        WindowList.ItemsSource = new ObservableCollection<CapturableWindow>(orderedWindows);
        WindowList.SelectedIndex = orderedWindows.Count > 0 ? 0 : -1;
    }

    private void WindowList_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        ConfirmSelection();
    }

    private void SelectButton_OnClick(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isSelected = false;
        Close();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _isSelected = false;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            ConfirmSelection();
        }
    }

    private void ConfirmSelection()
    {
        if (WindowList.SelectedItem is not CapturableWindow)
        {
            return;
        }

        _isSelected = true;
        Close();
    }

    private static string GetWindowTitle(nint hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(length + 1);
        _ = GetWindowText(hWnd, builder, builder.Capacity);
        return builder.ToString();
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    private static ImageSource? GetWindowIcon(nint hWnd)
    {
        try
        {
            var iconHandle = SendMessage(hWnd, WmGetIcon, (nint)IconBig, nint.Zero);
            if (iconHandle == nint.Zero)
            {
                iconHandle = SendMessage(hWnd, WmGetIcon, (nint)IconSmall, nint.Zero);
            }

            if (iconHandle == nint.Zero)
            {
                iconHandle = GetClassLongPtr(hWnd, GclHicon);
            }

            if (iconHandle == nint.Zero)
            {
                return null;
            }

            return Imaging.CreateBitmapSourceFromHIcon(
                iconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return null;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern nint GetClassLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
}
