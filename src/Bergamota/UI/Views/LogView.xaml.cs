using System.Windows;
using System.Windows.Controls;
using Bergamota.Core;

namespace Bergamota.UI.Views;

public partial class LogView : UserControl
{
    public LogView()
    {
        InitializeComponent();
        List.ItemsSource = ActivityLog.Entries;
        void sync() => Empty.Visibility = ActivityLog.Entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActivityLog.Entries.CollectionChanged += (_, _) => sync();
        sync();
    }
}
