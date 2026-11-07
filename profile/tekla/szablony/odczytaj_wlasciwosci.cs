// SZABLON: odczytaj_wlasciwosci
// Dla mostu TeklaBridge (C# 5). Wynik zaczyna sie od OK albo BLAD; przy wstawianiu: id=<ID>.
// Podstawianie: znacznik w podwojnych nawiasach klamrowych - liczba: literal z kropka dziesietna (np. 3.53, -0.5, 1001); tekst: tresc literalu w cudzyslowie
//   (gospodarz escapuje \ i " oraz nowe linie).
// BRAKUJACE parametry opcjonalne: tekst -> "", liczba (takze integer) -> double.NaN; szablon zamienia NaN na wartosc domyslna.
// Poziomy (_m) sa w metrach i szablon mnozy je x1000; reszta w mm.
// PARAMETRY (wymagany / opcjonalny, domyslna wartosc):
//   id : integer, WYMAGANY
using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using Tekla.Structures.Model;
using Tekla.Structures.Geometry3d;

public static class Script
{
    static string F(double d) { return d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
    static string T(string s, string d) { return string.IsNullOrEmpty(s) ? d : s; }
    static string Norm(string s) { return (s ?? "").Replace(" ", "").ToUpperInvariant(); }
    // niepodana liczba opcjonalna przychodzi jako double.NaN -> traktujemy jak 0 (= wartosc domyslna)
    static double Num(double d) { return double.IsNaN(d) ? 0.0 : d; }
    static int ToId(double d) { return (int)Math.Round(d); }
    static ModelObject Find(Tekla.Structures.Model.Model m, int id)
    {
        try { return m.SelectModelObject(new Tekla.Structures.Identifier(id)); } catch { return null; }
    }
    static bool Vertical(Beam b)
    {
        double dx = b.EndPoint.X - b.StartPoint.X, dy = b.EndPoint.Y - b.StartPoint.Y, dz = b.EndPoint.Z - b.StartPoint.Z;
        return Math.Abs(dx) < 1.0 && Math.Abs(dy) < 1.0 && Math.Abs(dz) > 1.0;
    }
    static string ProfCheck(string prof)
    {
        try
        {
            System.Reflection.Assembly a = System.Reflection.Assembly.Load("Tekla.Structures.Catalogs, Version=2019.1.0.0, Culture=neutral, PublicKeyToken=2f04dbe497b71114");
            Type tli = a.GetType("Tekla.Structures.Catalogs.LibraryProfileItem");
            object li = Activator.CreateInstance(tli);
            System.Reflection.MethodInfo sel = tli.GetMethod("Select", new Type[] { typeof(string) });
            object r = sel.Invoke(li, new object[] { prof });
            if (r is bool && !(bool)r) return " UWAGA: profilu " + prof + " nie ma w katalogu (moze byc parametryczny - sprawdz w Tekli)";
        }
        catch { }
        return "";
    }
    static string fTyp = "", fProfil = "", fMat = "", fNazwa = "", fKlasa = "", fDot = "";
    static double fPoz = 0, fTol = 2.0;

    static void InitFiltr(string typ, string profil, string mat, string nazwa, string klasa, double pozM, string dot, double tolM)
    {
        fTyp = (typ ?? "").Trim().ToLowerInvariant();
        if (fTyp == "wszystko") fTyp = "";
        fProfil = profil ?? ""; fMat = mat ?? ""; fNazwa = nazwa ?? ""; fKlasa = klasa ?? "";
        fDot = (dot ?? "").Trim().ToLowerInvariant();
        if (double.IsNaN(pozM)) { fDot = ""; fPoz = 0; }
        else { fPoz = pozM * 1000.0; if (fDot.Length == 0) fDot = "gora"; }
        fTol = tolM > 0 ? tolM * 1000.0 : 2.0;
    }
    static bool Conc(string m)
    {
        string s = Norm(m);
        return s.Length > 1 && (s[0] == 'C' || s[0] == 'B') && char.IsDigit(s[1]);
    }
    static string Typ(ModelObject o)
    {
        Beam b = o as Beam;
        if (b != null)
        {
            double dx = b.EndPoint.X - b.StartPoint.X, dy = b.EndPoint.Y - b.StartPoint.Y, dz = b.EndPoint.Z - b.StartPoint.Z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len > 0 && Math.Abs(dz) > 0.7 * len) return "slup";
            return "belka";
        }
        ContourPlate p = o as ContourPlate;
        if (p != null) return Conc(p.Material.MaterialString) ? "plyta" : "blacha";
        return "inny";
    }
    static bool Like(string val, string pat)
    {
        string v = Norm(val), p = Norm(pat);
        if (p.Length == 0) return true;
        if (p.EndsWith("*")) return v.StartsWith(p.Substring(0, p.Length - 1));
        return v == p;
    }
    static bool Poziom(ModelObject o)
    {
        if (fDot.Length == 0) return true;
        double lo = 0, hi = 0;
        Beam b = o as Beam;
        if (b != null) { lo = Math.Min(b.StartPoint.Z, b.EndPoint.Z); hi = Math.Max(b.StartPoint.Z, b.EndPoint.Z); }
        else
        {
            ContourPlate p = o as ContourPlate;
            if (p == null) return false;
            Solid s = p.GetSolid();
            if (s != null) { lo = s.MinimumPoint.Z; hi = s.MaximumPoint.Z; }
            else
            {
                lo = double.MaxValue; hi = double.MinValue;
                foreach (ContourPoint cp in p.Contour.ContourPoints) { lo = Math.Min(lo, cp.Z); hi = Math.Max(hi, cp.Z); }
            }
        }
        double v = fDot == "dol" ? lo : (fDot == "srodek" ? (lo + hi) / 2.0 : hi);
        return Math.Abs(v - fPoz) <= fTol;
    }
    static bool Pasuje(ModelObject o)
    {
        Part pt = o as Part;
        if (pt == null) return false;
        if (fTyp.Length > 0 && Typ(o) != fTyp) return false;
        if (!Like(pt.Profile.ProfileString, fProfil)) return false;
        if (!Like(pt.Material.MaterialString, fMat)) return false;
        if (fKlasa.Length > 0 && Norm(pt.Class) != Norm(fKlasa)) return false;
        if (fNazwa.Length > 0 && (pt.Name ?? "").ToUpperInvariant().IndexOf(fNazwa.ToUpperInvariant()) < 0) return false;
        return Poziom(o);
    }
    static void Zbierz(ModelObjectEnumerator en, List<ModelObject> list)
    {
        while (en.MoveNext()) { ModelObject o = en.Current; if (o != null && Pasuje(o)) list.Add(o); }
    }
    static List<ModelObject> Szukaj(Tekla.Structures.Model.Model model, bool zSelekcji)
    {
        List<ModelObject> list = new List<ModelObject>();
        if (zSelekcji)
        {
            Zbierz(new Tekla.Structures.Model.UI.ModelObjectSelector().GetSelectedObjects(), list);
            return list;
        }
        ModelObjectSelector ms = model.GetModelObjectSelector();
        if (fTyp.Length == 0 || fTyp == "slup" || fTyp == "belka") Zbierz(ms.GetAllObjectsWithType(ModelObject.ModelObjectEnum.BEAM), list);
        if (fTyp.Length == 0 || fTyp == "plyta" || fTyp == "blacha") Zbierz(ms.GetAllObjectsWithType(ModelObject.ModelObjectEnum.CONTOURPLATE), list);
        return list;
    }
    static string Ids(List<ModelObject> list, int max)
    {
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < list.Count && i < max; i++) { if (i > 0) sb.Append(","); sb.Append(list[i].Identifier.ID); }
        if (list.Count > max) sb.Append(",... (pokazano " + max + " z " + list.Count + ")");
        return sb.ToString();
    }

    public static string Run(Tekla.Structures.Model.Model model)
    {
        if (!model.GetConnectionStatus()) return "BLAD: brak polaczenia z modelem";
        TransformationPlane stara = null;
        try
        {
            stara = model.GetWorkPlaneHandler().GetCurrentTransformationPlane();
            model.GetWorkPlaneHandler().SetCurrentTransformationPlane(new TransformationPlane());
            return Do(model);
        }
        catch (Exception ex) { return "BLAD: " + ex.Message; }
        finally { if (stara != null) model.GetWorkPlaneHandler().SetCurrentTransformationPlane(stara); }
    }

    static string Do(Tekla.Structures.Model.Model model)
    {
        int id = ToId({{id}});
        ModelObject o = Find(model, id);
        Part p = o as Part;
        if (p == null) return "BLAD: nie znaleziono czesci o id=" + id;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("OK id=" + id);
        sb.AppendLine("typ=" + Typ(p) + " profil=" + p.Profile.ProfileString + " material=" + p.Material.MaterialString + " klasa=" + p.Class + " nazwa='" + p.Name + "'");
        Beam b = p as Beam;
        if (b != null)
        {
            sb.AppendLine("poczatek=(" + F(b.StartPoint.X) + ", " + F(b.StartPoint.Y) + ", " + F(b.StartPoint.Z) + ") mm, koniec=(" + F(b.EndPoint.X) + ", " + F(b.EndPoint.Y) + ", " + F(b.EndPoint.Z) + ") mm");
            sb.AppendLine("poziom_dolu_m=" + F(Math.Min(b.StartPoint.Z, b.EndPoint.Z) / 1000.0) + " poziom_gory_m=" + F(Math.Max(b.StartPoint.Z, b.EndPoint.Z) / 1000.0));
        }
        ContourPlate cp = p as ContourPlate;
        if (cp != null)
        {
            Solid s = cp.GetSolid();
            if (s != null) sb.AppendLine("poziom_dolu_m=" + F(s.MinimumPoint.Z / 1000.0) + " poziom_gory_m=" + F(s.MaximumPoint.Z / 1000.0) + " (bryla)");
            sb.AppendLine("punktow konturu=" + cp.Contour.ContourPoints.Count);
        }
        double len = 0, wt = 0;
        string pos = "", apos = "";
        if (p.GetReportProperty("LENGTH", ref len)) sb.AppendLine("dlugosc_mm=" + F(len));
        if (p.GetReportProperty("WEIGHT", ref wt)) sb.AppendLine("masa_kg=" + F(wt));
        if (p.GetReportProperty("PART_POS", ref pos)) sb.AppendLine("pozycja_czesci=" + pos);
        if (p.GetReportProperty("ASSEMBLY_POS", ref apos)) sb.AppendLine("pozycja_zespolu=" + apos);
        return sb.ToString();
    }
}
