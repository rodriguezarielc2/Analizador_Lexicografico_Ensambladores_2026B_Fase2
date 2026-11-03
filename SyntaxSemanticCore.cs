using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace AnalizadorFase2
{
    public static class SyntaxSemanticCore
    {
        private enum SegmentKind { None, Stack, Data, Code }

        private sealed class ParsedValue
        {
            public long Number;
            public string Display;
            public bool IsCharacter;
            public int CharacterCount;
        }

        private sealed class Operand
        {
            public bool IsRegister;
            public bool IsMemory;
            public bool IsImmediate;
            public bool IsSymbol;
            public bool IsPtrQualified;
            public bool HasKnownAllocation;
            public int Width;
            public int AllocationBytes;
            public string Name;
            public long Immediate;
        }

        private sealed class EffectiveAddress
        {
            public string SymbolName;
            public int ElementWidth;
            public int AllocationBytes;
            public bool HasKnownAllocation;
        }

        private static readonly Regex SegmentPattern = new Regex(
            @"^(?:\.(stack|data|code)(?:\s+segment)?|(stack|data|code)\s+segment)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex DeclarationPattern = new Regex(
            @"^(\p{L}[\p{L}\p{Nd}_]{0,9})\s+(db|dw|equ)\s+(.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex CountDupPattern = new Regex(
            @"^([+-]?(?:[0-9]+|0(?:[0-9a-f]{2}|[0-9a-f]{4})h|(?:[01]{8}|[01]{16})b))\s+dup\s*\(\s*(.*?)\s*\)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex NamePattern = new Regex(
            @"^\p{L}[\p{L}\p{Nd}_]{0,9}$", RegexOptions.CultureInvariant);
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AL", "AH", "BL", "BH", "CL", "CH", "DL", "DH", "AX", "BX", "CX", "DX",
            "SI", "DI", "BP", "SP", "CS", "DS", "ES", "SS", "DB", "DW", "EQU", "DUP",
            "ENDS", "MACRO", "ENDM", "PROC", "ENDP", "LOCAL", "BYTE", "WORD", "DWORD", "PTR",
            "CBW", "CLC", "LODSB", "LODSW", "STOSB", "AAA", "POP", "IDIV", "INT", "AND",
            "ADC", "LEA", "LES", "JNZ", "JZ", "LOOPNZ", "JGE", "JNA", "JNL", "JB"
        };

        public static ProgramAnalysisResult Analyze(string source, IList<Lexeme> lexemes,
            IEnumerable<string> assignedInstructions)
        {
            ProgramAnalysisResult result = new ProgramAnalysisResult();
            string[] lines = (source ?? String.Empty).Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            Dictionary<string, SymbolEntry> symbols = new Dictionary<string, SymbolEntry>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> instructions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (assignedInstructions != null)
                foreach (string item in assignedInstructions)
                    if (!String.IsNullOrWhiteSpace(item)) instructions.Add(item.Trim());

            for (int i = 0; i < lines.Length; i++) result.Lines.Add(new LineAnalysisResult(i + 1, lines[i]));
            List<Lexeme>[] byLine = GroupLexemes(lines.Length, lexemes);
            SegmentKind active = SegmentKind.None;
            SegmentKind expected = SegmentKind.Stack;
            int activeStartLine = 0;
            bool activeHasRequiredContent = false;
            HashSet<SegmentKind> seen = new HashSet<SegmentKind>();

            for (int i = 0; i < lines.Length; i++)
            {
                string code = RemoveComment(lines[i]).Trim();
                LineAnalysisResult lineResult = result.Lines[i];
                if (code.Length == 0) continue;
                lineResult.SetValid("Sintaxis reconocida.");

                Match segmentMatch = SegmentPattern.Match(code);
                if (segmentMatch.Success)
                {
                    string segmentName = segmentMatch.Groups[1].Success
                        ? segmentMatch.Groups[1].Value : segmentMatch.Groups[2].Value;
                    SegmentKind next = ParseSegment(segmentName);
                    if (active != SegmentKind.None)
                    {
                        lineResult.AddError("No se puede iniciar un segmento antes de cerrar el segmento " + SegmentName(active) + ".");
                        continue;
                    }
                    if (seen.Contains(next))
                    {
                        lineResult.AddError("El segmento " + SegmentName(next) + " está duplicado.");
                        continue;
                    }
                    if (next != expected)
                    {
                        lineResult.AddError("Orden incorrecto: se esperaba el segmento " + SegmentName(expected) + ".");
                        continue;
                    }
                    active = next;
                    activeStartLine = i + 1;
                    activeHasRequiredContent = false;
                    seen.Add(next);
                    expected = next == SegmentKind.Stack ? SegmentKind.Data :
                        (next == SegmentKind.Data ? SegmentKind.Code : SegmentKind.None);
                    lineResult.SetValid("Inicio del segmento " + SegmentName(next) + ".");
                    continue;
                }

                if (String.Equals(code, "ends", StringComparison.OrdinalIgnoreCase))
                {
                    if (active == SegmentKind.None)
                        lineResult.AddError("ENDS aparece fuera de un segmento.");
                    else
                    {
                        if (!activeHasRequiredContent && active == SegmentKind.Stack)
                            lineResult.AddError("El segmento .stack debe contener al menos una reserva DW cantidad DUP(...).");
                        else if (!activeHasRequiredContent && active == SegmentKind.Data)
                            lineResult.AddError("El segmento .data debe contener al menos una declaración DB, DW o EQU.");
                        lineResult.SetValid("Fin del segmento " + SegmentName(active) + ".");
                        active = SegmentKind.None;
                        activeStartLine = 0;
                        activeHasRequiredContent = false;
                    }
                    continue;
                }

                if (active == SegmentKind.None)
                {
                    lineResult.AddError("La línea debe estar dentro del segmento .stack, .data o .code correspondiente.");
                    continue;
                }

                for (int j = 0; j < byLine[i].Count; j++)
                {
                    if (byLine[i][j].Type == "Elemento inválido")
                        lineResult.AddError("Elemento inválido: " + byLine[i][j].Text + ".");
                }
                if (lineResult.Status == "Incorrecta") continue;

                if (active == SegmentKind.Stack)
                    activeHasRequiredContent |= AnalyzeStackLine(code, lineResult);
                else if (active == SegmentKind.Data)
                    activeHasRequiredContent |= AnalyzeDataLine(code, lineResult, symbols, result.Symbols, i + 1);
                else
                    AnalyzeCodeLine(code, lineResult, symbols, result.Symbols, instructions, i + 1);
            }

            if (active != SegmentKind.None && activeStartLine > 0)
                result.Lines[activeStartLine - 1].AddError("El segmento " + SegmentName(active) + " no tiene su línea ENDS.");
            if (!seen.Contains(SegmentKind.Stack)) AddMissingSegment(result, SegmentKind.Stack);
            if (!seen.Contains(SegmentKind.Data)) AddMissingSegment(result, SegmentKind.Data);
            if (!seen.Contains(SegmentKind.Code)) AddMissingSegment(result, SegmentKind.Code);
            return result;
        }

        private static List<Lexeme>[] GroupLexemes(int lineCount, IList<Lexeme> lexemes)
        {
            List<Lexeme>[] grouped = new List<Lexeme>[lineCount];
            for (int i = 0; i < lineCount; i++) grouped[i] = new List<Lexeme>();
            if (lexemes == null) return grouped;
            foreach (Lexeme token in lexemes)
                if (token != null && token.Line >= 1 && token.Line <= lineCount) grouped[token.Line - 1].Add(token);
            return grouped;
        }

        private static void AddMissingSegment(ProgramAnalysisResult result, SegmentKind kind)
        {
            int index = Math.Max(0, result.Lines.Count - 1);
            for (int i = result.Lines.Count - 1; i >= 0; i--)
            {
                if (RemoveComment(result.Lines[i].Source).Trim().Length > 0) { index = i; break; }
            }
            result.Lines[index].AddError("Falta la definición completa del segmento " + SegmentName(kind) + ".");
        }

        private static bool AnalyzeStackLine(string code, LineAnalysisResult result)
        {
            Match declaration = Regex.Match(code, @"^dw\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            Match dup = declaration.Success ? CountDupPattern.Match(declaration.Groups[1].Value.Trim()) : Match.Empty;
            if (!dup.Success)
            {
                result.AddError("En .stack se esperaba DW cantidad DUP(constante numérica de palabra).");
                return false;
            }
            long count;
            string countError;
            if (!TryParseWordCount(dup.Groups[1].Value, out count, out countError))
                result.AddError("La cantidad de palabras " + countError);
            ParsedValue item;
            string error;
            if (!TryParseWordValue(dup.Groups[2].Value.Trim(), out item, out error))
                result.AddError("El valor de DUP debe ser constante numérica de palabra: " + error);
            if (result.Status != "Incorrecta") result.SetValid("Declaración de reserva de pila válida.");
            return result.Status != "Incorrecta";
        }

        private static bool AnalyzeDataLine(string code, LineAnalysisResult result,
            Dictionary<string, SymbolEntry> symbols, IList<SymbolEntry> orderedSymbols, int lineNumber)
        {
            Match match = DeclarationPattern.Match(code);
            if (!match.Success)
            {
                result.AddError("En .data se esperaba una declaración de símbolo con DB, DW o EQU.");
                return false;
            }
            string name = match.Groups[1].Value;
            string directive = match.Groups[2].Value.ToUpperInvariant();
            string valueText = match.Groups[3].Value.Trim();
            if (!IsValidSymbolName(name))
            {
                result.AddError("El nombre de símbolo debe comenzar con letra y tener como máximo 10 caracteres.");
                return false;
            }
            if (symbols.ContainsKey(name))
            {
                result.AddError("El símbolo " + name + " ya fue declarado.");
                return false;
            }

            string type;
            string value;
            string size;
            string error;
            if (directive == "EQU")
            {
                ParsedValue parsed;
                if (!TryParseWordValue(valueText, out parsed, out error)) { result.AddError("EQU: " + error); return false; }
                type = "Constante EQU";
                value = parsed.Display;
                size = FormatSize(2);
            }
            else if (!TryBuildDeclaration(directive, valueText, out type, out value, out size, out error))
            {
                result.AddError(directive + ": " + error);
                return false;
            }

            SymbolEntry entry = new SymbolEntry(name, type, value, size, lineNumber);
            symbols.Add(name, entry);
            orderedSymbols.Add(entry);
            result.SetValid("Declaración válida: " + name + " ocupa " + size + ".");
            return true;
        }

        private static bool TryBuildDeclaration(string directive, string valueText,
            out string type, out string value, out string size, out string error)
        {
            type = String.Empty; value = String.Empty; size = String.Empty; error = String.Empty;
            bool isByte = directive == "DB";
            int width = isByte ? 1 : 2;
            Match dup = CountDupPattern.Match(valueText);
            if (dup.Success)
            {
                long count;
                if (!TryParseWordCount(dup.Groups[1].Value, out count, out error))
                { error = "la cantidad de DUP " + error; return false; }
                string itemText = dup.Groups[2].Value.Trim();
                ParsedValue item;
                bool valid = isByte ? TryParseByteValue(itemText, out item, out error) : TryParseWordValue(itemText, out item, out error);
                if (!valid) return false;
                long bytes = count * (isByte && item.IsCharacter ? item.CharacterCount : width);
                if (bytes > Int32.MaxValue) { error = "el tamaño total excede el límite permitido."; return false; }
                type = isByte ? "Arreglo DB" : "Arreglo DW";
                value = count.ToString(CultureInfo.InvariantCulture) + " DUP(" + item.Display + ")";
                size = FormatSize((int)bytes);
                return true;
            }

            ParsedValue parsedValue;
            bool parsed = isByte ? TryParseByteValue(valueText, out parsedValue, out error) : TryParseWordValue(valueText, out parsedValue, out error);
            if (!parsed) return false;
            type = isByte ? (parsedValue.IsCharacter ? "Dato DB carácter" : "Dato DB numérico") : "Dato DW";
            value = parsedValue.Display;
            size = FormatSize(isByte && parsedValue.IsCharacter ? Math.Max(1, parsedValue.CharacterCount) : width);
            return true;
        }

        private static bool TryParseByteValue(string text, out ParsedValue value, out string error)
        {
            if (TryParseCharacter(text, out value)) { error = String.Empty; return true; }
            if (!TryParseNumber(text, out value, out error)) return false;
            if (value.Number < -128 || value.Number > 255) { error = "el valor byte debe estar entre -128 y 255."; return false; }
            return true;
        }

        private static bool TryParseWordValue(string text, out ParsedValue value, out string error)
        {
            if (TryParseCharacter(text, out value)) { error = "una palabra requiere constante numérica."; return false; }
            if (!TryParseNumber(text, out value, out error)) return false;
            if (value.Number < -32768 || value.Number > 65535) { error = "el valor palabra debe estar entre -32768 y 65535."; return false; }
            return true;
        }

        private static bool TryParseCharacter(string text, out ParsedValue value)
        {
            value = null;
            if (String.IsNullOrEmpty(text) || text.Length < 2) return false;
            char quote = text[0];
            if ((quote != '\'' && quote != '"') || text[text.Length - 1] != quote) return false;
            string contents = text.Substring(1, text.Length - 2);
            if (contents.Length == 0 || contents.Length > 65535) return false;
            foreach (char character in contents)
                if (character > 255) return false;
            value = new ParsedValue { Display = text, IsCharacter = true, CharacterCount = contents.Length };
            return true;
        }

        private static bool TryParseNumber(string text, out ParsedValue value, out string error)
        {
            value = null;
            error = "se esperaba una constante numérica decimal, hexadecimal o binaria.";
            string number = text.Trim();
            long parsed;
            if (Regex.IsMatch(number, @"^[+-]?[0-9]+$", RegexOptions.CultureInvariant))
            {
                if (!Int64.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out parsed))
                { error = "la constante decimal está fuera de rango."; return false; }
            }
            else if (Regex.IsMatch(number, @"^[+-]?0(?:[0-9a-f]{2}|[0-9a-f]{4})h$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string digits = number.Substring(0, number.Length - 1);
                bool negative = digits.StartsWith("-", StringComparison.Ordinal);
                bool positive = digits.StartsWith("+", StringComparison.Ordinal);
                digits = digits.TrimStart('+', '-').Substring(1);
                parsed = Int64.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                if (negative) parsed = -parsed;
                if (positive) { /* preserve explicit plus */ }
            }
            else if (Regex.IsMatch(number, @"^[+-]?(?:[01]{8}|[01]{16})b$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string digits = number.Substring(0, number.Length - 1);
                bool negative = digits.StartsWith("-", StringComparison.Ordinal);
                digits = digits.TrimStart('+', '-');
                parsed = Convert.ToInt64(digits, 2);
                if (negative) parsed = -parsed;
            }
            else return false;
            value = new ParsedValue { Number = parsed, Display = number };
            error = String.Empty;
            return true;
        }

        private static bool TryParseWordCount(string text, out long count, out string error)
        {
            ParsedValue parsed;
            if (!TryParseNumber(text, out parsed, out error)) { count = 0; return false; }
            count = parsed.Number;
            if (count < 1 || count > 65535)
            {
                error = "debe ser una constante numérica palabra sin signo entre 1 y 65535.";
                return false;
            }
            error = String.Empty;
            return true;
        }

        private static void AnalyzeCodeLine(string code, LineAnalysisResult result,
            Dictionary<string, SymbolEntry> symbols, IList<SymbolEntry> orderedSymbols,
            HashSet<string> instructions, int lineNumber)
        {
            string remaining = code.Trim();
            Match labelMatch = Regex.Match(remaining, @"^(\p{L}[\p{L}\p{Nd}_]{0,9})\s*:\s*(.*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (labelMatch.Success)
            {
                string label = labelMatch.Groups[1].Value;
                if (!IsValidSymbolName(label)) { result.AddError("El nombre de etiqueta es inválido o está reservado."); return; }
                if (symbols.ContainsKey(label)) { result.AddError("El símbolo " + label + " ya fue declarado."); return; }
                SymbolEntry entry = new SymbolEntry(label, "Etiqueta", "Pendiente", "No aplica", lineNumber);
                symbols.Add(label, entry);
                orderedSymbols.Add(entry);
                remaining = labelMatch.Groups[2].Value.Trim();
                if (remaining.Length == 0) { result.SetValid("Definición de etiqueta válida."); return; }
            }
            if (remaining.Length == 0) return;

            Match instructionMatch = Regex.Match(remaining, @"^([A-Za-z][A-Za-z0-9]*)\b(.*)$", RegexOptions.CultureInvariant);
            if (!instructionMatch.Success) { result.AddError("No se reconoce el mnemónico de la instrucción."); return; }
            string mnemonic = instructionMatch.Groups[1].Value.ToUpperInvariant();
            string operandsText = instructionMatch.Groups[2].Value.Trim();
            if (!instructions.Contains(mnemonic))
            { result.AddError("La instrucción " + mnemonic + " no pertenece al subconjunto asignado al Equipo 2."); return; }
            List<string> operands;
            string splitError;
            if (!TrySplitOperands(operandsText, out operands, out splitError)) { result.AddError(splitError); return; }

            if (mnemonic == "CBW" || mnemonic == "CLC" || mnemonic == "LODSB" || mnemonic == "LODSW" || mnemonic == "STOSB" || mnemonic == "AAA")
            {
                if (operands.Count != 0) { result.AddError(mnemonic + " no acepta operandos explícitos."); return; }
            }
            else if (mnemonic == "POP")
            {
                if (operands.Count != 1) { result.AddError("POP requiere exactamente un operando de 16 bits."); return; }
                Operand operand;
                if (!TryResolveOperand(operands[0], symbols, out operand, out splitError) ||
                    !(operand.IsRegister && operand.Width == 16 && !String.Equals(operand.Name, "CS", StringComparison.OrdinalIgnoreCase)) &&
                    !(operand.IsMemory && operand.Width == 16))
                { result.AddError("POP requiere un registro o una ubicación de memoria de 16 bits; CS no se puede usar."); return; }
            }
            else if (mnemonic == "IDIV")
            {
                if (operands.Count != 1) { result.AddError("IDIV requiere exactamente un operando registro o memoria."); return; }
                Operand operand;
                if (!TryResolveOperand(operands[0], symbols, out operand, out splitError) || operand.IsImmediate ||
                    (!operand.IsRegister && !operand.IsMemory) || (operand.Width != 8 && operand.Width != 16) ||
                    (operand.IsRegister && !IsGeneralRegister(operand.Name)))
                { result.AddError("IDIV requiere un operando de registro o memoria, no un inmediato."); return; }
            }
            else if (mnemonic == "INT")
            {
                if (operands.Count != 1) { result.AddError("INT requiere un número de interrupción inmediato."); return; }
                Operand value;
                if (!TryResolveOperand(operands[0], symbols, out value, out splitError) || !value.IsImmediate || value.Immediate < 0 || value.Immediate > 255)
                { result.AddError("INT requiere un inmediato entre 0 y 255."); return; }
            }
            else if (mnemonic == "AND" || mnemonic == "ADC")
            {
                if (operands.Count != 2) { result.AddError(mnemonic + " requiere exactamente dos operandos."); return; }
                Operand destination, source;
                if (!TryResolveOperand(operands[0], symbols, out destination, out splitError) ||
                    (!destination.IsRegister && !destination.IsMemory) ||
                    (destination.IsRegister && !IsGeneralRegister(destination.Name)))
                { result.AddError(mnemonic + " requiere como destino un registro o memoria de tamaño conocido."); return; }
                if (!TryResolveOperand(operands[1], symbols, out source, out splitError) ||
                    (!source.IsRegister && !source.IsMemory && !source.IsImmediate) ||
                    (source.IsRegister && !IsGeneralRegister(source.Name)))
                { result.AddError(mnemonic + " tiene un operando fuente inválido o no declarado."); return; }
                if (destination.IsMemory && source.IsMemory) { result.AddError(mnemonic + " no permite dos operandos de memoria."); return; }
                int operationWidth = destination.Width;
                if (destination.IsMemory && operationWidth == 0 && source.IsRegister)
                    operationWidth = source.Width;
                if (operationWidth != 8 && operationWidth != 16)
                { result.AddError(mnemonic + " requiere que el tamaño de memoria pueda determinarse por su declaración, PTR o registro compañero."); return; }
                if (source.IsImmediate)
                {
                    long min = operationWidth == 8 ? -128 : -32768;
                    long max = operationWidth == 8 ? 255 : 65535;
                    if (source.Immediate < min || source.Immediate > max) { result.AddError("El inmediato no cabe en el tamaño del destino."); return; }
                }
                else if (source.Width != 0 && source.Width != operationWidth)
                { result.AddError("Los operandos de " + mnemonic + " deben tener el mismo tamaño."); return; }
            }
            else if (mnemonic == "LEA" || mnemonic == "LES")
            {
                if (operands.Count != 2) { result.AddError(mnemonic + " requiere dos operandos."); return; }
                Operand destination, source;
                if (!TryResolveOperand(operands[0], symbols, out destination, out splitError) || !destination.IsRegister || destination.Width != 16 || !IsGeneralRegister(destination.Name))
                { result.AddError(mnemonic + " requiere un registro general de 16 bits como destino."); return; }
                if (!TryResolveOperand(operands[1], symbols, out source, out splitError) || !source.IsMemory)
                { result.AddError(mnemonic + " requiere una referencia a memoria como fuente."); return; }
                if (mnemonic == "LES" && ((source.IsPtrQualified && source.Width != 32) ||
                    (source.HasKnownAllocation && source.AllocationBytes < 4)))
                { result.AddError("LES requiere memoria para un puntero lejano de 32 bits; use DWORD PTR o una ubicación declarada de al menos cuatro bytes."); return; }
            }
            else
            {
                if (operands.Count != 1) { result.AddError(mnemonic + " requiere exactamente una etiqueta de destino."); return; }
                string target = operands[0].Trim();
                if (!IsValidSymbolName(target) || !symbols.ContainsKey(target) || symbols[target].Type != "Etiqueta")
                { result.AddError("La etiqueta de destino " + target + " debe estar definida antes de usarse."); return; }
            }
            result.SetValid("Instrucción " + mnemonic + " válida.");
        }

        private static bool TryResolveOperand(string text, Dictionary<string, SymbolEntry> symbols, out Operand operand, out string error)
        {
            operand = new Operand(); error = String.Empty;
            string value = text.Trim();
            Match ptr = Regex.Match(value, @"^(byte|word|dword)\s+ptr\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            int forcedWidth = 0;
            if (ptr.Success)
            {
                string qualifier = ptr.Groups[1].Value;
                forcedWidth = String.Equals(qualifier, "byte", StringComparison.OrdinalIgnoreCase) ? 8 :
                    (String.Equals(qualifier, "word", StringComparison.OrdinalIgnoreCase) ? 16 : 32);
                value = ptr.Groups[2].Value.Trim();
            }

            int registerWidth;
            if (IsRegister(value, out registerWidth))
            {
                if (forcedWidth != 0)
                { error = "BYTE PTR, WORD PTR y DWORD PTR solo califican operandos de memoria."; return false; }
                operand.IsRegister = true; operand.Width = registerWidth; operand.Name = value.ToUpperInvariant(); return true;
            }
            ParsedValue numeric;
            if (TryParseNumber(value, out numeric, out error))
            {
                if (forcedWidth != 0)
                { error = "BYTE PTR, WORD PTR y DWORD PTR no pueden calificar una constante inmediata."; return false; }
                operand.IsImmediate = true; operand.Immediate = numeric.Number; return true;
            }
            error = String.Empty;

            if (value.StartsWith("[", StringComparison.Ordinal) && value.EndsWith("]", StringComparison.Ordinal))
            {
                string inside = value.Substring(1, value.Length - 2).Trim();
                EffectiveAddress address;
                if (!TryParseEffectiveAddress(inside, symbols, out address, out error)) return false;
                operand.IsMemory = true;
                operand.IsSymbol = address.SymbolName != null;
                operand.IsPtrQualified = forcedWidth != 0;
                operand.Width = forcedWidth != 0 ? forcedWidth : address.ElementWidth;
                operand.AllocationBytes = address.AllocationBytes;
                operand.HasKnownAllocation = address.HasKnownAllocation;
                operand.Name = inside;
                return true;
            }
            if (IsValidSymbolName(value) && symbols.ContainsKey(value))
            {
                operand.Name = value;
                if (symbols[value].Type == "Constante EQU")
                {
                    if (forcedWidth != 0)
                    { error = "BYTE PTR, WORD PTR y DWORD PTR no pueden calificar una constante EQU."; return false; }
                    ParsedValue equValue;
                    string ignored;
                    if (!TryParseNumber(symbols[value].Value, out equValue, out ignored))
                    { error = "el valor EQU asociado no es un inmediato numérico válido."; return false; }
                    operand.IsImmediate = true; operand.Immediate = equValue.Number; return true;
                }
                if (symbols[value].Type == "Etiqueta")
                { error = "una etiqueta debe usarse como destino de transferencia o dentro de una referencia de memoria."; return false; }
                operand.IsMemory = true; operand.IsSymbol = true;
                operand.IsPtrQualified = forcedWidth != 0;
                operand.Width = forcedWidth != 0 ? forcedWidth : GetDeclaredElementWidth(symbols[value].Type);
                operand.AllocationBytes = ParseSize(symbols[value].Size);
                operand.HasKnownAllocation = true;
                return true;
            }
            error = "operando no reconocido o símbolo sin declarar: " + value + ".";
            return false;
        }

        private static bool TryParseEffectiveAddress(string expression,
            Dictionary<string, SymbolEntry> symbols, out EffectiveAddress address, out string error)
        {
            address = new EffectiveAddress();
            error = "El direccionamiento entre corchetes no es una forma válida de 8086.";
            string compact = Regex.Replace(expression ?? String.Empty, @"\s+", String.Empty);
            if (compact.Length == 0) return false;

            int baseRegisters = 0;
            int indexRegisters = 0;
            int registerCount = 0;
            int displacementCount = 0;
            int ignoredRegisterWidth;
            int position = 0;
            int sign = 1;
            if (compact[0] == '+' || compact[0] == '-')
            {
                sign = compact[0] == '-' ? -1 : 1;
                position++;
            }

            while (position < compact.Length)
            {
                int start = position;
                while (position < compact.Length && compact[position] != '+' && compact[position] != '-') position++;
                string term = compact.Substring(start, position - start);
                if (term.Length == 0) return false;

                string register = term.ToUpperInvariant();
                if (register == "BX" || register == "BP" || register == "SI" || register == "DI")
                {
                    if (sign < 0) return false;
                    registerCount++;
                    if (register == "BX" || register == "BP") baseRegisters++;
                    else indexRegisters++;
                    if (baseRegisters > 1 || indexRegisters > 1 || registerCount > 2) return false;
                }
                else if (IsRegister(term, out ignoredRegisterWidth))
                {
                    error = "El registro " + term + " no puede formar parte de una dirección efectiva 8086 entre corchetes.";
                    return false;
                }
                else
                {
                    displacementCount++;
                    if (displacementCount > 1) return false;

                    ParsedValue numeric;
                    if (TryParseAddressDisplacement(term, out numeric, out error))
                    {
                        long displacement = sign * numeric.Number;
                        if (displacement < -32768 || displacement > 65535)
                        { error = "El desplazamiento numérico debe caber en una palabra."; return false; }
                    }
                    else
                    {
                        error = String.Empty;
                        if (!IsValidSymbolName(term) || !symbols.ContainsKey(term))
                        { error = "símbolo no declarado en la referencia de memoria: " + term + "."; return false; }

                        SymbolEntry symbol = symbols[term];
                        if (address.SymbolName != null)
                        { error = "la referencia de memoria solo admite un símbolo de desplazamiento."; return false; }
                        address.SymbolName = symbol.Symbol;
                        if (symbol.Type != "Constante EQU" && symbol.Type != "Etiqueta")
                        {
                            address.ElementWidth = GetDeclaredElementWidth(symbol.Type);
                            address.AllocationBytes = ParseSize(symbol.Size);
                            address.HasKnownAllocation = true;
                        }
                    }
                }

                if (position < compact.Length)
                {
                    sign = compact[position] == '-' ? -1 : 1;
                    position++;
                    if (position >= compact.Length) return false;
                }
            }

            if (registerCount == 2 && (baseRegisters != 1 || indexRegisters != 1)) return false;
            if (registerCount == 0 && displacementCount == 0) return false;
            error = String.Empty;
            return true;
        }

        private static bool TryParseAddressDisplacement(string text, out ParsedValue value, out string error)
        {
            if (TryParseNumber(text, out value, out error)) return true;
            string number = (text ?? String.Empty).Trim();
            if (Regex.IsMatch(number, @"^[0-9][0-9a-f]{0,3}h$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                string digits = number.Substring(0, number.Length - 1);
                long parsed = Int64.Parse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                value = new ParsedValue { Number = parsed, Display = number };
                error = String.Empty;
                return true;
            }
            value = null;
            error = "se esperaba un desplazamiento numérico o un símbolo declarado.";
            return false;
        }

        private static int GetDeclaredElementWidth(string type)
        {
            if (type == "Dato DB carácter" || type == "Dato DB numérico" || type == "Arreglo DB") return 8;
            if (type == "Dato DW" || type == "Arreglo DW") return 16;
            return 0;
        }

        private static bool IsRegister(string text, out int width)
        {
            string name = text.Trim().ToUpperInvariant();
            if ("AL AH BL BH CL CH DL DH".Contains(" " + name) || name == "AL" || name == "AH" || name == "BL" || name == "BH" || name == "CL" || name == "CH" || name == "DL" || name == "DH")
            { width = 8; return true; }
            if ("AX BX CX DX SI DI BP SP CS DS ES SS".Contains(" " + name) || name == "AX" || name == "BX" || name == "CX" || name == "DX" || name == "SI" || name == "DI" || name == "BP" || name == "SP" || name == "CS" || name == "DS" || name == "ES" || name == "SS")
            { width = 16; return true; }
            width = 0; return false;
        }

        private static bool IsGeneralRegister(string name)
        {
            string value = (name ?? String.Empty).ToUpperInvariant();
            return value == "AL" || value == "AH" || value == "BL" || value == "BH" ||
                value == "CL" || value == "CH" || value == "DL" || value == "DH" ||
                value == "AX" || value == "BX" || value == "CX" || value == "DX" ||
                value == "SI" || value == "DI" || value == "BP" || value == "SP";
        }

        private static bool IsValidSymbolName(string name)
        {
            return NamePattern.IsMatch(name ?? String.Empty) && !ReservedNames.Contains(name);
        }

        private static string FormatSize(int bytes)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + (bytes == 1 ? " byte" : " bytes");
        }

        private static int ParseSize(string size)
        {
            Match match = Regex.Match(size ?? String.Empty, @"^(\d+) bytes?$", RegexOptions.CultureInvariant);
            int bytes;
            return match.Success && Int32.TryParse(match.Groups[1].Value, out bytes) ? bytes : 0;
        }

        private static bool TrySplitOperands(string text, out List<string> operands, out string error)
        {
            operands = new List<string>(); error = String.Empty;
            string value = (text ?? String.Empty).Trim();
            if (value.Length == 0) return true;
            int start = 0; int bracket = 0; int paren = 0; char quote = '\0';
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; continue; }
                if (c == '\'' || c == '"') { quote = c; continue; }
                if (c == '[') bracket++;
                else if (c == ']') bracket--;
                else if (c == '(') paren++;
                else if (c == ')') paren--;
                else if (c == ',' && bracket == 0 && paren == 0)
                {
                    string item = value.Substring(start, i - start).Trim();
                    if (item.Length == 0) { error = "Hay un operando vacío entre comas."; return false; }
                    operands.Add(item); start = i + 1;
                }
                if (bracket < 0 || paren < 0) { error = "Los corchetes o paréntesis no están balanceados."; return false; }
            }
            if (quote != '\0' || bracket != 0 || paren != 0) { error = "La instrucción contiene comillas, corchetes o paréntesis sin cerrar."; return false; }
            string last = value.Substring(start).Trim();
            if (last.Length == 0) { error = "La instrucción termina con una coma y falta un operando."; return false; }
            operands.Add(last);
            return true;
        }

        private static string RemoveComment(string line)
        {
            if (line == null) return String.Empty;
            char quote = '\0';
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == ';') return line.Substring(0, i);
            }
            return line;
        }

        private static SegmentKind ParseSegment(string name)
        {
            if (String.Equals(name, "stack", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Stack;
            if (String.Equals(name, "data", StringComparison.OrdinalIgnoreCase)) return SegmentKind.Data;
            return SegmentKind.Code;
        }

        private static string SegmentName(SegmentKind kind)
        {
            if (kind == SegmentKind.Stack) return ".stack";
            if (kind == SegmentKind.Data) return ".data";
            if (kind == SegmentKind.Code) return ".code";
            return "ninguno";
        }
    }
}
