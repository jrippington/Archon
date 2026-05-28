using Microsoft.Extensions.Options;

namespace Archon.Infrastructure.Neo4j.Configuration
{
    /// <summary>
    /// Validates <see cref="Neo4jOptions"/> before the Neo4j adapter creates driver resources or executes health checks.
    /// </summary>
    /// <remarks>
    /// Validation reports only setting names and safe structural problems. It never includes the configured password or other
    /// credential values because options validation failures are commonly surfaced through logs and startup exceptions.
    /// </remarks>
    public sealed class Neo4jOptionsValidator : IValidateOptions<Neo4jOptions>
    {
        /// <summary>
        /// Validates a named options instance and returns a credential-safe result.
        /// </summary>
        /// <param name="name">The options instance name supplied by the Microsoft options infrastructure.</param>
        /// <param name="options">The options instance whose connection settings should be checked.</param>
        /// <returns>A validation result containing success or a list of credential-safe configuration failures.</returns>
        public ValidateOptionsResult Validate(string? name, Neo4jOptions options)
        {
            // Options validation runs during startup and before the first dependency probe, so it must collect every known safe
            // problem in one pass instead of forcing developers through repeated one-setting-at-a-time failures.
            List<string> failures = new();

            ValidateUri(options.Uri, failures);
            ValidateRequiredText(options.Database, nameof(Neo4jOptions.Database), failures);
            ValidateRequiredText(options.Username, nameof(Neo4jOptions.Username), failures);
            ValidateRequiredText(options.Password, nameof(Neo4jOptions.Password), failures);
            ValidatePositiveDuration(options.ConnectionTimeout, nameof(Neo4jOptions.ConnectionTimeout), failures);
            ValidatePositiveDuration(options.MaxTransactionRetryTime, nameof(Neo4jOptions.MaxTransactionRetryTime), failures);
            ValidatePositiveInteger(options.PersistenceBatchSize, nameof(Neo4jOptions.PersistenceBatchSize), failures);
            ValidateEncryptionMode(options.EncryptionMode, failures);

            if (failures.Count == 0)
            {
                // A successful result lets the options pipeline continue without allocating an unnecessary joined string.
                return ValidateOptionsResult.Success;
            }

            return ValidateOptionsResult.Fail(failures);
        }

        /// <summary>
        /// Validates that the configured URI is absolute and uses a Bolt-compatible Neo4j scheme.
        /// </summary>
        /// <param name="uri">The configured URI value supplied through configuration.</param>
        /// <param name="failures">The mutable failure list that receives credential-safe validation messages.</param>
        private static void ValidateUri(string? uri, ICollection<string> failures)
        {
            // The driver accepts several URI schemes, but Archon explicitly validates the safe operational subset that can be
            // documented for local Aspire, Testcontainers, and later production deployments.
            if (string.IsNullOrWhiteSpace(uri))
            {
                failures.Add($"{nameof(Neo4jOptions.Uri)} is required.");
                return;
            }

            if (!System.Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri))
            {
                failures.Add($"{nameof(Neo4jOptions.Uri)} must be an absolute Neo4j URI.");
                return;
            }

            string scheme = parsedUri.Scheme.ToLowerInvariant();
            if (scheme is not ("bolt" or "neo4j" or "bolt+s" or "neo4j+s" or "bolt+ssc" or "neo4j+ssc"))
            {
                failures.Add($"{nameof(Neo4jOptions.Uri)} must use a Neo4j Bolt-compatible URI scheme.");
            }
        }

        /// <summary>
        /// Validates that a required text setting contains meaningful non-whitespace content.
        /// </summary>
        /// <param name="value">The configured value to check.</param>
        /// <param name="settingName">The safe setting name to include in validation output.</param>
        /// <param name="failures">The mutable failure list that receives credential-safe validation messages.</param>
        private static void ValidateRequiredText(string? value, string settingName, ICollection<string> failures)
        {
            // Only the setting name is reported. The value itself may be a secret or may reveal local infrastructure details.
            if (string.IsNullOrWhiteSpace(value))
            {
                failures.Add($"{settingName} is required.");
            }
        }

        /// <summary>
        /// Validates that a duration setting is greater than zero.
        /// </summary>
        /// <param name="value">The configured duration value to check.</param>
        /// <param name="settingName">The safe setting name to include in validation output.</param>
        /// <param name="failures">The mutable failure list that receives credential-safe validation messages.</param>
        private static void ValidatePositiveDuration(TimeSpan value, string settingName, ICollection<string> failures)
        {
            // Durations are safe to classify by setting name, but the precise value is not needed for developers to fix the
            // configuration and can make logs noisy.
            if (value <= TimeSpan.Zero)
            {
                failures.Add($"{settingName} must be greater than zero.");
            }
        }

        /// <summary>
        /// Validates that an integer setting is greater than zero.
        /// </summary>
        /// <param name="value">The configured integer value to check.</param>
        /// <param name="settingName">The safe setting name to include in validation output.</param>
        /// <param name="failures">The mutable failure list that receives credential-safe validation messages.</param>
        private static void ValidatePositiveInteger(int value, string settingName, ICollection<string> failures)
        {
            // Batch sizes must be positive because zero or negative values cannot form bounded list-parameter windows. Reporting only
            // the setting name follows the validator's safe-message pattern and keeps operational logs compact.
            if (value <= 0)
            {
                failures.Add($"{settingName} must be greater than zero.");
            }
        }

        /// <summary>
        /// Validates that the configured encryption mode is one of the defined Archon-supported values.
        /// </summary>
        /// <param name="value">The configured encryption mode value to check.</param>
        /// <param name="failures">The mutable failure list that receives credential-safe validation messages.</param>
        private static void ValidateEncryptionMode(Neo4jEncryptionMode value, ICollection<string> failures)
        {
            // Enum binding can produce undefined numeric values, so validation guards the driver factory from unexpected modes.
            if (!Enum.IsDefined(value))
            {
                failures.Add($"{nameof(Neo4jOptions.EncryptionMode)} must be a supported encryption mode.");
            }
        }
    }
}
