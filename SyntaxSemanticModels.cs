using System;
using System.Collections.Generic;

namespace AnalizadorFase2
{
    public sealed class LineAnalysisResult
    {
        public int LineNumber { get; private set; }
        public string Source { get; private set; }
        public string Status { get; private set; }
        public string Message { get; private set; }

        internal LineAnalysisResult(int lineNumber, string source)
        {
            LineNumber = lineNumber;
            Source = source ?? String.Empty;
            Status = "Correcta";
            Message = "Línea vacía o comentario; se omite.";
        }

        internal void SetValid(string message)
        {
            if (Status == "Incorrecta") return;
            Status = "Correcta";
            Message = message;
        }

        internal void AddError(string message)
        {
            Status = "Incorrecta";
            if (String.IsNullOrWhiteSpace(Message) || Message == "Línea vacía o comentario; se omite.")
                Message = message;
            else
                Message += " " + message;
        }
    }

    public sealed class SymbolEntry
    {
        public string Symbol { get; private set; }
        public string Type { get; private set; }
        public string Value { get; private set; }
        public string Size { get; private set; }
        public int LineNumber { get; private set; }

        internal SymbolEntry(string symbol, string type, string value, string size, int lineNumber)
        {
            Symbol = symbol;
            Type = type;
            Value = value;
            Size = size;
            LineNumber = lineNumber;
        }
    }

    public sealed class ProgramAnalysisResult
    {
        public IList<LineAnalysisResult> Lines { get; private set; }
        public IList<SymbolEntry> Symbols { get; private set; }

        internal ProgramAnalysisResult()
        {
            Lines = new List<LineAnalysisResult>();
            Symbols = new List<SymbolEntry>();
        }
    }
}
