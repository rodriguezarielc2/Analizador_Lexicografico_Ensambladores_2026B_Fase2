using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace AnalizadorFase2
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form, IMessageFilter
    {
        private const int SourceLinesPerPage = 25;
        private const int ElementsPerPage = 50;
        private const int MinimumZoomPercent = 80;
        private const int MaximumZoomPercent = 150;
        private const int ZoomStepPercent = 10;
        private const int WmMouseWheel = 0x020A;

        private readonly Label _fileLabel;
        private readonly Label _statusLabel;
        private readonly TextBox _sourceView;
        private readonly DataGridView _elementsGrid;
        private readonly Button _sourcePrevious;
        private readonly Button _sourceNext;
        private readonly Button _elementsPrevious;
        private readonly Button _elementsNext;
        private readonly LinkLabel _zoomResetLink;
        private readonly SyntaxSemanticResultsForm _resultsPanel;
        private readonly ToolTip _toolTip;

        private readonly List<string> _assignedInstructions;
        private List<Lexeme> _elements = new List<Lexeme>();
        private ProgramAnalysisResult _semanticResult;
        private string[] _sourceLines = new string[0];
        private string _source = String.Empty;
        private string _filePath;
        private int _sourcePage;
        private int _elementsPage;
        private int _zoomPercent = 100;
        private bool _mouseWheelFilterAttached;

        public MainForm()
        {
            Text = "Analizador de ensamblador | Fase 2";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(1080, 740);
            Size = new Size(1440, 940);
            BackColor = Color.FromArgb(239, 243, 247);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            _assignedInstructions = LoadAssignedInstructions();
            _toolTip = new ToolTip();
            _fileLabel = new Label();
            _statusLabel = new Label();
            _sourceView = new TextBox();
            _elementsGrid = new DataGridView();
            _sourcePrevious = new Button();
            _sourceNext = new Button();
            _elementsPrevious = new Button();
            _elementsNext = new Button();
            _zoomResetLink = new LinkLabel();
            _resultsPanel = new SyntaxSemanticResultsForm();

            BuildWindow();
            _sourceView.Resize += delegate { ShowSourcePage(); };
            _elementsGrid.Resize += delegate { ShowElementsPage(); };
            Shown += OnMainFormShown;
            FormClosed += OnMainFormClosed;
            ShowSourcePage();
            ShowElementsPage();
            UpdateStatus();
        }

        private void BuildWindow()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 2;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(root);

            Panel toolbar = BuildToolbar();
            root.Controls.Add(toolbar, 0, 0);

            SplitContainer contentSplit = new SplitContainer();
            contentSplit.Dock = DockStyle.Fill;
            contentSplit.Orientation = Orientation.Horizontal;
            contentSplit.Size = new Size(1200, 700);
            contentSplit.SplitterWidth = 6;
            contentSplit.SplitterDistance = 380;
            contentSplit.Panel1MinSize = 230;
            contentSplit.Panel2MinSize = 360;
            contentSplit.BackColor = Color.FromArgb(213, 222, 231);
            root.Controls.Add(contentSplit, 0, 1);

            SplitContainer workspaceSplit = new SplitContainer();
            workspaceSplit.Dock = DockStyle.Fill;
            workspaceSplit.Orientation = Orientation.Vertical;
            workspaceSplit.Size = new Size(1200, 400);
            workspaceSplit.SplitterWidth = 6;
            workspaceSplit.SplitterDistance = 600;
            workspaceSplit.Panel1MinSize = 330;
            workspaceSplit.Panel2MinSize = 330;
            workspaceSplit.BackColor = Color.FromArgb(213, 222, 231);
            contentSplit.Panel1.Controls.Add(workspaceSplit);

            GroupBox sourceGroup = new GroupBox();
            sourceGroup.Text = "1. Archivo fuente";
            sourceGroup.Dock = DockStyle.Fill;
            sourceGroup.Padding = new Padding(10);
            sourceGroup.ForeColor = Color.FromArgb(23, 50, 77);
            sourceGroup.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            workspaceSplit.Panel1.Controls.Add(sourceGroup);

            GroupBox elementsGroup = new GroupBox();
            elementsGroup.Text = "2. Separaci\u00f3n e identificaci\u00f3n";
            elementsGroup.Dock = DockStyle.Fill;
            elementsGroup.Padding = new Padding(10);
            elementsGroup.ForeColor = Color.FromArgb(23, 50, 77);
            elementsGroup.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            workspaceSplit.Panel2.Controls.Add(elementsGroup);

            BuildSourcePanel(sourceGroup);
            BuildElementsPanel(elementsGroup);
            _resultsPanel.Dock = DockStyle.Fill;
            contentSplit.Panel2.Controls.Add(_resultsPanel);

            Shown += delegate
            {
                SetInitialSplitterDistance(contentSplit, 0.46F);
                SetInitialSplitterDistance(workspaceSplit, 0.5F);
            };
        }

        private static void SetInitialSplitterDistance(SplitContainer split, float firstPanelRatio)
        {
            int total = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
            int maximum = total - split.SplitterWidth - split.Panel2MinSize;
            if (maximum < split.Panel1MinSize) return;
            int distance = (int)Math.Round((total - split.SplitterWidth) * firstPanelRatio);
            split.SplitterDistance = Math.Max(split.Panel1MinSize, Math.Min(maximum, distance));
        }

        private Panel BuildToolbar()
        {
            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Fill;
            toolbar.BackColor = Color.FromArgb(23, 50, 77);
            toolbar.Padding = new Padding(12, 8, 12, 8);

            FlowLayoutPanel row = new FlowLayoutPanel();
            row.Dock = DockStyle.Top;
            row.Height = 40;
            row.WrapContents = false;
            row.FlowDirection = FlowDirection.LeftToRight;
            row.BackColor = toolbar.BackColor;
            toolbar.Controls.Add(row);

            Label title = new Label();
            title.Text = "ANALIZADOR ENSAMBLADOR · FASE 2";
            title.ForeColor = Color.White;
            title.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            title.AutoSize = false;
            title.Width = 310;
            title.Height = 38;
            title.TextAlign = ContentAlignment.MiddleLeft;
            row.Controls.Add(title);

            Button openButton = CreateButton("Abrir archivo .ens", Color.FromArgb(25, 165, 138));
            openButton.Width = 145;
            openButton.Click += OpenFile;
            row.Controls.Add(openButton);

            _zoomResetLink.Text = "Zoom 100%";
            _zoomResetLink.AutoSize = true;
            _zoomResetLink.Height = 36;
            _zoomResetLink.Margin = new Padding(12, 10, 4, 0);
            _zoomResetLink.LinkColor = Color.FromArgb(184, 215, 239);
            _zoomResetLink.ActiveLinkColor = Color.White;
            _zoomResetLink.VisitedLinkColor = Color.FromArgb(184, 215, 239);
            _zoomResetLink.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
            _zoomResetLink.LinkClicked += delegate { SetZoom(100); };
            row.Controls.Add(_zoomResetLink);
            _toolTip.SetToolTip(_zoomResetLink, "Clic para restablecer el zoom. Usa Ctrl + rueda para acercar o alejar.");

            Panel information = new Panel();
            information.Dock = DockStyle.Bottom;
            information.Height = 24;
            toolbar.Controls.Add(information);

            _fileLabel.Text = "Selecciona un archivo fuente";
            _fileLabel.ForeColor = Color.White;
            _fileLabel.AutoEllipsis = true;
            _fileLabel.Dock = DockStyle.Fill;
            _fileLabel.TextAlign = ContentAlignment.MiddleLeft;
            information.Controls.Add(_fileLabel);
            _toolTip.SetToolTip(_fileLabel, "La aplicaci\u00f3n permite abrir archivos .ens desde cualquier carpeta.");

            _statusLabel.ForeColor = Color.FromArgb(217, 229, 239);
            _statusLabel.AutoEllipsis = true;
            _statusLabel.Width = 350;
            _statusLabel.Dock = DockStyle.Right;
            _statusLabel.TextAlign = ContentAlignment.MiddleRight;
            information.Controls.Add(_statusLabel);
            return toolbar;
        }

        private void BuildSourcePanel(GroupBox group)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            group.Controls.Add(layout);

            _sourceView.Dock = DockStyle.Fill;
            _sourceView.Multiline = true;
            _sourceView.ReadOnly = true;
            _sourceView.WordWrap = true;
            _sourceView.ScrollBars = ScrollBars.Vertical;
            _sourceView.BackColor = Color.White;
            _sourceView.ForeColor = Color.FromArgb(23, 43, 77);
            _sourceView.Font = new Font("Consolas", 10F, FontStyle.Regular, GraphicsUnit.Point);
            _sourceView.BorderStyle = BorderStyle.FixedSingle;
            layout.Controls.Add(_sourceView, 0, 0);

            FlowLayoutPanel pager = CreatePager(_sourcePrevious, _sourceNext);
            _sourcePrevious.Click += delegate { ChangeSourcePage(-1); };
            _sourceNext.Click += delegate { ChangeSourcePage(1); };
            layout.Controls.Add(pager, 0, 1);
        }

        private void BuildElementsPanel(GroupBox group)
        {
            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
            group.Controls.Add(layout);

            _elementsGrid.Dock = DockStyle.Fill;
            _elementsGrid.ScrollBars = ScrollBars.None;
            _elementsGrid.ReadOnly = true;
            _elementsGrid.AllowUserToAddRows = false;
            _elementsGrid.AllowUserToDeleteRows = false;
            _elementsGrid.AllowUserToResizeRows = false;
            _elementsGrid.MultiSelect = false;
            _elementsGrid.RowHeadersVisible = false;
            _elementsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _elementsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _elementsGrid.BackgroundColor = Color.White;
            _elementsGrid.BorderStyle = BorderStyle.FixedSingle;
            _elementsGrid.EnableHeadersVisualStyles = false;
            _elementsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(220, 230, 239);
            _elementsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(23, 50, 77);
            _elementsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _elementsGrid.DefaultCellStyle.Font = new Font("Consolas", 9.5F, FontStyle.Regular);
            _elementsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(199, 231, 226);
            _elementsGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(16, 42, 67);
            _elementsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252);
            _elementsGrid.Columns.Add("number", "No.");
            _elementsGrid.Columns.Add("element", "Elemento separado");
            _elementsGrid.Columns.Add("type", "Tipo de elemento");
            _elementsGrid.Columns[0].FillWeight = 13F;
            _elementsGrid.Columns[1].FillWeight = 46F;
            _elementsGrid.Columns[2].FillWeight = 41F;
            layout.Controls.Add(_elementsGrid, 0, 0);

            FlowLayoutPanel pager = CreatePager(_elementsPrevious, _elementsNext);
            _elementsPrevious.Click += delegate { ChangeElementsPage(-1); };
            _elementsNext.Click += delegate { ChangeElementsPage(1); };
            layout.Controls.Add(pager, 0, 1);
        }

        private static FlowLayoutPanel CreatePager(Button previous, Button next)
        {
            previous.Text = "\u2039 Anterior";
            previous.Width = 104;
            previous.Height = 30;
            previous.Margin = new Padding(4, 0, 0, 0);
            previous.FlatStyle = FlatStyle.Flat;
            previous.BackColor = Color.FromArgb(220, 230, 239);
            previous.ForeColor = Color.FromArgb(23, 50, 77);
            previous.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
            previous.FlatAppearance.BorderColor = Color.FromArgb(190, 203, 216);

            next.Text = "Siguiente \u203a";
            next.Width = 104;
            next.Height = 30;
            next.Margin = new Padding(4, 0, 0, 0);
            next.FlatStyle = FlatStyle.Flat;
            next.BackColor = Color.FromArgb(220, 230, 239);
            next.ForeColor = Color.FromArgb(23, 50, 77);
            next.Font = new Font("Segoe UI", 8.5F, FontStyle.Regular);
            next.FlatAppearance.BorderColor = Color.FromArgb(190, 203, 216);

            FlowLayoutPanel panel = new FlowLayoutPanel();
            panel.Dock = DockStyle.Fill;
            panel.FlowDirection = FlowDirection.RightToLeft;
            panel.WrapContents = false;
            panel.Padding = new Padding(0, 3, 0, 0);
            panel.Controls.Add(next);
            panel.Controls.Add(previous);
            return panel;
        }

        private static Button CreateButton(string text, Color color)
        {
            Button button = new Button();
            button.Text = text;
            button.Height = 36;
            button.Margin = new Padding(0, 1, 8, 1);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            return button;
        }

        private void ChangeZoom(int change)
        {
            SetZoom(_zoomPercent + change);
        }

        private void SetZoom(int zoomPercent)
        {
            _zoomPercent = Math.Max(MinimumZoomPercent, Math.Min(MaximumZoomPercent, zoomPercent));
            float scale = _zoomPercent / 100F;
            _sourceView.Font = new Font("Consolas", 10F * scale, FontStyle.Regular, GraphicsUnit.Point);
            _elementsGrid.DefaultCellStyle.Font = new Font("Consolas", 9.5F * scale, FontStyle.Regular, GraphicsUnit.Point);
            _elementsGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F * scale, FontStyle.Bold, GraphicsUnit.Point);

            _elementsGrid.ColumnHeadersHeight = Math.Max(26, (int)Math.Ceiling(26F * scale));
            int rowHeight = Math.Max(24, (int)Math.Ceiling(24F * scale));
            _elementsGrid.RowTemplate.Height = rowHeight;
            foreach (DataGridViewRow row in _elementsGrid.Rows)
                row.Height = rowHeight;

            _zoomResetLink.Text = "Zoom " + _zoomPercent.ToString() + "%";
            _resultsPanel.ApplyZoom(_zoomPercent);
            ShowSourcePage();
            ShowElementsPage();
        }

        private void OnMainFormShown(object sender, EventArgs e)
        {
            if (_mouseWheelFilterAttached) return;
            Application.AddMessageFilter(this);
            _mouseWheelFilterAttached = true;
        }

        private void OnMainFormClosed(object sender, FormClosedEventArgs e)
        {
            if (!_mouseWheelFilterAttached) return;
            Application.RemoveMessageFilter(this);
            _mouseWheelFilterAttached = false;
        }

        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != WmMouseWheel || (Control.ModifierKeys & Keys.Control) == 0)
                return false;
            if (!ContainsFocus || !RectangleToScreen(ClientRectangle).Contains(Control.MousePosition))
                return false;

            int delta = unchecked((short)((message.WParam.ToInt64() >> 16) & 0xFFFF));
            if (delta == 0) return false;
            ChangeZoom(delta > 0 ? ZoomStepPercent : -ZoomStepPercent);
            return true;
        }

        private void OpenFile(object sender, EventArgs e)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Selecciona el programa fuente";
                dialog.Filter = "Todos los archivos (*.*)|*.*";
                dialog.CheckFileExists = true;
                dialog.CheckPathExists = true;
                dialog.ValidateNames = true;
                dialog.Multiselect = false;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadFile(dialog.FileName);
            }
        }

        private void LoadFile(string path)
        {
            string error;
            if (!LexicalAnalysisCore.ValidateSourcePath(path, out error))
            {
                MessageBox.Show(this, error, "No se puede abrir el archivo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                string source;
                try
                {
                    source = File.ReadAllText(path, new UTF8Encoding(false, true));
                }
                catch (DecoderFallbackException)
                {
                    source = File.ReadAllText(path, Encoding.Default);
                }

                _filePath = path;
                _source = source;
                _sourceLines = source.Length == 0 ? new string[0] :
                    source.Split(new string[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                _fileLabel.Text = Path.GetFileName(path);
                _toolTip.SetToolTip(_fileLabel, path);
                _sourcePage = 0;
                _elementsPage = 0;
                AnalyzeCurrentFile();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo leer el archivo: " + ex.Message,
                    "Error de lectura", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AnalyzeCurrentFile()
        {
            if (String.IsNullOrEmpty(_filePath)) return;
            _elements = LexicalAnalysisCore.Analyze(_source, _assignedInstructions);
            _semanticResult = SyntaxSemanticCore.Analyze(_source, _elements, _assignedInstructions);
            _resultsPanel.SetResult(_semanticResult);
            _elementsPage = 0;
            ShowSourcePage();
            ShowElementsPage();
            UpdateStatus();
        }

        private void ChangeSourcePage(int change)
        {
            int count = BuildSourcePageStarts().Count;
            _sourcePage = Math.Max(0, Math.Min(_sourcePage + change, Math.Max(0, count - 1)));
            ShowSourcePage();
        }

        private void ChangeElementsPage(int change)
        {
            int count = LexicalAnalysisCore.PageCount(_elements.Count, GetElementsPerPage());
            _elementsPage = Math.Max(0, Math.Min(_elementsPage + change, Math.Max(0, count - 1)));
            ShowElementsPage();
        }

        private void ShowSourcePage()
        {
            List<int> pageStarts = BuildSourcePageStarts();
            int total = Math.Max(1, pageStarts.Count);
            _sourcePage = Math.Max(0, Math.Min(_sourcePage, total - 1));
            int start = pageStarts[_sourcePage];
            int end = _sourcePage + 1 < pageStarts.Count ? pageStarts[_sourcePage + 1] : _sourceLines.Length;
            StringBuilder visible = new StringBuilder();
            for (int i = start; i < end; i++)
            {
                if (i > start) visible.AppendLine();
                visible.Append(_sourceLines[i]);
            }
            _sourceView.Text = visible.ToString();

            _sourcePrevious.Enabled = _sourcePage > 0;
            _sourceNext.Enabled = _sourcePage + 1 < total;
        }

        private List<int> BuildSourcePageStarts()
        {
            List<int> starts = new List<int>();
            starts.Add(0);
            if (_sourceLines.Length == 0) return starts;

            int availableWidth = Math.Max(1, _sourceView.ClientSize.Width - 8);
            int availableHeight = Math.Max(1, _sourceView.ClientSize.Height - 4);
            int lineHeight = Math.Max(1, TextRenderer.MeasureText("Mg", _sourceView.Font).Height);
            int maximumVisualRows = Math.Max(1, availableHeight / lineHeight);
            int linesOnPage = 0;
            int visualRowsOnPage = 0;

            for (int i = 0; i < _sourceLines.Length; i++)
            {
                Size measured = TextRenderer.MeasureText(
                    _sourceLines[i],
                    _sourceView.Font,
                    new Size(availableWidth, Int32.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
                int visualRows = Math.Max(1, (measured.Height + lineHeight - 1) / lineHeight);

                if (linesOnPage > 0 &&
                    (linesOnPage >= SourceLinesPerPage || visualRowsOnPage + visualRows > maximumVisualRows))
                {
                    starts.Add(i);
                    linesOnPage = 0;
                    visualRowsOnPage = 0;
                }

                linesOnPage++;
                visualRowsOnPage += visualRows;
            }

            return starts;
        }

        private void ShowElementsPage()
        {
            int elementsPerPage = GetElementsPerPage();
            int total = Math.Max(1, LexicalAnalysisCore.PageCount(_elements.Count, elementsPerPage));
            _elementsPage = Math.Max(0, Math.Min(_elementsPage, total - 1));
            _elementsGrid.Rows.Clear();
            List<Lexeme> page = LexicalAnalysisCore.GetPage(_elements, _elementsPage, elementsPerPage);
            foreach (Lexeme item in page)
                _elementsGrid.Rows.Add(item.Number, item.Text, item.Type);

            _elementsPrevious.Enabled = _elementsPage > 0;
            _elementsNext.Enabled = _elementsPage + 1 < total;
        }

        private int GetElementsPerPage()
        {
            int availableHeight = _elementsGrid.ClientSize.Height - _elementsGrid.ColumnHeadersHeight - 6;
            int rowHeight = Math.Max(1, _elementsGrid.RowTemplate.Height);
            int rowsThatFit = Math.Max(1, availableHeight / rowHeight);
            return Math.Min(ElementsPerPage, rowsThatFit);
        }

        private void UpdateStatus()
        {
            if (String.IsNullOrEmpty(_filePath))
            {
                _statusLabel.Text = "Listo para abrir un .ens";
                return;
            }
            string instructionStatus = _assignedInstructions.Count == 0
                ? "Instrucciones del equipo pendientes"
                : String.Format("Instrucciones asignadas: {0}", _assignedInstructions.Count);
            int incorrect = 0;
            if (_semanticResult != null)
                foreach (LineAnalysisResult line in _semanticResult.Lines)
                    if (line.Status == "Incorrecta") incorrect++;
            _statusLabel.Text = String.Format("{0} elementos | {1} líneas incorrectas | {2}",
                _elements.Count, incorrect, instructionStatus);
        }

        private static List<string> LoadAssignedInstructions()
        {
            List<string> instructions = new List<string>();
            string path = Path.Combine(Application.StartupPath, "instrucciones_equipo2.txt");
            if (!File.Exists(path)) return instructions;

            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith(";", StringComparison.Ordinal) ||
                    trimmed.StartsWith("#", StringComparison.Ordinal)) continue;
                string[] values = trimmed.Split(new char[] { ' ', '\t', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string value in values) instructions.Add(value);
            }
            return instructions;
        }
    }
}
