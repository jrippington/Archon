using Archon.Domain.Graph.ControlledValues;
using Archon.Domain.Graph.Identity;
using Archon.Domain.Graph.Metadata;

namespace Archon.Domain.Graph.Model
{
    /// <summary>
    /// Represents generated architecture narrative or exported summary content associated with a snapshot or target stable key.
    /// </summary>
    public sealed class GeneratedSummary
    {
        /// <summary>
        /// Initializes a validated generated summary model.
        /// </summary>
        /// <param name="snapshotStableKey">The stable key of the snapshot that scopes the summary.</param>
        /// <param name="stableKey">The deterministic stable key that identifies the summary within the snapshot contract.</param>
        /// <param name="summaryKind">The controlled summary kind.</param>
        /// <param name="targetStableKey">The optional target stable key described by the summary.</param>
        /// <param name="format">The generated content format, such as Markdown or PlainText.</param>
        /// <param name="title">The developer-facing summary title.</param>
        /// <param name="content">The generated summary content.</param>
        /// <param name="metadata">Deterministic metadata for summary details that are not normalized fields.</param>
        /// <param name="fingerprint">The deterministic fingerprint for diff-relevant summary content.</param>
        public GeneratedSummary(
            StableKey snapshotStableKey,
            StableKey stableKey,
            SummaryKind summaryKind,
            StableKey? targetStableKey,
            string? format,
            string? title,
            string? content,
            GraphMetadata metadata,
            Fingerprint fingerprint)
        {
            // Generated summaries are pure content contracts; later packages decide how to render or persist them.
            ArgumentNullException.ThrowIfNull(summaryKind);
            ArgumentNullException.ThrowIfNull(metadata);

            SnapshotStableKey = snapshotStableKey;
            StableKey = stableKey;
            SummaryKind = summaryKind;
            TargetStableKey = targetStableKey;
            Format = GraphFactValidation.RequiredString(format, nameof(format));
            Title = GraphFactValidation.RequiredString(title, nameof(title));
            Content = GraphFactValidation.RequiredString(content, nameof(content));
            Metadata = metadata;
            Fingerprint = fingerprint;
        }

        /// <summary>
        /// Gets the stable key of the snapshot that scopes the summary.
        /// </summary>
        public StableKey SnapshotStableKey { get; }

        /// <summary>
        /// Gets the deterministic stable key that identifies the summary within the snapshot contract.
        /// </summary>
        public StableKey StableKey { get; }

        /// <summary>
        /// Gets the controlled summary kind.
        /// </summary>
        public SummaryKind SummaryKind { get; }

        /// <summary>
        /// Gets the optional target stable key described by the summary.
        /// </summary>
        public StableKey? TargetStableKey { get; }

        /// <summary>
        /// Gets the generated content format, such as Markdown or PlainText.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Gets the developer-facing summary title.
        /// </summary>
        public string Title { get; }

        /// <summary>
        /// Gets the generated summary content.
        /// </summary>
        public string Content { get; }

        /// <summary>
        /// Gets deterministic metadata for summary details that are not normalized fields.
        /// </summary>
        public GraphMetadata Metadata { get; }

        /// <summary>
        /// Gets the deterministic fingerprint for diff-relevant summary content.
        /// </summary>
        public Fingerprint Fingerprint { get; }
    }
}
