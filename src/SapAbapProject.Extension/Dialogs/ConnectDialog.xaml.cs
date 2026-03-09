using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SapAbapProject.Extension.Services;
using SapAbapProject.RfcExtractor;

namespace SapAbapProject.Extension.Dialogs;

internal partial class ConnectDialog : Window
{
    private readonly ObservableCollection<ConnectionEntry> _recentConnections = new();

    public string? ResultConnectionString { get; private set; }
    public string? ResultSystemInfo { get; private set; }

    public ConnectDialog()
    {
        InitializeComponent();

        RecentList.ItemsSource = _recentConnections;
        LoadRecentConnections();

        // Show "New Connection" tab if no recent connections
        if (_recentConnections.Count == 0)
            MainTabs.SelectedItem = NewConnectionTab;
    }

    private void LoadRecentConnections()
    {
        _recentConnections.Clear();
        foreach (var entry in ConnectionStore.Load())
            _recentConnections.Add(entry);

        EmptyMessage.Visibility = _recentConnections.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnConnect(object sender, RoutedEventArgs e)
    {
        if (MainTabs.SelectedItem == RecentTab)
        {
            // Connect using selected recent connection
            if (RecentList.SelectedItem is ConnectionEntry entry)
                ConnectWithEntry(entry);
            else
                StatusText.Text = "Select a connection from the list.";
        }
        else
        {
            // Connect using form fields
            ConnectWithEntry(BuildEntryFromFields());
        }
    }

    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is ConnectionEntry entry)
            ConnectWithEntry(entry);
    }

    private void OnDeleteRecent(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ConnectionEntry entry)
        {
            ConnectionStore.Remove(entry);
            _recentConnections.Remove(entry);
            EmptyMessage.Visibility = _recentConnections.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void OnParseConnectionString(object sender, RoutedEventArgs e)
    {
        var text = ConnStringBox.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            StatusText.Text = "Paste a connection string first.";
            StatusText.Foreground = Brushes.Orange;
            return;
        }

        var entry = ConnectionEntry.ParseConnectionString(text);
        if (entry == null)
        {
            StatusText.Text = "Could not parse connection string. Expected format: ASHOST=...;SYSNR=...;CLIENT=...";
            StatusText.Foreground = Brushes.Red;
            return;
        }

        PopulateFields(entry);
        StatusText.Text = "Connection string parsed. Review the fields and click Connect.";
        StatusText.Foreground = Brushes.Green;
    }

    private void PopulateFields(ConnectionEntry entry)
    {
        AppServerHostBox.Text = entry.AppServerHost;
        SystemNumberBox.Text = entry.SystemNumber;
        ClientBox.Text = entry.Client;
        UserBox.Text = entry.User;
        PasswordBox.Password = entry.Password;
        LanguageBox.Text = entry.Language;
        SapRouterBox.Text = entry.SapRouter ?? "";
    }

    private ConnectionEntry BuildEntryFromFields()
    {
        return new ConnectionEntry
        {
            AppServerHost = AppServerHostBox.Text.Trim(),
            SystemNumber = SystemNumberBox.Text.Trim(),
            Client = ClientBox.Text.Trim(),
            User = UserBox.Text.Trim(),
            Password = PasswordBox.Password,
            Language = LanguageBox.Text.Trim(),
            SapRouter = string.IsNullOrWhiteSpace(SapRouterBox.Text) ? null : SapRouterBox.Text.Trim()
        };
    }

    private void ConnectWithEntry(ConnectionEntry entry)
    {
        ConnectButton.IsEnabled = false;
        StatusText.Text = "Connecting...";
        StatusText.Foreground = Brushes.Gray;

        // Ensure the SDK is loaded before attempting a connection
        if (!SapRfcSdkManager.IsLoaded)
        {
            if (!SapRfcSdkManager.EnsureSdkLoaded())
            {
                StatusText.Text = "SAP RFC SDK not configured. Please configure the SDK path first.";
                StatusText.Foreground = Brushes.Red;
                ConnectButton.IsEnabled = true;
                return;
            }
        }

        var connectionString = entry.ToConnectionString();
        var savedEntry = entry.Clone();
        var systemInfo = entry.DisplayName;

        // TODO: Once RFC connection wrapper is available, perform a real connection test here.
        // For now, we save the entry and return success.
        _ = Task.Run(() =>
        {
            // Placeholder for actual SAP RFC connection test
            // e.g. using SapRfcConnection to call RFC_PING
            System.Threading.Thread.Sleep(500); // Simulate connection attempt
        }).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                var msg = t.Exception?.InnerException?.Message ?? t.Exception?.Message ?? "Unknown error";
                StatusText.Text = $"Connection failed: {msg}";
                StatusText.Foreground = Brushes.Red;
                ConnectButton.IsEnabled = true;
            }
            else
            {
                // Save to recent connections
                ConnectionStore.AddOrUpdate(savedEntry);

                ResultConnectionString = connectionString;
                ResultSystemInfo = systemInfo;
                DialogResult = true;
                Close();
            }
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
