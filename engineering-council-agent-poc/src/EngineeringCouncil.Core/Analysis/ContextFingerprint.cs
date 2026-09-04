using System.Security.Cryptography;
using System.Text;
using EngineeringCouncil.Core.Domain;

namespace EngineeringCouncil.Core.Analysis;

/// <summary>
/// Deterministic identity of the effective context sent to an LLM-style provider
/// for one acquisition step. Two providers are comparable only when they received
/// the same effective context, so this fingerprint is stamped onto every execution
/// record (Milestone 012.2). It is derived from the SELECTED files and their
/// EFFECTIVE content after <see cref="ContextContentPolicy"/> (per-file
/// truncation), ordered by normalized relative path — it deliberately excludes
/// absolute paths, provider name, model name, API keys, timestamps, and RunId, so
/// two providers that received identical effective context produce the same value.
/// Uses SHA-256 (the platform's established hash), not a custom algorithm.
/// </summary>
public static class ContextFingerprint
{
    /// <summary>
    /// Computes the fingerprint of a context selection. The per-file effective
    /// content is what <see cref="RepositoryContextBuilder"/> actually renders
    /// (each file truncated at <see cref="ContextContentPolicy.MaxCharactersPerFile"/>),
    /// so the fingerprint changes exactly when the effective LLM context changes.
    /// </summary>
    public static string Compute(AnalysisContextSelection selection, ContextContentPolicy? policy = null)
    {
        policy ??= new ContextContentPolicy();

        var sb = new StringBuilder();
        foreach (var file in selection.Files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            var content = string.IsNullOrEmpty(file.Content) ? string.Empty : file.Content!;
            var effective = content.Length <= policy.MaxCharactersPerFile
                ? content
                : content[..policy.MaxCharactersPerFile];
            sb.Append(file.RelativePath).Append('\0');
            sb.Append(effective).Append('\0');
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        return "CTX-" + hash[..12].ToLowerInvariant();
    }
}
