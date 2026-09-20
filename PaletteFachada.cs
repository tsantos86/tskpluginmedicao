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
        private ResultadosPainel _painel;
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
                Padding = new Padding(10),
                BackColor = PaletteTheme.Fundo
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
            var tituloConfig = new Label
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaTituloSeccao,
                Text = "CONFIGURAÇÃO",
                Font = PaletteTheme.TituloSeccao,
                ForeColor = PaletteTheme.Tinta,
                BackColor = PaletteTheme.FundoSeccao,
                Padding = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft
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
            // ----- Resultados -----
            //
            // A mesma vista das outras abas: árvore por Piso > Material >
            // Artigo, pesquisa, filtros e propriedades. O material faz de
            // serviço na hierarquia — é o que agrupa os panos da mesma
            // natureza — e não se inventou campo novo no DWG para isso.
            _painel = new ResultadosPainel("MATERIAIS");
            _painel.PropriedadeEditada += AoEditarPropriedade;

            // A partir do primeiro clique numa linha, é o painel que manda no
            // destino dos títulos. Os eventos disparados pelo BindData não
            // contam — quem os provoca é a reconstrução, não o utilizador.
            _painel.SeleccaoMudou += (s, e) => { if (!_carregando) _escolheu = true; };

            _lblTotais = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 26,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font(Font, FontStyle.Bold),
                Padding = new Padding(6, 0, 0, 0)
            };

            Controls.Add(_painel);
            Controls.Add(_lblTotais);
            Controls.Add(tools);
            Controls.Add(tituloConfig);
            Controls.Add(config);

            UpdateCorSwatch();
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

        private static Label Lbl(string t) =>
            new Label { Text = t, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };

        /// <summary>
        /// Grava no desenho o que se editou em PROPRIEDADES.
        ///
        /// O painel não escreve: diz o que foi editado e quem manda no desenho
        /// decide. Aqui é o FacRepo, e ele só sabe gravar o ARTIGO de um pano:
        /// o material e o piso ficam como foram medidos, e as dimensões vêm da
        /// geometria. Ver ResultadosAdaptadores.DeMateriais, que é onde esses
        /// campos estão marcados como de leitura.
        /// </summary>
        private void AoEditarPropriedade(object sender, PropriedadeEditadaEventArgs e)
        {
            if (e.No == null || e.No.Handle == null) return;
            if (e.Propriedade.Campo != "artigo") return;

            bool ok = FacRepo.DefinirArtigo(e.No.Handle, ChaveDoArtigo(e.Valor));

            if (!ok)
                PaletteHost.Log("não consegui gravar o " + e.Propriedade.Nome +
                                " deste pano — pode ter sido apagado do desenho.");

            PaletteHost.RefreshData();
            PaletteHost.EscreverExcelAgora();
        }

        /// <summary>
        /// Traduz um código escrito à mão para a chave que uma medição guarda.
        ///
        /// Com mapa importado, escrever "3.1.1" passa a valer o artigo INTEIRO
        /// do mapa. Ficar com o código novo e a designação antiga colada atrás
        /// dava um par que não existe em mapa nenhum: a medição tornava-se
        /// órfã e ia parar ao fim da folha sem nada que o explicasse.
        /// </summary>
        private static string ChaveDoArtigo(string escrito)
        {
            string codigo = (escrito ?? "").Trim();
            if (codigo.Length == 0) return "";

            var no = MapaQuantidades.PorCodigo(codigo);
            if (no != null) return no.Chave;

            if (MapaQuantidades.Existe)
                PaletteHost.Log("o mapa não tem nenhum artigo com o código «" + codigo +
                                "». A medição fica com ele, mas sai no fim da folha.");
            return codigo;
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
            var noSel = _painel.NoSeleccionado;
            // Um grupo não se remove: não é uma medição, é uma arrumação — e
            // não tem handle nenhum para onde apontar.
            string handle = noSel == null || noSel.EhGrupo ? null : noSel.Handle;
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

            var no = _painel.NoSeleccionado;
            return no == null || no.EhGrupo ? null : no.Handle;
        }

        /// <summary>
        /// A linha em que a grelha está, tenha ou não sido escolhida por
        /// alguém. Serve para a reconstrução voltar ao mesmo sítio — aqui não
        /// interessa se foi escolha, interessa não perder a posição.
        /// </summary>
        private string HandleDaGrelhaBruto()
        {
            var no = _painel.NoSeleccionado;
            return no == null || no.EhGrupo ? null : no.Handle;
        }

        /// <summary>
        /// Põe o cursor nesta medição, se ela estiver à vista.
        ///
        /// Pelo Id do nó — que numa medição é o handle — e não pelo índice da
        /// linha: a lista é refeita a cada medição e os índices mudam todos.
        /// </summary>
        private bool Focar(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return false;
            _painel.Seleccionar("M:" + handle);
            var no = _painel.NoSeleccionado;
            return no != null && no.Handle == handle;
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
        /// LIDO DA FONTE, NÃO DAS LINHAS VISÍVEIS. Um filtro é uma lente: o
        /// pano que se acabou de medir continua a ser o alvo do título
        /// seguinte, mesmo que o filtro em vigor o esteja a esconder.
        private string UltimoHandle()
        {
            string acabada = PaletteHost.UltimaMedicao;
            if (acabada != null)
                foreach (var m in _meds)
                    if (m.Handle == acabada) return acabada;

            for (int i = _meds.Count - 1; i >= 0; i--)
                if (_meds[i].Handle != null) return _meds[i].Handle;

            return null;
        }

        /// <summary>
        /// Apaga todas as medições de materiais do desenho, com confirmação.
        ///
        /// OPERA SOBRE A FONTE COMPLETA, NUNCA SOBRE AS LINHAS VISÍVEIS. Com um
        /// filtro aplicado, percorrer o que está à vista apagava só essas — e o
        /// botão diz «limpar tudo», portanto quem o carrega fica convencido de
        /// que o desenho ficou limpo. A árvore repete ainda o handle do pano
        /// nos vãos e nos títulos dele, o que inflacionaria a contagem.
        /// </summary>
        private void LimparTudo()
        {
            var handles = new List<string>();
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in _meds)
                if (m.Handle != null && vistos.Add(m.Handle)) handles.Add(m.Handle);

            if (handles.Count == 0)
            {
                PaletteHost.Log("Não há medições de materiais para limpar.");
                return;
            }

            string aviso = _painel != null && _painel.Estado.AFiltrar
                ? "\n\nATENÇÃO: há um filtro aplicado, mas isto apaga TODAS as " +
                  "medições do desenho, não só as que estão à vista."
                : "";

            var resp = MessageBox.Show(
                string.Format("Apagar as {0} medição(ões) de materiais deste desenho?{1}\n\n" +
                              "Esta acção não pode ser desfeita pelo painel " +
                              "(mas o CTRL+Z do AutoCAD ainda funciona).",
                              handles.Count, aviso),
                "TSK TakeOff — limpar tudo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (resp != DialogResult.Yes) return;

            int apagadas = 0;
            foreach (string handle in handles)
                if (FacRepo.Remover(handle)) apagadas++;

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

            // Os eventos da reconstrução não são escolhas de ninguém.
            _carregando = true;
            try
            {
                // A selecção e a rolagem são repostas pelo painel, PELO ID do
                // nó — que numa medição é o handle. Era este o sítio onde a
                // grelha antiga guardava o índice da linha e o perdia à
                // primeira reordenação.
                var raiz = ResultadosArvore.Construir(
                    ResultadosAdaptadores.DeMateriais(
                        _meds, Config.Regra,
                        MapaQuantidades.Existe
                            ? (Func<string, bool>)(a => MapaQuantidades.Procurar(a) != null)
                            : null,
                        CultureInfo.CurrentCulture),
                    CultureInfo.CurrentCulture);

                _painel.Vincular(raiz, PaletteHost.MedicaoNova);
            }
            catch (Exception ex)
            {
                PaletteHost.Log("Materiais: " + ex.Message);
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
