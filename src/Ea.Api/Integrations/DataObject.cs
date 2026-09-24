namespace Ea.Api.Integrations;

/// <summary>En type data, der flyttes af integrationer (fx "Medarbejder"). Et navn, ét sted — ikke fritekst.</summary>
public sealed class DataObject
{
    private string _name = "";

    public Guid Id { get; set; }

    public string Name
    {
        get => _name;
        set
        {
            _name = value.Trim();
            NameNormalized = _name.ToLowerInvariant();
        }
    }

    public string NameNormalized { get; private set; } = "";
}

public sealed class IntegrationDataObject
{
    public Guid IntegrationId { get; set; }

    public Guid DataObjectId { get; set; }

    public DataObject DataObject { get; set; } = null!;
}
