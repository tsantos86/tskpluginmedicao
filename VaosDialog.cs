using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Lançamento sequencial dos vãos de uma medição, como na folha de medição:
    /// uma linha por vão (VE.01, VE.02, PC.04…), com largura, altura, quantidade,
    /// tipo e pré-aro. Os vãos detectados no desenho entram já preenchidos;
    /// o utilizador acrescenta ou corrige as linhas que quiser.
    /// </summary>
    public class VaosDialog : Form
    {
        private readonly DataGridView _dgv;
        private readonly double _espessuraParede;

        private const string ColUsar = "usar";
        private const string ColDes = "des";
        private const string ColLarg = "larg";
        private const string ColAlt = "alt";
        private const string ColQtd = "qtd";
        private const string ColTipo = "tipo";
        private const string ColAro = "prearo";
        private const string ColEsp = "esp";
        private const string ColOrig = "orig";

        public VaosDialog(string titulo, List<VaoCandidato> candidatos, double espessuraParede)
        {
            _espessuraParede = espessuraParede;

            Text = titulo;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(880, 400);
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = true,      // linha em branco no fim para escrever
                AllowUserToDeleteRows = true,
                RowHeadersVisible = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = Color.White,
                EditMode = DataGridViewEditMode.EditOnEnter
            };

            _dgv.Columns.Add(new DataGridViewCheckBoxColumn
            { Name = ColUsar, HeaderText = "Descontar", FillWeight = 55 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = ColDes, HeaderText = "Designação", FillWeight = 70 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = ColLarg, HeaderText = "Largura (m)", FillWeight = 60 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = ColAlt, HeaderText = "Altura (m)", FillWeight = 60 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = ColQtd, HeaderText = "Qtd", FillWeight = 35 });

            var colTipo = new DataGridViewComboBoxColumn
            { Name = ColTipo, HeaderText = "Tipo", FillWeight = 55 };
            colTipo.Items.Add("Porta");
            colTipo.Items.Add("Janela");
            _dgv.Columns.Add(colTipo);

            _dgv.Columns.Add(new DataGridViewCheckBoxColumn
            { Name = ColAro, HeaderText = "Pré-aro", FillWeight = 45 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = ColEsp, HeaderText = "Esp. aro (m)", FillWeight = 60 });
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            { Name = ColOrig, HeaderText = "Texto no desenho", ReadOnly = true, FillWeight = 100 });

            if (candidatos != null)
            {
                foreach (var c in candidatos)
                {
                    _dgv.Rows.Add(
                        c.Selecionado,
                        c.Designacao,
                        N2(c.LarguraM),
                        N2(c.AlturaM),
                        1,
                        c.TipoSugerido == TipoVao.Janela ? "Janela" : "Porta",
                        false,
                        N2(espessuraParede),
                        c.TextoOriginal);
                }
            }

            // Ao marcar pré-aro, preenche a espessura da parede automaticamente
            _dgv.CellValueChanged += OnCellValueChanged;
            _dgv.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_dgv.IsCurrentCellDirty)
                    _dgv.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _dgv.DataError += (s, e) => { e.ThrowException = false; };
            _dgv.DefaultValuesNeeded += (s, e) =>
            {
                e.Row.Cells[ColUsar].Value = true;
                e.Row.Cells[ColQtd].Value = 1;
                e.Row.Cells[ColTipo].Value = "Porta";
                e.Row.Cells[ColAro].Value = false;
                e.Row.Cells[ColEsp].Value = N2(_espessuraParede);
            };

            var info = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(8, 6, 0, 0),
                Text = "Escreva um vão por linha (a última linha em branco serve para acrescentar). " +
                       "Marque Pré-aro para contabilizar aros — a espessura da parede é usada por omissão."
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
                Height = 44,
                Padding = new Padding(6)
            };
            var btnOk = new Button
            { Text = "Gravar vãos", DialogResult = DialogResult.OK, AutoSize = true };
            var btnCancel = new Button
            { Text = "Sem vãos", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(_dgv);
            Controls.Add(buttons);
            Controls.Add(info);
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
            if (_dgv.Columns[e.ColumnIndex].Name != ColAro) return;

            var row = _dgv.Rows[e.RowIndex];
            if (row.IsNewRow) return;

            bool marcado = row.Cells[ColAro].Value is bool b && b;
            if (marcado)
            {
                var atual = row.Cells[ColEsp].Value;
                if (atual == null || LerNum(atual, 0) <= 0)
                    row.Cells[ColEsp].Value = N2(_espessuraParede);
            }
        }

        /// <summary>Lê a tabela e devolve os vãos marcados.</summary>
        private List<Vao> Recolher()
        {
            var lista = new List<Vao>();
            foreach (DataGridViewRow row in _dgv.Rows)
            {
                if (row.IsNewRow) continue;
                if (!(row.Cells[ColUsar].Value is bool usar) || !usar) continue;

                double larg = LerNum(row.Cells[ColLarg].Value, 0);
                double alt = LerNum(row.Cells[ColAlt].Value, 0);
                if (larg <= 0 || alt <= 0) continue;   // linha incompleta: ignora

                int qtd = (int)Math.Max(1, Math.Round(LerNum(row.Cells[ColQtd].Value, 1)));
                bool aro = row.Cells[ColAro].Value is bool a && a;

                lista.Add(new Vao
                {
                    Designacao = (row.Cells[ColDes].Value ?? "").ToString().Trim(),
                    Largura = larg,
                    Altura = alt,
                    Quantidade = qtd,
                    Tipo = (row.Cells[ColTipo].Value ?? "").ToString() == "Janela"
                        ? TipoVao.Janela : TipoVao.Porta,
                    PreAro = aro,
                    Espessura = aro ? LerNum(row.Cells[ColEsp].Value, _espessuraParede) : 0.0
                });
            }
            return lista;
        }

        /// <summary>
        /// Abre a tabela de vãos. Devolve a lista gravada (vazia se cancelar).
        /// </summary>
        public static List<Vao> Mostrar(string titulo, List<VaoCandidato> candidatos,
            double espessuraParede)
        {
            var vazio = new List<Vao>();
            try
            {
                using (var dlg = new VaosDialog(titulo, candidatos, espessuraParede))
                {
                    if (AcadApp.ShowModalDialog(dlg) != DialogResult.OK) return vazio;
                    return dlg.Recolher();
                }
            }
            catch (Exception ex)
            {
                PaletteHost.Log("Erro na tabela de vãos: " + ex.Message);
                return vazio;
            }
        }

        private static string N2(double v) => v.ToString("N2", CultureInfo.CurrentCulture);

        private static double LerNum(object valor, double fallback)
        {
            if (valor == null) return fallback;
            string s = valor.ToString().Trim().Replace(',', '.');
            if (s.Length == 0) return fallback;
            return double.TryParse(s, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }
    }
}
