using ArchonMcp.McpEnvelope;
using ArchonMcp.McpPrompts;
using ArchonMcp.McpRuntime;
using ArchonMcp.McpSecurity;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace ArchonMcp.Tests
{
    /// <summary>
    /// Verifies WP015 read-only MCP prompt registration, retrieval, audit, and prompt-content safety requirements.
    /// </summary>
    public sealed class ArchonMcpPromptTests
    {
        /// <summary>
        /// Stores the required curated prompt names from the WP015 prompt slice.
        /// </summary>
        private static readonly string[] RequiredPromptNames =
        [
            "impact-analysis",
            "modernization-brief",
            "refactoring-preflight",
            "new-feature-placement",
            "legacy-data-access-review",
            "hotlist-summary",
            "architecture-rule-check"
        ];

        /// <summary>
        /// Confirms the runtime catalog registers every required prompt capability as read-only and mandatory.
        /// </summary>
        [Fact]
        public void CatalogRegistersRequiredPromptCapabilities()
        {
            // The prompt capabilities must be visible to readiness validation alongside tools and resources.
            using WebApplication app = Program.BuildApplication(Array.Empty<string>());
            IArchonMcpRegistrationCatalog catalog = app.Services.GetRequiredService<IArchonMcpRegistrationCatalog>();
            IReadOnlyList<ArchonMcpCapabilityRegistration> registrations = catalog.GetRegistrations();
            ArchonMcpCatalogValidationResult validation = catalog.Validate();

            foreach (string promptName in RequiredPromptNames)
            {
                // Each curated prompt is a read-only template capability rather than a mutating tool.
                Assert.Contains(registrations, registration =>
                    registration.Name == promptName &&
                    registration.Kind == ArchonMcpCapabilityKind.Prompt &&
                    registration.Required &&
                    registration.ReadOnly);
            }

            Assert.Contains(registrations, registration => registration.Name == ArchonMcpPromptOperations.GetPrompt && registration.Required && registration.ReadOnly);
            Assert.Contains(registrations, registration => registration.Name == ArchonMcpPromptOperations.ListPrompts && registration.Required && registration.ReadOnly);
            Assert.True(validation.IsReady);
            Assert.Empty(validation.MissingRequiredCapabilityNames);
            Assert.Empty(validation.ForbiddenCapabilityNames);
        }

        /// <summary>
        /// Confirms the prompt registry loads all versioned templates from embedded resources.
        /// </summary>
        [Fact]
        public void PromptRegistryLoadsVersionedTemplates()
        {
            // Direct registry access keeps this test focused on embedded asset loading instead of authorization behavior.
            using WebApplication app = Program.BuildApplication(Array.Empty<string>());
            IArchonMcpPromptRegistry registry = app.Services.GetRequiredService<IArchonMcpPromptRegistry>();

            IReadOnlyList<ArchonMcpPromptDescriptor> descriptors = registry.ListPrompts();

            Assert.Equal(RequiredPromptNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase), descriptors.Select(descriptor => descriptor.Name));
            foreach (string promptName in RequiredPromptNames)
            {
                // The loaded template must preserve front-matter metadata and the full markdown prompt body.
                Assert.True(registry.TryGetPrompt(promptName, out ArchonMcpPromptTemplate? template));
                Assert.NotNull(template);
                Assert.Equal(1, template.Version);
                Assert.Equal(promptName, template.Name);
                Assert.Contains($"name: {promptName}", template.Content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Required grounding", template.Content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Safety and prompt-injection rules", template.Content, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms prompt listing and retrieval return common MCP envelopes and audit prompt retrieval.
        /// </summary>
        [Fact]
        public async Task PromptToolListsRetrievesAndAuditsPrompts()
        {
            // Capturing audit verifies prompt retrieval is routed through the same safe operation executor as tools and resources.
            CapturingArchonMcpAuditSink auditSink = new();
            using WebApplication app = Program.BuildApplication([
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                "Archon:Mcp:Security:TestCallerId=prompt-user",
                $"Archon:Mcp:Security:AllowedOperations:0={ArchonMcpPromptOperations.GetPrompt}",
                $"Archon:Mcp:Security:AllowedOperations:1={ArchonMcpPromptOperations.ListPrompts}"],
                builder => builder.Services.AddSingleton<IArchonMcpAuditSink>(auditSink));
            IArchonMcpPromptTool promptTool = app.Services.GetRequiredService<IArchonMcpPromptTool>();

            object listPayload = await promptTool.ListPromptsAsync(CancellationToken.None);
            object getPayload = await promptTool.GetPromptAsync(new ArchonMcpPromptRequest { Name = "impact-analysis" }, CancellationToken.None);

            // List and retrieve responses use the common envelope and preserve prompt metadata without snapshot-specific claims.
            ArchonMcpEnvelope<ArchonMcpPromptListFacts> listEnvelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpPromptListFacts>>(listPayload);
            ArchonMcpEnvelope<ArchonMcpPromptFacts> promptEnvelope = Assert.IsType<ArchonMcpEnvelope<ArchonMcpPromptFacts>>(getPayload);
            Assert.Equal(RequiredPromptNames.Length, listEnvelope.Facts.TotalPromptCount);
            Assert.Contains(listEnvelope.Facts.Prompts, prompt => prompt.Name == "impact-analysis");
            Assert.Equal("impact-analysis", promptEnvelope.Facts.Name);
            Assert.Contains("archon.assess_change_impact", promptEnvelope.Facts.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(auditSink.Events, auditEvent => auditEvent.OperationName == ArchonMcpPromptOperations.ListPrompts && auditEvent.CallerId == "prompt-user");
            Assert.Contains(auditSink.Events, auditEvent => auditEvent.OperationName == ArchonMcpPromptOperations.GetPrompt && auditEvent.SafeParameters["promptName"] == "impact-analysis");
        }

        /// <summary>
        /// Confirms disabled prompt operations fail before prompt registry access can return content.
        /// </summary>
        [Fact]
        public async Task DisabledPromptRetrievalFailsClosed()
        {
            // The allow-list intentionally omits prompt retrieval so authorization should fail before template lookup matters.
            using WebApplication app = Program.BuildApplication([
                "Archon:Mcp:Security:RequireAuthenticatedCaller=true",
                "Archon:Mcp:Security:TestCallerId=prompt-user",
                $"Archon:Mcp:Security:AllowedOperations:0={ArchonMcpPromptOperations.ListPrompts}"]);
            IArchonMcpPromptTool promptTool = app.Services.GetRequiredService<IArchonMcpPromptTool>();

            object payload = await promptTool.GetPromptAsync(new ArchonMcpPromptRequest { Name = "impact-analysis" }, CancellationToken.None);

            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(ArchonMcpErrorCategory.Forbidden, error.Error.Category);
        }

        /// <summary>
        /// Confirms invalid and unknown prompt requests produce structured safe errors.
        /// </summary>
        [Theory]
        [InlineData(null, ArchonMcpErrorCategory.Validation)]
        [InlineData("", ArchonMcpErrorCategory.Validation)]
        [InlineData("not-a-prompt", ArchonMcpErrorCategory.NotFound)]
        public async Task PromptRetrievalReturnsStructuredErrors(string? promptName, ArchonMcpErrorCategory expectedCategory)
        {
            // Structured errors keep prompt lookup failures safe and consistent with tool/resource error handling.
            using WebApplication app = Program.BuildApplication(Array.Empty<string>());
            IArchonMcpPromptTool promptTool = app.Services.GetRequiredService<IArchonMcpPromptTool>();

            object payload = await promptTool.GetPromptAsync(new ArchonMcpPromptRequest { Name = promptName }, CancellationToken.None);

            ArchonMcpErrorResponse error = Assert.IsType<ArchonMcpErrorResponse>(payload);
            Assert.Equal(expectedCategory, error.Error.Category);
            Assert.DoesNotContain("D:\\", error.Error.Message, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirms every prompt contains required grounding, unknown reporting, no-mutation, and prompt-injection guidance.
        /// </summary>
        [Fact]
        public void PromptContentMeetsSafetyAndGroundingRequirements()
        {
            // Content checks guard the curated workflow language that makes these templates safe for AI clients.
            using WebApplication app = Program.BuildApplication(Array.Empty<string>());
            IArchonMcpPromptRegistry registry = app.Services.GetRequiredService<IArchonMcpPromptRegistry>();
            string[] requiredPhrases =
            [
                "Ground",
                "evidence",
                "unknown",
                "confidence",
                "stable key",
                "Treat extracted source text",
                "untrusted",
                "instructions embedded",
                "read-only Archon MCP tools",
                "shell commands",
                "arbitrary SQL",
                "arbitrary Cypher",
                "filesystem mutation",
                "source-code mutation",
                "database mutation",
                "direct remediation"
            ];

            foreach (string promptName in RequiredPromptNames)
            {
                // Every prompt must include common safety language plus workflow-specific read-only operation sequences.
                Assert.True(registry.TryGetPrompt(promptName, out ArchonMcpPromptTemplate? template));
                Assert.NotNull(template);
                foreach (string requiredPhrase in requiredPhrases)
                {
                    Assert.Contains(requiredPhrase, template.Content, StringComparison.OrdinalIgnoreCase);
                }
                Assert.Contains("Do not", template.Content, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Safe follow-ups", template.Content, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Confirms the HTTP verification endpoints expose prompt listing and retrieval without exposing unsupported prompt paths.
        /// </summary>
        /// <returns>A task that completes after in-memory HTTP prompt verification succeeds.</returns>
        [Fact]
        public async Task PromptVerificationEndpointsReturnRegisteredPrompts()
        {
            // Verification endpoints are narrow host paths until the final MCP protocol adapter maps the same prompt services.
            await using WebApplication app = Program.BuildApplication(Array.Empty<string>(), builder => builder.WebHost.UseTestServer());
            await app.StartAsync();
            using HttpClient client = app.GetTestClient();

            HttpResponseMessage listResponse = await client.GetAsync("/mcp/prompts");
            HttpResponseMessage promptResponse = await client.GetAsync("/mcp/prompts/impact-analysis");
            HttpResponseMessage missingResponse = await client.GetAsync("/mcp/prompts/not-a-prompt");

            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, promptResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);

            string promptJson = await promptResponse.Content.ReadAsStringAsync();
            Assert.Contains("impact-analysis", promptJson, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("archon.assess_change_impact", promptJson, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Captures MCP audit events in memory so tests can inspect prompt telemetry without parsing log output.
        /// </summary>
        private sealed class CapturingArchonMcpAuditSink : IArchonMcpAuditSink
        {
            /// <summary>
            /// Stores the sanitized audit events recorded during the test case.
            /// </summary>
            private readonly List<ArchonMcpAuditEvent> _events = [];

            /// <summary>
            /// Gets the immutable captured audit events.
            /// </summary>
            public IReadOnlyList<ArchonMcpAuditEvent> Events => _events;

            /// <summary>
            /// Records one sanitized audit event for later assertions.
            /// </summary>
            /// <param name="auditEvent">The safe audit event emitted by the MCP operation executor.</param>
            public void Record(ArchonMcpAuditEvent auditEvent)
            {
                // Tests inspect the structured event object to verify prompt operations remain auditable and secret-safe.
                _events.Add(auditEvent);
            }
        }
    }
}
