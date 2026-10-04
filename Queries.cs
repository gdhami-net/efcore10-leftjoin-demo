namespace EfCore10LeftJoinDemo;

/// <summary>
/// The same left join written both ways. The projection is deliberately
/// identical in both, so any difference in the generated SQL comes from the
/// join operators and nothing else.
/// </summary>
internal static class Queries
{
    /// <summary>The pattern EF Core has needed since EF6: GroupJoin, then SelectMany over DefaultIfEmpty.</summary>
    public static IQueryable<Row> GroupJoinWay(ShopDb db) =>
        from c in db.Customers
        join o in db.Orders on c.Id equals o.CustomerId into matches
        from o in matches.DefaultIfEmpty()
        select new Row(c.Name, o.Code, (decimal?)o.Total);

    /// <summary>The same thing in method syntax, which is what the query form above compiles to.</summary>
    public static IQueryable<Row> GroupJoinWayMethodSyntax(ShopDb db) =>
        db.Customers
          .GroupJoin(db.Orders, c => c.Id, o => o.CustomerId, (c, matches) => new { c, matches })
          .SelectMany(x => x.matches.DefaultIfEmpty(), (x, o) => new Row(x.c.Name, o!.Code, (decimal?)o!.Total));

    /// <summary>.NET 10's LeftJoin operator, translated by EF Core 10.</summary>
    public static IQueryable<Row> LeftJoinWay(ShopDb db) =>
        db.Customers.LeftJoin(
            db.Orders,
            c => c.Id,
            o => o.CustomerId,
            (c, o) => new Row(c.Name, o!.Code, (decimal?)o!.Total));

    /// <summary>
    /// The shortest form when the model already links the two tables: walk the
    /// navigation, no join clause and no keys. Older than .NET 10.
    /// </summary>
    public static IQueryable<Row> NavigationWay(ShopDb db) =>
        from c in db.Customers
        from o in c.Orders.DefaultIfEmpty()
        select new Row(c.Name, o.Code, (decimal?)o.Total);

    /// <summary>
    /// The same join, projecting the order entity itself instead of its columns.
    /// EF Core leaves the whole order null when the customer has none.
    /// </summary>
    public static IQueryable<CustomerOrder> GroupJoinWholeOrder(ShopDb db) =>
        from c in db.Customers
        join o in db.Orders on c.Id equals o.CustomerId into matches
        from o in matches.DefaultIfEmpty()
        select new CustomerOrder(c.Name, o);

    /// <summary>
    /// The same thing through LeftJoin. The difference is only in the C# type:
    /// here the compiler knows the order can be absent, above it does not.
    /// </summary>
    public static IQueryable<CustomerOrder> LeftJoinWholeOrder(ShopDb db) =>
        db.Customers.LeftJoin(
            db.Orders,
            c => c.Id,
            o => o.CustomerId,
            (c, o) => new CustomerOrder(c.Name, o));

    /// <summary>
    /// The same join, but the money column is projected as a plain decimal
    /// instead of decimal?. Customers with no order have no total to return.
    /// </summary>
    public static IQueryable<decimal> TotalsWithoutTheNullableCast(ShopDb db) =>
        db.Customers.LeftJoin(
            db.Orders,
            c => c.Id,
            o => o.CustomerId,
            (c, o) => o!.Total);

    /// <summary>
    /// RightJoin, the other operator .NET 10 added. Here the source is Orders,
    /// so it is the source side that can be missing and needs the null check.
    /// </summary>
    public static IQueryable<Row> RightJoinWay(ShopDb db) =>
        db.Orders.RightJoin(
            db.Customers,
            o => o.CustomerId,
            c => c.Id,
            (o, c) => new Row(c.Name, o!.Code, (decimal?)o!.Total));
}
