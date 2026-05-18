using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;
using System.Xml.Linq;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class ProjectDiscoveryService
{
    private static readonly string[] IgnoreFolders = { ".git", ".vs", "Publish", "bin", "obj", ".github" };

    public static async Task<Maybe<string>> LocateExecutableProjectAsync(string targetDirectory, IHostServices host)
    {
        return await Maybe.TryAsync<string>(async () =>
        {
            var candidates = new List<string>();
            var directories = new DirectoryInfo(targetDirectory).GetDirectories();
            var allDirs = new List<DirectoryInfo> { new DirectoryInfo(targetDirectory) };
            allDirs.AddRange(directories.Where(d => !IgnoreFolders.Any(f => d.Name.Equals(f, StringComparison.OrdinalIgnoreCase))));

            foreach (var dir in allDirs)
            {
                var csprojs = dir.GetFiles("*.csproj");
                foreach (var proj in csprojs)
                {
                    var xml = XDocument.Load(proj.FullName);
                    var outputType = xml.Descendants("OutputType").FirstOrDefault()?.Value;

                    if (outputType != null && (outputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) ||
                                               outputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase)))
                    {
                        candidates.Add(proj.FullName);
                    }
                }
            }

            if (candidates.Count == 0)
                throw new Exception("No executable projects (<OutputType>Exe</OutputType>) found in the target directory.");

            if (candidates.Count == 1)
                return candidates[0];

            return await PromptForProjectSelectionAsync(candidates, host).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public static Maybe<ReleaseContext> ValidateAndExtractMetadata(ReleaseContext ctx)
    {
        return Maybe.Try<ReleaseContext>(() =>
        {
            var xml = XDocument.Load(ctx.ProjectFilePath);

            // 1. Enforce PublishSingleFile
            var singleFile = xml.Descendants("PublishSingleFile").FirstOrDefault()?.Value;
            if (string.IsNullOrWhiteSpace(singleFile) || !singleFile.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"The project '{Path.GetFileName(ctx.ProjectFilePath)}' is missing <PublishSingleFile>true</PublishSingleFile>.\n" +
                                    "GitHub releases require a standalone executable to prevent crashes on the user's machine.");
            }

            // 2. Extract Target Framework (TFM)
            var tfm = xml.Descendants("TargetFramework").FirstOrDefault()?.Value
                      ?? xml.Descendants("TargetFrameworks").FirstOrDefault()?.Value?.Split(';').FirstOrDefault();

            if (string.IsNullOrWhiteSpace(tfm))
                throw new Exception("Could not determine the <TargetFramework> from the project file.");

            // 3. Extract Version
            var version = xml.Descendants("Version").FirstOrDefault()?.Value
                          ?? xml.Descendants("PackageVersion").FirstOrDefault()?.Value
                          ?? xml.Descendants("AssemblyVersion").FirstOrDefault()?.Value
                          ?? xml.Descendants("FileVersion").FirstOrDefault()?.Value;

            if (string.IsNullOrWhiteSpace(version))
                throw new Exception("Could not extract a version tag (<Version>, <AssemblyVersion>, etc.) from the project file.");

            return ctx with
            {
                Tfm = tfm,
                InitialVersion = version,
                FinalVersion = version // Default final to initial until user overrides
            };
        });
    }

    private static async Task<string> PromptForProjectSelectionAsync(List<string> candidates, IHostServices host)
    {
        host.Logger.LogInfo("\nMultiple executable projects detected:");
        for (int i = 0; i < candidates.Count; i++)
        {
            host.Logger.LogInfo($"{i + 1}. {Path.GetFileName(candidates[i])}");
        }

        var result = await host.PromptUserAsync($"\nSelect project (1-{candidates.Count}):").ConfigureAwait(false);

        return result.Match(
            some: input =>
            {
                if (int.TryParse(input, out int choice) && choice > 0 && choice <= candidates.Count)
                    return candidates[choice - 1];

                throw new Exception("Invalid project selection.");
            },
            none: err => throw new Exception("Selection cancelled.")
        );
    }
}
