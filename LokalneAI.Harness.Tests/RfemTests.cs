using System.IO.Compression;
using System.Text;
using System.Text.Json;
using LokalneAI.Harness.Profiles;
using LokalneAI.Harness.Rfem;
using LokalneAI.Harness.Tools;
using Xunit.Abstractions;

namespace LokalneAI.Harness.Tests;

/// <summary>Buduje fragmenty pliku .rf5 w układzie rozpoznanym z modeli biura — testy nie potrzebują plików klientów.</summary>
internal static class Rf5Bytes
{
    private static readonly byte[] Key = Encoding.ASCII.GetBytes("\x0fOwnerParamGroup");
    private static readonly byte[] Tail14 = { 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] Tail9 = { 0, 0, 0, 0, 4, 0, 0, 0, 0 };

    public static byte[] Numbers(params int[] values)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write((uint)(12 + 4 * values.Length));
        w.Write(new byte[] { 1, 1, 7 });
        w.Write(Encoding.ASCII.GetBytes("Numbers"));
        w.Write((byte)0);
        w.Write((byte)(4 * values.Length));
        foreach (var v in values)
        {
            w.Write(v);
        }

        return m.ToArray();
    }

    private static byte[] Record(int no, int ones, byte[] body)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(no);
        w.Write(new byte[8]);
        w.Write(Enumerable.Repeat((byte)1, ones).ToArray());
        w.Write(Tail14);
        w.Write(Key);
        w.Write(body);
        w.Write(new byte[8]);
        return m.ToArray();
    }

    public static byte[] Node(int no, double x, double y, double z)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(new byte[8]);
        w.Write(x);
        w.Write(y);
        w.Write(z);
        w.Write((byte)8);
        w.Write(Encoding.ASCII.GetBytes("Standard"));
        return Record(no, 8, m.ToArray());
    }

    public static byte[] Line(int no, params int[] nodes)
    {
        var m = new MemoryStream();
        m.Write(new byte[10], 0, 10);
        var list = Numbers(nodes);
        m.Write(list, 0, list.Length);
        return Record(no, 4, m.ToArray());
    }

    public static byte[] Member(int no, int line, string type, float rotation, int[] sections, int[] second)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write((byte)0);
        w.Write(rotation);
        w.Write(new byte[5]);
        w.Write(line);
        w.Write((byte)type.Length);
        w.Write(Encoding.ASCII.GetBytes(type));
        w.Write(Numbers(sections));
        w.Write(Numbers(second));
        return Record(no, 17, m.ToArray());
    }

    public static byte[] Section(int no, string description, string textId, int material, double it, double iy, double iz, double area, float d1, float d2)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(no);
        w.Write(new byte[16]);
        w.Write(Enumerable.Repeat((byte)1, 14).ToArray());
        w.Write(Tail9);
        w.Write(it);
        w.Write(1f);
        w.Write(iy);
        w.Write(iz);
        w.Write(area);
        w.Write(area / 2);
        w.Write(area / 3);
        for (var i = 0; i < 5; i++)
        {
            w.Write(1f);
        }

        w.Write(new byte[8]);
        w.Write(material);
        var desc = Cp1250(description);
        w.Write((byte)desc.Length);
        w.Write(desc);
        w.Write(d1);
        w.Write(d2);
        w.Write(0xFFFFFFFFu);
        w.Write(new byte[] { 0x0c, 0, 0, 0, 1, 1, 7 });
        w.Write(Encoding.ASCII.GetBytes("Numbers"));
        w.Write(new byte[] { 0, 0, 1, 0, 0, 0 });
        w.Write((byte)textId.Length);
        w.Write(Encoding.ASCII.GetBytes(textId));
        w.Write(new byte[8]);
        w.Write(new byte[] { 1, 0, 0, 0, 0 });
        return m.ToArray();
    }

    public static byte[] Material(int no, string description, string kind, string norm, double e, double g, float gamma)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(no);
        w.Write(new byte[8]);
        w.Write(Enumerable.Repeat((byte)1, 8).ToArray());
        w.Write(new byte[] { 0, 1, 0, 0, 0, 0, 4, 0, 0, 0, 0 });
        var desc = Cp1250(description);
        w.Write((byte)desc.Length);
        w.Write(desc);
        w.Write(new byte[2]);
        var id = Encoding.ASCII.GetBytes("NameID|" + description + "@TypeID|" + kind + "@NormID|" + norm.Split(':')[0]);
        w.Write((byte)id.Length);
        w.Write(id);
        w.Write((byte)norm.Length);
        w.Write(Encoding.ASCII.GetBytes(norm));
        w.Write(e);
        w.Write(g);
        w.Write(1f);
        w.Write(gamma);
        w.Write(1.2e-5f);
        w.Write(new byte[8]);
        return m.ToArray();
    }

    public static byte[] LoadCase(int no, string description)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(no);
        w.Write(1);
        w.Write(0);
        var desc = Cp1250(description);
        w.Write((byte)desc.Length);
        w.Write(desc);
        w.Write(1f);
        w.Write(new byte[8]);
        return m.ToArray();
    }

    public static byte[] Combination(int no, string description, string expression)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(no);
        w.Write(1);
        var desc = Cp1250(description);
        w.Write((byte)desc.Length);
        w.Write(desc);
        w.Write((byte)0);
        w.Write((byte)expression.Length);
        w.Write(Encoding.ASCII.GetBytes(expression));
        w.Write(new byte[3]);
        w.Write((byte)8);
        w.Write(Encoding.ASCII.GetBytes("Eurocode"));
        w.Write(new byte[4]);
        return m.ToArray();
    }

    public static byte[] ResultCombination(int no, string description, string expression)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(new byte[4]);
        w.Write(no);
        var desc = Cp1250(description);
        w.Write((byte)desc.Length);
        w.Write(desc);
        w.Write((byte)expression.Length);
        w.Write(Encoding.ASCII.GetBytes(expression));
        w.Write(new byte[8]);
        return m.ToArray();
    }

    /// <summary>Plik sił .HXN: jeden pręt o <paramref name="points"/> punktach z podanymi siłami (N, Vy, Vz, Mt, My, Mz) i przemieszczeniami.</summary>
    public static byte[] Forces(IList<float[]> forces, IList<double> xs, IList<float[]> displacements = null)
    {
        var members = 1;
        var points = forces.Count;
        var header = new long[54];
        header[0] = 256;
        header[29] = members;
        header[31] = 2048;
        header[34] = points;
        header[36] = 2048 + 8 * members;
        header[39] = 6;
        header[40] = header[36] + 12 * points;
        header[41] = 24 * points;
        header[45] = 6;
        header[46] = header[40] + 24 * points;
        header[47] = 24 * points;
        var total = header[46] + 24 * points;
        var raw = new byte[total];
        for (var i = 0; i < header.Length; i++)
        {
            BitConverter.GetBytes(header[i]).CopyTo(raw, 8 * i);
        }

        BitConverter.GetBytes(points).CopyTo(raw, 2048);
        BitConverter.GetBytes(0).CopyTo(raw, 2052);
        for (var k = 0; k < points; k++)
        {
            BitConverter.GetBytes(xs[k]).CopyTo(raw, (int)header[36] + 12 * k);
            BitConverter.GetBytes(k).CopyTo(raw, (int)header[36] + 12 * k + 8);
            for (var c = 0; c < 6; c++)
            {
                BitConverter.GetBytes(forces[k][c]).CopyTo(raw, (int)header[40] + 24 * k + 4 * c);
                BitConverter.GetBytes(displacements != null ? displacements[k][c] : 0f).CopyTo(raw, (int)header[46] + 24 * k + 4 * c);
            }
        }

        return raw;
    }

    public static byte[] Concat(params byte[][] parts)
    {
        var m = new MemoryStream();
        foreach (var part in parts)
        {
            m.Write(new byte[16], 0, 16);
            m.Write(part, 0, part.Length);
        }

        return m.ToArray();
    }

    /// <summary>Mały model: 3 węzły, 2 linie, pręt 1 (HEA 160, 3 m), pręt 2 sztywny; 2 przekroje, 2 materiały, 2 przypadki, 2 kombinacje, 1 kombinacja wyników.</summary>
    public static byte[] SampleModel()
        => Concat(
            Node(1, 0, 0, 0), Node(2, 3, 0, 0), Node(3, 3, 0, 4),
            Line(1, 1, 2), Line(2, 2, 3),
            Member(1, 1, "Beam", 0f, new[] { 1 }, new[] { 0, 0 }),
            Member(2, 2, "BeamRigid", 1.5708f, new int[0], new[] { 0, 0 }),
            Section(1, "HE A 160 | DIN 1025-3:1994", "HE-A 160", 2, 1.219e-7, 1.673e-5, 6.156e-6, 3.877e-3, 0.16f, 0.152f),
            Section(2, "Rura 219.1/6.3", "Rohr 219.1/6.3", 2, 1.1e-5, 5.6e-6, 5.6e-6, 4.2e-3, 0.2191f, 0.2191f),
            Material(1, "Beton C30/37", "CONCRETE", "EN 1992-1-1:2004/A1:2014", 3.3e10, 1.375e10, 25000f),
            Material(2, "Stal S 355", "STEEL", "EN 1993-1-1:2005-05", 2.1e11, 8.0769e10, 78500f),
            LoadCase(1, "Ciężar własny"), LoadCase(11, "Obciążenia zmienne - podesty"),
            Combination(1, "", "1.35*LF1 + 1.5*LF11"), Combination(2, "SGU", "LF1 + LF11"),
            ResultCombination(1, "SGN - Wszystko", "LK1 o LK2"));

    /// <summary>Cały plik .rf5 (ZIP) z modelem, siatką i jednym zestawem wyników (KO1) dla pręta 1.</summary>
    public static byte[] SampleFile()
    {
        var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, true))
        {
            Add(archive, "RFEM Input Data.bin", SampleModel());
            Add(archive, "History.bin", Encoding.ASCII.GetBytes("xx\u0001\u0001\u0007History\u0004RFEM\u000e5.27.01.160034\u0000abc"));
            Add(archive, "Mesh/.MTO", Encoding.ASCII.GetBytes("         1         1         1         1         2         B\n         0         0         0         0         0         B\n"));
            Add(archive, "Mesh/.geo", Encoding.ASCII.GetBytes("NAME x\nNODE 1 0 0 0\nNODE 2 3 0 0\nNODE 3 3 0 4\nLINE 1 STRL 1 2\nLINE 2 STRL 2 3\nMACR 1 BEAM 1\n"));
            Add(archive, "Results_Main_V_2/.HXN000001", Forces(
                new[] { new[] { -10000f, 0f, 2000f, 0f, -3000f, 0f }, new[] { -10000f, 0f, 1000f, 0f, 0f, 0f }, new[] { -10000f, 0f, 0f, 0f, 1500f, 0f } },
                new[] { 0.0, 1.5, 3.0 },
                new[] { new[] { 0f, 0f, -0.001f, 0f, 0f, 0f }, new[] { 0f, 0f, -0.004f, 0f, 0f, 0f }, new[] { 0f, 0f, -0.002f, 0f, 0f, 0f } }));
            Add(archive, "Results_Main_V_2/.PRON000001", Encoding.ASCII.GetBytes("SYSTEM=RFEM   !\nNCOMB=1   !\nTEXT=   !\nNCASE=1,11   !\nNCOEF=1.3500,1.5000   !\n"));
        }

        return zip.ToArray();
    }

    private static void Add(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(content, 0, content.Length);
    }

    /// <summary>Minimalny plik OLE: katalog główny, folder <c>Results_Main_V_2</c> i strumień <c>.HXN000001</c> z podaną treścią (sektory 512 B).</summary>
    public static byte[] CompoundFile(byte[] content)
    {
        var dataSectors = (content.Length + 511) / 512;
        var total = 2 + dataSectors;
        var file = new byte[512 * (total + 1)];
        var w = new BinaryWriter(new MemoryStream(file));
        w.Write(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 });
        w.Seek(0x18, SeekOrigin.Begin);
        w.Write((ushort)0x3E);
        w.Write((ushort)3);
        w.Write((ushort)0xFFFE);
        w.Write((ushort)9);
        w.Write((ushort)6);
        w.Seek(0x2C, SeekOrigin.Begin);
        w.Write(1u);          // sektorów FAT
        w.Write(1u);          // pierwszy sektor katalogu
        w.Write(0u);
        w.Write(4096u);       // granica mini-strumieni
        w.Write(0xFFFFFFFEu); // mini FAT
        w.Write(0u);
        w.Write(0xFFFFFFFEu); // DIFAT
        w.Write(0u);
        w.Write(0u);          // DIFAT[0] = sektor 0
        for (var i = 1; i < 109; i++)
        {
            w.Write(0xFFFFFFFFu);
        }

        // FAT (sektor 0)
        w.Seek(512, SeekOrigin.Begin);
        w.Write(0xFFFFFFFDu);
        w.Write(0xFFFFFFFEu);
        for (var s = 2; s < total; s++)
        {
            w.Write(s == total - 1 ? 0xFFFFFFFEu : (uint)(s + 1));
        }

        for (var s = total; s < 128; s++)
        {
            w.Write(0xFFFFFFFFu);
        }

        // katalog (sektor 1)
        WriteEntry(file, 1024, "Root Entry", 5, 0xFFFFFFFF, 0xFFFFFFFF, 1, 0xFFFFFFFE, 0);
        WriteEntry(file, 1024 + 128, "Results_Main_V_2", 1, 0xFFFFFFFF, 0xFFFFFFFF, 2, 0, 0);
        WriteEntry(file, 1024 + 256, ".HXN000001", 2, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 2, (uint)content.Length);
        Array.Copy(content, 0, file, 512 * 3, content.Length);
        return file;
    }

    private static void WriteEntry(byte[] file, int at, string name, byte type, uint left, uint right, uint child, uint start, uint size)
    {
        var bytes = Encoding.Unicode.GetBytes(name);
        Array.Copy(bytes, 0, file, at, bytes.Length);
        BitConverter.GetBytes((ushort)(bytes.Length + 2)).CopyTo(file, at + 64);
        file[at + 66] = type;
        file[at + 67] = 1;
        BitConverter.GetBytes(left).CopyTo(file, at + 68);
        BitConverter.GetBytes(right).CopyTo(file, at + 72);
        BitConverter.GetBytes(child).CopyTo(file, at + 76);
        BitConverter.GetBytes(start).CopyTo(file, at + 116);
        BitConverter.GetBytes(size).CopyTo(file, at + 120);
    }

    private static byte[] Cp1250(string text)
    {
        var map = new Dictionary<char, byte> { ['ę'] = 0xEA, ['ż'] = 0xBF, ['ą'] = 0xB9, ['ł'] = 0xB3, ['ś'] = 0x9C, ['ć'] = 0xE6, ['ń'] = 0xF1, ['ó'] = 0xF3, ['ź'] = 0x9F };
        return text.Select(c => map.TryGetValue(c, out var b) ? b : (byte)c).ToArray();
    }
}

/// <summary>Czytnik .rf5 na danych syntetycznych: rekordy modelu, siatka, wyniki, OLE, wykonawca i pliki umiejętności.</summary>
public sealed class RfemUnitTests : IDisposable
{
    private static readonly HarnessProfile RfemProfile = HarnessProfile.Load(Paths.Profile("rfem"));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lokalneai_rfem_" + Guid.NewGuid().ToString("N"));

    public RfemUnitTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "RFEM"));
        File.WriteAllBytes(Path.Combine(_root, "RFEM", "hala.rf5"), Rf5Bytes.SampleFile());
        File.WriteAllText(Path.Combine(_root, "RFEM", "notatka.txt"), "x");
    }

    public void Dispose() => Directory.Delete(_root, true);

    private RfemExecutor Executor(string work = null) => new RfemExecutor(new RfemOptions { AllowedRoots = new List<string> { _root }, WorkingFolder = work ?? Path.Combine(_root, "RFEM") });

    private static ToolCall Call(string tool, object args) => new ToolCall(RfemProfile.FindTool(tool), Paths.Json(JsonSerializer.Serialize(args)), 1, 1);

    [Fact]
    public void Parser_WezlyLiniePretyPrzekrojeMaterialy()
    {
        var model = Rf5Parser.Parse(Rf5Bytes.SampleModel());

        Assert.Equal(3, model.Nodes.Count);
        Assert.Equal(4.0, model.Nodes[3].Z);
        Assert.Equal("Standard", model.Nodes[1].CoordinateSystem);
        Assert.Equal(new[] { 2, 3 }, model.Lines[2].Nodes);
        Assert.Equal(2, model.Members.Count);
        Assert.Equal("Beam", model.Members[1].Type);
        Assert.Equal(1, model.Members[1].CrossSectionStart);
        Assert.Equal("BeamRigid", model.Members[2].Type);
        Assert.Equal(0, model.Members[2].CrossSectionStart);
        Assert.Equal(3.0, model.MemberLength(model.Members[1])!.Value, 9);
        Assert.Equal(4.0, model.MemberLength(model.Members[2])!.Value, 9);

        Assert.Equal(2, model.CrossSections.Count);
        var hea = model.CrossSections[1];
        Assert.Equal("HE A 160 | DIN 1025-3:1994", hea.Description);
        Assert.Equal("HE-A 160", hea.TextId);
        Assert.Equal(2, hea.MaterialNo);
        Assert.Equal(3.877e-3, hea.Area, 12);
        Assert.Equal(1.673e-5, hea.Iy, 12);
        Assert.Equal(1.219e-7, hea.It, 12);
        Assert.Equal(0.16, hea.Dimension1, 6);

        Assert.Equal(2, model.Materials.Count);
        var steel = model.Materials[2];
        Assert.Equal("Stal S 355", steel.Description);
        Assert.Equal("STEEL", steel.Kind);
        Assert.Equal("EN 1993-1-1:2005-05", steel.Norm);
        Assert.Equal(2.1e11, steel.E);
        Assert.Equal(78500, steel.SpecificWeight, 1);
        Assert.Equal("Beton C30/37", model.Materials[1].Description);
        Assert.Empty(model.Warnings);
    }

    [Fact]
    public void Parser_PrzypadkiIKombinacje()
    {
        var model = Rf5Parser.Parse(Rf5Bytes.SampleModel());

        Assert.Equal(new[] { 1, 11 }, model.LoadCases.Select(lc => lc.No));
        Assert.Equal("Ciężar własny", model.LoadCases[0].Description);
        Assert.Equal("Obciążenia zmienne - podesty", model.LoadCases[1].Description);
        Assert.Equal(2, model.Combinations.Count);
        Assert.Equal("1.35*LF1 + 1.5*LF11", model.Combinations[0].Expression);
        Assert.Equal("1.35*PO1 + 1.5*PO11", model.Combinations[0].ExpressionPl);
        Assert.Equal("SGU", model.Combinations[1].Description);
        Assert.Single(model.ResultCombinations);
        Assert.Equal("SGN - Wszystko", model.ResultCombinations[0].Description);
        Assert.Equal("KO1 lub KO2", model.ResultCombinations[0].ExpressionPl);
    }

    [Fact]
    public void Parser_WersjaZHistorii()
        => Assert.Equal("5.27.01.160034", Rf5Parser.ReadVersion(Encoding.ASCII.GetBytes("\u0001History\u0004RFEM\u000e5.27.01.160034\u0000")));

    [Fact]
    public void Parser_PustyStrumienDajeOstrzezenie()
    {
        var model = Rf5Parser.Parse(new byte[10]);
        Assert.Empty(model.Nodes);
        Assert.Single(model.Warnings);
    }

    [Fact]
    public void Mesh_MtoIGeo()
    {
        var mesh = new Rf5Mesh();
        mesh.MemberOrder.AddRange(Rf5Mesh.ParseMto("    28   1   49   1   1  B\n    29   2   50   2   5  B\n     0   0    0   0   0  B\n"));
        Rf5Mesh.ParseGeo("NODE 1 0 0 0\nNODE 2 0 0 2.5\nLINE 7 STRL 1 2\nMACR 28 BEAM 7\nCON0 1 2\n", mesh);

        Assert.Equal(new[] { 28, 29 }, mesh.MemberOrder);
        Assert.Equal(7, mesh.MemberLine[28]);
        Assert.Equal(2.5, mesh.MemberLength(28)!.Value, 9);
        Assert.Null(mesh.MemberLength(29));
    }

    [Fact]
    public void Results_ProtokolIDekodowanie()
    {
        var set = new Rf5ResultSet();
        Rf5Results.ReadProtocol("SYSTEM=RFEM   !\nNCOMB=5   !\nTEXT=   !\nNCASE=1,3,21   !\nNCOEF=1.3500,1.3500,1.0000   !\n", set);
        Assert.Equal(5, set.CombinationNo);
        Assert.Equal(new[] { 1, 3, 21 }, set.CaseNos);
        Assert.Equal("1.35*PO1 + 1.35*PO3 + PO21", set.ExpressionFromProtocol());
        Assert.Equal("KO5", set.ShortLabel);

        var raw = Rf5Bytes.Forces(new[] { new[] { -3500f, 0f, 0f, 0f, 0f, 0f }, new[] { 0f, 0f, 10f, 0f, -4470f, 0f } }, new[] { 0.0, 2.832 });
        var points = Rf5Results.Decode(raw, 0, 28);
        Assert.Equal(2, points.Count);
        Assert.Equal(-3500, points[0].N);
        Assert.Equal(2.832, points[1].X, 9);
        Assert.Equal(-4470, points[1].My);
        Assert.NotNull(points[0].Displacements);
        Assert.Throws<RfemException>(() => Rf5Results.Decode(raw, 1, 29));
    }

    [Fact]
    public void Ole_CzytaStrumienWFolderze()
    {
        var content = Enumerable.Range(0, 5000).Select(i => (byte)(i % 251)).ToArray();
        var file = Rf5Bytes.CompoundFile(content);

        Assert.True(CompoundFile.HasSignature(file));
        var ole = new CompoundFile(file);
        Assert.Contains("Results_Main_V_2/.HXN000001", ole.Streams);
        Assert.Equal(5000, ole.Size("Results_Main_V_2/.HXN000001"));
        Assert.Equal(content, ole.Read("Results_Main_V_2/.HXN000001"));
        Assert.False(ole.Exists("Mesh/.geo"));
        Assert.Throws<RfemException>(() => Rf5Archive.FromBytes("x.rf5", file).ReadInputData());
    }

    [Fact]
    public void Dokument_CzytaCalyPlikIWyniki()
    {
        using var document = Rf5Document.FromBytes("hala.rf5", Rf5Bytes.SampleFile());

        Assert.Equal("ZIP", document.Container);
        Assert.Equal("5.27.01.160034", document.Model.Version);
        Assert.True(document.HasResults);
        Assert.Equal(3.0, document.MemberLength(1)!.Value, 9);
        var set = Assert.Single(document.Results!.Sets);
        Assert.Equal("KO1 (1.35*PO1 + 1.5*PO11)", set.Label(document.Model));
        Assert.Same(set, document.Results.Find("KO1"));
        Assert.Same(set, document.Results.Find("CO 1"));
        Assert.Null(document.Results.Find("PO1"));
        var forces = document.Results.MemberForces(set, 1);
        Assert.Equal(3, forces.Count);
        Assert.Equal(1500, forces[2].My);
        Assert.Throws<RfemException>(() => document.Results.MemberForces(set, 2));
    }

    [Fact]
    public void Wykonawca_Decyzje()
    {
        var executor = Executor();

        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("rfem_info", new { sciezka = "hala.rf5" })).Kind);
        Assert.Equal(ConfirmationKind.None, executor.Decide(Call("rfem_pret", new { numer = 1 })).Kind);   // jedyny .rf5 w folderze
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("rfem_pret", new { sciezka = "notatka.txt", numer = 1 })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("rfem_pret", new { sciezka = "brak.rf5", numer = 1 })).Kind);
        Assert.Equal(ConfirmationKind.Reject, executor.Decide(Call("rfem_info", new { sciezka = @"C:\Windows\x.rf5" })).Kind);
        Assert.Equal(ConfirmationKind.Reject, Executor(_root).Decide(Call("rfem_info", new { })).Kind);   // w _root nie ma .rf5
    }

    [Fact]
    public async Task Wykonawca_InfoPretSily()
    {
        var executor = Executor();

        var info = await executor.ExecuteAsync(Call("rfem_info", new { }), CancellationToken.None);
        Assert.True(info.Success, info.Text);
        Assert.Contains("3 węzłów, 2 linii, 2 prętów", info.Text);
        Assert.Contains("HE A 160", info.Text);
        Assert.Contains("Przypadki (2)", info.Text);
        Assert.Contains("Wyniki: 1 zestawów (KO1)", info.Text);

        var member = await executor.ExecuteAsync(Call("rfem_pret", new { numer = 1 }), CancellationToken.None);
        Assert.True(member.Success, member.Text);
        Assert.Contains("belka, linia 1, węzły 1 → 2, długość 3,000 m", member.Text);
        Assert.Contains("Przekrój 1: HE A 160 | DIN 1025-3:1994 (HE-A 160), materiał 2 Stal S 355 (E = 210 GPa); A = 38,77 cm²", member.Text);
        Assert.Contains("N: min -10,00 (KO1, x = 0,000)", member.Text);

        var rigid = await executor.ExecuteAsync(Call("rfem_pret", new { numer = 2 }), CancellationToken.None);
        Assert.Contains("pręt sztywny", rigid.Text);
        Assert.Contains("nie ma sił", rigid.Text);

        var missing = await executor.ExecuteAsync(Call("rfem_pret", new { numer = 4 }), CancellationToken.None);
        Assert.False(missing.Success);
        Assert.Contains("nie ma pręta 4", missing.Text);
        Assert.Contains("1–2", missing.Text);

        var forces = await executor.ExecuteAsync(Call("rfem_sily", new { numer = 1, obciazenie = "KO1" }), CancellationToken.None);
        Assert.True(forces.Success, forces.Text);
        Assert.Contains("KO1 (1.35*PO1 + 1.5*PO11)", forces.Text);
        Assert.Contains("0,000 | -10,00 | 0,00 | 2,00 | 0,00 | -3,00 | 0,00", forces.Text);
        Assert.Contains("My od -3,00 (x = 0,000) do 1,50 (x = 3,000)", forces.Text);

        var displacements = await executor.ExecuteAsync(Call("rfem_sily", new { numer = 1, obciazenie = "KO1", co = "przemieszczenia" }), CancellationToken.None);
        Assert.Contains("1,500 | 0,00 | 0,00 | -4,00", displacements.Text);

        var unknown = await executor.ExecuteAsync(Call("rfem_sily", new { numer = 1, obciazenie = "KO7" }), CancellationToken.None);
        Assert.False(unknown.Success);
        Assert.Contains("Są: KO1", unknown.Text);
    }

    [Fact]
    public async Task Wykonawca_SzukaniePrzekrojePrzypadkiWezel()
    {
        var executor = Executor();

        var search = await executor.ExecuteAsync(Call("rfem_szukaj_pretow", new { przekroj = "HEA 160" }), CancellationToken.None);
        Assert.Contains("OK 1 pręt (przekrój „HEA 160”) z 2", search.Text);
        var rigid = await executor.ExecuteAsync(Call("rfem_szukaj_pretow", new { typ = "sztywny" }), CancellationToken.None);
        Assert.Contains("pręt 2: pręt sztywny", rigid.Text);
        var atNode = await executor.ExecuteAsync(Call("rfem_szukaj_pretow", new { wezel = 2 }), CancellationToken.None);
        Assert.Contains("OK 2 pręty (węzeł 2)", atNode.Text);
        var longOnes = await executor.ExecuteAsync(Call("rfem_szukaj_pretow", new { min_dlugosc = 3.5 }), CancellationToken.None);
        Assert.Contains("OK 1 pręt (długość od 3,50 m )", longOnes.Text);

        var sections = await executor.ExecuteAsync(Call("rfem_przekroje", new { }), CancellationToken.None);
        Assert.Contains("1 | HE A 160 | DIN 1025-3:1994 (HE-A 160) | Stal S 355 | A = 38,77 | Iy = 1673,0 | Iz = 615,6 | It = 12,19 | 1 pręt (1), razem 3,00 m", sections.Text);
        Assert.Contains("2 Stal S 355 (stal, E = 210,0 GPa, G = 80,8 GPa, γ = 78,5 kN/m³, EN 1993-1-1:2005-05)", sections.Text);

        var loadings = await executor.ExecuteAsync(Call("rfem_przypadki", new { }), CancellationToken.None);
        Assert.Contains("PO11 Obciążenia zmienne - podesty", loadings.Text);
        Assert.Contains("KO1: 1.35*PO1 + 1.5*PO11 [wyniki]", loadings.Text);
        Assert.Contains("KW1 SGN - Wszystko: KO1 lub KO2", loadings.Text);

        var node = await executor.ExecuteAsync(Call("rfem_wezel", new { numer = 2 }), CancellationToken.None);
        Assert.Contains("OK węzeł 2: X = 3,000 m, Y = 0,000 m, Z = 0,000 m.", node.Text);
        Assert.Contains("Pręty w węźle (2)", node.Text);

        var files = await executor.ExecuteAsync(Call("szukaj_plikow", new { wzorzec = "*.rf5", sciezka = _root }), CancellationToken.None);
        Assert.Contains("hala.rf5", files.Text);
    }

    [Fact]
    public void Wykonawca_OdswiezaPoZmianiePliku()
    {
        var executor = Executor();
        var path = Path.Combine(_root, "RFEM", "hala.rf5");
        var first = executor.Document(path);
        Assert.Same(first, executor.Document(path));

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.NotSame(first, executor.Document(path));
    }

    [Fact]
    public void Profil_PrzykladyITestyWskazujaIstniejacePolecenia()
    {
        Assert.Equal(16, RfemProfile.Tools.Count(t => !t.IsBuiltIn));   // 9 na pliku .rf5 + 7 na otwartym RFEM (RfemExecutor.LiveTools)
        Assert.All(RfemExecutor.Tools, t => Assert.NotNull(RfemProfile.FindTool(t)));
        foreach (var line in File.ReadAllLines(Path.Combine(RfemProfile.Folder, "testy.jsonl"), Encoding.UTF8).Where(l => l.Trim().Length > 0))
        {
            var test = Paths.Json(line);
            foreach (var step in test.GetProperty("kroki").EnumerateArray())
            {
                foreach (var expected in step.GetProperty("oczekiwane").EnumerateArray())
                {
                    Assert.NotNull(RfemProfile.FindTool(expected.GetProperty("narzedzie").GetString()!));
                }
            }
        }

        foreach (var example in RfemProfile.Examples.Where(e => !string.IsNullOrWhiteSpace(e.CallsJson)))
        {
            foreach (var call in Paths.Json(example.CallsJson).EnumerateArray())
            {
                Assert.NotNull(RfemProfile.FindTool(call.GetProperty("narzedzie").GetString()!));
            }
        }
    }

    [Theory]
    [InlineData(2.832, 3, "2,832")]
    [InlineData(-0.0001, 2, "0,00")]
    [InlineData(-45.2, 2, "-45,20")]
    [InlineData(210, 0, "210")]
    public void Liczby_PoPolsku(double value, int decimals, string expected) => Assert.Equal(expected, RfemExecutor.Num(value, decimals));
}

/// <summary>Prawdziwe modele biura: ścieżki plików .rf5 w zmiennej środowiskowej <c>LOKALNEAI_RFEM_PROBKA</c> (rozdzielone „;”); bez niej test przechodzi bez sprawdzania.</summary>
public sealed class RfemLiveTests
{
    private readonly ITestOutputHelper _output;

    public RfemLiveTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void Probki_CzytajaSieIMajaSpojneTabele()
    {
        var samples = (Environment.GetEnvironmentVariable("LOKALNEAI_RFEM_PROBKA") ?? "").Split(';').Select(p => p.Trim()).Where(p => p.Length > 0 && File.Exists(p)).ToList();
        if (samples.Count == 0)
        {
            _output.WriteLine("Brak próbek (LOKALNEAI_RFEM_PROBKA) — pominięto.");
            return;
        }

        foreach (var sample in samples)
        {
            using var document = Rf5Document.Open(sample);
            var model = document.Model;
            _output.WriteLine($"{Path.GetFileName(sample)}: {document.Container}, RFEM {model.Version}, węzły {model.Nodes.Count}, linie {model.Lines.Count}, pręty {model.Members.Count}, przekroje {model.CrossSections.Count}, materiały {model.Materials.Count}, PO {model.LoadCases.Count}, KO {model.Combinations.Count}, KW {model.ResultCombinations.Count}, wyniki {(document.HasResults ? document.Results!.Sets.Count : 0)}");
            foreach (var warning in model.Warnings)
            {
                _output.WriteLine("  uwaga: " + warning);
            }

            Assert.NotEmpty(model.Nodes);
            Assert.NotEmpty(model.Lines);
            // Pojedyncze niespójności (np. pręt na linii zapisanej w nierozpoznanym układzie) wypisujemy; test pada dopiero, gdy jest ich więcej niż 3%.
            var inconsistencies = new List<string>();
            foreach (var line in model.Lines.Values)
            {
                inconsistencies.AddRange(line.Nodes.Where(n => !model.Nodes.ContainsKey(n)).Select(n => "linia " + line.No + " wskazuje węzeł " + n));
            }

            foreach (var member in model.Members.Values)
            {
                if (!model.Lines.ContainsKey(member.LineNo))
                {
                    inconsistencies.Add("pręt " + member.No + " wskazuje linię " + member.LineNo);
                }
                else
                {
                    var length = document.MemberLength(member.No);
                    if (!length.HasValue || length.Value <= 0.001)
                    {
                        inconsistencies.Add("pręt " + member.No + " bez długości");
                    }
                }

                if (member.CrossSectionStart > 0 && !model.CrossSections.ContainsKey(member.CrossSectionStart))
                {
                    inconsistencies.Add("pręt " + member.No + " wskazuje przekrój " + member.CrossSectionStart);
                }
            }

            foreach (var section in model.CrossSections.Values)
            {
                if (!(section.Area > 1e-6 && section.Area < 1))
                {
                    inconsistencies.Add("przekrój " + section.No + " A = " + section.Area);
                }

                if (!model.Materials.ContainsKey(section.MaterialNo))
                {
                    inconsistencies.Add("przekrój " + section.No + " wskazuje materiał " + section.MaterialNo);
                }
            }

            foreach (var problem in inconsistencies.Take(10))
            {
                _output.WriteLine("  niespójność: " + problem);
            }

            var objects = model.Nodes.Count + model.Lines.Count + model.Members.Count + model.CrossSections.Count;
            Assert.True(inconsistencies.Count <= Math.Max(2, objects * 3 / 100), "niespójności: " + inconsistencies.Count + " z " + objects + " obiektów");

            var first = model.Members.Values.OrderBy(m => m.No).FirstOrDefault(m => m.Type == "Beam");
            if (first != null)
            {
                var section = model.SectionOf(first);
                _output.WriteLine($"  pręt {first.No}: {first.TypeName}, L = {document.MemberLength(first.No):0.000} m, przekrój {section?.Name} ({section?.TextId}), materiał {model.MaterialOf(section!)?.Description}");
            }

            if (document.HasResults)
            {
                var results = document.Results!;
                Assert.All(results.Sets, s => Assert.True(s.CombinationNo > 0 || s.CaseNos.Length > 0, "zestaw " + s.Index + " bez protokołu"));
                var unmatched = results.Sets.Count(s => s.CombinationNo > 0 && model.Combination(s.CombinationNo) == null);
                Assert.Equal(0, unmatched);
                var member = results.MembersWithResults.First(m => model.Members.ContainsKey(m));
                var set = results.Sets[0];
                var points = results.MemberForces(set, member);
                Assert.NotEmpty(points);
                var length = document.MemberLength(member)!.Value;
                Assert.InRange(points.Max(p => p.X), length * 0.98, length * 1.02);
                _output.WriteLine($"  {set.Label(model)}, pręt {member}: {points.Count} punktów, N od {points.Min(p => p.N) / 1e3:0.00} do {points.Max(p => p.N) / 1e3:0.00} kN, My od {points.Min(p => p.My) / 1e3:0.00} do {points.Max(p => p.My) / 1e3:0.00} kNm");
            }
        }
    }
}
