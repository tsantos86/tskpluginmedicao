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
                    _ps.Add("Contagens", _ctrlContagem);
                    _ps.Add("Materiais", _ctrlFachada);
                    _ps.Add("Lineares", _ctrlLinear);
                    _ps.Add("Alvenaria", _ctrl);
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

                _ctrl.BindData(paredes);
                cron.Marcar("grelha alvenaria");
                _ctrlFachada.BindData(fachadas);
                _ctrlLinear?.BindData(lineares);
                _ctrlContagem?.BindData(contagens);
                cron.Marcar("outras grelhas");

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

    /// <summary>Conteúdo da paleta: configurações, botões com ícones e grade ao vivo.</summary>
    public class MedPanelControl : UserControl
    {
        private TextBox _txtServico;
        private ComboBox _cmbAlcado;
        private TextBox _txtBloco;
        private ComboBox _cmbPiso;
        private Button _btnCor;
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
        private ToolStripButton _btnExcel;
        private bool _carregando;

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

            // ----- Configurações (topo) -----
            var config = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Padding = new Padding(8)
            };
            config.ColumnCount = 3;
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            config.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));

            _txtServico = new TextBox { Text = Config.Servico, Dock = DockStyle.Fill };

            FachadaConfig.Carregar();
            _cmbPiso = new ComboBox { Dock = DockStyle.Fill };
            foreach (var piso in FachadaConfig.CoresPiso.Keys) _cmbPiso.Items.Add(piso);
            _cmbPiso.Text = Config.Piso;
            _cmbPiso.TextChanged += (s, e) => AtualizarCor();

            _btnCor = new Button { Text = "Cor…", Dock = DockStyle.Fill };
            _btnCor.Click += (s, e) => EscolherCor();

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
            config.Controls.Add(Rot("Artigo do mapa:"), 0, 0);
            config.Controls.Add(_cmbArtigoMqt, 1, 0);
            config.Controls.Add(new Label(), 2, 0);
            config.Controls.Add(new Label(), 0, 1);
            config.Controls.Add(_lblArtigoMqt, 1, 1);
            config.Controls.Add(new Label(), 2, 1);

            // Logo por baixo do artigo, que é de onde o texto vem quando vem
            // do mapa. O rótulo diz "título" e não "artigo" porque o mesmo
            // campo serve os dois botões, Capítulo e Artigo.
            config.Controls.Add(Rot("Texto do título:"), 0, 2);
            config.Controls.Add(_txtTitulo, 1, 2);
            config.Controls.Add(new Label(), 2, 2);

            // Já não diz "/ Layer": a layer tem campo próprio. O serviço passou
            // a ser só o que identifica a medição na folha do cliente.
            config.Controls.Add(Rot("Serviço:"), 0, 3);
            config.Controls.Add(_txtServico, 1, 3);
            config.Controls.Add(new Label(), 2, 3);

            // Deixou de ser só torre/fracção: o que aqui estiver arranca como
            // designação da medição na folha, e é onde se escreve o "WC1" ou
            // o "quarto 2". O rótulo tem de o dizer, senão ninguém adivinha.
            config.Controls.Add(Rot("Bloco / etiqueta:"), 0, 4);
            config.Controls.Add(_txtBloco, 1, 4);
            config.Controls.Add(new Label(), 2, 4);

            // A seguir ao bloco porque é dele — e do artigo — que o nome sai
            // quando o campo fica vazio.
            config.Controls.Add(Rot("Layer (opcional):"), 0, 5);
            config.Controls.Add(_txtLayer, 1, 5);
            config.Controls.Add(new Label(), 2, 5);
            config.Controls.Add(new Label(), 0, 6);
            config.Controls.Add(_lblLayer, 1, 6);
            config.Controls.Add(new Label(), 2, 6);
            // "(opcional)" no rótulo porque ele JÁ o é — o SyncConfig trata o
            // campo vazio como "sem piso" e a folha não emite cabeçalho nenhum.
            // Só que a caixa abre com "PISO 0" lá dentro e o rótulo não dizia
            // nada, e assim ninguém adivinha que se pode apagar.
            config.Controls.Add(Rot("Piso (opcional):"), 0, 7);
            config.Controls.Add(_cmbPiso, 1, 7);
            config.Controls.Add(_btnCor, 2, 7);
            config.Controls.Add(Rot("Alçado / zona:"), 0, 8);
            config.Controls.Add(_cmbAlcado, 1, 8);
            config.Controls.Add(new Label(), 2, 8);

            config.Controls.Add(Rot("Altura parede (m):"), 0, 9);
            config.Controls.Add(_numAltura, 1, 9);
            config.Controls.Add(new Label(), 2, 9);
            config.Controls.Add(Rot("Espessura (m):"), 0, 10);
            config.Controls.Add(_numEspessura, 1, 10);
            config.Controls.Add(new Label(), 2, 10);
            config.Controls.Add(Rot("Regra de vãos:"), 0, 11);
            config.Controls.Add(_cmbRegra, 1, 11);
            config.Controls.Add(new Label(), 2, 11);


            AtualizarCor();
            MostrarLayerEfectiva();

            // ----- Barra de botões com ícones -----
            var tools = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(24, 24),
                Padding = new Padding(6, 4, 6, 4),
                ShowItemToolTips = true,
                RenderMode = ToolStripRenderMode.System
            };

            tools.Items.Add(MakeButton("Retângulo", IconFactory.ParedeRet(), (s, e) => MedirParedeRet()));
            tools.Items.Add(MakeButton("Polyline", IconFactory.ParedePoly(), (s, e) => MedirParede()));
            // Área: para as camadas — betonilhas, enchimentos, impermeabilizações.
            // Desenha-se o contorno, sai o preenchimento, e a medição é a área
            // vezes a altura do painel (que aqui é a espessura da camada).
            tools.Items.Add(MakeButton("Área", IconFactory.Area(), (s, e) => MedirArea()));
            // E a mesma medição sobre o que o projecto já traz desenhado: os
            // pavimentos vêm hachurados e os compartimentos fechados, e
            // redesenhar o contorno por cima era trabalho a dobrar.
            tools.Items.Add(MakeButton("Área da Selecção", IconFactory.AreaSel(),
                (s, e) => MedirAreaSeleccao()));
            // Havia DOIS botões para isto, com grafias diferentes: um chamava
            // o comando directamente, o outro passava pelo SyncConfig antes.
            // Fica o que sincroniza o painel — o outro media com a altura e a
            // espessura antigas se elas tivessem sido mudadas e ainda não
            // aplicadas, e ninguém perceberia porquê.
            tools.Items.Add(MakeButton("Medir Selecção", IconFactory.MedirSel(),
                (s, e) => MedirSeleccaoNaPlanta()));
            tools.Items.Add(MakeButton("Adicionar Vão", IconFactory.Vao(), (s, e) => AdicionarVao()));
            tools.Items.Add(new ToolStripSeparator());
            _btnExcel = MakeButton("Excel ao Vivo", IconFactory.Excel(), (s, e) => ToggleExcel());
            tools.Items.Add(_btnExcel);
            tools.Items.Add(MakeButton("Exportar XLSX", IconFactory.Exportar(), (s, e) => Exportar()));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(MakeButton("Atualizar", IconFactory.Atualizar(), (s, e) =>
            {
                PaletteHost.RefreshData();
                PaletteHost.EscreverExcelAgora();   // o botão não espera
            }));
            tools.Items.Add(MakeButton("Remover", IconFactory.Remover(), (s, e) => RemoverParede()));
            tools.Items.Add(MakeButton("Linha branca", IconFactory.LinhaBranca(),
                (s, e) => AlternarSeparador()));
            // Ficou só o "Artigo". O capítulo e o sub-artigo saíam do mapa de
            // quantidades quando ele existe, e escrevê-los à mão ao lado do
            // mapa dava duas fontes para a mesma coisa — que é como uma folha
            // começa a discordar de si própria.
            tools.Items.Add(MakeButton("Artigo", IconFactory.Artigo(),
                (s, e) => MarcarTitulo("ART", "Artigo")));
            // Reclassificar: mudar de artigo o que JÁ está medido. Sem isto, um
            // mapa importado a meio da obra só arrumava as medições feitas
            // depois dele — as de antes ficavam sem artigo, saíam no fim da
            // folha, e a única saída era medir tudo outra vez.
            tools.Items.Add(MakeButton("Reclassificar", IconFactory.Reclassificar(),
                (s, e) => Reclassificar()));
            tools.Items.Add(MakeButton("Limpar tudo", IconFactory.Limpar(), (s, e) => LimparTudo()));

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

            // ----- Totais (rodapé) -----
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
        }

        private static Label Rot(string t) =>
            new Label { Text = t, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };

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
                var cor = FachadaConfig.CorDoPiso(PisoDaCor());
                _btnCor.BackColor = cor;
                _btnCor.ForeColor = cor.GetBrightness() < 0.5 ? Color.White : Color.Black;
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
            foreach (DataGridViewCell c in _dgv.SelectedCells) linhas.Add(c.RowIndex);
            foreach (DataGridViewRow r in _dgv.SelectedRows) linhas.Add(r.Index);
            if (linhas.Count == 0 && _dgv.CurrentRow != null)
                linhas.Add(_dgv.CurrentRow.Index);

            foreach (int i in linhas)
            {
                if (i < 0 || i >= _dgv.Rows.Count) continue;
                if (_linhasTitulo.Contains(i)) continue;
                string h = _dgv.Rows[i].Tag as string;
                if (h != null && vistos.Add(h)) handles.Add(h);
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
            foreach (DataGridViewRow linha in _dgv.Rows)
            {
                if ((linha.Tag as string) != handle) continue;
                if (_linhasVao.Contains(linha.Index) ||
                    _linhasTitulo.Contains(linha.Index)) continue;

                return string.Format("Nº {0} ({1}, {2} m)",
                    linha.Cells["num"].Value,
                    linha.Cells["servico"].Value,
                    linha.Cells["comp"].Value);
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

        public void BindData(List<Parede> paredes)
        {
            // O mapa pode ter acabado de ser importado, ou o utilizador pode
            // ter mudado de desenho. Refazer a lista aqui é o que faz a lista
            // aparecer preenchida logo a seguir ao TSKMQT, sem reabrir nada.
            FiltrarArtigos();

            // Pela ordem da folha, não pela ordem por que foram desenhadas.
            // Assim o Nº da grelha é o mesmo sítio que a linha do Excel, e o
            // "última medição" dos botões quer dizer o mesmo nos dois lados.
            _paredes = FolhaMedicao.OrdenarComoFolha(paredes ?? new List<Parede>());
            paredes = _paredes;

            // Guardar onde estávamos ANTES de deitar a grelha abaixo. Cada
            // medição refaz esta lista, e sem isto a selecção perdia-se de cada
            // vez — ficava-se sem alvo, tudo caía na última medição, e parecia
            // que os botões ignoravam a escolha.
            string handleSel = null;
            int vaoSel = -1;
            var linhaAntes = LinhaSeleccionada();
            if (linhaAntes != null)
            {
                handleSel = linhaAntes.Tag as string;
                if (!_indiceVao.TryGetValue(linhaAntes.Index, out vaoSel)) vaoSel = -1;
                if (!_linhasVao.Contains(linhaAntes.Index)) vaoSel = -1;
            }

            // Acabou de se medir: o cursor vai para a medição nova, não para
            // onde estava. É ela o alvo do vão e do título que venham a seguir.
            // Só se for MESMO desta grelha — pode ser um pano dos Materiais, e
            // aí ficava-se sem selecção nenhuma dos dois lados.
            string nova = PaletteHost.MedicaoNova;
            bool seguirNova = nova != null && paredes.Exists(p => p.Handle == nova);
            if (seguirNova)
            {
                handleSel = nova;
                vaoSel = -1;
            }

            int scroll = _dgv.FirstDisplayedScrollingRowIndex;

            _carregando = true;
            GarantirEstilos();

            // Sem isto, cada Rows.Add dispara um ciclo de layout e um redesenho
            // da paleta. Numa reconstrução de duzentas linhas são duzentos.
            _dgv.SuspendLayout();
            // E nada de medir larguras a meio do enchimento: mede-se uma vez
            // no fim, quando as linhas já lá estão todas.
            _dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _dgv.Rows.Clear();
            _pendentes.Clear();
            _linhasVao.Clear();
            _indiceVao.Clear();
            _linhasTitulo.Clear();
            _indiceTitulo.Clear();
            int n = 1, excesso = 0, semArtigo = 0;
            // Só faz sentido apontar o dedo às medições sem artigo quando há um
            // mapa onde as pôr. Sem mapa, não ter artigo é o normal.
            bool comMapa = MapaQuantidades.Existe;

            foreach (var p in paredes)
            {
                var linha = NovaLinha(
                    n++,
                    MarcaNaGrelha(p),
                    p.Servico,
                    CodigoArtigo(p.Artigo),
                    p.Alcado,
                    p.Bloco,
                    p.Piso,
                    // O total, não só o da geometria: numa hachura que pára nos
                    // vãos, o número que interessa é a parede inteira — e é
                    // esse que vai para o Excel. Mostrar outro aqui punha a
                    // paleta e a folha a discordar.
                    N2(p.ComprimentoTotal),
                    N2(p.Altura),
                    N2(p.Largura),
                    N2(p.Espessura),
                    N2(p.AreaBruta),
                    N2(p.DescontoVaos(Config.Regra)),
                    N2(p.AreaLiquida(Config.Regra)),
                    N2(p.AreaLiquida(Config.Regra)) + " " + Unid(p.Unidade),
                    N2(p.Volume(Config.Regra)),
                    p.PreAroUn,
                    N2(p.PreAroMl));
                linha.Tag = p.Handle;

                if (comMapa && string.IsNullOrEmpty(p.Artigo))
                {
                    var cel = linha.Cells[Col("artigo")];
                    cel.Value = SemArtigo;
                    cel.Style = _celSemArtigo;
                    cel.ToolTipText =
                        "Esta medição ainda não pertence a nenhum artigo do mapa, " +
                        "por isso sai no fim da folha.\nClique para escolher o artigo.";
                    semArtigo++;
                }
                else if (comMapa && MapaQuantidades.Procurar(p.Artigo) == null)
                {
                    // Tem artigo escrito, mas o mapa não o conhece. Não tem
                    // lugar no articulado e sai no fim da folha — tal como as
                    // que não têm artigo nenhum, mas por um motivo diferente e
                    // muito mais difícil de ver: o código está lá escrito, com
                    // ar de estar tudo certo. Foi assim que uma designação
                    // cortada aos 255 caracteres desligou o mapa inteiro sem
                    // dar um único sinal.
                    var cel = linha.Cells[Col("artigo")];
                    cel.Value = CodigoArtigo(p.Artigo) + " ?";
                    cel.Style = _celSemArtigo;
                    cel.ToolTipText =
                        "O mapa importado não conhece este artigo, por isso a " +
                        "medição sai no fim da folha.\nClique em Reclassificar " +
                        "para a ligar a um artigo do mapa.";
                    semArtigo++;
                }

                // Alerta quando os vãos excedem a parede — erro de medição,
                // não pode passar despercebido.
                if (p.DescontoVaos(Config.Regra) > p.AreaBruta + 1e-9)
                {
                    linha.DefaultCellStyle = _estAlerta;
                    linha.Cells[Col("vaos")].Style = _celAlerta;
                    excesso++;
                }

                // Vãos por baixo da parede, indentados — para se ver de onde
                // vem o desconto sem ter de abrir nada. São só de leitura: a
                // parede é que manda, o vão é detalhe dela.
                int ordemVao = 1;
                foreach (var v in p.Vaos)
                    AcrescentarLinhaVao(p, v, n - 1, ordemVao++);

                // O título sai por baixo da medição e dos seus vãos, como na
                // folha. A grelha passa a ser o espelho do Excel.
                var marcas = p.Marcas;
                for (int mi = 0; mi < marcas.Count; mi++)
                    AcrescentarLinhaTitulo(p, n - 1, marcas[mi], p.TextoDaMarca(mi), mi);
            }
            // Só agora é que a grelha vê as linhas, e vê-as todas de uma vez.
            // Tem de ser ANTES do RestaurarSeleccao: ele trabalha sobre linhas
            // da grelha, e enquanto elas estiverem na fila não há lá nada para
            // seleccionar nem para onde deslocar o scroll.
            if (_pendentes.Count > 0) _dgv.Rows.AddRange(_pendentes.ToArray());
            _pendentes.Clear();

            RestaurarSeleccao(handleSel, vaoSel, scroll, seguirNova);
            _dgv.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells;
            _dgv.ResumeLayout();
            _carregando = false;

            // Um total por unidade, nunca as duas na mesma soma. Estava
            // "Área líq.: {soma de todas} m²", e as camadas — que faturam m³ —
            // entravam nessa conta: o número não era m² nem m³.
            var totais = TotaisMedicao.PorUnidade(paredes, Config.Regra);
            var partes = new List<string>();
            foreach (var t in totais)
                partes.Add(N2(t.Value) + " " + Unid(t.Key));

            _lblTotais.Text = string.Format(
                "Medições: {0}   |   Total: {1}   |   Volume: {2} m³   |   Pré-aros: {3} un / {4} m",
                paredes.Count,
                partes.Count == 0 ? "0,00 m²" : string.Join("  ·  ", partes.ToArray()),
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
            // Sem escolha explícita não há "linha seleccionada": a grelha está
            // pousada na primeira linha, não foi ninguém que a pôs lá.
            if (!PaletteHost.GrelhaFoiEscolhida) return null;

            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            if (row == null && _dgv.SelectedCells.Count > 0)
                row = _dgv.Rows[_dgv.SelectedCells[0].RowIndex];
            return row?.Tag as string;
        }

        private string SelectedHandle()
        {
            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            if (row == null && _dgv.SelectedCells.Count > 0)
                row = _dgv.Rows[_dgv.SelectedCells[0].RowIndex];

            string handle = row?.Tag as string;
            if (handle == null)
            {
                MessageBox.Show("Clique numa linha da grade primeiro.", "TSK TakeOff",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            return handle;
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

        /// <summary>Verdadeiro se a linha seleccionada é um vão, não uma parede.</summary>
        private bool LinhaSeleccionadaEhVao()
        {
            var row = LinhaSeleccionada();
            return row != null && _linhasVao.Contains(row.Index);
        }

        /// <summary>
        /// A linha em que se está, venha a selecção por linha ou por célula.
        /// A grelha está em modo CellSelect, portanto na prática vem quase
        /// sempre por célula — procurar só em SelectedRows não encontrava nada.
        /// </summary>
        private DataGridViewRow LinhaSeleccionada()
        {
            var row = _dgv.CurrentRow;
            if (row == null && _dgv.SelectedRows.Count > 0) row = _dgv.SelectedRows[0];
            if (row == null && _dgv.SelectedCells.Count > 0)
                row = _dgv.Rows[_dgv.SelectedCells[0].RowIndex];
            return row;
        }

        /// <summary>Apaga o vão da linha seleccionada da parede a que pertence.</summary>
        private void RemoverVaoSeleccionado()
        {
            var row = LinhaSeleccionada();
            if (row == null) return;

            string handle = row.Tag as string;
            int indice;
            if (handle == null || !_indiceVao.TryGetValue(row.Index, out indice))
            {
                PaletteHost.Log("Não consegui identificar o vão desta linha. " +
                                "Carregue em Atualizar e tente outra vez.");
                return;
            }

            string nome = (row.Cells["servico"].Value ?? "").ToString()
                            .Replace("↳", "").Trim();
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
            // Linha de título: tira-se o título, a medição fica.
            var sel = LinhaSeleccionada();
            if (sel != null && _linhasTitulo.Contains(sel.Index))
            {
                string h = sel.Tag as string;
                if (h == null) return;

                string nivel = (sel.Cells["sep"].Value ?? "título").ToString();
                if (MessageBox.Show("Apagar esta linha de " + nivel.ToLowerInvariant() + "?",
                        "TSK TakeOff", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

                // DefinirMarca com marca vazia leva o texto com ela — senão
                // reaparecia sozinho da próxima vez que se pusesse um título.
                string marcaT = nivel.StartsWith("CAP") ? "CAP" : "ART";
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
        /// Confirma-se que ainda está na grelha: pode ter sido apagada, ou ser
        /// de outro desenho, e nesse caso volta-se ao que isto sempre fez.
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

        private string MarcaDe(string handle)
        {
            foreach (var p in _paredes)
                if (p.Handle == handle) return p.MarcaDepois ?? "";
            return "";
        }

        /// <summary>Apaga todas as medições de alvenaria do desenho, com confirmação.</summary>
        private void LimparTudo()
        {
            int n = _dgv.Rows.Count;
            if (n == 0)
            {
                PaletteHost.Log("Não há medições de alvenaria para limpar.");
                return;
            }

            var resp = MessageBox.Show(
                string.Format("Apagar as {0} medição(ões) de alvenaria deste desenho?\n\n" +
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
                if (handle != null && AlvRepo.RemoverParede(handle)) apagadas++;
            }

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
