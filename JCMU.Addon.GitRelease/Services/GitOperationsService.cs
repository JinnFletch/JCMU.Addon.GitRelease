using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;
using System.Text.RegularExpressions;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class GitOperationsService
{
    /// <summary>
    /// Executes git to find the remote URL and extracts the GitHub Owner and Repo name.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> DetermineGitHubRemoteAsync(ReleaseContext ctx, IStatelessRunner runner, IHostServices host)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        var request = CommandBuilder.Create("git")
            .WithArgument("remote get-url origin")
            .InDirectory(projectDir)
            .Build();

        return await runner.RunBufferedAsync(request)
            .EnsureSuccessAsync("Failed to read git remote. Ensure the project is a Git repository.")
            .BindAsync(cmdResult =>
            {
                var remoteUrl = cmdResult.StandardOutput.Trim();

                // Handles both HTTPS (https://github.com/owner/repo.git) and SSH (git@github.com:owner/repo.git)
                var match = Regex.Match(remoteUrl, @"github\.com[:/](.+?)/(.+?)(\.git)?$", RegexOptions.IgnoreCase);

                if (!match.Success)
                    return Maybe.None<ReleaseContext>($"Could not parse GitHub Owner/Repo from remote URL: {remoteUrl}");

                var owner = match.Groups[1].Value;
                var repo = match.Groups[2].Value;

                host.Logger.LogInfo($"[✓] Remote detected: {owner}/{repo}");

                return Maybe.Some(ctx with { Owner = owner, Repo = repo });
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates if the repository is clean and on the main branch. 
    /// If not, it traps the user in a prompt loop until they fix it or force an override.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> EnsureGitIntegrityAsync(ReleaseContext ctx, IStatelessRunner runner, IHostServices host)
    {
        // If the user kept the original version, we trust they know what they are doing and skip the strict git enforcement loop.
        if (ctx.InitialVersion.Equals(ctx.FinalVersion, StringComparison.OrdinalIgnoreCase))
            return Maybe.Some(ctx);

        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;

        Maybe<ReleaseContext> result = Maybe.Some(ctx);
        bool shouldLoop = true;

        while (shouldLoop)
        {
            var branchReq = CommandBuilder.Create("git").WithArgument("branch --show-current").InDirectory(projectDir).Build();
            var statusReq = CommandBuilder.Create("git").WithArgument("status --porcelain").InDirectory(projectDir).Build();

            // 1. Execute both git commands and map their outputs into a single tuple
            var gitCheckResult = await runner.RunBufferedAsync(branchReq)
                .EnsureSuccessAsync("Git branch check failed.")
                .BindAsync(b => runner.RunBufferedAsync(statusReq)
                    .EnsureSuccessAsync("Git status check failed.")
                    .MapAsync(s => (Branch: b.StandardOutput.Trim(), Status: s.StandardOutput.Trim())))
                .ConfigureAwait(false);

            // 2. Evaluate the tuple state
            var iteration = await gitCheckResult.MatchAsync(
                someAsync: async state =>
                {
                    bool isMainOrMaster = state.Branch.Equals("main", StringComparison.OrdinalIgnoreCase) ||
                                          state.Branch.Equals("master", StringComparison.OrdinalIgnoreCase);
                    bool isClean = string.IsNullOrWhiteSpace(state.Status);

                    if (isMainOrMaster && isClean)
                    {
                        host.Logger.LogInfo("[✓] Repository is clean and on the main branch.");
                        shouldLoop = false;
                        return Maybe.SUCCESS;
                    }

                    if (!isMainOrMaster) host.Logger.LogWarning($"Not on main/master branch (Current: '{state.Branch}').");
                    if (!isClean) host.Logger.LogWarning("Repository has uncommitted changes.");

                    var inputResult = await host.PromptUserAsync("Fix and [Enter] to re-check, or [C] to proceed anyway:").ConfigureAwait(false);

                    inputResult.Match(
                        some: input =>
                        {
                            if (input.Equals("C", StringComparison.OrdinalIgnoreCase))
                            {
                                host.Logger.LogWarning("Overriding strict git requirements. Proceeding...");
                                shouldLoop = false;
                            }
                            return Maybe.SUCCESS;
                        },
                        none: err => Maybe.SUCCESS // They just hit Enter or cancelled, meaning loop again and re-evaluate
                    );

                    return Maybe.SUCCESS;
                },
                noneAsync: async err =>
                {
                    result = Maybe.None<ReleaseContext>(err.Message);
                    shouldLoop = false; // Break loop on critical failure
                    return Maybe.SUCCESS;
                }
            ).ConfigureAwait(false);

            if (!iteration.HasValue) return Maybe.None<ReleaseContext>(iteration.Message);
        }

        return result;
    }
}