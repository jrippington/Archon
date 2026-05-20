using Archon.Domain.Graph.Metadata;
using Xunit;

namespace Archon.Domain.Tests.Graph.Metadata
{
    /// <summary>
    /// Verifies deterministic metadata behavior required by WP002 fingerprinting and graph contracts.
    /// </summary>
    public sealed class GraphMetadataTests
    {
        /// <summary>
        /// Verifies empty metadata is explicit and serializes safely as an empty JSON object.
        /// </summary>
        [Fact]
        public void EmptyMetadataSerializesAsEmptyObject()
        {
            // Empty metadata is an explicit value so callers never need to use null to mean no extraction-specific details.
            GraphMetadata metadata = GraphMetadata.Empty;

            Assert.True(metadata.IsEmpty);
            Assert.Equal("{}", metadata.ToCanonicalJson());
        }

        /// <summary>
        /// Verifies metadata canonicalization is independent of dictionary insertion order.
        /// </summary>
        [Fact]
        public void MetadataCanonicalizationOrdersPropertiesDeterministically()
        {
            // The same logical metadata can be assembled by different extractors in different insertion orders.
            GraphMetadata first = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["routeTemplate"] = "/api/customers/{id}",
                ["httpVerbs"] = new[] { "GET", "POST" },
                ["provider"] = "AspNetCore"
            });
            GraphMetadata second = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["provider"] = "AspNetCore",
                ["httpVerbs"] = new[] { "GET", "POST" },
                ["routeTemplate"] = "/api/customers/{id}"
            });

            Assert.Equal(first.ToCanonicalJson(), second.ToCanonicalJson());
            Assert.Equal("{\"httpVerbs\":[\"GET\",\"POST\"],\"provider\":\"AspNetCore\",\"routeTemplate\":\"/api/customers/{id}\"}", first.ToCanonicalJson());
        }

        /// <summary>
        /// Verifies nested metadata objects and arrays are canonicalized recursively.
        /// </summary>
        [Fact]
        public void MetadataCanonicalizationOrdersNestedObjectsRecursively()
        {
            // Nested provider metadata must remain deterministic because it participates in fingerprint input.
            GraphMetadata metadata = GraphMetadata.From(new Dictionary<string, object?>
            {
                ["binding"] = new Dictionary<string, object?>
                {
                    ["target"] = "CustomerViewModel.Name",
                    ["mode"] = "TwoWay"
                },
                ["controls"] = new[]
                {
                    new Dictionary<string, object?>
                    {
                        ["name"] = "CustomerNameTextBox",
                        ["type"] = "TextBox"
                    }
                }
            });

            Assert.Equal("{\"binding\":{\"mode\":\"TwoWay\",\"target\":\"CustomerViewModel.Name\"},\"controls\":[{\"name\":\"CustomerNameTextBox\",\"type\":\"TextBox\"}]}", metadata.ToCanonicalJson());
        }

        /// <summary>
        /// Verifies metadata rejects normalized graph properties that must remain first-class fields.
        /// </summary>
        /// <param name="reservedKey">The reserved normalized graph property key.</param>
        [Theory]
        [InlineData("stableKey")]
        [InlineData("nodeKind")]
        [InlineData("edgeKind")]
        [InlineData("evidenceKind")]
        [InlineData("knowledgeKind")]
        [InlineData("confidence")]
        [InlineData("unknownReason")]
        public void MetadataRejectsReservedNormalizedGraphProperties(string reservedKey)
        {
            // Core graph fields must not hide in metadata because queries, rules, and fingerprints need normalized fields.
            Assert.Throws<ArgumentException>(() => GraphMetadata.From(new Dictionary<string, object?>
            {
                [reservedKey] = "misplaced-core-value"
            }));
        }

        /// <summary>
        /// Verifies metadata rejects null, empty, or whitespace property names.
        /// </summary>
        /// <param name="key">The invalid metadata key.</param>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void MetadataRejectsInvalidPropertyNames(string? key)
        {
            // Meaningless metadata property names would create ambiguous canonical JSON and unclear extractor payloads.
            KeyValuePair<string?, object?>[] values =
            [
                new KeyValuePair<string?, object?>(key, "value")
            ];

            Assert.Throws<ArgumentException>(() => GraphMetadata.From(values));
        }
    }
}
