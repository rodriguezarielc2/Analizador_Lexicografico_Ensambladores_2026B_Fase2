using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AnalizadorFase2
{
    internal sealed class SyntaxSemanticResultsForm : UserControl
    {
        private const int MaximumRowsPerPage = 50;
        private const int LinePagerHeight = 30;
        private const int SymbolHeadingHeight = 22;
        private const int SymbolPagerHeight = 30;

        private readonly DataGridView _lineGrid;
        private readonly DataGridView _symbolGrid;
        private readonly Button _linePrevious;
        private readonly Button _lineNext;
        private readonly Button _symbolPrevious;
        private readonly Button _symbolNext;
        private readonly SplitContainer _resultsSplit;
        private IList<LineAnalysisResult> _lines;
        private IList<SymbolEntry> _symbols;
        private int _linePage;
        private int _symbolPage;
        private bool _settingResultsSplitter;
        private bool _userMovedResultsSplitter;

        public SyntaxSemanticResultsForm()
        {
            _lines = new List<LineAnalysisResult>();
            _symbols = new List<SymbolEntry>();
            BackColor = Color.FromArgb(239, 243, 247);
            Padding = new Padding(8, 0, 8, 8);

            TableLayoutPanel layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 1;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowCount = 2;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Controls.Add(layout);

            Label heading = new Label();
            heading.Text = "3. Análisis sintáctico y semántico por línea";
            heading.Dock = DockStyle.Fill;
            heading.ForeColor = Color.FromArgb(23, 50, 77);
            heading.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            heading.TextAlign = ContentAlignment.MiddleLeft;
            layout.Controls.Add(heading, 0, 0);

            _resultsSplit = new SplitContainer();
            _resultsSplit.Dock = DockStyle.Fill;
            _resultsSplit.Orientation = Orientation.Horizontal;
            _resultsSplit.Size = new Size(800, 360);
            _resultsSplit.SplitterWidth = 6;
            _resultsSplit.Panel1MinSize = 118;
            _resultsSplit.Panel2MinSize = 138;
            _resultsSplit.SplitterDistance = 178;
            _resultsSplit.BackColor = Color.FromArgb(213, 222, 231);
            layout.Controls.Add(_resultsSplit, 0, 1);

            TableLayoutPanel lineLayout = new TableLayoutPanel();
            lineLayout.Dock = DockStyle.Fill;
            lineLayout.ColumnCount = 1;
            lineLayout.RowCount = 2;
            lineLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            lineLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            lineLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, LinePagerHeight));
            _lineGrid = CreateGrid();
            _lineGrid.Columns.Add("line", "Línea");
            _lineGrid.Columns.Add("source", "Programa fuente");
            _lineGrid.Columns.Add("status", "Resultado");
            _lineGrid.Columns.Add("message", "Causa / explicación");
            _lineGrid.Columns[0].FillWeight = 7F;
            _lineGrid.Columns[1].FillWeight = 34F;
            _lineGrid.Columns[2].FillWeight = 11F;
            _lineGrid.Columns[3].FillWeight = 48F;
            _lineGrid.Columns[1].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _lineGrid.Columns[3].DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _lineGrid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCells;
            _lineGrid.Columns[1].MinimumWidth = 180;
            _lineGrid.Columns[3].MinimumWidth = 240;
            lineLayout.Controls.Add(_lineGrid, 0, 0);
            lineLayout.Controls.Add(CreatePager(out _linePrevious, out _lineNext), 0, 1);
            _resultsSplit.Panel1.Controls.Add(lineLayout);

            TableLayoutPanel symbolLayout = new TableLayoutPanel();
            symbolLayout.Dock = DockStyle.Fill;
            symbolLayout.ColumnCount = 1;
            symbolLayout.RowCount = 3;
            symbolLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            symbolLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, SymbolHeadingHeight));
            symbolLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            symbolLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, SymbolPagerHeight));

            Label symbolHeading = new Label();
            symbolHeading.Text = "Tabla de símbolos";
            symbolHeading.Dock = DockStyle.Fill;
            symbolHeading.ForeColor = Color.FromArgb(23, 50, 77);
            symbolHeading.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            symbolHeading.TextAlign = ContentAlignment.MiddleLeft;
            symbolLayout.Controls.Add(symbolHeading, 0, 0);

            _symbolGrid = CreateGrid();
            _symbolGrid.Columns.Add("symbol", "Símbolo");
            _symbolGrid.Columns.Add("type", "Tipo");
            _symbolGrid.Columns.Add("value", "Valor");
            _symbolGrid.Columns.Add("size", "Tamaño");
            _symbolGrid.Columns[0].FillWeight = 18F;
            _symbolGrid.Columns[1].FillWeight = 28F;
            _symbolGrid.Columns[2].FillWeight = 34F;
            _symbolGrid.Columns[3].FillWeight = 20F;
            symbolLayout.Controls.Add(_symbolGrid, 0, 1);
            symbolLayout.Controls.Add(CreatePager(out _symbolPrevious, out _symbolNext), 0, 2);
            _resultsSplit.Panel2.Controls.Add(symbolLayout);

            _linePrevious.Click += delegate { ChangeLinePage(-1); };
            _lineNext.Click += delegate { ChangeLinePage(1); };
            _symbolPrevious.Click += delegate { ChangeSymbolPage(-1); };
            _symbolNext.Click += delegate { ChangeSymbolPage(1); };
            _lineGrid.Resize += delegate { RenderLinePage(); };
            _symbolGrid.Resize += delegate { RenderSymbolPage(); };
            _resultsSplit.SizeChanged += delegate { AdjustResultsSplitterDistance(); };
            _resultsSplit.SplitterMoved += delegate
            {
                if (!_settingResultsSplitter) _userMovedResultsSplitter = true;
            };
            RenderLinePage();
            RenderSymbolPage();
            UpdatePanelMinimums();
            AdjustResultsSplitterDistance();
        }

        public SyntaxSemanticResultsForm(ProgramAnalysisResult result) : this()
        {
            SetResult(result);
        }

        public void SetResult(ProgramAnalysisResult result)
        {
            _lines = result == null ? (IList<LineAnalysisResult>)new List<LineAnalysisResult>() : result.Lines;
            _symbols = result == null ? (IList<SymbolEntry>)new List<SymbolEntry>() : result.Symbols;
            _linePage = 0;
            _symbolPage = 0;
            RenderLinePage();
            RenderSymbolPage();
        }

        public void ApplyZoom(int zoomPercent)
        {
            float scale = zoomPercent / 100F;
            ApplyGridZoom(_lineGrid, 9F, scale, 28);
            ApplyGridZoom(_symbolGrid, 9F, scale, 26);
            UpdatePanelMinimums();
            RenderLinePage();
            RenderSymbolPage();
        }

        private void UpdatePanelMinimums()
        {
            _resultsSplit.Panel1MinSize = _lineGrid.MinimumSize.Height + LinePagerHeight;
            _resultsSplit.Panel2MinSize = _symbolGrid.MinimumSize.Height + SymbolHeadingHeight + SymbolPagerHeight;
            AdjustResultsSplitterDistance();
        }

        private void AdjustResultsSplitterDistance()
        {
            if (_userMovedResultsSplitter || _settingResultsSplitter || _resultsSplit.ClientSize.Height <= 0) return;
            int availableHeight = _resultsSplit.ClientSize.Height - _resultsSplit.SplitterWidth;
            int maximum = availableHeight - _resultsSplit.Panel2MinSize;
            if (maximum < _resultsSplit.Panel1MinSize) return;

            int desired = (int)Math.Round(availableHeight * 0.55F);
            desired = Math.Max(_resultsSplit.Panel1MinSize, Math.Min(maximum, desired));
            if (_resultsSplit.SplitterDistance == desired) return;

            _settingResultsSplitter = true;
            try { _resultsSplit.SplitterDistance = desired; }
            finally { _settingResultsSplitter = false; }
        }

        private void ChangeLinePage(int change)
        {
            List<int> starts = BuildLinePageStarts();
            _linePage = Math.Max(0, Math.Min(_linePage + change, starts.Count - 1));
            RenderLinePage();
        }

        private void ChangeSymbolPage(int change)
        {
            int pageSize = GetSymbolRowsPerPage();
            int totalPages = Math.Max(1, (_symbols.Count + pageSize - 1) / pageSize);
            _symbolPage = Math.Max(0, Math.Min(_symbolPage + change, totalPages - 1));
            RenderSymbolPage();
        }

        private void RenderLinePage()
        {
            if (_lineGrid == null) return;
            List<int> starts = BuildLinePageStarts();
            int totalPages = Math.Max(1, starts.Count);
            _linePage = Math.Max(0, Math.Min(_linePage, totalPages - 1));
            int start = starts[_linePage];
            int end = _linePage + 1 < starts.Count ? starts[_linePage + 1] : _lines.Count;

            _lineGrid.Rows.Clear();
            for (int i = start; i < end; i++)
            {
                LineAnalysisResult line = _lines[i];
                int rowIndex = _lineGrid.Rows.Add(line.LineNumber, line.Source, line.Status, line.Message);
                _lineGrid.Rows[rowIndex].DefaultCellStyle.ForeColor = line.Status == "Incorrecta"
                    ? Color.FromArgb(160, 35, 35) : Color.FromArgb(34, 83, 62);
            }
            if (_lineGrid.Rows.Count > 0)
                _lineGrid.AutoResizeRows(DataGridViewAutoSizeRowsMode.DisplayedCells);

            _linePrevious.Enabled = _linePage > 0;
            _lineNext.Enabled = _linePage + 1 < totalPages;
        }

        private void RenderSymbolPage()
        {
            if (_symbolGrid == null) return;
            int pageSize = GetSymbolRowsPerPage();
            int totalPages = Math.Max(1, (_symbols.Count + pageSize - 1) / pageSize);
            _symbolPage = Math.Max(0, Math.Min(_symbolPage, totalPages - 1));
            int start = _symbolPage * pageSize;
            int end = Math.Min(start + pageSize, _symbols.Count);

            _symbolGrid.Rows.Clear();
            for (int i = start; i < end; i++)
            {
                SymbolEntry symbol = _symbols[i];
                _symbolGrid.Rows.Add(symbol.Symbol, symbol.Type, symbol.Value, symbol.Size);
            }

            _symbolPrevious.Enabled = _symbolPage > 0;
            _symbolNext.Enabled = _symbolPage + 1 < totalPages;
        }

        private List<int> BuildLinePageStarts()
        {
            List<int> starts = new List<int>();
            starts.Add(0);
            int line = 0;
            while (line < _lines.Count)
            {
                int capacity = GetLineRowsPerPage(line);
                line = Math.Min(_lines.Count, line + capacity);
                if (line < _lines.Count) starts.Add(line);
            }
            return starts;
        }

        private int GetLineRowsPerPage(int start)
        {
            int count = Math.Min(MaximumRowsPerPage, Math.Max(1, _lines.Count - start));
            if (_lineGrid.Columns.Count < 4 || _lineGrid.Columns[1].Width <= 0 || _lineGrid.Columns[3].Width <= 0)
                return count;

            int availableHeight = GetLineRowsViewportHeight();
            int usedHeight = 0;
            int fitted = 0;
            DataGridViewCellStyle sourceStyle = _lineGrid.Columns[1].InheritedStyle;
            DataGridViewCellStyle messageStyle = _lineGrid.Columns[3].InheritedStyle;
            Font sourceFont = sourceStyle.Font ?? _lineGrid.DefaultCellStyle.Font;
            Font messageFont = messageStyle.Font ?? _lineGrid.DefaultCellStyle.Font;
            int sourceWidth = Math.Max(1, _lineGrid.Columns[1].Width - sourceStyle.Padding.Horizontal - 4);
            int messageWidth = Math.Max(1, _lineGrid.Columns[3].Width - messageStyle.Padding.Horizontal - 4);
            for (int i = start; i < _lines.Count && fitted < count; i++)
            {
                int sourceHeight = MeasureWrappedHeight(_lines[i].Source, sourceFont, sourceWidth);
                int messageHeight = MeasureWrappedHeight(_lines[i].Message, messageFont, messageWidth);
                int verticalPadding = Math.Max(sourceStyle.Padding.Vertical, messageStyle.Padding.Vertical);
                int contentHeight = Math.Max(Math.Max(sourceHeight, messageHeight), Math.Max(sourceFont.Height, messageFont.Height));
                int rowHeight = contentHeight + verticalPadding + 4;
                if (fitted > 0 && usedHeight + rowHeight > availableHeight) break;
                usedHeight += rowHeight;
                fitted++;
            }
            return Math.Max(1, fitted);
        }

        private int GetLineRowsViewportHeight()
        {
            int height = _lineGrid.ClientSize.Height - _lineGrid.ColumnHeadersHeight - 6;
            foreach (Control child in _lineGrid.Controls)
            {
                HScrollBar horizontalBar = child as HScrollBar;
                if (horizontalBar != null && horizontalBar.Visible)
                {
                    height -= horizontalBar.Height;
                    break;
                }
            }
            return Math.Max(1, height);
        }

        private int GetSymbolRowsPerPage()
        {
            return Math.Min(MaximumRowsPerPage, GetRowsThatFit(_symbolGrid));
        }

        private static int GetRowsThatFit(DataGridView grid)
        {
            int availableHeight = grid.ClientSize.Height - grid.ColumnHeadersHeight - 6;
            int rowHeight = Math.Max(1, grid.RowTemplate.Height);
            return Math.Max(1, availableHeight / rowHeight);
        }

        private static int MeasureWrappedHeight(string text, Font font, int width)
        {
            if (String.IsNullOrEmpty(text)) text = " ";
            Size measured = TextRenderer.MeasureText(text, font, new Size(width, Int32.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding);
            return Math.Max(1, measured.Height);
        }

        private static Panel CreatePager(out Button previous, out Button next)
        {
            TableLayoutPanel pager = new TableLayoutPanel();
            pager.Dock = DockStyle.Fill;
            pager.Margin = new Padding(0);
            pager.ColumnCount = 2;
            pager.RowCount = 1;
            pager.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            pager.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            previous = new Button();
            previous.Text = "\u2039 Anterior";
            previous.Width = 104;
            previous.Height = 28;
            previous.Anchor = AnchorStyles.Left;
            previous.Margin = new Padding(0, 1, 0, 1);
            next = new Button();
            next.Text = "Siguiente \u203a";
            next.Width = 104;
            next.Height = 28;
            next.Anchor = AnchorStyles.Right;
            next.Margin = new Padding(0, 1, 0, 1);

            pager.Controls.Add(previous, 0, 0);
            pager.Controls.Add(next, 1, 0);
            return pager;
        }

        private static DataGridView CreateGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.MinimumSize = new Size(0, 60);
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ScrollBars = ScrollBars.Both;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.FixedSingle;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(220, 230, 239);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(23, 50, 77);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            grid.DefaultCellStyle.Font = new Font("Consolas", 9F, FontStyle.Regular);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(199, 231, 226);
            grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(16, 42, 67);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 250, 252);
            return grid;
        }

        private static void ApplyGridZoom(DataGridView grid, float baseFontSize, float scale, int baseRowHeight)
        {
            grid.DefaultCellStyle.Font = new Font("Consolas", baseFontSize * scale, FontStyle.Regular, GraphicsUnit.Point);
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F * scale, FontStyle.Bold, GraphicsUnit.Point);
            grid.ColumnHeadersHeight = Math.Max(26, (int)Math.Ceiling(26F * scale));
            int rowHeight = Math.Max(22, (int)Math.Ceiling(baseRowHeight * scale));
            grid.RowTemplate.Height = rowHeight;
            grid.MinimumSize = new Size(0, grid.ColumnHeadersHeight + rowHeight + 6);
            foreach (DataGridViewRow row in grid.Rows)
                row.Height = rowHeight;
        }
    }
}
