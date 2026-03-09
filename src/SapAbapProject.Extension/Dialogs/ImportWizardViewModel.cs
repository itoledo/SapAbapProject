using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.Threading;
using SapAbapProject.Core.Models;
using SapAbapProject.Core.Interfaces;
using SapAbapProject.Extension.Services;
using SapAbapProject.RfcExtractor;

namespace SapAbapProject.Extension.Dialogs;

internal sealed class ImportWizardViewModel : INotifyPropertyChanged
{
    private readonly string _projectPath;
    private readonly Action _closeAction;
    private int _currentStep = 1;
    private CancellationTokenSource? _cts;

    // Recent connections
    public ObservableCollection<ConnectionEntry> RecentConnections { get; } = new();
    private ConnectionEntry? _selectedRecentConnection;
    public ConnectionEntry? SelectedRecentConnection
    {
        get => _selectedRecentConnection;
        set { _selectedRecentConnection = value; OnPropertyChanged(nameof(SelectedRecentConnection)); }
    }

    public Visibility RecentEmptyVisibility => RecentConnections.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    // Connection properties
    public string AppServerHost { get; set; } = "";
    public string SystemNumber { get; set; } = "00";
    public string Client { get; set; } = "100";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public string Language { get; set; } = "EN";
    public string SapRouter { get; set; } = "";

    // Step 2 properties
    public string PackageSearchPattern { get; set; } = "Z*";
    public string FunctionModulePattern { get; set; } = "";
    public string FunctionGroupFilter { get; set; } = "";
    public ObservableCollection<SelectableItem> AvailablePackages { get; } = new();
    public ObservableCollection<SelectableItem> AvailableObjectTypes { get; } = new();
    public bool IncludeDocumentation { get; set; } = true;
    public bool IncludeSignature { get; set; } = true;
    public bool OverwriteExisting { get; set; } = true;

    // Step 3 properties
    public ObservableCollection<string> LogMessages { get; } = new();

    private string _connectionStatus = "";
    public string ConnectionStatus
    {
        get => _connectionStatus;
        set { _connectionStatus = value; OnPropertyChanged(nameof(ConnectionStatus)); }
    }

    private Brush _connectionStatusColor = Brushes.Black;
    public Brush ConnectionStatusColor
    {
        get => _connectionStatusColor;
        set { _connectionStatusColor = value; OnPropertyChanged(nameof(ConnectionStatusColor)); }
    }

    private int _progressValue;
    public int ProgressValue
    {
        get => _progressValue;
        set { _progressValue = value; OnPropertyChanged(nameof(ProgressValue)); }
    }

    private string _progressText = "";
    public string ProgressText
    {
        get => _progressText;
        set { _progressText = value; OnPropertyChanged(nameof(ProgressText)); }
    }

    public string StepTitle => _currentStep switch
    {
        1 => "Step 1: SAP System Connection",
        2 => "Step 2: Select Packages and Object Types",
        3 => "Step 3: Import Progress",
        _ => ""
    };

    public Visibility IsStep1Visible => _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsStep2Visible => _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility IsStep3Visible => _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

    public string NextButtonText => _currentStep switch
    {
        1 => "Next >",
        2 => "Import",
        3 => "Close",
        _ => "Next >"
    };

    public ICommand TestConnectionCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SearchPackagesCommand { get; }

    public ImportWizardViewModel(string projectPath, Action closeAction)
    {
        _projectPath = projectPath;
        _closeAction = closeAction;

        TestConnectionCommand = new RelayCommand(OnTestConnection);
        NextCommand = new RelayCommand(OnNext);
        BackCommand = new RelayCommand(OnBack, () => _currentStep == 2);
        SearchPackagesCommand = new RelayCommand(OnSearchPackages);

        // Load recent connections
        foreach (var entry in ConnectionStore.Load().Take(10))
            RecentConnections.Add(entry);

        // Initialize ABAP object types
        foreach (var type in Enum.GetValues(typeof(AbapObjectType)).Cast<AbapObjectType>())
        {
            AvailableObjectTypes.Add(new SelectableItem(type.ToString()));
        }
    }

    public void ApplyRecentConnection(ConnectionEntry entry)
    {
        AppServerHost = entry.AppServerHost;
        SystemNumber = entry.SystemNumber;
        Client = entry.Client;
        User = entry.User;
        Password = entry.Password;
        Language = entry.Language;
        SapRouter = entry.SapRouter ?? "";

        OnPropertyChanged(nameof(AppServerHost));
        OnPropertyChanged(nameof(SystemNumber));
        OnPropertyChanged(nameof(Client));
        OnPropertyChanged(nameof(User));
        OnPropertyChanged(nameof(Language));
        OnPropertyChanged(nameof(SapRouter));
    }

    public void RemoveRecentConnection(ConnectionEntry entry)
    {
        ConnectionStore.Remove(entry);
        RecentConnections.Remove(entry);
        OnPropertyChanged(nameof(RecentEmptyVisibility));
    }

    private SapConnectionSettings BuildConnectionSettings() => new()
    {
        AppServerHost = AppServerHost,
        SystemNumber = SystemNumber,
        Client = Client,
        User = User,
        Password = Password,
        Language = Language,
        SapRouter = string.IsNullOrWhiteSpace(SapRouter) ? null : SapRouter
    };

    private void SaveCurrentConnection()
    {
        if (string.IsNullOrWhiteSpace(AppServerHost))
            return;

        var entry = new ConnectionEntry
        {
            AppServerHost = AppServerHost,
            SystemNumber = SystemNumber,
            Client = Client,
            User = User,
            Password = Password,
            Language = Language,
            SapRouter = string.IsNullOrWhiteSpace(SapRouter) ? null : SapRouter
        };

        ConnectionStore.AddOrUpdate(entry);

        // Refresh the recent list
        RecentConnections.Clear();
        foreach (var e in ConnectionStore.Load().Take(10))
            RecentConnections.Add(e);
        OnPropertyChanged(nameof(RecentEmptyVisibility));
    }

    private void OnTestConnection()
    {
        TestConnectionCoreAsync().Forget();
    }

    private async Task TestConnectionCoreAsync()
    {
        ConnectionStatus = "Testing connection...";
        ConnectionStatusColor = Brushes.Gray;

        try
        {
            using var extractor = new AbapObjectExtractor(BuildConnectionSettings());
            await extractor.TestConnectionAsync();
            ConnectionStatus = "Connection successful!";
            ConnectionStatusColor = Brushes.Green;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Failed: {ex.Message}";
            ConnectionStatusColor = Brushes.Red;
        }
    }

    private void OnSearchPackages()
    {
        SearchPackagesCoreAsync().Forget();
    }

    private async Task SearchPackagesCoreAsync()
    {
        try
        {
            ConnectionStatus = "Searching packages...";
            ConnectionStatusColor = Brushes.Gray;

            using var extractor = new AbapObjectExtractor(BuildConnectionSettings());
            var packages = await extractor.GetPackagesAsync(PackageSearchPattern);

            AvailablePackages.Clear();
            foreach (var package in packages)
            {
                AvailablePackages.Add(new SelectableItem(package));
            }

            ConnectionStatus = $"Found {packages.Count} packages.";
            ConnectionStatusColor = Brushes.Green;
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Failed to search packages: {ex.Message}";
            ConnectionStatusColor = Brushes.Red;
        }
    }

    public void OnNext()
    {
        switch (_currentStep)
        {
            case 1:
                // If a recent connection is selected and fields are empty, apply it
                if (string.IsNullOrWhiteSpace(AppServerHost) && SelectedRecentConnection is not null)
                    ApplyRecentConnection(SelectedRecentConnection);

                SaveCurrentConnection();
                LoadPackagesAsync().Forget();
                break;
            case 2:
                RunImportAsync().Forget();
                break;
            case 3:
                _closeAction();
                break;
        }
    }

    private void OnBack()
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            NotifyStepChanged();
        }
    }

    private async Task LoadPackagesAsync()
    {
        try
        {
            ConnectionStatus = "Loading packages...";
            ConnectionStatusColor = Brushes.Gray;

            if (AvailablePackages.Count == 0)
            {
                // Auto-search with default pattern if none loaded yet
                using var extractor = new AbapObjectExtractor(BuildConnectionSettings());
                var packages = await extractor.GetPackagesAsync(PackageSearchPattern);

                AvailablePackages.Clear();
                foreach (var package in packages)
                    AvailablePackages.Add(new SelectableItem(package));

                ConnectionStatus = $"Found {packages.Count} packages.";
                ConnectionStatusColor = Brushes.Green;
            }

            _currentStep = 2;
            NotifyStepChanged();
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Failed to load packages: {ex.Message}";
            ConnectionStatusColor = Brushes.Red;
        }
    }

    private async Task RunImportAsync()
    {
        _currentStep = 3;
        NotifyStepChanged();

        _cts = new CancellationTokenSource();

        var selectedPackages = AvailablePackages
            .Where(s => s.IsSelected)
            .Select(s => s.Name)
            .ToList();

        var selectedTypes = AvailableObjectTypes
            .Where(t => t.IsSelected)
            .Select(t => (AbapObjectType)Enum.Parse(typeof(AbapObjectType), t.Name))
            .ToList();

        var options = new ImportOptions
        {
            Packages = selectedPackages,
            ObjectTypes = selectedTypes,
            FunctionModuleNamePattern = string.IsNullOrWhiteSpace(FunctionModulePattern) ? null : FunctionModulePattern,
            FunctionGroupFilter = string.IsNullOrWhiteSpace(FunctionGroupFilter) ? null : FunctionGroupFilter,
            OverwriteExisting = OverwriteExisting,
            IncludeDocumentation = IncludeDocumentation,
            IncludeSignature = IncludeSignature
        };

        var progress = new Progress<ImportProgress>(p =>
        {
            ProgressValue = p.TotalCount > 0 ? (int)((p.ProcessedCount * 100.0) / p.TotalCount) : 0;
            ProgressText = p.CurrentObject;

            var prefix = p.IsError ? "[ERROR] " : "[OK] ";
            LogMessages.Add($"{prefix}{p.CurrentObject}");

            if (p.ErrorMessage != null)
                LogMessages.Add($"       {p.ErrorMessage}");
        });

        try
        {
            LogMessages.Add($"Connecting to {AppServerHost} [{Client}]...");

            using var extractor = new AbapObjectExtractor(BuildConnectionSettings());
            var objects = await Task.Run(
                () => extractor.ExtractObjectsAsync(options, progress, _cts.Token));

            LogMessages.Add($"Extracted {objects.Count} objects.");
            LogMessages.Add($"Writing files to {_projectPath}...");

            var writer = new ScriptFileWriter();
            await Task.Run(() => writer.WriteAsync(_projectPath, objects, options, progress, _cts.Token));

            ProgressValue = 100;
            ProgressText = "Import completed!";
            LogMessages.Add($"Done! {objects.Count} files written.");
        }
        catch (OperationCanceledException)
        {
            LogMessages.Add("Import cancelled by user.");
            ProgressText = "Cancelled.";
        }
        catch (Exception ex)
        {
            LogMessages.Add($"[ERROR] {ex.Message}");
            ProgressText = "Import failed.";
        }
    }

    private void NotifyStepChanged()
    {
        OnPropertyChanged(nameof(StepTitle));
        OnPropertyChanged(nameof(IsStep1Visible));
        OnPropertyChanged(nameof(IsStep2Visible));
        OnPropertyChanged(nameof(IsStep3Visible));
        OnPropertyChanged(nameof(NextButtonText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
