using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// Aba "Contagens": conta blocos por unidade — portas, janelas, tomadas,
    /// luminárias, louças, equipamentos.
    ///
    /// O fluxo é o do dia-a-dia: dá-se um nome, selecciona-se (à mão se forem
    /// duas, por QSELECT se forem cinquenta) e carrega-se no botão. Cada bloco
    /// contado ganha um círculo no centro, para se ver no desenho o que já foi
    /// feito e o que falta.
    /// </summary>
    public class ContagemControl : UserControl
    {
        private ComboBox _cmbNome;
        private ComboBox _cmbPiso;
        private TextBox _txtCategoria;
        private NumericUpDown _numRaio;
        private CheckBox _chkTexto;
        private DataGridView _dgv;
        private Label _lblTotais;

        private List<MedContagem> _contagens = new List<MedContagem>();

        public ContagemControl()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;

            var config = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(6)
            };
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            ContagemConfig.Carregar();

            _cmbNome = new ComboBox { Dock = DockStyle.Fill };
            if (ContagemConfig.Nomes.Count == 0)
                ContagemConfig.Nomes.AddRange(new[]
                    { "P.01", "J.01", "TOMADA", "LUMINARIA", "LOUÇA" });
            _cmbNome.Items.AddRange(ContagemConfig.Nomes.ToArray());
            _cmbNome.Text = ContagemConfig.Nome;

            _cmbPiso = new ComboBox { Dock = DockStyle.Fill };
            foreach (var piso in FachadaConfig.CoresPiso.Keys) _cmbPiso.Items.Add(piso);
            _cmbPiso.Text = ContagemConfig.Piso;

            _txtCategoria = new TextBox { Dock = DockStyle.Fill, Text = ContagemConfig.Categoria };

            _numRaio = new NumericUpDown
            {
                DecimalPlaces = 2,
                Increment = 0.05M,
                Minimum = 0.01M,
                Maximum = 100M,
                Value = (decimal)ContagemConfig.Raio,
                Dock = DockStyle.Fill
            };

            _chkTexto = new CheckBox
            {
                Text = "Escrever o nome ao lado do círculo",
                Checked = ContagemConfig.ComTexto,
                Dock = DockStyle.Fill,
                AutoSize = true
            };

            config.Controls.Add(Lbl("Nome:"), 0, 0);
            config.Controls.Add(_cmbNome, 1, 0);
            config.Controls.Add(Lbl("Artigo / grupo:"), 0, 1);
            config.Controls.Add(_txtCategoria, 1, 1);
            config.Controls.Add(Lbl("Piso:"), 0, 2);
            config.Controls.Add(_cmbPiso, 1, 2);
            config.Controls.Add(Lbl("Raio do círculo:"), 0, 3);
            config.Controls.Add(_numRaio, 1, 3);
            config.Controls.Add(new Label(), 0, 4);
            config.Controls.Add(_chkTexto, 1, 4);

            var ajuda = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(8, 2, 8, 2),
                Text = "Poucos: seleccione no desenho e carregue em Contar. " +
                       "Muitos: use o QSELECT do AutoCAD e depois Contar."
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
            tools.Items.Add(MakeButton("Contar", IconFactory.Contagem(), (s, e) => Contar()));
            tools.Items.Add(MakeButton("QSELECT", IconFactory.Qselect(),
                (s, e) => PaletteHost.RunCommand("_QSELECT ")));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(MakeButton("Atualizar", IconFactory.Atualizar(),
                (s, e) => PaletteHost.RefreshData()));
            tools.Items.Add(MakeButton("Apagar nome", IconFactory.Remover(),
                (s, e) => ApagarNome()));
            tools.Items.Add(MakeButton("Limpar tudo", IconFactory.Limpar(),
                (s, e) => LimparTudo()));

            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                RowHeadersVisible = false,
                // DisplayedCells, não AllCells. Com AllCells, cada linha
                // acrescentada faz o DataGridView remedir todas as colunas
                // contra TODAS as linhas já postas — é quadrático, e é por
                // isso que a paleta ia ficando pesada à medida que se media.
                // DisplayedCells mede só o que está visível: o custo passa a
                // depender do tamanho da janela, não do tamanho da obra.
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                BackgroundColor = Color.White
            };
            AddCol("nome", "Nome");
            AddCol("cat", "Artigo / grupo");
            AddCol("piso", "Piso");
            AddCol("qt", "Quantidade");

            _lblTotais = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(6, 0, 0, 0)
            };

            Controls.Add(_dgv);
            Controls.Add(_lblTotais);
            Controls.Add(tools);
            Controls.Add(ajuda);
            Controls.Add(config);
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

        private static Label Lbl(string t) =>
            new Label { Text = t, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };

        private void AddCol(string name, string header)
        {
            _dgv.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        // ------------------------------------------------------------------
        private void SyncConfig()
        {
            ContagemConfig.Nome = (_cmbNome.Text ?? "").Trim();
            ContagemConfig.Categoria = (_txtCategoria.Text ?? "").Trim();
            // Mesmo comportamento da aba Alvenaria (Palette.SyncConfig): vazio
            // = sem piso (não inventar "PISO 0"), e maiúsculas normalizadas
            // para "piso 0" digitado agrupar com "PISO 0".
            ContagemConfig.Piso = (_cmbPiso.Text ?? "").Trim().ToUpperInvariant();
            ContagemConfig.Raio = (double)_numRaio.Value;
            ContagemConfig.ComTexto = _chkTexto.Checked;
            ContagemConfig.Guardar();
        }

        private void Contar()
        {
            SyncConfig();
            if (ContagemConfig.Nome.Length == 0)
            {
                MessageBox.Show("Escreva primeiro o nome (ex.: P.01, TOMADA).",
                    "TSK TakeOff", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            PaletteHost.RunCommand("TSKCONTAR ");
        }

        private void ApagarNome()
        {
            SyncConfig();
            string nome = ContagemConfig.Nome;
            if (nome.Length == 0) return;

            if (MessageBox.Show(
                    "Apagar todas as contagens de \"" + nome + "\"?",
                    "TSK TakeOff", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

            var doc = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            int n = ContRepo.Limpar(doc.Database, nome);
            PaletteHost.Log(n + " contagem(ns) de \"" + nome + "\" apagada(s).");
            PaletteHost.RefreshData();
        }

        private void LimparTudo()
        {
            if (_contagens.Count == 0) return;
            if (MessageBox.Show(
                    "Apagar TODAS as contagens do desenho (" + _contagens.Count + ")?",
                    "TSK TakeOff", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes) return;

            var doc = Autodesk.AutoCAD.ApplicationServices.Application
                .DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            int n = ContRepo.Limpar(doc.Database, "");
            PaletteHost.Log(n + " contagem(ns) apagada(s).");
            PaletteHost.RefreshData();
        }

        // ------------------------------------------------------------------
        public void BindData(List<MedContagem> contagens)
        {
            _contagens = contagens ?? new List<MedContagem>();
            _dgv.Rows.Clear();

            foreach (var g in _contagens
                .GroupBy(c => new
                {
                    Nome = c.Nome ?? "",
                    Cat = c.Categoria ?? "",
                    Piso = c.Piso ?? ""
                })
                .OrderBy(g => g.Key.Cat).ThenBy(g => g.Key.Piso).ThenBy(g => g.Key.Nome))
            {
                _dgv.Rows.Add(g.Key.Nome, g.Key.Cat, g.Key.Piso, g.Count());
            }

            _lblTotais.Text = string.Format("Contagens: {0}  |  Tipos: {1}",
                _contagens.Count,
                _contagens.Select(c => c.Nome).Distinct().Count());

            // Nomes novos entram na lista sem apagar o que a pessoa escreveu.
            foreach (var nome in _contagens.Select(c => c.Nome).Distinct())
                if (!string.IsNullOrEmpty(nome) && !_cmbNome.Items.Contains(nome))
                    _cmbNome.Items.Add(nome);
        }
    }
}
