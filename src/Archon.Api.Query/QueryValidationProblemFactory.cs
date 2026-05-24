using Microsoft.AspNetCore.Http;

namespace Archon.Api.Query
{
    /// <summary>
    /// Creates deterministic validation-problem responses for controlled query API endpoints.
    /// </summary>
    internal static class QueryValidationProblemFactory
    {
        /// <summary>
        /// Converts an argument exception raised by an application query contract into a validation-problem response.
        /// </summary>
        /// <param name="exception">The exception raised while constructing or validating a controlled query.</param>
        /// <param name="fallbackKey">The validation key used when the exception does not carry a parameter name.</param>
        /// <returns>An HTTP validation-problem result with deterministic field names and messages.</returns>
        public static IResult FromArgumentException(ArgumentException exception, string fallbackKey)
        {
            // Public endpoints should not leak stack traces or CLR exception type names; only the deterministic field and message cross the boundary.
            ArgumentNullException.ThrowIfNull(exception);
            string key = string.IsNullOrWhiteSpace(exception.ParamName) ? fallbackKey : exception.ParamName!;
            return Results.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [key] = [exception.Message]
            });
        }
    }
}