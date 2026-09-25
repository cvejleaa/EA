using System.Security.Cryptography;
using System.Text.Json;
using Ea.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ea.Api.Common;

/// <summary>
/// Det fælles for importerne (kortet og koblingerne): at læse filen sikkert, tjekke overskrifterne og protokollen
/// med tør-kørsel og fingeraftryk, så der gemmes præcis det, brugeren så — eller intet.
/// </summary>
public static class CsvImport
{
    /// <summary>Nok til at rette filen; flere fejl ville kun gøre svaret uoverskueligt.</summary>
    public const int MaxErrors = 200;

    /// <summary>Læser request-kroppen, men aldrig mere end <paramref name="maxBytes"/> (null = for stor).</summary>
    public static async Task<byte[]?> ReadBodyAsync(Stream body, long? contentLength, int maxBytes, CancellationToken ct)
    {
        if (contentLength > maxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        try
        {
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Kestrels egen grænse (fx en chunked upload uden Content-Length): samme svar som vores.
            return null;
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Fejlen i første linje — eller null, når overskrifterne er præcis <paramref name="expected"/> (tomme felter
    /// til sidst tæller ikke; Excel efterlader dem gerne). Er det i stedet registrets ANDEN fil
    /// (<paramref name="otherFile"/>), siger fejlen det og hvor den indlæses.
    /// </summary>
    public static ImportRowError? HeaderError(
        IReadOnlyList<string> header,
        IReadOnlyList<string> expected,
        (IReadOnlyList<string> Header, string Message)? otherFile = null)
    {
        var fields = WithoutTrailingEmpty(header);
        if (fields.Count == 0)
        {
            return new ImportRowError(1, null, "Filen er tom.");
        }

        if (fields.SequenceEqual(expected))
        {
            return null;
        }

        if (otherFile is { } other && fields.SequenceEqual(other.Header))
        {
            return new ImportRowError(1, null, other.Message);
        }

        var hint = fields.Count == 1 && fields[0].Contains(',', StringComparison.Ordinal)
            ? " Filen ser ud til at bruge komma som skilletegn. " + Csv.SaveAsUtf8Advice
            : "";
        return new ImportRowError(1, null, $"Første linje skal være præcis: {string.Join(Csv.Separator, expected)}.{hint}");
    }

    public static List<string> WithoutTrailingEmpty(IReadOnlyList<string> fields)
    {
        var count = fields.Count;
        while (count > 0 && fields[count - 1].Trim().Length == 0)
        {
            count--;
        }

        return fields.Take(count).ToList();
    }

    /// <summary>
    /// Et fingeraftryk af det, importen vil gøre. Er data ændret, siden tør-kørslen blev lavet, giver den samme fil
    /// et andet aftryk — og importen afvises i stedet for at gemme noget, brugeren ikke har set.
    /// </summary>
    public static string Fingerprint<T>(T changes) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(changes)));

    /// <summary>
    /// Gennemfører en import i én transaktion: låser (<paramref name="tableLock"/> — en fast lås, aldrig en streng),
    /// beregner planen igen på den låste tilstand og gemmer kun, hvis aftrykket er det, tør-kørslen viste. Null betyder
    /// "data er ændret siden tør-kørslen" — også når en samtidig ændring af en række opdages ved gem. Kører i EF's
    /// retry-strategi, så et forbindelsesbrud starter forfra med en ny beregning.
    /// </summary>
    /// <param name="recompute">Planen og dens aftryk på den låste tilstand; en null-plan (fx nye fejl) afvises.</param>
    public static Task<TPlan?> CommitIfUnchangedAsync<TPlan>(
        EaDbContext db,
        TableLock tableLock,
        string fingerprint,
        Func<Task<(TPlan? Plan, string? Fingerprint)>> recompute,
        Func<TPlan, Task> apply,
        CancellationToken ct)
        where TPlan : class =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.LockAsync(tableLock, ct);

            var (plan, current) = await recompute();
            if (plan is null || !string.Equals(current, fingerprint, StringComparison.Ordinal))
            {
                return null;
            }

            await apply(plan);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                return null;
            }

            await transaction.CommitAsync(ct);
            return plan;
        });
}
