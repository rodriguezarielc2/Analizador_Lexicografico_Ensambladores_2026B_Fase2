using System;
using System.Collections.Generic;
using System.IO;
using AnalizadorFase2;

internal static class LegacyInputProcessingTests
{
    private static readonly string[] RequiredExamples = {
        "Plantilla_Asm.ens",
        "Explorador_Basico_Archivos.ens",
        "Programa2_Comparacion_De_Cadenas.ens"
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Uso: LegacyInputProcessingTests <carpeta-entradas-heredadas> <catalogo-instrucciones>");
            return 2;
        }

        try
        {
            string examplesDirectory = Path.GetFullPath(args[0]);
            List<string> instructions = LoadInstructions(args[1]);
            if (instructions.Count == 0) throw new Exception("El catálogo no contiene instrucciones asignadas.");

            foreach (string fileName in RequiredExamples)
                AnalyzeExample(Path.Combine(examplesDirectory, fileName), instructions);

            Console.WriteLine("Entradas heredadas: 3 procesadas; esta prueba verifica procesamiento y numeración, no su corrección sintáctico-semántica.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FALLO al procesar entradas heredadas: " + ex.Message);
            return 1;
        }
    }

    private static void AnalyzeExample(string path, IList<string> instructions)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No existe el ejemplo requerido.", path);
        string source = File.ReadAllText(path);
        string[] sourceLines = source.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        List<Lexeme> lexemes = LexicalAnalysisCore.Analyze(source, instructions);
        ProgramAnalysisResult result = SyntaxSemanticCore.Analyze(source, lexemes, instructions);

        if (result.Lines.Count != sourceLines.Length)
            throw new Exception(Path.GetFileName(path) + ": se esperaban " + sourceLines.Length +
                " resultados y se obtuvieron " + result.Lines.Count + ".");
        for (int i = 0; i < result.Lines.Count; i++)
            if (result.Lines[i].LineNumber != i + 1)
                throw new Exception(Path.GetFileName(path) + ": numeración de salida incorrecta en la posición " + (i + 1) + ".");

        int incorrect = 0;
        foreach (LineAnalysisResult line in result.Lines)
            if (line.Status == "Incorrecta") incorrect++;
        Console.WriteLine("OK: {0} | {1} líneas, {2} incorrectas (válidas o inválidas no condicionan la prueba).",
            Path.GetFileName(path), result.Lines.Count, incorrect);
    }

    private static List<string> LoadInstructions(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No existe el catálogo de instrucciones.", path);
        List<string> instructions = new List<string>();
        foreach (string line in File.ReadAllLines(path))
        {
            string value = line.Trim();
            if (value.Length == 0 || value.StartsWith(";", StringComparison.Ordinal) ||
                value.StartsWith("#", StringComparison.Ordinal)) continue;
            instructions.AddRange(value.Split(new char[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries));
        }
        return instructions;
    }
}
