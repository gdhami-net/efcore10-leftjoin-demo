# efcore10-leftjoin-demo

Companion repo for the post **"One-line left joins in EF Core 10 with LeftJoin"**
([gdhami.net](https://gdhami.net) — link added when the post is live).

.NET 10 added `LeftJoin` and `RightJoin` to LINQ, and EF Core 10 translates
them. This repo writes the same left join both ways — the old
`GroupJoin` + `SelectMany` + `DefaultIfEmpty` pattern and the new one-call form
— and checks that the SQL, the rows and the nulls all match.

## What it proves

Four customers, three orders. Ada has two orders, Chen has one, Brahim and
Dalia have none, so a left join returns five rows and two of them have no order.

```bash
dotnet run                 # the checks below, all of them
dotnet run -- --sql        # the SQL both forms generate, for SQLite and SQL Server
dotnet run -- --no-probes  # skip the two checks that shell out to dotnet build/run
```

`dotnet run` prints:

```
PASS  System.Linq.Queryable.LeftJoin exists (runtime 10.0.5)
      2 overload(s), 1 of them with 5 parameters
PASS  System.Linq.Queryable.RightJoin exists (runtime 10.0.5)
      2 overload(s), 1 of them with 5 parameters
PASS  GroupJoin + SelectMany + DefaultIfEmpty returns the 5 expected rows
      Ada/A-1/42.5 | Ada/A-2/7.0 | Brahim/null/null | Chen/C-1/19.99 | Dalia/null/null
PASS  LeftJoin returns the same 5 rows, same nulls
      Ada/A-1/42.5 | Ada/A-2/7.0 | Brahim/null/null | Chen/C-1/19.99 | Dalia/null/null
PASS  the 2 customers with no orders come back with a null code and a null total
      Brahim/null/null | Dalia/null/null
PASS  SQLite: both forms produce byte-identical SQL
      SELECT "c"."Name", "o"."Code", "o"."Total"
PASS  SQLite: and that SQL is a LEFT JOIN
      SELECT "c"."Name", "o"."Code", "o"."Total"
PASS  SQL Server: both forms produce byte-identical SQL
      SELECT [c].[Name], [o].[Code], [o].[Total]
PASS  SQL Server: and that SQL is a LEFT JOIN
      SELECT [c].[Name], [o].[Code], [o].[Total]
PASS  the query-syntax join and its method-syntax equivalent produce the same SQL
PASS  walking the Orders navigation gives the same 5 rows
      Ada/A-1/42.5 | Ada/A-2/7.0 | Brahim/null/null | Chen/C-1/19.99 | Dalia/null/null
PASS  and its SQL is the same string as LeftJoin's
      LEFT JOIN "Orders" AS "o" ON "c"."Id" = "o"."CustomerId"
PASS  projecting the order entity gives null for the 2 customers with no orders
      Ada/A-1 | Ada/A-2 | Brahim/null | Chen/C-1 | Dalia/null
PASS  the query-syntax form returns those same 2 nulls, with no nullable type to warn you
      Ada/A-1 | Ada/A-2 | Brahim/null | Chen/C-1 | Dalia/null
PASS  projecting o.Total as a plain decimal throws InvalidOperationException: Nullable object must have a value.
      InvalidOperationException: Nullable object must have a value.
PASS  RightJoin over the same two tables returns the same rows as LeftJoin
      Ada/A-1/42.5 | Ada/A-2/7.0 | Brahim/null/null | Chen/C-1/19.99 | Dalia/null/null
PASS  RightJoin translates to a RIGHT JOIN in SQL
      SELECT "c"."Name", "o"."Code", "o"."Total"
PASS  in the nullable probe, exactly 1 CS8602: LeftJoin warns, the query-syntax form does not
      nullable-probe\Probe.cs(47,23): warning CS8602: Dereference of a possibly null reference. [nullable-probe\NullableProbe.csproj]
PASS  the same LeftJoin query under EF Core 9 throws 'could not be translated'
      THREW InvalidOperationException: The LINQ expression 'DbSet<Customer>() .LeftJoin( inner: DbSet<Order>(), outerKeySelector: c => c.Id, innerKeySelector: o => o.CustomerId, resultSelector: (c, o) => o.Code)' could not be translated. ...

ALL CHECKS PASSED
```

That is 19 checks. The row lines read `Name/Code/Total`, with `null` printed
where the value is missing.

## The two queries

Both live in `Queries.cs`, with the same projection, so any difference in the
generated SQL would come from the join operators alone.

```csharp
// the pattern EF Core has needed since EF6
from c in db.Customers
join o in db.Orders on c.Id equals o.CustomerId into matches
from o in matches.DefaultIfEmpty()
select new Row(c.Name, o.Code, (decimal?)o.Total);

// .NET 10's operator, translated by EF Core 10
db.Customers.LeftJoin(
    db.Orders,
    c => c.Id,
    o => o.CustomerId,
    (c, o) => new Row(c.Name, o!.Code, (decimal?)o!.Total));
```

`dotnet run -- --sql` prints what each one compiles to. On SQLite, both:

```sql
SELECT "c"."Name", "o"."Code", "o"."Total"
FROM "Customers" AS "c"
LEFT JOIN "Orders" AS "o" ON "c"."Id" = "o"."CustomerId"
```

and on SQL Server, both:

```sql
SELECT [c].[Name], [o].[Code], [o].[Total]
FROM [Customers] AS [c]
LEFT JOIN [Orders] AS [o] ON [c].[Id] = [o].[CustomerId]
```

The checks compare the two `ToQueryString()` results with `==`, per provider.
No connection is opened against SQL Server; `ToQueryString` only needs the
provider to compile the query into its own dialect.

## Edge cases the post leaves out

- **The nullable type is the only C# difference.** `LeftJoin`'s result selector
  is declared `Expression<Func<TOuter, TInner?, TResult>>`, so the right side is
  an `Order?` and dotting into it is CS8602. The query-syntax form types the
  same thing as a plain `Order` and warns about nothing, even though the same
  two rows arrive null. `nullable-probe/` holds both spellings with no null
  check; the build produces exactly one warning, on the `LeftJoin` one.
- **A non-nullable projection throws, it does not default.** Projecting
  `o!.Total` as a plain `decimal` gives
  `InvalidOperationException: Nullable object must have a value.` on the first
  row with no order. Project `(decimal?)o!.Total`, or keep the order entity and
  let it be null — the checks cover both.
- **You need both halves.** `efcore9-probe/` is .NET 10 with EF Core 9.0.20. The
  operator exists there (it is a BCL method), but EF Core 9 has no translation
  for it, so the query builds and then throws
  `The LINQ expression ... could not be translated` at run time.
- **A navigation is shorter than either of them.** `Customer` has an `Orders`
  collection, so `from o in c.Orders.DefaultIfEmpty()` needs no join clause and
  no keys. Its SQL comes out as the same string as `LeftJoin`'s. The hand-written
  join is for tables your model does not link.
- **Method syntax and query syntax agree.** `Queries.cs` also holds the explicit
  `GroupJoin(...).SelectMany(...)` spelling, and a check confirms it generates
  the same SQL as the `from ... select` form. Worth knowing before you conclude
  that query syntax does something special.
- **`RightJoin` is the mirror.** Starting from `Orders` and right-joining
  `Customers` returns the same five rows and translates to `RIGHT JOIN`. SQLite
  has supported `RIGHT JOIN` since 3.39; older SQLite builds will not run it.
- **Not covered here:** composite keys, the six-parameter overload that takes an
  `IEqualityComparer<TKey>`, filtering the right side before the join, and
  `LeftJoin` over in-memory sequences through `Enumerable`.

## Versions

| | |
|---|---|
| SDK | .NET 10.0.201 (`dotnet --version`) |
| Runtime | 10.0.5, as the first two check lines print |
| EF Core | 10.0.12 (SQLite and SQL Server providers) |
| EF Core, probe | 9.0.20 (SQLite provider) |
| Database | SQLite, in memory, created by `EnsureCreated()` |
| OS | Windows 11; nothing here is platform-specific, but I have not run it on Linux |

Requires the .NET 10 SDK. MIT license.
