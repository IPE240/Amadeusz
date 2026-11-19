using System;
using System.ComponentModel;
using LokalneAI.Harness.Session;

namespace LokalneAI.Harness.Wpf
{
    /// <summary>
    /// Kafelek podagenta nad polem polecenia (<see cref="HarnessPanel.AgentTilesList"/>): numer, komputer, model, co robi
    /// i od ilu sekund, z przyciskiem „Przerwij”. Widok <see cref="SubAgentInfo"/>; odświeżany przy zdarzeniach sesji i co sekundę.
    /// </summary>
    public sealed class AgentTile : INotifyPropertyChanged
    {
        private string _status = "";

        /// <summary>Tworzy kafelek dla podagenta.</summary>
        public AgentTile(SubAgentInfo info)
        {
            Info = info ?? throw new ArgumentNullException(nameof(info));
            Refresh();
        }

        /// <summary>Zmiana stanu (odświeża widok).</summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>Stan podagenta z sesji.</summary>
        public SubAgentInfo Info { get; }

        /// <summary>Numer podagenta w sesji.</summary>
        public int Number => Info.Number;

        /// <summary>Zadanie (podpowiedź po najechaniu).</summary>
        public string Task => Info.Task;

        /// <summary>„Podagent 2 · PC-ODS12 · gemma4:12b”.</summary>
        public string Title => "Podagent " + Info.Number + " · " + (Info.Machine ?? "?") + " · " + (Info.Model ?? "?");

        /// <summary>„pliki · Model pracuje (krok 2 z 12)… · 35 s”.</summary>
        public string Status => _status;

        /// <summary>Czy jeszcze pracuje (przycisk „Przerwij” aktywny).</summary>
        public bool IsRunning => Info.IsRunning;

        /// <summary>Przerywa tego podagenta (jeśli jeszcze pracuje).</summary>
        public void Cancel()
        {
            var cancel = Info.Cancel;
            if (cancel != null && Info.IsRunning)
            {
                _status = "przerywam…";
                Notify(nameof(Status));
                cancel();
            }
        }

        /// <summary>Przelicza napisy ze stanu podagenta (po zdarzeniu sesji i co sekundę).</summary>
        public void Refresh()
        {
            var elapsed = Info.IsRunning && Info.Started != default(DateTime) ? DateTime.UtcNow - Info.Started : Info.Duration;
            var seconds = Math.Max(0, (int)Math.Round(elapsed.TotalSeconds));
            _status = (string.IsNullOrWhiteSpace(Info.Skills) ? "" : Info.Skills + " · ")
                + (string.IsNullOrWhiteSpace(Info.Status) ? "startuje" : Info.Status)
                + " · " + seconds + " s";
            Notify(nameof(Status));
            Notify(nameof(Title));
            Notify(nameof(IsRunning));
        }

        private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
