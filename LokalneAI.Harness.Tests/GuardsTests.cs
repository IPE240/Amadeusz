using LokalneAI.Harness.Tools;
using LokalneAI.Harness.Validation;

namespace LokalneAI.Harness.Tests;

public class SqlGuardTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select sum(h.hours_counted) from timesheet.v_my_hours h where h.person_id = '{{ja}}';")]
    [InlineData("WITH x AS (SELECT 1 AS a) SELECT a FROM x")]
    [InlineData("SELECT 'delete; drop table x' AS tekst")]
    [InlineData("SELECT \"nazwa kolumny\" FROM t -- komentarz z insert\n")]
    [InlineData("(SELECT 1) UNION (SELECT 2)")]
    public void Przepuszcza_ZapytaniaDoOdczytu(string sql) => Assert.Null(SqlGuard.Check(sql));

    [Theory]
    [InlineData("DELETE FROM project.project", "tylko zapytania SELECT")]
    [InlineData("SELECT 1; SELECT 2", "jedno zapytanie")]
    [InlineData("SELECT * INTO nowa FROM project.v_project", "INTO")]
    [InlineData("WITH d AS (DELETE FROM t RETURNING *) SELECT * FROM d", "DELETE")]
    [InlineData("SELECT pg_sleep(100)", "pg_sleep")]
    [InlineData("SELECT * FROM t FOR UPDATE", "UPDATE")]
    [InlineData("UPDATE t SET a = 1", "SELECT")]
    [InlineData("SELECT set_config('default_transaction_read_only','off',false)", "SET")]
    [InlineData("SELECT 'niezamknięty", "Niezamknięty")]
    public void Odrzuca_ZapytaniaZmieniajace(string sql, string fragment)
    {
        var problem = SqlGuard.Check(sql);
        Assert.NotNull(problem);
        Assert.Contains(fragment, problem, StringComparison.OrdinalIgnoreCase);
    }
}

public class NumberGuardTests
{
    [Fact]
    public void Extract_CzytaPolskiZapisLiczb()
    {
        var numbers = NumberGuard.Extract("Razem 1 234,5 h, czyli 845,00 albo 62.75; profil HEB300 i stal S355 się nie liczą.");
        Assert.Equal(new[] { 1234.5m, 845.00m, 62.75m }, numbers);
    }

    [Fact]
    public void FindUnsupported_WskazujeLiczbyZGlowy()
    {
        var sources = new[] { "godziny_liczone\n845.00\n", "Ile przepracowałem w 2025?" };

        Assert.Empty(NumberGuard.FindUnsupported("W 2025 przepracowałeś 845 h (845,00).", sources));
        Assert.Equal(new[] { 62.25m }, NumberGuard.FindUnsupported("Suma to 62,25 h.", sources));
    }
}

public class CalculatorTests
{
    [Theory]
    [InlineData("7,5 + 8 + 6,5 + 4,25 + 8 + 7,75 + 3,5 + 9 + 2,25 + 6", "62,75")]
    [InlineData("suma(7,5; 8; 6,5)", "22")]
    [InlineData("(1234,5 + 987,25) / 2", "1110,875")]
    [InlineData("srednia(1234.5, 987.25)", "1110,875")]
    [InlineData("zaokraglij(845/8; 2)", "105,63")]
    [InlineData("2 + 3 * 4 ^ 2", "50")]
    [InlineData("-(2 - 5) * 1.5", "4,5")]
    [InlineData("max(3; 9; 4) - min(3; 9; 4)", "6")]
    public void Licz_Dokladnie(string expression, string expected)
        => Assert.Equal(expected, Calculator.Format(Calculator.Evaluate(expression)));

    [Theory]
    [InlineData("1 / 0")]
    [InlineData("2 +")]
    [InlineData("foo(1)")]
    [InlineData("3 $ 4")]
    public void BledneWyrazenie_DajeWyjatek(string expression)
        => Assert.ThrowsAny<Exception>(() => Calculator.Evaluate(expression));
}

public class KnowledgeIndexTests
{
    [Fact]
    public void Search_ZnajdujeFragmentBezPolskichZnakow()
    {
        var index = new KnowledgeIndex();
        index.Add("slupy.md", "# Podstawa słupa\nBlacha podstawy, kotwy fundamentowe i ostroga przenosząca ścinanie.\n# Głowica słupa\nGłowica jest na górze słupa.");
        index.Add("stal.md", "# Gatunki stali\nS235JR, S355J2 — liczone osobno w tonach.");

        var hits = index.Search("ostroga sciananie podstawa slupa");

        Assert.NotEmpty(hits);
        Assert.Contains("Podstawa słupa", hits[0]);
        Assert.Contains("Głowica", index.Search("glowica")[0]);
    }
}
