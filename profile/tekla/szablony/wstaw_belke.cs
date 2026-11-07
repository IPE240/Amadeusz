// SZABLON: wstaw_belke
// Dla mostu TeklaBridge (C# 5). Wynik zaczyna sie od OK albo BLAD; przy wstawianiu: id=<ID>.
// Podstawianie: znacznik w podwojnych nawiasach klamrowych - liczba: literal z kropka dziesietna (np. 3.53, -0.5, 1001); tekst: tresc literalu w cudzyslowie
//   (gospodarz escapuje \ i " oraz nowe linie).
// BRAKUJACE parametry opcjonalne: tekst -> "", liczba (takze integer) -> double.NaN; szablon zamienia NaN na wartosc domyslna.
// Opcjonalne w tym szablonie: material, klasa, nazwa, obrot_stopni
// Poziomy (_m) sa w metrach i szablon mnozy je x1000; reszta w mm.
// PARAMETRY (wymagany / opcjonalny, domyslna wartosc):
//   x1_mm : number, WYMAGANY
//   y1_mm : number, WYMAGANY
//   z1_m : number, WYMAGANY
//   x2_mm : number, WYMAGANY
//   y2_mm : number, WYMAGANY
//   z2_m : number, WYMAGANY
//   profil : string, WYMAGANY
//   material : string, opcjonalny, domyslnie S235JR
//   klasa : string, opcjonalny, domyslnie 1
//   nazwa : string, opcjonalny, domyslnie BELKA
//   obrot_stopni : number, opcjonalny, domyslnie 0
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
        double x1 = {{x1_mm}}, y1 = {{y1_mm}}, z1 = {{z1_m}} * 1000.0, x2 = {{x2_mm}}, y2 = {{y2_mm}}, z2 = {{z2_m}} * 1000.0;
        string profil = "{{profil}}";
        string mat = T("{{material}}", "S235JR"), kl = T("{{klasa}}", "1"), nazwa = T("{{nazwa}}", "BELKA");
        double obrot = Num({{obrot_stopni}});
        if (profil.Length == 0) return "BLAD: brak profilu";
        double len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1) + (z2 - z1) * (z2 - z1));
        if (len < 1.0) return "BLAD: poczatek i koniec belki to ten sam punkt";
        Beam b = new Beam(new Point(x1, y1, z1), new Point(x2, y2, z2));
        b.Profile.ProfileString = profil;
        b.Material.MaterialString = mat;
        b.Class = kl;
        b.Name = nazwa;
        b.Position.Plane = Position.PlaneEnum.MIDDLE;
        b.Position.Depth = Position.DepthEnum.MIDDLE;
        b.Position.Rotation = Position.RotationEnum.FRONT;
        b.Position.RotationOffset = obrot;
        if (!b.Insert()) return "BLAD: Insert nie powiodl sie";
        model.CommitChanges();
        return "OK id=" + b.Identifier.ID + " belka " + profil + " dlugosc " + F(len) + " mm" + ProfCheck(profil);
    }
}
