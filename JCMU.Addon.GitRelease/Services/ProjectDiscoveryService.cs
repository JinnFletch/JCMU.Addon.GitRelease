using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.Monad;
using System.Xml.Linq;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class ProjectDiscoveryService
{
    private static readonly string[] IgnoreFolders = { ".git", ".vs", "Publish", "bin", "obj", ".github" };

    public static async Task<Maybe<string>> LocateExecutableProjectAsync(string targetDir, IHostServices host)
    {
        return await Maybe.TryAsync(async () =>
        {
            var candidates = Directory.GetFiles(targetDir, "*.csproj", SearchOption.AllDirectories)
                .Where(f => !f.Contains("bin") && !f.Contains("obj"))
                .Where(f => {
                    var xml = XDocument.Load(f);
                    var type = xml.Descendants("OutputType").FirstOrDefault()?.Value;
                    return type != null && type.Contains("Exe", StringComparison.OrdinalIgnoreCase);
                }).ToList();

            if (candidates.Count == 0) throw new Exception("No executable projects (<OutputType>Exe</OutputType>) found.");
            if (candidates.Count == 1) return candidates[0];

            host.Logger.LogInfo("\nMultiple projects found:");
            for (int i = 0; i < candidates.Count; i++) host.Logger.LogInfo($"{i + 1}. {Path.GetFileName(candidates[i])}");

            var result = await host.PromptUserAsync($"Select project (1-{candidates.Count}):").ConfigureAwait(false);
            return result.Bind(input => int.TryParse(input, out int choice) && choice > 0 && choice <= candidates.Count
                ? Maybe.Some(candidates[choice - 1])
                : Maybe.None<string>("Invalid selection."));
        }).ConfigureAwait(false);
    }

    public static Maybe<ReleaseContext> ExtractMetadata(ReleaseContext ctx)
    {
        return Maybe.Try<ReleaseContext>(() =>
        {
            var xml = XDocument.Load(ctx.ProjectFilePath);
            var tfm = xml.Descendants("TargetFramework").FirstOrDefault()?.Value
                      ?? xml.Descendants("TargetFrameworks").FirstOrDefault()?.Value?.Split(';').FirstOrDefault();
            var ver = xml.Descendants("Version").FirstOrDefault()?.Value
                      ?? xml.Descendants("PackageVersion").FirstOrDefault()?.Value ?? "1.0.0";

            if (string.IsNullOrWhiteSpace(tfm)) throw new Exception("Could not determine TargetFramework.");

            return ctx with { Tfm = tfm, InitialVersion = ver, FinalVersion = ver };
        });
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
