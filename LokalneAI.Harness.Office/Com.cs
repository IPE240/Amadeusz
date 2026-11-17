using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace LokalneAI.Harness.Office
{
    /// <summary>Błąd automatyzacji Office’a z komunikatem dla modelu (po polsku, bez śladu stosu).</summary>
    public sealed class OfficeException : Exception
    {
        /// <summary>Tworzy błąd z komunikatem dla modelu.</summary>
        public OfficeException(string message) : base(message)
        {
        }

        /// <summary>Tworzy błąd z komunikatem i przyczyną (wyjątek COM).</summary>
        public OfficeException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Późne wiązanie COM (<c>Type.InvokeMember</c> przez IDispatch), żeby nie zależeć od bibliotek Interop ani wersji Office’a.
    /// Działa na .NET Framework 4.8 i .NET 5+ (tylko Windows). Każde wywołanie z profilu idzie w osobnym wątku STA
    /// (<see cref="RunSta{T}"/>), bo Office tego wymaga, a wykonawca jest wołany z wątku w tle.
    /// </summary>
    internal static class Com
    {
        [DllImport("ole32.dll")]
        private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

        [DllImport("oleaut32.dll")]
        private static extern int GetActiveObject(ref Guid clsid, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object obj);

        /// <summary>Wartość właściwości albo wynik metody bez argumentów (w bibliotekach typów Office’a <c>Item</c> bywa metodą).</summary>
        public static object Get(object target, string name, params object[] args)
            => Invoke(target, name, BindingFlags.GetProperty | BindingFlags.InvokeMethod, args);

        /// <summary>
        /// Wywołanie metody. Najpierw jako metoda, a gdy Office nie zna takiej metody — jako właściwość z argumentami.
        /// Obie flagi naraz nie są bezpieczne: .NET Framework z zarejestrowaną biblioteką Interop (typ <c>WorkbookClass</c>)
        /// wysyłał wtedy <c>SaveAs</c> Excela jako odczyt właściwości („Pobranie właściwości SaveAs … nie jest możliwe”).
        /// </summary>
        public static object Call(object target, string name, params object[] args)
        {
            try
            {
                return Invoke(target, name, BindingFlags.InvokeMethod, args);
            }
            catch (OfficeException e) when (IsMemberNotFound(e))
            {
                return Invoke(target, name, BindingFlags.GetProperty, args);
            }
        }

        /// <summary>DISP_E_MEMBERNOTFOUND (0x80020003) albo DISP_E_UNKNOWNNAME (0x80020006): Office nie zna elementu w tej roli.</summary>
        private static bool IsMemberNotFound(OfficeException e)
        {
            var code = e.InnerException is ExternalException ext ? ext.ErrorCode : 0;
            return code == unchecked((int)0x80020003) || code == unchecked((int)0x80020006) || e.InnerException is MissingMemberException;
        }

        /// <summary>Ustawienie właściwości.</summary>
        public static void Set(object target, string name, object value)
            => Invoke(target, name, BindingFlags.SetProperty, new[] { value });

        /// <summary>Tekst właściwości (null → pusty).</summary>
        public static string Text(object target, string name, params object[] args)
            => Get(target, name, args)?.ToString() ?? "";

        /// <summary>Liczba całkowita z właściwości (np. <c>Count</c>).</summary>
        public static int Int(object target, string name, params object[] args)
            => Convert.ToInt32(Get(target, name, args) ?? 0, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Wartość logiczna z właściwości.</summary>
        public static bool Bool(object target, string name, params object[] args)
            => Convert.ToBoolean(Get(target, name, args) ?? false, System.Globalization.CultureInfo.InvariantCulture);

        private static object Invoke(object target, string name, BindingFlags flags, object[] args)
        {
            if (target == null)
            {
                throw new OfficeException("BŁĄD: obiekt Office’a nie istnieje (" + name + ").");
            }

            try
            {
                return target.GetType().InvokeMember(name, flags, null, target, args ?? new object[0]);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                throw new OfficeException(Message(e.InnerException, name), e.InnerException);
            }
            catch (COMException e)
            {
                throw new OfficeException(Message(e, name), e);
            }
            catch (MissingMemberException e)
            {
                throw new OfficeException("BŁĄD: Office nie zna elementu „" + name + "” (" + e.Message + ").", e);
            }
        }

        private static string Message(Exception e, string member = null)
        {
            var text = (e.Message ?? "").Trim();
            var cut = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (cut > 0)
            {
                text = text.Substring(0, cut); // Office dokleja listę „możliwych przyczyn” — model potrzebuje tylko pierwszego zdania
            }

            var hresult = e is COMException com ? com.ErrorCode : (e is ExternalException ext ? ext.ErrorCode : 0);
            if (hresult == unchecked((int)0x800A01A8))
            {
                text = "obiekt nie istnieje (błąd 424 Office’a: 0x800A01A8) — " + text;
            }

            return "BŁĄD Office" + (member == null ? "" : " (" + member + ")") + ": " + text.Replace("\r\n", " ").Replace("\n", " ");
        }

        /// <summary>Czy program o tym ProgID jest zainstalowany (zarejestrowany w COM).</summary>
        public static bool IsInstalled(string progId) => Type.GetTypeFromProgID(progId) != null;

        /// <summary>Działająca instancja programu (ROT) albo null.</summary>
        public static object Running(string progId)
        {
            if (CLSIDFromProgID(progId, out var clsid) != 0)
            {
                return null;
            }

            return GetActiveObject(ref clsid, IntPtr.Zero, out var active) == 0 ? active : null;
        }

        /// <summary>Nowa instancja programu; wyjątek, gdy nie jest zainstalowany.</summary>
        public static object Create(string progId)
        {
            var type = Type.GetTypeFromProgID(progId);
            if (type == null)
            {
                throw new OfficeException("BŁĄD: na tym komputerze nie ma programu " + progId.Replace(".Application", "") + " (brak rejestracji COM).");
            }

            try
            {
                return Activator.CreateInstance(type);
            }
            catch (Exception e) when (e is COMException || e is TargetInvocationException || e is InvalidCastException)
            {
                throw new OfficeException("BŁĄD: nie udało się uruchomić programu " + progId.Replace(".Application", "") + ": " + (e.InnerException ?? e).Message, e);
            }
        }

        /// <summary>Zwalnia obiekt COM (ignoruje błędy i obiekty spoza COM).</summary>
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
                    // Obiekt już zwolniony albo proces zamknięty — nic do zrobienia.
                }
            }
        }

        /// <summary>Zwalnia kilka obiektów COM w odwrotnej kolejności.</summary>
        public static void Release(IList<object> targets)
        {
            for (var i = targets.Count - 1; i >= 0; i--)
            {
                Release(targets[i]);
            }
        }

        /// <summary>
        /// Wykonuje pracę w nowym wątku STA i czeka na wynik. Wyjątek z wątku wraca do wołającego.
        /// Przerwanie (<paramref name="cancellationToken"/>) jest sprawdzane przed startem; trwającego wywołania COM nie da się przerwać.
        /// </summary>
        public static T RunSta<T>(Func<T> work, CancellationToken cancellationToken, TimeSpan timeout)
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
                Name = "LokalneAI Office COM",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(timeout))
            {
                throw new OfficeException("BŁĄD: Office nie odpowiedział w ciągu " + (int)timeout.TotalSeconds + " s (może czeka na okno dialogowe — sprawdź program).");
            }

            if (failure != null)
            {
                if (failure is OfficeException)
                {
                    throw failure;
                }

                throw new OfficeException(Message(failure), failure);
            }

            return result;
        }
    }
}
