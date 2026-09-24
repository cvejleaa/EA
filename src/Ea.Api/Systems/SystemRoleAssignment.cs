using Ea.Api.Persons;

namespace Ea.Api.Systems;

public sealed class SystemRoleAssignment
{
    public Guid SystemId { get; set; }

    public SystemRole Role { get; set; }

    public Guid PersonId { get; set; }

    public Person Person { get; set; } = null!;
}
