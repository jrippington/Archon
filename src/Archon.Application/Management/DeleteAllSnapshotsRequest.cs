using System.Text.Json;
using System.Text.Json.Serialization;

namespace Archon.Application.Management
{
    /// <summary>
    /// Represents a management request to delete every persisted snapshot after explicit destructive-operation confirmation.
    /// </summary>
    public sealed class DeleteAllSnapshotsRequest
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DeleteAllSnapshotsRequest"/> class.
        /// </summary>
        /// <param name="confirmation">The confirmation phrase that must equal <c>delete-all-snapshots</c> before cleanup is accepted.</param>
        /// <param name="requestedBy">The optional actor identity recorded in audit metadata for the destructive operation.</param>
        [JsonConstructor]
        public DeleteAllSnapshotsRequest(string? confirmation, string? requestedBy)
        {
            // The request shape deliberately captures unmapped JSON fields so the service can reject dry-run and scoped-filter attempts safely.
            Confirmation = confirmation;
            RequestedBy = requestedBy;
        }

        /// <summary>
        /// Gets the confirmation phrase that must equal <c>delete-all-snapshots</c> before cleanup is accepted.
        /// </summary>
        public string? Confirmation { get; }

        /// <summary>
        /// Gets the optional actor identity recorded in audit metadata for the destructive operation.
        /// </summary>
        public string? RequestedBy { get; }

        /// <summary>
        /// Gets any unsupported JSON fields supplied by a caller, including dry-run or scoped-filter attempts.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? UnsupportedFields { get; init; }
    }
}
