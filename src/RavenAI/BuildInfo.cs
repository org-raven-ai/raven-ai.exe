using System.Linq;
using System.Reflection;

namespace RavenAI;

/// <summary>
/// Build identity — version, git commit, and build time — stamped into assembly metadata at
/// compile time (see the StampBuildInfo target in RavenAI.csproj) so the exact running build can be
/// identified in-app. Surfaced in the Settings pane and the startup log.
/// </summary>
public static class BuildInfo
{
    private static readonly Assembly Asm = typeof(BuildInfo).Assembly;

    /// <summary>Assembly version, e.g. "1.0.0".</summary>
    public static string Version { get; } = Asm.GetName().Version?.ToString(3) ?? "?";

    /// <summary>Short git commit the build came from (with "-dirty" if the tree had changes).</summary>
    public static string Commit { get; } = Metadata("GitCommit");

    /// <summary>UTC build timestamp.</summary>
    public static string BuildTime { get; } = Metadata("BuildTimeUtc");

    /// <summary>One-line summary, e.g. "v1.0.0 · c715bc3 · built 2026-07-07 00:12:34Z".</summary>
    public static string Summary { get; } = $"v{Version} · {Commit} · built {BuildTime}";

    private static string Metadata(string key) =>
        Asm.GetCustomAttributes<AssemblyMetadataAttribute>()
           .FirstOrDefault(a => a.Key == key)?.Value ?? "unknown";
}
