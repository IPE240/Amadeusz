namespace LokalneAI.Harness.Tools
{
    /// <summary>Co zrobić z wywołaniem przed wykonaniem.</summary>
    public enum ConfirmationKind
    {
        /// <summary>Wykonać bez pytania (np. polecenie tylko czyta w dozwolonym folderze).</summary>
        None,

        /// <summary>Zapytać użytkownika (<see cref="Session.SessionOptions.ConfirmMutation"/>).</summary>
        Ask,

        /// <summary>Odrzucić bez pytania (np. ścieżka poza dozwolonymi folderami); powód wraca do modelu.</summary>
        Reject,
    }

    /// <summary>Decyzja wykonawcy o konkretnym wywołaniu — zależy od argumentów, nie tylko od rodzaju polecenia.</summary>
    public sealed class ConfirmationDecision
    {
        private ConfirmationDecision(ConfirmationKind kind, string message, bool dangerous)
        {
            Kind = kind;
            Message = message;
            Dangerous = dangerous;
        }

        public ConfirmationKind Kind { get; }

        /// <summary>Treść pytania (dla <see cref="ConfirmationKind.Ask"/>) albo powód odrzucenia.</summary>
        public string Message { get; }

        /// <summary>Polecenie może coś bezpowrotnie zniszczyć (np. kasuje pliki) — okno pytania powinno domyślnie odmawiać.</summary>
        public bool Dangerous { get; }

        public static ConfirmationDecision NotNeeded { get; } = new ConfirmationDecision(ConfirmationKind.None, null, false);

        public static ConfirmationDecision Ask(string message, bool dangerous = false) => new ConfirmationDecision(ConfirmationKind.Ask, message, dangerous);

        public static ConfirmationDecision Reject(string reason) => new ConfirmationDecision(ConfirmationKind.Reject, reason, false);
    }

    /// <summary>
    /// Wykonawca, który sam ocenia każde wywołanie (zamiast stałego <c>zmienia_model</c> z profilu):
    /// np. polecenie PowerShella, które tylko czyta, idzie bez pytania, a kasujące — z pytaniem.
    /// Sesja pyta o decyzję przed wykonaniem; wykonawca bez tego interfejsu działa po staremu.
    /// </summary>
    public interface IConfirmingExecutor
    {
        ConfirmationDecision Decide(ToolCall call);
    }
}
