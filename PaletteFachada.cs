using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>Aba "Materiais": material, piso com cor, retângulo/polyline e grade ao vivo.</summary>
    public class FachadaControl : UserControl
    {
        private ComboBox _cmbMaterial;
        private ComboBox _cmbAlcado;
        private ComboBox _cmbPiso;
        private Button _btnCor;
        private NumericUpDown _numAlturaPiso;
        /// <summary>Texto que sai na linha de título criada pelos botões Capítulo / Artigo.</summary>
        private TextBox _txtTitulo;
        private DataGridView _dgv;
        /// <summary>Última lista carregada na grelha, para consultar sem reler o DWG.</summary>
        private List<MedFachada> _meds = new List<MedFachada>();
        private Label _lblTotais;

        public FachadaControl()
        {
            BuildUi();
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;

            // ----- Configurações -----
            var config = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 3,
                Padding = new Padding(6)
            };
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));

            _cmbMaterial = new ComboBox { Dock = DockStyle.Fill };
            FachadaConfig.Carregar();
            if (FachadaConfig.Materiais.Count == 0)
                FachadaConfig.Materiais.AddRange(new[]
                    { "ETICS", "REBOCO", "PINTURA", "IMPERMEABILIZACAO" });
            _cmbMaterial.Items.AddRange(FachadaConfig.Materiais.ToArray());
            _cmbMaterial.Text = FachadaConfig.Material;

            _cmbPiso = new ComboBox { Dock = DockStyle.Fill };
            foreach (var piso in FachadaConfig.CoresPiso.Keys)
                _cmbPiso.Items.Add(piso);
            _cmbPiso.Text = FachadaConfig.Piso;
            _cmbPiso.TextChanged += (s, e) => UpdateCorSwatch();

            _btnCor = new Button { Text = "Cor…", Dock = DockStyle.Fill };
            _btnCor.Click += (s, e) => EscolherCor();

            _cmbAlcado = new ComboBox { Dock = DockStyle.Fill };
            foreach (var a in FachadaConfig.Alcados) _cmbAlcado.Items.Add(a);
            _cmbAlcado.Text = FachadaConfig.Alcado;

            _numAlturaPiso = new NumericUpDown
            {
                DecimalPlaces = 2,
                Increment = 0.05M,
                Minimum = 0.1M,
                Maximum = 30M,
                Value = (decimal)FachadaConfig.AlturaPiso,
                Dock = DockStyle.Fill
            };

            // O mesmo campo da aba Alvenaria, e com o mesmo contrato: mostra o
            // que vai ser escrito na linha de título. Aqui não há lista de
            // artigos própria — o artigo corrente é o do painel, partilhado —
            // por isso abre com ele e escreve-se por cima quando é preciso um
            // título que o mapa não tem.
            _txtTitulo = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = PaletteHost.TextoDoArtigoCorrente()
            };

            config.Controls.Add(Lbl("Texto do título:"), 0, 0);
            config.Controls.Add(_txtTitulo, 1, 0);
            config.Controls.Add(new Label(), 2, 0);
            config.Controls.Add(Lbl("Material:"), 0, 1);
            config.Controls.Add(_cmbMaterial, 1, 1);
            config.Controls.Add(new Label(), 2, 1);
            config.Controls.Add(Lbl("Piso:"), 0, 2);
            config.Controls.Add(_cmbPiso, 1, 2);
            config.Controls.Add(_btnCor, 2, 2);
            config.Controls.Add(Lbl("Alçado / zona:"), 0, 3);
            config.Controls.Add(_cmbAlcado, 1, 3);
            config.Controls.Add(new Label(), 2, 3);
            config.Controls.Add(Lbl("Altura piso (m):"), 0, 4);
            config.Controls.Add(_numAlturaPiso, 1, 4);
            config.Controls.Add(new Label(), 2, 4);

            // ----- Botões -----
            var tools = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(24, 24),
                Padding = new Padding(6, 4, 6, 4),
                ShowItemToolTips = true,
                RenderMode = ToolStripRenderMode.System
            };
            tools.Items.Add(MakeButton("Retângulo", IconFactory.Retangulo(), (s, e) => Medir("TSKRET ")));
            tools.Items.Add(MakeButton("Polyline × Alt.", IconFactory.Polf(), (s, e) => Medir("TSKPOLF ")));
            tools.Items.Add(MakeButton("Medir Seleção", IconFactory.MedirSel(), (s, e) => Medir("TSKMEDSEL ")));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(MakeButton("Exportar XLSX", IconFactory.Exportar(),
                (s, e) => PaletteHost.RunCommand("TSKEXPORT ")));
            tools.Items.Add(MakeButton("Atualizar", IconFactory.Atualizar(),
                (s, e) => PaletteHost.RefreshData()));
            tools.Items.Add(MakeButton("Remover", IconFactory.Remover(), (s, e) => Remover()));
            tools.Items.Add(MakeButton("Linha branca", IconFactory.LinhaBranca(),
                (s, e) => AlternarSeparador()));
            tools.Items.Add(MakeButton("Capítulo", IconFactory.Capitulo(),
                (s, e) => MarcarTitulo("CAP", "Capítulo")));
            tools.Items.Add(MakeButton("Artigo", IconFactory.Artigo(),
                (s, e) => MarcarTitulo("ART", "Artigo")));
            tools.Items.Add(MakeButton("Limpar tudo", IconFactory.Limpar(), (s, e) => LimparTudo()));

            // ----- Grade -----
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
                BackgroundColor = System.Drawing.Color.White
            };
            // Nº e Artigo à cabeça, como na grelha da Alvenaria: o Nº é a
            // linha da folha e o Artigo é o que diz onde a medição vai parar
            // no mapa. Sem eles, medir um pano com um artigo escolhido não
            // dava sinal nenhum na paleta — só no Excel.
            AddCol("num", "Nº");
            AddCol("sep", "⏎");
            AddCol("artigo", "Artigo");
            AddCol("mat", "Material");
            AddCol("alcado", "Alçado");
            AddCol("piso", "Piso");
            AddCol("tipo", "Tipo");
            AddCol("comp", "Comp. (m)");
            AddCol("alt", "Alt. (m)");
            AddCol("area", "Área (m²)");

            // A partir do primeiro clique numa linha, é a grelha que manda no
            // destino dos títulos. Os eventos disparados pelo BindData não
            // contam — quem os provoca é a reconstrução, não o utilizador.
            _dgv.SelectionChanged += (s, e) => { if (!_carregando) _escolheu = true; };

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
            Controls.Add(config);

            UpdateCorSwatch();
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
            FachadaConfig.Material = string.IsNullOrWhiteSpace(_cmbMaterial.Text)
                ? "ETICS"
                : _cmbMaterial.Text.Trim().ToUpperInvariant().Replace(" ", "_");
            FachadaConfig.Piso = string.IsNullOrWhiteSpace(_cmbPiso.Text)
                ? "PISO 0"
                : _cmbPiso.Text.Trim().ToUpperInvariant();
            FachadaConfig.AlturaPiso = (double)_numAlturaPiso.Value;
            FachadaConfig.Alcado = (_cmbAlcado.Text ?? "").Trim();
            FachadaConfig.RegistarAlcado(FachadaConfig.Alcado);
            if (!string.IsNullOrEmpty(FachadaConfig.Alcado) &&
                !_cmbAlcado.Items.Contains(FachadaConfig.Alcado))
                _cmbAlcado.Items.Add(FachadaConfig.Alcado);

            if (!FachadaConfig.CoresPiso.ContainsKey(FachadaConfig.Piso))
                FachadaConfig.CoresPiso[FachadaConfig.Piso] =
                    FachadaConfig.CorDoPiso(FachadaConfig.Piso);
            if (!_cmbPiso.Items.Contains(FachadaConfig.Piso))
                _cmbPiso.Items.Add(FachadaConfig.Piso);

            // Material novo fica guardado para as próximas sessões
            FachadaConfig.RegistarMaterial(FachadaConfig.Material);
            if (!_cmbMaterial.Items.Contains(FachadaConfig.Material))
                _cmbMaterial.Items.Add(FachadaConfig.Material);
        }

        private void Medir(string comando)
        {
            SyncConfig();
            PaletteHost.RunCommand(comando);
        }

        private void EscolherCor()
        {
            SyncConfig();
            using (var dlg = new ColorDialog
            {
                Color = FachadaConfig.CorDoPiso(FachadaConfig.Piso),
                FullOpen = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                FachadaConfig.CoresPiso[FachadaConfig.Piso] = dlg.Color;
                FachadaConfig.Guardar();
                UpdateCorSwatch();
            }
        }

        private void UpdateCorSwatch()
        {
            string piso = string.IsNullOrWhiteSpace(_cmbPiso.Text)
                ? "PISO 0" : _cmbPiso.Text.Trim().ToUpperInvariant();
            var cor = FachadaConfig.CorDoPiso(piso);
            _btnCor.BackColor = cor;
            _btnCor.ForeColor = cor.GetBrightness() < 0.5 ? System.Drawing.Color.White : System.Drawing.Color.Black;
        }

        private void Remover()
        {
            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            string handle = row?.Tag as string;
            if (handle == null)
            {
                MessageBox.Show("Clique numa linha da grade primeiro.", "TSK TakeOff",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var resp = MessageBox.Show("Apagar a medição selecionada do desenho?",
                "TSK TakeOff", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (resp != DialogResult.Yes) return;

            FacRepo.Remover(handle);
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Linha em branco. Com uma medição seleccionada, marca-a. Sem selecção,
        /// fica à espera da próxima — que é como se separa um bloco do seguinte
        /// enquanto se está a medir.
        /// </summary>
        private void AlternarSeparador()
        {
            string handle = PaletteHost.HandleAlvo(HandleDaGrelha(), UltimoHandle());

            if (handle == null)
            {
                Config.SeparadorPendente = !Config.SeparadorPendente;
                PaletteHost.Log(Config.SeparadorPendente
                    ? "Linha em branco activada: a próxima medição começa um bloco novo."
                    : "Linha em branco desactivada.");
                return;
            }

            FacRepo.AlternarSeparador(handle);
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Título depois da última medição (ou da seleccionada). Aparece logo,
        /// e as medições seguintes caem por baixo dele. Carregar outra vez no
        /// mesmo botão remove.
        /// </summary>
        private void MarcarTitulo(string marca, string nome)
        {
            string handle = PaletteHost.HandleAlvo(HandleDaGrelha(), UltimoHandle());

            if (handle == null)
            {
                PaletteHost.Log("Meça primeiro: o título sai por baixo de uma medição.");
                return;
            }

            // Como na Alvenaria: acrescenta o nível, ou tira-o se já lá estiver.
            // Os outros níveis ficam — CAP e ART coexistem por baixo da mesma
            // medição, um capítulo não apaga um artigo que já lá estivesse.
            bool tinha = MarcasDe(handle).Contains(marca);

            FacRepo.AlternarTitulo(handle, marca);

            if (tinha)
            {
                PaletteHost.Log(nome + " removido.");
                PaletteHost.RefreshData();
                return;
            }

            // O texto do campo, como na Alvenaria. Sem isto a linha saía vazia
            // e só se podia escrever no Excel.
            string codigo, texto;
            PaletteHost.SepararTitulo(_txtTitulo != null ? _txtTitulo.Text : "",
                                      out codigo, out texto);

            if (codigo.Length > 0 || texto.Length > 0)
            {
                // Em que posição ficou o nível recém-criado. A ordem dos
                // títulos é SEMPRE capítulo e depois artigo — o AlternarMarca
                // reordena — por isso o índice sai daí, sem reler o desenho:
                // um capítulo fica em 0; um artigo fica em 1 se já houver
                // capítulo, senão em 0.
                //
                // As marcas lidas são as de ANTES do toggle: o _meds ainda é
                // a lista da última actualização, e é isso que aqui se quer.
                int i = (marca == "ART" && MarcasDe(handle).Contains("CAP")) ? 1 : 0;
                FacRepo.DefinirTextoDeTitulo(handle, i, codigo + "\u001f" + texto);
            }

            PaletteHost.Log("Linha de " + nome.ToLowerInvariant() +
                            " acrescentada por baixo" +
                            (texto.Length > 0 ? " com o texto do painel." : "."));
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Os títulos que este pano tem, tal como estavam na última leitura do
        /// desenho. Lista vazia quando o handle não é de cá.
        /// </summary>
        private List<string> MarcasDe(string handle)
        {
            foreach (var m in _meds)
                if (m.Handle == handle) return m.Marcas;
            return new List<string>();
        }

        /// <summary>
        /// Handle da linha escolhida na grelha, ou null.
        ///
        /// Só conta se alguém a tiver mesmo escolhido: um DataGridView com
        /// linhas está sempre pousado na primeira, e sem esta distinção o
        /// título ia parar ao primeiro pano do desenho. Mesmo defeito que a
        /// grelha da Alvenaria tinha — ver PaletteHost.GrelhaFoiEscolhida.
        /// </summary>
        private string HandleDaGrelha()
        {
            if (!_escolheu) return null;

            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            return row?.Tag as string;
        }

        /// <summary>
        /// A linha em que a grelha está, tenha ou não sido escolhida por
        /// alguém. Serve para a reconstrução voltar ao mesmo sítio — aqui não
        /// interessa se foi escolha, interessa não perder a posição.
        /// </summary>
        private string HandleDaGrelhaBruto()
        {
            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            return row?.Tag as string;
        }

        /// <summary>Põe o cursor nesta medição, se ela estiver na grelha.</summary>
        private bool Focar(string handle)
        {
            for (int i = 0; i < _dgv.Rows.Count; i++)
            {
                if ((_dgv.Rows[i].Tag as string) != handle) continue;
                try
                {
                    _dgv.CurrentCell = _dgv.Rows[i].Cells[0];
                    _dgv.Rows[i].Selected = true;
                    return true;
                }
                catch { return false; }
            }
            return false;
        }

        /// <summary>
        /// Volta a pôr a grelha onde estava, sem passar do fim. Ao remover,
        /// a lista encolhe e o índice antigo pode já não existir.
        /// </summary>
        private void ReporRolagem(int scroll)
        {
            if (scroll < 0 || _dgv.Rows.Count == 0) return;
            try
            {
                int alvo = scroll < _dgv.Rows.Count ? scroll : _dgv.Rows.Count - 1;
                _dgv.FirstDisplayedScrollingRowIndex = alvo;
            }
            catch { /* grelha mais curta do que o índice: fica onde está */ }
        }

        /// <summary>Alguém clicou numa linha desta grelha (e não foi o BindData).</summary>
        private bool _escolheu;

        /// <summary>Verdadeiro enquanto o BindData reconstrói: os eventos dele não contam.</summary>
        private bool _carregando;

        /// <summary>
        /// O pano para onde vai o que se acrescentar sem escolher nada: o que
        /// se acabou de medir, e só depois a última linha. Mesmo contrato da
        /// grelha da Alvenaria — e pela mesma razão, agora que esta grelha
        /// também sai por ordem do articulado.
        /// </summary>
        private string UltimoHandle()
        {
            string acabada = PaletteHost.UltimaMedicao;
            if (acabada != null)
                for (int i = 0; i < _dgv.Rows.Count; i++)
                    if ((_dgv.Rows[i].Tag as string) == acabada) return acabada;

            for (int i = _dgv.Rows.Count - 1; i >= 0; i--)
            {
                string h = _dgv.Rows[i].Tag as string;
                if (h != null) return h;
            }
            return null;
        }

        /// <summary>Apaga todas as medições de materiais do desenho, com confirmação.</summary>
        private void LimparTudo()
        {
            int n = _dgv.Rows.Count;
            if (n == 0)
            {
                PaletteHost.Log("Não há medições de materiais para limpar.");
                return;
            }

            var resp = MessageBox.Show(
                string.Format("Apagar as {0} medição(ões) de materiais deste desenho?\n\n" +
                              "Esta acção não pode ser desfeita pelo painel " +
                              "(mas o CTRL+Z do AutoCAD ainda funciona).", n),
                "TSK TakeOff — limpar tudo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (resp != DialogResult.Yes) return;

            int apagadas = 0;
            foreach (DataGridViewRow row in _dgv.Rows)
            {
                string handle = row.Tag as string;
                if (handle != null && FacRepo.Remover(handle)) apagadas++;
            }

            PaletteHost.Log(apagadas + " medição(ões) de materiais apagada(s).");
            PaletteHost.RefreshData();
        }

        // ------------------------------------------------------------------
        public void BindData(List<MedFachada> meds)
        {
            // Pela ordem da folha, não pela ordem por que foram desenhados —
            // como na grelha da Alvenaria. É o que faz o Nº desta grelha ser
            // a mesma linha do Excel.
            _meds = FolhaMedicao.OrdenarComoFolha(meds ?? new List<MedFachada>());
            meds = _meds;

            // Onde estávamos, ANTES de deitar a grelha abaixo. Sem isto a
            // grelha voltava ao topo a cada medição e a cada remoção — quem
            // estava a trabalhar no fim da lista perdia o sítio de cada vez.
            string handleSel = HandleDaGrelhaBruto();
            int scroll = _dgv.FirstDisplayedScrollingRowIndex;

            // Os eventos da reconstrução não são escolhas de ninguém.
            _carregando = true;
            try
            {
            _dgv.Rows.Clear();
            bool comMapa = MapaQuantidades.Existe;
            int n = 1;

            foreach (var m in meds)
            {
                int idx = _dgv.Rows.Add(
                    n++,
                    m.Separador ? "⏎" : "",
                    CodigoArtigo(m.Artigo),
                    m.Material,
                    m.Alcado,
                    m.Piso,
                    m.Tipo == TipoFachada.Retangulo ? "Retângulo" : "Polyline",
                    N2(m.Comp),
                    N2(m.Alt),
                    N2(m.Area));
                var row = _dgv.Rows[idx];
                row.Tag = m.Handle;
                var cor = FachadaConfig.CorDoPiso(m.Piso);
                row.Cells["piso"].Style.BackColor = cor;
                row.Cells["piso"].Style.ForeColor =
                    cor.GetBrightness() < 0.5 ? System.Drawing.Color.White : System.Drawing.Color.Black;

                // Só se aponta o dedo a um pano sem artigo quando há um mapa
                // onde o pôr. Sem mapa, não ter artigo é o normal.
                if (comMapa && string.IsNullOrEmpty(m.Artigo))
                {
                    var cel = row.Cells["artigo"];
                    cel.Value = "⊕";
                    cel.Style.ForeColor = System.Drawing.Color.FromArgb(192, 0, 0);
                    cel.ToolTipText = "Sem artigo do mapa: sai no fim da folha.";
                }
            }

            // Acabou de se medir um pano: o cursor vai para ele. Sem isto a
            // grelha era reconstruída e ficava na primeira linha, e o Capítulo
            // / Artigo a seguir caía no pano errado. Mesmo contrato da
            // Alvenaria.
            //
            // A medição nova manda na rolagem: pôr o CurrentCell já rola até
            // lá, e repor a rolagem antiga a seguir tirava-a do ecrã.
            string nova = PaletteHost.MedicaoNova;
            bool seguiuNova = nova != null && Focar(nova);

            // Senão volta-se ao que estava seleccionado; e se ele desapareceu
            // — foi removido — repõe-se ao menos a rolagem.
            if (!seguiuNova && !(handleSel != null && Focar(handleSel)))
                ReporRolagem(scroll);
            }
            finally { _carregando = false; }

            var porPiso = meds.GroupBy(m => m.Piso)
                .Select(g => $"{g.Key}: {N2(g.Sum(m => m.Area))} m²");
            _lblTotais.Text = string.Format("Total: {0} m²   |   {1}",
                N2(meds.Sum(m => m.Area)), string.Join("   |   ", porPiso));
        }

        private static string N2(double v) => v.ToString("N2", CultureInfo.CurrentCulture);

        /// <summary>Só o código do artigo — a descrição não cabe numa célula.</summary>
        private static string CodigoArtigo(string artigo)
        {
            if (string.IsNullOrEmpty(artigo)) return "";
            int i = artigo.IndexOf('\u001f');
            return i >= 0 ? artigo.Substring(0, i) : artigo;
        }
    }
}
