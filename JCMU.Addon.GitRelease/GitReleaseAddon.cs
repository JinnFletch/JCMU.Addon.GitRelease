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

        // IStatelessRunner is instantiated here so it can be passed down the pipeline
        var runner = new StatelessRunner();

        host.Logger.LogInfo("==================================================");
        host.Logger.LogInfo("    GitHub Release Creator (Single-File Exe)      ");
        host.Logger.LogInfo("==================================================\n");

        // The Railway-Oriented Pipeline
        var pipelineResult = await ProjectDiscoveryService.LocateExecutableProjectAsync(context.TargetDirectory, host)
            .MapAsync(projectPath => new ReleaseContext
            {
                TargetDirectory = context.TargetDirectory,
                ProjectFilePath = projectPath
            })
            .BindAsync(ProjectDiscoveryService.ValidateAndExtractMetadata)
            .BindAsync(ctx => GitOperationsService.DetermineGitHubRemoteAsync(ctx, runner, host))
            .BindAsync(ctx => GitHubCliService.CheckExistingReleaseAsync(ctx, runner, host))
            .BindAsync(ctx => PromptForVersionOverrideAsync(ctx, host))
            .BindAsync(ctx => GitOperationsService.EnsureGitIntegrityAsync(ctx, runner, host))
            .BindAsync(ctx => DotnetPublishService.ExecuteDotnetPublishAsync(ctx, runner, host))
            .BindAsync(ctx => DotnetPublishService.LocatePublishedBinary(ctx, host)
                // We use a nested BindAsync here so we can pass BOTH the context and the binaryPath into the final step
                .BindAsync(binaryPath => GitHubCliService.CreateReleaseAsync(ctx, binaryPath, runner, host)))
            .ConfigureAwait(false);

        // Terminal UI Behavior Mapping
        return pipelineResult.Match(
            some: () => Maybe.Some(5), // Success: Countdown 5 seconds and auto-close
            none: err => Maybe.PropagateFailure<int, Maybe>(pipelineResult) // Failure: Will trigger Core's 10-second error display
        );
    }

    /// <summary>
    /// Interactively asks the user if they want to override the version extracted from the project file.
    /// </summary>
    private static async Task<Maybe<ReleaseContext>> PromptForVersionOverrideAsync(ReleaseContext ctx, IHostServices host)
    {
        var inputResult = await host.PromptUserAsync($"\nCurrent version is {ctx.InitialVersion}. Enter new version or press Enter to keep:").ConfigureAwait(false);

        return inputResult.Match(
            some: input =>
            {
                // If they typed a value, use it as the FinalVersion
                var finalVersion = string.IsNullOrWhiteSpace(input) ? ctx.InitialVersion : input.Trim();
                return Maybe.Some(ctx with { FinalVersion = finalVersion });
            },
            none: err =>
            {
                // Core's PromptUserAsync returns None if the user just presses Enter with empty input.
                // In our case, that just means "keep the default".
                return Maybe.Some(ctx);
            }
        );
    }
}