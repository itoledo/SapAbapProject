using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SapAbapProject.Extension.Commands;
using SapAbapProject.Extension.Dialogs;
using SapAbapProject.RfcExtractor;
using Task = System.Threading.Tasks.Task;

namespace SapAbapProject.Extension;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("SAP ABAP Source Project", "Manage SAP ABAP source code extracted from SAP systems via RFC", "1.0")]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideBindingPath]
[ProvideTextMateGrammarDirectory("SapAbapProject", "Grammars")]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class SapAbapPackage : AsyncPackage
{
    public const string PackageGuidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";
    public const string AbapUIContextGuid = "c8d9e0f1-a2b3-4567-8901-23456789abcd";

    // Must hold references to prevent COM event sinks from being GC'd
    private Events? _dteEvents;
    private WindowEvents? _windowEvents;

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken,
        IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await ImportFromSapCommand.InitializeAsync(this);
        await ConnectSapCommand.InitializeAsync(this);

        // Check if SAP RFC SDK path is configured; prompt on first load
        try
        {
            CheckSdkConfiguration();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SAP ABAP: SDK check failed: {ex.Message}");
            // Don't prevent the package from loading if SDK dialog fails
        }

        TrackActiveDocument();
    }

    private void CheckSdkConfiguration()
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var sdkPath = SapRfcSdkManager.GetConfiguredSdkPath();
        if (sdkPath is null)
        {
            var dialog = new SdkPathDialog();
            dialog.ShowDialog();
        }
        else
        {
            // Ensure SDK is loaded into the current process
            SapRfcSdkManager.EnsureSdkLoaded(sdkPath);
        }
    }

    private void TrackActiveDocument()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (GetService(typeof(DTE)) is not DTE dte) return;
        _dteEvents = dte.Events;
        _windowEvents = _dteEvents.WindowEvents;
        _windowEvents.WindowActivated += OnWindowActivated;
    }

    private void OnWindowActivated(Window gotFocus, Window lostFocus)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var isAbapFile = false;
        try
        {
            var doc = gotFocus?.Document;
            if (doc != null)
            {
                var ext = Path.GetExtension(doc.FullName);
                isAbapFile = string.Equals(ext, ".abap", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Window may not have a document (e.g. tool windows)
        }

        SetAbapUIContext(isAbapFile);
    }

    private void SetAbapUIContext(bool active)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (GetService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection monitorSelection)
        {
            var contextGuid = new Guid(AbapUIContextGuid);
            monitorSelection.GetCmdUIContextCookie(ref contextGuid, out var cookie);
            monitorSelection.SetCmdUIContext(cookie, active ? 1 : 0);
        }
    }
}
