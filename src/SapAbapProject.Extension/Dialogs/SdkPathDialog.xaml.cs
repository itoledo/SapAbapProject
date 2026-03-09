using System.Windows;
using Microsoft.VisualStudio.PlatformUI;
using SapAbapProject.RfcExtractor;

namespace SapAbapProject.Extension.Dialogs;

public partial class SdkPathDialog : DialogWindow
{
    public string? SelectedSdkPath { get; private set; }

    public SdkPathDialog()
    {
        InitializeComponent();

        // Pre-fill if there's a saved path
        var existing = SapRfcSdkManager.GetConfiguredSdkPath();
        if (existing is not null)
            SdkPathTextBox.Text = existing;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the folder containing SAP NetWeaver RFC SDK (sapnwrfc.dll)",
            ShowNewFolderButton = false,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            SdkPathTextBox.Text = dialog.SelectedPath;
        }
    }

    private void SdkPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        var path = SdkPathTextBox.Text.Trim();
        var isValid = SapRfcSdkManager.IsValidSdkPath(path);

        OkButton.IsEnabled = isValid;

        if (string.IsNullOrEmpty(path))
        {
            ValidationText.Text = "";
            ValidationText.Foreground = System.Windows.Media.Brushes.Gray;
        }
        else if (isValid)
        {
            ValidationText.Text = "Valid SDK path - sapnwrfc.dll found";
            ValidationText.Foreground = System.Windows.Media.Brushes.Green;
        }
        else
        {
            ValidationText.Text = "Invalid path - sapnwrfc.dll and ICU libraries not found";
            ValidationText.Foreground = System.Windows.Media.Brushes.Red;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        var path = SdkPathTextBox.Text.Trim();
        SapRfcSdkManager.SaveSdkPath(path);
        SapRfcSdkManager.EnsureSdkLoaded(path);
        SelectedSdkPath = path;
        DialogResult = true;
        Close();
    }
}
