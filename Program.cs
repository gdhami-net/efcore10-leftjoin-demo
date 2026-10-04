using System.Diagnostics;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EfCore10LeftJoinDemo;

internal static class Program
{
    private static int _failures;

    private static int Main(string[] args)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var sqlite = new ShopDb(new DbContextOptionsBuilder<ShopDb>()
            .UseSqlite(connection).Options);
        sqlite.Seed();

        // No connection is opened against SQL Server. ToQueryString only needs
        // the provider to compile the query into its own dialect.
        using var sqlserver = new ShopDb(new DbContextOptionsBuilder<ShopDb>()
            .UseSqlServer("Server=none;Database=none;Trusted_Connection=True").Options);

        if (args.Contains("--sql"))
        {
            PrintSql("SQLite", sqlite);
            PrintSql("SQL Server", sqlserver);
            return 0;
        }

        OperatorsExist();
        SameRows(sqlite);
        SameSql("SQLite", sqlite);
        SameSql("SQL Server", sqlserver);
        MethodSyntaxMatchesQuerySyntax(sqlite);
        TheWholeOrderComesBackNull(sqlite);
        PlainDecimalProjectionThrows(sqlite);
        RightJoinMirrorsLeftJoin(sqlite);
        if (!args.Contains("--no-probes"))
        {
            NullableWarningOnTheRightSide();
            EfCore9CannotTranslateIt();
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "ALL CHECKS PASSED" : $"{_failures} CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    // ---- the checks ---------------------------------------------------------

    /// <summary>LeftJoin and RightJoin are on Queryable itself, not an EF Core extension.</summary>
    private static void OperatorsExist()
    {
        foreach (var name in new[] { "LeftJoin", "RightJoin" })
        {
            var overloads = typeof(Queryable)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == name)
                .ToList();
            var five = overloads.Count(m => m.GetParameters().Length == 5);
            Check($"System.Linq.Queryable.{name} exists (runtime {Environment.Version})",
                five == 1,
                $"{overloads.Count} overload(s), {five} of them with 5 parameters");
        }
    }

    /// <summary>The rows and the nulls are the same, whichever way the join is written.</summary>
    private static void SameRows(ShopDb db)
    {
        var expected = new[]
        {
            new Row("Ada", "A-1", 42.50m),
            new Row("Ada", "A-2", 7.00m),
            new Row("Brahim", null, null),
            new Row("Chen", "C-1", 19.99m),
            new Row("Dalia", null, null),
        };

        var groupJoin = Sorted(Queries.GroupJoinWay(db).ToList());
        var leftJoin = Sorted(Queries.LeftJoinWay(db).ToList());

        Check("GroupJoin + SelectMany + DefaultIfEmpty returns the 5 expected rows",
            groupJoin.SequenceEqual(expected), Show(groupJoin));
        Check("LeftJoin returns the same 5 rows, same nulls",
            leftJoin.SequenceEqual(groupJoin), Show(leftJoin));
        Check("the 2 customers with no orders come back with a null code and a null total",
            leftJoin.Count(r => r is { Code: null, Total: null }) == 2,
            Show(leftJoin.Where(r => r.Code is null).ToList()));
    }

    /// <summary>Both forms compile to the same SQL, on both providers.</summary>
    private static void SameSql(string provider, ShopDb db)
    {
        var groupJoin = Queries.GroupJoinWay(db).ToQueryString();
        var leftJoin = Queries.LeftJoinWay(db).ToQueryString();
        Check($"{provider}: both forms produce byte-identical SQL",
            groupJoin == leftJoin,
            groupJoin == leftJoin ? First(leftJoin) : $"GroupJoin:\n{groupJoin}\n\nLeftJoin:\n{leftJoin}");
        Check($"{provider}: and that SQL is a LEFT JOIN",
            leftJoin.Contains("LEFT JOIN", StringComparison.Ordinal), First(leftJoin));
    }

    private static void MethodSyntaxMatchesQuerySyntax(ShopDb db)
    {
        var query = Queries.GroupJoinWay(db).ToQueryString();
        var method = Queries.GroupJoinWayMethodSyntax(db).ToQueryString();
        Check("the query-syntax join and its method-syntax equivalent produce the same SQL",
            query == method, query == method ? "" : $"query:\n{query}\n\nmethod:\n{method}");
    }

    /// <summary>Projecting the order entity itself: EF leaves it null when there is no match.</summary>
    private static void TheWholeOrderComesBackNull(ShopDb db)
    {
        var rows = Queries.LeftJoinWholeOrder(db).ToList();
        Check("projecting the order entity gives null for the 2 customers with no orders",
            rows.Count == 5 && rows.Count(r => r.Order is null) == 2,
            string.Join(" | ", rows.Select(r => $"{r.Name}/{r.Order?.Code ?? "null"}")));

        // The query-syntax range variable is typed as a non-nullable Order, yet
        // the same two rows arrive null: the old form's type was never honest.
        var old = Queries.GroupJoinWholeOrder(db).ToList();
        Check("the query-syntax form returns those same 2 nulls, with no nullable type to warn you",
            old.Count == 5 && old.Count(r => r.Order is null) == 2,
            string.Join(" | ", old.Select(r => $"{r.Name}/{r.Order?.Code ?? "null"}")));
    }

    /// <summary>
    /// A plain decimal projection has nowhere to put the missing row's total.
    /// The message is read off the exception, not predicted.
    /// </summary>
    private static void PlainDecimalProjectionThrows(ShopDb db)
    {
        string outcome;
        try
        {
            var totals = Queries.TotalsWithoutTheNullableCast(db).ToList();
            outcome = "no exception; returned " + string.Join(", ", totals);
        }
        catch (InvalidOperationException ex)
        {
            outcome = $"{ex.GetType().Name}: {OneLine(ex.Message)}";
        }
        Check("projecting o.Total as a plain decimal throws InvalidOperationException: Nullable object must have a value.",
            outcome == "InvalidOperationException: Nullable object must have a value.", outcome);
    }

    private static void RightJoinMirrorsLeftJoin(ShopDb db)
    {
        var left = Sorted(Queries.LeftJoinWay(db).ToList());
        var right = Sorted(Queries.RightJoinWay(db).ToList());
        var sql = Queries.RightJoinWay(db).ToQueryString();
        Check("RightJoin over the same two tables returns the same rows as LeftJoin",
            right.SequenceEqual(left), Show(right));
        Check("RightJoin translates to a RIGHT JOIN in SQL",
            sql.Contains("RIGHT JOIN", StringComparison.Ordinal), First(sql));
    }

    /// <summary>
    /// The right side arrives as a nullable reference, so dotting into it
    /// without a null check is CS8602. The probe project holds both spellings
    /// of the join with no null check; exactly one of them warns.
    /// </summary>
    private static void NullableWarningOnTheRightSide()
    {
        // --no-incremental: an up-to-date build prints no warnings at all.
        var output = Run("dotnet", $"build \"{Probe("nullable-probe")}\" --no-incremental --nologo -v:q");
        // MSBuild repeats each warning in its summary, so distinct lines are counted.
        var warnings = output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Contains("warning CS", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var ok = warnings.Count == 1 && warnings[0].Contains("CS8602", StringComparison.Ordinal);
        Check("in the nullable probe, exactly 1 CS8602: LeftJoin warns, the query-syntax form does not",
            ok, Relative(OneLine(ok ? warnings[0] : output)));
    }

    /// <summary>
    /// The operator is .NET 10; the translation is EF Core 10. EF Core 9 on the
    /// same runtime sees a method it has no translation for.
    /// </summary>
    private static void EfCore9CannotTranslateIt()
    {
        var output = Run("dotnet", $"run --project \"{Probe("efcore9-probe")}\" --nologo");
        Check("the same LeftJoin query under EF Core 9 throws 'could not be translated'",
            output.Contains("THREW InvalidOperationException", StringComparison.Ordinal)
            && output.Contains("could not be translated", StringComparison.Ordinal),
            OneLine(output.Split('\n').FirstOrDefault(l => l.StartsWith("THREW", StringComparison.Ordinal)) ?? output));
    }

    // ---- plumbing -----------------------------------------------------------

    private static void PrintSql(string provider, ShopDb db)
    {
        Console.WriteLine($"==== {provider} ====");
        Console.WriteLine("-- GroupJoin + SelectMany + DefaultIfEmpty");
        Console.WriteLine(Queries.GroupJoinWay(db).ToQueryString());
        Console.WriteLine();
        Console.WriteLine("-- LeftJoin");
        Console.WriteLine(Queries.LeftJoinWay(db).ToQueryString());
        Console.WriteLine();
        Console.WriteLine("-- RightJoin");
        Console.WriteLine(Queries.RightJoinWay(db).ToQueryString());
        Console.WriteLine();
    }

    /// <summary>Walks up from the running binary to the repository root, then into a probe folder.</summary>
    private static string Probe(string folder)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "EfCore10LeftJoinDemo.csproj")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("could not find the repository root");
        return Path.Combine(dir.FullName, folder);
    }

    private static string Run(string file, string arguments)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private static List<Row> Sorted(List<Row> rows) =>
        rows.OrderBy(r => r.Name, StringComparer.Ordinal)
            .ThenBy(r => r.Code ?? "", StringComparer.Ordinal)
            .ToList();

    private static string Show(List<Row> rows) =>
        string.Join(" | ", rows.Select(r => $"{r.Name}/{r.Code ?? "null"}/{r.Total?.ToString() ?? "null"}"));

    private static string First(string sql) => OneLine(sql.Split('\n').FirstOrDefault() ?? "");

    private static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();

    /// <summary>Strips the checkout path out of build output so the log is the same on any machine.</summary>
    private static string Relative(string s)
    {
        var root = Path.GetDirectoryName(Probe("x"))!;
        return s.Replace(root + Path.DirectorySeparatorChar, "", StringComparison.OrdinalIgnoreCase)
                .Replace(root, "", StringComparison.OrdinalIgnoreCase);
    }

    private static void Check(string name, bool ok, string detail)
    {
        if (!ok) _failures++;
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (detail.Length > 0) Console.WriteLine($"      {detail}");
    }
}
