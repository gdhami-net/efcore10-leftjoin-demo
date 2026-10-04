using Microsoft.EntityFrameworkCore;

namespace EfCore10LeftJoinDemo.NullableProbe;

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Code { get; set; } = "";
}

public sealed class Db : DbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnConfiguring(DbContextOptionsBuilder b) => b.UseSqlite("Data Source=:memory:");
}

/// <summary>
/// Two spellings of the same left join, neither one guarding the right side.
/// The check script builds this project with nullable warnings on (and not as
/// errors) and reads the warnings out of the build log. Expected: exactly one,
/// CS8602, on the LeftJoin form.
/// </summary>
public static class Probe
{
    // No warning: the query-syntax range variable is a plain Order.
    public static IQueryable<string> QuerySyntax(Db db) =>
        from c in db.Customers
        join o in db.Orders on c.Id equals o.CustomerId into matches
        from o in matches.DefaultIfEmpty()
        select o.Code;

    // CS8602: LeftJoin hands the result selector an Order?.
    public static IQueryable<string> LeftJoin(Db db) =>
        db.Customers.LeftJoin(
            db.Orders,
            c => c.Id,
            o => o.CustomerId,
            (c, o) => o.Code);
}
