using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Bergamota.Models;

namespace Bergamota.UI.Views;

public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        Version.Text = "versão " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0");
    }

    void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Folder}\"") { UseShellExecute = true });
    }
}
