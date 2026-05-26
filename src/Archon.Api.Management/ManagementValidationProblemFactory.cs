using Archon.Application.Management;
using Microsoft.AspNetCore.Http;

namespace Archon.Api.Management
{
    /// <summary>
    /// Creates safe validation problem responses for controlled management endpoints.
    /// </summary>
    internal static class ManagementValidationProblemFactory
    {
        /// <summary>
        /// Converts management validation errors into ASP.NET Core validation problem details.
        /// </summary>
        /// <param name="errors">The validation errors returned by the application service.</param>
        /// <returns>A result that writes a 400 validation problem response.</returns>
        public static IResult Create(IReadOnlyList<ManagementValidationError> errors)
        {
            // The API projects stable validation codes as keys and credential-safe messages as values.
            Dictionary<string, string[]> problemErrors = errors
                .GroupBy(error => error.Code, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Message).ToArray(), StringComparer.Ordinal);
            return Results.ValidationProblem(problemErrors, title: "Management request validation failed.");
        }
    }
}
