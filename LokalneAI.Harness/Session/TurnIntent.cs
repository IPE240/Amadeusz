using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Session
{
    /// <summary>
    /// Czego użytkownik wyraźnie chce w poleceniu — rozpoznawane po słowach, nie przez model (mały model potrafi to zignorować):
    /// <list type="bullet">
    /// <item><see cref="AsksForWeb"/> — „sprawdź w internecie”, „w necie”, „online”, „w Google”: sesja z narzędziami internetu
    /// musi z nich skorzystać, nawet gdy model „wie” (wiedza modelu bywa nieaktualna).</item>
    /// <item><see cref="AsksForCorrection"/> — „popraw”, „jeszcze raz”, „to nie tak”, „źle”: model ma nie powtarzać poprzedniej
    /// odpowiedzi, tylko sprawdzić ją ponownie i dać poprawioną.</item>
    /// </list>
    /// </summary>
    public static class TurnIntent
    {
        private static readonly Regex Web = new Regex(
            @"\b(interne\w*|necie|necik\w*|online|\w*googl\w*|wyszukiwar\w*|www)\b|\bw\s+sieci\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex NoWeb = new Regex(
            @"\bbez\s+(interne\w*|sieci|necie)\b|\bnie\s+(szukaj|sprawdzaj|uzywaj|korzystaj)\w*\s+(w\s+|z\s+)?(interne\w*|sieci|necie)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex Correction = new Regex(
            @"\b(popraw\w*|jeszcze\s+raz|ponownie|od\s+nowa|inaczej|zle|nieprawd\w*|nie\s+tak|nie\s+to|to\s+nie\s+(jest\s+)?(prawda|dobrze|to)|lepsz\w*\s+odpowiedz|zmien\s+odpowiedz|sprawdz\s+(to\s+)?(jeszcze|ponownie|dokladniej)|nie\s+zgadza)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Użytkownik wyraźnie prosi o sprawdzenie w internecie (i nie zabrania go).</summary>
        public static bool AsksForWeb(string text)
        {
            var folded = Text.Fold(text ?? "");
            return Web.IsMatch(folded) && !NoWeb.IsMatch(folded);
        }

        /// <summary>Użytkownik prosi o poprawioną albo inną odpowiedź niż poprzednia.</summary>
        public static bool AsksForCorrection(string text) => Correction.IsMatch(Text.Fold(text ?? ""));

        /// <summary>Uwaga dla modelu doklejana do polecenia z prośbą o poprawkę.</summary>
        public const string CorrectionHint =
            "[Uwaga programu: użytkownik nie jest zadowolony z poprzedniej odpowiedzi. Nie powtarzaj jej. Sprawdź ją jeszcze raz krok po kroku, "
            + "znajdź błąd albo braki i podaj poprawioną, inną wersję; jeśli poprzednia była dobra, powiedz krótko dlaczego i co dodajesz.]";

        /// <summary>Uwaga dla modelu, gdy użytkownik prosi o internet, a sesja go ma.</summary>
        public const string WebHint =
            "[Uwaga programu: użytkownik wyraźnie prosi o sprawdzenie w internecie. Zanim odpowiesz, użyj narzędzia szukaj_w_internecie "
            + "(i czytaj_strone dla 1–2 najlepszych wyników) — nawet jeśli znasz odpowiedź, bo Twoja wiedza może być nieaktualna. "
            + "Odpowiedz na podstawie znalezionych stron i podaj „Źródła:”.]";

        /// <summary>Uwaga dla modelu, gdy użytkownik prosi o internet, a sesja go nie ma.</summary>
        public const string NoWebHint =
            "[Uwaga programu: użytkownik prosi o sprawdzenie w internecie, ale ta rozmowa nie ma dostępu do internetu. Nie udawaj, że sprawdziłeś. "
            + "Powiedz to na początku odpowiedzi jednym zdaniem: trzeba włączyć „Odblokuj dostęp do internetu” (a w zakładce Code umiejętność musi na to pozwalać); "
            + "potem możesz podać, co wiesz, z zaznaczeniem, że to bez sprawdzenia.]";
    }
}
