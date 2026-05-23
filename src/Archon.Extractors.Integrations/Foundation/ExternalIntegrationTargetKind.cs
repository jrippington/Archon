namespace Archon.Extractors.Integrations.Foundation
{
    /// <summary>
    /// Identifies the graph target category produced by the WP010 foundation integration extractor.
    /// </summary>
    public enum ExternalIntegrationTargetKind
    {
        /// <summary>
        /// Represents an outbound service dependency such as HTTP, REST, SOAP, gRPC, email, storage, payment, or another service API.
        /// </summary>
        ExternalService,

        /// <summary>
        /// Represents a queue target used by producer, consumer, handler, or receiver evidence.
        /// </summary>
        Queue,

        /// <summary>
        /// Represents a topic target used by publish, subscribe, handler, or receiver evidence.
        /// </summary>
        Topic
    }
}
