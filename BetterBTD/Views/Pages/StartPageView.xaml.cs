using System.Windows.Controls;
using BetterBTD.Services;
using BetterBTD.ViewModels;

namespace BetterBTD.Views.Pages;

public partial class StartPageView : Page
{
    public StartPageView()
    {
        InitializeComponent();
        DataContext = new StartPageViewModel(LocalizationService.Instance, MaskWindowService.Instance);
    }
}
