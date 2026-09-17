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
        private ResultadosPainel _painel;
        private Label _lblTotais;
        private List<MedItem> _lineares = new List<MedItem>();

        public LinearControl()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            AutoScaleMode = AutoScaleMode.Dpi;

            var cabecalho = new Label
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaCabecalho,
                Text = "LINEARES\r\nComprimentos por categoria",
                Font = PaletteTheme.TituloSeccao,
                ForeColor = PaletteTheme.Tinta,
                BackColor = PaletteTheme.AzulTopo,
                Padding = new Padding(10, 5, 8, 3),
                TextAlign = ContentAlignment.MiddleLeft
            };

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

            _painel = new ResultadosPainel("LINEARES")
            {
                // Uma medição linear não se configura por artigo: não há
                // «próxima medição» para fixar, e um botão que nunca faz nada
                // é pior do que não existir.
                PermiteMedirAqui = false
            };

            _lblTotais = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(8, 0, 0, 0)
            };

            Controls.Add(_painel);
            Controls.Add(_lblTotais);
            Controls.Add(tools);
            Controls.Add(ajuda);
            Controls.Add(cabecalho);
            var dicas = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100 };
            BackColor = PaletteTheme.Fundo;
            ForeColor = PaletteTheme.Tinta;
            PaletteTheme.AplicarTema(this);
            PaletteTheme.PrepararInteraccao(this, dicas);
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
                ToolTipText = text,
                AccessibleName = text
            };
        }

        public void BindData(List<MedItem> lineares)
        {
            // A ordem que a árvore recebe é a ordem por que os grupos nascem.
            _lineares = (lineares ?? new List<MedItem>())
                .OrderBy(m => m.Categoria ?? "")
                .ThenBy(m => m.Layer ?? "")
                .ThenBy(m => m.Handle ?? "")
                .ToList();

            try
            {
                var raiz = ResultadosArvore.Construir(
                    ResultadosAdaptadores.DeLineares(_lineares, CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture);
                _painel.Vincular(raiz, PaletteHost.MedicaoNova);
            }
            catch (Exception ex)
            {
                PaletteHost.Log("Lineares: " + ex.Message);
            }

            var porCategoria = _lineares
                .GroupBy(m => m.Categoria ?? "GERAL")
                .Select(g => g.Key + ": " + N2(g.Sum(m => m.Comprimento)) + " m");
            _lblTotais.Text = string.Format("Total: {0} m   |   {1} medição(ões){2}",
                N2(_lineares.Sum(m => m.Comprimento)),
                _lineares.Count,
                _lineares.Count == 0 ? "" : "   |   " + string.Join("   |   ", porCategoria));
        }

        private static string N2(double valor)
        {
            return valor.ToString("N2", CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// `Ctrl+F` leva o foco à pesquisa dos resultados, esteja o foco onde
        /// estiver dentro da aba. No ProcessCmdKey e não num KeyDown porque um
        /// atalho que só funciona com o foco no sítio certo não é um atalho.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F) && _painel != null)
            {
                _painel.FocarPesquisa();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
