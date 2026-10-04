using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EfCore10LeftJoinDemo.EfCore9Probe;

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

public sealed class Db(DbContextOptions<Db> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
}

/// <summary>
/// Runs the LeftJoin query under EF Core 9 on .NET 10 and prints what happens,
/// on one line, so the parent check script can assert on it.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var db = new Db(new DbContextOptionsBuilder<Db>().UseSqlite(connection).Options);
        db.Database.EnsureCreated();

        Console.WriteLine($"EF Core {typeof(DbContext).Assembly.GetName().Version}");
        try
        {
            var rows = db.Customers
                .LeftJoin(db.Orders, c => c.Id, o => o.CustomerId, (c, o) => o!.Code)
                .ToList();
            Console.WriteLine($"TRANSLATED: {rows.Count} rows");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"THREW {ex.GetType().Name}: {ex.Message.Replace("\r", " ").Replace("\n", " ")}");
        }
        return 0;
    }
}
