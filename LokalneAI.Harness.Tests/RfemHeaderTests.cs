using System.Text;
using LokalneAI.Harness.Rfem;

namespace LokalneAI.Harness.Tests;

/// <summary>
/// Nagłówek rekordu obiektu w .rf5: <c>[i32 numer][tekst 1][tekst 2][00×6][01×n]</c>, teksty <c>[u8 len]</c> albo <c>[FF][u16 len]</c>.
/// Model z Tekli (F10, 2026-11-19) miał w każdym pręcie 266-znakowe <c>&lt;TeklaItemMetadata&gt;</c> — w tekście 1 albo 2 —
/// i parser widział 163 z 527 prętów, a numer brał z wnętrza tekstu („0000” = 808464432). Do tego linie z kątem π/2.
/// </summary>
public class RfemHeaderTests
{
    private static readonly byte[] Tail14 = { 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
    private static readonly byte[] Key = Encoding.ASCII.GetBytes("\u000fOwnerParamGroup");
    private const string Metadata = "<TeklaItemMetadata IsAnalysisItem=\"false\" Index=\"-1\"><PhysicalID>7041ae4f-411f-4de3-abae-6cd0751ccfc4</PhysicalID>"
        + "<AnalysisModelID>00000000-0000-0000-0000-000000000000</AnalysisModelID><AnalysisID>00000000-0000-0000-0000-000000000000</AnalysisID></TeklaItemMetadata>";

    private static void WriteText(BinaryWriter w, string text)
    {
        var bytes = Encoding.GetEncoding("iso-8859-1").GetBytes(text ?? "");
        if (bytes.Length >= 255)
        {
            w.Write((byte)0xFF);
            w.Write((ushort)bytes.Length);
        }
        else
        {
            w.Write((byte)bytes.Length);
        }

        w.Write(bytes);
    }

    private static byte[] Record(int no, string text1, string text2, int ones, byte[] body)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(no);
        WriteText(w, text1);
        WriteText(w, text2);
        w.Write(new byte[6]);
        w.Write(Enumerable.Repeat((byte)1, ones).ToArray());
        w.Write(Tail14);
        w.Write(Key);
        w.Write(body);
        w.Write(new byte[8]);
        return m.ToArray();
    }

    private static byte[] Numbers(params int[] values)
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

    private static byte[] MemberBody(int line, int section)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write((byte)0);
        w.Write(0f);
        w.Write(new byte[5]);
        w.Write(line);
        w.Write((byte)4);
        w.Write(Encoding.ASCII.GetBytes("Beam"));
        w.Write(Numbers(section));
        w.Write(Numbers());
        return m.ToArray();
    }

    private static byte[] NodeBody(double x, double y, double z)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write(new byte[8]);
        w.Write(x);
        w.Write(y);
        w.Write(z);
        w.Write((byte)8);
        w.Write(Encoding.ASCII.GetBytes("Standard"));
        return m.ToArray();
    }

    private static byte[] LineBody(float angle, params int[] nodes)
    {
        var m = new MemoryStream();
        var w = new BinaryWriter(m);
        w.Write((byte)0);
        w.Write(angle);
        w.Write(new byte[5]);
        w.Write(Numbers(nodes));
        return m.ToArray();
    }

    private static Rf5Model Parse(params byte[][] records)
    {
        var m = new MemoryStream();
        m.Write(new byte[64], 0, 64);
        foreach (var r in records)
        {
            m.Write(r, 0, r.Length);
        }

        m.Write(new byte[64], 0, 64);
        return Rf5Parser.Parse(m.ToArray());
    }

    [Fact]
    public void MetadaneTekli_WTekscie1Albo2_NumerIGuidCzytaneDobrze()
    {
        var model = Parse(
            Record(1, "", "", 8, NodeBody(0, 0, 0)),
            Record(2, "", "", 8, NodeBody(3, 0, 0)),
            Record(5, "", "", 4, LineBody(0, 1, 2)),
            Record(2, Metadata, "", 17, MemberBody(5, 7)),          // Tekla w tekście 1
            Record(3, "", Metadata, 17, MemberBody(5, 7)),          // Tekla w tekście 2
            Record(14, "Slup osi A", Metadata, 17, MemberBody(5, 9)), // komentarz + Tekla
            Record(808, Metadata, Metadata, 17, MemberBody(5, 7)));  // dwa razy (zdarzyło się raz w modelu)

        Assert.Equal(new[] { 2, 3, 14, 808 }, model.Members.Keys);
        Assert.All(model.Members.Values, m => Assert.Equal("7041ae4f-411f-4de3-abae-6cd0751ccfc4", m.TeklaGuid));
        Assert.Equal("", model.Members[2].Comment);
        Assert.Equal("", model.Members[3].Comment);
        Assert.Equal("Slup osi A", model.Members[14].Comment);
        Assert.Equal(9, model.Members[14].CrossSectionStart);
        Assert.Equal(5, model.Members[14].LineNo);
        Assert.Equal(3.0, model.MemberLength(model.Members[14]).Value, 6);
        Assert.Equal("", model.Nodes[1].Comment);
    }

    [Fact]
    public void KrotkiKomentarz_IBezKomentarza_JakDotad()
    {
        var model = Parse(
            Record(7, "Podpora 6-2", "", 8, NodeBody(1, 2, 3)),
            Record(9, "", "", 8, NodeBody(1, 2, 4)),
            Record(3, "", "", 4, LineBody(0, 7, 9)),
            Record(11, "", "", 17, MemberBody(3, 105)));

        Assert.Equal("Podpora 6-2", model.Nodes[7].Comment);
        Assert.Equal("", model.Nodes[9].Comment);
        Assert.Equal("", model.Members[11].TeklaGuid);
        Assert.Equal(105, model.Members[11].CrossSectionStart);
        Assert.Equal(1.0, model.MemberLength(model.Members[11]).Value, 6);
    }

    [Fact]
    public void LiniaZKatem_JestLiniaZWezlami()
    {
        var model = Parse(
            Record(30, "", "", 8, NodeBody(0, 0, 0)),
            Record(1129, "", "", 8, NodeBody(0, 4, 0)),
            Record(1311, "", "", 4, LineBody((float)(Math.PI / 2), 30, 1129)),
            Record(391, "", "", 17, MemberBody(1311, 14)));

        Assert.True(model.Lines.ContainsKey(1311));
        Assert.Equal(new[] { 30, 1129 }, model.Lines[1311].Nodes);
        Assert.Equal(Math.PI / 2, model.Lines[1311].Rotation, 5);
        Assert.Equal(4.0, model.MemberLength(model.Members[391]).Value, 6);
    }

    [Fact]
    public void WynikiPrzypadku_PodPseudoKombinacja10000MinusN_ToPrzypadek()
    {
        // Solver zapisuje wyniki samego PO n liczonego razem z kombinacjami jako NCOMB = 10000 − n.
        var set = new Rf5ResultSet();
        Rf5Results.ReadProtocol("NCOMB=9997 !\nNCASE=3 !\nNCOEF=1 !\n", set);
        Assert.Equal(0, set.CombinationNo);
        Assert.Equal(3, set.SingleCaseNo);
        Assert.Equal("PO3", set.ShortLabel);

        var combination = new Rf5ResultSet();
        Rf5Results.ReadProtocol("NCOMB=5 !\nNCASE=1,3 !\nNCOEF=1.35,1.5 !\n", combination);
        Assert.Equal(5, combination.CombinationNo);
        Assert.Equal("KO5", combination.ShortLabel);

        var odd = new Rf5ResultSet();
        Rf5Results.ReadProtocol("NCOMB=9990 !\nNCASE=3 !\n", odd);   // nie 10000 − n: zostaje kombinacją
        Assert.Equal(9990, odd.CombinationNo);
    }
}
