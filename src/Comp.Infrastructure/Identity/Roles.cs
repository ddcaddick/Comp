namespace Comp.Infrastructure.Identity;

/// <summary>The four roles from the security model. Seeded into asp_net_roles by migration.</summary>
public static class Roles
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string Admin = "ADMIN";
    public const string Official = "OFFICIAL";
    public const string ReadOnly = "READ_ONLY";

    public static readonly IReadOnlyList<string> All = [SuperAdmin, Admin, Official, ReadOnly];
}
