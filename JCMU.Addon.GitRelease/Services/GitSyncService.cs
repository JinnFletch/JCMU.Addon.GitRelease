using System.Text.RegularExpressions;
using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class GitSyncService
{
    /// <summary>
    /// Extracts the GitHub Owner and Repo name from the git remote URL.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> DetermineGitHubRemoteAsync(ReleaseContext ctx, IStatelessRunner runner)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;
        var request = CommandBuilder.Create("git").WithArgument("remote get-url origin").InDirectory(projectDir).Build();

        return await runner.RunBufferedAsync(request)
            .EnsureSuccessAsync("Failed to read git remote.")
            .BindAsync(res =>
            {
                var match = Regex.Match(res.StandardOutput.Trim(), @"github\.com[:/](.+?)/(.+?)(\.git)?$", RegexOptions.IgnoreCase);
                return match.Success
                    ? Maybe.Some(ctx with { Owner = match.Groups[1].Value, Repo = match.Groups[2].Value })
                    : Maybe.None<ReleaseContext>("Could not parse GitHub Owner/Repo from remote URL.");
            }).ConfigureAwait(false);
    }

    /// <summary>
    /// Forces a monadic sync loop until Local HEAD == Origin HEAD.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> EnsureRemoteSyncAsync(ReleaseContext ctx, IStatelessRunner runner, IHostServices host)
    {
        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;
        host.Logger.LogInfo("\n[Git] Synchronizing with GitHub...");

        while (true)
        {
            // 1. Chain all the git data gathering monads together
            var iterationState = await RunGitAsync("status --porcelain", projectDir, runner)
                .BindAsync(status => string.IsNullOrWhiteSpace(status)
                    ? RunGitAsync("fetch", projectDir, runner)
                    : Task.FromResult(Maybe.None<string>("Repository has uncommitted changes. Please commit or stash them first.")))
                .BindAsync(_ => RunGitAsync("rev-parse HEAD", projectDir, runner))
                .BindAsync(local => RunGitAsync("rev-parse @{u}", projectDir, runner)
                    .MapAsync(remote => (Local: local, Remote: remote)))
                .BindAsync(pair => RunGitAsync("merge-base HEAD @{u}", projectDir, runner)
                    .MapAsync(baseHash => (pair.Local, pair.Remote, BaseHash: baseHash)))
                .ConfigureAwait(false);

            // 2. Safely unwrap and evaluate the state
            var evaluation = await iterationState.MatchAsync(
                someAsync: async hashes =>
                {
                    if (hashes.Local == hashes.Remote)
                    {
                        host.Logger.LogInfo("[✓] Local is in-sync with GitHub.");
                        return Maybe.Some(true); // Return True = Done Looping
                    }

                    if (hashes.Remote != hashes.BaseHash && hashes.Local == hashes.BaseHash)
                        return Maybe.None<bool>("Your local branch is behind origin. Please 'git pull' before releasing.");

                    host.Logger.LogWarning("You have commits that haven't been pushed to GitHub yet.");
                    var inputResult = await host.PromptUserAsync("Push changes to GitHub now? (y/n):").ConfigureAwait(false);

                    // 3. Match the user input
                    return await inputResult.MatchAsync(
                        someAsync: async val =>
                        {
                            if (val.Equals("y", StringComparison.OrdinalIgnoreCase))
                            {
                                host.Logger.LogInfo("[Git] Pushing to origin...");
                                return await RunGitAsync("push", projectDir, runner).MapAsync(_ => false).ConfigureAwait(false); // Return False = Loop Again
                            }
                            return Maybe.None<bool>("Sync cancelled. Code must be pushed to GitHub to trigger a release.");
                        },
                        noneAsync: async err => Maybe.None<bool>("Sync cancelled.")
                    ).ConfigureAwait(false);
                },
                noneAsync: async err => Maybe.None<bool>(err.Message)
            ).ConfigureAwait(false);

            // 4. Handle the evaluation result
            if (!evaluation.HasValue) return Maybe.None<ReleaseContext>(evaluation.Message);

            // We know it has a value at this point, but we still use Match for pure extraction
            bool isSynced = evaluation.Match(some: val => val, none: _ => false);
            if (isSynced) return Maybe.Some(ctx);
        }
    }

    /// <summary>
    /// Monadic helper to execute git commands and extract the output.
    /// </summary>
    private static Task<Maybe<string>> RunGitAsync(string args, string dir, IStatelessRunner runner)
    {
        var request = CommandBuilder.Create("git").WithArgument(args).InDirectory(dir).Build();

        return runner.RunBufferedAsync(request)
            .EnsureSuccessAsync($"Git command failed: git {args}")
            .MapAsync(res => res.StandardOutput.Trim());
    }
}