using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace LokalneAI.Harness.Rfem.Com
{
    /// <summary>Wywołania OLE/COM potrzebne do RF-COM (bez paczek Interop; .NET Framework 4.8 i .NET 10, tylko Windows).</summary>
    internal static class Native
    {
        /// <summary>CLASS_E_NOTLICENSED: wywołanie bez zajętej licencji RF-COM albo sesja RFEM bez dodatku RF-COM.</summary>
        public const int ClassNotLicensed = unchecked((int)0x80040112);

        /// <summary>MK_E_UNAVAILABLE: RFEM działa, ale nie ma go w tablicy uruchomionych obiektów (inny poziom uprawnień, model nieotwarty).</summary>
        public const int MonikerUnavailable = unchecked((int)0x800401E3);

        [DllImport("ole32.dll")]
        public static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

        [DllImport("oleaut32.dll")]
        public static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object obj);

        [DllImport("oleaut32.dll")]
        public static extern int LoadRegTypeLib(ref Guid guid, short major, short minor, int lcid, out ITypeLib typeLib);

        [DllImport("oleaut32.dll")]
        public static extern uint SafeArrayGetDim(IntPtr psa);

        [DllImport("oleaut32.dll")]
        public static extern uint SafeArrayGetElemsize(IntPtr psa);

        [DllImport("oleaut32.dll")]
        public static extern int SafeArrayGetLBound(IntPtr psa, uint dim, out int bound);

        [DllImport("oleaut32.dll")]
        public static extern int SafeArrayGetUBound(IntPtr psa, uint dim, out int bound);

        [DllImport("oleaut32.dll")]
        public static extern int SafeArrayAccessData(IntPtr psa, out IntPtr data);

        [DllImport("oleaut32.dll")]
        public static extern int SafeArrayUnaccessData(IntPtr psa);

        [DllImport("oleaut32.dll")]
        public static extern int SafeArrayDestroy(IntPtr psa);

        /// <summary>
        /// Rekordy z jednowymiarowej tablicy SAFEARRAY, którą RF-COM oddał przez <c>out</c> (np. siły w pręcie). Tablica jest
        /// zwalniana po przeczytaniu (<c>SafeArrayDestroy</c> zwalnia też BSTR-y w rekordach). Rozmiar rekordu musi zgadzać się
        /// z deklaracją w C# — inaczej błąd zamiast cichego czytania złych bajtów.
        /// </summary>
        public static T[] ReadRecords<T>(IntPtr psa) where T : struct
        {
            if (psa == IntPtr.Zero)
            {
                return new T[0];
            }

            try
            {
                if (SafeArrayGetDim(psa) != 1)
                {
                    throw new RfemException("BŁĄD: RF-COM oddał tablicę o nieoczekiwanym wymiarze (" + typeof(T).Name + ").");
                }

                SafeArrayGetLBound(psa, 1, out var low);
                SafeArrayGetUBound(psa, 1, out var high);
                var count = high - low + 1;
                if (count <= 0)
                {
                    return new T[0];
                }

                var size = (int)SafeArrayGetElemsize(psa);
                var expected = Marshal.SizeOf(typeof(T));
                if (size != expected)
                {
                    throw new RfemException("BŁĄD: rekord " + typeof(T).Name + " ma w RFEM " + size + " B, a program oczekuje " + expected + " B (inna wersja RF-COM niż 5.7).");
                }

                var hr = SafeArrayAccessData(psa, out var data);
                if (hr != 0)
                {
                    throw new RfemException("BŁĄD: nie udało się odczytać tablicy RF-COM (0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + ").");
                }

                try
                {
                    var result = new T[count];
                    for (var i = 0; i < count; i++)
                    {
                        result[i] = (T)Marshal.PtrToStructure(data + i * size, typeof(T));
                    }

                    return result;
                }
                finally
                {
                    SafeArrayUnaccessData(psa);
                }
            }
            finally
            {
                SafeArrayDestroy(psa);
            }
        }
    }

    /// <summary>
    /// Sprawdzenie, czy zarejestrowana biblioteka typów RF-COM ma metody w tych miejscach vtable, które zakłada <c>RfemCom.cs</c>
    /// (wygenerowany dla v5.7). Porównuje nazwy metod z <c>ITypeInfo</c> dla próbki miejsc; wynik udany jest zapamiętany na czas procesu.
    /// </summary>
    internal static class RfemComLayout
    {
        private static readonly Guid TypeLibGuid = new Guid("5A027F61-50D6-4979-B4FB-A80F4B9FD2C3");
        private static readonly object Lock = new object();
        private static bool _verified;

        private static readonly Tuple<Type, int, string>[] Checks =
        {
            Tuple.Create(typeof(IApplication), 8, "GetActiveModel"),
            Tuple.Create(typeof(IApplication), 12, "GetVersion"),
            Tuple.Create(typeof(IModel), 23, "GetModelData"),
            Tuple.Create(typeof(IModel), 27, "GetActiveView"),
            Tuple.Create(typeof(IModelData), 28, "GetNode"),
            Tuple.Create(typeof(IModelData), 58, "GetMaterial"),
            Tuple.Create(typeof(IModelData), 157, "GetCrossSection"),
            Tuple.Create(typeof(IModelData), 177, "GetMember"),
            Tuple.Create(typeof(IModelData), 197, "GetSetOfMembers"),
            Tuple.Create(typeof(IMember), 0, "GetData"),
            Tuple.Create(typeof(ILoads), 15, "GetLoadCombination"),
            Tuple.Create(typeof(ILoadCase), 12, "GetData"),
            Tuple.Create(typeof(ICalculation), 33, "GetResultsInFeNodes"),
            Tuple.Create(typeof(IResults), 6, "GetNodalSupportForces"),
            Tuple.Create(typeof(IResults), 25, "GetMemberDeformations"),
            Tuple.Create(typeof(IResults), 32, "GetMemberExtremeInternalForces"),
            Tuple.Create(typeof(IView), 0, "PickObjects"),
        };

        /// <summary>Opis niezgodności (zaczyna się od „BŁĄD”) albo null, gdy układ pasuje. Wołać w wątku STA przed pierwszym użyciem RF-COM.</summary>
        public static string Problem()
        {
            lock (Lock)
            {
                if (_verified)
                {
                    return null;
                }

                var guid = TypeLibGuid;
                var hr = Native.LoadRegTypeLib(ref guid, 5, 0, 0, out var lib);
                if (hr != 0 || lib == null)
                {
                    return "BŁĄD: na tym komputerze nie ma biblioteki typów RF-COM (RFEM 5 nie jest zainstalowany?), kod 0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + ".";
                }

                try
                {
                    var version = Version(lib);
                    foreach (var check in Checks)
                    {
                        var found = MethodAt(lib, check.Item1, check.Item2);
                        if (found != check.Item3)
                        {
                            return "BŁĄD: układ RF-COM (biblioteka typów v" + version + ") jest inny niż ten, dla którego napisano program (v5.7): w " + check.Item1.Name + " miejsce " + check.Item2
                                + " to „" + (found ?? "brak") + "”, oczekiwano „" + check.Item3 + "”. Polecenia na otwartym RFEM są wyłączone, pliki .rf5 czytają się dalej.";
                        }
                    }

                    _verified = true;
                    return null;
                }
                finally
                {
                    Marshal.ReleaseComObject(lib);
                }
            }
        }

        private static string Version(ITypeLib lib)
        {
            lib.GetLibAttr(out var pAttr);
            try
            {
                var attr = (TYPELIBATTR)Marshal.PtrToStructure(pAttr, typeof(TYPELIBATTR));
                return attr.wMajorVerNum + "." + attr.wMinorVerNum;
            }
            finally
            {
                lib.ReleaseTLibAttr(pAttr);
            }
        }

        /// <summary>Nazwa metody interfejsu w danym miejscu vtable (liczonym od pierwszej metody po IUnknown) albo null.</summary>
        private static string MethodAt(ITypeLib lib, Type iface, int slot)
        {
            var iid = iface.GUID;
            ITypeInfo info;
            try
            {
                lib.GetTypeInfoOfGuid(ref iid, out info);
            }
            catch (Exception e) when (RfemCom.IsComFailure(e))
            {
                return null;
            }

            try
            {
                info.GetTypeAttr(out var pAttr);
                int count;
                try
                {
                    count = ((TYPEATTR)Marshal.PtrToStructure(pAttr, typeof(TYPEATTR))).cFuncs;
                }
                finally
                {
                    info.ReleaseTypeAttr(pAttr);
                }

                var wanted = (slot + 3) * IntPtr.Size;
                for (var i = 0; i < count; i++)
                {
                    info.GetFuncDesc(i, out var pDesc);
                    int offset, memid;
                    try
                    {
                        var desc = (FUNCDESC)Marshal.PtrToStructure(pDesc, typeof(FUNCDESC));
                        offset = desc.oVft;
                        memid = desc.memid;
                    }
                    finally
                    {
                        info.ReleaseFuncDesc(pDesc);
                    }

                    if (offset == wanted)
                    {
                        info.GetDocumentation(memid, out var name, out _, out _, out _);
                        return name;
                    }
                }

                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(info);
            }
        }
    }

    /// <summary>
    /// Połączenie z uruchomionym RFEM 5 przez RF-COM: podpina się do otwartej sesji (nigdy nie uruchamia RFEM-a), zajmuje licencję
    /// RF-COM na czas pracy i zwalnia ją w <see cref="Dispose"/> — bez zajętej licencji każde wywołanie wraca z CLASS_E_NOTLICENSED,
    /// a zajęta blokuje okno RFEM użytkownika, więc trzyma się ją krótko. Wszystkie obiekty COM pobrane przez sesję są zwalniane razem z nią.
    /// Używać tylko w wątku STA (<see cref="RfemCom.RunSta{T}"/>). Nic nie zapisuje do modelu.
    /// </summary>
    internal sealed class RfemComSession : IDisposable
    {
        /// <summary>ProgID aplikacji RFEM 5.</summary>
        public const string ProgId = "RFEM5.Application";

        /// <summary>Nazwa procesu RFEM 5 (64-bit).</summary>
        public const string ProcessName = "RFEM64";

        private readonly List<object> _owned = new List<object>();
        private IModel _model;
        private IModelData _data;
        private ILoads _loads;
        private ICalculation _calculation;
        private bool _locked;

        private RfemComSession(IApplication app)
        {
            App = app;
        }

        /// <summary>Aplikacja RFEM.</summary>
        public IApplication App { get; }

        /// <summary>Czy RFEM 5 jest zainstalowany (ProgID zarejestrowany w COM).</summary>
        public static bool IsInstalled() => Native.CLSIDFromProgID(ProgId, out _) == 0;

        /// <summary>Tytuł okna uruchomionego RFEM 5 („RFEM 5.27.01 x64 - nazwa modelu”) albo null, gdy nie działa.</summary>
        public static string RunningWindowTitle()
        {
            var processes = Process.GetProcessesByName(ProcessName);
            try
            {
                var running = processes.FirstOrDefault();
                if (running == null)
                {
                    return null;
                }

                try
                {
                    var title = running.MainWindowTitle;
                    return string.IsNullOrWhiteSpace(title) ? "RFEM 5" : title;
                }
                catch (InvalidOperationException)
                {
                    return "RFEM 5";
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        /// <summary>Podpięcie do uruchomionego RFEM 5 (bez licencji — tę zajmuje <see cref="Lock"/>).</summary>
        public static RfemComSession Connect()
        {
            if (Native.CLSIDFromProgID(ProgId, out var clsid) != 0)
            {
                throw new RfemException("BŁĄD: na tym komputerze nie ma RFEM 5 (brak rejestracji COM " + ProgId + "). Polecenia na otwartym RFEM działają tam, gdzie RFEM 5 jest zainstalowany; z pliku .rf5 czyta rfem_info.");
            }

            var hr = Native.GetActiveObject(ref clsid, IntPtr.Zero, out var active);
            if (hr != 0 || active == null)
            {
                var title = RunningWindowTitle();
                throw new RfemException(title == null
                    ? "BŁĄD: RFEM 5 nie jest uruchomiony. Otwórz model w RFEM 5 i spytaj ponownie (z zapisanego pliku .rf5 czytają polecenia rfem_info, rfem_pret, rfem_sily)."
                    : "BŁĄD: RFEM 5 działa (" + title + "), ale nie oddaje się przez COM (kod 0x" + hr.ToString("X8", CultureInfo.InvariantCulture) + "). Najczęściej RFEM i Amadeusz działają na różnych poziomach uprawnień "
                      + "(jeden jako administrator, drugi nie) albo model nie jest jeszcze otwarty.");
            }

            IApplication app;
            try
            {
                app = (IApplication)active;
            }
            catch (InvalidCastException e)
            {
                Marshal.ReleaseComObject(active);
                throw new RfemException("BŁĄD: obiekt " + ProgId + " nie obsługuje interfejsu IApplication RF-COM (inna wersja RFEM?).", e);
            }

            return new RfemComSession(app);
        }

        /// <summary>Zajmuje licencję RF-COM. Bez niej RFEM odrzuca każde wywołanie; sesja bez dodatku RF-COM (np. licencja „żelbetowa”) daje czytelny komunikat.</summary>
        public void Lock()
        {
            if (_locked)
            {
                return;
            }

            try
            {
                App.LockLicense();
                _locked = true;
            }
            catch (COMException e) when (e.ErrorCode == Native.ClassNotLicensed)
            {
                throw new RfemException("BŁĄD: otwarta sesja RFEM 5 nie ma licencji RF-COM (płatny dodatek, bywa tylko na części licencji biura — w ODS jest na licencji „stalowej”, nie na „żelbetowej”). "
                    + "Sprawdź w RFEM, którą licencję trzyma ta sesja. Z zapisanego pliku .rf5 czytają polecenia rfem_info, rfem_pret, rfem_sily.", e);
            }
        }

        /// <summary>Aktywny (bieżący) model w RFEM.</summary>
        public IModel Model
        {
            get
            {
                if (_model == null)
                {
                    try
                    {
                        _model = Track(App.GetActiveModel());
                    }
                    catch (Exception e) when (RfemCom.IsComFailure(e))
                    {
                        throw new RfemException("BŁĄD: w RFEM 5 nie ma otwartego modelu (" + RfemCom.Message(e) + "). Otwórz model i spytaj ponownie.", e);
                    }
                }

                return _model;
            }
        }

        /// <summary>Dane modelu (węzły, linie, pręty, przekroje, materiały).</summary>
        public IModelData Data => _data ?? (_data = Track(Model.GetModelData()));

        /// <summary>Obciążenia: przypadki i kombinacje.</summary>
        public ILoads Loads => _loads ?? (_loads = Track(Model.GetLoads()));

        /// <summary>Obliczenia i wyniki.</summary>
        public ICalculation Calculation => _calculation ?? (_calculation = Track(Model.GetCalculation()));

        /// <summary>Zapamiętuje obiekt COM do zwolnienia razem z sesją.</summary>
        public T Track<T>(T target) where T : class
        {
            if (target != null)
            {
                _owned.Add(target);
            }

            return target;
        }

        /// <summary>Zwalnia obiekt COM od razu (obiekty pobierane w pętli, np. pręt po pręcie).</summary>
        public static void Release(object target)
        {
            if (target != null && Marshal.IsComObject(target))
            {
                try
                {
                    Marshal.FinalReleaseComObject(target);
                }
                catch (Exception e) when (e is COMException || e is InvalidComObjectException || e is ArgumentException)
                {
                    // Obiekt już zwolniony albo RFEM zamknięty — nic do zrobienia.
                }
            }
        }

        /// <summary>Zwalnia licencję RF-COM (odblokowuje okno RFEM) i wszystkie obiekty COM sesji.</summary>
        public void Dispose()
        {
            if (_locked)
            {
                _locked = false;
                try
                {
                    App.UnlockLicense();
                }
                catch (COMException)
                {
                    // RFEM mógł zostać zamknięty w trakcie — licencja i tak wraca do puli.
                }
            }

            for (var i = _owned.Count - 1; i >= 0; i--)
            {
                Release(_owned[i]);
            }

            _owned.Clear();
            Release(App);
        }
    }

    /// <summary>Wątek STA i komunikaty błędów COM dla RF-COM.</summary>
    internal static class RfemCom
    {
        /// <summary>
        /// Czy wyjątek pochodzi z wywołania COM. CLR zamienia kody HRESULT na różne typy: „obiekt nie został odnaleziony” z RFEM-a
        /// wraca jako FileNotFoundException, E_INVALIDARG jako ArgumentException, E_POINTER jako NullReferenceException — nie tylko COMException.
        /// </summary>
        public static bool IsComFailure(Exception e)
            => e is COMException || e is ExternalException || e is InvalidCastException || e is System.IO.IOException || e is ArgumentException
               || e is InvalidOperationException || e is NotSupportedException || e is UnauthorizedAccessException || e is IndexOutOfRangeException
               || e is NullReferenceException || e is NotImplementedException;

        /// <summary>Pierwsze zdanie komunikatu wyjątku COM z kodem HRESULT.</summary>
        public static string Message(Exception e)
        {
            var text = (e.Message ?? "").Trim().Replace("\r\n", " ").Replace("\n", " ");
            var hresult = e is ExternalException ext ? ext.ErrorCode : e.HResult;
            if (hresult == Native.ClassNotLicensed)
            {
                return "brak licencji RF-COM (CLASS_E_NOTLICENSED)";
            }

            return hresult != 0 ? text + " [0x" + hresult.ToString("X8", CultureInfo.InvariantCulture) + "]" : text;
        }

        /// <summary>
        /// Wykonuje pracę w nowym wątku STA i czeka na wynik; wyjątki COM wracają jako <see cref="RfemException"/> z komunikatem po polsku.
        /// Przerwanie jest sprawdzane tylko przed startem — trwającego wywołania RF-COM (np. okna wyboru w RFEM) nie da się przerwać.
        /// </summary>
        public static T RunSta<T>(Func<T> work, TimeSpan timeout, CancellationToken cancellationToken, string timeoutHint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            T result = default;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = work();
                }
                catch (Exception e)
                {
                    failure = e;
                }
                finally
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            })
            {
                IsBackground = true,
                Name = "LokalneAI RFEM COM",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(timeout))
            {
                throw new RfemException("BŁĄD: RFEM nie odpowiedział w ciągu " + (int)timeout.TotalSeconds + " s" + (string.IsNullOrEmpty(timeoutHint) ? "." : " — " + timeoutHint));
            }

            if (failure == null)
            {
                return result;
            }

            if (failure is RfemException)
            {
                throw failure;
            }

            if (IsComFailure(failure))
            {
                throw new RfemException("BŁĄD RFEM: " + Message(failure), failure);
            }

            throw failure;
        }
    }
}
