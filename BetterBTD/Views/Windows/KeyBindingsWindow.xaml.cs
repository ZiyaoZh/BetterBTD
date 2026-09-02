using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using BetterBTD.Models;
using BetterBTD.View.Controls.KeyBindings;
using BetterBTD.ViewModels;
using Wpf.Ui.Controls;

namespace BetterBTD.Views.Windows;

public partial class KeyBindingsWindow : FluentWindow
{
    private readonly string? _targetConfigPropertyPath;

    public KeyBindingsWindow(string? targetConfigPropertyPath = null)
    {
        _targetConfigPropertyPath = targetConfigPropertyPath;
        InitializeComponent();
        DataContext = new KeyBindingsSettingsPageViewModel();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        if (string.IsNullOrWhiteSpace(_targetConfigPropertyPath) ||
            DataContext is not KeyBindingsSettingsPageViewModel viewModel ||
            viewModel.FindItem(_targetConfigPropertyPath) is not { } targetItem)
        {
            return;
        }

        KeyBindingsTree.SelectedItem = targetItem;
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => FocusTargetEditor(targetItem));
    }

    private void FocusTargetEditor(KeyBindingSettingItem targetItem)
    {
        KeyBindingsTree.UpdateLayout();
        if (KeyBindingsTree.ItemContainerGenerator.ContainerFromItem(targetItem) is FrameworkElement container)
        {
            container.BringIntoView();
            KeyBindingsTree.UpdateLayout();
        }

        var editor = FindVisualChildren<KeyBindingTextBox>(KeyBindingsTree)
            .FirstOrDefault(candidate => ReferenceEquals(candidate.DataContext, targetItem));
        if (editor is null)
        {
            return;
        }

        editor.BringIntoView();
        _ = editor.Focus();
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
