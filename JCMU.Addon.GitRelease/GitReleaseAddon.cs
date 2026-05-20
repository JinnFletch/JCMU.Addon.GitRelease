using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.Addon.GitRelease.Services;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.JCMU.SDK.Models;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.GitRelease;

public class GitReleaseAddon : IJcmuAddon
{
    public async Task<Maybe<int>> ExecuteAsync(ActionContext context)
    {
        var host = context.HostServices;
        var runner = new StatelessRunner();

        host.Logger.LogInfo("==================================================");
        host.Logger.LogInfo("    JCMU GitHub Orchestrator: Release Mode        ");
        host.Logger.LogInfo("==================================================\n");

        return await ProjectDiscoveryService.LocateExecutableProjectAsync(context.TargetDirectory, host)
            .MapAsync(path => new ReleaseContext { TargetDirectory = context.TargetDirectory, ProjectFilePath = path })
            .BindAsync(ProjectDiscoveryService.ExtractMetadata)
            .BindAsync(ctx => PromptUserOptionsAsync(ctx, host))
            .BindAsync(ProjectMutationService.UpdateProjectVersion)
            .BindAsync(ctx => GitCommitService.CommitVersionBumpAsync(ctx, runner, host))
            .BindAsync(ctx => GitSyncService.DetermineGitHubRemoteAsync(ctx, runner))
            .BindAsync(ctx => GitSyncService.EnsureRemoteSyncAsync(ctx, runner, host))
            .BindAsync(ctx => GitHubOrchestratorService.VerifyNewTagAsync(ctx, runner, host))
            .BindAsync(async ctx =>
            {
                // Nested bind allows us to keep 'ctx' in scope while capturing the optional binary path
                return await GitHubOrchestratorService.ExecuteOptionalBuildAsync(ctx, runner, host)
                    .BindAsync(binaryPath => GitHubOrchestratorService.CreateGitHubReleaseAsync(ctx, binaryPath, runner, host)).ConfigureAwait(false);
            })
            .MatchAsync(
            some: () =>
            {
                host.Logger.LogInfo("\n[FINISH] GitHub Release process complete.");
                return Maybe.Some<int>(5); // Success: Auto-close in 5s
            },
            none: err =>
            {
                host.Logger.LogError($"Release Failed: {err.Message}");
                return Maybe.None<int>(err.Message); // Failure: Display error
            })
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Handles the interactive configuration of the release.
    /// </summary>
    private static async Task<Maybe<ReleaseContext>> PromptUserOptionsAsync(ReleaseContext ctx, IHostServices host)
    {
        // 1. Version Override
        var verResult = await host.PromptUserAsync($"Target Version [{ctx.InitialVersion}]:").ConfigureAwait(false);
        var finalVersion = verResult.Match(
            some: v => string.IsNullOrWhiteSpace(v) ? ctx.InitialVersion : v.Trim(),
            none: _ => ctx.InitialVersion
        );

        // 2. Local Build Opt-In
        var buildResult = await host.PromptUserAsync("Build and upload local .exe? (y/N):").ConfigureAwait(false);
        var attachLocal = buildResult.Match(
            some: b => b.Equals("y", StringComparison.OrdinalIgnoreCase),
            none: _ => false
        );

        return Maybe.Some(ctx with
        {
            FinalVersion = finalVersion,
            AttachLocalBinary = attachLocal
        });
    }
}