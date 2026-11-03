using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using AnalizadorFase2;

internal static class GuiSmokeTests
{
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (MainForm form = new MainForm())
        {
            IntPtr formHandle = form.Handle;
            CreateHandles(form);
            form.PerformLayout();

            VerifyTeamTwoInstructions(form);
            Run("vista de resultados sintáctico-semánticos", VerifySyntaxSemanticResultsView);

            List<GroupBox> groups = Find<GroupBox>(form);
            List<DataGridView> grids = Find<DataGridView>(form);
            List<TextBox> textBoxes = Find<TextBox>(form);
            Assert(formHandle != IntPtr.Zero, "la ventana debe crear su handle");
            Assert(groups.Count == 3, "la ventana debe mostrar fuente, análisis léxico y tabla de símbolos");
            Assert(grids.Count == 3 && grids[0].Columns.Count == 3 && grids[1].Columns.Count == 4,
                "la ventana debe mostrar lista léxica y dictamen sintáctico-semántico integrado");
            Assert(grids[0].ScrollBars == ScrollBars.None, "la lista se muestra paginada sin barras de desplazamiento");
            Assert(textBoxes.Count == 1 && textBoxes[0].ReadOnly, "el archivo fuente debe mostrarse en un panel de solo lectura");
            Assert(textBoxes[0].ScrollBars == ScrollBars.Vertical && textBoxes[0].WordWrap,
                "la paginacion permite recorrer una linea mayor que la pantalla");

            Run("zoom", delegate { VerifyZoom(form, grids[0], textBoxes[0]); });
            Run("paginacion independiente", delegate { VerifyIndependentPagination(form, grids[0], textBoxes[0]); });
            Run("reajuste de fuente", delegate { VerifySourceReflowsOnZoom(form, textBoxes[0]); });
            Run("barra visible en ventana minima", delegate { VerifyToolbarFits(form); });
            Run("linea mayor que una pagina", delegate { VerifyOversizedLine(form, textBoxes[0]); });
            Run("archivo vacio y codificaciones", delegate { VerifyFileContents(form, textBoxes[0]); });
            Run("lineas vacias de la fuente", delegate { VerifyEmptyLines(form, textBoxes[0]); });
        }
        Console.WriteLine("Interfaz: {0} fallos", _failed);
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action action)
    {
        try { action(); Console.WriteLine("OK: " + name); }
        catch (Exception ex) { _failed++; Console.WriteLine("FALLO: " + name + ": " + ex); }
    }

    private static void VerifyTeamTwoInstructions(MainForm form)
    {
        string[] expected = new string[] {
            "CBW", "CLC", "LODSB", "LODSW", "STOSB", "AAA", "POP", "IDIV", "INT", "AND",
            "ADC", "LEA", "LES", "JNZ", "JZ", "LOOPNZ", "JGE", "JNA", "JNL", "JB"
        };
        FieldInfo field = typeof(MainForm).GetField("_assignedInstructions", BindingFlags.Instance | BindingFlags.NonPublic);
        List<string> actual = field.GetValue(form) as List<string>;
        Assert(actual != null && actual.Count == expected.Length, "se cargan las 20 instrucciones del equipo 2");
        foreach (string instruction in expected)
            Assert(actual.Exists(value => String.Equals(value, instruction, StringComparison.OrdinalIgnoreCase)),
                "configuración del equipo 2 contiene " + instruction);

        Assert(Find<SyntaxSemanticResultsForm>(form).Count == 1,
            "los resultados de Fase 2 están integrados en la ventana principal");

        List<Lexeme> analyzed = LexicalAnalysisCore.Analyze(String.Join(" ", expected) + " JE MOV", actual);
        for (int i = 0; i < expected.Length; i++)
            Assert(analyzed[i].Type == "Instrucci\u00f3n", expected[i] + " se identifica como instruccion asignada");
        Assert(analyzed[expected.Length].Text == "JE" && analyzed[expected.Length].Type == "S\u00edmbolo",
            "JE queda como simbolo porque esta asignada a otro equipo");
        Assert(analyzed[expected.Length + 1].Text == "MOV" && analyzed[expected.Length + 1].Type == "S\u00edmbolo",
            "MOV queda como simbolo porque no esta asignada al equipo 2");
    }

    private static void VerifySyntaxSemanticResultsView()
    {
        string source = String.Join(Environment.NewLine, new string[] {
            ".stack segment", "dw 1 dup(0)", "ends",
            ".data segment", "item db 1", "ends",
            ".code segment", "inicio:", "int 21", "ends"
        });
        string[] assigned = new string[] {
            "CBW", "CLC", "LODSB", "LODSW", "STOSB", "AAA", "POP", "IDIV", "INT", "AND",
            "ADC", "LEA", "LES", "JNZ", "JZ", "LOOPNZ", "JGE", "JNA", "JNL", "JB"
        };
        List<Lexeme> lexemes = LexicalAnalysisCore.Analyze(source, assigned);
        ProgramAnalysisResult result = SyntaxSemanticCore.Analyze(source, lexemes, assigned);
        using (SyntaxSemanticResultsForm form = new SyntaxSemanticResultsForm(result))
        {
            List<DataGridView> grids = Find<DataGridView>(form);
            Assert(grids.Count == 2, "la vista contiene resultados por línea y tabla de símbolos");
            Assert(grids[0].Columns.Count == 4 && grids[0].Rows.Count == 10,
                "la vista presenta cada línea con fuente, resultado y explicación");
            Assert(Convert.ToString(grids[0].Rows[8].Cells[2].Value) == "Correcta",
                "la línea INT válida aparece como Correcta");
            Assert(grids[1].Columns.Count == 4 && grids[1].Rows.Count == 2,
                "la tabla presenta los campos requeridos y datos más etiqueta");
            Assert(Convert.ToString(grids[1].Columns[0].HeaderText) == "Símbolo" &&
                Convert.ToString(grids[1].Columns[3].HeaderText) == "Tamaño",
                "la tabla usa los encabezados solicitados");
        }
    }

    private static void VerifyZoom(MainForm form, DataGridView grid, TextBox sourceView)
    {
        LinkLabel zoomReset = Find<LinkLabel>(form).Find(link => link.Text == "Zoom 100%");
        Assert(zoomReset != null, "la barra debe ofrecer un restablecimiento discreto de zoom");

        float sourceSize = sourceView.Font.Size;
        float elementSize = grid.DefaultCellStyle.Font.Size;
        DataGridView resultGrid = Find<DataGridView>(GetField<SyntaxSemanticResultsForm>(form))[0];
        float resultSize = resultGrid.DefaultCellStyle.Font.Size;
        MethodInfo changeZoom = typeof(MainForm).GetMethod("ChangeZoom", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo setZoom = typeof(MainForm).GetMethod("SetZoom", BindingFlags.Instance | BindingFlags.NonPublic);
        changeZoom.Invoke(form, new object[] { 10 });
        Assert(sourceView.Font.Size > sourceSize && grid.DefaultCellStyle.Font.Size > elementSize &&
            resultGrid.DefaultCellStyle.Font.Size > resultSize,
            "el zoom amplía fuente, elementos y resultados");
        Assert(zoomReset.Text == "Zoom 110%", "el indicador refleja el zoom aplicado");

        setZoom.Invoke(form, new object[] { 100 });
        Assert(sourceView.Font.Size == sourceSize && grid.DefaultCellStyle.Font.Size == elementSize,
            "100 por ciento restaura el tamaño inicial");
        changeZoom.Invoke(form, new object[] { -10 });
        Assert(sourceView.Font.Size < sourceSize && grid.DefaultCellStyle.Font.Size < elementSize &&
            resultGrid.DefaultCellStyle.Font.Size < resultSize,
            "el zoom reduce fuente, elementos y resultados");
    }

    private static void VerifyIndependentPagination(MainForm form, DataGridView grid, TextBox sourceView)
    {
        string directory = Path.Combine(Path.GetTempPath(), "AnalizadorFase2-gui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "paginacion.ens");
        List<string> lines = new List<string>();
        for (int i = 0; i < 51; i++) lines.Add("mov ax, 1 ; comentario");
        File.WriteAllText(path, String.Join(Environment.NewLine, lines));
        try
        {
            MethodInfo load = typeof(MainForm).GetMethod("LoadFile", BindingFlags.Instance | BindingFlags.NonPublic);
            load.Invoke(form, new object[] { path });
            SyntaxSemanticResultsForm resultsPanel = GetField<SyntaxSemanticResultsForm>(form);
            DataGridView resultGrid = Find<DataGridView>(resultsPanel)[0];
            Assert(resultGrid.Rows.Count == lines.Count,
                "los resultados por línea se actualizan automáticamente al abrir el .ens");

            Label sourcePage = GetField<Label>(form, "_sourcePageLabel");
            Label elementsPage = GetField<Label>(form, "_elementsPageLabel");
            Button sourceNext = GetField<Button>(form, "_sourceNext");
            Button elementsNext = GetField<Button>(form, "_elementsNext");
            MethodInfo changeSource = typeof(MainForm).GetMethod("ChangeSourcePage", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo changeElements = typeof(MainForm).GetMethod("ChangeElementsPage", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert(sourcePage.Text.StartsWith("P\u00e1gina 1 de 3", StringComparison.Ordinal), "fuente paginada en grupos de 25 lineas");
            Assert(elementsPage.Text.StartsWith("P\u00e1gina 1 de ", StringComparison.Ordinal), "la lista indica su pagina actual y total");
            int firstPageSize = grid.Rows.Count;
            Assert(firstPageSize > 0 && firstPageSize <= 50, "la pagina de elementos se ajusta al espacio visible");
            Assert(Convert.ToString(grid.Rows[0].Cells[0].Value) == "1" &&
                Convert.ToString(grid.Rows[firstPageSize - 1].Cells[0].Value) == Convert.ToString(firstPageSize),
                "numeracion global de la primera pagina");
            Assert(sourceView.Lines.Length == 25, "primera pagina de fuente contiene 25 lineas");

            Assert(sourceNext.Enabled, "el boton siguiente de fuente debe habilitarse");
            changeSource.Invoke(form, new object[] { 1 });
            Assert(sourcePage.Text.StartsWith("P\u00e1gina 2 de 3", StringComparison.Ordinal), "navega la fuente");
            Assert(elementsPage.Text.StartsWith("P\u00e1gina 1 de ", StringComparison.Ordinal), "la fuente no cambia la pagina de la lista");

            Assert(elementsNext.Enabled, "el boton siguiente de elementos debe habilitarse");
            changeElements.Invoke(form, new object[] { 1 });
            Assert(sourcePage.Text.StartsWith("P\u00e1gina 2 de 3", StringComparison.Ordinal), "la lista no cambia la pagina de fuente");
            Assert(elementsPage.Text.StartsWith("P\u00e1gina 2 de ", StringComparison.Ordinal), "navega la lista de elementos");
            Assert(Convert.ToString(grid.Rows[0].Cells[0].Value) == Convert.ToString(firstPageSize + 1),
                "la numeracion continua en la siguiente pagina");

            MethodInfo setZoom = typeof(MainForm).GetMethod("SetZoom", BindingFlags.Instance | BindingFlags.NonPublic);
            int rowsAtCurrentZoom = grid.Rows.Count;
            setZoom.Invoke(form, new object[] { 150 });
            Assert(grid.ScrollBars == ScrollBars.None && grid.Rows.Count > 0 && grid.Rows.Count <= rowsAtCurrentZoom,
                "la paginacion de resultados se reajusta al zoom sin scroll");
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static void VerifySourceReflowsOnZoom(MainForm form, TextBox sourceView)
    {
        List<string> lines = new List<string>();
        StringBuilder longLine = new StringBuilder();
        for (int i = 0; i < 40; i++) longLine.Append(" MOV");
        for (int i = 0; i < 51; i++) lines.Add(longLine.ToString());

        FieldInfo sourceLines = typeof(MainForm).GetField("_sourceLines", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo sourcePage = typeof(MainForm).GetField("_sourcePage", BindingFlags.Instance | BindingFlags.NonPublic);
        sourceLines.SetValue(form, lines.ToArray());
        sourcePage.SetValue(form, 0);
        MethodInfo setZoom = typeof(MainForm).GetMethod("SetZoom", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo buildPageStarts = typeof(MainForm).GetMethod("BuildSourcePageStarts", BindingFlags.Instance | BindingFlags.NonPublic);

        setZoom.Invoke(form, new object[] { 100 });
        int pagesAt100 = ((List<int>)buildPageStarts.Invoke(form, null)).Count;
        setZoom.Invoke(form, new object[] { 150 });
        int pagesAt150 = ((List<int>)buildPageStarts.Invoke(form, null)).Count;
        Assert(sourceView.ScrollBars == ScrollBars.Vertical && sourceView.WordWrap,
            "el codigo se envuelve y permite recorrer una linea extensa");
        Assert(pagesAt150 > pagesAt100, "las paginas de instrucciones se reajustan cuando aumenta el zoom");
    }

    private static T GetField<T>(MainForm form, string name) where T : class
    {
        FieldInfo field = typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        return field.GetValue(form) as T;
    }

    private static void VerifyToolbarFits(MainForm form)
    {
        Size originalSize = form.Size;
        try
        {
            form.Size = form.MinimumSize;
            form.PerformLayout();
            foreach (string name in new string[] { "_fileLabel", "_statusLabel" })
            {
                Label label = GetField<Label>(form, name);
                Point location = Point.Empty;
                for (Control ancestor = label; ancestor != form; ancestor = ancestor.Parent)
                    location.Offset(ancestor.Location);
                Assert(location.X >= 0 && location.X + label.Width <= form.ClientSize.Width,
                    name + " dentro de ventana: x=" + location.X + ", ancho=" + label.Width + ", cliente=" + form.ClientSize.Width + ", padre=" + label.Parent.Size);
            }
        }
        finally { form.Size = originalSize; form.PerformLayout(); }
    }

    private static void VerifyOversizedLine(MainForm form, TextBox sourceView)
    {
        string line = String.Join(" ", new string('a', 8000).ToCharArray());
        typeof(MainForm).GetField("_sourceLines", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, new string[] { line });
        typeof(MainForm).GetMethod("ShowSourcePage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        Assert(sourceView.Text == line, "se conserva completa una linea muy larga");
        Point last = sourceView.GetPositionFromCharIndex(sourceView.TextLength - 1);
        Assert(last.Y < sourceView.ClientSize.Height || sourceView.ScrollBars == ScrollBars.Vertical || sourceView.ScrollBars == ScrollBars.Both,
            "se puede llegar al final de una linea mayor que la altura de la pagina");
    }

    private static void VerifyFileContents(MainForm form, TextBox sourceView)
    {
        string directory = Path.Combine(Path.GetTempPath(), "AnalizadorFase2-encoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "contenido.ens");
        MethodInfo load = typeof(MainForm).GetMethod("LoadFile", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            foreach (Encoding encoding in new Encoding[] { new UTF8Encoding(false), Encoding.Unicode, Encoding.GetEncoding(1252) })
            {
                File.WriteAllText(path, "niño db 'áéíóú'", encoding);
                load.Invoke(form, new object[] { path });
                Assert(sourceView.Text == "niño db 'áéíóú'", "lectura de " + encoding.WebName);
            }
            File.WriteAllText(path, String.Empty);
            load.Invoke(form, new object[] { path });
            Assert(sourceView.Text.Length == 0, "fuente vacia");
            Assert(GetField<Label>(form, "_sourcePageLabel").Text.Contains("Archivo vacío"), "informa que el archivo esta vacio");
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }

    private static void VerifyEmptyLines(MainForm form, TextBox sourceView)
    {
        typeof(MainForm).GetField("_sourceLines", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(form, new string[] { "", "", "DB 0", "" });
        typeof(MainForm).GetMethod("ShowSourcePage", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
        Assert(sourceView.Text == Environment.NewLine + Environment.NewLine + "DB 0" + Environment.NewLine,
            "la vista conserva las lineas vacias iniciales y finales");
    }

    private static void CreateHandles(Control parent)
    {
        parent.CreateControl();
        foreach (Control child in parent.Controls) CreateHandles(child);
    }

    private static List<T> Find<T>(Control parent) where T : Control
    {
        List<T> found = new List<T>();
        if (parent is T) found.Add((T)parent);
        foreach (Control child in parent.Controls) found.AddRange(Find<T>(child));
        return found;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("Fallo: " + message);
    }
}
