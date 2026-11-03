using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AnalizadorFase2
{
    public sealed class Lexeme
    {
        public int Number { get; private set; }
        public int Line { get; private set; }
        public int Column { get; private set; }
        public string Text { get; private set; }
        public string Type { get; private set; }

        public Lexeme(int number, int line, int column, string text, string type)
        {
            Number = number;
            Line = line;
            Column = column;
            Text = text;
            Type = type;
        }
    }

    public static class LexicalAnalysisCore
    {
        // Presentación, diapositivas 6, 10 y 27. Las frases se reconocen aparte.
        private static readonly HashSet<string> Registers = new HashSet<string>(
            "al ah bl bh cl ch dl dh ax bx cx dx si di bp sp cs ds es ss".Split(' '),
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Pseudoinstructions = new HashSet<string>(
            "db dw equ dup ends macro endm proc endp local".Split(' '), StringComparer.OrdinalIgnoreCase);
        private static readonly Regex SegmentPhrase = new Regex(
            @"\G(?:(?:\.(?:code|data|stack))(?:[\t\p{Zs}]+segment)?|(?:code|data|stack)[\t\p{Zs}]+segment)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PointerPhrase = new Regex(
            @"\G(?:byte|word|dword)[\t\p{Zs}]+ptr", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Clase 17: cero inicial adicional, 2/4 dígitos hexadecimales y 8/16 binarios.
        private static readonly Regex Hexadecimal = new Regex(
            @"^[+-]?0(?:[0-9a-f]{2}|[0-9a-f]{4})h$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Binary = new Regex(
            @"^[+-]?(?:[01]{8}|[01]{16})b$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Decimal = new Regex(@"^[+-]?[0-9]+$", RegexOptions.CultureInvariant);

        public static List<Lexeme> Analyze(string source, IEnumerable<string> assignedInstructions)
        {
            HashSet<string> assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (assignedInstructions != null)
                foreach (string instruction in assignedInstructions)
                    if (!String.IsNullOrWhiteSpace(instruction)) assigned.Add(instruction.Trim());

            List<Lexeme> items = new List<Lexeme>();
            string[] lines = (source ?? String.Empty).Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++) AnalyzeLine(lines[i], i + 1, items, assigned);
            return items;
        }

        public static int PageCount(int itemCount, int pageSize)
        {
            if (pageSize <= 0) throw new ArgumentOutOfRangeException("pageSize");
            return itemCount <= 0 ? 0 : 1 + (itemCount - 1) / pageSize;
        }

        public static List<T> GetPage<T>(IList<T> items, int pageIndex, int pageSize)
        {
            if (items == null) throw new ArgumentNullException("items");
            if (pageSize <= 0) throw new ArgumentOutOfRangeException("pageSize");
            List<T> page = new List<T>();
            long start = (long)pageIndex * pageSize;
            if (pageIndex < 0 || start >= items.Count) return page;
            int end = (int)Math.Min(start + pageSize, items.Count);
            for (int i = (int)start; i < end; i++) page.Add(items[i]);
            return page;
        }

        public static bool ValidateSourcePath(string path, out string error)
        {
            error = String.Empty;
            if (String.IsNullOrWhiteSpace(path))
            {
                error = "Selecciona un archivo .ens.";
                return false;
            }
            if (!File.Exists(path))
            {
                error = "El archivo seleccionado no existe.";
                return false;
            }
            if (!String.Equals(Path.GetExtension(path), ".ens", StringComparison.OrdinalIgnoreCase))
            {
                error = "Solo se pueden abrir archivos con extensión .ens.";
                return false;
            }
            return true;
        }

        private static void AnalyzeLine(string line, int lineNumber, List<Lexeme> items, HashSet<string> assigned)
        {
            int[] compoundEnds = BuildCompoundEnds(line);
            int index = 0;
            while (index < line.Length)
            {
                if (line[index] == ';') break;
                if (IsSeparator(line[index])) { index++; continue; }

                int start = index;
                int phraseEnd = MatchPhrase(line, index, SegmentPhrase);
                if (phraseEnd == index) phraseEnd = MatchPhrase(line, index, PointerPhrase);
                if (phraseEnd > index)
                {
                    Add(items, lineNumber, start + 1, line.Substring(start, phraseEnd - start), "Pseudoinstrucción");
                    index = phraseEnd;
                    continue;
                }

                // Solo los compuestos cerrados protegen sus separadores interiores.
                // Si faltan cierres, se recupera la separación sin inventar caracteres.
                while (index < line.Length && !IsBoundary(line[index]))
                {
                    if (compoundEnds[index] > index) index = compoundEnds[index];
                    else index++;
                }
                string text = line.Substring(start, index - start);
                string type = ClassifyCompound(line, start, index, compoundEnds, assigned);
                if (index < line.Length && line[index] == ':')
                {
                    type = type == "Símbolo" ? "Etiqueta" : "Elemento inválido";
                    text += ':';
                    index++;
                }
                Add(items, lineNumber, start + 1, text, type);
            }
        }

        private static int MatchPhrase(string line, int start, Regex expression)
        {
            Match match = expression.Match(line, start);
            int end = match.Index + match.Length;
            return match.Success && (end == line.Length || IsBoundary(line[end])) ? end : start;
        }

        private static bool IsSeparator(char c)
        {
            return Char.IsWhiteSpace(c) || c == ',' || c == ':';
        }

        private static bool IsBoundary(char c)
        {
            return IsSeparator(c) || c == ';';
        }

        // Se construyen los cierres una sola vez por línea. Evita volver a recorrer
        // sufijos completos cuando una entrada contiene muchos paréntesis sin cerrar.
        private static int[] BuildCompoundEnds(string line)
        {
            int[] ends = new int[line.Length];
            Stack<int> openings = new Stack<int>();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == ';') break;
                if (c == '\'' || c == '"')
                {
                    int close = line.IndexOf(c, i + 1);
                    if (close >= 0)
                    {
                        ends[i] = close + 1;
                        i = close;
                    }
                    else openings.Clear();
                    continue;
                }
                if (c == '(' || c == '[') openings.Push(i);
                else if (c == ')' || c == ']')
                {
                    if (openings.Count == 0) continue;
                    int opening = openings.Peek();
                    if ((line[opening] == '(' && c == ')') || (line[opening] == '[' && c == ']'))
                    {
                        openings.Pop();
                        ends[opening] = i + 1;
                    }
                    else openings.Clear();
                }
            }
            return ends;
        }

        private static string ClassifyCompound(string line, int start, int end, int[] compoundEnds, HashSet<string> assigned)
        {
            string text = line.Substring(start, end - start);
            if (text[0] == '\'' || text[0] == '"')
                return compoundEnds[start] == end ? "Constante carácter" : "Elemento inválido";

            if (text.Length >= 4 && text.StartsWith("dup(", StringComparison.OrdinalIgnoreCase))
            {
                bool complete = compoundEnds[start + 3] == end;
                return complete && text.Substring(4, text.Length - 5).Trim().Length > 0
                    ? "Pseudoinstrucción" : "Elemento inválido";
            }

            if (text[0] == '[')
            {
                if (compoundEnds[start] != end || text.IndexOf('[', 1) >= 0) return "Elemento inválido";
                string inner = text.Substring(1, text.Length - 2).Trim();
                if (inner.Length == 0) return "Elemento inválido";
                return Registers.Contains(inner) ? "Referencia a registro" : "Referencia a memoria";
            }
            return ClassifyAtom(text, assigned);
        }

        private static string ClassifyAtom(string text, HashSet<string> assigned)
        {
            if (Registers.Contains(text)) return "Registro";
            if (Pseudoinstructions.Contains(text)) return "Pseudoinstrucción";
            if (assigned.Contains(text)) return "Instrucción";
            if (Hexadecimal.IsMatch(text)) return "Constante numérica hexadecimal";
            if (Binary.IsMatch(text)) return "Constante numérica binaria";
            if (Decimal.IsMatch(text)) return "Constante numérica decimal";
            return IsSymbol(text) ? "Símbolo" : "Elemento inválido";
        }

        private static bool IsSymbol(string text)
        {
            // Diapositiva 8: inicio alfabético y máximo de diez caracteres.
            // Se conserva el guion bajo interior de los nombres del proyecto.
            if (text.Length == 0 || text.Length > 10 || !Char.IsLetter(text[0])) return false;
            for (int i = 1; i < text.Length; i++)
                if (!Char.IsLetterOrDigit(text[i]) && text[i] != '_') return false;
            return true;
        }

        private static void Add(List<Lexeme> items, int line, int column, string text, string type)
        {
            items.Add(new Lexeme(items.Count + 1, line, column, text, type));
        }
    }
}
