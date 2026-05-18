namespace JinnDev.JCMU.Addon.GitRelease.Models;

public record ReleaseContext
{
    public required string TargetDirectory { get; init; }
    public string ProjectFilePath { get; init; } = string.Empty;
    public string Tfm { get; init; } = string.Empty;
    public string InitialVersion { get; init; } = string.Empty;
    public string FinalVersion { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string Repo { get; init; } = string.Empty;
}