using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>Paleta ancorada do plugin (singleton).</summary>
    public static class PaletteHost
    {
        private static PaletteSet _ps;
        private static MedPanelControl _ctrl;
        private static FachadaControl _ctrlFachada;
        private static LinearControl _ctrlLinear;
        private static ContagemControl _ctrlContagem;

        public static readonly ExcelLiveSync Excel = new ExcelLiveSync();

        public static void Show()
        {
            try
            {
                if (_ps == null)
                {
                    _ps = new PaletteSet("TSK TakeOff — Medições",
                        new Guid("7E4B1F7A-3C55-4A8E-9C31-0AC5011AD001"));
                    _ctrlFachada = new FachadaControl();
                    _ctrlLinear = new LinearControl();
                    _ctrl = new MedPanelControl();
                    _ctrlContagem = new ContagemControl();
                    // UM PAINEL SÓ.
                    //
                    // Eram quatro abas — Arquitetura, Materiais, Lineares,
                    // Contagens — e isso dividia o que a medição junta. Não há
                    // especialidades: mede-se arquitetura, e o que varia é o
                    // TIPO DE MEDIDA, que agora é um nível da árvore.
                    //
                    // Com abas, o PISO 0 nunca mostrava o seu total nas quatro
                    // unidades: cada aba só via a sua, e para saber o que ali
                    // estava medido era preciso somar de cabeça entre
                    // separadores. Agora a árvore mostra
                    // «40,33 m² · 3,88 m³ · 29,15 m · 2 un.» numa linha.
                    _ps.Add("Arquitetura", _ctrl);
                    _ps.DockEnabled = DockSides.Left | DockSides.Right | DockSides.None;
                }
                _ps.Visible = true;
                // MinimumSize só depois de a janela existir (evita COM 0x80010114)
                try { _ps.MinimumSize = new Size(520, 420); } catch { }
            }
            catch (Exception ex)
            {
                Log("Erro ao abrir a paleta: " + ex.Message);
                return;
            }
            RefreshData();
        }

        /// <summary>Recarrega tudo do DWG, atualiza as grades e o Excel ao vivo.</summary>
        public static void RefreshData()
        {
            if (_ctrl == null) return;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                var cron = new Cronometro("Actualização");

                // Uma travessia do Model Space, não três. Ver Leitura.cs.
                List<Parede> paredes;
                List<MedFachada> fachadas;
                List<MedItem> lineares;
                List<MedContagem> contagens;
                Leitura.Tudo(doc.Database, out paredes, out fachadas,
                             out lineares, out contagens);
                cron.Marcar("ler o desenho");
                cron.Nota(paredes.Count + " paredes, " + fachadas.Count +
                          " materiais, " + lineares.Count + " lineares, " +
                          contagens.Count + " contagens");

                // Tudo para o mesmo painel, numa árvore só. A travessia do
                // Model Space continua a ser UMA — ver Leitura.Tudo — e agora
                // a apresentação também.
                _ctrl.BindData(paredes, fachadas, lineares, contagens);
                cron.Marcar("árvore de resultados");

                // Depois das grelhas todas: já cada uma teve a hipótese de pôr
                // o cursor na medição acabada de fazer.
                LimparMedicaoNova();

                // As contagens entram na folha pelo construtor, que as lê daqui.
                FolhaMedicao.Contagens = contagens;

                if (Excel.Conectado)
                {
                    AgendarExcel(paredes, fachadas, lineares);
                    cron.Marcar("Excel");
                }
                cron.Fim();
            }
            catch (Exception ex)
            {
                Log("Erro ao atualizar medições: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        // Escrita do Excel, adiada
        // ------------------------------------------------------------------
        private static System.Windows.Forms.Timer _adiar;
        private static IList<Parede> _pendParedes;
        private static IList<MedFachada> _pendFachadas;
        private static IList<MedItem> _pendLineares;

        /// <summary>
        /// Quanto custou a última escrita do Excel, em milissegundos. É a
        /// medida de que a espera seguinte depende — ver <see cref="AgendarExcel"/>.
        /// </summary>
        private static long _ultimaEscritaMs;

        /// <summary>
        /// Marca o Excel para reescrever daqui a pouco. Cada nova medição
        /// reinicia a contagem, por isso medir cinco paredes seguidas custa uma
        /// escrita e não cinco — e o AutoCAD deixa de ficar à espera do Excel
        /// entre cliques.
        ///
        /// A ESPERA É PROPORCIONAL AO CUSTO, e não fixa.
        ///
        /// Com um atraso fixo de 400 ms, uma folha grande punha o AutoCAD a
        /// congelar quase a cada medição: se a escrita custa dois segundos e a
        /// pausa entre medições é de dois, paga-se a escrita quase sempre. O
        /// utilizador sente isto como "o programa trava a meio de medir", e não
        /// tem como saber que está à espera do Excel.
        ///
        /// Esperando pelo menos o que a ÚLTIMA escrita demorou, o Excel nunca
        /// ocupa mais de metade do tempo de quem mede; e durante uma série de
        /// medições seguidas simplesmente não chega a disparar. Quando a pessoa
        /// pára para pensar, a folha apanha tudo de uma vez.
        ///
        /// Isto corrige-se sozinho: numa folha pequena a espera fica nos 400 ms
        /// como sempre esteve, e numa folha que cresceu vai subindo com ela.
        /// </summary>
        private static void AgendarExcel(IList<Parede> paredes, IList<MedFachada> fachadas,
            IList<MedItem> lineares)
        {
            _pendParedes = paredes;
            _pendFachadas = fachadas;
            _pendLineares = lineares;

            if (Config.AtrasoExcelMs <= 0) { EscreverExcelAgora(); return; }

            int espera = Config.AtrasoExcelMs;
            if (_ultimaEscritaMs > espera)
            {
                // O tecto existe para a folha não deixar de acompanhar de todo.
                // Sem ele, uma escrita muito lenta empurrava a seguinte para
                // tão longe que a folha parecia ter deixado de funcionar.
                espera = (int)System.Math.Min(_ultimaEscritaMs, Config.AtrasoExcelMaxMs);
            }

            if (_adiar == null)
            {
                _adiar = new System.Windows.Forms.Timer();
                _adiar.Tick += (s, e) => EscreverExcelAgora();
            }
            _adiar.Interval = espera;
            _adiar.Stop();
            _adiar.Start();
        }

        /// <summary>Escreve já o que estiver pendente. Usado pelo botão Atualizar.</summary>
        internal static void EscreverExcelAgora()
        {
            if (_adiar != null) _adiar.Stop();
            if (_pendParedes == null || !Excel.Conectado) return;

            var p = _pendParedes; var f = _pendFachadas; var l = _pendLineares;
            _pendParedes = null; _pendFachadas = null; _pendLineares = null;

            var relogio = System.Diagnostics.Stopwatch.StartNew();
            try { Excel.AtualizarTudo(p, f, l, FolhaMedicao.Contagens, Config.Regra); }
            catch (Exception ex) { Log("Excel: " + ex.Message); }
            finally
            {
                relogio.Stop();
                // Medir SEMPRE, mesmo quando a escrita rebentou: uma escrita que
                // demora e falha continua a ser tempo em que o AutoCAD esteve
                // preso, e a espera seguinte tem de contar com isso.
                _ultimaEscritaMs = relogio.ElapsedMilliseconds;

                if (Cronometro.Sempre && _ultimaEscritaMs > Config.AtrasoExcelMs)
                    Log("Excel demorou " + _ultimaEscritaMs + " ms: a próxima " +
                        "escrita só acontece após " +
                        System.Math.Min(_ultimaEscritaMs, Config.AtrasoExcelMaxMs) +
                        " ms sem medir.");
            }
        }

        /// <summary>Mensagens vão para a linha de comando, nunca para caixas de erro.</summary>
        internal static void Log(string msg)
        {
            try
            {
                AcadApp.DocumentManager.MdiActiveDocument?
                    .Editor.WriteMessage("\n[TSK] " + msg + "\n");
            }
            catch { }
        }

        /// <summary>Quando é que a linha seleccionada na grelha mudou.</summary>
        private static DateTime _instanteGrelha = DateTime.MinValue;

        /// <summary>A grelha foi clicada: passa a ser ela a mandar.</summary>
        internal static void MarcarSeleccaoGrelha()
        {
            _instanteGrelha = DateTime.UtcNow;
        }

        /// <summary>
        /// A linha em que a grelha está foi MESMO escolhida por alguém?
        ///
        /// Um DataGridView com linhas tem sempre uma CurrentRow — a 0, se
        /// ninguém tocou em nada. Sem esta distinção, "a linha seleccionada"
        /// dava a primeira medição do desenho a quem nunca clicou em lado
        /// nenhum, e era para lá que ia o título e o vão: abria-se o desenho,
        /// carregava-se em Artigo, e a linha aparecia colada à primeira parede
        /// da obra em vez de à medição em que se estava a trabalhar.
        /// </summary>
        internal static bool GrelhaFoiEscolhida
        {
            get { return _instanteGrelha != DateTime.MinValue; }
        }

        /// <summary>
        /// A medição que se acabou mesmo de fazer.
        ///
        /// A ÚLTIMA LINHA DA GRELHA NÃO SERVE PARA ISTO. A grelha sai por
        /// ordem do articulado, não por ordem de quem mediu: mede-se uma
        /// parede de um artigo do meio do mapa e ela fica a meio da lista,
        /// com outras por baixo. O "Adicionar vão" caía na última LINHA — uma
        /// parede qualquer lá de baixo — e o vão descontava na medição errada,
        /// sem dar erro nenhum. Os totais até fechavam; só estavam no sítio
        /// errado.
        ///
        /// Guarda-se o handle no momento em que a medição nasce, que é a única
        /// altura em que se sabe, com certeza, qual é.
        /// </summary>
        internal static string UltimaMedicao
        {
            get
            {
                // O handle é do desenho onde se mediu, e um handle igual pode
                // existir noutro DWG a apontar para outra coisa. Ao mudar de
                // desenho, o registo deixa de valer.
                return _docDaUltima != null && _docDaUltima == NomeDoDocumento()
                    ? _ultimaMedicao : null;
            }
        }

        private static string _ultimaMedicao;
        private static string _docDaUltima;

        private static string NomeDoDocumento()
        {
            try { return AcadApp.DocumentManager.MdiActiveDocument?.Name; }
            catch { return null; }
        }

        /// <summary>Regista a medição acabada de criar. Chamado pelos comandos.</summary>
        internal static void RegistarMedicao(string handle)
        {
            if (string.IsNullOrEmpty(handle)) return;
            _ultimaMedicao = handle;
            _docDaUltima = NomeDoDocumento();
            _porSeleccionar = handle;
        }

        /// <summary>
        /// A medição nova que a grelha ainda não seleccionou.
        ///
        /// O CURSOR TEM DE IR ATRÁS DE QUEM MEDE. A grelha é reconstruída a
        /// cada medição e repunha o cursor onde ele estava ANTES — numa
        /// medição antiga qualquer. Como o Adicionar vão respeita a linha
        /// seleccionada (e bem: é a escolha explícita de quem mede), o vão ia
        /// para essa medição antiga em vez de ir para a parede acabada de
        /// desenhar. Descontava na errada, e os totais fechavam na mesma.
        ///
        /// Consome-se uma vez: passada a reconstrução, quem manda volta a ser
        /// a selecção da pessoa.
        /// </summary>
        internal static string MedicaoNova { get { return _porSeleccionar; } }

        /// <summary>
        /// Larga a marca, depois de TODAS as grelhas terem tido a hipótese de
        /// a apanhar. Não pode ser a primeira grelha a consumi-la: a medição
        /// nova pode ser um pano, e quem faz bind primeiro é a alvenaria —
        /// consumia-a e a grelha dos Materiais nunca chegava a vê-la.
        /// </summary>
        internal static void LimparMedicaoNova()
        {
            _porSeleccionar = null;
        }

        private static string _porSeleccionar;

        // ------------------------------------------------------------------
        // Texto das linhas de título — partilhado pelas duas abas
        // ------------------------------------------------------------------

        /// <summary>
        /// Parte o texto do campo em código + designação.
        ///
        /// Aceita as duas formas por que uma pessoa escreve isto: "7.2.2 — 2 -
        /// Fornecimento…" (o que o mapa preenche) e texto solto — "SEM REF" —
        /// que não tem código nenhum. O separador é o travessão com espaços de
        /// ambos os lados; um hífen agarrado a uma palavra não conta, senão um
        /// "SEM-REF" partia-se ao meio.
        /// </summary>
        internal static void SepararTitulo(string bruto, out string codigo, out string texto)
        {
            codigo = "";
            texto = (bruto ?? "").Trim();
            if (texto.Length == 0) return;

            // Os dois separadores têm 3 caracteres: espaço, traço, espaço.
            int i = texto.IndexOf(" — ", StringComparison.Ordinal);
            if (i < 0) i = texto.IndexOf(" - ", StringComparison.Ordinal);
            if (i <= 0) return;                     // sem código: é tudo designação

            string possivel = texto.Substring(0, i).Trim();

            // Só é código se PARECER um: dígitos, pontos e letras curtas. Uma
            // frase antes do travessão é designação, não código de artigo.
            if (possivel.Length > 16 || possivel.IndexOf(' ') >= 0) return;

            codigo = possivel;
            texto = texto.Substring(i + 3).Trim();
        }

        /// <summary>O inverso do <see cref="SepararTitulo"/>, para encher o campo.</summary>
        internal static string JuntarTitulo(string codigo, string designacao)
        {
            codigo = (codigo ?? "").Trim();
            designacao = (designacao ?? "").Trim();
            if (codigo.Length == 0) return designacao;
            if (designacao.Length == 0) return codigo;
            return codigo + " — " + designacao;
        }

        /// <summary>O texto do artigo que está escolhido, para abrir o campo cheio.</summary>
        internal static string TextoDoArtigoCorrente()
        {
            if (string.IsNullOrEmpty(Config.Artigo)) return "";
            var no = MapaQuantidades.Procurar(Config.Artigo);
            if (no != null) return JuntarTitulo(no.Codigo, no.Designacao);

            int sep = Config.Artigo.IndexOf('\u001f');
            return sep >= 0
                ? JuntarTitulo(Config.Artigo.Substring(0, sep), Config.Artigo.Substring(sep + 1))
                : Config.Artigo;
        }

        /// <summary>
        /// Esquece qual foi a última medição.
        ///
        /// Para os comandos que criam VÁRIAS de uma vez — o TSKMEDSEL — em que
        /// "a última" não quer dizer nada. Sem isto ficava a apontar para a
        /// medição anterior ao comando, que é pior do que não saber: o
        /// Adicionar vão iria para uma parede que já lá estava antes.
        /// </summary>
        internal static void EsquecerUltimaMedicao()
        {
            _ultimaMedicao = null;
            _docDaUltima = null;
        }

        /// <summary>
        /// Onde é que a próxima coisa acrescentada vai parar — títulos, vãos,
        /// linhas em branco, tudo.
        ///
        /// Manda o lado onde se mexeu por último: clicaste numa linha da
        /// paleta, é ela; clicaste numa célula do Excel, é ela. Antes o Excel
        /// ganhava sempre, e isso estava errado — o Excel tem SEMPRE uma célula
        /// activa, mesmo esquecida do dia anterior, portanto a selecção da
        /// paleta nunca chegava a contar. Sem nenhuma das duas, vai para a
        /// última medição, que é o que se quer enquanto se mede a direito.
        /// </summary>
        internal static string HandleAlvo(string daGrelha, string ultimo)
        {
            string doExcel = Excel.MedicaoNaCelulaSeleccionada();

            // Empate a favor da grelha: no arranque as duas datas são iguais e,
            // se a pessoa clicou numa linha da paleta, foi essa a sua escolha.
            if (daGrelha != null && _instanteGrelha >= Excel.InstanteSeleccao())
                return daGrelha;

            return doExcel ?? daGrelha ?? ultimo;
        }

        /// <summary>Executa um comando do plugin com segurança a partir da UI.</summary>
        internal static void RunCommand(string comando)
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                {
                    Log("Abra um desenho antes de medir.");
                    return;
                }

                // Ao carregar num botão da paleta, o foco fica na paleta e não no
                // desenho. O SendStringToExecute só corre quando a janela do
                // desenho fica activa — daí ser preciso clicar duas ou três vezes
                // para o comando arrancar. Devolver o foco ao AutoCAD antes de
                // enviar resolve isso.
                try { AcadApp.MainWindow.Focus(); } catch { }
                try { doc.Window.Focus(); } catch { }

                doc.SendStringToExecute(comando, true, false, true);
            }
            catch (Exception ex)
            {
                Log("Não foi possível executar " + comando.Trim() + ": " + ex.Message);
            }
        }
    }

    /// <summary>Conteúdo da aba Arquitetura: configuração, ações e resultados ao vivo.</summary>
    public class MedPanelControl : UserControl
    {
        private TextBox _txtServico;
        private ComboBox _cmbAlcado;
        private TextBox _txtBloco;
        private ComboBox _cmbPiso;
        private Button _btnCor;
        /// <summary>A cor do piso corrente, desenhada na pastilha do botão.</summary>
        private Color _corDoPiso = Color.Gray;
        private NumericUpDown _numAltura;
        private NumericUpDown _numEspessura;
        private ComboBox _cmbRegra;
        /// <summary>Artigo do mapa usado nas próximas medições.</summary>
        private ComboBox _cmbArtigoMqt;

        /// <summary>Texto que sai na linha de título criada pelos botões Capítulo / Artigo.</summary>
        private TextBox _txtTitulo;

        /// <summary>Layer das próximas medições (vazio = calculada do artigo + bloco).</summary>
        private TextBox _txtLayer;

        /// <summary>Mostra a layer que vai mesmo ser usada.</summary>
        private Label _lblLayer;
        private Label _lblArtigoMqt;
        // O texto do artigo é escolhido no mapa de quantidades. Títulos antigos
        // de outros níveis são ignorados pelo modelo e não voltam a ser criados.
        private DataGridView _dgv;
        private Label _lblTotais;
        /// <summary>Última lista carregada na grelha, para consultar sem reler o DWG.</summary>
        private List<Parede> _paredes = new List<Parede>();
        /// <summary>As outras três listas do mesmo desenho, para a árvore única.</summary>
        private List<MedFachada> _fachadas = new List<MedFachada>();
        private List<MedItem> _lineares = new List<MedItem>();
        private List<MedContagem> _contagens = new List<MedContagem>();
        private ToolStripButton _btnExcel;
        private bool _carregando;
        private Panel _resultadosCompactos;
        private DataGridView _dgvCompacto;
        private Label _lblResultadoResumo;
        private Button _lblPropriedadeTitulo;
        private bool _configExpandida;
        /// <summary>PROPRIEDADES recolhe-se, como CONFIGURAÇÃO e Mais opções.</summary>
        private bool _propriedadesAbertas = true;
        private NoResultado _raizResultados;
        private EstadoVista _estadoResultados = new EstadoVista();
        private TextBox _txtPesquisa;
        private Button _btnFiltros;
        private Button _btnLimparFiltros;
        private Button _btnPropriedadesModo;
        /// <summary>As propriedades como mosaicos de métrica.</summary>
        private PalettePanelShell.MosaicoMetricas _mosaico;
        private Button _btnMedirAqui;
        /// <summary>O resumo da próxima medição, no cabeçalho.</summary>
        private Label _lblProximaMedicao;
        /// <summary>O DWG activo, no canto do cabeçalho.</summary>
        private Label _lblDwg;
        /// <summary>Chips do que está filtrado, na barra dos resultados.</summary>
        private Label _lblChips;
        /// <summary>Estado vazio, por cima da árvore.</summary>
        private Label _lblVazio;
        /// <summary>Pausa antes de refazer a árvore enquanto se escreve na pesquisa.</summary>
        private Timer _adiarPesquisa;
        /// <summary>O cabeçalho da CONFIGURAÇÃO, que repete esse resumo quando recolhida.</summary>
        private Button _btnConfigToggle;
        /// <summary>Barra de ações sobre o que já está medido, separada da de MEDIR.</summary>
        private ToolStrip _barraResultados;
        /// <summary>Modo do painel de propriedades: só o essencial, ou tudo.</summary>
        private bool _propriedadesEssenciais = true;

        public MedPanelControl()
        {
            BuildUi();
        }

        // ------------------------------------------------------------------
        // UI
        // ------------------------------------------------------------------
        private void BuildUi()
        {
            Dock = DockStyle.Fill;

            // ----- Cabeçalho compacto da Arquitetura -----
            //
            // A próxima medição aparece aqui E no resumo da CONFIGURAÇÃO
            // recolhida, de propósito. É a única coisa da paleta que muda o
            // que acontece a seguir sem ninguém estar a olhar para ela, e
            // medir dez paredes para o artigo errado descobre-se tarde demais.
            var cabecalho = new Panel
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaCabecalho,
                BackColor = PaletteTheme.AzulTopo
            };
            _lblProximaMedicao = new Label
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10, 0, 8, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PaletteTheme.Pequeno,
                ForeColor = PaletteTheme.AcentoEscuro,
                AutoEllipsis = true,
                AccessibleName = "Próxima medição"
            };
            // A faixa de cima leva a marca à esquerda e o DWG activo à direita.
            //
            // O DWG tem de estar à vista: um handle só quer dizer alguma coisa
            // dentro do desenho onde foi criado, e trabalhar com dois abertos
            // ao mesmo tempo é o normal. Sem isto, media-se convencido de estar
            // no ficheiro errado e só a folha o dizia.
            var faixa = new Panel { Dock = DockStyle.Top, Height = 20 };
            _lblDwg = new Label
            {
                Dock = DockStyle.Right,
                Width = 190,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, PaletteTheme.Margem, 0),
                Font = PaletteTheme.Pequeno,
                AutoEllipsis = true,
                AccessibleName = "Desenho activo"
            };

            // A marca e a aba, com pesos diferentes: "TSK TAKEOFF" identifica o
            // produto e "Arquitetura" diz onde se está. Tudo com o mesmo peso
            // lia-se como uma frase, e nenhuma das duas informações chegava.
            var marca = new Label
            {
                Dock = DockStyle.Fill,
                Text = "TSK TAKEOFF",
                Padding = new Padding(10, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = PaletteTheme.Tinta,
                Font = PaletteTheme.TituloSeccao,
                AutoEllipsis = true
            };
            faixa.Controls.Add(marca);
            faixa.Controls.Add(_lblDwg);

            cabecalho.Controls.Add(_lblProximaMedicao);
            cabecalho.Controls.Add(faixa);

            // ----- Configurações da Arquitetura (topo) -----
            //
            // A grelha é declarada ANTES do botão que a recolhe. O lambda do
            // Click fecha sobre ela, e uma variável local só existe a partir
            // da linha em que é declarada: com a ordem trocada isto não
            // compilava (CS0841).
            var config = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                Visible = false,
                ColumnCount = 2,
                Padding = new Padding(8)
            };

            var configHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaTituloSeccao,
                BackColor = PaletteTheme.FundoSeccao
            };
            _btnConfigToggle = new Button {
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                FlatAppearance = { BorderSize = 0 },
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PaletteTheme.TituloSeccao,
                BackColor = PaletteTheme.FundoSeccao,
                ForeColor = PaletteTheme.Tinta,
                Text = "▶  CONFIGURAÇÃO",
                AccessibleName = "Configuração"
            };
            _btnConfigToggle.Click += (s, e) => {
                _configExpandida = !_configExpandida;
                config.Visible = _configExpandida;
                _btnConfigToggle.AccessibleDescription =
                    _configExpandida ? "Expandida." : "Recolhida.";
                AtualizarResumoDaProxima();
            };
            // Um risco de acento sob o título: a mesma linguagem do separador
            // activo no mockup Eberick. Sem isto, CONFIGURAÇÃO só se distingue
            // do resto por um cinzento ligeiramente diferente — a única cor do
            // painel inteiro ficava reservada à selecção e ao foco.
            _btnConfigToggle.Paint += (s, e) =>
            {
                using (var pincel = new SolidBrush(PaletteTheme.Acento))
                    e.Graphics.FillRectangle(pincel, 0, _btnConfigToggle.Height - 2,
                        _btnConfigToggle.Width, 2);
            };
            configHost.Controls.Add(_btnConfigToggle);
            // QUATRO colunas: rótulo · campo · rótulo · campo.
            //
            // Em pares, os campos que se lêem juntos ficam juntos — Serviço ao
            // lado de Bloco, Altura ao lado de Espessura — e a secção passa de
            // nove linhas para cinco. Num painel estreito, essas quatro linhas
            // são a diferença entre ver a árvore de resultados e ter de rolar
            // até ela.
            config.ColumnCount = 4;
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _txtServico = new TextBox { Text = Config.Servico, Dock = DockStyle.Fill };

            FachadaConfig.Carregar();
            _cmbPiso = new ComboBox { Dock = DockStyle.Fill };
            foreach (var piso in FachadaConfig.CoresPiso.Keys) _cmbPiso.Items.Add(piso);
            _cmbPiso.Text = Config.Piso;
            _cmbPiso.TextChanged += (s, e) => AtualizarCor();

            // A cor do piso numa PASTILHA, e não no fundo do botão inteiro.
            //
            // Pintado por inteiro, o botão ficava um bloco de cor com o texto
            // "Cor…" por cima — e a legibilidade dependia da cor que o piso
            // calhasse a ter. Com uma pastilha à esquerda, o botão lê-se
            // sempre e a cor vê-se na mesma.
            _btnCor = new Button
            {
                Text = "  Cor…",
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleRight,
                Font = PaletteTheme.Pequeno,
                BackColor = PaletteTheme.Fundo,
                ForeColor = PaletteTheme.Tinta,
                UseVisualStyleBackColor = false,
                AccessibleName = "Cor do piso"
            };
            _btnCor.FlatAppearance.BorderColor = PaletteTheme.BordaCampo;
            _btnCor.Click += (s, e) => EscolherCor();
            _btnCor.Paint += (s, e) =>
            {
                var r = new Rectangle(4, (_btnCor.Height - 12) / 2, 12, 12);
                using (var pincel = new SolidBrush(_corDoPiso))
                    e.Graphics.FillRectangle(pincel, r);
                using (var caneta = new Pen(PaletteTheme.BordaCampo))
                    e.Graphics.DrawRectangle(caneta, r);
            };

            _cmbAlcado = new ComboBox { Dock = DockStyle.Fill };
            foreach (var a in FachadaConfig.Alcados) _cmbAlcado.Items.Add(a);
            _cmbAlcado.Text = Config.Alcado;

            _txtBloco = new TextBox { Text = Config.Bloco, Dock = DockStyle.Fill };

            // A layer onde a medição é desenhada, sem o prefixo MED_.
            //
            // Vazia, é calculada: os 6 primeiros do código do artigo mais o
            // bloco. O placeholder mostra o que vai sair, para não ser preciso
            // adivinhar nem escrever o que já se sabe.
            _txtLayer = new TextBox { Text = Config.Layer, Dock = DockStyle.Fill };
            _lblLayer = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(90, 90, 90)
            };

            // O artigo é escolhido directamente na lista do mapa importado.
            // A procura foi retirada para deixar o topo mais limpo e evitar
            // duas caixas para a mesma tarefa. A lista tem scroll e mostra
            // todos os artigos disponíveis.
            _cmbArtigoMqt = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                DropDownWidth = 620,
                MaxDropDownItems = 20,
                IntegralHeight = false
            };
            _lblArtigoMqt = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(90, 90, 90)
            };

            // O TEXTO QUE VAI SAIR NA LINHA DE TÍTULO.
            //
            // Chegou a não existir, de propósito: com o articulado importado, o
            // código e a designação vêm do mapa, e duas fontes para a mesma
            // coisa é como uma folha começa a discordar de si própria. Só que
            // isso deixou de fora tudo o que o mapa não tem — o "SEM REF", o
            // título escrito à mão, o artigo que ainda não foi orçamentado — e
            // aí a linha saía VAZIA e só se podia escrever no Excel.
            //
            // Volta a existir, mas com uma regra que evita a discórdia de
            // origem: é UM campo só, e mostra sempre o que vai ser escrito.
            // Escolher um artigo no mapa preenche-o; escrever por cima manda.
            _txtTitulo = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = PaletteHost.TextoDoArtigoCorrente()
            };

            _cmbArtigoMqt.SelectedIndexChanged += (s, e) => EscolherArtigoMqt();


            _numAltura = new NumericUpDown
            {
                DecimalPlaces = 2,
                Increment = 0.05M,
                Minimum = 0.1M,
                Maximum = 20M,
                Value = (decimal)Config.Altura,
                Dock = DockStyle.Fill
            };
            _numEspessura = new NumericUpDown
            {
                DecimalPlaces = 2,
                Increment = 0.01M,
                Minimum = 0M,          // 0 = usar o lado menor do retângulo
                Maximum = 2M,
                Value = (decimal)Config.Espessura,
                Dock = DockStyle.Fill
            };
            _cmbRegra = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _cmbRegra.Items.Add(Config.RegraDescricao(RegraDesconto.DescontarTudo));
            _cmbRegra.Items.Add(Config.RegraDescricao(RegraDesconto.Sinapi2m2));
            _cmbRegra.Items.Add(Config.RegraDescricao(RegraDesconto.NaoDescontar));
            _cmbRegra.SelectedIndex = (int)Config.Regra;
            _cmbRegra.SelectedIndexChanged += (s, e) =>
            {
                Config.Regra = (RegraDesconto)_cmbRegra.SelectedIndex;
                PaletteHost.RefreshData();
            };

            // Ordem pelo fluxo de trabalho: classificação, serviço, localização
            // e por fim os parâmetros geométricos. O topo fica uma linha menor
            // e a lista de artigos passa a ocupar todo o espaço disponível.
            // O artigo atravessa as três colunas da direita: é o campo mais
            // longo de todos — um código e uma designação de caderno de
            // encargos — e emparelhá-lo cortava-o a meio.
            config.Controls.Add(Rot("Artigo do mapa:"), 0, 0);
            config.Controls.Add(_cmbArtigoMqt, 1, 0);
            config.SetColumnSpan(_cmbArtigoMqt, 3);

            config.Controls.Add(new Label { Width = 0, Height = 0 }, 0, 1);
            config.Controls.Add(_lblArtigoMqt, 1, 1);
            config.SetColumnSpan(_lblArtigoMqt, 3);

            // Já não diz "/ Layer": a layer tem campo próprio. O serviço passou
            // a ser só o que identifica a medição na folha do cliente.
            //
            // Ao lado do bloco, que é o par natural: um diz o QUE se mede, o
            // outro diz ONDE.
            config.Controls.Add(Rot("Serviço:"), 0, 2);
            config.Controls.Add(_txtServico, 1, 2);
            // Deixou de ser só torre/fracção: o que aqui estiver arranca como
            // designação da medição na folha, e é onde se escreve o "WC1" ou
            // o "quarto 2". O rótulo tem de o dizer, senão ninguém adivinha.
            config.Controls.Add(Rot("Bloco:"), 2, 2);
            config.Controls.Add(_txtBloco, 3, 2);

            // "(opcional)" no rótulo porque ele JÁ o é — o SyncConfig trata o
            // campo vazio como "sem piso" e a folha não emite cabeçalho nenhum.
            // Só que a caixa abre com "PISO 0" lá dentro e o rótulo não dizia
            // nada, e assim ninguém adivinha que se pode apagar.
            //
            // A cor vai colada ao piso, que é de quem ela é.
            var pisoECor = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0), BackColor = Color.Transparent
            };
            pisoECor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pisoECor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62));
            pisoECor.Controls.Add(_cmbPiso, 0, 0);
            pisoECor.Controls.Add(_btnCor, 1, 0);

            config.Controls.Add(Rot("Piso:"), 0, 3);
            config.Controls.Add(pisoECor, 1, 3);
            config.Controls.Add(Rot("Alçado / zona:"), 2, 3);
            config.Controls.Add(_cmbAlcado, 3, 3);

            // As duas dimensões lado a lado: são lidas em conjunto — "2,80 por
            // 0,15" — e separadas em linhas obrigavam a saltar entre elas.
            config.Controls.Add(Rot("Altura (m):"), 0, 4);
            config.Controls.Add(_numAltura, 1, 4);
            config.Controls.Add(Rot("Espessura (m):"), 2, 4);
            config.Controls.Add(_numEspessura, 3, 4);

            config.Controls.Add(Rot("Regra de vãos:"), 0, 5);
            config.Controls.Add(_cmbRegra, 1, 5);
            config.SetColumnSpan(_cmbRegra, 3);

            // ----- Mais opções -----
            //
            // O texto do título e a layer manual saem do corpo principal.
            // Não são campos do dia a dia: a layer é calculada do artigo e do
            // bloco em quase todos os casos, e o texto do título vem do mapa.
            // Ocupavam três das doze linhas da CONFIGURAÇÃO, e num painel
            // estreito isso empurrava a altura e a espessura para fora do ecrã.
            var maisOpcoes = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Visible = false,
                Margin = new Padding(0),
                BackColor = PaletteTheme.Fundo
            };
            maisOpcoes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            maisOpcoes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // O rótulo diz "título" e não "artigo" porque o mesmo campo serve
            // os dois botões, Capítulo e Artigo.
            maisOpcoes.Controls.Add(Rot("Texto do título:"), 0, 0);
            maisOpcoes.Controls.Add(_txtTitulo, 1, 0);
            maisOpcoes.Controls.Add(Rot("Layer (opcional):"), 0, 1);
            maisOpcoes.Controls.Add(_txtLayer, 1, 1);
            maisOpcoes.Controls.Add(new Label { Width = 0, Height = 0 }, 0, 2);
            maisOpcoes.Controls.Add(_lblLayer, 1, 2);

            var btnMaisOpcoes = new Button
            {
                Dock = DockStyle.Fill,
                Text = "▶  Mais opções",
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PaletteTheme.Pequeno,
                ForeColor = PaletteTheme.Acento,
                BackColor = PaletteTheme.Fundo,
                Height = PaletteTheme.AlturaCampo,
                AccessibleName = "Mais opções",
                AccessibleDescription = "Recolhida. Texto do título e layer manual."
            };
            btnMaisOpcoes.FlatAppearance.BorderSize = 0;
            btnMaisOpcoes.Click += (s, e) =>
            {
                maisOpcoes.Visible = !maisOpcoes.Visible;
                btnMaisOpcoes.Text = (maisOpcoes.Visible ? "▼" : "▶") + "  Mais opções";
                btnMaisOpcoes.AccessibleDescription =
                    (maisOpcoes.Visible ? "Expandida. " : "Recolhida. ") +
                    "Texto do título e layer manual.";
            };

            config.Controls.Add(btnMaisOpcoes, 0, 6);
            config.SetColumnSpan(btnMaisOpcoes, 4);
            config.Controls.Add(maisOpcoes, 0, 7);
            config.SetColumnSpan(maisOpcoes, 4);


            AtualizarCor();
            MostrarLayerEfectiva();

            // ----- Barra de ações da Arquitetura -----
            var medirTitulo = new Label {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaTituloSeccao,
                Text = "MEDIR      Escolha o tipo de medição",
                Padding = new Padding(PaletteTheme.Margem + 3, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = PaletteTheme.FundoSeccao,
                // Explícita, e não herdada. Herdada, ficava escura sobre a
                // faixa escura da secção e o título desaparecia.
                ForeColor = PaletteTheme.Tinta,
                Font = PaletteTheme.TituloSeccao
            };
            // Risco de acento à esquerda — a mesma linguagem do cartão do
            // mockup "claro refinado". Sem isto, CartaoArredondado ficava só
            // uma borda arredondada à volta de uma faixa cinzenta sem marca.
            medirTitulo.Paint += (s, e) =>
            {
                using (var pincel = new SolidBrush(PaletteTheme.Acento))
                    e.Graphics.FillRectangle(pincel, 0, 0, 3, medirTitulo.Height);
            };
            // Seis células iguais, com o fio da grelha entre elas — a
            // composição aprovada. Antes era uma ToolStrip com ícones de 24 px
            // e texto por baixo: sessenta píxeis de altura, botões encostados à
            // esquerda e o resto da faixa vazio. Num painel estreito usado o
            // dia inteiro, isso é espaço que a árvore não tem.
            var dicasMedir = new ToolTip { AutoPopDelay = 15000 };
            var tools = PalettePanelShell.GrelhaDeAccoes(3);

            tools.Controls.Add(PalettePanelShell.Accao("Retângulo",
                IconFactory.ParedeRet(), (s, e) => MedirParedeRet(), dicasMedir, true), 0, 0);
            tools.Controls.Add(PalettePanelShell.Accao("Polyline",
                IconFactory.ParedePoly(), (s, e) => MedirParede(), dicasMedir), 1, 0);
            // Área: para as camadas — betonilhas, enchimentos, impermeabilizações.
            // Desenha-se o contorno, sai o preenchimento, e a medição é a área
            // vezes a altura do painel (que aqui é a espessura da camada).
            tools.Controls.Add(PalettePanelShell.Accao("Área",
                IconFactory.Area(), (s, e) => MedirArea(), dicasMedir), 2, 0);
            // E a mesma medição sobre o que o projecto já traz desenhado: os
            // pavimentos vêm hachurados e os compartimentos fechados, e
            // redesenhar o contorno por cima era trabalho a dobrar.
            tools.Controls.Add(PalettePanelShell.Accao("Área da seleção",
                IconFactory.AreaSel(), (s, e) => MedirAreaSeleccao(), dicasMedir), 0, 1);
            // Havia DOIS botões para isto, com grafias diferentes: um chamava
            // o comando directamente, o outro passava pelo SyncConfig antes.
            // Fica o que sincroniza o painel — o outro media com a altura e a
            // espessura antigas se elas tivessem sido mudadas e ainda não
            // aplicadas, e ninguém perceberia porquê.
            tools.Controls.Add(PalettePanelShell.Accao("Medir seleção",
                IconFactory.MedirSel(), (s, e) => MedirSeleccaoNaPlanta(), dicasMedir), 1, 1);
            tools.Controls.Add(PalettePanelShell.Accao("Adicionar vão",
                IconFactory.Vao(), (s, e) => AdicionarVao(), dicasMedir), 2, 1);

            // Filas 3 e 4: os tipos de medida que viviam nas outras abas.
            //
            // Com um painel só, os comandos delas têm de estar aqui — senão a
            // paleta passa a mostrar contagens e lineares nos resultados e não
            // tem por onde os medir. São geometrias diferentes do mesmo
            // trabalho, não especialidades diferentes.
            // Quatro filas, quatro RowStyle — a GrelhaDeAccoes já trouxe a
            // primeira. Faltava uma: com RowCount=4 e só três RowStyle
            // explícitos, a quarta fila («Contar blocos» / QSELECT /
            // Definições…) ficava sem altura fixa e podia sair mais baixa ou
            // mais alta do que as outras três, partindo a grelha de células
            // iguais que o comentário abaixo promete.
            tools.RowCount = 4;
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, PaletteTheme.AlturaBotaoAccao));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, PaletteTheme.AlturaBotaoAccao));
            tools.RowStyles.Add(new RowStyle(SizeType.Absolute, PaletteTheme.AlturaBotaoAccao));

            tools.Controls.Add(PalettePanelShell.Accao("Pano retângulo",
                IconFactory.Retangulo(), (s, e) => MedirNoutroTipo("TSKRET "), dicasMedir), 0, 2);
            tools.Controls.Add(PalettePanelShell.Accao("Pano × altura",
                IconFactory.Polf(), (s, e) => MedirNoutroTipo("TSKPOLF "), dicasMedir), 1, 2);
            tools.Controls.Add(PalettePanelShell.Accao("Linear",
                IconFactory.Linear(), (s, e) => MedirNoutroTipo("TSKLINEAR "), dicasMedir), 2, 2);
            tools.Controls.Add(PalettePanelShell.Accao("Contar blocos",
                IconFactory.Contagem(), (s, e) => MedirNoutroTipo("TSKCONTAR "), dicasMedir), 0, 3);
            tools.Controls.Add(PalettePanelShell.Accao("QSELECT",
                IconFactory.Qselect(), (s, e) => PaletteHost.RunCommand("_QSELECT "), dicasMedir), 1, 3);
            tools.Controls.Add(PalettePanelShell.Accao("Definições…",
                IconFactory.Definicoes(), (s, e) => AbrirDefinicoesDoTipo(), dicasMedir), 2, 3);

            // ----- Barra de RESULTADOS -----
            //
            // Separada da de MEDIR, e não a seguir a um traço vertical na
            // mesma barra. São duas coisas diferentes: uma dispara comandos
            // sobre o desenho, a outra trata do que já está medido — e
            // misturadas, o «Limpar tudo» ficava a dois botões do «Retângulo».
            // Compacta: ícone pequeno ao LADO do texto, 24 px de altura. São
            // acções secundárias — trabalham sobre o que já está medido — e
            // não merecem o mesmo peso visual dos botões de MEDIR. Com ícones
            // de 20 px e texto por baixo, esta faixa sozinha valia outra secção.
            _barraResultados = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(14, 14),
                Padding = new Padding(6, 1, 6, 1),
                ShowItemToolTips = true,
                RenderMode = ToolStripRenderMode.System,
                BackColor = PaletteTheme.FundoBarra,
                AutoSize = true,
                // ToolStrip nasce fora da ordem de Tab (TabStop = false por
                // omissão): sem isto, "Excel ao Vivo"/"Exportar"/"Atualizar"/
                // "Mais" só se alcançavam com o rato.
                TabStop = true,
                TabIndex = 1
            };

            _btnExcel = BotaoDeBarra("Excel ao Vivo", IconFactory.Excel(), (s, e) => ToggleExcel());
            _barraResultados.Items.Add(_btnExcel);
            _barraResultados.Items.Add(BotaoDeBarra("Exportar", IconFactory.Exportar(),
                (s, e) => Exportar()));

            // UMA acção «Atualizar», não duas.
            //
            // Havia o botão da barra e o ícone do cabeçalho, e não faziam o
            // mesmo: um forçava a escrita imediata no Excel, o outro não. Dois
            // botões com o mesmo nome e comportamentos diferentes é pior do que
            // não ter nenhum — fica o que escreve já, que é o que se espera de
            // quem carrega em «Atualizar» a olhar para a folha.
            _barraResultados.Items.Add(BotaoDeBarra("Atualizar", IconFactory.Atualizar(), (s, e) =>
            {
                PaletteHost.RefreshData();
                PaletteHost.EscreverExcelAgora();   // o botão não espera
            }));

            // ----- Menu «Mais» -----
            //
            // As acções destrutivas e as menos frequentes saem da barra. Ficam
            // a um clique, mas deixam de estar encostadas aos botões de medir,
            // onde o «Limpar tudo» era vizinho do «Retângulo».
            var mais = new ToolStripDropDownButton("Mais")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ToolTipText = "Mais ações sobre os resultados",
                AccessibleName = "Mais ações",
                Alignment = ToolStripItemAlignment.Right
            };
            mais.DropDownItems.Add(ItemDeMenu("Remover", IconFactory.Remover(),
                "Apaga a medição, o vão ou o título escolhido na árvore.",
                () => RemoverParede()));
            mais.DropDownItems.Add(ItemDeMenu("Linha branca", IconFactory.LinhaBranca(),
                "Linha em branco por baixo da medição-alvo.",
                () => AlternarSeparador()));
            // Ficou só o "Artigo". O capítulo e o sub-artigo saíam do mapa de
            // quantidades quando ele existe, e escrevê-los à mão ao lado do
            // mapa dava duas fontes para a mesma coisa — que é como uma folha
            // começa a discordar de si própria.
            mais.DropDownItems.Add(ItemDeMenu("Artigo", IconFactory.Artigo(),
                "Linha de título de artigo por baixo da medição-alvo.",
                () => MarcarTitulo("ART", "Artigo")));
            // Reclassificar: mudar de artigo o que JÁ está medido. Sem isto, um
            // mapa importado a meio da obra só arrumava as medições feitas
            // depois dele — as de antes ficavam sem artigo, saíam no fim da
            // folha, e a única saída era medir tudo outra vez.
            mais.DropDownItems.Add(ItemDeMenu("Reclassificar…", IconFactory.Reclassificar(),
                "Passa as medições escolhidas para outro artigo do mapa.",
                () => Reclassificar()));
            mais.DropDownItems.Add(new ToolStripSeparator());
            mais.DropDownItems.Add(ItemDeMenu("Limpar tudo…", IconFactory.Limpar(),
                "Apaga TODAS as medições de alvenaria do desenho.",
                () => LimparTudo()));
            _barraResultados.Items.Add(mais);
            _barraResultados.AccessibleName = "Ações dos resultados";

            // ----- Grade -----
            _dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = false,   // altura, largura e espessura editáveis na grade
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                // Multi-selecção por causa do «Reclassificar»: um artigo trocado
                // a meio da obra são dezenas de medições, e passá-las uma a uma
                // é onde se desiste e se vai fazer à mão no Excel.
                MultiSelect = true,
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

            // O DataGridView desenha sem duplo buffer por omissão e a
            // propriedade que o liga é protegida — daí a reflexão. Sem isto a
            // grelha pisca a cada reconstrução, e numa obra grande vê-se.
            // É cosmético: se a propriedade não existir num runtime futuro,
            // segue-se sem ela em vez de deitar a paleta abaixo.
            try
            {
                typeof(DataGridView)
                    .GetProperty("DoubleBuffered",
                                 System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(_dgv, true, null);
            }
            catch { }

            AddCol("num", "Nº");
            AddCol("sep", "⏎ / Nível");
            // "servico" e "comp" ficam editáveis ao nível da coluna por causa
            // dos vãos — nome e largura mudam-se ali. Nas linhas de parede
            // continuam bloqueados, mas isso decide-se linha a linha, no
            // CellBeginEdit: o serviço vem do painel e o comprimento do desenho.
            AddCol("servico", "Serviço", editavel: true);
            AddCol("artigo", "Artigo", editavel: true);
            AddCol("alcado", "Alçado");
            AddCol("bloco", "Bloco");
            AddCol("piso", "Piso", editavel: true);
            AddCol("comp", "Compr. (m)", editavel: true, realce: false);
            AddCol("alt", "Altura (m)", editavel: true);
            AddCol("larg", "Largura (m)", editavel: true);
            AddCol("esp", "Espessura (m)", editavel: true);
            AddCol("bruta", "Área Bruta (m²)");
            AddCol("vaos", "Vãos (m²)");
            AddCol("liq", "Área Líq. (m²)");
            // A quantidade que vai faturar, COM a unidade dentro da célula.
            // As colunas de área têm o m² no cabeçalho, e o cabeçalho é igual
            // para todas as linhas — mas uma camada (área em planta × espessura)
            // fatura m³ e enchia na mesma a coluna "Área Líq. (m²)". Quem lia a
            // grelha via um volume rotulado como área. Aqui a unidade viaja com
            // o número, que é a única maneira de estar certa linha a linha.
            AddCol("qtd", "Quantidade");
            AddCol("vol", "Volume (m³)");
            AddCol("aroUn", "Pré-aro (un)");
            AddCol("aroMl", "Pré-aro (m)");

            // Os campos do painel passam para a configuração ASSIM QUE mudam,
            // não só quando se carrega num botão da paleta. Sem isto, escrever
            // TSKMEDSEL ou TSKPAREDE na linha de comando usava a altura e a
            // espessura antigas — mudavam-se no painel e não fazia diferença
            // nenhuma, sem nada que o explicasse.
            EventHandler sincronizar = (s2, e2) =>
            {
                if (_carregando) return;
                SyncConfig();
                // O nome calculado muda com o bloco e com o artigo; mostrá-lo
                // ao vivo é o que evita medir primeiro e descobrir depois.
                MostrarLayerEfectiva();
                // E o resumo da próxima medição, nos dois sítios que o
                // mostram: é ele que evita medir dez paredes para o artigo
                // errado e só dar por isso na folha.
                AtualizarResumoDaProxima();
            };
            _txtServico.TextChanged += sincronizar;
            _txtBloco.TextChanged += sincronizar;
            _txtLayer.TextChanged += sincronizar;
            _cmbAlcado.TextChanged += sincronizar;
            _cmbPiso.TextChanged += sincronizar;
            _numAltura.ValueChanged += sincronizar;
            _numEspessura.ValueChanged += sincronizar;
            _cmbRegra.SelectedIndexChanged += sincronizar;

            _dgv.CellEndEdit += OnCellEndEdit;

            // Clicar numa linha da grelha é uma escolha explícita: a partir daí
            // é ela o alvo, até se voltar a mexer no Excel. Sem isto, o Excel
            // ganhava sempre — porque tem sempre uma célula activa — e a
            // selecção da paleta não servia para nada.
            _dgv.CellClick += (s2, e2) =>
            {
                PaletteHost.MarcarSeleccaoGrelha();

                // Um clique na célula «⊕ classificar…» abre a lista de artigos
                // para AQUELA medição. Só nessas: numa célula que já tem código
                // o clique tem de continuar a servir para a seleccionar e
                // escrever por cima — uma caixa a saltar a cada clique numa
                // coluna editável seria intolerável.
                if (_carregando || e2.RowIndex < 0 || e2.ColumnIndex < 0) return;
                if (_dgv.Columns[e2.ColumnIndex].Name != "artigo") return;

                var cel = _dgv.Rows[e2.RowIndex].Cells[e2.ColumnIndex];
                if ((cel.Value as string) != SemArtigo) return;

                string h = _dgv.Rows[e2.RowIndex].Tag as string;
                if (h != null) ClassificarMedicoes(new List<string> { h });
            };
            _dgv.SelectionChanged += (s2, e2) =>
            {
                if (!_carregando) PaletteHost.MarcarSeleccaoGrelha();
            };
            // Cada tipo de linha edita as suas colunas. Numa parede editam-se
            // serviço, artigo, piso, altura, largura e espessura; num vão, o
            // nome, a largura e a altura. O comprimento nunca — esse vem da
            // geometria, e escrevê-lo aqui punha a folha a mentir sobre o
            // desenho. Para o mudar, mude-se a polyline.
            // Clicar no cabeçalho de Piso ou de Artigo selecciona a COLUNA toda.
            //
            // Tirar o piso a uma obra inteira é o caso normal, não a excepção —
            // e sem isto era preciso ir ao fim de noventa e tal linhas com o
            // Shift carregado, ou descobrir sozinho que o Ctrl+A também serve.
            // Assim são dois gestos: clicar no cabeçalho, carregar em Delete.
            //
            // O cabeçalho não ordena (SortMode = NotSortable), portanto o
            // clique não estava a servir para nada.
            _dgv.ColumnHeaderMouseClick += (s2, e2) =>
            {
                if (_carregando || e2.ColumnIndex < 0) return;
                string col = _dgv.Columns[e2.ColumnIndex].Name;
                if (col != "piso" && col != "artigo") return;

                // O CurrentCell PRIMEIRO: atribuí-lo limpa a selecção, e feito
                // depois do ciclo deixava seleccionada uma célula só.
                DataGridViewCell primeira = null;
                foreach (DataGridViewRow r in _dgv.Rows)
                {
                    if (_linhasTitulo.Contains(r.Index)) continue;
                    primeira = r.Cells[e2.ColumnIndex];
                    break;
                }
                if (primeira == null) return;

                _dgv.CurrentCell = primeira;
                _dgv.ClearSelection();

                int quantas = 0;
                foreach (DataGridViewRow r in _dgv.Rows)
                {
                    if (_linhasTitulo.Contains(r.Index)) continue;
                    r.Cells[e2.ColumnIndex].Selected = true;
                    if (!_linhasVao.Contains(r.Index)) quantas++;
                }

                PaletteHost.Log("coluna " + col + ": " + quantas +
                                " medição(ões) seleccionadas. Delete limpa-as todas.");
            };

            // A tecla Delete limpa a célula de Piso ou de Artigo.
            //
            // O DataGridView não faz NADA com o Delete fora do modo de edição:
            // não é uma tecla que abra a edição (só as imprimíveis e o F2), e o
            // AllowUserToDeleteRows está desligado. Quem seleccionava a coluna
            // Piso e carregava em Delete não via apagar nem via aviso nenhum —
            // gravava o desenho convencido de que tinha limpo, e o PISO 3
            // continuava lá. Não havia como descobrir isto sozinho.
            //
            // Limpa a selecção inteira, que é o gesto que se está a fazer: são
            // dezenas de linhas, não uma.
            _dgv.KeyDown += (s2, e2) =>
            {
                if (e2.KeyCode != Keys.Delete || _carregando) return;
                // Dentro da edição o Delete é do texto, não da célula.
                if (_dgv.IsCurrentCellInEditMode) return;

                var actual = _dgv.CurrentCell;
                if (actual == null) return;

                // Daqui para baixo o Delete responde SEMPRE, nem que seja a
                // dizer que ali não se apaga. Um Delete que não faz nada e não
                // diz nada é o que fez perder uma tarde: apagava-se, gravava-se
                // o desenho, e o valor estava lá na mesma.
                e2.Handled = true;
                e2.SuppressKeyPress = true;

                string col = _dgv.Columns[actual.ColumnIndex].Name;
                if (col != "piso" && col != "artigo")
                {
                    PaletteHost.Log("o Delete limpa as colunas Piso e Artigo. " +
                                    "Ponha o cursor numa delas.");
                    return;
                }

                // Numa linha de título o artigo é o texto do cabeçalho, e
                // apagá-lo daqui não é o mesmo que desclassificar uma medição.
                if (_linhasTitulo.Contains(actual.RowIndex))
                {
                    PaletteHost.Log("esta linha é um título — o texto dele " +
                                    "escreve-se no Excel, não se apaga aqui.");
                    return;
                }

                var alvos = HandlesSeleccionados();
                if (alvos.Count == 0)
                {
                    PaletteHost.Log("nenhuma medição seleccionada.");
                    return;
                }

                if (col == "piso")
                    Relatar(AlvRepo.DefinirPisoEmVarias(alvos, ""), alvos.Count,
                            "piso (removido)");
                else
                    Relatar(AlvRepo.DefinirArtigoEmVarias(alvos, ""), alvos.Count,
                            "artigo (removido)");

                AplicarNaFolha();
            };

            _dgv.CellBeginEdit += (s2, e2) =>
            {
                string col = _dgv.Columns[e2.ColumnIndex].Name;
                if (_linhasTitulo.Contains(e2.RowIndex))
                {
                    // Só o código. O texto aparece cortado, e deixar gravar o
                    // que está à vista apagaria o resto do artigo — escreve-se
                    // no Excel ou nos campos do painel.
                    if (col != "artigo") e2.Cancel = true;
                }
                else if (_linhasVao.Contains(e2.RowIndex))
                {
                    if (col != "servico" && col != "comp" && col != "alt")
                        e2.Cancel = true;
                }
                else if (col == "comp") e2.Cancel = true;   // vem do desenho

                // O aviso «sem artigo» sai da frente assim que se começa a
                // escrever: obrigar a apagá-lo antes de pôr o código era
                // transformar um aviso em estorvo.
                if (!e2.Cancel && col == "artigo")
                {
                    var cel = _dgv.Rows[e2.RowIndex].Cells[e2.ColumnIndex];
                    if ((cel.Value as string) == SemArtigo) cel.Value = "";
                }
            };

            // ----- Resultados compactos e propriedades -----
            CriarResultadosCompactos();

            // ----- Totais (rodapé da Arquitetura) -----
            // A barra de estado, no fundo — com a cor do cabeçalho, para
            // fechar o painel entre duas faixas da mesma família. Cinzenta e
            // com o tipo de letra do sistema, lia-se como uma sobra do
            // formulário; é a linha que responde à pergunta com que se abre a
            // paleta, e tem de se ler como uma resposta.
            _lblTotais = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PaletteTheme.Pequeno,
                ForeColor = PaletteTheme.Tinta,
                BackColor = PaletteTheme.AzulTopo,
                Padding = new Padding(PaletteTheme.Margem, 0, PaletteTheme.Margem, 0),
                AutoEllipsis = true,
                AccessibleName = "Totais"
            };

            Controls.Add(_resultadosCompactos);
            // A grelha larga já não entra no painel: a árvore compacta é a
            // vista de resultados. Fica declarada até a limpeza da Fase 8
            // levar os ajudantes que só ela usava.
            // Controls.Add(_dgv);
            Controls.Add(_lblTotais);
            Controls.Add(PalettePanelShell.CartaoArredondado(medirTitulo, tools));
            Controls.Add(config);
            Controls.Add(configHost);
            Controls.Add(cabecalho);

            AtualizarResumoDaProxima();
            PrepararInteraccao();
        }

        private void PrepararInteraccao()
        {
            var dicas = new ToolTip { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 100 };
            BackColor = PaletteTheme.Fundo;
            ForeColor = PaletteTheme.Tinta;
            PaletteTheme.AplicarTema(this);
            PaletteTheme.PrepararInteraccao(this, dicas);
            if (_dgvCompacto != null)
            {
                _dgvCompacto.AccessibleDescription = "Árvore de resultados. Use as setas para navegar, Enter para expandir ou ativar e Espaço para expandir.";
                dicas.SetToolTip(_dgvCompacto, "Resultados. Use as setas, Enter ou Espaço.");
            }
            if (_mosaico != null)
            {
                _mosaico.AccessibleName = "Propriedades";
                _mosaico.AccessibleDescription =
                    "Use as setas para escolher uma medida e Enter ou Espaço para editar a sublinhada.";
                dicas.SetToolTip(_mosaico,
                    "Medidas do resultado escolhido. As sublinhadas editam-se ao clique ou por teclado.");
                // PrepararInteraccao (acima) desce a `propriedades` e só
                // encontra o `_editor` escondido lá dentro — o mosaico em si
                // é um Panel pintado à mão, não um dos tipos que a busca
                // reconhece como alvo de foco. Mesmo tratamento manual do
                // resto: o mesmo contorno de acento (PaletteTheme.ComFoco)
                // usado em todos os outros controlos principais.
                PaletteTheme.ComFoco(_mosaico);
            }
        }

        private void CriarResultadosCompactos()
        {
            // Fill, e não Bottom: com a grelha larga fora do painel, é a árvore
            // que ocupa o espaço que sobra. Em Bottom ficava uma faixa vazia
            // do tamanho da grelha que já lá não está.
            _resultadosCompactos = new Panel { Dock = DockStyle.Fill, BackColor = PaletteTheme.Fundo };
            // TabIndex explícito nos quatro filhos directos, na ordem em que se
            // LEEM (pesquisa/filtros, depois as acções, depois a árvore, depois
            // propriedades). Sem isto, o Tab seguia a ordem de Controls.Add, que
            // para Dock=Top é o INVERSO da ordem visual — ver o comentário mais
            // abaixo sobre "o último a entrar fica mais acima". Resultado: quem
            // navegava por teclado entrava pela árvore, não pela pesquisa.
            var barra = new Panel { Dock = DockStyle.Top, Height = PaletteTheme.AlturaBarra, BackColor = PaletteTheme.FundoSeccao, TabIndex = 0 };
            var titulo = new Label { Dock = DockStyle.Left, Width = 95, Text = "RESULTADOS", Padding = new Padding(8, 0, 0, 0), TextAlign = ContentAlignment.MiddleLeft, Font = PaletteTheme.TituloSeccao };
            _lblResultadoResumo = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, Padding = new Padding(0, 0, 8, 0), ForeColor = PaletteTheme.Apagado };
            // «Limpar» apaga pesquisa E filtros — as duas coisas que escondem
            // resultados. As recolhas ficam onde estavam: a vista a que se
            // volta tem de ser a de antes de procurar, não uma árvore toda
            // aberta que ninguém pediu.
            _btnLimparFiltros = new Button
            {
                Dock = DockStyle.Right, Width = 56, Text = "Limpar",
                FlatStyle = FlatStyle.Flat, Font = PaletteTheme.Pequeno,
                Visible = false,
                BackColor = PaletteTheme.FundoSeccao, ForeColor = PaletteTheme.Apagado,
                AccessibleName = "Limpar pesquisa e filtros"
            };
            _btnLimparFiltros.FlatAppearance.BorderSize = 0;
            _btnLimparFiltros.Click += (s, e) =>
            {
                _estadoResultados.LimparVista();
                _carregando = true;
                try { _txtPesquisa.Text = ""; } finally { _carregando = false; }
                AtualizarResultadosCompactos(_raizResultados);
            };

            _btnFiltros = new Button
            {
                Dock = DockStyle.Right, Width = 66, Text = "Filtros",
                FlatStyle = FlatStyle.Flat, Font = PaletteTheme.Pequeno,
                BackColor = PaletteTheme.Fundo, ForeColor = PaletteTheme.Tinta,
                UseVisualStyleBackColor = false,
                AccessibleName = "Filtros"
            };
            _btnFiltros.Click += (s, e) => AbrirFiltros();

            _txtPesquisa = new TextBox {
                Dock = DockStyle.Right, Width = 130, TabStop = true,
                AccessibleName = "Pesquisar resultados",
                // Explícito, e não herdado: uma TextBox nasce branca, e num
                // painel grafite isso é um buraco de luz.
                BackColor = PaletteTheme.FundoCampo,
                ForeColor = PaletteTheme.Tinta,
                BorderStyle = BorderStyle.FixedSingle
            };
            PaletteTheme.TextoDeSugestao(_txtPesquisa, "Pesquisar…");

            // Debounce: a árvore não se refaz a cada tecla.
            //
            // Escrever "alvenaria" são nove reconstruções da lista inteira, e
            // num desenho grande sente-se o painel a arrastar-se atrás de quem
            // escreve. Com a pausa, escreve-se a palavra toda e a lista muda
            // uma vez.
            _adiarPesquisa = new Timer { Interval = 220 };
            _adiarPesquisa.Tick += (s, e) =>
            {
                _adiarPesquisa.Stop();
                _estadoResultados.Pesquisa = _txtPesquisa.Text;
                AtualizarResultadosCompactos(_raizResultados);
            };
            _txtPesquisa.TextChanged += (s, e) =>
            {
                if (_carregando) return;
                _adiarPesquisa.Stop();
                _adiarPesquisa.Start();
            };
            // Enter não espera pela pausa: quem carrega em Enter já decidiu.
            _txtPesquisa.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    _adiarPesquisa.Stop();
                    _estadoResultados.Pesquisa = _txtPesquisa.Text;
                    AtualizarResultadosCompactos(_raizResultados);
                    e.Handled = e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _txtPesquisa.Text = "";
                    e.Handled = e.SuppressKeyPress = true;
                }
            };

            // Os chips do que está filtrado. Até dois: com mais, a barra
            // encolhe a pesquisa e deixa de caber nada.
            _lblChips = new Label
            {
                Dock = DockStyle.Right,
                AutoSize = false,
                Width = 0,
                TextAlign = ContentAlignment.MiddleRight,
                Font = PaletteTheme.Pequeno,
                ForeColor = PaletteTheme.AcentoEscuro,
                AutoEllipsis = true
            };
            barra.Controls.Add(_lblResultadoResumo);
            barra.Controls.Add(_lblChips);
            barra.Controls.Add(_btnLimparFiltros);
            barra.Controls.Add(_btnFiltros);
            barra.Controls.Add(_txtPesquisa);
            barra.Controls.Add(titulo);

            _dgvCompacto = new DataGridView {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                RowHeadersVisible = false,
                TabIndex = 2,
                // Multi-selecção por causa do «Reclassificar» e da edição em
                // lote: um artigo trocado a meio da obra são dezenas de
                // medições, e passá-las uma a uma é onde se desiste e se vai
                // fazer à mão no Excel. Ctrl escolhe soltas, Shift um intervalo.
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                AllowUserToResizeRows = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                BackgroundColor = PaletteTheme.Fundo,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                Font = PaletteTheme.Normal,
                AccessibleName = "Resultados",
                TabStop = true
            };

            // QUATRO colunas: Estrutura/elemento, Comp., Altura e Quantidade.
            //
            // Uma versão anterior tinha só duas — Estrutura e Quantidade —
            // para não repetir o erro do brief original: com colunas fixas
            // para cinco grandezas, a contagem de 2 portas aparecia debaixo
            // de "Comp." e o comprimento de uma parede lia-se como se fosse
            // a medição dela. A resolução (2026-09-05) não é tirar as
            // colunas, é fazer a UNIDADE VIAJAR DENTRO DA CÉLULA: numa
            // camada, "Comp." mostra a área em planta com "m²" ao lado, e a
            // coluna que não se aplica mostra "—", nunca um zero — ver
            // NoResultado.TextoComprimento/TextoAltura. As dimensões
            // completas continuam em PROPRIEDADES; aqui é só o atalho.
            var colEstrutura = new DataGridViewTextBoxColumn
            {
                Name = "estrutura",
                HeaderText = "Estrutura / elemento",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 100,
                MinimumWidth = 150
            };
            var colComp = new DataGridViewTextBoxColumn
            {
                Name = "comp",
                HeaderText = "Comp.",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 72,
                MinimumWidth = 56
            };
            var colAltura = new DataGridViewTextBoxColumn
            {
                Name = "altura",
                HeaderText = "Altura",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 72,
                MinimumWidth = 56
            };
            colComp.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colAltura.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colComp.DefaultCellStyle.ForeColor = PaletteTheme.Apagado;
            colAltura.DefaultCellStyle.ForeColor = PaletteTheme.Apagado;
            var colQuantidade = new DataGridViewTextBoxColumn
            {
                Name = "quantidade",
                HeaderText = "Quantidade",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
                Width = 132,
                MinimumWidth = 96
            };
            colQuantidade.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            _dgvCompacto.Columns.Add(colEstrutura);
            _dgvCompacto.Columns.Add(colComp);
            _dgvCompacto.Columns.Add(colAltura);
            _dgvCompacto.Columns.Add(colQuantidade);

            // O DataGridView desenha sem duplo buffer e a propriedade que o liga
            // é protegida — daí a reflexão. Sem isto a árvore pisca a cada
            // medição, e numa obra grande vê-se.
            try
            {
                typeof(DataGridView)
                    .GetProperty("DoubleBuffered",
                                 System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(_dgvCompacto, true, null);
            }
            catch { }

            // A indentação, as guias, o ícone do nó e o [▸]/[▾] são desenhados.
            // Com espaços não funcionava: a letra é proporcional, e níveis
            // diferentes acabavam alinhados uns com os outros.
            _dgvCompacto.CellPainting += DesenharCelulaDaArvore;

            // Clicar na zona do [▸]/[▾] abre e fecha. É a área à esquerda do
            // ícone, calculada a partir da profundidade — a mesma conta que o
            // desenho usa, para o alvo do rato ser exactamente o que se vê.
            _dgvCompacto.CellMouseDown += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var no = _dgvCompacto.Rows[e.RowIndex].Tag as NoResultado;
                if (no == null || no.Filhos.Count == 0) return;
                if (e.X > RecuoDoNo(no) + PaletteTheme.LarguraTwisty) return;

                _estadoResultados.Alternar(no.Id);
                AtualizarResultadosCompactos(_raizResultados);
            };

            _dgvCompacto.SelectionChanged += (s, e) =>
            {
                if (_carregando) return;
                var no = _dgvCompacto.CurrentRow == null
                    ? null : _dgvCompacto.CurrentRow.Tag as NoResultado;
                // Seleccionar consulta; não muda a próxima medição. Só o
                // "Medir aqui" faz isso, e só em nós de artigo.
                _estadoResultados.Seleccionado = no == null ? null : no.Id;
                AtualizarPropriedadesCompactas();
            };
            _dgvCompacto.CellClick += (s, e) => {
                AtualizarPropriedadesCompactas();
                var no = e.RowIndex < 0 ? null : _dgvCompacto.Rows[e.RowIndex].Tag as NoResultado;
                if (no != null && no.Handle != null) PaletteHost.MarcarSeleccaoGrelha();
            };
            _dgvCompacto.CellDoubleClick += (s, e) => {
                if (e.RowIndex < 0) return;
                var no = _dgvCompacto.Rows[e.RowIndex].Tag as NoResultado;
                if (no == null || !no.EhGrupo) return;
                _estadoResultados.Alternar(no.Id);
                AtualizarResultadosCompactos(_raizResultados);
            };
            // ----- Teclado -----
            //
            // As setas verticais, o Ctrl e o Shift já são do DataGridView. O
            // que falta é o que faz de uma lista uma ÁRVORE: Left/Right a
            // fechar e abrir, Home/End aos extremos, Enter/Espaço a activar.
            // Sem isto, quem navega por teclado chegava a um grupo fechado e
            // não tinha como o abrir.
            _dgvCompacto.KeyDown += (s, e) =>
            {
                var no = NoSeleccionado();

                switch (e.KeyCode)
                {
                    case Keys.Enter:
                    case Keys.Space:
                        if (no == null) return;
                        if (no.Filhos.Count > 0)
                        {
                            _estadoResultados.Alternar(no.Id);
                            AtualizarResultadosCompactos(_raizResultados);
                        }
                        else if (no.PermiteMedirAqui) MedirAqui();
                        e.Handled = e.SuppressKeyPress = true;
                        return;

                    case Keys.Left:
                        // Fechado já, sobe ao pai — é o que se espera de uma
                        // árvore: o Left leva sempre para "menos fundo".
                        if (no == null) return;
                        if (no.Filhos.Count > 0 && _estadoResultados.Expandido(no.Id))
                        {
                            _estadoResultados.Recolher(no.Id);
                            AtualizarResultadosCompactos(_raizResultados);
                        }
                        else if (no.Pai != null) SeleccionarNo(no.Pai.Id);
                        e.Handled = e.SuppressKeyPress = true;
                        return;

                    case Keys.Right:
                        if (no == null || no.Filhos.Count == 0) return;
                        if (!_estadoResultados.Expandido(no.Id))
                        {
                            _estadoResultados.Expandir(no.Id);
                            AtualizarResultadosCompactos(_raizResultados);
                        }
                        else SeleccionarNo(no.Filhos[0].Id);
                        e.Handled = e.SuppressKeyPress = true;
                        return;

                    case Keys.Home:
                    case Keys.End:
                        if (_dgvCompacto.Rows.Count == 0) return;
                        int alvo = e.KeyCode == Keys.Home ? 0 : _dgvCompacto.Rows.Count - 1;
                        _dgvCompacto.CurrentCell = _dgvCompacto.Rows[alvo].Cells[0];
                        e.Handled = e.SuppressKeyPress = true;
                        return;
                }
            };

            var propriedades = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 96,
                BackColor = PaletteTheme.Fundo,
                TabIndex = 3
            };

            var cabecalhoProps = new Panel
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaTituloSeccao,
                BackColor = PaletteTheme.FundoSeccao,
                // Mesma razão do TabIndex dos quatro painéis de RESULTADOS:
                // sem isto, a ordem de Tab dentro de PROPRIEDADES ficava ao
                // sabor do empate entre `cabecalhoProps` e `_mosaico` (os
                // dois nascem com TabIndex 0). Cabeçalho primeiro, mosaico
                // a seguir — a ordem em que se lêem.
                TabIndex = 0
            };

            // Essenciais / Tudo. As propriedades de uma parede são dezassete e
            // o painel tem três linhas de altura: sem este interruptor, ou se
            // mostra tudo e não se lê nada, ou se escolhe por alguém o que
            // interessa. Assim o normal é curto e o resto está a um clique.
            _btnPropriedadesModo = new Button
            {
                Dock = DockStyle.Right,
                Width = 76,
                Text = "Essenciais",
                FlatStyle = FlatStyle.Flat,
                Font = PaletteTheme.Pequeno,
                BackColor = PaletteTheme.Palido,
                ForeColor = PaletteTheme.Acento,
                AccessibleName = "Detalhe das propriedades"
            };
            _btnPropriedadesModo.FlatAppearance.BorderSize = 0;
            _btnPropriedadesModo.Click += (s, e) =>
            {
                _propriedadesEssenciais = !_propriedadesEssenciais;
                _btnPropriedadesModo.Text = _propriedadesEssenciais ? "Essenciais" : "Tudo";
                _btnPropriedadesModo.BackColor = _propriedadesEssenciais
                    ? PaletteTheme.Palido : PaletteTheme.Fundo;
                _btnPropriedadesModo.ForeColor = _propriedadesEssenciais
                    ? PaletteTheme.Acento : PaletteTheme.Apagado;
                AtualizarPropriedadesCompactas();
            };

            // "Medir aqui" só existe em nós de artigo, e é a ÚNICA operação
            // que muda a próxima medição. Nasce escondido: aparece quando o nó
            // seleccionado o permite.
            _btnMedirAqui = new Button
            {
                Dock = DockStyle.Right,
                Width = 82,
                Text = "Medir aqui",
                FlatStyle = FlatStyle.Flat,
                Font = PaletteTheme.PequenoNegrito,
                ForeColor = PaletteTheme.Acento,
                BackColor = PaletteTheme.FundoSeccao,
                Visible = false,
                AccessibleName = "Medir aqui",
                AccessibleDescription =
                    "Copia o piso, o serviço e o artigo deste nó para a próxima medição."
            };
            _btnMedirAqui.FlatAppearance.BorderSize = 0;
            _btnMedirAqui.Click += (s, e) => MedirAqui();

            // PROPRIEDADES recolhe-se como CONFIGURAÇÃO: num painel a 480 px,
            // o mosaico de métricas é espaço que a árvore não tem, e quem só
            // quer conferir totais não precisa dele aberto.
            _lblPropriedadeTitulo = new Button
            {
                Dock = DockStyle.Fill,
                Text = "▼  PROPRIEDADES  —  nada selecionado",
                FlatStyle = FlatStyle.Flat,
                Padding = new Padding(PaletteTheme.Margem, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PaletteTheme.TituloSeccao,
                ForeColor = PaletteTheme.Tinta,
                BackColor = PaletteTheme.FundoSeccao,
                AutoEllipsis = true,
                AccessibleName = "Propriedades"
            };
            _lblPropriedadeTitulo.FlatAppearance.BorderSize = 0;
            _lblPropriedadeTitulo.Click += (s, e) =>
            {
                _propriedadesAbertas = !_propriedadesAbertas;
                AtualizarPropriedadesCompactas();
            };

            cabecalhoProps.Controls.Add(_lblPropriedadeTitulo);
            cabecalhoProps.Controls.Add(_btnMedirAqui);
            cabecalhoProps.Controls.Add(_btnPropriedadesModo);

            // As propriedades como MOSAICOS, e não como lista de duas colunas.
            //
            // O que se lê aqui são as medidas de que se desconfia — o
            // comprimento, a altura, a área bruta, a líquida, o volume. Em
            // lista, é preciso percorrer; em mosaico, lêem-se de uma passagem.
            // Os que se editam trazem sublinhado tracejado e abrem ao clique.
            _mosaico = new PalettePanelShell.MosaicoMetricas { TabIndex = 1 };
            _mosaico.Editado += AoEditarMetrica;

            propriedades.Controls.Add(_mosaico);
            propriedades.Controls.Add(cabecalhoProps);

            // O estado vazio é uma etiqueta por cima da árvore, e não uma
            // linha dentro dela: uma "linha" a dizer que não há linhas seria
            // seleccionável, contaria para os totais e podia ser alvo de uma
            // acção.
            _lblVazio = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = PaletteTheme.Normal,
                ForeColor = PaletteTheme.Apagado,
                BackColor = PaletteTheme.Fundo,
                Visible = false,
                AccessibleName = "Sem resultados"
            };

            _resultadosCompactos.Controls.Add(_lblVazio);
            _resultadosCompactos.Controls.Add(_dgvCompacto);
            _resultadosCompactos.Controls.Add(propriedades);
            // Num Dock=Top, o último a entrar fica mais acima: a barra de
            // pesquisa por cima, e as acções sobre os resultados por baixo dela.
            if (_barraResultados != null) _resultadosCompactos.Controls.Add(_barraResultados);
            _resultadosCompactos.Controls.Add(barra);
        }

        /// <summary>
        /// Reconstrói a árvore de resultados a partir do modelo.
        ///
        /// A SELECÇÃO E O SCROLL VOLTAM PELO ID, NÃO PELO ÍNDICE DA LINHA.
        /// Esta lista é refeita a cada medição, e os índices mudam todos: com
        /// o índice, quem estava numa parede a meio da obra dava por si noutra
        /// qualquer, e as acções contextuais iam atrás. O Id de uma medição é
        /// o handle dela — sobrevive a reordenar, a filtrar e a reclassificar.
        /// </summary>
        private void AtualizarResultadosCompactos(NoResultado raiz)
        {
            if (_dgvCompacto == null) return;
            _raizResultados = raiz;

            // Onde estávamos, ANTES de deitar a lista abaixo.
            string idSeleccionado = _estadoResultados.Seleccionado;
            string idNoTopo = null;
            try
            {
                int topo = _dgvCompacto.FirstDisplayedScrollingRowIndex;
                if (topo >= 0 && topo < _dgvCompacto.Rows.Count)
                {
                    var noTopo = _dgvCompacto.Rows[topo].Tag as NoResultado;
                    if (noTopo != null) idNoTopo = noTopo.Id;
                }
            }
            catch { }

            // Acabou de se medir: o cursor vai para a medição nova. É ela o
            // alvo do vão e do título que venham a seguir.
            string nova = PaletteHost.MedicaoNova;
            bool seguirNova = false;
            if (!string.IsNullOrEmpty(nova) && raiz != null
                && ResultadosArvore.Handles(raiz).Contains(nova))
            {
                idSeleccionado = "M:" + nova;
                seguirNova = true;
            }

            _dgvCompacto.SuspendLayout();
            _dgvCompacto.ClearSelection();
            _dgvCompacto.Rows.Clear();

            if (raiz == null)
            {
                _lblResultadoResumo.Text = "0 medições";
                _dgvCompacto.ResumeLayout();
                ReporSeleccaoDaArvore(idSeleccionado, -1);
                AtualizarPropriedadesCompactas();
                return;
            }

            var vista = ResultadosArvore.Projetar(raiz, _estadoResultados);

            var linhas = new List<DataGridViewRow>();
            foreach (var item in vista.Nos)
            {
                var no = item.No;
                var linha = new DataGridViewRow();
                linha.CreateCells(_dgvCompacto,
                    no.Rotulo,
                    no.TextoComprimento(CultureInfo.CurrentCulture),
                    no.TextoAltura(CultureInfo.CurrentCulture),
                    item.Quantidades == null ? "" : item.Quantidades.Texto(CultureInfo.CurrentCulture));
                linha.Tag = no;
                linha.Height = PaletteTheme.AlturaLinha;

                if (no.EhGrupo)
                {
                    linha.DefaultCellStyle.BackColor = PaletteTheme.FundoGrupo;
                    linha.DefaultCellStyle.Font = PaletteTheme.Negrito;
                }
                if (no.Tipo == TipoNo.Vao)
                {
                    // Vermelho, como as deduções no Excel: o que desconta
                    // lê-se à primeira, sem ter de reparar no sinal.
                    linha.DefaultCellStyle.ForeColor = PaletteTheme.VermelhoDeducao;
                    linha.DefaultCellStyle.Font = PaletteTheme.Italico;
                }
                if (no.Tipo == TipoNo.Titulo)
                    linha.DefaultCellStyle.ForeColor = PaletteTheme.Apagado;

                // Os alertas não podem depender só da cor: em alto contraste
                // ela desaparece, e há quem não a distinga. O texto da dica e
                // a descrição acessível dizem-no por palavras.
                if (no.Alertas != AlertaNo.Nenhum)
                {
                    string aviso = DescreverAlertas(no.Alertas);
                    linha.Cells[0].ToolTipText = aviso;
                    if ((no.Alertas & AlertaNo.PorClassificar) != 0 && !no.EhGrupo)
                    {
                        linha.DefaultCellStyle.BackColor = PaletteTheme.FundoPorClassificar;
                        linha.DefaultCellStyle.ForeColor = PaletteTheme.TextoPorClassificar;
                    }
                    if ((no.Alertas & AlertaNo.VaosExcessivos) != 0 && no.Tipo == TipoNo.Medicao)
                        linha.DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 224);
                }

                if (item.Correspondeu && vista.AFiltrar)
                    linha.DefaultCellStyle.BackColor = PaletteTheme.Palido;

                linhas.Add(linha);
            }

            // Uma entrega só. Um Rows.Add por linha faz o DataGridView
            // reajustar-se a cada uma — 152 ms contra 20 ms numa lista desta
            // dimensão, medido na grelha antiga.
            if (linhas.Count > 0) _dgvCompacto.Rows.AddRange(linhas.ToArray());

            _lblResultadoResumo.Text = vista.Resumo(CultureInfo.CurrentCulture);
            AtualizarBarraDeFiltros();

            // Estado vazio: dizer POR QUE está vazio e o que fazer a seguir.
            //
            // Uma lista em branco lê-se como "o plugin perdeu as medições". A
            // diferença entre "não há nada medido" e "o filtro escondeu tudo"
            // é toda, e é a segunda que assusta quem acabou de medir a manhã.
            if (vista.Vazia)
            {
                _lblVazio.Text = _estadoResultados.AFiltrar
                    ? "Nenhum resultado corresponde à pesquisa ou aos filtros.\r\n" +
                      "As " + vista.MedicoesTotais + " medições continuam no desenho — " +
                      "carregue em «Limpar» para as ver todas."
                    : "Ainda não há medições de alvenaria neste desenho.\r\n" +
                      "Use os botões de MEDIR para começar.";
                // A árvore SAI de cena enquanto o aviso está à vista.
                //
                // Os dois são Dock=Fill no mesmo painel, e com dois Fill ao
                // mesmo tempo quem fica com o espaço depende da ordem-z — que
                // é frágil e muda com um BringToFront de outro sítio qualquer.
                // Um de cada vez não tem como correr mal.
                _dgvCompacto.Visible = false;
                _lblVazio.Visible = true;
                _lblVazio.BringToFront();
            }
            else
            {
                _lblVazio.Visible = false;
                _dgvCompacto.Visible = true;
            }

            ReporSeleccaoDaArvore(idSeleccionado, seguirNova ? -1 : IndiceDe(idNoTopo));
            _dgvCompacto.ResumeLayout();
            AtualizarPropriedadesCompactas();
        }

        /// <summary>
        /// Abre o painel de filtros por baixo do botão que o pediu.
        ///
        /// O rascunho vive dentro do popup: enquanto ele estiver aberto, a
        /// árvore não muda. Só o `Aplicar` a refaz, e uma vez só.
        /// </summary>
        private void AbrirFiltros()
        {
            if (_raizResultados == null)
            {
                PaletteHost.Log("Não há resultados para filtrar.");
                return;
            }

            var popup = new FiltrosPopup(_raizResultados, _estadoResultados.Filtro,
                novo =>
                {
                    _estadoResultados.Filtro = novo;
                    AtualizarResultadosCompactos(_raizResultados);
                });

            // Devolver o foco a quem abriu, ao fechar. Sem isto o foco ficava
            // preso num painel que já não existe e a tecla seguinte não ia
            // para lado nenhum.
            popup.Closed += (s, e) =>
            {
                try { _btnFiltros.Focus(); } catch { }
            };
            popup.Show(_btnFiltros, new Point(0, _btnFiltros.Height));
        }

        /// <summary>
        /// O badge, os chips e o estado do «Limpar».
        ///
        /// O badge conta GRUPOS de filtro, não valores: escolher três pisos é
        /// um filtro, não três. Dizer "3" a quem filtrou por um critério era
        /// sugerir que havia mais dois escondidos algures.
        /// </summary>
        private void AtualizarBarraDeFiltros()
        {
            var filtro = _estadoResultados.Filtro;
            int n = filtro.Contagem;

            _btnFiltros.Text = n > 0 ? "Filtros (" + n + ")" : "Filtros";
            _btnFiltros.Font = n > 0 ? PaletteTheme.PequenoNegrito : PaletteTheme.Pequeno;
            _btnFiltros.BackColor = n > 0 ? PaletteTheme.Palido : PaletteTheme.Fundo;
            _btnFiltros.ForeColor = n > 0 ? PaletteTheme.Acento : PaletteTheme.Tinta;
            _btnFiltros.FlatAppearance.BorderColor = n > 0
                ? PaletteTheme.Acento : PaletteTheme.BordaCampo;
            _btnFiltros.AccessibleDescription = n == 0
                ? "Nenhum filtro aplicado."
                : n + " filtro(s) aplicado(s): " + string.Join("; ", filtro.Chips().ToArray());

            // Até dois chips. Com mais, a barra encolhe a caixa de pesquisa e
            // deixa de caber nada — e o badge já diz quantos são.
            var chips = filtro.Chips();
            if (chips.Count == 0)
            {
                _lblChips.Text = "";
                _lblChips.Width = 0;
            }
            else
            {
                var mostrar = new List<string>();
                for (int i = 0; i < chips.Count && i < 2; i++) mostrar.Add(chips[i]);
                if (chips.Count > 2) mostrar.Add("+" + (chips.Count - 2));

                _lblChips.Text = string.Join("  ·  ", mostrar.ToArray());
                _lblChips.Width = 190;
                _lblChips.AccessibleDescription = string.Join("; ", chips.ToArray());
            }

            // ESCONDIDO, e não desactivado.
            //
            // Um botão flat desactivado é pintado pelo Windows com um cinzento
            // que ele calcula sozinho — e sobre grafite esse cinzento fica
            // indistinguível do fundo. Lia-se como um defeito, não como uma
            // acção indisponível. Sem nada para limpar, o botão simplesmente
            // não existe.
            _btnLimparFiltros.Visible = _estadoResultados.AFiltrar;
        }

        /// <summary>A linha em que este nó está agora, ou -1 se saiu da vista.</summary>
        private int IndiceDe(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _dgvCompacto.Rows.Count; i++)
            {
                var no = _dgvCompacto.Rows[i].Tag as NoResultado;
                if (no != null && no.Id == id) return i;
            }
            return -1;
        }

        /// <summary>
        /// Volta a pôr o cursor no mesmo NÓ e a lista onde estava.
        ///
        /// Se o nó já não está visível — foi apagado, ou um filtro escondeu-o —
        /// a selecção é LIMPA, e não empurrada para o vizinho. Uma selecção
        /// invisível é pior do que nenhuma: as acções contextuais continuavam
        /// apontadas a uma medição que quem carrega no botão não está a ver.
        /// </summary>
        private void ReporSeleccaoDaArvore(string id, int scroll)
        {
            try
            {
                int linha = IndiceDe(id);
                if (linha >= 0)
                {
                    _dgvCompacto.CurrentCell = _dgvCompacto.Rows[linha].Cells[0];
                    _dgvCompacto.Rows[linha].Selected = true;
                    _estadoResultados.Seleccionado = id;
                }
                else
                {
                    _dgvCompacto.ClearSelection();
                    _dgvCompacto.CurrentCell = null;
                    _estadoResultados.Seleccionado = null;
                }

                if (scroll >= 0 && scroll < _dgvCompacto.Rows.Count)
                    _dgvCompacto.FirstDisplayedScrollingRowIndex = scroll;
            }
            catch { /* a lista pode ter encolhido: fica onde está */ }
        }

        /// <summary>Os alertas de um nó, por palavras.</summary>
        private static string DescreverAlertas(AlertaNo alertas)
        {
            var partes = new List<string>();
            if ((alertas & AlertaNo.PorClassificar) != 0)
                partes.Add("Por classificar — sai no fim da folha.");
            if ((alertas & AlertaNo.ArtigoDesconhecido) != 0)
                partes.Add("O mapa não conhece este artigo — sai no fim da folha.");
            if ((alertas & AlertaNo.VaosExcessivos) != 0)
                partes.Add("Os vãos descontam mais do que a parede tem.");
            return string.Join("\n", partes.ToArray());
        }

        /// <summary>O recuo, em píxeis, a que o conteúdo deste nó começa.</summary>
        private static int RecuoDoNo(NoResultado no)
        {
            return PaletteTheme.MargemPequena + no.Profundidade * PaletteTheme.RecuoPorNivel;
        }

        /// <summary>
        /// Desenha a coluna da estrutura: guias, [▸]/[▾], ícone do nó e texto.
        ///
        /// À mão porque o DataGridView não tem hierarquia nenhuma — e com
        /// espaços não dava: a letra é proporcional, e o nível 3 acabava
        /// alinhado com o 2 consoante o texto que estivesse por cima.
        /// </summary>
        private void DesenharCelulaDaArvore(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var no = _dgvCompacto.Rows[e.RowIndex].Tag as NoResultado;
            if (no == null) return;

            e.PaintBackground(e.CellBounds, true);

            var g = e.Graphics;
            int recuo = RecuoDoNo(no);
            Color tinta = (e.State & DataGridViewElementStates.Selected) != 0
                ? PaletteTheme.TextoSeleccionado
                : (e.CellStyle.ForeColor.IsEmpty ? PaletteTheme.Tinta : e.CellStyle.ForeColor);

            // Guias verticais, uma por nível acima deste.
            using (var caneta = new Pen(PaletteTheme.Guia))
                for (int n = 1; n <= no.Profundidade; n++)
                {
                    int x = e.CellBounds.X + PaletteTheme.MargemPequena +
                            (n - 1) * PaletteTheme.RecuoPorNivel + 6;
                    g.DrawLine(caneta, x, e.CellBounds.Top, x, e.CellBounds.Bottom);
                }

            int xConteudo = e.CellBounds.X + recuo;

            // O [▸]/[▾], só onde há filhos para mostrar.
            if (no.Filhos.Count > 0)
            {
                bool aberto = _estadoResultados.AFiltrar || _estadoResultados.Expandido(no.Id);
                using (var pincel = new SolidBrush(PaletteTheme.Acento))
                    g.DrawString(aberto ? "▾" : "▸", PaletteTheme.Pequeno, pincel,
                                 xConteudo, e.CellBounds.Y + 4);
            }
            xConteudo += PaletteTheme.LarguraTwisty;

            // Um quadrado da cor do tipo do nó, à falta de ícones vectoriais
            // para seis tipos. Diz o mesmo — a que família a linha pertence —
            // e não custa um recurso do GDI por linha.
            var corNo = PaletteTheme.CorDoNo(no.Tipo);
            using (var pincel = new SolidBrush(corNo))
                g.FillRectangle(pincel, xConteudo + 1, e.CellBounds.Y + 7, 7, 7);
            xConteudo += PaletteTheme.LarguraIconeNo;

            // O texto, e a marca de alerta a seguir — em texto, não só em cor.
            string texto = no.Rotulo ?? "";
            if ((no.Alertas & AlertaNo.PorClassificar) != 0 && !no.EhGrupo) texto += "  ⚑";
            if ((no.Alertas & AlertaNo.VaosExcessivos) != 0) texto += "  ⚠";

            var fonte = e.CellStyle.Font ?? PaletteTheme.Normal;

            // A etiqueta PRÓXIMA, no artigo para onde as próximas medições
            // vão. Desenhada à direita e o texto encurta antes dela: é o
            // único sítio da árvore que diz o que acontece a SEGUIR, e ficar
            // cortado por um rótulo comprido derrotava o efeito.
            int largura = e.CellBounds.Right - xConteudo - 2;
            if (EhProximaMedicao(no))
            {
                const string etiqueta = "PRÓXIMA";
                var medida = TextRenderer.MeasureText(g, etiqueta, PaletteTheme.PequenoNegrito);
                int x = e.CellBounds.Right - medida.Width - 6;
                var selo = new Rectangle(x - 3, e.CellBounds.Y + 3,
                                         medida.Width + 6, e.CellBounds.Height - 7);

                using (var pincel = new SolidBrush(PaletteTheme.Acento))
                    g.FillRectangle(pincel, selo);
                TextRenderer.DrawText(g, etiqueta, PaletteTheme.PequenoNegrito, selo,
                    PaletteTheme.AltoContraste ? SystemColors.HighlightText : Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                largura = selo.Left - xConteudo - 4;
            }

            var caixa = new Rectangle(xConteudo, e.CellBounds.Y,
                                      largura < 10 ? 10 : largura, e.CellBounds.Height);
            TextRenderer.DrawText(g, texto, fonte, caixa, tinta,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            e.Handled = true;
        }

        /// <summary>
        /// É este o artigo para onde as próximas medições vão?
        ///
        /// Compara pela chave normalizada do artigo — a mesma que o mapa usa
        /// para indexar —, e não pelo rótulo: o rótulo é texto para ler e uma
        /// designação cortada faria a etiqueta desaparecer sem explicação.
        /// </summary>
        private static bool EhProximaMedicao(NoResultado no)
        {
            if (no == null || no.Tipo != TipoNo.Artigo) return false;
            if (string.IsNullOrEmpty(Config.Artigo)) return false;
            if (!ChaveArtigo.Compativel(no.Artigo ?? "", Config.Artigo)) return false;

            // O mesmo artigo pode estar em vários pisos. A etiqueta é do nó
            // onde a próxima medição vai MESMO cair.
            string piso = string.IsNullOrEmpty(Config.Piso)
                ? ResultadosArvore.SemPiso : Config.Piso;
            return string.Equals(no.Piso ?? "", piso, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Copia piso + serviço + artigo do nó seleccionado para a próxima
        /// medição. É a ÚNICA operação da árvore que mexe na Config — e é
        /// explícita de propósito: seleccionar serve para consultar.
        /// </summary>
        private void MedirAqui()
        {
            var no = NoSeleccionado();
            if (no == null || !no.PermiteMedirAqui) return;

            Config.Piso = no.Piso == ResultadosArvore.SemPiso ? "" : (no.Piso ?? "");
            Config.Servico = no.Servico == ResultadosArvore.SemServico
                ? Config.Servico : (no.Servico ?? Config.Servico);
            Config.Artigo = no.Artigo ?? "";

            // Os campos do painel acompanham, senão o próximo SyncConfig
            // escrevia por cima do que se acabou de escolher.
            _carregando = true;
            try
            {
                _cmbPiso.Text = Config.Piso;
                _txtServico.Text = Config.Servico;
                if (_txtTitulo != null)
                    _txtTitulo.Text = PaletteHost.TextoDoArtigoCorrente();
            }
            finally { _carregando = false; }

            AtualizarCor();
            MostrarLayerEfectiva();
            AtualizarResumoDaProxima();
            PaletteHost.Log("Próxima medição: " + ResumoDaProxima());
        }

        /// <summary>Põe o cursor neste nó, se ele estiver à vista.</summary>
        private void SeleccionarNo(string id)
        {
            int linha = IndiceDe(id);
            if (linha < 0) return;
            try { _dgvCompacto.CurrentCell = _dgvCompacto.Rows[linha].Cells[0]; }
            catch { }
        }

        /// <summary>
        /// Atalhos do painel inteiro: `Ctrl+F` vai para a pesquisa.
        ///
        /// No ProcessCmdKey e não num KeyDown porque tem de funcionar esteja o
        /// foco onde estiver dentro da paleta — na árvore, num campo da
        /// configuração ou num botão. Um atalho que só funciona com o foco no
        /// sítio certo não é um atalho.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.F))
            {
                if (_txtPesquisa != null)
                {
                    _txtPesquisa.Focus();
                    _txtPesquisa.SelectAll();
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>O nó em que a árvore está, ou nulo.</summary>
        private NoResultado NoSeleccionado()
        {
            if (_dgvCompacto == null || _dgvCompacto.CurrentRow == null) return null;
            return _dgvCompacto.CurrentRow.Tag as NoResultado;
        }

        /// <summary>O valor de uma propriedade do nó, pelo identificador do campo.</summary>
        private static string ValorPropriedade(List<Propriedade> propriedades, string campo)
        {
            if (propriedades == null) return "";
            foreach (var p in propriedades)
                if (p.Campo == campo) return p.Valor;
            return "";
        }


        /// <summary>
        /// Grava no desenho o que se escreveu em PROPRIEDADES.
        ///
        /// Passa pelos MESMOS métodos do <see cref="AlvRepo"/> que a grelha
        /// larga usava — não há um segundo caminho de escrita — e valida antes
        /// de chamar: uma medida que não se lê não chega a tocar no desenho.
        ///
        /// A EDIÇÃO EM LOTE CONTINUA A VALER. Com várias medições escolhidas na
        /// árvore, o que se escreve numa vale para todas. Sem isso, tirar o
        /// piso a trinta medições eram trinta edições — e a árvore reagrupa-se
        /// a cada uma, porque agrupa POR piso: a medição que se acabou de mudar
        /// salta para outro sítio e a seguinte já não está onde estava.
        /// </summary>
        private void AplicarEdicaoDePropriedade(NoResultado no, Propriedade p,
                                                string bruto, List<string> alvos)
        {
            if (_carregando) return;
            if (p == null || !p.Editavel || no == null || no.EhGrupo) return;

            string handle = no.Handle;
            if (handle == null) return;

            bruto = (bruto ?? "").Trim();
            if (alvos == null) alvos = HandlesSeleccionados();

            // Com várias linhas escolhidas na árvore, a escrita vale para
            // todas — mas só se a que estamos a editar for uma delas.
            bool emBloco = alvos.Count > 1 && alvos.Contains(handle);

            switch (p.Campo)
            {
                // ---- parede -------------------------------------------------
                case "servico":
                    if (!AlvRepo.DefinirServico(handle, bruto))
                        PaletteHost.Log("Serviço inválido — a medição não foi alterada.");
                    break;

                case "artigo":
                {
                    string chave = ChaveEscrita(handle, bruto);
                    if (emBloco)
                        Relatar(AlvRepo.DefinirArtigoEmVarias(alvos, chave), alvos.Count, "artigo");
                    else
                        Confirmar(AlvRepo.DefinirArtigo(handle, chave), "artigo");
                    break;
                }

                case "piso":
                    if (emBloco)
                        Relatar(AlvRepo.DefinirPisoEmVarias(alvos, bruto), alvos.Count,
                                bruto.Length == 0 ? "piso (removido)" : "piso «" + bruto + "»");
                    else
                        Confirmar(AlvRepo.DefinirPiso(handle, bruto), "piso");
                    break;

                case "altura":
                case "largura":
                case "espessura":
                {
                    double valor;
                    if (!TentarMedida(bruto, out valor)) return;

                    // O AlvRepo conhece estas dimensões por nomes curtos — os
                    // mesmos que as colunas da grelha larga usavam.
                    string campo = p.Campo == "altura" ? "alt"
                                 : p.Campo == "largura" ? "larg" : "esp";
                    if (emBloco)
                        Relatar(AlvRepo.AlterarDimensaoEmVarias(alvos, campo, valor),
                                alvos.Count, p.Nome);
                    else
                        Confirmar(AlvRepo.AlterarDimensao(handle, campo, valor), p.Nome);
                    break;
                }

                // ---- vão ----------------------------------------------------
                case "designacao":
                    if (no.Indice < 0) return;
                    AlvRepo.DefinirDesignacaoVao(handle, no.Indice, bruto);
                    break;

                case "larguraVao":
                case "alturaVao":
                case "quantidadeVao":
                {
                    if (no.Indice < 0) return;
                    double medida;
                    if (!TentarMedida(bruto, out medida)) return;

                    // "qt", e não "qtd": é o nome que o AlvRepo.AlterarVao
                    // reconhece. Um nome errado não dá erro nenhum — o método
                    // simplesmente não faz nada, e a quantidade do vão ficava
                    // como estava sem uma única mensagem.
                    string campo = p.Campo == "larguraVao" ? "larg"
                                 : p.Campo == "alturaVao" ? "alt" : "qt";
                    Confirmar(AlvRepo.AlterarVao(handle, no.Indice, campo, medida), p.Nome);
                    break;
                }

                // ---- título -------------------------------------------------
                case "codigoTitulo":
                case "descricaoTitulo":
                {
                    if (no.Indice < 0) return;

                    // O código e a descrição viajam juntos num campo só,
                    // separados pelo 0x1F. Editar um tem de conservar o outro
                    // — a grelha larga mostrava a descrição cortada e gravá-la
                    // de volta apagava o resto do artigo.
                    string codigo = ValorPropriedade(no.Propriedades, "codigoTitulo");
                    string descricao = ValorPropriedade(no.Propriedades, "descricaoTitulo");
                    if (p.Campo == "codigoTitulo") codigo = bruto; else descricao = bruto;

                    AlvRepo.DefinirTextoDeTitulo(handle, no.Indice,
                        codigo + ChaveArtigo.Sep + descricao);
                    break;
                }

                default:
                    return;
            }

            AplicarNaFolha();
        }

        /// <summary>
        /// O que vai sair na próxima medição, em texto — para o cabeçalho e
        /// para o resumo da CONFIGURAÇÃO recolhida.
        /// </summary>
        private static string ResumoDaProxima()
        {
            var partes = new List<string>();
            if (!string.IsNullOrEmpty(Config.Piso)) partes.Add(Config.Piso);
            if (!string.IsNullOrEmpty(Config.Servico)) partes.Add(Config.Servico);

            string codigo = ChaveArtigo.Codigo(Config.Artigo ?? "").Trim();
            partes.Add(codigo.Length > 0 ? codigo : ResultadosArvore.PorClassificar);

            partes.Add("h " + Config.Altura.ToString("N2", CultureInfo.CurrentCulture) + " m");
            partes.Add("e " + Config.Espessura.ToString("N2", CultureInfo.CurrentCulture) + " m");
            return string.Join(" · ", partes.ToArray());
        }

        /// <summary>Põe o resumo da próxima medição nos dois sítios que o mostram.</summary>
        private void AtualizarResumoDaProxima()
        {
            string resumo = ResumoDaProxima();
            if (_lblProximaMedicao != null)
            {
                _lblProximaMedicao.Text = "Próxima medição: " + resumo;
                _lblProximaMedicao.AccessibleDescription = _lblProximaMedicao.Text;
            }
            if (_btnConfigToggle != null)
                _btnConfigToggle.Text = (_configExpandida ? "▼" : "▶") +
                                        "  CONFIGURAÇÃO   " + resumo;
            AtualizarDesenhoActivo();
        }

        /// <summary>
        /// Diz que desenho está activo. Sem nenhum aberto di-lo por palavras,
        /// em vez de ficar em branco — um cabeçalho vazio lê-se como um erro.
        /// </summary>
        private void AtualizarDesenhoActivo()
        {
            if (_lblDwg == null) return;
            string nome = null;
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc != null)
                    nome = System.IO.Path.GetFileName(doc.Name);
            }
            catch { }

            bool ha = !string.IsNullOrWhiteSpace(nome);
            _lblDwg.Text = ha ? "● " + nome : "○ sem desenho";
            _lblDwg.ForeColor = ha ? PaletteTheme.AcentoEscuro : PaletteTheme.Apagado;
            _lblDwg.AccessibleDescription = ha ? "Desenho activo: " + nome : "Sem desenho aberto";
            ToolTipDoCabecalho().SetToolTip(_lblDwg, _lblDwg.AccessibleDescription);
        }

        private ToolTip _dicaCabecalho;
        private ToolTip ToolTipDoCabecalho()
        {
            return _dicaCabecalho ?? (_dicaCabecalho = new ToolTip { AutoPopDelay = 15000 });
        }

        private void AtualizarPropriedadesCompactas()
        {
            if (_mosaico == null) return;

            var no = NoSeleccionado();
            _lblPropriedadeTitulo.Text = (_propriedadesAbertas ? "▼  " : "▶  ") +
                "PROPRIEDADES  —  " + (no == null ? "nada selecionado" : no.Rotulo);
            _lblPropriedadeTitulo.AccessibleDescription =
                (_propriedadesAbertas ? "Expandida. " : "Recolhida. ") +
                (no == null ? "Nada selecionado." : no.Rotulo);

            // "Medir aqui" só em nós de artigo, como o plano manda. Fica no
            // cabeçalho mesmo recolhido — é uma acção, não uma leitura.
            if (_btnMedirAqui != null)
                _btnMedirAqui.Visible = no != null && no.PermiteMedirAqui;

            _mosaico.No = no;
            _mosaico.Handles = HandlesSeleccionados();
            _mosaico.Visible = _propriedadesAbertas;

            var painel = _mosaico.Parent;

            if (no == null || no.Propriedades == null)
            {
                _mosaico.Definir(null);
                if (painel != null)
                    painel.Height = _propriedadesAbertas
                        ? PaletteTheme.AlturaTituloSeccao + _mosaico.AlturaNecessaria
                        : PaletteTheme.AlturaTituloSeccao;
                return;
            }

            var mostrar = new List<Propriedade>();
            foreach (var p in no.Propriedades)
            {
                // O modo "Essenciais" esconde só o acessório — bloco, alçado,
                // layer, handle. As DIMENSÕES ficam sempre à vista: saíram da
                // árvore para aqui, e escondê-las atrás de um modo obrigava a
                // dois cliques para tirar uma dúvida sobre um número.
                if (_propriedadesEssenciais && !p.Essencial) continue;
                mostrar.Add(p);
            }
            _mosaico.Definir(mostrar);

            // A faixa cresce com o que tem de mostrar, em vez de cortar. Uma
            // parede com dez métricas não cabe na altura de seis. Recolhida,
            // fica só a altura do cabeçalho — o mosaico continua desenhado
            // por trás, pronto a reaparecer sem se recompor.
            if (painel != null)
                painel.Height = _propriedadesAbertas
                    ? PaletteTheme.AlturaTituloSeccao + _mosaico.AlturaNecessaria
                    : PaletteTheme.AlturaTituloSeccao;
        }

        /// <summary>
        /// Grava o que se escreveu num mosaico de métrica.
        ///
        /// Reencaminha para o mesmo caminho de escrita da grelha antiga — os
        /// métodos do <see cref="AlvRepo"/> —, porque um segundo caminho de
        /// escrita é como as duas metades de um programa começam a discordar.
        /// </summary>
        private void AoEditarMetrica(object sender, PropriedadeEditadaEventArgs e)
        {
            AplicarEdicaoDePropriedade(e.No, e.Propriedade, e.Valor, e.Handles);
        }

        /// <summary>
        /// Rótulo de campo da CONFIGURAÇÃO.
        ///
        /// Passa pelo tema em vez de herdar a cor do painel. A herança
        /// funcionava enquanto o fundo era branco e o preto do sistema servia;
        /// em grafite, um rótulo que não declare a sua cor fica à mercê do que
        /// o AutoCAD tenha posto no controlo pai — e foi assim que os nomes dos
        /// campos ficaram cinzento-escuro sobre cinzento-escuro.
        /// </summary>
        private static Label Rot(string t)
        {
            return new Label
            {
                Text = t,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Fill,
                ForeColor = PaletteTheme.Apagado,
                BackColor = Color.Transparent,
                Font = PaletteTheme.Normal,
                AccessibleName = t.TrimEnd(':', ' ')
            };
        }

        /// <summary>
        /// Item do menu «Mais», com o clique protegido pela mesma razão dos
        /// botões: um erro vai para a linha de comandos, não abre uma caixa
        /// vermelha por cima do desenho.
        /// </summary>
        private static ToolStripMenuItem ItemDeMenu(string texto, Image icone,
                                                    string descricao, Action accao)
        {
            var item = new ToolStripMenuItem(texto, icone);
            item.ToolTipText = descricao;
            item.AccessibleName = texto;
            item.AccessibleDescription = descricao;
            item.Click += (s, e) =>
            {
                try { accao(); }
                catch (Exception ex) { PaletteHost.Log(texto + ": " + ex.Message); }
            };
            return item;
        }

        /// <summary>
        /// Botão de barra compacto: ícone pequeno ao LADO do texto, numa linha
        /// só. Para as acções sobre resultados — frequentes, mas secundárias.
        /// </summary>
        private static ToolStripButton BotaoDeBarra(string texto, Image icone,
                                                    EventHandler aoClicar)
        {
            EventHandler seguro = (s, e) =>
            {
                try { aoClicar(s, e); }
                catch (Exception ex) { PaletteHost.Log(texto + ": " + ex.Message); }
            };
            return new ToolStripButton(texto, icone, seguro)
            {
                TextImageRelation = TextImageRelation.ImageBeforeText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                ImageScaling = ToolStripItemImageScaling.SizeToFit,
                AutoSize = true,
                Font = PaletteTheme.Pequeno,
                Padding = new Padding(3, 1, 3, 1),
                Margin = new Padding(0, 0, 2, 0),
                ToolTipText = texto,
                AccessibleName = texto
            };
        }

        /// <summary>Botão com o clique protegido: um erro nunca abre caixa vermelha.</summary>
        private static ToolStripButton MakeButton(string text, Image icon, EventHandler onClick)
        {
            EventHandler seguro = (s, e) =>
            {
                try { onClick(s, e); }
                catch (Exception ex) { PaletteHost.Log(text + ": " + ex.Message); }
            };
            return new ToolStripButton(text, icon, seguro)
            {
                TextImageRelation = TextImageRelation.ImageAboveText,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                AutoSize = true,
                Padding = new Padding(2, 1, 2, 1),
                Margin = new Padding(1, 0, 1, 0),
                ToolTipText = text
            };
        }

        private void AtualizarCor()
        {
            try
            {
                // A MESMA CHAVE QUE O EscolherCor GRAVA.
                //
                // Aqui inventava-se "PISO 0" quando o campo estava vazio, mas
                // quem grava usa o Config.Piso — que é "" nesse caso, porque
                // campo vazio quer dizer SEM PISO. Escolhia-se uma cor, ela ia
                // para a chave "", e este lado ia lê-la a "PISO 0": a caixa
                // abria, escolhia-se, e não mudava nada. Duas chaves para a
                // mesma coisa.
                _corDoPiso = FachadaConfig.CorDoPiso(PisoDaCor());
                _btnCor.AccessibleDescription =
                    "Cor do piso " + PisoDaCor() + ". Vale para as próximas medições.";
                _btnCor.Invalidate();   // é o Paint que desenha a pastilha
            }
            catch { }
        }

        /// <summary>
        /// Sob que chave é que a cor deste piso é guardada e lida. Um sítio só,
        /// para os dois lados não voltarem a divergir.
        /// </summary>
        private string PisoDaCor()
        {
            return (_cmbPiso.Text ?? "").Trim().ToUpperInvariant();
        }

        private void EscolherCor()
        {
            SyncConfig();
            string chave = PisoDaCor();
            using (var dlg = new System.Windows.Forms.ColorDialog
            {
                Color = FachadaConfig.CorDoPiso(chave),
                FullOpen = true
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                FachadaConfig.CoresPiso[chave] = dlg.Color;
                FachadaConfig.Guardar();
                AtualizarCor();

                // As medições já feitas não mudam sozinhas: a cor está na
                // entidade, escrita quando ela nasceu. Dizê-lo evita a
                // conclusão errada de que a escolha não pegou.
                PaletteHost.Log("Cor de \"" + (chave.Length == 0 ? "(sem piso)" : chave) +
                                "\" alterada. Vale para as PRÓXIMAS medições; " +
                                "as que já estão no desenho mantêm a cor com que foram criadas.");
            }
        }

        private void AddCol(string name, string header, bool editavel = false, bool realce = true)
        {
            var col = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = !editavel
            };
            // O realce amarelo diz "isto edita-se". Nas colunas que só se editam
            // nas linhas de vão, pintar a coluna toda prometia o que não se
            // cumpre — nessas, a cor põe-se célula a célula.
            if (editavel && realce)
                col.DefaultCellStyle.BackColor = Color.FromArgb(255, 252, 225);
            _dgv.Columns.Add(col);
        }

        /// <summary>Grava na entidade o valor editado na grade.</summary>
        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_carregando || e.RowIndex < 0) return;

            var row = _dgv.Rows[e.RowIndex];
            string handle = row.Tag as string;
            if (handle == null) return;

            string coluna = _dgv.Columns[e.ColumnIndex].Name;
            string bruto = (row.Cells[e.ColumnIndex].Value ?? "").ToString();

            // ---- linha de título ----------------------------------------------
            if (_linhasTitulo.Contains(e.RowIndex))
            {
                if (coluna != "artigo") return;

                // Só o código muda aqui. A descrição vai-se buscar ao que já
                // está guardado, para não se perder o que a grelha não mostra.
                int idxT;
                if (!_indiceTitulo.TryGetValue(e.RowIndex, out idxT)) return;

                string descricao = "";
                foreach (var pd in _paredes)
                    if (pd.Handle == handle)
                    {
                        string tt = pd.TextoDaMarca(idxT);
                        int i = tt.IndexOf('\u001f');
                        descricao = i >= 0 ? tt.Substring(i + 1) : tt;
                        break;
                    }

                AlvRepo.DefinirTextoDeTitulo(handle, idxT, bruto.Trim() + "\u001f" + descricao);
                PaletteHost.RefreshData();
                return;
            }

            // ---- linha de vão -------------------------------------------------
            if (_linhasVao.Contains(e.RowIndex))
            {
                int indice;
                if (!_indiceVao.TryGetValue(e.RowIndex, out indice)) return;

                if (coluna == "servico")
                {
                    // Tirar a indentação com que a linha é desenhada, senão as
                    // setas iam-se acumulando no nome a cada edição.
                    AlvRepo.DefinirDesignacaoVao(handle, indice,
                        bruto.Replace("↳", "").Trim());
                    PaletteHost.RefreshData();
                    return;
                }

                double medida;
                if (!TentarMedida(bruto, out medida)) return;

                // Na linha do vão a coluna "Compr." mostra a largura do vão.
                AlvRepo.AlterarVao(handle, indice,
                    coluna == "comp" ? "larg" : coluna, medida);
                PaletteHost.RefreshData();
                return;
            }

            // ---- linha de parede ----------------------------------------------

            // Com várias linhas seleccionadas, o que se escreve numa vale para
            // todas.
            //
            // Sem isto, tirar o piso a trinta medições eram trinta edições — e
            // a grelha REORDENA-SE a cada uma, porque agrupa por piso: a
            // medição que se acabou de limpar salta para outro sítio e a linha
            // seguinte já não está onde estava. Editava-se duas vezes umas e
            // nenhuma vez outras, e ficava a parecer que a paleta ignorava
            // metade do trabalho.
            //
            // Selecciona-se com Ctrl ou Shift e escreve-se por cima — o clique
            // que abre a edição desfaria a selecção, escrever directamente não.
            var alvos = HandlesSeleccionados();
            bool emBloco = alvos.Count > 1 && alvos.Contains(handle);

            if (coluna == "artigo")
            {
                string chave = ChaveEscrita(handle, bruto);
                if (emBloco)
                    Relatar(AlvRepo.DefinirArtigoEmVarias(alvos, chave),
                            alvos.Count, "artigo");
                else
                    Confirmar(AlvRepo.DefinirArtigo(handle, chave), "artigo");
                AplicarNaFolha();
                return;
            }

            if (coluna == "piso")
            {
                string piso = bruto.Trim();
                if (emBloco)
                    Relatar(AlvRepo.DefinirPisoEmVarias(alvos, piso), alvos.Count,
                            piso.Length == 0 ? "piso (removido)" : "piso «" + piso + "»");
                else
                    Confirmar(AlvRepo.DefinirPiso(handle, piso), "piso");
                AplicarNaFolha();
                return;
            }

            if (coluna == "servico")
            {
                if (!AlvRepo.DefinirServico(handle, bruto))
                    PaletteHost.Log("Serviço inválido — a medição não foi alterada.");
                AplicarNaFolha();
                return;
            }

            if (coluna != "alt" && coluna != "larg" && coluna != "esp") return;

            double valor;
            if (!TentarMedida(bruto, out valor)) return;

            if (emBloco)
                Relatar(AlvRepo.AlterarDimensaoEmVarias(alvos, coluna, valor),
                        alvos.Count, coluna);
            else
                Confirmar(AlvRepo.AlterarDimensao(handle, coluna, valor), coluna);
            AplicarNaFolha();
        }

        /// <summary>
        /// Depois de editar na grelha: relê o desenho e escreve JÁ no Excel.
        ///
        /// O RefreshData sozinho não chegava. Ele AGENDA a escrita do Excel num
        /// temporizador — o que faz todo o sentido a medir, onde cinco paredes
        /// seguidas devem custar uma escrita e não cinco — mas uma edição na
        /// grelha é um gesto único e deliberado: tirou-se o piso, olha-se para
        /// o Excel, e ele ainda está como estava.
        ///
        /// Pior: o RefreshData só agenda se o Excel ao vivo estiver LIGADO.
        /// Editar com ele desligado nunca chegava à folha, e o botão «Excel ao
        /// Vivo» não resolve — é um interruptor, não um "actualizar": carregar
        /// nele com o Excel ligado DESLIGA-O.
        /// </summary>
        private static void AplicarNaFolha()
        {
            PaletteHost.RefreshData();
            PaletteHost.EscreverExcelAgora();
        }

        /// <summary>
        /// Diz o que uma edição em bloco fez. Sempre — mesmo quando corre bem.
        ///
        /// Mudar trinta medições de uma vez não pode acontecer em silêncio: quem
        /// tinha uma selecção esquecida de antes tem de perceber logo o que
        /// mexeu, para desfazer com o CTRL+Z do AutoCAD enquanto ainda se lembra.
        /// </summary>
        private static void Relatar(int mudadas, int seleccionadas, string campo)
        {
            if (mudadas == 0)
            {
                PaletteHost.Log("nenhuma das " + seleccionadas +
                                " medições seleccionadas mudou de " + campo +
                                " — já estavam todas assim.");
                return;
            }
            PaletteHost.Log(campo + ": " + mudadas + " de " + seleccionadas +
                            " medições seleccionadas.");
        }

        /// <summary>
        /// Diz alto quando uma edição da grelha não chegou ao desenho.
        ///
        /// Antes não dizia nada: a gravação devolvia false, ninguém olhava, a
        /// grelha refrescava com o valor antigo e ficava a parecer que a
        /// tecla não tinha sido carregada. Foi assim que uma limpeza do Piso
        /// funcionou em metade das medições e na outra metade não, sem uma
        /// única mensagem em lado nenhum.
        /// </summary>
        private static void Confirmar(bool gravou, string campo)
        {
            if (gravou) return;
            PaletteHost.Log("não consegui gravar o " + campo +
                            " desta medição — pode ter sido apagada do desenho. " +
                            "Carregue em Atualizar e tente outra vez.");
        }

        /// <summary>
        /// As medições abrangidas pela selecção da grelha, sem repetições.
        ///
        /// Uma linha de vão aponta para a parede a que pertence — seleccionar o
        /// vão vale seleccionar a parede, que é o que se espera. As linhas de
        /// título ficam de fora: são texto da folha, não medições.
        /// </summary>
        private List<string> HandlesSeleccionados()
        {
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            var handles = new List<string>();

            var linhas = new SortedSet<int>();
            foreach (DataGridViewCell c in _dgvCompacto.SelectedCells) linhas.Add(c.RowIndex);
            foreach (DataGridViewRow r in _dgvCompacto.SelectedRows) linhas.Add(r.Index);
            if (linhas.Count == 0 && _dgvCompacto.CurrentRow != null)
                linhas.Add(_dgvCompacto.CurrentRow.Index);

            foreach (int i in linhas)
            {
                if (i < 0 || i >= _dgvCompacto.Rows.Count) continue;
                var no = _dgvCompacto.Rows[i].Tag as NoResultado;
                if (no == null) continue;

                // Um GRUPO não entra. Não tem handle nenhum — não há entidade
                // no desenho para onde apontar — e é isso que o impede de ser
                // removido, editado ou reclassificado. Deixá-lo arrastar os
                // filhos consigo transformava um clique num piso numa operação
                // sobre a obra inteira.
                if (no.EhGrupo) continue;

                // Um TÍTULO é texto da folha, não uma medição. Fica de fora
                // das operações em lote, como sempre esteve.
                if (no.Tipo == TipoNo.Titulo) continue;

                // Um VÃO aponta para a parede a que pertence: seleccionar o
                // vão vale seleccionar a parede, que é o que se espera.
                if (no.Handle != null && vistos.Add(no.Handle)) handles.Add(no.Handle);
            }
            return handles;
        }

        /// <summary>
        /// Passa as medições seleccionadas para o artigo escolhido no painel.
        ///
        /// É a resposta ao caso normal de uma obra a sério: mede-se antes de o
        /// mapa do cliente chegar, ou o mapa muda de versão a meio. O artigo de
        /// uma medição não é geometria — é classificação — e classificação
        /// muda-se sem se tocar no desenho.
        /// </summary>
        private void Reclassificar()
        {
            var handles = HandlesSeleccionados();
            if (handles.Count == 0)
            {
                MessageBox.Show("Seleccione na grelha as medições a reclassificar.\n\n" +
                    "Ctrl escolhe linhas soltas, Shift escolhe um intervalo.",
                    "TSK TakeOff — reclassificar",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            ClassificarMedicoes(handles);
        }

        /// <summary>
        /// Abre a lista de artigos e passa estas medições para o que for
        /// escolhido. É por aqui que passam os dois caminhos — a célula
        /// «classificar» de uma linha e o botão sobre a selecção — para não
        /// haver duas maneiras de classificar com comportamentos diferentes.
        /// </summary>
        private void ClassificarMedicoes(List<string> handles)
        {
            if (handles == null || handles.Count == 0) return;

            if (!MapaQuantidades.Existe)
            {
                MessageBox.Show(
                    "Este desenho não tem mapa de quantidades importado.\n\n" +
                    "Importe o mapa do cliente com o comando TSKMQT e volte aqui.",
                    "TSK TakeOff — classificar",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MapaQuantidades.No no;
            using (var dlg = new ArtigoDialog(handles.Count, Config.Artigo))
            {
                // ShowModalDialog e não ShowDialog: dentro do AutoCAD é o que
                // põe a caixa modal em relação ao editor, e não só à paleta.
                if (AcadApp.ShowModalDialog(dlg) != DialogResult.OK) return;
                no = dlg.Escolhido;
            }
            if (no == null) return;

            int feitas = AlvRepo.DefinirArtigoEmVarias(handles, no.Chave);
            PaletteHost.Log(string.Format("{0} medição(ões) classificadas em {1} — {2}",
                feitas, no.Codigo,
                (no.Designacao ?? "").Length > 60
                    ? no.Designacao.Substring(0, 60) + "…" : no.Designacao));

            PaletteHost.RefreshData();
            PaletteHost.EscreverExcelAgora();
        }

        /// <summary>
        /// Traduz o que se escreveu na coluna «Artigo» para a chave que uma
        /// medição guarda — código + designação.
        ///
        /// Com mapa importado, escrever "3.1.1" passa a valer o artigo INTEIRO
        /// do mapa. Antes ficava o código novo com a designação ANTIGA colada
        /// atrás, e esse par não existe em mapa nenhum: a medição tornava-se
        /// órfã, ia parar ao fim da folha e não havia nada que o explicasse.
        /// Era isto que fazia com que reclassificar uma medição já feita não
        /// desse resultado nenhum.
        ///
        /// Célula vazia tira o artigo. Sem mapa — ou com um código que ele não
        /// conhece — fica a designação que a medição já trazia: é a única que
        /// há, e deitá-la fora perdia trabalho feito.
        /// </summary>
        private string ChaveEscrita(string handle, string escrito)
        {
            string codigo = (escrito ?? "").Trim();
            // O aviso da grelha não é um código: quem sai da célula sem lhe
            // tocar não está a classificar nada.
            if (codigo.Length == 0 || codigo == SemArtigo) return "";

            var no = MapaQuantidades.PorCodigo(codigo);
            if (no != null)
            {
                if (!no.EhArtigo)
                    PaletteHost.Log(no.Codigo + " é um capítulo do mapa, não um artigo. " +
                                    "A medição ficou lá — confirme se era o que queria.");
                return no.Chave;
            }

            if (MapaQuantidades.Existe)
                PaletteHost.Log("o mapa não tem nenhum artigo com o código «" + codigo +
                                "». A medição fica com ele, mas sai no fim da folha.");

            string descricao = "";
            foreach (var pd in _paredes)
                if (pd.Handle == handle)
                {
                    int i = (pd.Artigo ?? "").IndexOf(MapaQuantidades.Sep,
                                                      StringComparison.Ordinal);
                    if (i >= 0)
                        descricao = pd.Artigo.Substring(i + MapaQuantidades.Sep.Length);
                    break;
                }
            return codigo + MapaQuantidades.Sep + descricao;
        }

        /// <summary>
        /// Lê uma medida escrita na grelha. Aceita vírgula ou ponto — quem mede
        /// escreve como lhe sai, e não vale a pena castigar por isso.
        /// </summary>
        private static bool TentarMedida(string texto, out double valor)
        {
            if (double.TryParse((texto ?? "").Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out valor) && valor >= 0)
                return true;

            PaletteHost.Log("Valor inválido — a medição não foi alterada.");
            PaletteHost.RefreshData();
            return false;
        }

        // ------------------------------------------------------------------
        // Dados
        // ------------------------------------------------------------------
        /// <summary>
        /// Linhas da grelha que são vãos, e não paredes. Guardadas à parte para
        /// os botões saberem que ali não se remove nem se edita: quem manda é a
        /// parede que está por cima.
        /// </summary>
        private readonly HashSet<int> _linhasVao = new HashSet<int>();

        /// <summary>
        /// Para cada linha de vão, a sua posição na lista de vãos da parede.
        /// Sem isto não se sabia qual dos vãos editar quando há mais do que um.
        /// </summary>
        private readonly Dictionary<int, int> _indiceVao = new Dictionary<int, int>();

        /// <summary>
        /// Linhas da grelha que são títulos (capítulo, artigo, sub-artigo).
        /// Têm linha própria, como os vãos: é ali que se lê o texto, se edita
        /// e se apaga. Antes o título estava escondido dentro da medição e a
        /// única forma de lhe mexer era adivinhar qual delas o carregava.
        /// </summary>
        private readonly HashSet<int> _linhasTitulo = new HashSet<int>();

        /// <summary>Qual dos títulos da medição é cada linha de título.</summary>
        private readonly Dictionary<int, int> _indiceTitulo = new Dictionary<int, int>();

        /// <summary>
        /// As linhas em construção, antes de irem para a grelha de uma vez só.
        ///
        /// Um Rows.Add por linha faz o DataGridView reajustar-se a cada uma.
        /// Medido numa grelha com esta forma (17 colunas, 461 linhas): 152 ms
        /// a acrescentar uma a uma, 20 ms com um Rows.AddRange no fim. É a
        /// diferença entre dar-se pela paleta a pensar depois de cada medição
        /// e não se dar.
        /// </summary>
        private readonly List<DataGridViewRow> _pendentes = new List<DataGridViewRow>();

        /// <summary>
        /// Cria a linha e põe-na na fila, em vez de a entregar já à grelha.
        /// O índice que ela vai ter é o que a fila tem agora — a grelha foi
        /// limpa antes de se começar e não há linha-fantasma no fim
        /// (AllowUserToAddRows = false), por isso a ordem da fila é a ordem
        /// final. É esse índice que o _linhasVao e companhia guardam.
        /// </summary>
        private DataGridViewRow NovaLinha(params object[] valores)
        {
            var linha = new DataGridViewRow();
            linha.CreateCells(_dgv, valores);
            _pendentes.Add(linha);
            return linha;
        }

        /// <summary>
        /// A coluna pelo nome, em número.
        ///
        /// Numa linha ainda por entregar à grelha, o Cells["artigo"] rebenta
        /// com ArgumentException: a linha não conhece as colunas enquanto não
        /// pertencer ao DataGridView. Pelo índice funciona, e o índice vem
        /// daqui.
        /// </summary>
        private int Col(string nome)
        {
            return _dgv.Columns[nome].Index;
        }

        /// <summary>
        /// O que a coluna Artigo mostra numa medição por classificar, havendo
        /// mapa. Uma célula vazia lia-se como "não interessa"; e «— sem artigo —»
        /// dizia o problema mas não dizia a saída — ficava-se a olhar para o
        /// aviso sem nada por onde lhe pegar. O texto é agora o próprio gesto:
        /// clicar aqui abre a lista de artigos para esta medição.
        /// </summary>
        private const string SemArtigo = "⊕ classificar…";

        /// <summary>Vermelho das deduções — o mesmo do Excel, para se ler igual nos dois lados.</summary>
        private static readonly Color VermelhoDeducao = Color.FromArgb(192, 0, 0);

        // Uma Font é um recurso do GDI. Criar uma por linha, a cada
        // actualização, era alocar e deitar fora centenas por medição — e
        // nenhuma delas era diferente das outras.
        private Font _fonteNegrito;
        private Font _fonteItalico;

        private Font FonteNegrito
        {
            get { return _fonteNegrito ?? (_fonteNegrito = new Font(_dgv.Font, FontStyle.Bold)); }
        }

        private Font FonteItalico
        {
            get { return _fonteItalico ?? (_fonteItalico = new Font(_dgv.Font, FontStyle.Italic)); }
        }

        // Estilos partilhados. Cada "linha.DefaultCellStyle.BackColor = ..."
        // cria um DataGridViewCellStyle novo para essa linha, e cada
        // "Cells[c].Style.X = ..." cria outro para essa célula. Numa grelha de
        // 135 linhas eram centenas de objectos por actualização, todos iguais
        // entre si. Atribuídos por referência, são cinco no total.
        private DataGridViewCellStyle _estTitulo, _estVao, _estAlerta,
                                      _celEditavel, _celAlerta, _celSemArtigo;

        private void GarantirEstilos()
        {
            if (_estTitulo != null) return;

            _estTitulo = new DataGridViewCellStyle
            {
                BackColor = FundoTitulo,
                Font = FonteNegrito
            };
            _estVao = new DataGridViewCellStyle
            {
                ForeColor = VermelhoDeducao,
                BackColor = Color.FromArgb(255, 248, 248),
                Font = FonteItalico
            };
            _estAlerta = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(255, 224, 224)
            };
            // Só a cor de fundo: o resto herda do estilo da linha.
            _celEditavel = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(255, 246, 225)
            };
            _celAlerta = new DataGridViewCellStyle
            {
                ForeColor = Color.DarkRed,
                Font = FonteNegrito
            };
            // Medição sem artigo, havendo mapa importado. Não é erro — é
            // trabalho por classificar — mas tem de se ver: são estas que saem
            // no FIM da folha, e é isso que faz parecer que o mapa baralhou a
            // medição quando na verdade só arrumou o que sabia arrumar.
            _celSemArtigo = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(255, 236, 204),
                ForeColor = Color.FromArgb(150, 90, 0),
                Font = FonteItalico
            };
        }

        /// <summary>
        /// Descreve a medição-alvo como ela aparece na grelha: número, serviço
        /// e comprimento. Sem isto, quando algo saía no sítio errado não havia
        /// forma de saber se o alvo estava mal escolhido ou se era a folha que
        /// o punha noutro lado — e discutia-se às cegas.
        /// </summary>
        private string DescreverAlvo(string handle)
        {
            if (handle == null) return "nenhuma";

            // Lido da lista em memória e não da árvore: um filtro pode ter
            // escondido a linha, e a medição continua a ser o alvo legítimo —
            // o alvo pode vir da célula do Excel ou da última medição.
            foreach (var p in _paredes)
            {
                if (p.Handle != handle) continue;
                return string.Format("{0} ({1}, {2} m)",
                    string.IsNullOrEmpty(p.Nota) ? "handle " + handle : p.Nota,
                    string.IsNullOrEmpty(p.Servico) ? "sem serviço" : p.Servico,
                    N2(p.ComprimentoTotal));
            }
            return "handle " + handle;
        }

        /// <summary>De onde veio o alvo, para se perceber quem mandou.</summary>
        private string OrigemDoAlvo(string handle)
        {
            string doExcel = PaletteHost.Excel.MedicaoNaCelulaSeleccionada();
            if (handle != null && handle == doExcel) return "célula do Excel";
            if (handle != null && handle == SelectedHandleSilencioso()) return "linha da paleta";
            return "última medição";
        }

        /// <summary>
        /// O que esta medição acrescenta à folha, na coluna estreita: a linha
        /// em branco e o título que sai por baixo dela. Sem isto não havia como
        /// saber, olhando para a grelha, qual das medições carregava o título
        /// que queríamos tirar — era preciso ir ao Excel adivinhar.
        /// </summary>
        private static string MarcaNaGrelha(Parede p)
        {
            // O título tem linha própria por baixo; aqui basta a linha em branco.
            // Mostra QUANTAS, não só que há. Sem o número, pedir a segunda
            // linha e não a ver aparecer não tinha como ser percebido.
            int n = p.LinhasEmBrancoDepois;
            if (n > 1) return "⏎×" + n;
            return n > 0 || p.Separador ? "⏎" : "";
        }

        /// <summary>
        /// Só o código do artigo ("1.1.4"). A descrição do caderno de encargos
        /// tem parágrafos inteiros — na grelha não cabe e no Excel já está.
        /// </summary>
        private static string CodigoArtigo(string artigo)
        {
            if (string.IsNullOrEmpty(artigo)) return "";
            int i = artigo.IndexOf('\u001f');
            return i >= 0 ? artigo.Substring(0, i) : artigo;
        }

        /// <summary>
        /// Volta a pôr o cursor onde estava antes de a grelha ser reconstruída.
        /// Procura a mesma medição — e, se estávamos num vão, o mesmo vão.
        /// </summary>
        private void RestaurarSeleccao(string handle, int indiceVao, int scroll,
                                       bool seguirSeleccao = false)
        {
            try
            {
                if (handle != null)
                {
                    foreach (DataGridViewRow linha in _dgv.Rows)
                    {
                        if ((linha.Tag as string) != handle) continue;

                        bool ehVao = _linhasVao.Contains(linha.Index);
                        int idx;
                        bool mesmoVao = ehVao &&
                            _indiceVao.TryGetValue(linha.Index, out idx) && idx == indiceVao;

                        // Estávamos num vão: só serve esse vão. Estávamos na
                        // parede: só serve a linha da parede, não os vãos dela.
                        if (indiceVao >= 0 ? !mesmoVao : ehVao) continue;

                        _dgv.CurrentCell = linha.Cells[0];

                        // Acabou de se medir: manda a medição nova, e ela já
                        // está à vista porque pôr o CurrentCell rola até lá.
                        // Repor a rolagem antiga aqui punha-a fora do ecrã
                        // outra vez — a paleta seleccionava uma linha que não
                        // se via.
                        if (!seguirSeleccao) ReporRolagem(scroll);
                        return;
                    }
                }

                // Sem linha para onde voltar — foi apagada, ou nunca houve
                // selecção. A rolagem tem de ser reposta na mesma: sem isto a
                // grelha saltava para o topo a cada remoção, e quem estava a
                // trabalhar na linha 300 perdia o sítio de cada vez.
                ReporRolagem(scroll);
            }
            catch { /* a medição pode ter sido apagada: fica sem selecção */ }
        }

        /// <summary>
        /// Volta a pôr a grelha onde estava, sem passar do fim.
        ///
        /// Ao apagar linhas a lista encolhe, e o índice antigo pode já não
        /// existir — o DataGridView atira nesse caso. Encosta-se ao fim, que é
        /// onde se estava a trabalhar.
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

        /// <summary>Cor de fundo das linhas de título.</summary>
        private static readonly Color FundoTitulo = Color.FromArgb(232, 240, 252);

        /// <summary>Uma linha de título, por baixo da medição que a gerou.</summary>
        private void AcrescentarLinhaTitulo(Parede p, int numeroParede,
            string marca, string textoBruto, int indice)
        {
            string nivel = marca == "CAP" ? "CAPÍTULO" : "ARTIGO";

            string codigo = "", texto = "";
            string t = textoBruto ?? "";
            int i = t.IndexOf('\u001f');
            if (i >= 0) { codigo = t.Substring(0, i); texto = t.Substring(i + 1); }
            else texto = t;

            // Só um resumo: o caderno de encargos são parágrafos inteiros e a
            // grelha é estreita. O texto por extenso lê-se e escreve-se no
            // Excel; aqui o que interessa é o código e saber que nível é.
            if (texto.Length > 40) texto = texto.Substring(0, 40).TrimEnd() + "…";
            if (texto.Length == 0) texto = "(sem texto)";

            var linha = NovaLinha(
                numeroParede + "." + nivel.Substring(0, 1),
                nivel,
                "  " + texto,
                codigo,
                // alçado, bloco, piso, comp, alt, larg, esp, bruta, vãos,
                // líq., vol., pré-aro un, pré-aro m — nada disto é do título
                "", "", "", "", "", "", "", "", "", "", "", "", "");

            int idx = _pendentes.Count - 1;
            linha.Tag = p.Handle;
            _linhasTitulo.Add(idx);
            _indiceTitulo[idx] = indice;

            linha.DefaultCellStyle = _estTitulo;
            linha.Cells[Col("artigo")].Style = _celEditavel;
        }

        /// <summary>Uma linha de vão, indentada por baixo da parede a que pertence.</summary>
        private void AcrescentarLinhaVao(Parede p, Vao v, int numeroParede, int ordem)
        {
            string nome = string.IsNullOrWhiteSpace(v.Designacao)
                ? (v.Tipo == TipoVao.Janela ? "janela" : "porta")
                : v.Designacao;

            var linha = NovaLinha(
                // Numeração em sub-nível: 3.1, 3.2 — vê-se logo a que parede
                // pertence sem ter de contar linhas para cima.
                numeroParede + "." + ordem,
                "",
                "      ↳ " + nome,           // a indentação que dá o efeito de árvore
                "", "", "", "",              // artigo, alçado, bloco, piso: são da parede
                N2(v.Largura),               // comp   — largura do vão
                N2(v.Altura),                // altura
                "", "",                      // larg, esp: não se aplicam a um vão
                N2(v.AreaTotal),             // bruta  — área que este vão desconta
                "", "", "",                  // vaos, liq, vol: só fazem sentido na parede
                v.PreAroUnidades,
                N2(v.PreAroMetros));

            int idx = _pendentes.Count - 1;
            // Mesmo handle da parede: seleccionar o vão continua a apontar para
            // a medição certa quando se acrescenta um título ou outro vão.
            linha.Tag = p.Handle;
            _linhasVao.Add(idx);
            _indiceVao[idx] = ordem - 1;      // ordem começa em 1, a lista em 0

            // Vermelho, como as deduções no Excel: o que desconta lê-se à
            // primeira, sem ter de reparar na seta nem no sinal.
            linha.DefaultCellStyle = _estVao;

            // Só estas três se editam; ficam com o realce de sempre para se ver.
            foreach (string c in new[] { "servico", "comp", "alt" })
                linha.Cells[Col(c)].Style = _celEditavel;
        }

        /// <summary>
        /// Recebe TUDO o que está medido no desenho — já não só a alvenaria.
        ///
        /// O painel deixou de ter quatro abas: o tipo de medida passou a ser um
        /// nível da árvore, e por isso as quatro listas entram aqui e saem numa
        /// hierarquia só. É o que permite ler o total de um piso nas quatro
        /// unidades sem somar de cabeça entre separadores.
        /// </summary>
        public void BindData(List<Parede> paredes, List<MedFachada> fachadas,
                             List<MedItem> lineares, List<MedContagem> contagens)
        {
            // O mapa pode ter acabado de ser importado, ou o utilizador pode
            // ter mudado de desenho. Refazer a lista aqui é o que faz a lista
            // aparecer preenchida logo a seguir ao TSKMQT, sem reabrir nada.
            FiltrarArtigos();

            // Pela ordem da folha, não pela ordem por que foram desenhadas.
            // Assim a árvore e o Excel dizem o mesmo, e o "última medição" dos
            // botões quer dizer o mesmo nos dois lados.
            _paredes = FolhaMedicao.OrdenarComoFolha(paredes ?? new List<Parede>());
            paredes = _paredes;

            _fachadas = FolhaMedicao.OrdenarComoFolha(fachadas ?? new List<MedFachada>());
            _lineares = lineares ?? new List<MedItem>();
            _contagens = contagens ?? new List<MedContagem>();

            // TUDO NUMA ÁRVORE SÓ.
            //
            // As quatro listas — paredes, panos, lineares e contagens — vêm da
            // mesma travessia do desenho e passam pelos mesmos adaptadores.
            // Cada uma declara o seu TIPO DE MEDIDA, e é esse que faz o segundo
            // nível: é o tipo que fixa a unidade, e é por isso que um piso pode
            // mostrar m², m³, m e un. lado a lado sem nunca os somar.
            var medicoes = ResultadosArvore.DeParedes(
                paredes, Config.Regra,
                MapaQuantidades.Existe
                    ? (Func<string, bool>)(a => MapaQuantidades.Procurar(a) != null)
                    : null,
                CultureInfo.CurrentCulture);
            medicoes.AddRange(ResultadosAdaptadores.DeMateriais(
                _fachadas, Config.Regra,
                MapaQuantidades.Existe
                    ? (Func<string, bool>)(a => MapaQuantidades.Procurar(a) != null)
                    : null,
                CultureInfo.CurrentCulture));
            medicoes.AddRange(ResultadosAdaptadores.DeLineares(
                _lineares, CultureInfo.CurrentCulture));
            medicoes.AddRange(ResultadosAdaptadores.DeContagens(
                _contagens, CultureInfo.CurrentCulture));

            // A ÁRVORE COMPACTA É A ÚNICA VISTA DE RESULTADOS.
            //
            // Aqui enchia-se também a grelha larga de dezassete colunas, com
            // uma linha por parede, por vão e por título. Deixou de se fazer:
            // eram duas listas a dizer o mesmo, duas selecções a divergir e
            // dois caminhos de escrita para o desenho — e a que se via era a
            // de baixo. A edição vive agora em PROPRIEDADES, que escreve pelos
            // mesmos métodos do AlvRepo.
            //
            // O _carregando protege o painel de propriedades: reconstruir a
            // árvore muda a selecção, e sem isto o CellEndEdit disparava
            // sozinho e regravava valores que ninguém escreveu.
            _carregando = true;
            try
            {
                AtualizarResultadosCompactos(
                    ResultadosArvore.Construir(medicoes, CultureInfo.CurrentCulture));
            }
            catch (Exception ex)
            {
                PaletteHost.Log("Resultados: " + ex.Message);
            }
            finally
            {
                _carregando = false;
            }

            // Contadores dos avisos do rodapé. Saíam do ciclo que enchia a
            // grelha; agora contam-se aqui, sobre a mesma lista e com as
            // mesmas regras.
            //
            // Só faz sentido apontar o dedo às medições sem artigo quando há um
            // mapa onde as pôr. Sem mapa, não ter artigo é o normal.
            bool comMapa = MapaQuantidades.Existe;
            int excesso = 0, semArtigo = 0;
            foreach (var p in paredes)
            {
                if (comMapa && (string.IsNullOrEmpty(p.Artigo) ||
                                MapaQuantidades.Procurar(p.Artigo) == null))
                    semArtigo++;

                if (p.DescontoVaos(Config.Regra) > p.AreaBruta + 1e-9)
                    excesso++;
            }

            // O TOTAL DO PAINEL É O DA RAIZ DA ÁRVORE.
            //
            // Era calculado só sobre as paredes, o que fazia sentido quando o
            // painel só via alvenaria. Com tudo na mesma árvore, o rodapé tem
            // de dizer o mesmo que a linha de topo — senão são dois totais no
            // mesmo ecrã a discordarem um do outro.
            //
            // A regra de nunca somar unidades diferentes é a de sempre; agora
            // vem do próprio acumulador da raiz.
            string total = _raizResultados == null || _raizResultados.Quantidades.Vazio
                ? "0,00 m²"
                : _raizResultados.Quantidades.Texto(CultureInfo.CurrentCulture);

            _lblTotais.Text = string.Format(
                "Medições: {0}   |   Total: {1}   |   Volume: {2} m³   |   Pré-aros: {3} un / {4} m",
                medicoes.Count,
                total,
                N2(paredes.Sum(p => p.Volume(Config.Regra))),
                paredes.Sum(p => p.PreAroUn),
                N2(paredes.Sum(p => p.PreAroMl)));

            // Primeiro o que falta classificar, depois o que está errado: são
            // coisas diferentes e a segunda é que manda na cor.
            if (semArtigo > 0)
                _lblTotais.Text += string.Format(
                    "   ⚑ {0} sem artigo (saem no fim da folha — seleccione e Reclassificar)",
                    semArtigo);

            if (excesso > 0)
            {
                _lblTotais.ForeColor = Color.DarkRed;
                _lblTotais.Text += string.Format(
                    "   ⚠ {0} parede(s) com vãos maiores que a própria parede", excesso);
            }
            else
            {
                _lblTotais.ForeColor = semArtigo > 0
                    ? Color.FromArgb(150, 90, 0)
                    : SystemColors.ControlText;
            }
        }

        private static string N2(double v) => v.ToString("N2", CultureInfo.CurrentCulture);

        /// <summary>
        /// A unidade como se escreve: "m2" -> "m²".
        ///
        /// O modelo guarda a forma simples porque é essa que o mapa compara
        /// (o Normalizar do MapaQuantidades reduz m² a m2). Ao ecrã vai a
        /// forma composta, que é a que quem mede espera ler.
        /// </summary>
        private static string Unid(string u)
        {
            if (u == "m2") return "m²";
            if (u == "m3") return "m³";
            return u ?? "";
        }

        /// <summary>Linha ativa da grade (funciona com selecção por célula ou por linha).</summary>
        /// <summary>Como o SelectedHandle, mas devolve null em silêncio: quem
        /// chama tem outras alternativas antes de desistir.</summary>
        private string SelectedHandleSilencioso()
        {
            // Sem escolha explícita não há "linha seleccionada": a árvore está
            // pousada na primeira linha, não foi ninguém que a pôs lá.
            if (!PaletteHost.GrelhaFoiEscolhida) return null;

            var no = NoSeleccionado();
            // Um grupo não é alvo de nada: não tem handle. Quem tem a raiz
            // seleccionada não escolheu medição nenhuma.
            return no == null || no.EhGrupo ? null : no.Handle;
        }

        private string SelectedHandle()
        {
            var no = NoSeleccionado();
            if (no != null && !no.EhGrupo && no.Handle != null) return no.Handle;

            MessageBox.Show(
                no != null && no.EhGrupo
                    ? "«" + no.Rotulo + "» é um grupo, não uma medição.\n\n" +
                      "Escolha uma medição, um vão ou um título dentro dele."
                    : "Escolha primeiro uma linha na árvore de resultados.",
                "TSK TakeOff", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        /// <summary>Copia os campos da paleta para a Config antes de medir.</summary>
        // ------------------------------------------------------------------
        // Mapa de quantidades
        // ------------------------------------------------------------------

        /// <summary>
        /// Evita que o preenchimento da combo dispare a escolha. Sem isto,
        /// cada tecla escrita na procura reatribuía o artigo corrente ao
        /// primeiro resultado — e o artigo mudava sozinho enquanto se escrevia.
        /// </summary>
        private bool _aEncherArtigos;

        /// <summary>
        /// Enche a combo com os artigos do mapa que casam com o que está
        /// escrito. Só artigos: os capítulos existem para dar contexto, mas
        /// não se mede num capítulo.
        /// </summary>
        /// <summary>
        /// O que a lista de artigos está a mostrar neste momento. Serve para
        /// não a refazer quando nada mudou.
        /// </summary>
        private string _assinaturaArtigos;   // null = ainda não encheu

        private void FiltrarArtigos()
        {
            if (_cmbArtigoMqt == null) return;

            // O BindData corre a CADA medição. A lista só é reconstruída quando
            // o mapa, o serviço ou o artigo corrente mudam, evitando re-layout
            // desnecessário da palette durante uma sequência de medições.
            string servicoAgora = string.IsNullOrWhiteSpace(_txtServico.Text)
                ? Config.Servico
                : _txtServico.Text.Trim().ToUpperInvariant().Replace(" ", "_");
            string assinatura = string.Join("|",
                                MapaQuantidades.Nos.Select(n =>
                                    n.Ordem + ":" + n.Chave + ":" + n.Unidade + ":" +
                                    n.Nivel + ":" + n.EhArtigo)) + "|" +
                                servicoAgora + "|" + Config.Artigo;
            if (assinatura == _assinaturaArtigos) return;
            _assinaturaArtigos = assinatura;

            _aEncherArtigos = true;
            try
            {
                _cmbArtigoMqt.BeginUpdate();
                _cmbArtigoMqt.Items.Clear();

                if (!MapaQuantidades.Existe)
                {
                    _cmbArtigoMqt.Enabled = false;
                    _lblArtigoMqt.Text = "sem mapa importado — use o TSKMQT";
                    return;
                }

                _cmbArtigoMqt.Enabled = true;
                var achados = MapaQuantidades.Filtrar("", true);
                for (int i = 0; i < achados.Count; i++)
                    _cmbArtigoMqt.Items.Add(achados[i]);

                _lblArtigoMqt.Text = achados.Count == 0
                    ? "nenhum artigo disponível no mapa"
                    : achados.Count + " artigo(s) — escolha na lista";

                // O artigo a mostrar é o do SERVIÇO corrente, se já tiver sido
                // dito. Trocar de serviço passa a trazer o artigo dele — que é
                // o que evita medir o reboco para o artigo da alvenaria por
                // esquecimento. A lista continua completa; a escolha é feita
                // directamente na combo, com scroll e pesquisa incremental do
                // próprio Windows Forms pelo teclado.
                string servActual = string.IsNullOrWhiteSpace(_txtServico.Text)
                    ? Config.Servico
                    : _txtServico.Text.Trim().ToUpperInvariant().Replace(" ", "_");
                string alvo = MapaQuantidades.ArtigoDoServico(servActual);
                if (alvo.Length == 0) alvo = Config.Artigo;

                if (!string.IsNullOrEmpty(alvo))
                {
                    for (int i = 0; i < _cmbArtigoMqt.Items.Count; i++)
                    {
                        var n = _cmbArtigoMqt.Items[i] as MapaQuantidades.No;
                        if (n != null && n.Chave == alvo)
                        {
                            _cmbArtigoMqt.SelectedIndex = i;
                            Config.Artigo = alvo;
                            break;
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("MQT: falhou a filtrar — " + ex.Message);
            }
            finally
            {
                _cmbArtigoMqt.EndUpdate();
                _aEncherArtigos = false;
            }
        }

        /// <summary>
        /// Fixa o artigo escolhido. A partir daqui, as medições que se fizerem
        /// vão para ele — é o que evita ter de o escrever em cada linha.
        /// </summary>
        private void EscolherArtigoMqt()
        {
            if (_aEncherArtigos) return;

            var n = _cmbArtigoMqt.SelectedItem as MapaQuantidades.No;
            if (n == null) return;

            Config.Artigo = n.Chave;

            // O artigo fica ligado ao SERVIÇO corrente, e guardado no desenho.
            //
            // Uma parede é medida em três camadas — alvenaria, reboco,
            // revestimento — e cada uma pertence a um artigo diferente. Dizer
            // isto uma vez por serviço chega para a obra toda: quando o
            // TSKMEDSEL criar as três medições de uma vez, cada uma sabe para
            // onde vai sem se perguntar nada.
            string servico = string.IsNullOrWhiteSpace(_txtServico.Text)
                ? Config.Servico
                : _txtServico.Text.Trim().ToUpperInvariant().Replace(" ", "_");
            if (!string.IsNullOrEmpty(servico))
                MapaQuantidades.DefinirArtigoDoServico(servico, n.Chave);

            _lblArtigoMqt.Text = servico + "  →  " + n.Codigo +
                (string.IsNullOrEmpty(n.Unidade) ? "  (sem unidade no mapa)"
                                                 : "  [" + n.Unidade + "]");

            // O campo do título acompanha o artigo escolhido: o que se vê é o
            // que vai ser escrito. Quem quiser outra coisa escreve por cima.
            if (_txtTitulo != null)
                _txtTitulo.Text = PaletteHost.JuntarTitulo(n.Codigo, n.Designacao);

            PaletteHost.Log("artigo corrente: " + n.Codigo + " — " +
                            (n.Designacao.Length > 70
                                ? n.Designacao.Substring(0, 70) + "…"
                                : n.Designacao));

            // O artigo entra no nome calculado da layer: mostrar já o que vai
            // sair evita a surpresa de medir e só depois ver onde foi parar.
            MostrarLayerEfectiva();
            AtualizarResumoDaProxima();
        }

        /// <summary>Diz, por baixo do campo, qual é a layer que vai mesmo ser usada.</summary>
        private void MostrarLayerEfectiva()
        {
            if (_lblLayer == null) return;
            try
            {
                string nome = Commands.LayerPrefix + Util.NomeLayer(Config.LayerEfectiva());
                _lblLayer.Text = string.IsNullOrWhiteSpace(Config.Layer)
                    ? nome + "   (calculada do artigo + bloco)"
                    : nome;
            }
            catch { _lblLayer.Text = ""; }
        }

        private void SyncConfig()
        {
            Config.Servico = string.IsNullOrWhiteSpace(_txtServico.Text)
                ? "ALVENARIA"
                : _txtServico.Text.Trim().ToUpperInvariant().Replace(" ", "_");
            Config.Bloco = (_txtBloco.Text ?? "").Trim().ToUpperInvariant();
            // Vazio de propósito: é assim que o LayerEfectiva sabe que há de
            // calcular o nome a partir do artigo e do bloco.
            Config.Layer = (_txtLayer.Text ?? "").Trim().ToUpperInvariant().Replace(" ", "_");
            Config.Alcado = (_cmbAlcado.Text ?? "").Trim();
            FachadaConfig.RegistarAlcado(Config.Alcado);
            if (!string.IsNullOrEmpty(Config.Alcado) &&
                !_cmbAlcado.Items.Contains(Config.Alcado))
                _cmbAlcado.Items.Add(Config.Alcado);
            Config.Altura = (double)_numAltura.Value;
            Config.Espessura = (double)_numEspessura.Value;
            Config.Regra = (RegraDesconto)_cmbRegra.SelectedIndex;

            // Campo do piso vazio quer dizer SEM PISO, não "PISO 0".
            //
            // Inventava-se o "PISO 0" a quem tinha deixado o campo em branco, e
            // a medição saía com um piso que ninguém escreveu — na grelha, na
            // folha, e num cabeçalho de piso a dividir um bloco que não tinha
            // divisão nenhuma. Uma camada de enchimento medida por área não
            // pertence a piso nenhum, e apagar o campo era a maneira óbvia de o
            // dizer; só que não era ouvida.
            //
            // Quem quer o piso escreve-o — e continua a ser proposto na lista.
            Config.Piso = (_cmbPiso.Text ?? "").Trim().ToUpperInvariant();

            if (Config.Piso.Length > 0)
            {
                if (!FachadaConfig.CoresPiso.ContainsKey(Config.Piso))
                {
                    FachadaConfig.CoresPiso[Config.Piso] = FachadaConfig.CorDoPiso(Config.Piso);
                    FachadaConfig.Guardar();
                }
                if (!_cmbPiso.Items.Contains(Config.Piso)) _cmbPiso.Items.Add(Config.Piso);
            }
        }

        // ------------------------------------------------------------------
        // Ações
        // ------------------------------------------------------------------
        private void MedirParede()
        {
            SyncConfig();
            PaletteHost.RunCommand("TSKPAREDE ");
        }

        /// <summary>
        /// Dispara um comando de outro tipo de medida — pano, linear, contagem.
        ///
        /// O SyncConfig antes de enviar é pela mesma razão de sempre: o piso, a
        /// altura e o alçado do painel têm de chegar ao comando. Escrever
        /// TSKRET à mão media com os valores antigos, e ninguém percebia porquê.
        ///
        /// Cada tipo tem ainda ajustes próprios — o material de um pano, o raio
        /// de uma contagem — que vivem nas Definições e não em cada botão: são
        /// coisas que se escolhem uma vez e valem para a sessão.
        /// </summary>
        private void MedirNoutroTipo(string comando)
        {
            SyncConfig();
            PaletteHost.RunCommand(comando);
        }

        /// <summary>
        /// As definições dos tipos que não têm campos próprios no painel:
        /// material e altura de piso dos panos, nome/categoria/raio das
        /// contagens.
        ///
        /// Fica num diálogo em vez de encher a CONFIGURAÇÃO com campos que só
        /// servem um tipo de cada vez — metade ficaria sempre cinzenta,
        /// consoante o que se estivesse a medir. Grava directamente nas
        /// classes estáticas que o TSKRET, o TSKPOLF e o TSKCONTAR já lêem, e
        /// por isso não há mais nada a fazer aqui depois de fechar o diálogo.
        /// </summary>
        private void AbrirDefinicoesDoTipo()
        {
            using (var dlg = new DefinicoesTipoDialog())
                dlg.ShowDialog(this);
        }

        /// <summary>
        /// Mede o que já está desenhado. O SyncConfig antes de enviar garante
        /// que o serviço, a altura e a espessura do painel chegam ao comando —
        /// era isso que faltava quando se escrevia TSKMEDSEL à mão.
        /// </summary>
        private void MedirSeleccaoNaPlanta()
        {
            SyncConfig();
            PaletteHost.RunCommand("TSKMEDSEL ");
        }

        private void MedirParedeRet()
        {
            SyncConfig();
            PaletteHost.RunCommand("TSKPAREDERET ");
        }

        /// <summary>
        /// Área em planta × altura. A «Altura» do painel é aqui a espessura da
        /// camada — o campo é o mesmo, o significado é o da medição que se está
        /// a fazer, e o rótulo no desenho di-lo por extenso para não haver
        /// dúvida sobre o que foi multiplicado por quê.
        /// </summary>
        private void MedirArea()
        {
            SyncConfig();
            PaletteHost.RunCommand("TSKAREA ");
        }

        /// <summary>
        /// Área × altura sobre hachuras e polylines fechadas que já estão no
        /// desenho do projecto. Mede sobre cópias — o desenho do arquitecto
        /// fica intacto.
        /// </summary>
        private void MedirAreaSeleccao()
        {
            SyncConfig();
            PaletteHost.RunCommand("TSKAREASEL ");
        }

        private void AdicionarVao()
        {
            // Mesma regra dos títulos: manda a célula seleccionada no Excel;
            // sem selecção, a linha da grelha; sem nada, a última medição.
            string handle = PaletteHost.HandleAlvo(SelectedHandleSilencioso(), UltimoHandle());
            if (handle == null)
            {
                PaletteHost.Log("Meça primeiro, ou escolha a medição na grelha / no Excel.");
                return;
            }

            using (var dlg = new VaoDialog())
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                AlvRepo.AdicionarVao(handle, dlg.Resultado);
            }
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// A linha da grelha larga antiga em que se está.
        ///
        /// Só lá é usada, e sai com ela quando a edição estiver toda em
        /// PROPRIEDADES. As acções da paleta já não passam por aqui: usam
        /// <see cref="NoSeleccionado"/>.
        /// </summary>
        private DataGridViewRow LinhaSeleccionada()
        {
            if (_dgv == null) return null;
            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            if (row == null && _dgv.SelectedCells.Count > 0)
                row = _dgv.Rows[_dgv.SelectedCells[0].RowIndex];
            return row;
        }

        /// <summary>Verdadeiro se o nó seleccionado é um vão, não uma parede.</summary>
        private bool LinhaSeleccionadaEhVao()
        {
            var no = NoSeleccionado();
            return no != null && no.Tipo == TipoNo.Vao;
        }

        /// <summary>Apaga o vão do nó seleccionado da parede a que pertence.</summary>
        private void RemoverVaoSeleccionado()
        {
            var no = NoSeleccionado();
            if (no == null || no.Tipo != TipoNo.Vao) return;

            string handle = no.Handle;
            int indice = no.Indice;
            if (handle == null || indice < 0)
            {
                PaletteHost.Log("Não consegui identificar o vão desta linha. " +
                                "Carregue em Atualizar e tente outra vez.");
                return;
            }

            // O rótulo do nó é "Vão · P01": tira-se o prefixo para a pergunta
            // ficar em português corrente.
            string nome = (no.Rotulo ?? "").Replace("Vão ·", "").Trim();
            if (nome.Length == 0) nome = "este vão";

            var resp = MessageBox.Show(
                "Apagar o vão " + nome + " desta medição?", "TSK TakeOff",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (resp != DialogResult.Yes) return;

            if (AlvRepo.RemoverVao(handle, indice))
                PaletteHost.Log("Vão " + nome + " apagado.");
            else
                PaletteHost.Log("Não foi possível apagar o vão " + nome + ".");

            PaletteHost.RefreshData();
        }

        private void RemoverParede()
        {
            // Um grupo não se remove: não é uma medição, é uma arrumação. Sem
            // isto, «Remover» com um piso seleccionado ou não fazia nada ou —
            // pior — apagava a primeira medição lá de dentro.
            var sel = NoSeleccionado();
            if (sel != null && sel.EhGrupo)
            {
                PaletteHost.Log("«" + sel.Rotulo + "» é um grupo. Escolha a medição, " +
                                "o vão ou o título que quer remover.");
                return;
            }

            // Nó de título: tira-se o título, a medição fica.
            if (sel != null && sel.Tipo == TipoNo.Titulo)
            {
                string h = sel.Handle;
                if (h == null) return;

                // O nível vem do modelo, não do texto da célula: o rótulo é
                // para ler, e lê-lo de volta para decidir o que apagar era
                // fazer depender uma escrita no desenho de uma cadeia de UI.
                string marcaT = ValorPropriedade(sel.Propriedades, "nivelTitulo") == "Capítulo"
                    ? "CAP" : "ART";
                string nivel = marcaT == "CAP" ? "Capítulo" : "Artigo";

                if (MessageBox.Show("Apagar esta linha de " + nivel.ToLowerInvariant() + "?",
                        "TSK TakeOff", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

                // AlternarTitulo com a marca presente leva o texto com ela —
                // senão reaparecia sozinho da próxima vez que se pusesse um
                // título.
                AlvRepo.AlternarTitulo(h, marcaT);
                PaletteHost.Log(nivel + " apagado.");
                PaletteHost.RefreshData();
                return;
            }

            // Linha de vão: apaga-se o vão, não a parede. Antes isto só dizia
            // "use o painel de vãos" — e ficava-se preso, porque um vão que
            // saiu na medição errada tem de poder desaparecer dali.
            if (LinhaSeleccionadaEhVao())
            {
                RemoverVaoSeleccionado();
                return;
            }

            string handle = SelectedHandle();
            if (handle == null) return;

            var resp = MessageBox.Show(
                "Apagar a parede selecionada do desenho?", "TSK TakeOff",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (resp != DialogResult.Yes) return;

            AlvRepo.RemoverParede(handle);
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Linha em branco. Com uma medição seleccionada, marca-a (a linha sai
        /// antes dela). Sem nada seleccionado, fica à espera da próxima medição
        /// — que é o caso normal: medem-se três paredes, carrega-se no botão, e
        /// o bloco seguinte começa separado.
        /// </summary>
        private void AlternarSeparador()
        {
            string handle = PaletteHost.HandleAlvo(SelectedHandleSilencioso(), UltimoHandle());

            if (handle == null)
            {
                Config.SeparadorPendente = !Config.SeparadorPendente;
                PaletteHost.Log(Config.SeparadorPendente
                    ? "Linha em branco activada: a próxima medição começa um bloco novo."
                    : "Linha em branco desactivada.");
                return;
            }

            string onde = DescreverAlvo(handle), quem = OrigemDoAlvo(handle);

            // Quantas ficam depois de somar. Lido da lista que já temos em
            // memória, para não ir ao desenho outra vez só por causa disto.
            int antes = 0;
            foreach (var pd in _paredes)
                if (pd.Handle == handle) { antes = pd.LinhasEmBrancoDepois; break; }
            int agora = antes + 1;
            if (agora > Parede.MaxLinhasEmBranco) agora = 0;

            AlvRepo.AcrescentarSeparadorDepois(handle);

            PaletteHost.Log(agora == 0
                ? "Linhas em branco removidas por baixo da medição " + onde +
                  "  [alvo: " + quem + "]"
                : agora + " linha(s) em branco por baixo da medição " + onde +
                  "  [alvo: " + quem + "]  (carregue outra vez para somar; " +
                  "ao passar de " + Parede.MaxLinhasEmBranco + " volta a zero)");
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Marca um título (capítulo ou artigo) para sair antes da próxima
        /// medição. A linha sai vazia, com o estilo e a altura do modelo —
        /// escreve-se o texto no Excel. Carregar outra vez desliga.
        /// </summary>
        private void MarcarTitulo(string marca, string nome)
        {
            // Onde sai o título, por ordem de prioridade:
            //   1. a célula seleccionada no Excel — apontas na folha e sai ali;
            //   2. a linha seleccionada na grelha da paleta;
            //   3. a última medição, que é o caso normal enquanto se mede.
            string handle = PaletteHost.HandleAlvo(SelectedHandleSilencioso(), UltimoHandle());

            if (handle == null)
            {
                PaletteHost.Log("Meça primeiro: o título sai por baixo de uma medição.");
                return;
            }

            // Acrescenta o nível, ou tira-o se já lá estiver. Os outros níveis
            // ficam: um sub-artigo por baixo de um artigo não apaga o artigo.
            bool tinha = MarcaDe(handle).Contains(marca);
            AlvRepo.AlternarTitulo(handle, marca);

            if (tinha)
            {
                PaletteHost.Log(nome + " removido da medição " + DescreverAlvo(handle) + ".");
                PaletteHost.RefreshData();
                return;
            }

            // O TEXTO VEM DO CAMPO "Texto do título" DO PAINEL.
            //
            // Esse campo mostra sempre o que vai ser escrito: escolher um
            // artigo no mapa preenche-o, escrever por cima manda. Foi assim
            // que se resolveu a objecção que tinha feito desaparecer o campo —
            // duas fontes para a mesma coisa põem a folha a discordar de si
            // própria — sem deixar de fora o que o mapa não tem: o "SEM REF",
            // o capítulo escrito à mão, o artigo ainda por orçamentar. A fonte
            // visível é uma só, e é esta.
            //
            // Vazio, a linha sai vazia (como saía antes) e escreve-se
            // directamente no Excel, que continua a ser válido.
            string codigo, texto;
            PaletteHost.SepararTitulo(_txtTitulo != null ? _txtTitulo.Text : "", out codigo, out texto);

            if (codigo.Length > 0 || texto.Length > 0)
            {
                // Em que posição ficou o nível recém-criado: a ordem é sempre
                // capítulo, artigo, sub-artigo, independentemente da ordem por
                // que se carregou nos botões.
                int i = 0;
                foreach (string mm in MarcaDe(handle).Split(';'))
                {
                    if (mm == marca) break;
                    if (mm.Length > 0) i++;
                }
                AlvRepo.DefinirTextoDeTitulo(handle, i, codigo + "\u001f" + texto);
            }

            PaletteHost.Log("Linha de " + nome.ToLowerInvariant() +
                            " acrescentada por baixo da medição " + DescreverAlvo(handle) +
                            "  [alvo: " + OrigemDoAlvo(handle) + "]" +
                            (texto.Length > 0 ? " com o texto do painel." : ""));
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// A medição para onde vai o que se acrescentar sem escolher nada.
        ///
        /// Primeiro a que se acabou de medir — ver PaletteHost.UltimaMedicao —
        /// e só depois a última LINHA da grelha. Desde que a grelha passou a
        /// sair por ordem do articulado, as duas deixaram de coincidir, e era
        /// por isso que um vão acrescentado logo a seguir a medir descontava
        /// noutra parede qualquer.
        ///
        /// Confirma-se que ainda existe: pode ter sido apagada, ou ser de outro
        /// desenho, e nesse caso volta-se ao que isto sempre fez.
        ///
        /// LIDO DA FONTE, NÃO DA ÁRVORE VISÍVEL. Um filtro é uma lente: a
        /// medição que se acabou de fazer continua a ser o alvo do vão
        /// seguinte, mesmo que o filtro em vigor a esteja a esconder. Ir buscá-la
        /// às linhas visíveis punha um filtro a mudar onde o vão descontava.
        /// </summary>
        private string UltimoHandle()
        {
            string acabada = PaletteHost.UltimaMedicao;
            if (acabada != null)
                foreach (var p in _paredes)
                    if (p.Handle == acabada) return acabada;

            for (int i = _paredes.Count - 1; i >= 0; i--)
                if (_paredes[i].Handle != null) return _paredes[i].Handle;

            return null;
        }

        private string MarcaDe(string handle)
        {
            foreach (var p in _paredes)
                if (p.Handle == handle) return p.MarcaDepois ?? "";
            return "";
        }

        /// <summary>Apaga todas as medições de alvenaria do desenho, com confirmação.</summary>
        private void LimparTudo()
        {
            // A FONTE COMPLETA, NUNCA AS LINHAS VISÍVEIS.
            //
            // São duas armadilhas na mesma operação. A primeira: com um filtro
            // aplicado, percorrer as linhas à vista apagava só essas — e o
            // botão diz «limpar tudo», portanto quem o carrega fica convencido
            // de que o desenho ficou limpo. A segunda: a árvore repete o handle
            // da parede nos vãos e nos títulos dela, por isso a mesma medição
            // aparecia várias vezes e era apagada várias vezes, inflacionando a
            // contagem que sai no fim.
            var handles = new List<string>();
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in _paredes)
                if (p.Handle != null && vistos.Add(p.Handle)) handles.Add(p.Handle);

            if (handles.Count == 0)
            {
                PaletteHost.Log("Não há medições de alvenaria para limpar.");
                return;
            }

            // Dizer que o filtro NÃO protege nada. Quem tem o PISO 1 filtrado
            // à frente e carrega em «Limpar tudo» tem de saber, ANTES de
            // confirmar, que também vão os outros pisos.
            string aviso = _estadoResultados != null && _estadoResultados.AFiltrar
                ? "\n\nATENÇÃO: há um filtro aplicado, mas isto apaga TODAS as " +
                  "medições do desenho, não só as que estão à vista."
                : "";

            var resp = MessageBox.Show(
                string.Format("Apagar as {0} medição(ões) de alvenaria deste desenho?{1}\n\n" +
                              "Esta acção não pode ser desfeita pelo painel " +
                              "(mas o CTRL+Z do AutoCAD ainda funciona).",
                              handles.Count, aviso),
                "TSK TakeOff — limpar tudo",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (resp != DialogResult.Yes) return;

            int apagadas = 0;
            foreach (string handle in handles)
                if (AlvRepo.RemoverParede(handle)) apagadas++;

            PaletteHost.Log(apagadas + " medição(ões) de alvenaria apagada(s).");
            PaletteHost.RefreshData();
        }

        private void ToggleExcel()
        {
            try
            {
                if (PaletteHost.Excel.Conectado)
                {
                    PaletteHost.Excel.Desconectar();
                    _btnExcel.Checked = false;
                }
                else
                {
                    PaletteHost.Excel.Conectar(NomeDoDesenho());
                    _btnExcel.Checked = true;
                    PaletteHost.RefreshData();
                    PaletteHost.Log(PaletteHost.Excel.ModoModelo
                        ? "Excel ao vivo sobre o modelo da casa."
                        : "Excel ao vivo em folha simples (TSKMODELO regista o modelo da casa).");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Não foi possível conectar ao Excel: " + ex.Message,
                    "Medições", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Nome do DWG activo, para baptizar a cópia de trabalho do modelo.</summary>
        private static string NomeDoDesenho()
        {
            try
            {
                var doc = Autodesk.AutoCAD.ApplicationServices.Application
                    .DocumentManager.MdiActiveDocument;
                string nome = doc == null
                    ? null
                    : System.IO.Path.GetFileNameWithoutExtension(doc.Name);
                return string.IsNullOrWhiteSpace(nome) ? "desenho" : nome;
            }
            catch { return "desenho"; }
        }

        private void Exportar()
        {
            PaletteHost.RunCommand("TSKEXPORT ");
        }
    }

    /// <summary>Diálogo de cadastro de vão (porta/janela).</summary>
    public class VaoDialog : Form
    {
        private TextBox _txtDesignacao;
        private NumericUpDown _numLargura;
        private NumericUpDown _numAltura;
        private NumericUpDown _numQtd;
        private ComboBox _cmbTipo;
        private CheckBox _chkPreAro;

        public Vao Resultado { get; private set; }

        public VaoDialog()
        {
            Text = "Adicionar Vão";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(300, 250);

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                Padding = new Padding(10)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _txtDesignacao = new TextBox { Text = "", Dock = DockStyle.Fill };
            _numLargura = Num(0.80M);
            _numAltura = Num(2.10M);
            _numQtd = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 1, Dock = DockStyle.Fill };
            _cmbTipo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            _cmbTipo.Items.Add("Porta");
            _cmbTipo.Items.Add("Janela");
            _cmbTipo.SelectedIndex = 0;
            _chkPreAro = new CheckBox { Text = "Possui pré-aro", Dock = DockStyle.Fill };

            table.Controls.Add(Lbl("Designação:"), 0, 0); table.Controls.Add(_txtDesignacao, 1, 0);
            table.Controls.Add(Lbl("Largura (m):"), 0, 1); table.Controls.Add(_numLargura, 1, 1);
            table.Controls.Add(Lbl("Altura (m):"), 0, 2); table.Controls.Add(_numAltura, 1, 2);
            table.Controls.Add(Lbl("Quantidade:"), 0, 3); table.Controls.Add(_numQtd, 1, 3);
            table.Controls.Add(Lbl("Tipo:"), 0, 4); table.Controls.Add(_cmbTipo, 1, 4);
            table.Controls.Add(new Label(), 0, 5); table.Controls.Add(_chkPreAro, 1, 5);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 40,
                Padding = new Padding(6)
            };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel };
            btnOk.Click += (s, e) =>
            {
                Resultado = new Vao
                {
                    Designacao = (_txtDesignacao.Text ?? "").Trim().ToUpperInvariant(),
                    Largura = (double)_numLargura.Value,
                    Altura = (double)_numAltura.Value,
                    Quantidade = (int)_numQtd.Value,
                    Tipo = _cmbTipo.SelectedIndex == 1 ? TipoVao.Janela : TipoVao.Porta,
                    Espessura = _chkPreAro.Checked ? Config.Espessura : 0.0,
                    PreAro = _chkPreAro.Checked
                };
            };
            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
            Controls.Add(table);
            Controls.Add(buttons);
        }

        private static NumericUpDown Num(decimal valorInicial)
        {
            return new NumericUpDown
            {
                DecimalPlaces = 2,
                Increment = 0.05M,
                Minimum = 0.05M,
                Maximum = 50M,
                Value = valorInicial,
                Dock = DockStyle.Fill
            };
        }

        private static Label Lbl(string text)
        {
            return new Label { Text = text, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
        }
    }
}
