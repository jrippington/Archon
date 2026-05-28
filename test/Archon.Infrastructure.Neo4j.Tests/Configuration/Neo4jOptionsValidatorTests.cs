using Archon.Infrastructure.Neo4j.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Archon.Infrastructure.Neo4j.Tests.Configuration
{
    /// <summary>
    /// Verifies credential-safe validation behavior for Neo4j connection options.
    /// </summary>
    public sealed class Neo4jOptionsValidatorTests
    {
        /// <summary>
        /// Confirms a complete local-development configuration passes validation.
        /// </summary>
        [Fact]
        public void ValidateAcceptsCompleteConfiguration()
        {
            // The scenario mirrors the local Aspire and Testcontainers shape: Bolt URI, default database, basic auth, and explicit
            // unencrypted transport for local containers.
            Neo4jOptions options = CreateValidOptions();
            Neo4jOptionsValidator validator = new();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

            Assert.True(result.Succeeded);
        }

        /// <summary>
        /// Confirms the default persistence batch size is accepted when configuration omits an explicit tuning value.
        /// </summary>
        [Fact]
        public void ValidateAcceptsDefaultPersistenceBatchSize()
        {
            // The unset-or-default path is the common host configuration path, so validation must not require operators to tune the value.
            Neo4jOptions options = CreateValidOptions();
            Neo4jOptionsValidator validator = new();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

            Assert.True(result.Succeeded);
            Assert.Equal(Neo4jOptions.DefaultPersistenceBatchSize, options.PersistenceBatchSize);
        }

        /// <summary>
        /// Confirms an explicitly configured positive persistence batch size is accepted for constrained Neo4j environments.
        /// </summary>
        [Fact]
        public void ValidateAcceptsExplicitPersistenceBatchSize()
        {
            // A small positive value is valid because tests and low-resource containers may intentionally force more batches.
            Neo4jOptions options = CreateValidOptions();
            options.PersistenceBatchSize = 25;
            Neo4jOptionsValidator validator = new();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

            Assert.True(result.Succeeded);
        }

        /// <summary>
        /// Confirms zero or negative persistence batch sizes are rejected before a writer can build invalid batch ranges.
        /// </summary>
        /// <param name="batchSize">The invalid configured batch size to validate.</param>
        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void ValidateRejectsInvalidPersistenceBatchSize(int batchSize)
        {
            // Invalid batch sizes are reported by setting name only, matching the validator's credential-safe message pattern.
            Neo4jOptions options = CreateValidOptions();
            options.PersistenceBatchSize = batchSize;
            Neo4jOptionsValidator validator = new();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Failures ?? Array.Empty<string>(), failure => failure.Contains(nameof(Neo4jOptions.PersistenceBatchSize), StringComparison.Ordinal));
        }

        /// <summary>
        /// Confirms missing required values are reported by setting name without exposing configured secrets.
        /// </summary>
        [Fact]
        public void ValidateRejectsMissingRequiredValuesWithoutLeakingPassword()
        {
            // The password value is deliberately distinctive so the assertion proves validation messages do not echo secrets when
            // other settings fail.
            Neo4jOptions options = new()
            {
                Uri = "not-a-valid-uri",
                Database = " ",
                Username = " ",
                Password = "SuperSecretPasswordValue",
                ConnectionTimeout = TimeSpan.Zero,
                MaxTransactionRetryTime = TimeSpan.FromSeconds(-1)
            };
            Neo4jOptionsValidator validator = new();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);
            string failureText = string.Join("|", result.Failures ?? Array.Empty<string>());

            Assert.False(result.Succeeded);
            Assert.Contains(nameof(Neo4jOptions.Uri), failureText);
            Assert.Contains(nameof(Neo4jOptions.Database), failureText);
            Assert.Contains(nameof(Neo4jOptions.Username), failureText);
            Assert.Contains(nameof(Neo4jOptions.ConnectionTimeout), failureText);
            Assert.Contains(nameof(Neo4jOptions.MaxTransactionRetryTime), failureText);
            Assert.DoesNotContain(options.Password, failureText, StringComparison.Ordinal);
        }

        /// <summary>
        /// Confirms password validation names the missing setting without logging or returning a password value.
        /// </summary>
        [Fact]
        public void ValidateRejectsMissingPasswordWithSafeMessage()
        {
            // A missing password is a common local setup problem. The validator should identify the field but never suggest a
            // fallback or reveal any secret material.
            Neo4jOptions options = CreateValidOptions();
            options.Password = null;
            Neo4jOptionsValidator validator = new();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);
            string failureText = string.Join("|", result.Failures ?? Array.Empty<string>());

            Assert.False(result.Succeeded);
            Assert.Contains(nameof(Neo4jOptions.Password), failureText);
            Assert.DoesNotContain("secret", failureText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms invalid numeric enum values are rejected before the driver factory uses them.
        /// </summary>
        [Fact]
        public void ValidateRejectsUnsupportedEncryptionMode()
        {
            // Configuration binders can assign undefined enum numeric values, so validation guards the later driver factory switch.
            Neo4jOptions options = CreateValidOptions();
            options.EncryptionMode = (Neo4jEncryptionMode)999;
            Neo4jOptionsValidator validator = new();

            ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Failures ?? Array.Empty<string>(), failure => failure.Contains(nameof(Neo4jOptions.EncryptionMode), StringComparison.Ordinal));
        }

        /// <summary>
        /// Creates a reusable valid options object for validation tests.
        /// </summary>
        /// <returns>A valid Neo4j options instance that individual tests can mutate for failure scenarios.</returns>
        private static Neo4jOptions CreateValidOptions()
        {
            // Centralizing valid defaults keeps each test focused on the one field or behavior it wants to prove.
            return new Neo4jOptions
            {
                Uri = "bolt://localhost:7687",
                Database = "neo4j",
                Username = "neo4j",
                Password = "local-development-password",
                ConnectionTimeout = TimeSpan.FromSeconds(5),
                MaxTransactionRetryTime = TimeSpan.FromSeconds(5),
                EncryptionMode = Neo4jEncryptionMode.Unencrypted
            };
        }
    }
}
