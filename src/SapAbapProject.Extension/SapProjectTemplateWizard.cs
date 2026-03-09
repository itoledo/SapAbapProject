using System;
using System.Collections.Generic;
using System.IO;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TemplateWizard;

namespace SapAbapProject.Extension;

/// <summary>
/// Template wizard that renames the generated .csproj to .sapproj after project creation.
/// </summary>
public sealed class SapProjectTemplateWizard : IWizard
{
    private string? _csprojPath;
    private DTE? _dte;

    public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary,
        WizardRunKind runKind, object[] customParams)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _dte = automationObject as DTE;
    }

    public void ProjectFinishedGenerating(Project project)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _csprojPath = project.FullName;
        _dte ??= project.DTE;
    }

    public void RunFinished()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_csprojPath == null || _dte == null) return;

        var sapprojPath = Path.ChangeExtension(_csprojPath, ".sapproj");
        var csprojName = Path.GetFileName(_csprojPath);
        var sapprojName = Path.GetFileName(sapprojPath);

        // Get solution file path via DTE
        var slnPath = _dte.Solution.FullName;
        if (string.IsNullOrEmpty(slnPath)) return;

        // Force-save then close via IVsSolution2 (no UI prompts)
        var vsSolution = ServiceProvider.GlobalProvider.GetService(typeof(SVsSolution)) as IVsSolution2;
        if (vsSolution == null) return;
        vsSolution.SaveSolutionElement(
            (uint)__VSSLNSAVEOPTIONS.SLNSAVEOPT_ForceSave, null, 0);
        vsSolution.CloseSolutionElement(
            (uint)__VSSLNSAVEOPTIONS.SLNSAVEOPT_NoSave, null, 0);

        // Rename the project file on disk
        if (File.Exists(_csprojPath) && !File.Exists(sapprojPath))
        {
            File.Move(_csprojPath, sapprojPath);
        }

        // Update the solution file (.slnx or .sln) to reference .sapproj
        if (File.Exists(slnPath))
        {
            var content = File.ReadAllText(slnPath);
            content = content.Replace(csprojName, sapprojName);

            // For .slnx format, add Type GUID so VS knows which project factory to use
            if (slnPath.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                content = content.Replace(
                    ".sapproj\"",
                    ".sapproj\" Type=\"{9A19103F-16F7-4668-BE54-9A1E7A4F7556}\"");
            }

            File.WriteAllText(slnPath, content);
        }

        // Reopen the solution
        vsSolution.OpenSolutionFile(0, slnPath);
    }

    public void ProjectItemFinishedGenerating(ProjectItem projectItem) { }
    public bool ShouldAddProjectItem(string filePath) => true;
    public void BeforeOpeningFile(ProjectItem projectItem) { }
}
