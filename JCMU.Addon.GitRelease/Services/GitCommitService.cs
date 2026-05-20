using JinnDev.JCMU.Addon.GitRelease.Models;
using JinnDev.JCMU.SDK.Interfaces;
using JinnDev.Utilities.CommandLine;
using JinnDev.Utilities.Monad;

namespace JinnDev.JCMU.Addon.GitRelease.Services;

public static class GitCommitService
{
    /// <summary>
    /// Adds and commits the project file if a version bump occurred.
    /// </summary>
    public static async Task<Maybe<ReleaseContext>> CommitVersionBumpAsync(
        ReleaseContext ctx,
        IStatelessRunner runner,
        IHostServices host)
    {
        if (!ctx.IsVersionBumped) return Maybe.Some(ctx);

        host.Logger.LogInfo($"[Git] Committing version bump to {ctx.FinalVersion}...");

        var projectDir = Path.GetDirectoryName(ctx.ProjectFilePath)!;
        var fileName = Path.GetFileName(ctx.ProjectFilePath);

        // 1. Stage the file
        var addRequest = CommandBuilder.Create("git")
            .WithArgument("add")
            .WithQuotedArgument(fileName)
            .InDirectory(projectDir)
            .Build();

        // 2. Commit the file
        var commitRequest = CommandBuilder.Create("git")
            .WithArgument("commit")
            .WithArgument("-m")
            .WithQuotedArgument($"chore: bump version to {ctx.FinalVersion}")
            .InDirectory(projectDir)
            .Build();

        return await runner.RunBufferedAsync(addRequest)
            .EnsureSuccessAsync("Failed to stage .csproj change.")
            .BindAsync(_ => runner.RunBufferedAsync(commitRequest))
            .EnsureSuccessAsync("Failed to commit version bump.")
            .MapAsync(_ => ctx)
            .ConfigureAwait(false);
    }
}