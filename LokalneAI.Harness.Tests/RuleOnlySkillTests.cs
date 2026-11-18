using LokalneAI.Harness.Profiles;

namespace LokalneAI.Harness.Tests;

/// <summary>Umiejętność samych zasad (bez narzędzi, np. „formatowanie”) przy łączeniu z innymi.</summary>
public class RuleOnlySkillTests
{
    [Fact]
    public void Formatowanie_NieJestRola_INieZmieniaZasad()
    {
        var formatting = HarnessProfile.Load(Paths.Profile("formatowanie"));
        Assert.Empty(formatting.Tools);

        var tekla = HarnessProfile.Load(Paths.Profile("tekla"));
        var both = HarnessProfile.Combine([tekla, formatting]);
        Assert.DoesNotContain("# Rola:", both.SystemText);              // jedna rola — bez „kilku ról”
        Assert.StartsWith(tekla.SystemText.Trim(), both.SystemText);
        Assert.Contains("# Zasady odpowiedzi (formatowanie)", both.SystemText);
        Assert.Equal(tekla.Settings.Finish, both.Settings.Finish);
        Assert.Equal(tekla.Settings.Internet, both.Settings.Internet);
        Assert.Equal(tekla.Settings.CloudProviders.OrderBy(c => c), both.Settings.CloudProviders.OrderBy(c => c));

        var cde = HarnessProfile.Load(Paths.Profile("cde"));
        var three = HarnessProfile.Combine([tekla, cde, formatting]);
        Assert.Contains("# Rola: tekla", three.SystemText);
        Assert.Contains("# Rola: cde", three.SystemText);
        Assert.DoesNotContain("# Rola: formatowanie", three.SystemText);
        Assert.False(three.Settings.Internet);                          // cde nadal wyłącza internet
    }
}
