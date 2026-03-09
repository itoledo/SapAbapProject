using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SapAbapProject.Extension.Services;

namespace SapAbapProject.Extension.Dialogs;

public partial class ImportWizardDialog : Window
{
    private readonly ImportWizardViewModel _viewModel;

    public ImportWizardDialog(string projectPath)
    {
        InitializeComponent();
        _viewModel = new ImportWizardViewModel(projectPath, () => Close());
        DataContext = _viewModel;

        // Wire up PasswordBox (can't bind directly in WPF)
        PasswordBox.PasswordChanged += (s, e) => _viewModel.Password = PasswordBox.Password;

        // If there are recent connections, stay on the Recent tab; otherwise switch to New
        if (_viewModel.RecentConnections.Count == 0)
            Step1Tabs.SelectedIndex = 1;
    }

    private void OnRecentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is ConnectionEntry entry)
        {
            _viewModel.ApplyRecentConnection(entry);
            PasswordBox.Password = entry.Password;
            _viewModel.OnNext();
        }
    }

    private void OnDeleteRecent(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ConnectionEntry entry)
        {
            _viewModel.RemoveRecentConnection(entry);
        }
    }
}
