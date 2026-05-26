namespace Archon.Application.Management
{
    /// <summary>
    /// Represents one safe validation error produced by controlled management operations.
    /// </summary>
    public sealed class ManagementValidationError
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ManagementValidationError"/> class.
        /// </summary>
        /// <param name="code">The stable validation code that API tests and clients can assert.</param>
        /// <param name="message">The credential-safe human-readable validation message.</param>
        public ManagementValidationError(string code, string message)
        {
            // Validation errors are intentionally small and safe for direct API problem-detail projection.
            Code = string.IsNullOrWhiteSpace(code) ? "ManagementValidationError" : code.Trim();
            Message = string.IsNullOrWhiteSpace(message) ? "The management request is invalid." : message.Trim();
        }

        /// <summary>
        /// Gets the stable validation code that API tests and clients can assert.
        /// </summary>
        public string Code { get; }

        /// <summary>
        /// Gets the credential-safe human-readable validation message.
        /// </summary>
        public string Message { get; }
    }
}
