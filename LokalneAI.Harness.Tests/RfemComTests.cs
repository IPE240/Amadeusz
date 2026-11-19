using System.Runtime.InteropServices;
using System.Text.Json;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Rfem;
using LokalneAI.Harness.Rfem.Com;
using LokalneAI.Harness.Tools;
using Xunit.Abstractions;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Polecenia na otwartym RFEM 5 (RF-COM) bez RFEM-a: układ struktur i liczba miejsc w vtable muszą zgadzać się z biblioteką typów v5.7
/// (zmierzone comtypes na RFEM 5.27, x64), decyzje wykonawcy, parsowanie etykiet obciążeń i list z okna wyboru.
/// </summary>
public sealed class RfemComUnitTests
{
    private static readonly HarnessProfile RfemProfile = HarnessProfile.Load(Paths.Profile("rfem"));

    private static ToolCall Call(string tool, object args) => new ToolCall(RfemProfile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    [Theory]
    [InlineData(typeof(Rotation), 24)]
    [InlineData(typeof(Point3D), 24)]
    [InlineData(typeof(Member), 128)]
    [InlineData(typeof(CrossSection), 144)]
    [InlineData(typeof(Material), 120)]
    [InlineData(typeof(Node), 88)]
    [InlineData(typeof(Line), 96)]
    [InlineData(typeof(SetOfMembers), 80)]
    [InlineData(typeof(MemberHinge), 184)]
    [InlineData(typeof(Loading), 8)]
    [InlineData(typeof(LoadCase), 96)]
    [InlineData(typeof(LoadCombination), 64)]
    [InlineData(typeof(ResultCombination), 64)]
    [InlineData(typeof(MemberForces), 88)]
    [InlineData(typeof(MemberDeformations), 80)]
    [InlineData(typeof(NodalSupportForces), 64)]
    public void Struktury_MajaRozmiarZBibliotekiTypow(Type type, int size) => Assert.Equal(size, Marshal.SizeOf(type));

    [Fact]
    public void Struktury_PolaLezaTamGdzieWRfem()
    {
        Assert.Equal(36, (int)Marshal.OffsetOf(typeof(Member), nameof(Member.LineNo)));
        Assert.Equal(48, (int)Marshal.OffsetOf(typeof(Member), nameof(Member.Rotation)));
        Assert.Equal(72, (int)Marshal.OffsetOf(typeof(Member), nameof(Member.StartCrossSectionNo)));
        Assert.Equal(112, (int)Marshal.OffsetOf(typeof(Member), nameof(Member.Length)));
        Assert.Equal(40, (int)Marshal.OffsetOf(typeof(CrossSection), nameof(CrossSection.Description)));
        Assert.Equal(104, (int)Marshal.OffsetOf(typeof(CrossSection), nameof(CrossSection.AxialArea)));
        Assert.Equal(64, (int)Marshal.OffsetOf(typeof(Material), nameof(Material.ElasticityModulus)));
        Assert.Equal(40, (int)Marshal.OffsetOf(typeof(Node), nameof(Node.X)));
        Assert.Equal(48, (int)Marshal.OffsetOf(typeof(Line), nameof(Line.NodeList)));
        Assert.Equal(88, (int)Marshal.OffsetOf(typeof(Line), nameof(Line.ControlPoints)));
        Assert.Equal(96, (int)Marshal.OffsetOf(typeof(MemberHinge), nameof(MemberHinge.TranslationalConstantX)));
        Assert.Equal(56, (int)Marshal.OffsetOf(typeof(LoadCombination), nameof(LoadCombination.Definition)));
        Assert.Equal(72, (int)Marshal.OffsetOf(typeof(LoadCase), nameof(LoadCase.SelfWeightFactor)));
        Assert.Equal(24, (int)Marshal.OffsetOf(typeof(MemberForces), nameof(MemberForces.Location)));
        Assert.Equal(56, (int)Marshal.OffsetOf(typeof(MemberForces), nameof(MemberForces.Moments)));
        Assert.Equal(80, (int)Marshal.OffsetOf(typeof(MemberForces), nameof(MemberForces.CorrespondingLoading)));
        Assert.Equal(56, (int)Marshal.OffsetOf(typeof(MemberDeformations), nameof(MemberDeformations.Rotations)));
        Assert.Equal(56, (int)Marshal.OffsetOf(typeof(NodalSupportForces), nameof(NodalSupportForces.CorrespondingLoading)));
    }

    [Theory]
    [InlineData(typeof(IApplication), 17)]
    [InlineData(typeof(IModel), 34)]
    [InlineData(typeof(IModelData), 241)]
    [InlineData(typeof(IMember), 10)]
    [InlineData(typeof(ICrossSection), 4)]
    [InlineData(typeof(IMaterial), 5)]
    [InlineData(typeof(INode), 4)]
    [InlineData(typeof(ILine), 5)]
    [InlineData(typeof(ICalculation), 37)]
    [InlineData(typeof(IResults), 146)]
    [InlineData(typeof(ILoads), 30)]
    [InlineData(typeof(ILoadCase), 84)]
    [InlineData(typeof(ILoadCombination), 10)]
    [InlineData(typeof(IResultCombination), 8)]
    [InlineData(typeof(IView), 9)]
    [InlineData(typeof(ISetOfMembers), 3)]
    [InlineData(typeof(IMemberHinge), 4)]
    public void Interfejsy_MajaTyleMiejscWVtableCoBibliotekaTypow(Type type, int methods)
    {
        Assert.Equal(methods, type.GetMethods().Length);
        Assert.NotNull(type.GetCustomAttributes(typeof(ComImportAttribute), false).SingleOrDefault());
        Assert.Equal(ComInterfaceType.InterfaceIsIUnknown, ((InterfaceTypeAttribute)type.GetCustomAttributes(typeof(InterfaceTypeAttribute), false).Single()).Value);
    }

    [Fact]
    public void Interfejsy_UzywaneMetodyLezaWeWlasciwychMiejscach()
    {
        // Kolejność deklaracji w interfejsie (= kolejność tokenów metadanych) to kolejność w vtable; miejsca z biblioteki typów v5.7
        // (c_IModelData.txt z comtypes). GetMethods() na .NET Framework nie gwarantuje kolejności deklaracji — stąd sortowanie po tokenie.
        static string At(Type type, int slot) => type.GetMethods().OrderBy(m => m.MetadataToken).ElementAt(slot).Name;
        Assert.Equal("GetMember", At(typeof(IModelData), 177));
        Assert.Equal("GetCrossSection", At(typeof(IModelData), 157));
        Assert.Equal("GetNode", At(typeof(IModelData), 28));
        Assert.Equal("GetLine", At(typeof(IModelData), 33));
        Assert.Equal("GetMaterial", At(typeof(IModelData), 58));
        Assert.Equal("GetModelData", At(typeof(IModel), 23));
        Assert.Equal("GetActiveView", At(typeof(IModel), 27));
        Assert.Equal("GetResultsInFeNodes", At(typeof(ICalculation), 33));
        Assert.Equal("GetMemberInternalForces", At(typeof(IResults), 31));
        Assert.Equal("GetLoadCombination", At(typeof(ILoads), 15));
        Assert.Equal("PickObjects", At(typeof(IView), 0));
    }

    [Fact]
    public void BibliotekaTypow_JesliJestZarejestrowanaToUkladSieZgadza()
    {
        // Na komputerze bez RFEM 5 sprawdzenie mówi o braku biblioteki; z RFEM 5 (v5.7) — układ pasuje. Inna wersja = czytelny BŁĄD, który chcemy zobaczyć.
        var problem = RfemComLayout.Problem();
        Assert.True(problem == null || problem.StartsWith("BŁĄD: na tym komputerze nie ma biblioteki typów", StringComparison.Ordinal), problem);
    }

    [Fact]
    public void Wykonawca_PoleceniaNaOtwartymRfemBezPytaniaIBezSciezki()
    {
        var executor = new RfemExecutor(new RfemOptions { AllowedRoots = new List<string>(), WorkingFolder = Path.GetTempPath() });
        foreach (var tool in RfemExecutor.LiveTools)
        {
            Assert.NotNull(RfemProfile.FindTool(tool));
            Assert.Equal(ConfirmationKind.None, executor.Decide(Call(tool, new { numer = 1 })).Kind);
        }

        Assert.Equal(7, RfemExecutor.LiveTools.Count);
        Assert.Empty(RfemExecutor.LiveTools.Intersect(RfemExecutor.Tools));
    }

    [Theory]
    [InlineData("KO5", 2, 5)]
    [InlineData("co12", 2, 12)]
    [InlineData("PO1", 1, 1)]
    [InlineData("LC 3", 1, 3)]
    [InlineData("KW2", 3, 2)]
    [InlineData("RC-7", 3, 7)]
    [InlineData("17", 2, 17)]
    public void Obciazenie_ZEtykiety(string text, int type, int no)
    {
        Assert.True(RfemLive.TryParseLoading(text, out var loading, out var problem), problem);
        Assert.Equal(type, loading.Type);
        Assert.Equal(no, loading.No);
    }

    [Fact]
    public void Obciazenie_ZleEtykietyIPrzeklad()
    {
        Assert.False(RfemLive.TryParseLoading("wszystkie", out _, out var problem));
        Assert.StartsWith("BŁĄD", problem);
        Assert.Equal("KO5", RfemLive.Label(new Loading { Type = 2, No = 5 }));
        Assert.Equal("1.35*PO1 + 1.35*PO2 + PO21", RfemLive.Translate("1.35*LC1 + 1.35*LC2 + LC21"));
        Assert.Equal("KO1/p lub KO2/p", RfemLive.Translate("CO1/p or CO2/p"));
        Assert.Equal("pręt sztywny", RfemLive.TypeName(2));
        Assert.Equal("kratownicowy (tylko N)", RfemLive.TypeName(5));
    }

    [Fact]
    public void ListaZOknaWyboru()
    {
        Assert.Equal(new[] { 1, 5, 7 }, RfemLive.ParseList("1,5,7"));
        Assert.Equal(new[] { 1, 2, 3, 8 }, RfemLive.ParseList("1-3;8"));
        Assert.Equal(new[] { 28, 29 }, RfemLive.ParseList("28 29 28"));
        Assert.Empty(RfemLive.ParseList(null));
        Assert.Empty(RfemLive.ParseList(" "));
    }

    [Fact]
    public void Stan_RfemNaTymKomputerze()
    {
        var status = RfemExecutor.LiveStatus();
        Assert.Contains("RFEM 5", status);
        Assert.Contains("rfem_wskaz", status);
    }
}

/// <summary>
/// Prawdziwy, otwarty RFEM 5 przez RF-COM: tylko gdy zmienna <c>LOKALNEAI_RFEM_COM=1</c> i RFEM 5 działa (test zajmuje licencję RF-COM
/// na ułamek sekundy i tylko czyta). Bez zmiennej przechodzi bez sprawdzania.
/// </summary>
public sealed class RfemComLiveTests
{
    private readonly ITestOutputHelper _output;

    public RfemComLiveTests(ITestOutputHelper output) => _output = output;

    private static bool Enabled => Environment.GetEnvironmentVariable("LOKALNEAI_RFEM_COM") == "1" && RfemComSession.RunningWindowTitle() != null;

    private static readonly HarnessProfile RfemProfile = HarnessProfile.Load(Paths.Profile("rfem"));

    private static ToolCall Call(string tool, object args) => new ToolCall(RfemProfile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    [Fact]
    public async Task OtwartyModel_PodsumowaniePretPrzekrojePrzypadki()
    {
        if (!Enabled)
        {
            return;
        }

        var executor = new RfemExecutor(new RfemOptions { AllowedRoots = new List<string>(), WorkingFolder = Path.GetTempPath(), MaxOutputChars = 20000 });
        var summary = await executor.ExecuteAsync(Call("rfem_otwarty", new { }), CancellationToken.None);
        _output.WriteLine(summary.Text);
        Assert.True(summary.Success, summary.Text);
        Assert.StartsWith("OK RFEM 5", summary.Text);
        Assert.Contains("prętów", summary.Text);
        Assert.Contains("Obciążenia:", summary.Text);

        var firstMember = int.Parse(System.Text.RegularExpressions.Regex.Match(summary.Text, @"numery prętów (\d+)").Groups[1].Value);
        var member = await executor.ExecuteAsync(Call("rfem_otwarty_pret", new { numer = firstMember }), CancellationToken.None);
        _output.WriteLine(member.Text);
        Assert.True(member.Success, member.Text);
        Assert.Contains("długość", member.Text);
        Assert.Contains("Przekrój", member.Text);

        var missing = await executor.ExecuteAsync(Call("rfem_otwarty_pret", new { numer = 999999 }), CancellationToken.None);
        Assert.False(missing.Success);
        Assert.Contains("nie ma pręta 999999", missing.Text);

        var sections = await executor.ExecuteAsync(Call("rfem_otwarty_przekroje", new { }), CancellationToken.None);
        _output.WriteLine(sections.Text);
        Assert.True(sections.Success, sections.Text);
        Assert.Contains("A = ", sections.Text);

        var loadings = await executor.ExecuteAsync(Call("rfem_otwarty_przypadki", new { }), CancellationToken.None);
        _output.WriteLine(loadings.Text);
        Assert.True(loadings.Success, loadings.Text);
        Assert.Contains("PO1", loadings.Text);
    }

    [Fact]
    public async Task OtwartyModel_SilyIReakcje()
    {
        if (!Enabled)
        {
            return;
        }

        var executor = new RfemExecutor(new RfemOptions { AllowedRoots = new List<string>(), WorkingFolder = Path.GetTempPath(), MaxOutputChars = 20000, MaxLoadingsScanned = 30 });
        var summary = await executor.ExecuteAsync(Call("rfem_otwarty", new { }), CancellationToken.None);
        var firstMember = int.Parse(System.Text.RegularExpressions.Regex.Match(summary.Text, @"numery prętów (\d+)").Groups[1].Value);
        var solved = System.Text.RegularExpressions.Regex.Match(summary.Text, @"np\. KO(\d+)");
        if (!solved.Success)
        {
            _output.WriteLine("model bez wyników — siły pominięte");
            return;
        }

        var combination = "KO" + solved.Groups[1].Value;
        var forces = await executor.ExecuteAsync(Call("rfem_otwarty_sily", new { numer = firstMember, obciazenie = combination }), CancellationToken.None);
        _output.WriteLine(forces.Text);
        Assert.True(forces.Success, forces.Text);
        Assert.Contains("x | N | Vy | Vz | Mt | My | Mz", forces.Text);
        Assert.Contains("Ekstrema:", forces.Text);

        var extremes = await executor.ExecuteAsync(Call("rfem_otwarty_sily", new { numer = firstMember }), CancellationToken.None);
        _output.WriteLine(extremes.Text);
        Assert.True(extremes.Success, extremes.Text);
        Assert.Contains("N: min", extremes.Text);

        var displacements = await executor.ExecuteAsync(Call("rfem_otwarty_sily", new { numer = firstMember, obciazenie = combination, co = "przemieszczenia" }), CancellationToken.None);
        _output.WriteLine(displacements.Text);
        Assert.True(displacements.Success, displacements.Text);
        Assert.Contains("wypadkowe", displacements.Text);

        var unknown = await executor.ExecuteAsync(Call("rfem_otwarty_sily", new { numer = firstMember, obciazenie = "KO999999" }), CancellationToken.None);
        Assert.False(unknown.Success);
        _output.WriteLine(unknown.Text);

        var reactions = await executor.ExecuteAsync(Call("rfem_otwarty_reakcje", new { wezel = 1, obciazenie = combination }), CancellationToken.None);
        _output.WriteLine(reactions.Text);   // węzeł 1 może nie być podparty — ważne, że odpowiedź jest OK albo czytelny BŁĄD
        Assert.True(reactions.Text.StartsWith("OK") || reactions.Text.StartsWith("BŁĄD"), reactions.Text);
    }
}
