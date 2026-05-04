using System.Windows.Controls;
using System.Windows.Input;

namespace BetterBTD.Views.Controls.ScriptEditor;

public partial class ScriptInstructionPropertiesPanel : UserControl
{
    public ScriptInstructionPropertiesPanel()
    {
        InitializeComponent();
    }

    private void PropertiesScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        var nextOffset = scrollViewer.VerticalOffset - e.Delta;
        if (nextOffset < 0)
        {
            nextOffset = 0;
        }
        else if (nextOffset > scrollViewer.ScrollableHeight)
        {
            nextOffset = scrollViewer.ScrollableHeight;
        }

        if (Math.Abs(nextOffset - scrollViewer.VerticalOffset) < double.Epsilon)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(nextOffset);
        e.Handled = true;
    }
}
