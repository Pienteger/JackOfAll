using System.Windows;
using System.Windows.Controls;
using JackOfAll.Lib.Services;

namespace JackOfAll.UserControls;

public partial class TimestampToolView : UserControl
{
    public TimestampToolView()
    {
        InitializeComponent();
        RefreshNow();
    }

    private void OnRefreshNow(object sender, RoutedEventArgs e) => RefreshNow();

    private void OnCopyNow(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(NowOutput.Text))
            Clipboard.SetText(NowOutput.Text);
    }

    private void RefreshNow()
    {
        NowOutput.Text = TimestampService.GetCurrentTimestamps();
    }

    private void OnTimestampToDate(object sender, RoutedEventArgs e)
    {
        var (result, error) = TimestampService.FromUnixTimestamp(TimestampInput.Text);
        if (error != null)
        {
            TimestampToDateOutput.Visibility = Visibility.Collapsed;
            TimestampError.Text = error;
            TimestampError.Visibility = Visibility.Visible;
        }
        else
        {
            TimestampError.Visibility = Visibility.Collapsed;
            TimestampToDateOutput.Text = result;
            TimestampToDateOutput.Visibility = Visibility.Visible;
        }
    }

    private void OnDateToTimestamp(object sender, RoutedEventArgs e)
    {
        var (result, error) = TimestampService.ToUnixTimestamp(DateInput.Text);
        if (error != null)
        {
            DateToTimestampOutput.Visibility = Visibility.Collapsed;
            DateError.Text = error;
            DateError.Visibility = Visibility.Visible;
        }
        else
        {
            DateError.Visibility = Visibility.Collapsed;
            DateToTimestampOutput.Text = result;
            DateToTimestampOutput.Visibility = Visibility.Visible;
        }
    }
}
