// SZABLON: ustaw_poziom_gory_slupa
// Dla mostu TeklaBridge (C# 5). Wynik zaczyna sie od OK albo BLAD; przy wstawianiu: id=<ID>.
// Podstawianie: znacznik w podwojnych nawiasach klamrowych - liczba: literal z kropka dziesietna (np. 3.53, -0.5, 1001); tekst: tresc literalu w cudzyslowie
//   (gospodarz escapuje \ i " oraz nowe linie).
// BRAKUJACE parametry opcjonalne: tekst -> "", liczba (takze integer) -> double.NaN; szablon zamienia NaN na wartosc domyslna.
// Poziomy (_m) sa w metrach i szablon mnozy je x1000; reszta w mm.
// PARAMETRY (wymagany / opcjonalny, domyslna wartosc):
//   id : integer, WYMAGANY
//   poziom_gora_m : number, WYMAGANY
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
        int id = ToId({{id}});
        double z = {{poziom_gora_m}} * 1000.0;
        Beam b = Find(model, id) as Beam;
        if (b == null) return "BLAD: nie znaleziono slupa o id=" + id;
        if (!Vertical(b)) return "BLAD: element id=" + id + " nie jest pionowym slupem";
        bool startLower = b.StartPoint.Z <= b.EndPoint.Z;
        bool zmieniamyStart = (startLower == false);
        double drugi = zmieniamyStart ? b.EndPoint.Z : b.StartPoint.Z;
        if (false) { if (z >= drugi - 1.0) return "BLAD: nowy dol (" + F(z) + " mm) nie jest nizej niz gora (" + F(drugi) + " mm)"; }
        else { if (z <= drugi + 1.0) return "BLAD: nowa gora (" + F(z) + " mm) nie jest wyzej niz dol (" + F(drugi) + " mm)"; }
        if (zmieniamyStart) b.StartPoint = new Point(b.StartPoint.X, b.StartPoint.Y, z);
        else b.EndPoint = new Point(b.EndPoint.X, b.EndPoint.Y, z);
        if (!b.Modify()) return "BLAD: Modify nie powiodl sie dla id=" + id;
        model.CommitChanges();
        return "OK id=" + id + " gora slupa na " + F(z) + " mm (blacha podstawy/glowicy i kotwy nie zostaly przesuniete)";
    }
}
