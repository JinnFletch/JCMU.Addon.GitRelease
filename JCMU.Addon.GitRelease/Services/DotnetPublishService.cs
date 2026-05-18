using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class DotnetPublishService
{
    /// <summary>
    /// Executes dotnet publish in Release mode, overriding the version in-memory, 
    /// and streaming the output back to the Core's UI.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> ExecuteDotnetPublishAsync(ReleaseContext ctx, IStatelessRunner runner, IHostServices host)
    {
        host.Logger.LogInfo($"\n--- Compiling Single-File Executable (v{ctx.FinalVersion}) ---");

        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var request = CommandBuilder.Create("dotnet")
            .WithArgument("publish")
            .WithQuotedArgument(ctx.ProjectFilePath)
            .WithArgument("-c Release")
            // Overrides the version dynamically without mutating the csproj file
            .WithArgument($"-p:Version={ctx.FinalVersion}")
            .InDirectory(projectDir)
            .Build();

        return await Maybe.TryAsync<ReleaseContext>(async () =>
        {
            await foreach (var line in runner.StreamAsync(request).ConfigureAwait(false))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    // Print raw build output slightly indented for readability
                    host.Logger.LogInfo($"  [build] {line}");
                }
            }

            host.Logger.LogInfo("[✓] Publish completed.");
            return ctx;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Locates the generated .exe file in the TFM-specific publish directory.
    /// </summary>
    public static Maybe<string> LocatePublishedBinary(ReleaseContext ctx, IHostServices host)
    {
        return Maybe.Try<string>(() =>
        {
            var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

            // Standard .NET publish output path: bin/Release/{TFM}/publish
            var publishDir = Path.Combine(projectDir, "bin", "Release", ctx.Tfm, "publish");

            if (!Directory.Exists(publishDir))
                throw new Exception($"Publish directory not found: {publishDir}");

            // Look specifically for an executable matching the project name
            var projectName = Path.GetFileNameWithoutExtension(ctx.ProjectFilePath);
            var expectedExeName = $"{projectName}.exe";
            var exactPath = Path.Combine(publishDir, expectedExeName);

            if (File.Exists(exactPath))
            {
                host.Logger.LogInfo($"[✓] Found executable: {expectedExeName}");
                return exactPath;
            }

            // Fallback: Just grab the first .exe in the publish directory
            var firstExe = Directory.GetFiles(publishDir, "*.exe").FirstOrDefault();

            if (firstExe == null)
                throw new Exception($"Could not locate any .exe files in {publishDir}. Did the publish step fail silently?");

            host.Logger.LogInfo($"[✓] Found fallback executable: {Path.GetFileName(firstExe)}");
            return firstExe;
        });
    }
}
