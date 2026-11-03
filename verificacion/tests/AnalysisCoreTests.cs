using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using AnalizadorFase2;

internal static class LexicalAnalysisCoreTests
{
    private const string Invalid = "Elemento inválido", Symbol = "Símbolo", Pseudo = "Pseudoinstrucción";
    private const string Decimal = "Constante numérica decimal", Hex = "Constante numérica hexadecimal";
    private const string Binary = "Constante numérica binaria", Character = "Constante carácter";
    private static int _passed, _failed, _assertions;
    private static readonly string[] TeamTwo = {
        "CBW", "CLC", "LODSB", "LODSW", "STOSB", "AAA", "POP", "IDIV", "INT", "AND",
        "ADC", "LEA", "LES", "JNZ", "JZ", "LOOPNZ", "JGE", "JNA", "JNL", "JB"
    };

    private static int Main(string[] args)
    {
        Run("separadores, etiquetas y posiciones", SeparatorsAndPositions);
        Run("solo instrucciones del equipo", AssignedInstructions);
        Run("catalogo docente de registros", RegisterCatalog);
        Run("catalogo docente de pseudoinstrucciones", PseudoinstructionCatalog);
        Run("formas de segmento y calificadores completos", CompletePhrases);
        Run("limites de frases compuestas", PhraseBoundaries);
        Run("contador separado de DUP", DupCounter);
        Run("DUP con espacio y sufijos", DupBoundaries);
        Run("referencias entre corchetes", References);
        Run("elementos pegados", AttachedElements);
        Run("simbolos y etiquetas", SymbolRules);
        Run("formato decimal", DecimalConstants);
        Run("formato hexadecimal", HexConstants);
        Run("formato binario", BinaryConstants);
        Run("constantes caracter", QuotedConstants);
        Run("recuperacion de cadenas incompletas", UnterminatedQuotes);
        Run("comentarios fuera de literales", Comments);
        Run("comentarios tras compuestos incompletos", MalformedComments);
        Run("sin escapes de otro lenguaje", LiteralBackslashes);
        Run("numeracion y saltos de linea", LineEndings);
        Run("paginacion sin desbordamientos", Pagination);
        Run("archivos ens en cualquier carpeta", FileValidation);
        Run("familias de longitudes numericas", NumericFamilies);
        Run("entradas arbitrarias sin perdida de posiciones", MalformedInputFamilies);
        Run("compuestos muy largos sin recursion", LongCompounds);
        if (args.Length > 0) Run("plantilla de la clase 17", delegate { TeacherTemplate(args[0]); });
        if (args.Length > 1) Run("ejemplos de demostracion", delegate { DemonstrationFiles(args[1]); });
        Console.WriteLine("Grupos: {0} correctos, {1} fallidos; comprobaciones: {2}", _passed, _failed, _assertions);
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("OK: " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FALLO: " + name + ": " + ex.Message); }
    }
    private static List<Lexeme> Analyze(string source) { return LexicalAnalysisCore.Analyze(source, TeamTwo); }
    private static void Token(string source, string type)
    {
        List<Lexeme> items = Analyze(source);
        Equal(1, items.Count, "un elemento para " + source);
        Equal(source, items[0].Text, "conserva " + source);
        Equal(type, items[0].Type, "tipo de " + source);
    }
    private static void Texts(string source, params string[] expected)
    {
        List<Lexeme> items = Analyze(source);
        Equal(expected.Length, items.Count, "cantidad en " + source);
        for (int i = 0; i < expected.Length; i++) Equal(expected[i], items[i].Text, "elemento " + (i + 1));
    }
    private static void SeparatorsAndPositions()
    {
        List<Lexeme> items = Analyze("  inicio:\tINT ax, 045H\r\n\tfin: ; comentario");
        Equal(5, items.Count, "cantidad");
        Equal("inicio:", items[0].Text, "dos puntos");
        Equal("Etiqueta", items[0].Type, "etiqueta");
        Equal(3, items[0].Column, "columna inicial");
        Equal(11, items[1].Column, "columna tras tabulador");
        Equal(2, items[4].Line, "segunda linea");
        Equal(2, items[4].Column, "columna de fin");
        for (int i = 0; i < items.Count; i++) Equal(i + 1, items[i].Number, "numero global");
        Texts("  , : ; comentario");
    }
    private static void AssignedInstructions()
    {
        foreach (string mnemonic in TeamTwo) Token(mnemonic.ToLowerInvariant(), "Instrucción");
        foreach (string other in new string[] { "MOV", "JE", "ADD", "RET", "NOP", "XOR" }) Token(other, Symbol);
        Equal(Symbol, LexicalAnalysisCore.Analyze("INT", null)[0].Type, "sin catalogo");
        Equal("Instrucción", LexicalAnalysisCore.Analyze("INT", new string[] { " int ", "INT", null, "" })[0].Type, "normaliza lista");
        Equal(0, LexicalAnalysisCore.Analyze(null, TeamTwo).Count, "fuente nula");
    }
    private static void RegisterCatalog()
    {
        foreach (string register in "AX AH AL BX BH BL CX CH CL DX DH DL SI DI SP BP SS CS DS ES".Split(' ')) Token(register.ToLowerInvariant(), "Registro");
        foreach (string other in new string[] { "IP", "FLAGS", "EAX", "FS", "GS", "XMM0" }) Token(other, Symbol);
    }
    private static void PseudoinstructionCatalog()
    {
        foreach (string keyword in "DB DW EQU DUP ENDS MACRO ENDM PROC ENDP LOCAL".Split(' ')) Token(keyword, Pseudo);
        foreach (string other in new string[] { "model", "org", "dd", "ptr", "segment", "offset", "assume", "byte", "word", "end" }) Token(other, Symbol);
        Token(".model", Invalid); Token(".modelo", Invalid);
    }
    private static void CompletePhrases()
    {
        foreach (string segment in new string[] { ".code segment", ".DATA\tSEGMENT", ".stack   segment" }) Token(segment, Pseudo);
        foreach (string segment in new string[] { "STACK SEGMENT", "data segment", "CODE SEGMENT", ".STACK", ".data", ".CODE" }) Token(segment, Pseudo);
        foreach (string qualifier in new string[] { "byte ptr", "WORD\tPTR", "DWORD PTR" }) Token(qualifier, Pseudo);
        Texts(".stacks segment", ".stacks", "segment");
        foreach (string segmentName in new string[] { "stack", "data", "code" }) Token(segmentName, Symbol);
        Token(".stacks", Invalid);
        Texts(".data\nsegment", ".data", "segment");
    }
    private static void PhraseBoundaries()
    {
        Texts(".data segment$", ".data", "segment$");
        Texts("byte ptr$dato", "byte", "ptr$dato");
        Texts("word ptr[si]", "word", "ptr[si]");
        Texts(".code segment?", ".code", "segment?");
        Texts(".stack segment,db", ".stack segment", "db");
    }
    private static void DupCounter()
    {
        Texts("dw 10 dup(?)", "dw", "10", "dup(?)");
        foreach (string value in new string[] { "dup(0)", "DUP('a')", "dup(1, 2 dup(0))", "dup('a)b')" }) Token(value, Pseudo);
        Texts("2 dup(1, 2 dup(0))", "2", "dup(1, 2 dup(0))");
        Token("dup()", Invalid);
    }
    private static void DupBoundaries()
    {
        Texts("dup (0)", "dup", "(0)");
        foreach (string value in new string[] { "(0)", "dupy(0)", "dup(0)resto", "10dup(0)", "dup(0)(1)" }) Token(value, Invalid);
    }
    private static void References()
    {
        Token("[SI]", "Referencia a registro"); Token("[ si ]", "Referencia a registro");
        foreach (string value in new string[] { "[bx + si]", "[bp + 4]", "[2]" }) Token(value, "Referencia a memoria");
        foreach (string value in new string[] { "[]", "[si]resto", "[[si]]" }) Token(value, Invalid);
    }
    private static void AttachedElements()
    {
        Texts("mov msg[2], 034H", "mov", "msg[2]", "034H");
        foreach (string value in new string[] { "msg[2]", "dato!otro", "dato+1", "dato-1", "#dato#", "@dato", "dato$", "dato?", "\"hola\"resto", "'a''b'" }) Token(value, Invalid);
    }
    private static void SymbolRules()
    {
        foreach (string value in new string[] { "tecla", "Var1", "abcdefghij", "mayor_ig", "a1", "ñandú" }) Token(value, Symbol);
        foreach (string value in new string[] { "7tecla", "abcdefghijk", "_dato", "@", ".", "$", "a@b" }) Token(value, Invalid);
        Token("lectura:", "Etiqueta"); Token("abcdefghij:", "Etiqueta");
        foreach (string value in new string[] { "abcdefghijk:", "7tecla:", "AX:", "INT:" }) Token(value, Invalid);
    }
    private static void DecimalConstants()
    {
        foreach (string value in new string[] { "0", "42", "+128", "-10", "-32768", "65535", "00042", "999999999999999999999999" }) Token(value, Decimal);
        foreach (string value in new string[] { "122d", "12.5", "0x20", "0b10101010", "--12", "+", "-", "123XYZ" }) Token(value, Invalid);
    }
    private static void HexConstants()
    {
        foreach (string value in new string[] { "045H", "021h", "0FFH", "000H", "0AF23H", "00000h", "0ffffH", "+0FFH", "-0ABCDh" }) Token(value, Hex);
        foreach (string value in new string[] { "45H", "21h", "0345H", "0FH", "0H", "0ABCH", "0ABCDEH", "0GGH", "0x20" }) Token(value, Invalid);
        Token("FH", Symbol);
    }
    private static void BinaryConstants()
    {
        foreach (string value in new string[] { "00000000b", "10101010B", "1111000011110000b", "+00001111b", "-10000000B" }) Token(value, Binary);
        foreach (string value in new string[] { "101b", "000011111b", "1111111b", "11111111111111111b", "00000002b", "0b10101010" }) Token(value, Invalid);
    }
    private static void QuotedConstants()
    {
        foreach (string value in new string[] { "'A'", "'hola mundo'", "\"hola mundo\"", "\"a:b,c;d\"", "'\"hola\"'" }) Token(value, Character);
        Token("''", Character); Token("\"\"", Character);
        Texts("db \"a;b\", 'c:d' ; fin", "db", "\"a;b\"", "'c:d'");
    }
    private static void UnterminatedQuotes()
    {
        Texts("pkey db \"presiona key...$ una vez", "pkey", "db", "\"presiona", "key...$", "una", "vez");
        Texts("db 'sin cierre\nINT 021H", "db", "'sin", "cierre", "INT", "021H");
        Texts("db hola mundo\"", "db", "hola", "mundo\"");
        Texts("db \"sin cierre ; comentario", "db", "\"sin", "cierre");
    }
    private static void Comments()
    {
        Texts("; toda la linea\nINT 021H; ignorar\nDB \"A;B\"", "INT", "021H", "DB", "\"A;B\"");
        Texts("dup(';') ; comentario", "dup(';')"); Texts("[si]; comentario", "[si]");
    }
    private static void MalformedComments()
    {
        foreach (string source in new string[] { "dw dup(0 ; SECRETO", "mov [si ; SECRETO", "dup(\"a; b\" ; SECRETO", "[si + 1 ; SECRETO" })
        {
            List<Lexeme> items = Analyze(source);
            True(items.Count > 0, "conserva el texto previo");
            foreach (Lexeme item in items) True(!item.Text.Contains("SECRETO"), "no filtra comentarios a los elementos");
        }
    }
    private static void LiteralBackslashes()
    {
        Token("\"C:\\carpeta\\\"", Character); Token("'\\'", Character); Token("'a''b'", Invalid);
    }
    private static void LineEndings()
    {
        List<Lexeme> items = Analyze("INT 021H\rCBW\nCLC\r\nJB fin");
        Equal(6, items.Count, "CR, LF y CRLF");
        Equal(1, items[0].Line, "primera"); Equal(2, items[2].Line, "segunda");
        Equal(3, items[3].Line, "tercera"); Equal(4, items[4].Line, "cuarta");
        for (int i = 0; i < items.Count; i++) Equal(i + 1, items[i].Number, "numero global");
    }
    private static void Pagination()
    {
        Equal(0, LexicalAnalysisCore.PageCount(0, 25), "sin elementos");
        Equal(3, LexicalAnalysisCore.PageCount(51, 25), "pagina parcial");
        Equal(2, LexicalAnalysisCore.PageCount(Int32.MaxValue, Int32.MaxValue - 1), "sin desbordamiento en suma");
        List<int> values = new List<int>(); for (int i = 1; i <= 55; i++) values.Add(i);
        List<int> last = LexicalAnalysisCore.GetPage(values, 1, 50);
        Equal(5, last.Count, "ultima pagina"); Equal(51, last[0], "numeracion");
        Equal(0, LexicalAnalysisCore.GetPage(values, -1, 50).Count, "indice negativo");
        Equal(0, LexicalAnalysisCore.GetPage(values, Int32.MaxValue, 50).Count, "sin desbordamiento en producto");
        Throws<ArgumentOutOfRangeException>(delegate { LexicalAnalysisCore.PageCount(1, 0); });
        Throws<ArgumentNullException>(delegate { LexicalAnalysisCore.GetPage<int>(null, 0, 25); });
    }
    private static void FileValidation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "AnalizadorFase2-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string ens = Path.Combine(directory, "programa con espacios.ENS"), other = Path.Combine(directory, "prueba.asm");
        File.WriteAllText(ens, "INT 021H"); File.WriteAllText(other, "INT 021H");
        try
        {
            string error;
            True(LexicalAnalysisCore.ValidateSourcePath(ens, out error), "ENS en otra ubicacion");
            True(!LexicalAnalysisCore.ValidateSourcePath(other, out error), "extension incorrecta");
            True(!LexicalAnalysisCore.ValidateSourcePath(Path.Combine(directory, "inexistente.ens"), out error), "inexistente");
            True(!LexicalAnalysisCore.ValidateSourcePath(null, out error), "nula");
            True(!LexicalAnalysisCore.ValidateSourcePath(directory, out error), "directorio");
        }
        finally { File.Delete(ens); File.Delete(other); Directory.Delete(directory); }
    }
    private static void NumericFamilies()
    {
        for (int length = 1; length <= 34; length++)
        {
            Token(new string('1', length) + "b", length == 8 || length == 16 ? Binary : Invalid);
            Token("0" + new string('A', length) + "h", length == 2 || length == 4 ? Hex : Invalid);
        }
        for (int value = 0; value <= 255; value++) Token("0" + value.ToString("X2") + "H", Hex);
    }
    private static void MalformedInputFamilies()
    {
        Random random = new Random(8086); const string alphabet = "ab0[]()\"';:, +-$?@\t\r\n";
        for (int trial = 0; trial < 600; trial++)
        {
            StringBuilder source = new StringBuilder();
            for (int i = 0; i < 160; i++) source.Append(alphabet[random.Next(alphabet.Length)]);
            string text = source.ToString();
            string[] lines = text.Split(new string[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            List<Lexeme> items = Analyze(text); int previousLine = 0, previousEnd = 0;
            for (int i = 0; i < items.Count; i++)
            {
                Lexeme item = items[i]; Equal(i + 1, item.Number, "numeracion");
                True(item.Line >= previousLine, "orden de lineas");
                True(item.Column >= 1 && item.Text.Length > 0, "posicion y progreso");
                True(item.Column - 1 + item.Text.Length <= lines[item.Line - 1].Length, "dentro de linea");
                Equal(item.Text, lines[item.Line - 1].Substring(item.Column - 1, item.Text.Length), "texto exacto de origen");
                if (item.Line == previousLine) True(item.Column - 1 >= previousEnd, "no hay solapamiento");
                previousLine = item.Line; previousEnd = item.Column - 1 + item.Text.Length;
            }
        }
    }
    private static void True(bool condition, string message)
    { _assertions++; if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual, string message)
    {
        _assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception(message + ". Esperado=" + expected + ", actual=" + actual);
    }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { _assertions++; return; } throw new Exception("Falta excepcion " + typeof(T).Name); }

    private static void LongCompounds()
    {
        Token(new string('(', 20000), Invalid);
        Token("dup(" + new string('(', 20000) + "0" + new string(')', 20000) + ")", Pseudo);
        List<Lexeme> items = Analyze(String.Join(" ", new string('[', 8000).ToCharArray()));
        Equal(8000, items.Count, "cada apertura sin cierre sigue separada por espacio");
    }

    private static void TeacherTemplate(string path)
    {
        List<Lexeme> items = Analyze(File.ReadAllText(path));
        string[,] expected = {
            { ".stack segment", Pseudo }, { ".stacks", Invalid }, { "dupy(0)", Invalid },
            { "dup(0)", Pseudo }, { "datasegment", Invalid }, { "7tecla", Invalid },
            { "045H", Hex }, { "45H", Invalid }, { "000011111b", Invalid },
            { "10101010b", Binary }, { "1111000011110000b", Binary },
            { "msg[2]", Invalid }, { "lectura:", "Etiqueta" }, { "fin3:", "Etiqueta" },
            { "\"press any key...$\"", Character }, { "'hola'", Character },
            { "\"presiona", Invalid }, { "key...$", Invalid }
        };
        for (int row = 0; row < expected.GetLength(0); row++)
        {
            Lexeme item = items.Find(value => value.Text == expected[row, 0]);
            True(item != null, "la plantilla conserva " + expected[row, 0]);
            Equal(expected[row, 1], item.Type, "clasificacion de " + expected[row, 0]);
        }
        for (int i = 0; i < items.Count; i++) Equal(i + 1, items[i].Number, "numeracion de plantilla");
    }

    private static void DemonstrationFiles(string directory)
    {
        foreach (string name in new string[] { "01_basico.ens", "02_compuestos_y_constantes.ens", "03_equipo2.ens", "05_paginacion.ens" })
            foreach (Lexeme item in Analyze(File.ReadAllText(Path.Combine(directory, name))))
                True(!item.Type.StartsWith("Elemento", StringComparison.Ordinal), name + ": error inesperado en " + item.Text);
        List<Lexeme> intentional = Analyze(File.ReadAllText(Path.Combine(directory, "04_elementos_invalidos.ens")));
        True(intentional.Exists(value => value.Type == Invalid), "los errores intencionales se detectan");
    }
}
