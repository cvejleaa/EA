namespace Ea.Api.Tests.Infrastructure;

/// <summary>Stier i repoet — til golden-filer, der committes (kontrakt og CSV-skabelon).</summary>
public static class RepoPaths
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(dir.FullName, "EA.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Kunne ikke finde repo-roden (EA.slnx).");
    }

    public static string File(params string[] parts) => Path.Combine([Root(), .. parts]);

    /// <summary>Golden-filer genskrives med UPDATE_CONTRACT=1 (kun når en ændring af kontrakten er tilsigtet).</summary>
    public static bool UpdateGoldenFiles => Environment.GetEnvironmentVariable("UPDATE_CONTRACT") == "1";
}
