namespace Ea.Api.Auth;

/// <summary>Konfiguration af udviklings-login (Auth:Dev). Kun fiktive brugere.</summary>
public sealed class DevAuthOptions
{
    public const string Issuer = "ea-dev";
    public const string Audience = "ea-api";

    public string SigningKey { get; set; } = "";

    public List<DevUser> Users { get; set; } = [];
}

public sealed class DevUser
{
    public string Id { get; set; } = "";

    public string Oid { get; set; } = "";

    public string Name { get; set; } = "";

    public List<string> Roles { get; set; } = [];
}
