// SZABLON: wstaw_blache
// Dla mostu TeklaBridge (C# 5). Wynik zaczyna sie od OK albo BLAD; przy wstawianiu: id=<ID>.
// Podstawianie: znacznik w podwojnych nawiasach klamrowych - liczba: literal z kropka dziesietna (np. 3.53, -0.5, 1001); tekst: tresc literalu w cudzyslowie
//   (gospodarz escapuje \ i " oraz nowe linie).
// BRAKUJACE parametry opcjonalne: tekst -> "", liczba (takze integer) -> double.NaN; szablon zamienia NaN na wartosc domyslna.
// Opcjonalne w tym szablonie: polozenie, material, klasa, nazwa
// Poziomy (_m) sa w metrach i szablon mnozy je x1000; reszta w mm.
// PARAMETRY (wymagany / opcjonalny, domyslna wartosc):
//   x_mm : number, WYMAGANY
//   y_mm : number, WYMAGANY
//   poziom_m : number, WYMAGANY
//   polozenie : string, opcjonalny, domyslnie brak filtra, dozwolone: gora/dol
//   dlugosc_x_mm : number, WYMAGANY
//   szerokosc_y_mm : number, WYMAGANY
//   grubosc_mm : number, WYMAGANY
//   material : string, opcjonalny, domyslnie S235JR
//   klasa : string, opcjonalny, domyslnie 1
//   nazwa : string, opcjonalny, domyslnie BLACHA
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
    // plyta prostokatna ze srodkiem w (cx,cy); top=true: gorna powierzchnia na z (plyta lezy ponizej), top=false: dolna powierzchnia na z
    static ContourPlate MakePlate(Tekla.Structures.Model.Model model, double cx, double cy, double lx, double ly, double z, double t, string mat, string cls, string name, bool top)
    {
        ContourPlate p = new ContourPlate();
        p.AddContourPoint(new ContourPoint(new Point(cx - lx / 2, cy - ly / 2, z), null));
        p.AddContourPoint(new ContourPoint(new Point(cx + lx / 2, cy - ly / 2, z), null));
        p.AddContourPoint(new ContourPoint(new Point(cx + lx / 2, cy + ly / 2, z), null));
        p.AddContourPoint(new ContourPoint(new Point(cx - lx / 2, cy + ly / 2, z), null));
        p.Profile.ProfileString = "PL" + F(t);
        p.Material.MaterialString = mat;
        p.Class = cls;
        p.Name = name;
        p.Position.Depth = top ? Position.DepthEnum.BEHIND : Position.DepthEnum.FRONT;
        if (!p.Insert()) return null;
        model.CommitChanges();
        // kontrola strony grubosci - jesli plyta wyszla po zlej stronie, odwracamy
        if (!SideOk(p, z, top))
        {
            p.Position.Depth = (p.Position.Depth == Position.DepthEnum.FRONT) ? Position.DepthEnum.BEHIND : Position.DepthEnum.FRONT;
            p.Modify();
            model.CommitChanges();
        }
        return p;
    }
    static bool SideOk(ContourPlate p, double z, bool top)
    {
        Solid s = p.GetSolid();
        if (s == null) return true;
        double cur = top ? s.MaximumPoint.Z : s.MinimumPoint.Z;
        return Math.Abs(cur - z) < 1.0;
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
        double x = {{x_mm}}, y = {{y_mm}}, z = {{poziom_m}} * 1000.0, lx = {{dlugosc_x_mm}}, ly = {{szerokosc_y_mm}}, t = {{grubosc_mm}};
        string pol = "{{polozenie}}";
        string mat = T("{{material}}", "S235JR"), kl = T("{{klasa}}", "1"), nazwa = T("{{nazwa}}", "BLACHA");
        if (lx < 1.0 || ly < 1.0 || t < 1.0) return "BLAD: wymiary blachy musza byc dodatnie";
        bool top = pol != "dol";
        ContourPlate p = MakePlate(model, x, y, lx, ly, z, t, mat, kl, nazwa, top);
        if (p == null) return "BLAD: Insert blachy nie powiodl sie";
        return "OK id=" + p.Identifier.ID + " blacha PL" + F(t) + " " + F(lx) + " x " + F(ly) + " mm, " + (top ? "gora" : "dol") + " na " + F(z) + " mm";
    }
}
