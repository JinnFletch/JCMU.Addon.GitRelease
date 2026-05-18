using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class GitHubCliService
{
    /// <summary>
    /// Non-halting check to see if the target release tag already exists on GitHub.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> CheckExistingReleaseAsync(ReleaseContext ctx, IStatelessRunner runner, IHostServices host)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var request = CommandBuilder.Create("gh")
            .WithArgument($"release view v{ctx.InitialVersion}")
            .InDirectory(projectDir)
            .Build();

        await runner.RunBufferedAsync(request)
            .TapAsync(cmd =>
            {
                // gh exit code 0 means the release WAS found
                if (cmd.ExitCode == 0)
                {
                    host.Logger.LogWarning($"\n[WARNING] GitHub Release 'v{ctx.InitialVersion}' already exists!");
                    host.Logger.LogWarning("If you proceed with this version, the release creation step will fail.");
                }
            })
            // Ignore gh failures (like networking or no tags yet)
            .ConfigureAwait(false);

        return Maybe.Some(ctx);
    }

    /// <summary>
    /// Executes the gh release create command, uploading the executable binary and generating notes.
    /// </summary>
    public static async Task<Maybe> CreateReleaseAsync(ReleaseContext ctx, string binaryPath, IStatelessRunner runner, IHostServices host)
    {
        host.Logger.LogInfo($"\n--- Uploading Release to GitHub (v{ctx.FinalVersion}) ---");
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var releaseRequest = CommandBuilder.Create("gh")
            .WithArgument("release create")
            .WithArgument($"v{ctx.FinalVersion}")
            // Attaches the actual file we found in DotnetPublishService
            .WithQuotedArgument(binaryPath)
            .WithArgument("--title")
            .WithQuotedArgument($"v{ctx.FinalVersion}")
            .WithArgument("--generate-notes")
            .InDirectory(projectDir)
            .Build();

        return await runner.RunBufferedAsync(releaseRequest)
            .EnsureSuccessAsync(cmd =>
            {
                if (cmd.StandardError.Contains("already exists", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Release creation failed: The tag 'v{ctx.FinalVersion}' already exists on GitHub.";
                }

                return $"Failed to create GitHub release. Check your 'gh' authentication.\n{cmd.StandardError}";
            })
            .BindAsync(cmd =>
            {
                host.Logger.LogInfo($"\n[SUCCESS] Release v{ctx.FinalVersion} created and published!");

                // Try to extract the clickable URL from the gh output for the user
                var urlLine = cmd.StandardOutput.Split('\n').FirstOrDefault(x => x.Contains("https://github.com"));
                if (!string.IsNullOrWhiteSpace(urlLine))
                {
                    host.Logger.LogInfo(urlLine.Trim());
                }

                return Maybe.SUCCESS;
            }).ConfigureAwait(false);
    }
}