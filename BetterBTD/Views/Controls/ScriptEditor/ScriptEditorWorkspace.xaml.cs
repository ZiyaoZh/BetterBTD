using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BetterBTD.Views.Controls.ScriptEditor;

public partial class ScriptEditorWorkspace : UserControl
{
    private const double DragAutoScrollBoundary = 24d;
    private static readonly TimeSpan DragAutoScrollInterval = TimeSpan.FromMilliseconds(150);

    private readonly DispatcherTimer _instructionSequenceAutoScrollTimer;
    private ScrollViewer? _instructionSequenceScrollViewer;
    private DragAutoScrollDirection _instructionSequenceAutoScrollDirection;

    public ScriptEditorWorkspace()
    {
        InitializeComponent();

        _instructionSequenceAutoScrollTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = DragAutoScrollInterval
        };
        _instructionSequenceAutoScrollTimer.Tick += InstructionSequenceAutoScrollTimer_Tick;

        InstructionSequenceListBox.AddHandler(
            DragDrop.PreviewDragOverEvent,
            new DragEventHandler(InstructionSequenceListBox_PreviewDragOver),
            handledEventsToo: true);
        InstructionSequenceListBox.AddHandler(
            DragDrop.DragLeaveEvent,
            new DragEventHandler(InstructionSequenceListBox_DragLeave),
            handledEventsToo: true);
        InstructionSequenceListBox.AddHandler(
            DragDrop.DropEvent,
            new DragEventHandler(InstructionSequenceListBox_Drop),
            handledEventsToo: true);

        Loaded += ScriptEditorWorkspace_Loaded;
        Unloaded += ScriptEditorWorkspace_Unloaded;
    }

    private void InstructionSequenceListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
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

            listBox.UpdateLayout();
            listBox.ScrollIntoView(listBox.SelectedItem);
        }, DispatcherPriority.Background);
    }

    private void ScriptEditorWorkspace_Loaded(object sender, RoutedEventArgs e)
    {
        _instructionSequenceScrollViewer = FindDescendant<ScrollViewer>(InstructionSequenceListBox);
    }

    private void ScriptEditorWorkspace_Unloaded(object sender, RoutedEventArgs e)
    {
        StopInstructionSequenceAutoScroll();
        _instructionSequenceScrollViewer = null;
    }

    private void InstructionSequenceListBox_PreviewDragOver(object sender, DragEventArgs e)
    {
        var scrollViewer = _instructionSequenceScrollViewer ??= FindDescendant<ScrollViewer>(InstructionSequenceListBox);
        if (scrollViewer is null || e.Effects == DragDropEffects.None)
        {
            StopInstructionSequenceAutoScroll();
            return;
        }

        var position = e.GetPosition(scrollViewer);
        var scrollBoundary = Math.Min(DragAutoScrollBoundary, scrollViewer.ActualHeight / 2d);
        if (position.Y < scrollBoundary && scrollViewer.VerticalOffset > 0)
        {
            StartInstructionSequenceAutoScroll(DragAutoScrollDirection.Up);
        }
        else if (position.Y >= scrollViewer.ActualHeight - scrollBoundary && scrollViewer.VerticalOffset < scrollViewer.ScrollableHeight)
        {
            StartInstructionSequenceAutoScroll(DragAutoScrollDirection.Down);
        }
        else
        {
            StopInstructionSequenceAutoScroll();
        }
    }

    private void InstructionSequenceListBox_DragLeave(object sender, DragEventArgs e)
    {
        StopInstructionSequenceAutoScroll();
    }

    private void InstructionSequenceListBox_Drop(object sender, DragEventArgs e)
    {
        StopInstructionSequenceAutoScroll();
    }

    private void InstructionSequenceAutoScrollTimer_Tick(object? sender, EventArgs e)
    {
        if (_instructionSequenceScrollViewer is null)
        {
            StopInstructionSequenceAutoScroll();
            return;
        }

        var previousOffset = _instructionSequenceScrollViewer.VerticalOffset;
        switch (_instructionSequenceAutoScrollDirection)
        {
            case DragAutoScrollDirection.Up:
                _instructionSequenceScrollViewer.LineUp();
                break;
            case DragAutoScrollDirection.Down:
                _instructionSequenceScrollViewer.LineDown();
                break;
            default:
                StopInstructionSequenceAutoScroll();
                return;
        }

        if (Math.Abs(_instructionSequenceScrollViewer.VerticalOffset - previousOffset) < double.Epsilon)
        {
            StopInstructionSequenceAutoScroll();
        }
    }

    private void StartInstructionSequenceAutoScroll(DragAutoScrollDirection direction)
    {
        _instructionSequenceAutoScrollDirection = direction;
        if (!_instructionSequenceAutoScrollTimer.IsEnabled)
        {
            _instructionSequenceAutoScrollTimer.Start();
        }
    }

    private void StopInstructionSequenceAutoScroll()
    {
        _instructionSequenceAutoScrollDirection = DragAutoScrollDirection.None;
        _instructionSequenceAutoScrollTimer.Stop();
    }

    private static T? FindDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T descendant)
            {
                return descendant;
            }

            var nestedDescendant = FindDescendant<T>(child);
            if (nestedDescendant is not null)
            {
                return nestedDescendant;
            }
        }

        return null;
    }

    private enum DragAutoScrollDirection
    {
        None,
        Up,
        Down
    }
}
