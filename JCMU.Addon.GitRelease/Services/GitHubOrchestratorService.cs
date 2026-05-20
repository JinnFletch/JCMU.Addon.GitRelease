using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;
using System.Xml.Linq;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class GitHubOrchestratorService
{
    /// <summary>
    /// Checks GitHub to see if a release with this version tag already exists.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> VerifyNewTagAsync(ReleaseContext ctx, IStatelessRunner runner, IHostServices host)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;
        var request = CommandBuilder.Create("gh").WithArgument($"release view v{ctx.FinalVersion}").InDirectory(projectDir).Build();

        return await runner.RunBufferedAsync(request).BindAsync(res =>
            res.ExitCode == 0
                ? Maybe.None<ReleaseContext>($"Release 'v{ctx.FinalVersion}' already exists on GitHub.")
                : Maybe.Some(ctx))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a local publish ONLY if the user opted-in.
    /// </summary>
    public static async Task<Maybe<string?>> ExecuteOptionalBuildAsync(ReleaseContext ctx, IStatelessRunner runner, IHostServices host)
    {
        if (!ctx.AttachLocalBinary) return Maybe.SomeAllowNull<string?>(null);

        return await Maybe.TryAsync<string?>(async () =>
        {
            host.Logger.LogInfo($"\n[Build] Compiling standalone executable (v{ctx.FinalVersion})...");

            // Validate Single-File requirement for local uploads
            var xml = XDocument.Load(ctx.ProjectFilePath);
            var isSingle = xml.Descendants("PublishSingleFile").FirstOrDefault()?.Value?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;
            if (!isSingle) throw new Exception("Local attachment requires <PublishSingleFile>true</PublishSingleFile> in the .csproj.");

            var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;
            var buildReq = CommandBuilder.Create("dotnet")
                .WithArgument("publish").WithQuotedArgument(ctx.ProjectFilePath)
                .WithArgument("-c Release").WithArgument($"-p:Version={ctx.FinalVersion}")
                .InDirectory(projectDir).Build();

            await foreach (var line in runner.StreamAsync(buildReq).ConfigureAwait(false)) 
                host.Logger.LogInfo($"  [dotnet] {line}");

            var binaryPath = Path.Combine(projectDir, "bin", "Release", ctx.Tfm, "publish", $"{Path.GetFileNameWithoutExtension(ctx.ProjectFilePath)}.exe");
            if (!File.Exists(binaryPath)) throw new Exception("Could not locate the compiled .exe file.");

            host.Logger.LogInfo("[✓] Local build successful.");
            return binaryPath;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The final step: Creates the release and optionally attaches the binary.
    /// </summary>
    public static async Task<Maybe> CreateGitHubReleaseAsync(ReleaseContext ctx, string? binaryPath, IStatelessRunner runner, IHostServices host)
    {
        host.Logger.LogInfo($"\n[GitHub] Creating release v{ctx.FinalVersion}...");
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var requestBuilder = CommandBuilder.Create("gh")
            .WithArgument("release create").WithArgument($"v{ctx.FinalVersion}")
            .WithArgument("--title").WithQuotedArgument($"v{ctx.FinalVersion}")
            .WithArgument("--generate-notes");

        if (!string.IsNullOrEmpty(binaryPath)) requestBuilder.WithQuotedArgument(binaryPath);

        return await runner.RunBufferedAsync(requestBuilder.InDirectory(projectDir).Build())
            .EnsureSuccessAsync(res => $"GitHub release failed: {res.StandardError}")
            .TapAsync(res => host.Logger.LogInfo($"[✓] {res.StandardOutput.Trim()}"))
            .BindAsync(_ => Maybe.SUCCESS)
            .ConfigureAwait(false);
    }
}