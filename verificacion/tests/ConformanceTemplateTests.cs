using System;
using System.Collections.Generic;
using System.IO;
using AnalizadorFase2;

internal static class ConformanceTemplateTests
{
    private sealed class SymbolExpectation
    {
        public readonly string Name;
        public readonly string Type;
        public readonly string Value;
        public readonly string Size;

        public SymbolExpectation(string name, string type, string value, string size)
        {
            Name = name;
            Type = type;
            Value = value;
            Size = size;
        }
    }

    private sealed class TemplateExpectation
    {
        public readonly string FileName;
        public readonly SymbolExpectation[] Symbols;

        public TemplateExpectation(string fileName, params SymbolExpectation[] symbols)
        {
            FileName = fileName;
            Symbols = symbols;
        }
    }

    private static readonly TemplateExpectation[] Templates = {
        new TemplateExpectation("01_tipos_y_datos.ens",
            new SymbolExpectation("mensaje", "Dato DB carácter", "'OK'", "2 bytes"),
            new SymbolExpectation("vectorB", "Arreglo DB", "3 DUP('A')", "3 bytes"),
            new SymbolExpectation("limite", "Constante EQU", "65535", "2 bytes"),
            new SymbolExpectation("inicio", "Etiqueta", "Pendiente", "No aplica")),
        new TemplateExpectation("02_etiquetas_y_saltos.ens",
            new SymbolExpectation("contador", "Dato DB numérico", "3", "1 byte"),
            new SymbolExpectation("entrada", "Etiqueta", "Pendiente", "No aplica")),
        new TemplateExpectation("03_operandos_8086.ens",
            new SymbolExpectation("palabras", "Arreglo DW", "2 DUP(0)", "4 bytes"),
            new SymbolExpectation("revision", "Etiqueta", "Pendiente", "No aplica"))
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Uso: ConformanceTemplateTests <carpeta-plantillas> <catalogo-instrucciones>");
            return 2;
        }

        try
        {
            string templateDirectory = Path.GetFullPath(args[0]);
            List<string> instructions = LoadInstructions(args[1]);
            HashSet<string> covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (instructions.Count == 0) throw new Exception("El catálogo no contiene instrucciones asignadas.");

            foreach (TemplateExpectation template in Templates)
                AnalyzeTemplate(Path.Combine(templateDirectory, template.FileName), template, instructions, covered);

            foreach (string instruction in instructions)
                if (!covered.Contains(instruction))
                    throw new Exception("Las plantillas no cubren la instrucción asignada " + instruction + ".");

            Console.WriteLine("Plantillas de conformidad: {0}; todas las líneas correctas, segmentos en orden, símbolos clave presentes e instrucciones asignadas cubiertas.", Templates.Length);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FALLO en plantillas de conformidad: " + ex.Message);
            return 1;
        }
    }

    private static void AnalyzeTemplate(string path, TemplateExpectation expected,
        IList<string> instructions, HashSet<string> covered)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No existe una plantilla de conformidad.", path);
        string source = File.ReadAllText(path);
        string[] sourceLines = source.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        List<Lexeme> lexemes = LexicalAnalysisCore.Analyze(source, instructions);
        ProgramAnalysisResult result = SyntaxSemanticCore.Analyze(source, lexemes, instructions);

        if (result.Lines.Count != sourceLines.Length)
            throw new Exception(expected.FileName + ": la cantidad de resultados no coincide con las líneas fuente.");
        for (int i = 0; i < result.Lines.Count; i++)
        {
            LineAnalysisResult line = result.Lines[i];
            if (line.LineNumber != i + 1)
                throw new Exception(expected.FileName + ": numeración incorrecta en la línea " + (i + 1) + ".");
            if (line.Status != "Correcta")
                throw new Exception(expected.FileName + ": línea " + line.LineNumber + " incorrecta: " + line.Source + " — " + line.Message);
        }

        AssertSegmentOrder(sourceLines, expected.FileName);
        foreach (Lexeme lexeme in lexemes)
            if (lexeme.Type == "Instrucción") covered.Add(lexeme.Text);
        foreach (SymbolExpectation symbol in expected.Symbols)
            AssertSymbol(result.Symbols, symbol, expected.FileName);

        Console.WriteLine("OK: {0} | {1} líneas correctas, {2} símbolos esperados.",
            expected.FileName, result.Lines.Count, expected.Symbols.Length);
    }

    private static void AssertSegmentOrder(string[] lines, string fileName)
    {
        int stack = FindLine(lines, ".stack segment");
        int data = FindLine(lines, ".data segment");
        int code = FindLine(lines, ".code segment");
        if (stack < 0 || data <= stack || code <= data)
            throw new Exception(fileName + ": se requieren .stack segment, .data segment y .code segment en ese orden.");
    }

    private static int FindLine(string[] lines, string value)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            string code = lines[i].Split(';')[0].Trim();
            if (String.Equals(code, value, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static void AssertSymbol(IList<SymbolEntry> symbols, SymbolExpectation expected, string fileName)
    {
        foreach (SymbolEntry symbol in symbols)
            if (String.Equals(symbol.Symbol, expected.Name, StringComparison.OrdinalIgnoreCase))
            {
                if (symbol.Type != expected.Type || symbol.Value != expected.Value || symbol.Size != expected.Size)
                    throw new Exception(fileName + ": símbolo " + expected.Name + " tiene tipo/valor/tamaño " +
                        symbol.Type + "/" + symbol.Value + "/" + symbol.Size + "; se esperaba " +
                        expected.Type + "/" + expected.Value + "/" + expected.Size + ".");
                return;
            }
        throw new Exception(fileName + ": falta el símbolo requerido " + expected.Name + ".");
    }

    private static List<string> LoadInstructions(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No existe el catálogo de instrucciones.", path);
        List<string> result = new List<string>();
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Split(';')[0].Trim();
            if (line.Length > 0) result.Add(line);
        }
        return result;
    }
}
