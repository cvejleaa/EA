using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Ea.Api.Common;

/// <summary>
/// Fejlsvar på dansk. Klienten viser <c>detail</c> (og feltfejl fra <c>errors</c>). Konflikter (409) har en
/// <c>type</c>, så klienten kan skelne: kun en forældet version må tilbyde "Hent nyeste version".
/// </summary>
public static class Problems
{
    /// <summary>Nogen har ændret data, siden brugeren hentede dem.</summary>
    public const string StaleVersionType = "urn:ea:problem:stale-version";

    /// <summary>Det findes allerede (navn eller dublet-nøgle).</summary>
    public const string DuplicateType = "urn:ea:problem:duplicate";

    /// <summary>Handlingen er blokeret af noget, der afhænger af den (fx sletning af et system i brug).</summary>
    public const string BlockedType = "urn:ea:problem:blocked";

    /// <summary>
    /// Data er ændret siden en tør-kørsel. Handlingen er at køre tør-kørslen igen — ikke "Hent nyeste version".
    /// </summary>
    public const string StaleDryRunType = "urn:ea:problem:stale-dry-run";

    public static ProblemHttpResult StaleVersion(string subject) => Conflict(
        $"{subject} er ændret af en anden, siden du åbnede det. Genindlæs for at se den nyeste version.",
        StaleVersionType);

    public static ProblemHttpResult StaleDryRun() => Conflict(
        "Kortet eller koblingerne til det er ændret, siden du lavede tør-kørslen. Kør tør-kørslen igen for at se, hvad importen nu vil gøre.",
        StaleDryRunType);

    public static ProblemHttpResult Duplicate(string detail) => Conflict(detail, DuplicateType);

    public static ProblemHttpResult Blocked(string detail) => Conflict(detail, BlockedType);

    public static ValidationProblem Validation(Dictionary<string, string[]> errors) =>
        TypedResults.ValidationProblem(errors, title: "Der er fejl i oplysningerne.");

    public static ValidationProblem Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    private static ProblemHttpResult Conflict(string detail, string type) =>
        TypedResults.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict, title: "Konflikt", type: type);
}

public static class DbErrors
{
    /// <summary>Om en gem-fejl skyldes netop dette unikke indeks (så den kan blive en forståelig 409).</summary>
    public static bool IsUniqueViolation(DbUpdateException ex, string indexName) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg &&
        pg.ConstraintName == indexName;
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
