using Archon.Extractors.Integrations.Foundation;
using Xunit;

namespace Archon.Extractors.Integrations.Tests.Foundation
{
    /// <summary>
    /// Verifies the deterministic stable-key helpers required by the WP010 foundation slice.
    /// </summary>
    public sealed class ExternalIntegrationStableKeyTests
    {
        /// <summary>
        /// Verifies external-service keys use logical service identity rather than machine-specific repository paths.
        /// </summary>
        [Fact]
        public void ForExternalService_WhenRepositoryRootsDiffer_ShouldReturnSameKey()
        {
            // Service keys must not include absolute paths because different developer machines analyze the same repository.
            string firstKey = ExternalIntegrationStableKey.ForExternalService("D:/Dev/Archon", "Billing API").Value;
            string secondKey = ExternalIntegrationStableKey.ForExternalService("E:/Agent/work/Archon", "Billing API").Value;

            Assert.Equal("externalservice://Billing API", firstKey);
            Assert.Equal(firstKey, secondKey);
        }

        /// <summary>
        /// Verifies unknown external-service keys are repository-relative and source-location scoped.
        /// </summary>
        [Fact]
        public void ForUnknownExternalService_WhenAbsoluteRootsDiffer_ShouldReturnSameRepositoryRelativeKey()
        {
            // Unknown-service keys preserve the repository-relative evidence path and source line without leaking absolute roots.
            string firstKey = ExternalIntegrationStableKey.ForUnknownExternalService("D:/Dev/Archon", "D:/Dev/Archon/src/App/Client.cs", 42, "HttpClient.SendAsync").Value;
            string secondKey = ExternalIntegrationStableKey.ForUnknownExternalService("E:/Agent/work/Archon", "E:/Agent/work/Archon/src/App/Client.cs", 42, "HttpClient.SendAsync").Value;

            Assert.Equal(firstKey, secondKey);
            Assert.StartsWith("externalservice://unknown/src/App/Client.cs/42/", firstKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// Verifies queue and topic keys are logical transport identities that do not depend on input ordering.
        /// </summary>
        [Fact]
        public void ForQueueAndTopic_WhenInputsAreShuffled_ShouldReturnDeterministicKeys()
        {
            // Queue and topic identities are transport-level names, so the same inputs should produce the same set regardless of enumeration order.
            string[] original =
            [
                ExternalIntegrationStableKey.ForQueue("AzureServiceBus", "orders").Value,
                ExternalIntegrationStableKey.ForTopic("AzureServiceBus", "invoices").Value
            ];
            string[] shuffled =
            [
                ExternalIntegrationStableKey.ForTopic("AzureServiceBus", "invoices").Value,
                ExternalIntegrationStableKey.ForQueue("AzureServiceBus", "orders").Value
            ];

            Assert.Equal(original.Order(StringComparer.Ordinal).ToArray(), shuffled.Order(StringComparer.Ordinal).ToArray());
            Assert.Contains("queue://AzureServiceBus:orders", original);
            Assert.Contains("topic://AzureServiceBus:invoices", original);
        }

        /// <summary>
        /// Verifies relationship keys are based on relationship semantics and endpoint stable keys.
        /// </summary>
        [Fact]
        public void ForRelationship_WhenCalledTwice_ShouldReturnSameKey()
        {
            // Relationship keys must be repeatable so accumulation can de-duplicate independently discovered equivalent relationships.
            string firstKey = ExternalIntegrationStableKey.ForRelationship("CALLS_EXTERNAL_SERVICE", "method://App.Client.Send", "externalservice://Billing API").Value;
            string secondKey = ExternalIntegrationStableKey.ForRelationship("CALLS_EXTERNAL_SERVICE", "method://App.Client.Send", "externalservice://Billing API").Value;

            Assert.Equal("relationship://CALLS_EXTERNAL_SERVICE/method://App.Client.Send/externalservice://Billing API", firstKey);
            Assert.Equal(firstKey, secondKey);
        }
    }
}
