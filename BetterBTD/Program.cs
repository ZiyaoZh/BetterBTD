using System;
using System.Windows;

namespace BetterBTD;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new App();
        app.LaunchArguments = args;
        app.InitializeComponent();
        app.Run();
    }
}
