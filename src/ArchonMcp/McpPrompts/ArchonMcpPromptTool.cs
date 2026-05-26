using ArchonMcp.McpEnvelope;
using ArchonMcp.McpSecurity;

namespace ArchonMcp.McpPrompts
{
    /// <summary>
    /// Provides authorized read-only MCP prompt listing and retrieval over versioned embedded prompt assets.
    /// </summary>
    public sealed class ArchonMcpPromptTool : IArchonMcpPromptTool
    {
        /// <summary>
        /// Resolves prompt templates from embedded read-only resources.
        /// </summary>
        private readonly IArchonMcpPromptRegistry _promptRegistry;

        /// <summary>
        /// Executes prompt operations through authorization, allow-listing, audit, and safe error mapping.
        /// </summary>
        private readonly IArchonMcpOperationExecutor _operationExecutor;

        /// <summary>
        /// Creates a prompt tool over the prompt registry and common MCP operation executor.
        /// </summary>
        /// <param name="promptRegistry">The registry that provides read-only prompt templates.</param>
        /// <param name="operationExecutor">The executor that applies authorization and audit before prompt access.</param>
        public ArchonMcpPromptTool(
            IArchonMcpPromptRegistry promptRegistry,
            IArchonMcpOperationExecutor operationExecutor)
        {
            // Prompt handling reuses the same security pipeline as tools and resources so prompt access remains auditable.
            _promptRegistry = promptRegistry ?? throw new ArgumentNullException(nameof(promptRegistry));
            _operationExecutor = operationExecutor ?? throw new ArgumentNullException(nameof(operationExecutor));
        }

        /// <inheritdoc />
        public async Task<object> ListPromptsAsync(CancellationToken cancellationToken)
        {
            // Listing prompt metadata is still routed through authorization so disabled prompt operations fail closed.
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpPromptOperations.ListPrompts,
                parameters: null,
                () => Task.FromResult<object>(CreatePromptListEnvelope()),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <inheritdoc />
        public async Task<object> GetPromptAsync(ArchonMcpPromptRequest request, CancellationToken cancellationToken)
        {
            // Authorization intentionally runs before request validation to avoid leaking supported prompt names to disabled callers.
            ArgumentNullException.ThrowIfNull(request);
            ArchonMcpOperationResult result = await _operationExecutor.ExecuteAsync(
                ArchonMcpPromptOperations.GetPrompt,
                new Dictionary<string, string>
                {
                    ["promptName"] = request.Name ?? string.Empty
                },
                () => Task.FromResult<object>(CreatePromptEnvelope(request)),
                cancellationToken).ConfigureAwait(false);

            return result.Payload;
        }

        /// <summary>
        /// Creates the prompt inventory envelope returned by the list operation.
        /// </summary>
        /// <returns>A common MCP envelope containing prompt descriptors.</returns>
        private ArchonMcpEnvelope<ArchonMcpPromptListFacts> CreatePromptListEnvelope()
        {
            // Prompt descriptors are intentionally concise so listing does not expand all workflow text into the client context.
            IReadOnlyList<ArchonMcpPromptDescriptor> prompts = _promptRegistry.ListPrompts();
            ArchonMcpPromptListFacts facts = new(prompts, prompts.Count);

            return new ArchonMcpEnvelope<ArchonMcpPromptListFacts>(
                ArchonMcpPromptOperations.ListPrompts,
                snapshot: null,
                $"Returned {prompts.Count} registered read-only Archon MCP prompt templates.",
                new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "Prompt descriptors were loaded from versioned embedded MCP host resources."),
                facts,
                evidence: [],
                findings: [],
                unknowns: [],
                warnings: [],
                limits: ArchonMcpLimitMetadata.None("promptCount", prompts.Count),
                suggestedFollowUps: prompts
                    .Select(prompt => new ArchonMcpSuggestedFollowUp(
                        "Retrieve prompt template",
                        ArchonMcpPromptOperations.GetPrompt,
                        new Dictionary<string, string>
                        {
                            ["name"] = prompt.Name
                        }))
                    .ToArray());
        }

        /// <summary>
        /// Creates a prompt retrieval envelope or a structured validation/not-found error.
        /// </summary>
        /// <param name="request">The prompt retrieval request supplied by the caller.</param>
        /// <returns>A common MCP prompt envelope or structured safe error response.</returns>
        private object CreatePromptEnvelope(ArchonMcpPromptRequest request)
        {
            // Prompt names are stable tokens; validation keeps blank requests from reaching the registry lookup as ambiguous input.
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpPromptOperations.GetPrompt,
                    ArchonMcpErrorCategory.Validation,
                    "A prompt name is required.",
                    [new ArchonMcpSuggestedFollowUp("List available prompts", ArchonMcpPromptOperations.ListPrompts, null)]);
            }

            if (!_promptRegistry.TryGetPrompt(request.Name, out ArchonMcpPromptTemplate? template) || template is null)
            {
                return ArchonMcpErrorResponse.Create(
                    ArchonMcpPromptOperations.GetPrompt,
                    ArchonMcpErrorCategory.NotFound,
                    "The requested MCP prompt template is not registered.",
                    [new ArchonMcpSuggestedFollowUp("List available prompts", ArchonMcpPromptOperations.ListPrompts, null)]);
            }

            ArchonMcpPromptFacts facts = new(template.Name, template.Version, template.Summary, template.Content);
            return new ArchonMcpEnvelope<ArchonMcpPromptFacts>(
                ArchonMcpPromptOperations.GetPrompt,
                snapshot: null,
                $"Returned read-only MCP prompt template '{template.Name}' version {template.Version}.",
                new ArchonMcpConfidence(ArchonMcpConfidenceLevel.High, "The prompt was loaded from a versioned embedded MCP host resource."),
                facts,
                evidence: [],
                findings: [],
                unknowns: [],
                warnings: [],
                limits: ArchonMcpLimitMetadata.None("promptTemplate", 1),
                suggestedFollowUps: []);
        }
    }
}
