using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// Aba "Lineares": apresenta no painel as medições criadas por
    /// MEDIR/TSKLINEAR. A leitura destas entidades já alimentava o Excel; esta
    /// grelha torna a mesma informação visível no AutoCAD.
    /// </summary>
    public class LinearControl : UserControl
    {
        private DataGridView _dgv;
        private Label _lblTotais;
        private List<MedItem> _lineares = new List<MedItem>();
        private bool _carregando;

        public LinearControl()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;

            var ajuda = new Label
            {
                Dock = DockStyle.Top,
                Height = 38,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(8, 5, 8, 3),
                Text = "Comprimentos medidos por categoria com o comando Medição Linear."
            };

            var tools = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(24, 24),
                Padding = new Padding(6, 4, 6, 4),
                ShowItemToolTips = true,
                RenderMode = ToolStripRenderMode.System
            };
            tools.Items.Add(MakeButton("Medir linear", IconFactory.Linear(),
                (s, e) => PaletteHost.RunCommand("TSKLINEAR ")));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(MakeButton("Exportar XLSX", IconFactory.Exportar(),
                (s, e) => PaletteHost.RunCommand("TSKEXPORT ")));
            tools.Items.Add(MakeButton("Atualizar", IconFactory.Atualizar(),
                (s, e) => PaletteHost.RefreshData()));

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };
            AddCol("num", "Nº", 44);
            AddCol("categoria", "Categoria", 130);
            AddCol("layer", "Layer", 180);
            AddCol("comp", "Comprimento (m)", 110);
            AddCol("vertices", "Vértices", 72);

            _dgv.SelectionChanged += (s, e) =>
            {
                if (!_carregando) PaletteHost.MarcarSeleccaoGrelha();
            };

            _lblTotais = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(8, 0, 0, 0)
            };

            Controls.Add(_dgv);
            Controls.Add(_lblTotais);
            Controls.Add(tools);
            Controls.Add(ajuda);
        }

        private static ToolStripButton MakeButton(string text, Image icon, EventHandler onClick)
        {
            return new ToolStripButton(text, icon, onClick)
            {
                TextImageRelation = TextImageRelation.ImageAboveText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                AutoSize = true,
                Padding = new Padding(2, 1, 2, 1),
                Margin = new Padding(1, 0, 1, 0),
                ToolTipText = text
            };
        }

        private void AddCol(string name, string header, float width)
        {
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = width,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        public void BindData(List<MedItem> lineares)
        {
            _lineares = (lineares ?? new List<MedItem>())
                .OrderBy(m => m.Categoria ?? "")
                .ThenBy(m => m.Layer ?? "")
                .ThenBy(m => m.Handle ?? "")
                .ToList();

            string anterior = HandleSelecionado();
            string nova = PaletteHost.MedicaoNova;
            bool seguirNova = nova != null && _lineares.Exists(m => m.Handle == nova);

            _carregando = true;
            try
            {
                _dgv.SuspendLayout();
                _dgv.Rows.Clear();

                int n = 1;
                foreach (var med in _lineares)
                {
                    int idx = _dgv.Rows.Add(
                        n++,
                        med.Categoria ?? "",
                        med.Layer ?? "",
                        N2(med.Comprimento),
                        med.Vertices);
                    _dgv.Rows[idx].Tag = med.Handle;
                }

                if (!(seguirNova && Focar(nova))) Focar(anterior);
            }
            finally
            {
                _dgv.ResumeLayout();
                _carregando = false;
            }

            var porCategoria = _lineares
                .GroupBy(m => m.Categoria ?? "GERAL")
                .Select(g => g.Key + ": " + N2(g.Sum(m => m.Comprimento)) + " m");
            _lblTotais.Text = string.Format("Total: {0} m   |   {1} medição(ões){2}",
                N2(_lineares.Sum(m => m.Comprimento)),
                _lineares.Count,
                _lineares.Count == 0 ? "" : "   |   " + string.Join("   |   ", porCategoria));
        }

        private string HandleSelecionado()
        {
            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            return row?.Tag as string;
        }

        private bool Focar(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return false;
            foreach (DataGridViewRow row in _dgv.Rows)
            {
                if ((row.Tag as string) != handle) continue;
                _dgv.CurrentCell = row.Cells[0];
                return true;
            }
            return false;
        }

        private static string N2(double valor)
        {
            return valor.ToString("N2", CultureInfo.CurrentCulture);
        }
    }
}
