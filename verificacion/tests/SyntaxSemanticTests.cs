using System;
using System.Collections.Generic;
using AnalizadorFase2;

internal static class SyntaxSemanticTests
{
    private static readonly string[] TeamTwo = {
        "CBW", "CLC", "LODSB", "LODSW", "STOSB", "AAA", "POP", "IDIV", "INT", "AND",
        "ADC", "LEA", "LES", "JNZ", "JZ", "LOOPNZ", "JGE", "JNA", "JNL", "JB"
    };
    private static int _failed;

    private static int Main()
    {
        Run("estructura, instrucciones y tabla de símbolos", ValidProgram);
        Run("errores semánticos localizados", InvalidProgram);
        Run("rangos y orden de segmentos", DeclarationRangesAndSegmentOrder);
        Run("cantidad de resultados por línea", OneResultPerSourceLine);
        Run("ancho por elemento de arreglos", ArrayElementWidths);
        Run("direccionamiento efectivo 8086", EffectiveAddressing);
        Run("PTR solo califica memoria", PtrOnlyAppliesToMemory);
        Run("segmentos de pila y datos no vacíos", RequiredSegmentContents);
        Run("símbolos Unicode consistentes con Fase 1", UnicodeSymbols);
        Run("ancho de constantes inmediatas", ImmediateWidths);
        Run("formas alternativas de encabezado de segmento", SegmentHeaderForms);
        Run("LES con calificador DWORD PTR", LesDwordPointer);
        Run("tamaños de operandos AND y ADC", AndAdcOperandWidths);
        Console.WriteLine("Pruebas sintáctico-semánticas: {0} fallidas", _failed);
        return _failed == 0 ? 0 : 1;
    }

    private static void ValidProgram()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment",
            "dw 064H dup(0)",
            "ends",
            ".data segment",
            "buffer db 3 dup('A')",
            "value db -10",
            "letter db 'H'",
            "bytes db 4 dup(-1)",
            "wide dw 32000",
            "pointer dw 2 dup(0)",
            "limit equ 20",
            "ends",
            ".code segment",
            "inicio:",
            "cbw",
            "clc",
            "pop ax",
            "idiv bl",
            "int 21",
            "and ax, bx",
            "adc al, byte ptr value",
            "lea si, buffer",
            "les di, pointer",
            "jz inicio",
            "loopnz inicio",
            "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        Equal(26, result.Lines.Count, "una salida por cada línea");
        Equal(8, result.Symbols.Count, "tabla completa de datos y etiqueta");
        Equal("3 bytes", Find(result, "buffer").Size, "tamaño DB DUP");
        Equal("1 byte", Find(result, "value").Size, "tamaño de byte");
        Equal("1 byte", Find(result, "letter").Size, "tamaño de carácter DB");
        Equal("4 bytes", Find(result, "bytes").Size, "tamaño DB DUP numérico");
        Equal("2 bytes", Find(result, "wide").Size, "tamaño de palabra");
        Equal("4 bytes", Find(result, "pointer").Size, "tamaño DW DUP");
        Equal("Pendiente", Find(result, "inicio").Value, "dirección de etiqueta provisional");
        Equal(0, CountIncorrect(result), "programa válido no tiene líneas incorrectas");
    }

    private static void InvalidProgram()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 10 dup(0)", "ends",
            ".data segment", "left db 1", "right dw 2", "left db 3", "ends",
            ".code segment", "jz futuro", "and al, right", "mov ax, 1", "futuro:", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[6].Status == "Incorrecta" && Contains(result.Lines[6].Message, "ya fue declarado"), "detecta símbolo duplicado");
        True(result.Lines[9].Status == "Incorrecta" && Contains(result.Lines[9].Message, "definida antes"), "rechaza salto hacia etiqueta futura");
        True(result.Lines[10].Status == "Incorrecta" && Contains(result.Lines[10].Message, "mismo tamaño"), "detecta ancho incompatible");
        True(result.Lines[11].Status == "Incorrecta" && Contains(result.Lines[11].Message, "Equipo 2"), "rechaza instrucción no asignada");
    }

    private static void OneResultPerSourceLine()
    {
        string source = ".stack segment\n; comentario\n";
        ProgramAnalysisResult result = Analyze(source);
        Equal(3, result.Lines.Count, "conserva líneas vacías y comentarios");
        Equal(1, result.Lines[0].LineNumber, "número inicial");
        Equal(3, result.Lines[2].LineNumber, "número de línea final");
    }

    private static void DeclarationRangesAndSegmentOrder()
    {
        string orderSource = String.Join(Environment.NewLine, new string[] {
            ".data segment", "ends",
            ".stack segment", "dw 1 dup(0)", "ends",
            ".code segment", "cbw", "ends"
        });
        ProgramAnalysisResult orderResult = Analyze(orderSource);
        True(orderResult.Lines[0].Status == "Incorrecta" && Contains(orderResult.Lines[0].Message, "Orden incorrecto"),
            "detecta segmento de datos fuera de orden");

        string rangeSource = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 1 dup(0)", "ends",
            ".data segment", "tooBig db 256", "tooWide dw 65536", "ends",
            ".code segment", "cbw", "ends"
        });
        ProgramAnalysisResult rangeResult = Analyze(rangeSource);
        True(rangeResult.Lines[4].Status == "Incorrecta" && Contains(rangeResult.Lines[4].Message, "byte"),
            "rechaza un valor fuera del rango de byte");
        True(rangeResult.Lines[5].Status == "Incorrecta" && Contains(rangeResult.Lines[5].Message, "palabra"),
            "rechaza un valor fuera del rango de palabra");
    }

    private static void ArrayElementWidths()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 10 dup(0)", "ends",
            ".data segment", "bytes db 2 dup(0)", "words dw 2 dup(0)", "text db \"AB\"",
            "single db 1", "farPtr dw 2 dup(0)", "limit equ 12", "ends",
            ".code segment", "and al, bytes", "and ax, bytes", "and ax, words", "and al, words",
            "idiv words", "pop words", "les di, farPtr", "and ax, limit", "and al, limit",
            "and al, text", "and ax, text", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[12].Status == "Correcta", "DB DUP conserva ancho byte por elemento");
        True(result.Lines[13].Status == "Incorrecta", "DB DUP no se ensancha por ocupar dos bytes");
        True(result.Lines[14].Status == "Correcta", "DW DUP conserva ancho palabra por elemento");
        True(result.Lines[15].Status == "Incorrecta", "DW DUP no se trata como un operando de 32 bits");
        True(result.Lines[16].Status == "Correcta", "IDIV acepta un elemento de arreglo DW");
        True(result.Lines[17].Status == "Correcta", "POP acepta un elemento de arreglo DW");
        True(result.Lines[18].Status == "Correcta", "LES acepta almacenamiento de al menos cuatro bytes");
        True(result.Lines[19].Status == "Correcta", "EQU tiene ancho palabra");
        True(result.Lines[20].Status == "Correcta", "EQU numérico pequeño cabe en AL por contexto");
        True(result.Lines[21].Status == "Correcta", "una cadena DB conserva ancho byte por elemento");
        True(result.Lines[22].Status == "Incorrecta", "una cadena DB no se trata como palabra por longitud");
    }

    private static void EffectiveAddressing()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 10 dup(0)", "ends",
            ".data segment", "buffer db 8 dup(0)", "ends",
            ".code segment", "and [si], al", "and [bx+di], bh", "and [di+20], bh", "and ah, [si]",
            "lea ax, [437AH]", "lea ax, [si]", "lea ax, [bx+si+4]", "lea ax, [ax]",
            "lea ax, [si+di]", "lea ax, [bx+bp]", "lea ax, [missing]", "lea ax, [buffer+si]",
            "lea ax, [si+missing]", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[7].Status == "Correcta", "memoria [SI] toma ancho del registro compañero");
        True(result.Lines[8].Status == "Correcta", "acepta combinación base más índice [BX+DI]");
        True(result.Lines[9].Status == "Correcta", "acepta índice más desplazamiento [DI+20]");
        True(result.Lines[10].Status == "Correcta", "ancho del registro destino resuelve memoria fuente");
        True(result.Lines[11].Status == "Correcta", "acepta dirección directa numérica 8086");
        True(result.Lines[13].Status == "Correcta", "acepta base, índice y desplazamiento");
        True(result.Lines[14].Status == "Incorrecta", "rechaza AX como registro de dirección efectiva");
        True(result.Lines[15].Status == "Incorrecta", "rechaza dos registros índice");
        True(result.Lines[16].Status == "Incorrecta", "rechaza dos registros base");
        True(result.Lines[17].Status == "Incorrecta", "rechaza un símbolo no declarado dentro de corchetes");
        True(result.Lines[18].Status == "Correcta", "resuelve un símbolo declarado como desplazamiento");
        True(result.Lines[19].Status == "Incorrecta", "rechaza símbolo no declarado combinado con registro");
    }

    private static void PtrOnlyAppliesToMemory()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 10 dup(0)", "ends",
            ".data segment", "byteValue db 1", "wordValue dw 2", "limit equ 1", "ends",
            ".code segment", "and ax, byte ptr ax", "and ax, word ptr 1", "and ax, word ptr limit",
            "and ax, word ptr [bx]", "and al, byte ptr [si]", "idiv byte ptr [bx]", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[9].Status == "Incorrecta", "PTR no puede calificar un registro");
        True(result.Lines[10].Status == "Incorrecta", "PTR no puede calificar un inmediato");
        True(result.Lines[11].Status == "Incorrecta", "PTR no puede calificar una constante EQU");
        True(result.Lines[12].Status == "Correcta", "WORD PTR califica una referencia a memoria");
        True(result.Lines[13].Status == "Correcta", "BYTE PTR califica una referencia a memoria");
        True(result.Lines[14].Status == "Correcta", "IDIV acepta memoria de ancho explícito");
    }

    private static void RequiredSegmentContents()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "ends", ".data segment", "ends", ".code segment", "cbw", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[1].Status == "Incorrecta" && Contains(result.Lines[1].Message, "reserva"),
            "pila vacía requiere una reserva DW DUP");
        True(result.Lines[3].Status == "Incorrecta" && Contains(result.Lines[3].Message, "declaración"),
            "datos vacíos requieren al menos una declaración");
    }

    private static void UnicodeSymbols()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 1 dup(0)", "ends", ".data segment", "ñandú db 1", "ends",
            ".code segment", "ñfin:", "cbw", "jz ñfin", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[4].Status == "Correcta", "declaración con letra Unicode: " + result.Lines[4].Message);
        True(result.Lines[7].Status == "Correcta", "etiqueta Unicode: " + result.Lines[7].Message);
        True(result.Lines[9].Status == "Correcta", "salto a etiqueta Unicode previamente definida: " + result.Lines[9].Message);
        Equal(0, CountIncorrect(result), "nombres con letras Unicode siguen la regla de Fase 1");
        Equal("ñandú", Find(result, "ñandú").Symbol, "declaración Unicode en tabla de símbolos");
        Equal("ñfin", Find(result, "ñfin").Symbol, "etiqueta Unicode en tabla de símbolos");
    }

    private static void ImmediateWidths()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 10 dup(0)", "ends", ".data segment", "limit equ 12", "ends",
            ".code segment", "and ax, 1", "and ax, 65536", "and al, 256", "and ax, 045H",
            "and al, 045H", "and ax, 0ABCDH", "and al, 0ABCDH", "adc ax, 256", "adc al, 256",
            "and ax, limit", "and al, limit", "and ax, -32769", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[7].Status == "Correcta", "AND AX, 1 se codifica con el ancho que requiere AX");
        True(result.Lines[8].Status == "Incorrecta", "un inmediato mayor que una palabra no cabe en AX");
        True(result.Lines[9].Status == "Incorrecta", "un inmediato mayor que un byte no cabe en AL");
        True(result.Lines[10].Status == "Correcta", "AND AX acepta un valor hexadecimal byte extendido al destino");
        True(result.Lines[11].Status == "Correcta", "AND AL acepta un valor hexadecimal byte");
        True(result.Lines[12].Status == "Correcta", "AND AX acepta un inmediato palabra");
        True(result.Lines[13].Status == "Incorrecta", "un inmediato palabra fuera de rango no cabe en AL");
        True(result.Lines[14].Status == "Correcta", "ADC AX acepta un valor dentro del rango palabra");
        True(result.Lines[15].Status == "Incorrecta", "ADC AL rechaza un valor fuera del rango byte");
        True(result.Lines[16].Status == "Correcta", "EQU puede usarse como inmediato en AX");
        True(result.Lines[17].Status == "Correcta", "EQU inmediato pequeño cabe en AL por contexto");
        True(result.Lines[18].Status == "Incorrecta", "un inmediato menor que el mínimo de palabra se rechaza");
    }

    private static void SegmentHeaderForms()
    {
        string[] sources = {
            String.Join(Environment.NewLine, new string[] {
                ".STACK", "dw 10 dup(0)", "ends", ".DATA", "value db 1", "ends",
                ".CODE", "inicio:", "clc", "ends"
            }),
            String.Join(Environment.NewLine, new string[] {
                "STACK SEGMENT", "dw 10 dup(0)", "ends", "DATA SEGMENT", "value db 1", "ends",
                "CODE SEGMENT", "inicio:", "clc", "ends"
            })
        };

        foreach (string source in sources)
        {
            ProgramAnalysisResult result = Analyze(source);
            Equal(source.Split(new string[] { Environment.NewLine }, StringSplitOptions.None).Length,
                result.Lines.Count, "una salida por línea en encabezados alternativos");
            foreach (LineAnalysisResult line in result.Lines)
                True(line.Status == "Correcta", "encabezado alternativo: " + line.Source + " => " + line.Message);
            Equal("Etiqueta", Find(result, "inicio").Type, "la etiqueta sigue en la tabla");
        }

        ProgramAnalysisResult invalid = Analyze(String.Join(Environment.NewLine, new string[] {
            "stack", "dw 10 dup(0)", "ends", "data", "value db 1", "ends", "code", "clc", "ends"
        }));
        True(invalid.Lines[0].Status == "Incorrecta", "no se acepta el nombre del segmento sin una forma completa");
    }

    private static void LesDwordPointer()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 10 dup(0)", "ends", ".data segment",
            "farptr dw 2 dup(0)", "small db 1", "ends", ".code segment",
            "les ax, DWORD PTR [bx]", "les bx, DWORD PTR farptr", "les ax, DWORD PTR small",
            "les ax, WORD PTR farptr", "les ax, DWORD PTR 123", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        True(result.Lines[8].Status == "Correcta", "LES acepta una referencia de memoria con DWORD PTR");
        True(result.Lines[9].Status == "Correcta", "LES acepta cuatro bytes declarados con DWORD PTR");
        True(result.Lines[10].Status == "Incorrecta", "LES rechaza un objeto de memoria menor de cuatro bytes");
        True(result.Lines[11].Status == "Incorrecta", "LES rechaza un calificador WORD PTR");
        True(result.Lines[12].Status == "Incorrecta", "DWORD PTR no convierte un inmediato en memoria");
    }

    private static void AndAdcOperandWidths()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 10 dup(0)", "ends", ".data segment",
            "byteValue db 1", "wordValue dw 1", "ends", ".code segment",
            "and al, bl", "adc ax, bx", "and al, byteValue", "adc ax, wordValue",
            "and al, bx", "adc ax, bl", "and al, wordValue", "adc ax, byteValue", "ends"
        });
        ProgramAnalysisResult result = Analyze(source);
        for (int i = 8; i <= 11; i++)
            True(result.Lines[i].Status == "Correcta", "AND/ADC con tamaños compatibles: " + result.Lines[i].Source);
        for (int i = 12; i <= 15; i++)
            True(result.Lines[i].Status == "Incorrecta", "AND/ADC con tamaños incompatibles: " + result.Lines[i].Source);
    }

    private static ProgramAnalysisResult Analyze(string source)
    {
        List<Lexeme> lexemes = LexicalAnalysisCore.Analyze(source, TeamTwo);
        return SyntaxSemanticCore.Analyze(source, lexemes, TeamTwo);
    }

    private static SymbolEntry Find(ProgramAnalysisResult result, string name)
    {
        foreach (SymbolEntry symbol in result.Symbols)
            if (String.Equals(symbol.Symbol, name, StringComparison.OrdinalIgnoreCase)) return symbol;
        throw new Exception("Falta símbolo: " + name);
    }

    private static int CountIncorrect(ProgramAnalysisResult result)
    {
        int count = 0;
        foreach (LineAnalysisResult line in result.Lines) if (line.Status == "Incorrecta") count++;
        return count;
    }

    private static bool Contains(string value, string expected) { return value.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0; }
    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!Object.Equals(expected, actual)) throw new Exception(message + ": esperado " + expected + ", recibido " + actual);
    }
    private static void True(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("OK: " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FALLO: " + name + ": " + ex.Message); }
    }
}
