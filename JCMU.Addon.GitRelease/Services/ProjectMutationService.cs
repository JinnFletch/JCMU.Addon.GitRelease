using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.Utilities.Monad;
using System.Xml.Linq;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class ProjectMutationService
{
    /// <summary>
    /// Physically overwrites the version tags in the .csproj file.
    /// </summary>
    public static Maybe<ReleaseContext> UpdateProjectVersion(ReleaseContext ctx)
    {
        if (!ctx.IsVersionBumped) return Maybe.Some(ctx);

        return Maybe.Try<ReleaseContext>(() =>
        {
            var xml = XDocument.Load(ctx.ProjectFilePath);

            // We update all common versioning tags found in .NET projects
            var versionTags = new[] { "Version", "PackageVersion", "AssemblyVersion", "FileVersion" };
            bool updated = false;

            foreach (var tagName in versionTags)
            {
                var element = xml.Descendants(tagName).FirstOrDefault();
                if (element != null)
                {
                    element.Value = ctx.FinalVersion;
                    updated = true;
                }
            }

            // If no version tag existed at all, we create the <Version> tag in the first PropertyGroup
            if (!updated)
            {
                var propertyGroup = xml.Descendants("PropertyGroup").FirstOrDefault();
                if (propertyGroup != null)
                {
                    propertyGroup.Add(new XElement("Version", ctx.FinalVersion));
                }
            }

            xml.Save(ctx.ProjectFilePath);
            return ctx;
        });
    }
}