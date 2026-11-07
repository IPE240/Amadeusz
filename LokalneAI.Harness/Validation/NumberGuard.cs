using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace LokalneAI.Harness.Validation
{
    /// <summary>
    /// Sprawdza, czy liczby w odpowiedzi modelu pochodzą z wyników narzędzi albo z pytania.
    /// Małe modele mylą się w rachunkach — liczba „z głowy” to sygnał błędu.
    /// Liczby sklejone z literami (HEB300, S355, M24) nie są brane pod uwagę.
    /// </summary>
    public static class NumberGuard
    {
        // 1 234 567,89 | 1234,5 | 1234.5 | 845 — bez cyfr/liter tuż przed i po.
        private static readonly Regex NumberPattern = new Regex(
            @"(?<![\p{L}\d.,])(\d{1,3}(?:[  ]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?)(?![\p{L}\d])",
            RegexOptions.Compiled);

        public static IList<decimal> Extract(string text)
        {
            var numbers = new List<decimal>();
            foreach (Match match in NumberPattern.Matches(text ?? ""))
            {
                var raw = match.Groups[1].Value.Replace(" ", "").Replace(" ", "").Replace(',', '.');
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                {
                    numbers.Add(value);
                }
            }

            return numbers;
        }

        /// <summary>
        /// Liczby z odpowiedzi, których nie ma wśród dozwolonych (po zaokrągleniu do liczby miejsc po przecinku
        /// użytej w odpowiedzi). Liczby całkowite 0–31 są pomijane (dni, miesiące, „2 projekty”).
        /// </summary>
        public static IList<decimal> FindUnsupported(string answer, IEnumerable<string> sources)
        {
            var allowed = sources.SelectMany(Extract).ToList();
            var unsupported = new List<decimal>();
            foreach (Match match in NumberPattern.Matches(answer ?? ""))
            {
                var raw = match.Groups[1].Value.Replace(" ", "").Replace(" ", "").Replace(',', '.');
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }

                if (value == Math.Truncate(value) && value >= 0 && value <= 31)
                {
                    continue;
                }

                var dot = raw.IndexOf('.');
                var decimals = Math.Min(28, dot < 0 ? 0 : raw.Length - dot - 1);
                if (!allowed.Any(a => Math.Round(a, decimals, MidpointRounding.AwayFromZero) == value || a == value))
                {
                    unsupported.Add(value);
                }
            }

            return unsupported;
        }
    }
}
