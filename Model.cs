using Microsoft.EntityFrameworkCore;

namespace EfCore10LeftJoinDemo;

public sealed class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public List<Order> Orders { get; } = [];
}

public sealed class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public string Code { get; set; } = "";
    public decimal Total { get; set; }
}

/// <summary>One row of the join: a customer, and an order if the customer has one.</summary>
public sealed record Row(string Name, string? Code, decimal? Total);

/// <summary>The same row, keeping the order entity instead of its columns.</summary>
public sealed record CustomerOrder(string Name, Order? Order);

public sealed class ShopDb(DbContextOptions<ShopDb> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Customer>().HasMany(c => c.Orders).WithOne().HasForeignKey(o => o.CustomerId);
    }

    /// <summary>
    /// Four customers, three orders. Brahim and Dalia have no orders at all, so a
    /// left join returns five rows and an inner join returns three.
    /// </summary>
    public void Seed()
    {
        Database.EnsureCreated();
        if (Customers.Any()) return;
        Customers.AddRange(
            new Customer { Id = 1, Name = "Ada" },
            new Customer { Id = 2, Name = "Brahim" },
            new Customer { Id = 3, Name = "Chen" },
            new Customer { Id = 4, Name = "Dalia" });
        Orders.AddRange(
            new Order { Id = 10, CustomerId = 1, Code = "A-1", Total = 42.50m },
            new Order { Id = 11, CustomerId = 1, Code = "A-2", Total = 7.00m },
            new Order { Id = 12, CustomerId = 3, Code = "C-1", Total = 19.99m });
        SaveChanges();
    }
}
