using System.Security.Cryptography;
using System.Text;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Analysis;

/// <summary>
/// Deterministic identity of the actual analyzed WORKING STATE of a repository
/// (Milestone 015.3C) — distinct from <see cref="ContextFingerprint"/>, which
/// identifies the effective context sent to a provider. The snapshot fingerprint
/// describes the files actually eligible/selected for analysis, not machine state.
///
/// It is derived from the SAME repository file selection the scanner already
/// produced (the post-ignore-rule set — bin/obj/.git/node_modules/packages/etc.
/// are excluded by <see cref="ScanOptions.IgnoredDirectories"/>; there is no
/// second independent definition of "repository files" for snapshotting). For
/// every selected file it hashes the raw content bytes, keyed by normalized
/// relative path, in deterministic (Ordinal) order, then hashes the aggregate.
///
/// - Relative paths only (never absolute), so identical source trees on different
///   machines produce the same identity.
/// - Content is hashed, never stored; no secrets, source code, or timestamps.
/// - Dirty tracked changes and relevant untracked files change the fingerprint,
///   so a commit SHA alone is never sufficient identity for a dirty working tree.
/// Uses SHA-256 (the platform's established hash), not a custom algorithm.
/// </summary>
public static class SnapshotFingerprint
{
    /// <summary>Marker for a selected file whose bytes could not be read at fingerprint time.</summary>
    private const string UnreadableMarker = "unreadable";

    /// <summary>
    /// Computes the snapshot fingerprint over the scanned selection. Reads each
    /// file's bytes from disk so the identity reflects the true content of the
    /// analyzed working state regardless of which files were content-loaded during
    /// the scan. Files are ordered by relative path (Ordinal) so filesystem
    /// enumeration order never matters.
    /// </summary>
    public static string Compute(string rootPath, IReadOnlyList<ScannedFile> files)
    {
        var sb = new StringBuilder();
        foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            sb.Append(file.RelativePath).Append('\0');
            sb.Append(ContentHash(rootPath, file.RelativePath)).Append('\0');
        }

        var aggregate = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return "SNAP-" + Convert.ToHexString(aggregate)[..12].ToLowerInvariant();
    }

    private static string ContentHash(string rootPath, string relativePath)
    {
        var absolute = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            var bytes = File.ReadAllBytes(absolute);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deterministic: an unreadable file contributes a stable marker (never
            // content), so identical filesystem states still yield identical hashes.
            return UnreadableMarker;
        }
    }
}