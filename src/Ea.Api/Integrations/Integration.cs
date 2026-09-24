using Ea.Api.Systems;

namespace Ea.Api.Integrations;

/// <summary>
/// Et dataflow fra ét system til et andet ("data går fra → til"), evt. via en platform. Fra og til kan ikke
/// ændres efter oprettelse — integrationens identitet er dens ender (også ved import).
/// </summary>
public sealed class Integration
{
    private string? _name;

    public Guid Id { get; set; }

    public Guid SourceSystemId { get; set; }

    public SystemEntity SourceSystem { get; set; } = null!;

    public Guid TargetSystemId { get; set; }

    public SystemEntity TargetSystem { get; set; } = null!;

    public Guid? ViaPlatformId { get; set; }

    public SystemEntity? ViaPlatform { get; set; }

    public IntegrationType? Type { get; set; }

    /// <summary>Valgfrit navn — skelner flere flows mellem de samme ender (indgår i dublet-nøglen).</summary>
    public string? Name
    {
        get => _name;
        set
        {
            _name = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            NameNormalized = _name?.ToLowerInvariant();
        }
    }

    public string? NameNormalized { get; private set; }

    public string? Description { get; set; }

    public List<IntegrationDataObject> DataObjects { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Sættes ved HVER skrivning — så rækken altid opdateres, og Version (xmin) altid tjekkes,
    /// også når kun dataobjekterne ændres.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    public uint Version { get; set; }
}
