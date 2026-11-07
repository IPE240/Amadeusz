// SZABLON: wstaw_glowice_slupa
// Dla mostu TeklaBridge (C# 5). Wynik zaczyna sie od OK albo BLAD; przy wstawianiu: id=<ID>.
// Podstawianie: znacznik w podwojnych nawiasach klamrowych - liczba: literal z kropka dziesietna (np. 3.53, -0.5, 1001); tekst: tresc literalu w cudzyslowie
//   (gospodarz escapuje \ i " oraz nowe linie).
// BRAKUJACE parametry opcjonalne: tekst -> "", liczba (takze integer) -> double.NaN; szablon zamienia NaN na wartosc domyslna.
// Opcjonalne w tym szablonie: material, klasa
// Poziomy (_m) sa w metrach i szablon mnozy je x1000; reszta w mm.
// PARAMETRY (wymagany / opcjonalny, domyslna wartosc):
//   id : integer, WYMAGANY
//   grubosc_mm : number, WYMAGANY
//   dlugosc_x_mm : number, WYMAGANY
//   szerokosc_y_mm : number, WYMAGANY
//   material : string, opcjonalny, domyslnie S235JR
//   klasa : string, opcjonalny, domyslnie 1
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
        int id = ToId({{id}});
        double t = {{grubosc_mm}}, lx = {{dlugosc_x_mm}}, ly = {{szerokosc_y_mm}};
        string mat = T("{{material}}", "S235JR"), kl = T("{{klasa}}", "1");
        if (lx < 1.0 || ly < 1.0 || t < 1.0) return "BLAD: wymiary blachy musza byc dodatnie";
        Beam col = Find(model, id) as Beam;
        if (col == null) return "BLAD: nie znaleziono slupa o id=" + id;
        if (!Vertical(col)) return "BLAD: element id=" + id + " nie jest pionowym slupem";
        Point hi = col.StartPoint.Z >= col.EndPoint.Z ? col.StartPoint : col.EndPoint;
        ContourPlate pl = MakePlate(model, hi.X, hi.Y, lx, ly, hi.Z, t, mat, kl, "GLOWICA", false);
        if (pl == null) return "BLAD: Insert blachy glowicy nie powiodl sie";
        try
        {
            Weld w = new Weld();
            w.MainObject = col;
            w.SecondaryObject = pl;
            w.TypeAbove = BaseWeld.WeldTypeEnum.WELD_TYPE_FILLET;
            w.SizeAbove = 6.0;
            w.Insert();
            model.CommitChanges();
        }
        catch { }
        return "OK id=" + pl.Identifier.ID + " blacha glowicy PL" + F(t) + " " + F(lx) + " x " + F(ly) + " nad slupem id=" + id;
    }
}
