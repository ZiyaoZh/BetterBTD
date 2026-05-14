using System.Windows.Controls;
using System.Windows.Threading;

namespace BetterBTD.Views.Controls.ScriptEditor;

public partial class ScriptEditorWorkspace : UserControl
{
    public ScriptEditorWorkspace()
    {
        InitializeComponent();
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
}
