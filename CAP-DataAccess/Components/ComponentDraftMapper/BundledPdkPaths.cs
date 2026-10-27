using System;
using System.Collections.Generic;
using System.IO;

namespace CAP_DataAccess.Components.ComponentDraftMapper;

/// <summary>
/// Locates the read-only bundled (shipped foundry) PDK directory and answers whether a
/// file path points into it. Bundled PDK JSONs are NEVER written at runtime (repo
/// invariant) — edits are forked into the managed user-pdks root instead. This class is
/// the single source of truth for that boundary: the component library resolves its
/// bundled load directory here, and <see cref="PdkJsonSaver"/> refuses write targets
/// that fall inside it (defense in depth against save paths that lost track of a
/// bundled origin, round-5 review finding [1]).
/// </summary>
public static class BundledPdkPaths
{
    /// <summary>
    /// How many ancestor directories to probe for a repo-source checkout
    /// (<c>CAP-DataAccess/PDKs</c>) when running from a build output folder.
    /// </summary>
    private const int RepoSearchAncestorLevels = 6;

    /// <summary>
    /// Resolves the directory bundled PDKs are loaded from: a repo-source
    /// <c>CAP-DataAccess/PDKs</c> checkout above <paramref name="baseDir"/> when present
    /// (developer runs), otherwise the <c>PDKs</c> folder shipped next to the executable.
    /// Returns null when neither exists.
    /// </summary>
    public static string? ResolveBundledPdkDirectory(string baseDir)
    {
        var bundled = Path.Combine(baseDir, "PDKs");

        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < RepoSearchAncestorLevels && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "CAP-DataAccess", "PDKs");
            if (Directory.Exists(candidate) &&
                Directory.GetFiles(candidate, "*.json").Length > 0)
                return candidate;
        }

        return Directory.Exists(bundled) ? bundled : null;
    }

    /// <summary>
    /// True when <paramref name="filePath"/> lies directly inside a bundled-PDK directory
    /// candidate (the shipped <c>PDKs</c> folder next to the executable or a repo-source
    /// <c>CAP-DataAccess/PDKs</c> checkout above it). Used to detect bundled origins
    /// independently of the library's registration state and to refuse writes.
    /// </summary>
    /// <param name="filePath">The PDK JSON file path to classify.</param>
    /// <param name="baseDir">
    /// Application base directory override for tests; defaults to
    /// <see cref="AppContext.BaseDirectory"/>.
    /// </param>
    public static bool IsBundledPdkFile(string filePath, string? baseDir = null)
    {
        baseDir ??= AppContext.BaseDirectory;
        string? targetDir;
        try
        {
            targetDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        }
        catch (Exception)
        {
            // Malformed paths cannot point into the bundled directory.
            return false;
        }
        if (string.IsNullOrEmpty(targetDir))
            return false;

        var candidates = new List<string>(EnumerateBundledDirectoryCandidates(baseDir));
        foreach (var candidate in candidates)
        {
            if (PathsEqual(targetDir, candidate))
                return true;
        }

        // A save through an alias of the directory writes the same physical file.
        // Resolve both sides: the installation's own PDK directory may itself be a link.
        // Keep the lexical check above so absent bundled destinations stay protected.
        var physicalTarget = ResolveDirectoryAliases(targetDir);
        foreach (var candidate in candidates)
        {
            if (PathsEqual(physicalTarget, ResolveDirectoryAliases(candidate)))
                return true;
        }
        return false;
    }

    private static string ResolveDirectoryAliases(string path)
    {
        int remainingLinks = 64;
        return Resolve(path, ref remainingLinks);

        static string Resolve(string value, ref int remainingLinks)
        {
            var full = Path.GetFullPath(value);
            var root = Path.GetPathRoot(full)!;
            var resolved = root;
            var parts = full[root.Length..].Split(
                new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                resolved = Path.Combine(resolved, part);
                var directory = new DirectoryInfo(resolved);
                if (directory.LinkTarget == null) continue;
                if (--remainingLinks < 0)
                    throw new IOException("Too many directory links while checking bundled PDK protection.");
                var target = directory.ResolveLinkTarget(returnFinalTarget: false)
                             ?? throw new IOException($"Cannot resolve PDK directory alias '{resolved}'.");
                // A target may have a linked ancestor as well as being a link itself.
                resolved = Resolve(target.FullName, ref remainingLinks);
            }
            return resolved;
        }
    }

    /// <summary>
    /// All directories that may hold bundled PDK JSONs for <paramref name="baseDir"/>:
    /// the shipped <c>PDKs</c> folder (whether or not it exists yet) plus every existing
    /// repo-source <c>CAP-DataAccess/PDKs</c> folder up the ancestor chain. The guard is
    /// deliberately broader than <see cref="ResolveBundledPdkDirectory"/> — writing into
    /// ANY of these locations would corrupt foundry truth.
    /// </summary>
    private static IEnumerable<string> EnumerateBundledDirectoryCandidates(string baseDir)
    {
        yield return Path.Combine(baseDir, "PDKs");

        var dir = new DirectoryInfo(baseDir);
        for (int i = 0; i < RepoSearchAncestorLevels && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "CAP-DataAccess", "PDKs");
            if (Directory.Exists(candidate))
                yield return candidate;
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        static string Normalize(string path) => Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }
}
