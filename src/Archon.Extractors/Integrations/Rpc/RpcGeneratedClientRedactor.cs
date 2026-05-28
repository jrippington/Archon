using Archon.Extractors.Integrations.HttpRest;

namespace Archon.Extractors.Integrations.Rpc
{
    /// <summary>
    /// Redacts secret-bearing RPC and generated-client source fragments before they can enter metadata, diagnostics, or evidence previews.
    /// </summary>
    internal static class RpcGeneratedClientRedactor
    {
        /// <summary>
        /// Redacts secret-like values while preserving safe structural generated-client evidence.
        /// </summary>
        /// <param name="value">The source, artifact, diagnostic, metadata, or target value to redact.</param>
        /// <returns>The redacted value, or <see langword="null" /> when the input is absent.</returns>
        public static string? Redact(string? value)
        {
            // RPC generated artifacts can carry the same token and connection fragments as HTTP client code, so reuse the shared HTTP/REST redaction rules.
            return HttpRestRedactor.Redact(value);
        }

        /// <summary>
        /// Redacts a potential service target name without removing normal endpoint identities.
        /// </summary>
        /// <param name="targetName">The candidate service target name.</param>
        /// <returns>The redacted target name, or <see langword="null" /> when no target was supplied.</returns>
        public static string? RedactTargetName(string? targetName)
        {
            // Targets may be literal endpoint addresses, endpoint names, or generated client names; only obvious secret-bearing tokens are replaced.
            return Redact(targetName);
        }
    }
}
