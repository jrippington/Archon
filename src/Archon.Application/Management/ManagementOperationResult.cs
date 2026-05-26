namespace Archon.Application.Management
{
    /// <summary>
    /// Wraps controlled management operation data or validation errors in a common application result.
    /// </summary>
    /// <typeparam name="TData">The response data type produced by the operation when validation succeeds.</typeparam>
    public sealed class ManagementOperationResult<TData>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ManagementOperationResult{TData}"/> class.
        /// </summary>
        /// <param name="data">The successful response data, or <see langword="null"/> when validation failed.</param>
        /// <param name="errors">The safe validation errors collected before any state change.</param>
        private ManagementOperationResult(TData? data, IEnumerable<ManagementValidationError>? errors)
        {
            // The constructor centralizes list copying so factory methods cannot accidentally expose mutable error collections.
            Data = data;
            Errors = errors?.ToArray() ?? [];
        }

        /// <summary>
        /// Gets the successful response data, or <see langword="null"/> when validation failed.
        /// </summary>
        public TData? Data { get; }

        /// <summary>
        /// Gets the safe validation errors collected before any state change.
        /// </summary>
        public IReadOnlyList<ManagementValidationError> Errors { get; }

        /// <summary>
        /// Gets a value indicating whether the operation completed without validation errors.
        /// </summary>
        public bool IsSuccess
        {
            get
            {
                // A result is successful only when no validation errors were recorded.
                return Errors.Count == 0;
            }
        }

        /// <summary>
        /// Creates a successful result with operation data.
        /// </summary>
        /// <param name="data">The operation data produced after validation succeeded.</param>
        /// <returns>A successful management operation result.</returns>
        public static ManagementOperationResult<TData> Success(TData data)
        {
            // Successful operations always carry a data payload so route handlers can project a uniform response.
            ArgumentNullException.ThrowIfNull(data);
            return new ManagementOperationResult<TData>(data, []);
        }

        /// <summary>
        /// Creates a failed result from validation errors.
        /// </summary>
        /// <param name="errors">The validation errors that prevented the operation from mutating state.</param>
        /// <returns>A failed management operation result.</returns>
        public static ManagementOperationResult<TData> Failure(IEnumerable<ManagementValidationError> errors)
        {
            // Failed operations do not carry partially created data because API callers need a clear all-or-nothing contract.
            ArgumentNullException.ThrowIfNull(errors);
            return new ManagementOperationResult<TData>(default, errors);
        }
    }
}
