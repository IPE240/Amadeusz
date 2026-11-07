using System;
using System.Collections.Generic;
using System.Globalization;

namespace LokalneAI.Harness.Tools
{
    /// <summary>
    /// Kalkulator dla modelu: dokładne rachunki na liczbach dziesiętnych zamiast „z głowy”.
    /// Działania: + - * / % ^, nawiasy, funkcje: suma, srednia, min, max, zaokraglij(x;n), pierwiastek, abs
    /// (także angielskie nazwy: sum, avg, round, sqrt). Separator dziesiętny: przecinek albo kropka;
    /// argumenty funkcji oddziela średnik (albo przecinek przy liczbach z kropką).
    /// </summary>
    public static class Calculator
    {
        public static decimal Evaluate(string expression)
        {
            var parser = new Parser(expression ?? "");
            var value = parser.ParseExpression();
            parser.SkipSpaces();
            if (!parser.AtEnd)
            {
                throw new FormatException("Nieoczekiwany znak „" + parser.Current + "” na pozycji " + (parser.Position + 1) + ".");
            }

            return value;
        }

        /// <summary>Wynik po polsku, np. „62,75”; bez zbędnych zer.</summary>
        public static string Format(decimal value)
        {
            var rounded = Math.Round(value, 10, MidpointRounding.AwayFromZero);
            return rounded.ToString("0.##########", Text.Pl);
        }

        private sealed class Parser
        {
            private const int MaxDepth = 200;
            private readonly string _text;

            // Dla każdej otwartej funkcji: czy przecinek rozdziela argumenty (gdy w nawiasie nie ma średnika).
            private readonly Stack<bool> _commaSeparates = new Stack<bool>();
            private int _depth;

            public Parser(string text)
            {
                _text = text;
            }

            public int Position { get; private set; }

            public bool AtEnd => Position >= _text.Length;

            public char Current => _text[Position];

            public void SkipSpaces()
            {
                while (!AtEnd && char.IsWhiteSpace(Current))
                {
                    Position++;
                }
            }

            public decimal ParseExpression()
            {
                var value = ParseTerm();
                while (true)
                {
                    SkipSpaces();
                    if (Accept('+'))
                    {
                        value += ParseTerm();
                    }
                    else if (Accept('-'))
                    {
                        value -= ParseTerm();
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private decimal ParseTerm()
            {
                var value = ParsePower();
                while (true)
                {
                    SkipSpaces();
                    if (Accept('*') || Accept('×'))
                    {
                        value *= ParsePower();
                    }
                    else if (Accept('/') || Accept(':'))
                    {
                        var divisor = ParsePower();
                        if (divisor == 0)
                        {
                            throw new DivideByZeroException("Dzielenie przez zero.");
                        }

                        value /= divisor;
                    }
                    else if (Accept('%'))
                    {
                        value %= ParsePower();
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private decimal ParsePower()
            {
                var value = ParseUnary();
                SkipSpaces();
                if (Accept('^'))
                {
                    var exponent = ParsePower();
                    if (exponent == Math.Truncate(exponent) && Math.Abs(exponent) <= 64)
                    {
                        var result = 1m;
                        for (var i = 0; i < Math.Abs(exponent); i++)
                        {
                            result *= value;
                        }

                        return exponent < 0 ? 1m / result : result;
                    }

                    return (decimal)Math.Pow((double)value, (double)exponent);
                }

                return value;
            }

            private decimal ParseUnary()
            {
                if (++_depth > MaxDepth)
                {
                    throw new FormatException("Wyrażenie jest zbyt zagnieżdżone.");
                }

                try
                {
                    return ParseUnaryCore();
                }
                finally
                {
                    _depth--;
                }
            }

            private decimal ParseUnaryCore()
            {
                SkipSpaces();
                if (Accept('-'))
                {
                    return -ParseUnary();
                }

                if (Accept('+'))
                {
                    return ParseUnary();
                }

                return ParsePrimary();
            }

            private decimal ParsePrimary()
            {
                SkipSpaces();
                if (AtEnd)
                {
                    throw new FormatException("Niepełne wyrażenie.");
                }

                if (Accept('('))
                {
                    var value = ParseExpression();
                    SkipSpaces();
                    Expect(')');
                    return value;
                }

                if (char.IsDigit(Current))
                {
                    return ParseNumber();
                }

                if (char.IsLetter(Current))
                {
                    return ParseFunction();
                }

                throw new FormatException("Nieoczekiwany znak „" + Current + "” na pozycji " + (Position + 1) + ".");
            }

            private decimal ParseNumber()
            {
                var start = Position;
                while (!AtEnd && char.IsDigit(Current))
                {
                    Position++;
                }

                // Przecinek dziesiętny: poza funkcją zawsze, w funkcji tylko wtedy, gdy argumenty rozdziela średnik
                // (suma(7,5; 8)). Bez średnika przecinek rozdziela argumenty: max(3,5) = 5, a ułamki pisze się z kropką.
                var commaIsDecimal = _commaSeparates.Count == 0 || !_commaSeparates.Peek();
                if (!AtEnd && (Current == '.' || (Current == ',' && commaIsDecimal)) && Position + 1 < _text.Length && char.IsDigit(_text[Position + 1]))
                {
                    Position++;
                    while (!AtEnd && char.IsDigit(Current))
                    {
                        Position++;
                    }
                }

                var raw = _text.Substring(start, Position - start).Replace(',', '.');
                return decimal.Parse(raw, NumberStyles.Number, CultureInfo.InvariantCulture);
            }

            private decimal ParseFunction()
            {
                var start = Position;
                while (!AtEnd && (char.IsLetter(Current) || char.IsDigit(Current) || Current == '_'))
                {
                    Position++;
                }

                var name = Text.Fold(_text.Substring(start, Position - start));
                SkipSpaces();
                Expect('(');
                var semicolons = HasTopLevelSemicolon(Position);
                _commaSeparates.Push(!semicolons);
                var args = new List<decimal>();
                try
                {
                    SkipSpaces();
                    if (!Accept(')'))
                    {
                        while (true)
                        {
                            args.Add(ParseExpression());
                            SkipSpaces();
                            if (Accept(';') || (!semicolons && Accept(',')))
                            {
                                continue;
                            }

                            Expect(')');
                            break;
                        }
                    }
                }
                finally
                {
                    _commaSeparates.Pop();
                }

                return Apply(name, args);
            }

            private bool HasTopLevelSemicolon(int start)
            {
                var depth = 0;
                for (var i = start; i < _text.Length; i++)
                {
                    var c = _text[i];
                    if (c == '(')
                    {
                        depth++;
                    }
                    else if (c == ')')
                    {
                        if (depth == 0)
                        {
                            return false;
                        }

                        depth--;
                    }
                    else if (c == ';' && depth == 0)
                    {
                        return true;
                    }
                }

                return false;
            }

            private static decimal Apply(string name, List<decimal> args)
            {
                switch (name)
                {
                    case "suma":
                    case "sum":
                        return Sum(args);
                    case "srednia":
                    case "avg":
                    case "mean":
                        Require(name, args, 1, int.MaxValue);
                        return Sum(args) / args.Count;
                    case "min":
                        Require(name, args, 1, int.MaxValue);
                        return Fold(args, Math.Min);
                    case "max":
                        Require(name, args, 1, int.MaxValue);
                        return Fold(args, Math.Max);
                    case "zaokraglij":
                    case "round":
                        Require(name, args, 1, 2);
                        var digits = args.Count > 1 ? (int)Math.Max(0, Math.Min(28, args[1])) : 0;
                        return Math.Round(args[0], digits, MidpointRounding.AwayFromZero);
                    case "pierwiastek":
                    case "sqrt":
                        Require(name, args, 1, 1);
                        if (args[0] < 0)
                        {
                            throw new FormatException("Pierwiastek z liczby ujemnej.");
                        }

                        return (decimal)Math.Sqrt((double)args[0]);
                    case "abs":
                        Require(name, args, 1, 1);
                        return Math.Abs(args[0]);
                    default:
                        throw new FormatException("Nieznana funkcja „" + name + "”. Dostępne: suma, srednia, min, max, zaokraglij, pierwiastek, abs.");
                }
            }

            private static decimal Sum(List<decimal> args)
            {
                var total = 0m;
                foreach (var a in args)
                {
                    total += a;
                }

                return total;
            }

            private static decimal Fold(List<decimal> args, Func<decimal, decimal, decimal> f)
            {
                var value = args[0];
                for (var i = 1; i < args.Count; i++)
                {
                    value = f(value, args[i]);
                }

                return value;
            }

            private static void Require(string name, List<decimal> args, int min, int max)
            {
                if (args.Count < min || args.Count > max)
                {
                    throw new FormatException("Zła liczba argumentów funkcji „" + name + "”.");
                }
            }

            private bool Accept(char c)
            {
                if (!AtEnd && Current == c)
                {
                    Position++;
                    return true;
                }

                return false;
            }

            private void Expect(char c)
            {
                if (!Accept(c))
                {
                    throw new FormatException("Oczekiwano „" + c + "” na pozycji " + (Position + 1) + ".");
                }
            }
        }
    }
}
