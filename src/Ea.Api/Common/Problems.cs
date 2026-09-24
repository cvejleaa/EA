using Microsoft.AspNetCore.Http.HttpResults;

namespace Ea.Api.Common;

/// <summary>Fejlsvar på dansk. Klienten viser <c>detail</c> (og feltfejl fra <c>errors</c>).</summary>
public static class Problems
{
    public static ProblemHttpResult Conflict(string detail) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Konflikt");

    public static ProblemHttpResult StaleVersion() => Conflict(
        "Systemet er ændret af en anden, siden du åbnede det. Genindlæs for at se den nyeste version.");

    public static ValidationProblem Validation(Dictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, title: "Der er fejl i oplysningerne.");

    public static ValidationProblem Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });
}

public static class SqlPatterns
{
    /// <summary>ILIKE-mønster for "indeholder", hvor brugerens % og _ tolkes bogstaveligt (escape-tegn \).</summary>
    public static string Contains(string text) =>
        "%" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal) + "%";

    public const string Escape = "\\";
}
