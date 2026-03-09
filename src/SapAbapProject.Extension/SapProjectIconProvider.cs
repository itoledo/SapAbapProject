using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.ProjectSystem;

namespace SapAbapProject.Extension;

[Export(typeof(IProjectTreePropertiesProvider))]
[AppliesTo("SapAbapSourceProject")]
[Order(1000)]
internal sealed class SapProjectIconProvider : IProjectTreePropertiesProvider
{
    public void CalculatePropertyValues(
        IProjectTreeCustomizablePropertyContext propertyContext,
        IProjectTreeCustomizablePropertyValues propertyValues)
    {
        if (propertyValues.Flags.Contains(ProjectTreeFlags.Common.ProjectRoot))
        {
            propertyValues.Icon = KnownMonikers.Database.ToProjectSystemType();
            propertyValues.ExpandedIcon = KnownMonikers.Database.ToProjectSystemType();
        }
    }
}
