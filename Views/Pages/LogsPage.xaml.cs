using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using HorizonRadioOverlay.ViewModels;

namespace HorizonRadioOverlay.Views.Pages;

[SupportedOSPlatform("windows")]
public partial class LogsPage : UserControl
{
    public LogsPage()
    {
        InitializeComponent();
        DataContextChanged += LogsPage_DataContextChanged;
    }

    private void LogsPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LogsViewModel oldVm)
        {
            oldVm.RequestScrollToEnd -= ScrollToEnd;
        }

        if (e.NewValue is LogsViewModel newVm)
        {
            newVm.RequestScrollToEnd += ScrollToEnd;
        }
    }

    public void ScrollToEnd()
    {
        LogScrollViewer.ScrollToEnd();
    }
}
