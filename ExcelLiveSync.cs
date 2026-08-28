using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace TSKTakeOff
{
    /// <summary>
    /// Espelha as medições numa planilha do Excel ABERTA, em tempo real,
    /// via COM (late binding — não exige referência ao Office, só o Excel instalado).
    ///
    /// Se houver um modelo registado (ver <see cref="ModeloExcel"/>), abre uma
    /// cópia desse modelo e escreve nas colunas H..M das folhas de medição,
    /// deixando intactas as fórmulas auxiliares e as macros Ctrl+M / Ctrl+E / Ctrl+Q.
    /// Sem modelo, cai na folha simples de sempre — o plugin nunca fica preso.
    /// </summary>
    public class ExcelLiveSync
    {
        private dynamic _app;
        private dynamic _wb;
        private dynamic _ws;              // só no modo simples

        private bool _modoModelo;
        private string _folhaModelo;      // nome da folha em branco a duplicar
        private string _ficheiroTrabalho;

        /// <summary>Verdadeiro quando o modelo segue o estilo "Item / Designação…"
        /// em vez do estilo "art / descrição" do XX.xls.</summary>
        private bool _modoItem;
        private MapaItem _mapaItem;

        /// <summary>Última linha escrita em cada folha, para saber o que limpar a seguir.</summary>
        private readonly Dictionary<string, int> _ultimaLinha =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private IList<Parede> _paredes = new List<Parede>();
        private IList<MedFachada> _fachadas = new List<MedFachada>();
        private IList<MedItem> _lineares = new List<MedItem>();
        private IList<MedContagem> _contagens = new List<MedContagem>();

        private const int XlAutomatic = -4105;
        private const int XlCalculationManual = -4135;
        private const int XlCalculationAutomatic = -4105;
        private const int Vermelho = 255;          // BGR

        /// <summary>Verdadeiro quando está a trabalhar sobre uma cópia do modelo da casa.</summary>
        public bool ModoModelo { get { return _modoModelo; } }

        /// <summary>Caminho da cópia de trabalho aberta no Excel (null no modo simples).</summary>
        public string FicheiroTrabalho { get { return _ficheiroTrabalho; } }

        /// <summary>
        /// Porque é que o modelo não foi usado nesta ligação (null se correu bem
        /// ou se nem havia modelo registado).
        /// </summary>
        public string AvisoModelo { get; private set; }

        /// <summary>
        /// Verdadeiro só se o Excel ainda estiver mesmo vivo. Não basta o
        /// campo não ser nulo: se o utilizador fechar o Excel à mão, a
        /// referência COM fica morta e qualquer acesso lança 0x80010114.
        /// Por isso sondamos o objecto e limpamos o estado se ele já se foi.
        /// </summary>
        public bool Conectado
        {
            get
            {
                if (_app == null) return false;
                try
                {
                    var sonda = _app.Version;   // toca no COM: se morreu, lança
                    return sonda != null;
                }
                catch
                {
                    Esquecer();                 // estado morto: começar do zero
                    return false;
                }
            }
        }

        // ==================================================================
        // Ligação
        // ==================================================================

        /// <summary>Abre uma janela do Excel com a folha de medições ao vivo.</summary>
        public void Conectar()
        {
            Conectar(null);
        }

        /// <summary>Abre o Excel; <paramref name="nomeBase"/> dá nome à cópia de trabalho.</summary>
        public void Conectar(string nomeBase)
        {
            Desconectar();      // garante que não sobra nada da sessão anterior
            AvisoModelo = null;

            // Cada ligação cria uma cópia de trabalho do modelo; as antigas
            // ficavam para sempre em %TEMP%\TSKTakeOff.
            ModeloExcel.LimparCopiasAntigas();

            var tipo = Type.GetTypeFromProgID("Excel.Application");
            if (tipo == null)
                throw new InvalidOperationException(
                    "O Excel não está instalado nesta máquina.");

            try
            {
                _app = Activator.CreateInstance(tipo);
                _app.Visible = true;

                // Nada de caixas modais atrás da janela do AutoCAD: é isso que
                // deixa o ecrã preto à espera de um clique que ninguém vê.
                // O Excel pode ser o do próprio utilizador (ROT): guarda-se o
                // valor anterior de CADA definição para o devolver ao desligar.
                try { _displayAlertsAntes = _app.DisplayAlerts; } catch { }
                try { _app.DisplayAlerts = false; } catch { }
                try { _askToUpdateLinksAntes = _app.AskToUpdateLinks; } catch { }
                try { _app.AskToUpdateLinks = false; } catch { }
                try { _alertAntes = _app.AlertBeforeOverwriting; } catch { }
                try { _app.AlertBeforeOverwriting = false; } catch { }
                try { _featureInstallAntes = _app.FeatureInstall; } catch { }
                try { _app.FeatureInstall = 0; } catch { }   // msoFeatureInstallNone

                if (ModeloExcel.Existe)
                {
                    try
                    {
                        AbrirModelo(nomeBase);
                    }
                    catch (Exception ex)
                    {
                        // O modelo não serve, mas o trabalho não pode parar:
                        // segue em folha simples e diz porquê.
                        AvisoModelo = ex.Message;
                        PaletteHost.Log("Modelo não utilizado — " + ex.Message);
                        AbrirSimples();
                    }
                }
                else
                {
                    AbrirSimples();
                }
            }
            catch
            {
                try { if (_app != null) _app.Quit(); } catch { }
                Esquecer();     // nunca deixar meio-ligado
                throw;
            }
        }

        private void AbrirSimples()
        {
            _modoModelo = false;
            _wb = _app.Workbooks.Add();
            _ws = _wb.ActiveSheet;
            _ws.Name = "Medições (ao vivo)";
        }

        private void AbrirModelo(string nomeBase)
        {
            _ficheiroTrabalho = ModeloExcel.CopiaDeTrabalho(nomeBase);

            // Open(Filename, UpdateLinks:=0, ReadOnly:=False) — o 0 evita a
            // pergunta "actualizar ligações?", que abre modal e bloqueia tudo.
            _wb = _app.Workbooks.Open(_ficheiroTrabalho, 0, false);

            if (VistaProtegida())
            {
                // Fechar a janela protegida: senão fica pendurada por trás do AutoCAD.
                try
                {
                    foreach (dynamic pv in _app.ProtectedViewWindows) pv.Close();
                }
                catch { }
                _wb = null;

                throw new InvalidOperationException(
                    "o Excel abriu o modelo em Vista Protegida. " +
                    "Corra TSKMODELODESBLOQUEAR, ou abra o ficheiro no Excel " +
                    "e carregue em \"Activar edição\" uma vez.");
            }

            string diagnostico;
            _mapaItem = null;
            _folhaModelo = DescobrirFolhaModelo(out diagnostico);

            if (_folhaModelo == null)
            {
                // O ficheiro não tem o cabeçalho esperado: melhor avisar do que
                // escrever nas colunas erradas de um documento do cliente.
                try { _wb.Close(false); } catch { }
                _wb = null;
                throw new InvalidOperationException(
                    "nenhuma folha do modelo tem um cabeçalho de medições reconhecível " +
                    "('art'/'descrição', ou 'Item'/'Designação'). O que foi encontrado: " +
                    diagnostico);
            }

            _modoModelo = true;
            _ultimaLinha.Clear();
        }

        /// <summary>
        /// Verdadeiro quando o utilizador está a escrever numa célula.
        /// Interactive=false é o sinal fiável: o Excel bloqueia a automação
        /// enquanto a edição não é confirmada.
        /// </summary>
        private bool EmEdicao()
        {
            try { return !(bool)_app.Interactive; }
            catch { return true; }   // na dúvida, não mexer
        }

        /// <summary>Em Vista Protegida o livro existe mas está inacessível.</summary>
        private bool VistaProtegida()
        {
            try { return (int)_app.ProtectedViewWindows.Count > 0; }
            catch { return false; }
        }

        /// <summary>Até que linha vale a pena procurar um cabeçalho de medições.</summary>
        private const int LinhaMaximaCabecalho = 40;

        /// <summary>
        /// Procura a folha a duplicar. Reconhece dois estilos:
        ///   • o do "XX.xls" (linha com "art" em A e "descrição" noutra coluna);
        ///   • o de um mapa de medições normal (linha com "Item" e "Designação").
        /// Entre as candidatas do mesmo estilo, escolhe a mais vazia — para não
        /// arrastar as medições de outro capítulo/obra para a cópia.
        /// </summary>
        private string DescobrirFolhaModelo(out string diagnostico)
        {
            string melhor = null;
            bool melhorEhItem = false;
            MapaItem melhorMapa = null;
            double menos = double.MaxValue;
            var visto = new List<string>();

            foreach (dynamic folha in _wb.Sheets)
            {
                try
                {
                    int linhaClassico; MapaItem mapa;
                    bool ehClassico = ProcurarClassico(folha, out linhaClassico);
                    if (!ehClassico) mapa = ProcurarItem(folha);
                    else mapa = null;

                    if (visto.Count < 12)
                    {
                        string resumo = ehClassico ? "estilo art/descrição (linha " + linhaClassico + ")"
                            : mapa != null ? "estilo Item/Designação (linha " + mapa.LinhaCabecalho + ")"
                            : "sem cabeçalho reconhecido (A1=" + Celula(folha, "A1") + ")";
                        visto.Add((string)folha.Name + " [" + resumo + "]");
                    }

                    if (!ehClassico && mapa == null) continue;

                    string colDescricao = ehClassico ? "H" : ColunaLetra(mapa.ColDesc);
                    int linhaBase = ehClassico ? FolhaTemplate.PrimeiraLinha : mapa.LinhaCabecalho + 1;
                    int linhaFim = ehClassico ? FolhaTemplate.UltimaLinhaModelo : linhaBase + 2000;

                    double preenchidas;
                    try
                    {
                        preenchidas = (double)_app.WorksheetFunction.CountA(
                            folha.Range[colDescricao + linhaBase + ":" + colDescricao + linhaFim]);
                    }
                    catch { preenchidas = 0; }

                    if (preenchidas < menos)
                    {
                        menos = preenchidas;
                        melhor = (string)folha.Name;
                        melhorEhItem = !ehClassico;
                        melhorMapa = mapa;
                    }
                }
                catch { /* folhas de gráfico e afins não têm Range */ }
            }

            _modoItem = melhorEhItem;
            _mapaItem = melhorMapa;

            diagnostico = visto.Count == 0
                ? "nenhuma folha legível."
                : string.Join("; ", visto.ToArray());
            return melhor;
        }

        /// <summary>Estilo do XX.xls: procura, nas primeiras linhas, "art" numa
        /// célula e "descrição" noutra da mesma linha.</summary>
        private bool ProcurarClassico(dynamic folha, out int linha)
        {
            for (int r = 1; r <= LinhaMaximaCabecalho; r++)
            {
                bool temArt = false, temDescricao = false;
                for (int c = 1; c <= 15; c++)
                {
                    string t = Celula(folha, ColunaLetra(c) + r);
                    if (t == "art") temArt = true;
                    else if (t.StartsWith("descri")) temDescricao = true;
                }
                if (temArt && temDescricao) { linha = r; return true; }
            }
            linha = -1;
            return false;
        }

        /// <summary>Estilo "Item / Designação…": procura o mapa de colunas numa
        /// das primeiras linhas da folha.</summary>
        private MapaItem ProcurarItem(dynamic folha)
        {
            for (int r = 1; r <= LinhaMaximaCabecalho; r++)
            {
                int linha = r;
                var mapa = MapaItem.Detectar(r, c => Celula(folha, ColunaLetra(c) + linha));
                if (mapa != null)
                {
                    ProcurarLinhasExemplo(folha, mapa);
                    return mapa;
                }
            }
            return null;
        }

        /// <summary>Até onde procurar exemplos de cada tipo de linha.</summary>
        private const int LinhasAProcurarExemplo = 120;

        /// <summary>
        /// Encontra, nas linhas já preenchidas do modelo, uma de cada tipo:
        /// faixa de capítulo, subtítulo, artigo e medição. São estas que servem
        /// de molde ao aspecto — assim a folha sai com as cores, os negritos e
        /// as casas decimais da casa, em vez de um estilo inventado pelo plugin.
        /// </summary>
        private void ProcurarLinhasExemplo(dynamic folha, MapaItem m)
        {
            int inicio = m.LinhaCabecalho + 1;
            int fim = inicio + LinhasAProcurarExemplo;

            for (int r = inicio; r <= fim; r++)
            {
                bool temTudo = m.LinhaCapitulo > 0 && m.LinhaSubtitulo > 0 &&
                               m.LinhaArtigo > 0 && m.LinhaMedicao > 0;
                if (temTudo) break;

                try
                {
                    string desc = Celula(folha, ColunaLetra(m.ColDesc) + r);
                    if (desc.Length == 0 && m.LinhaMedicao <= 0)
                    {
                        // linha de medição pode não ter descrição: decide-se pelos números
                    }
                    else if (desc.Length == 0) continue;

                    bool temNumeros = Numero(folha, m.ColComp, r) || Numero(folha, m.ColQt, r);
                    bool temUnidade = m.ColUn > 0 &&
                        Celula(folha, ColunaLetra(m.ColUn) + r).Length > 0;
                    bool temSoma = m.ColTotais > 0 &&
                        Formula(folha, m.ColTotais, r).ToUpperInvariant().Contains("SUM(");

                    // medição: tem dimensões preenchidas
                    if (m.LinhaMedicao <= 0 && temNumeros)
                    {
                        m.LinhaMedicao = r;
                        if (m.ColParcial > 0)
                            m.FormulaUnitariaR1C1 = FormulaR1C1(folha, m.ColParcial, r);
                        if (m.ColTotais > 0)
                            m.FormulaTotalLinhaR1C1 = FormulaR1C1(folha, m.ColTotais, r);
                        continue;
                    }

                    // artigo: tem unidade e o total do bloco, mas não tem dimensões
                    if (m.LinhaArtigo <= 0 && temUnidade && temSoma && !temNumeros)
                    {
                        m.LinhaArtigo = r;
                        continue;
                    }

                    // títulos: só texto. Com fundo = capítulo; sem fundo = subtítulo.
                    if (desc.Length > 0 && !temNumeros && !temUnidade)
                    {
                        bool comFundo = TemFundo(folha, m, r);
                        if (comFundo && m.LinhaCapitulo <= 0) m.LinhaCapitulo = r;
                        else if (!comFundo && m.LinhaSubtitulo <= 0) m.LinhaSubtitulo = r;
                    }
                }
                catch { }
            }
        }

        private static bool Numero(dynamic folha, int coluna, int linha)
        {
            if (coluna <= 0) return false;
            try
            {
                var v = folha.Cells[linha, coluna].Value2;
                return v is double;
            }
            catch { return false; }
        }

        private static string Formula(dynamic folha, int coluna, int linha)
        {
            try { return (string)folha.Cells[linha, coluna].Formula ?? ""; }
            catch { return ""; }
        }

        private static string FormulaR1C1(dynamic folha, int coluna, int linha)
        {
            try
            {
                string f = (string)folha.Cells[linha, coluna].FormulaR1C1 ?? "";
                return f.StartsWith("=") ? f : null;
            }
            catch { return null; }
        }

        /// <summary>Verdadeiro se a linha tem cor de fundo (faixa de capítulo).</summary>
        private static bool TemFundo(dynamic folha, MapaItem m, int linha)
        {
            try
            {
                var idx = folha.Cells[linha, m.ColDesc].Interior.ColorIndex;
                // xlColorIndexNone = -4142
                return idx != null && (int)idx != -4142;
            }
            catch { return false; }
        }

        private static string ColunaLetra(int coluna)
        {
            string s = "";
            while (coluna > 0)
            {
                int resto = (coluna - 1) % 26;
                s = (char)('A' + resto) + s;
                coluna = (coluna - 1) / 26;
            }
            return s;
        }

        /// <summary>
        /// Lê uma célula em minúsculas e sem espaços. Usa Value2 e, se vier
        /// vazia, o texto mostrado — em células unidas o Value2 fica nulo.
        /// </summary>
        private static string Celula(dynamic folha, string endereco)
        {
            string v = "";
            try { v = Texto(folha.Range[endereco].Value2); } catch { }
            if (v.Length == 0)
            {
                try { v = Texto(folha.Range[endereco].Text); } catch { }
            }
            return v;
        }

        public void Desconectar()
        {
            // Salvar o que o utilizador escreveu ANTES de largar o Excel.
            // Sem isto, escrever os artigos e fechar sem medir mais perdia
            // tudo: a recolha só acontecia quando havia nova escrita.
            GuardarTextosPendentes();

            // Devolver ao Excel o que lhe mudámos. Sem isto, um DisplayAlerts
            // desligado numa sessão ao vivo ficava desligado no Excel do
            // utilizador para o resto do dia — perguntas de "guardar?" e
            // avisos a mais nunca mais voltavam.
            ReporDefinicoesExcel();

            // Não fecha o Excel do utilizador — apenas larga as referências.
            TryRelease(_ws);
            TryRelease(_wb);
            TryRelease(_app);
            Esquecer();
        }

        /// <summary>
        /// Percorre as folhas escritas e leva para o DWG o que estiver nas
        /// linhas de título (modo item) ou de artigo (modelo clássico).
        /// Chamado ao desligar e ao fechar o AutoCAD.
        /// </summary>
        public void GuardarTextosPendentes()
        {
            if (!_modoModelo) return;
            // A recolha do modo item precisa do mapa de colunas; sem ele não
            // há nada que se possa ler (guardava-se antes por aqui, e com
            // razão: não se entra no RecolherTextosEscritos sem _mapaItem).
            if (_modoItem && _mapaItem == null) return;
            if (_titulosEscritos.Count == 0) return;

            try
            {
                if (!Conectado || EmEdicao()) return;

                // Cópia das chaves: as recolhas limpam o dicionário.
                var folhas = new List<string>(_titulosEscritos.Keys);
                foreach (string folha in folhas)
                {
                    try
                    {
                        dynamic ws = _wb.Sheets[folha];
                        if (_modoItem) RecolherTextosEscritos(ws, folha);
                        else RecolherArtigosClassicos(ws, folha);
                    }
                    catch { /* folha apagada à mão: não há nada a salvar */ }
                }
            }
            catch { /* nunca impedir o encerramento por causa disto */ }
        }

        /// <summary>Larga o estado sem tocar no COM (usar quando já está morto).</summary>
        /// <summary>
        /// Devolve ao Excel as definições que lhe tínhamos mudado. O Excel pode
        /// ser o do próprio utilizador (instância ROT): deixá-las alteradas
        /// era mudar-lhe o comportamento para o resto do dia.
        /// </summary>
        private void ReporDefinicoesExcel()
        {
            try
            {
                // Sem o _app não há nada para repor, mas os campos limpam-se
                // na mesma — o return antecipado saltava a limpeza.
                if (_app != null)
                {
                    if (_displayAlertsAntes != null)
                        _app.DisplayAlerts = _displayAlertsAntes;
                if (_askToUpdateLinksAntes != null)
                    _app.AskToUpdateLinks = _askToUpdateLinksAntes;
                if (_alertAntes != null)
                    _app.AlertBeforeOverwriting = _alertAntes;
                    if (_featureInstallAntes != null)
                        _app.FeatureInstall = _featureInstallAntes;
                }
            }
            catch { }
            _displayAlertsAntes = null;
            _askToUpdateLinksAntes = null;
            _alertAntes = null;
            _featureInstallAntes = null;
        }

        private void Esquecer()
        {
            _ws = null; _wb = null; _app = null;
            _displayAlertsAntes = null;
            _askToUpdateLinksAntes = null;
            _alertAntes = null;
            _featureInstallAntes = null;
            _modoModelo = false;
            _modoItem = false;
            _mapaItem = null;
            _folhaModelo = null;
            _ficheiroTrabalho = null;
            _lineares = new List<MedItem>();
            _ultimaLinha.Clear();
            _titulosEscritos.Clear();
            _medicoesEscritas.Clear();
            _formaEscrita.Clear();
        }

        // ==================================================================
        // Actualização
        // ==================================================================

        public void Atualizar(IList<Parede> paredes, RegraDesconto regra)
        {
            _paredes = paredes ?? new List<Parede>();
            Redesenhar(regra);
        }

        /// <summary>
        /// Paredes e materiais de uma vez, com uma reescrita só.
        ///
        /// Chamar Atualizar e AtualizarFachadas em sequência reescrevia a folha
        /// inteira DUAS vezes por cada medição — a primeira era deitada fora
        /// pela segunda. É a mesma folha e o mesmo trabalho, feito a dobrar.
        /// </summary>
        public void AtualizarTudo(IList<Parede> paredes, IList<MedFachada> fachadas,
            RegraDesconto regra)
        {
            AtualizarTudo(paredes, fachadas, null, FolhaMedicao.Contagens, regra);
        }

        public void AtualizarTudo(IList<Parede> paredes, IList<MedFachada> fachadas,
            IList<MedContagem> contagens, RegraDesconto regra)
        {
            AtualizarTudo(paredes, fachadas, null, contagens, regra);
        }

        public void AtualizarTudo(IList<Parede> paredes, IList<MedFachada> fachadas,
            IList<MedItem> lineares, IList<MedContagem> contagens,
            RegraDesconto regra)
        {
            _paredes = paredes ?? new List<Parede>();
            _fachadas = fachadas ?? new List<MedFachada>();
            _lineares = lineares ?? new List<MedItem>();
            _contagens = contagens ?? new List<MedContagem>();
            Redesenhar(regra);
        }

        public void AtualizarFachadas(IList<MedFachada> meds)
        {
            _fachadas = meds ?? new List<MedFachada>();
            Redesenhar(Config.Regra);
        }

        private void Redesenhar(RegraDesconto regra)
        {
            if (!Conectado) return;

            // Com uma célula em edição, o Excel recusa quase tudo e ainda não
            // devolve o que está a ser escrito. Reescrever agora apagaria o
            // texto a meio. Melhor não mexer e esperar pela próxima.
            if (EmEdicao())
            {
                PaletteHost.Log("Excel em edição: termine a célula (Enter) e carregue em Atualizar.");
                return;
            }

            // Congelar o Excel enquanto se escreve. Sem isto ele redesenha e
            // RECALCULA a folha inteira a cada fórmula que entra — com algumas
            // centenas de linhas é aí que se vai o tempo, não na escrita.
            object calcAntes = null;
            try
            {
                _app.ScreenUpdating = false;
                try { calcAntes = _app.Calculation; } catch { }
                try { _app.Calculation = XlCalculationManual; } catch { }
                try { _app.EnableEvents = false; } catch { }

                if (_modoModelo) EscreverNoModelo(regra);
                else EscreverSimples(regra);
            }
            catch (COMException ex)
            {
                // Antes isto morria em silêncio: o Excel deixava de actualizar e
                // ninguém sabia porquê. Agora diz o que se passou antes de largar.
                PaletteHost.Log("Excel ao vivo parou [COM 0x" +
                    ex.ErrorCode.ToString("X8") + "]: " + ex.Message);
                ReporDefinicoesExcel();
                Esquecer();
            }
            catch (Exception ex)
            {
                PaletteHost.Log("Excel ao vivo parou [" + ex.GetType().Name + "]: " + ex.Message);
                ReporDefinicoesExcel();
                Esquecer();
            }
            finally
            {
                // Repor pela ordem inversa, e sempre — mesmo que a escrita tenha
                // rebentado a meio. Deixar o Excel em cálculo manual seria pior
                // do que o erro que a levou lá: os totais deixavam de mexer e
                // ninguém perceberia porquê.
                try { if (_app != null) _app.EnableEvents = true; } catch { }
                try
                {
                    if (_app != null)
                        _app.Calculation = calcAntes ?? (object)XlCalculationAutomatic;
                }
                catch { }
                try { if (_app != null) _app.ScreenUpdating = true; } catch { }
            }
        }

        // ------------------------------------------------------------------
        // Modo modelo — escreve nas folhas de capítulo
        // ------------------------------------------------------------------
        private void EscreverNoModelo(RegraDesconto regra)
        {
            if (_modoItem) EscreverNoModeloItem(regra);
            else EscreverNoModeloClassico(regra);
        }

        private void EscreverNoModeloClassico(RegraDesconto regra)
        {
            var folhas = FolhaTemplate.Construir(_paredes, _fachadas, _lineares,
                                                 _contagens, regra);
            foreach (var f in folhas) EscreverFolha(f);
        }

        // ------------------------------------------------------------------
        // Modo item — modelo "Item / Designação…", uma folha por capítulo
        // ------------------------------------------------------------------
        private void EscreverNoModeloItem(RegraDesconto regra)
        {
            int primeiraLinha = _mapaItem.LinhaCabecalho + 1;

            if (Config.UmaFolhaSo)
            {
                // Tudo numa folha. Uma medição é uma medição: separá-la por
                // abas da paleta obrigava a saltar de folha em folha para ler o
                // trabalho de um dia, e a numeração recomeçava em cada uma.
                // O construtor já separa alvenarias, materiais e contagens em
                // capítulos — a estrutura está lá, não precisa de abas.
                var tudo = FolhaMedicao.Construir(_paredes, _fachadas, _lineares,
                                                  _contagens, regra, primeiraLinha);
                if (tudo.Count > 0)
                    EscreverFolhaItem(Config.NomeDaFolha, tudo);
                return;
            }

            if (_paredes != null && _paredes.Count > 0)
            {
                var linhas = FolhaMedicao.Construir(_paredes, null, null,
                                                     null, regra, primeiraLinha);
                EscreverFolhaItem(FolhaTemplate.FolhaAlvenarias, linhas);
            }
            if (_fachadas != null && _fachadas.Count > 0)
            {
                var linhas = FolhaMedicao.Construir(null, _fachadas, null,
                                                     null, regra, primeiraLinha);
                EscreverFolhaItem(FolhaTemplate.FolhaMateriais, linhas);
            }

            if (_lineares != null && _lineares.Count > 0)
            {
                var linhas = FolhaMedicao.Construir(null, null, _lineares,
                                                     null, regra, primeiraLinha);
                EscreverFolhaItem(FolhaTemplate.FolhaLineares, linhas);
            }

            if (_contagens != null && _contagens.Count > 0)
            {
                var linhas = FolhaMedicao.Construir(null, null, null,
                                                     _contagens, regra, primeiraLinha);
                EscreverFolhaItem("CONTAGENS", linhas);
            }
        }

        /// <summary>
        /// Linhas de título/artigo escritas na última passagem: linha do Excel
        /// → handle da medição. É este mapa que permite ir buscar o que o
        /// utilizador escreveu antes de a folha ser reescrita.
        /// </summary>
        private readonly Dictionary<string, Dictionary<int, string>> _titulosEscritos =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Lê o que o utilizador escreveu nas linhas de ARTIGO do modelo
        /// clássico — coluna A (código) e coluna H (designação) — e devolve-o
        /// às medições, antes de a folha ser limpa.
        ///
        /// O modo item faz isto pelas linhas de título (RecolherTextosEscritos);
        /// o modelo clássico não tinha equivalente e o que se escrevesse na
        /// folha perdia-se na reescrita seguinte sem chegar ao DWG.
        /// </summary>
        private void RecolherArtigosClassicos(dynamic ws, string nomeFolha)
        {
            Dictionary<int, string> titulos;
            if (!_titulosEscritos.TryGetValue(nomeFolha, out titulos)) return;
            if (titulos.Count == 0) return;

            int menor = int.MaxValue, maior = int.MinValue;
            foreach (var par in titulos)
            {
                if (par.Key < menor) menor = par.Key;
                if (par.Key > maior) maior = par.Key;
            }
            if (menor > maior) { titulos.Clear(); return; }

            // As duas colunas de uma vez, em vez de duas idas ao COM por linha.
            object[,] itens = LerColuna(ws, FolhaTemplate.C_ART, menor, maior);
            object[,] descs = LerColuna(ws, FolhaTemplate.C_DESC, menor, maior);

            foreach (var par in titulos)
            {
                string handle = par.Value;
                if (string.IsNullOrEmpty(handle) || handle[0] != '@') continue;
                handle = handle.Substring(1);
                if (handle.Length == 0) continue;

                try
                {
                    string codigo = Celula(itens, par.Key, menor);
                    string descricao = Celula(descs, par.Key, menor);
                    if (codigo.Length == 0 && descricao.Length == 0) continue;

                    // A linha de artigo nasce de uma medição do bloco: é ela
                    // que diz o que já está no desenho, para só se gravar o
                    // que mudou e para não se confundir texto do utilizador
                    // com substitutos da folha.
                    Parede fonte = null;
                    foreach (var p in _paredes)
                        if (p.Handle == handle) { fonte = p; break; }

                    // O mesmo para os panos: na folha das Materiais o
                    // substituto da designação é o MATERIAL, e sem isto editar
                    // só o código gravava "ETICS" como designação do artigo.
                    MedFachada fonteFac = null;
                    if (fonte == null)
                        foreach (var x in _fachadas)
                            if (x.Handle == handle) { fonteFac = x; break; }

                    if (fonteFac != null)
                    {
                        string artigoActual = fonteFac.Artigo ?? "";
                        string codigoActual = artigoActual;
                        string descricaoActual = "";
                        int sepF = artigoActual.IndexOf('\u001f');
                        if (sepF >= 0)
                        {
                            codigoActual = artigoActual.Substring(0, sepF);
                            descricaoActual = artigoActual.Substring(sepF + 1);
                        }

                        if (descricaoActual.Length == 0 &&
                            descricao == (fonteFac.Material ?? ""))
                            descricao = "";

                        if (codigo == codigoActual && descricao == descricaoActual)
                            continue;
                    }

                    if (fonte != null)
                    {
                        string artigoActual = fonte.Artigo ?? "";
                        string codigoActual = artigoActual;
                        string descricaoActual = "";
                        int sep = artigoActual.IndexOf('\u001f');
                        if (sep >= 0)
                        {
                            codigoActual = artigoActual.Substring(0, sep);
                            descricaoActual = artigoActual.Substring(sep + 1);
                        }

                        // Quando o artigo não tem designação, a folha mostra o
                        // nome do SERVIÇO na coluna H. Esse texto não é do
                        // utilizador: editar só o código não pode gravá-lo
                        // como se fosse designação do artigo.
                        if (descricaoActual.Length == 0 &&
                            descricao == (fonte.Servico ?? ""))
                            descricao = "";

                        // Nada mudou: não reabrir o desenho para gravar o mesmo.
                        if (codigo == codigoActual && descricao == descricaoActual)
                            continue;
                    }

                    // Guardado como "código\u001f descrição" num campo só, como
                    // o GuardarArtigo do modo item espera.
                    GuardarArtigo(handle, codigo + "\u001f" + descricao);
                }
                catch { /* uma célula ilegível não pode parar a actualização */ }
            }

            titulos.Clear();
        }

        /// <summary>
        /// Lê o que o utilizador escreveu nas linhas de título e devolve-o às
        /// medições, antes de a folha ser limpa.
        ///
        /// Sem isto, o código do artigo e a descrição escritos à mão
        /// desapareciam na medição seguinte — a folha é sempre reconstruída.
        /// </summary>
        private void RecolherTextosEscritos(dynamic ws, string nomeFolha)
        {
            Dictionary<int, string> titulos;
            if (!_titulosEscritos.TryGetValue(nomeFolha, out titulos)) return;
            if (titulos.Count == 0) return;

            var m = _mapaItem;

            // As duas colunas de uma vez, em vez de duas idas ao COM por
            // título. Com dez artigos eram vinte chamadas por actualização.
            int menor = int.MaxValue, maior = int.MinValue;
            foreach (var par in titulos)
            {
                if (par.Key < menor) menor = par.Key;
                if (par.Key > maior) maior = par.Key;
            }
            object[,] itens = LerColuna(ws, m.ColItem, menor, maior);
            object[,] descs = LerColuna(ws, m.ColDesc, menor, maior);

            foreach (var par in titulos)
            {
                int linha = par.Key;
                string handle = par.Value;
                if (string.IsNullOrEmpty(handle)) continue;

                bool deArtigo = handle[0] == '@';
                if (deArtigo) handle = handle.Substring(1);

                // "handle#2" — qual dos títulos desta medição é este. Sem o
                // índice, escrever num título secundário ia parar ao artigo.
                int indiceTitulo = 0;
                int cardinal = handle.IndexOf('#');
                if (cardinal >= 0)
                {
                    int.TryParse(handle.Substring(cardinal + 1), out indiceTitulo);
                    handle = handle.Substring(0, cardinal);
                }
                if (handle.Length == 0) continue;

                try
                {
                    string codigo = Celula(itens, linha, menor);
                    string descricao = Celula(descs, linha, menor);

                    if (codigo.Length == 0 && descricao.Length == 0) continue;

                    // Guardado como "código\u001f descrição" num campo só.
                    string texto = codigo + "\u001f" + descricao;

                    // Cabeçalho de artigo: o texto vale para o artigo todo.
                    if (deArtigo) { GuardarArtigo(handle, texto); continue; }

                    // Só se grava o que mudou. Sem esta comparação, cada
                    // actualização reescrevia todos os títulos no desenho.
                    string actual = null;
                    foreach (var p in _paredes)
                        if (p.Handle == handle) { actual = p.TextoDaMarca(indiceTitulo); break; }
                    if (actual != null && actual == texto) continue;

                    if (!AlvRepo.DefinirTextoDeTitulo(handle, indiceTitulo, texto))
                        // As fachadas também podem ter mais do que um título;
                        // o índice viaja no handle ("#1", "#2") como nas paredes.
                        FacRepo.DefinirTextoDeTitulo(handle, indiceTitulo, texto);
                }
                catch { /* uma célula ilegível não pode parar a actualização */ }
            }

            titulos.Clear();
            RecolherNotasEscritas(ws, nomeFolha);
        }

        /// <summary>
        /// Guarda o que a pessoa escreveu na coluna Designação das linhas de
        /// MEDIÇÃO. Até aqui só se salvavam os títulos: escrever um "U" ou uma
        /// nota numa linha de medição perdia-se na actualização seguinte, o que
        /// é a maneira mais rápida de alguém deixar de confiar na ferramenta.
        /// </summary>
        private void RecolherNotasEscritas(dynamic ws, string nomeFolha)
        {
            Dictionary<int, string> medicoes;
            if (!_medicoesEscritas.TryGetValue(nomeFolha, out medicoes)) return;
            if (medicoes.Count == 0 || _mapaItem == null) return;
            if (_mapaItem.ColDesc <= 0) return;

            // Uma leitura só para a coluna toda. Célula a célula era uma ida ao
            // COM por medição — com meia centena de linhas dava-se pelo atraso
            // a cada vão acrescentado.
            int menor = int.MaxValue, maior = int.MinValue;
            foreach (var par in medicoes)
            {
                if (par.Key < menor) menor = par.Key;
                if (par.Key > maior) maior = par.Key;
            }
            if (menor > maior) { medicoes.Clear(); return; }

            object[,] coluna = LerColuna(ws, _mapaItem.ColDesc, menor, maior);
            if (coluna == null) { medicoes.Clear(); return; }

            // E o que já lá está no desenho, para só gravar o que mudou. Cada
            // gravação é uma transacção no DWG; fazê-las todas de cada vez que
            // se mede era o grosso da espera.
            var actuais = new Dictionary<string, string>();
            foreach (var p in _paredes)
                if (p.Handle != null) actuais[p.Handle] = p.Nota ?? "";

            foreach (var par in medicoes)
            {
                string handle = par.Value;
                if (string.IsNullOrEmpty(handle) || handle[0] == '@') continue;

                string texto;
                try
                {
                    object v = coluna[par.Key - menor + 1, 1];
                    texto = v == null ? "" : Convert.ToString(v).Trim();
                }
                catch { continue; }

                string anterior;
                if (actuais.TryGetValue(handle, out anterior) && anterior == texto)
                    continue;

                try { AlvRepo.DefinirNota(handle, texto); } catch { }
            }
            medicoes.Clear();
        }

        /// <summary>
        /// Reescreve o artigo em TODAS as medições que o partilham. O cabeçalho
        /// sai uma vez na folha, mas o artigo é de todas as medições do bloco —
        /// gravar só naquela de onde o cabeçalho veio partia o bloco em dois
        /// na actualização seguinte.
        /// </summary>
        private void GuardarArtigo(string handle, string texto)
        {
            string anterior = null;
            bool achou = false;
            foreach (var p in _paredes)
                if (p.Handle == handle) { anterior = p.Artigo; achou = true; break; }

            // Os panos da aba Materiais também têm artigo, e o cabeçalho no
            // Excel é o mesmo. Sem este ramo, escrever por cima do artigo de um
            // bloco de fachada ia bater ao AlvRepo, não encontrava lá parede
            // nenhuma e a edição perdia-se em silêncio.
            if (!achou)
            {
                foreach (var f in _fachadas)
                    if (f.Handle == handle)
                    {
                        if ((f.Artigo ?? "") == texto) return;

                        var mesmasFac = new List<string>();
                        foreach (var o in _fachadas)
                            if ((o.Artigo ?? "") == (f.Artigo ?? "")) mesmasFac.Add(o.Handle);
                        foreach (var h in mesmasFac) FacRepo.DefinirArtigo(h, texto);
                        return;
                    }
            }

            // O normal é o texto já estar igual: a folha é reescrita a cada
            // medição e ninguém lhe tocou. Sair já aqui evita reabrir o desenho
            // para gravar o mesmo — era isto que travava o AutoCAD.
            if (achou && (anterior ?? "") == texto) return;
            if (!achou) { AlvRepo.DefinirArtigo(handle, texto); return; }

            var mesmos = new List<string>();
            foreach (var p in _paredes)
                if ((p.Artigo ?? "") == (anterior ?? "")) mesmos.Add(p.Handle);

            AlvRepo.DefinirArtigoEmVarias(mesmos, texto);
        }

        /// <summary>
        /// Que tipo tinha cada linha da última vez que se escreveu esta folha.
        /// É a memória que permite não reformatar o que não mudou.
        /// </summary>
        private readonly Dictionary<string, TipoLinha[]> _formaEscrita =
            new Dictionary<string, TipoLinha[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A primeira linha do Excel cujo TIPO é diferente do da última escrita.
        /// Daí para baixo há que reformatar; acima está tudo igual e o aspecto
        /// já lá está.
        ///
        /// Compara tipos, não valores: mudar um comprimento não muda o aspecto
        /// da linha, mas acrescentar um título no meio muda tudo o que vem
        /// abaixo — e é isso que este cálculo apanha.
        /// </summary>
        private int PrimeiraLinhaAlterada(string nome, List<LinhaFolha> linhas, int primeira)
        {
            var agora = new TipoLinha[linhas.Count];
            for (int i = 0; i < linhas.Count; i++) agora[i] = linhas[i].Tipo;

            TipoLinha[] antes;
            bool tinha = _formaEscrita.TryGetValue(nome, out antes);
            _formaEscrita[nome] = agora;

            // Primeira escrita nesta folha, ou modelo sem linhas-exemplo:
            // formata-se tudo, como sempre se fez.
            if (!tinha || antes == null) return primeira;

            int comum = System.Math.Min(antes.Length, agora.Length);
            for (int i = 0; i < comum; i++)
                if (antes[i] != agora[i]) return primeira + i;

            // Prefixo todo igual: muda só a partir de onde uma delas acaba.
            return primeira + comum;
        }

        /// <summary>Lê um troço de uma coluna para memória, numa chamada só.</summary>
        private static object[,] LerColuna(dynamic ws, int coluna, int menor, int maior)
        {
            if (coluna <= 0 || menor > maior) return null;
            try
            {
                object valor = ws.Range[ws.Cells[menor, coluna],
                                         ws.Cells[maior, coluna]].Value2;
                var matriz = valor as object[,];
                if (matriz != null) return matriz;

                // O Excel devolve um escalar quando o intervalo tem uma só
                // célula — inclusive null quando a célula está vazia — mas
                // devolve uma matriz 1-based quando tem várias. Normaliza o
                // primeiro caso para o mesmo contrato usado por Celula().
                var uma = new object[2, 2];
                uma[1, 1] = valor;
                return uma;
            }
            catch { return null; }
        }

        /// <summary>Uma célula do troço lido, já em texto.</summary>
        private static string Celula(object[,] bloco, int linha, int menor)
        {
            if (bloco == null) return "";
            try
            {
                object v = bloco[linha - menor + 1, 1];
                return v == null ? "" : Convert.ToString(v).Trim();
            }
            catch { return ""; }
        }

        private static string Valor(dynamic ws, int linha, int coluna)
        {
            try
            {
                var v = ws.Cells[linha, coluna].Value2;
                return v == null ? "" : v.ToString().Trim();
            }
            catch { return ""; }
        }

        private void EscreverFolhaItem(string nomeCapitulo, List<LinhaFolha> linhas)
        {
            string nome = FolhaTemplate.NomeDeFolha(ModeloExcel.FolhaDe(nomeCapitulo));

            // Nunca escrever por cima da folha que serve de molde: é dela que se
            // copia o aspecto, e sem ela a folha seguinte sairia sem estilo.
            if (string.Equals(nome, _folhaModelo, StringComparison.OrdinalIgnoreCase))
                nome = FolhaTemplate.NomeDeFolha(nome + " TSK");

            var cron = new Cronometro("Excel: " + nome);

            dynamic ws = ObterFolhaItem(nome);
            cron.Marcar("obter folha");
            if (ws == null || linhas.Count == 0) return;

            // Ler o que o utilizador escreveu ANTES de limpar seja o que for.
            // Estava ao contrário: a folha era limpa dentro do ObterFolhaItem e
            // só depois se tentava ler — as células já vinham vazias e o que se
            // tinha escrito no Excel perdia-se em silêncio.
            RecolherTextosEscritos(ws, nome);
            cron.Marcar("recolher textos");
            cron.Nota(linhas.Count + " linhas a escrever");

            var m = _mapaItem;
            int primeira = m.LinhaCabecalho + 1;
            int ultima = primeira + linhas.Count - 1;

            // A partir de que linha é que a folha muda de FORMA. Entre duas
            // medições quase tudo fica igual: acrescenta-se uma linha no fim e
            // as outras cento e tal continuam a ser do mesmo tipo, logo com o
            // mesmo aspecto. Apagar e repor a formatação de todas para
            // acrescentar uma era o que levava três segundos.
            int mudaEm = PrimeiraLinhaAlterada(nome, linhas, primeira);

            LimparDadosItem(ws, nome, mudaEm);
            cron.Marcar("limpar");

            // Regista as linhas de título desta passagem, para na próxima se
            // saber onde ir buscar o que o utilizador escreveu.
            var titulos = new Dictionary<int, string>();
            // E as linhas de medição, para a célula seleccionada no Excel se
            // poder traduzir de volta para a medição que a gerou.
            var medicoesPorLinha = new Dictionary<int, string>();

            // ---- valores, de uma vez só ----
            int c1 = m.PrimeiraColuna, c2 = m.UltimaColuna;
            int largura = c2 - c1 + 1;
            var dados = new object[linhas.Count, largura];

            var porTipo = new Dictionary<TipoLinha, List<int>>();
            var artigos = new List<int>();
            var medicoes = new List<int>();
            var subgrupos = new List<int>();

            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                int linha = primeira + i;

                if (!porTipo.ContainsKey(l.Tipo)) porTipo[l.Tipo] = new List<int>();
                porTipo[l.Tipo].Add(linha);

                if (l.Tipo == TipoLinha.Vazia) continue;

                Por(dados, i, c1, m.ColDesc, l.Designacao);
                Por(dados, i, c1, m.ColUn, l.Un);
                if (l.Qt.HasValue) Por(dados, i, c1, m.ColQt, l.Qt.Value);
                if (l.Comp.HasValue) Por(dados, i, c1, m.ColComp, Math.Round(l.Comp.Value, 3));
                if (l.Largura.HasValue) Por(dados, i, c1, m.ColLarg, Math.Round(l.Largura.Value, 3));
                if (l.Altura.HasValue) Por(dados, i, c1, m.ColAlt, Math.Round(l.Altura.Value, 3));

                // O capítulo do construtor é, neste modelo, a linha de artigo:
                // é ela que leva a unidade e o total do bloco.
                if (l.Tipo == TipoLinha.Capitulo) artigos.Add(linha);
                if (l.Tipo == TipoLinha.Alcado || l.Tipo == TipoLinha.Piso) subgrupos.Add(linha);

                // Linha de título: guardar de que medição veio, para na próxima
                // passagem se ir buscar aqui o que o utilizador escrever.
                if (!string.IsNullOrEmpty(l.HandleOrigem))
                {
                    // O arroba à frente do handle marca um cabeçalho de artigo:
                    // o que lá se escrever vale para todas as medições dele.
                    titulos[linha] = (l.DeArtigo ? "@" : "") + l.HandleOrigem;

                    // A linha de TÍTULO não entra no mapa das medições.
                    //
                    // Entrava, e o estrago era este: o RecolherNotasEscritas
                    // percorre esse mapa, lê a coluna Designação de cada linha
                    // e grava-a como NOTA da medição. Numa linha de título, o
                    // que lá está é a designação do ARTIGO — "1 - Fornecimento
                    // e aplicação de paredes simples em bloco térmico 25cm…" —
                    // e ia parar à nota da medição que carregava o título. Na
                    // reescrita seguinte esse texto saía dentro da linha de
                    // medição, e ficava gravado no DWG.
                    //
                    // O "@" que marca os cabeçalhos de artigo protegia contra
                    // isto no mapa dos títulos, mas aqui o handle era guardado
                    // em cru e a guarda nunca disparava.
                    //
                    // Fora do mapa é também o que o MedicaoNaCelulaSeleccionada
                    // já dizia esperar: sem título no mapa, clicar num cai na
                    // medição anterior, que é o bloco onde se está.
                    Por(dados, i, c1, m.ColItem, l.Item);
                }
                else if (!string.IsNullOrEmpty(l.Handle))
                {
                    medicoesPorLinha[linha] = l.Handle;
                }

                if (l.Tipo == TipoLinha.Medicao || l.Tipo == TipoLinha.Deducao)
                {
                    // As contagens trazem o valor feito: escreve-se o número na
                    // coluna Parcial e não se lhes aplica a fórmula do modelo,
                    // que multiplicaria por células vazias e daria zero.
                    if (l.ValorParcial.HasValue)
                        Por(dados, i, c1, m.ColParcial, l.ValorParcial.Value);
                    else
                        medicoes.Add(linha);
                }
            }

            dynamic destino = ws.Range[ws.Cells[primeira, c1], ws.Cells[ultima, c2]];
            destino.Value2 = dados;
            cron.Marcar("escrever valores");

            // ---- aspecto, copiado das linhas-exemplo do modelo ----
            // Só as linhas que mudaram de tipo. As de cima já têm o aspecto
            // certo da escrita anterior e ninguém lhes tocou.
            if (mudaEm <= ultima)
            {
                foreach (var lista in porTipo.Values)
                    lista.RemoveAll(l => l < mudaEm);
                AplicarEstiloDoModelo(ws, porTipo);
            }
            cron.Marcar("formatação");
            cron.Nota("formatadas as linhas " + mudaEm + ".." + ultima +
                      " de " + primeira + ".." + ultima);

            // ---- fórmulas ----
            EscreverFormulasItem(ws, medicoes, artigos, subgrupos, linhas, primeira);
            FormatarColunaTotais(ws, linhas, primeira, mudaEm, m.ColTotais);
            cron.Marcar("fórmulas");
            cron.Fim();

            _ultimaLinha[nome] = ultima;
            _titulosEscritos[nome] = titulos;
            _medicoesEscritas[nome] = medicoesPorLinha;
        }

        /// <summary>Põe um valor no array de escrita, se a coluna existir.</summary>
        private static void Por(object[,] dados, int i, int primeiraColuna, int coluna, object valor)
        {
            if (coluna <= 0 || valor == null) return;
            int j = coluna - primeiraColuna;
            if (j >= 0 && j < dados.GetLength(1)) dados[i, j] = valor;
        }

        /// <summary>
        /// Copia o formato das linhas-exemplo do modelo para as linhas escritas.
        /// Faz-se por lotes de endereços para não gastar uma chamada COM por linha.
        /// </summary>
        private void AplicarEstiloDoModelo(dynamic ws, Dictionary<TipoLinha, List<int>> porTipo)
        {
            var m = _mapaItem;
            if (!m.TemEstilo) return;
            _falhasFormato = 0;

            dynamic modelo;
            try { modelo = _wb.Sheets[_folhaModelo]; }
            catch { return; }

            // Cada tipo do construtor vai buscar o molde à linha correspondente
            // do modelo. Quando não há exemplo desse tipo, usa-se o mais próximo.
            Estilo(ws, modelo, porTipo, TipoLinha.Capitulo,
                   m.LinhaArtigo > 0 ? m.LinhaArtigo : m.LinhaCapitulo);
            Estilo(ws, modelo, porTipo, TipoLinha.Alcado,
                   m.LinhaCapitulo > 0 ? m.LinhaCapitulo : m.LinhaSubtitulo);
            Estilo(ws, modelo, porTipo, TipoLinha.Piso,
                   m.LinhaSubtitulo > 0 ? m.LinhaSubtitulo : m.LinhaCapitulo);

            // Negrito e centrado nas linhas de agrupamento, por cima do que o
            // modelo trouxe. Numa obra com cinco pisos e vários alçados, é o
            // que permite percorrer a folha sem a ler toda.
            string colDesc = ColunaLetra(m.ColDesc);
            List<int> agrup;
            if (porTipo.TryGetValue(TipoLinha.Capitulo, out agrup))
                CentrarNegrito(ws, agrup, colDesc);
            if (porTipo.TryGetValue(TipoLinha.Alcado, out agrup))
                CentrarNegrito(ws, agrup, colDesc);
            if (porTipo.TryGetValue(TipoLinha.Piso, out agrup))
                CentrarNegrito(ws, agrup, colDesc);
            // Medições e deduções partilham o mesmo molde, e alternam na folha.
            // Estilizadas em separado, cada linha virava um bloco só dela e uma
            // colagem só dela. Juntas, um artigo inteiro é uma colagem só.
            // (O Copy já está fora do ciclo — ver Estilo — mas cada colagem
            // ainda custa uns milissegundos, e num artigo grande são dezenas.)
            var corpo = new List<int>();
            List<int> parcela;
            if (porTipo.TryGetValue(TipoLinha.Medicao, out parcela)) corpo.AddRange(parcela);
            if (porTipo.TryGetValue(TipoLinha.Deducao, out parcela)) corpo.AddRange(parcela);
            if (corpo.Count > 0)
            {
                var so = new Dictionary<TipoLinha, List<int>> { { TipoLinha.Medicao, corpo } };
                Estilo(ws, modelo, so, TipoLinha.Medicao, m.LinhaMedicao);
            }

            // Títulos inseridos à mão: saem vazios mas com o aspecto e a altura
            // do modelo, prontos para lá escrever.
            Estilo(ws, modelo, porTipo, TipoLinha.TituloCapitulo, m.LinhaCapitulo, true);
            Estilo(ws, modelo, porTipo, TipoLinha.TituloArtigo, m.LinhaArtigo, true);
            // Os títulos suportados são capítulo e artigo. A descrição do
            // artigo pode ser longa, mas não há um terceiro nível para formatar.
            // Alturas fixas, escolhidas no painel, mantêm as medições compactas.
            // Depois do estilo, senão a colagem do formato do modelo escrevia
            // por cima.
            //
            // As duas listas juntas de propósito: medições e deduções alternam,
            // por isso separadas cada linha virava um bloco só dela e uma ida ao
            // COM. Juntas formam um intervalo contíguo por artigo.
            var baixas = new List<int>();
            List<int> parte;
            if (porTipo.TryGetValue(TipoLinha.Medicao, out parte)) baixas.AddRange(parte);
            if (porTipo.TryGetValue(TipoLinha.Deducao, out parte)) baixas.AddRange(parte);
            AlturaLinhas(ws, baixas, Config.AlturaLinhaExcel);

            try { _app.CutCopyMode = false; } catch { }

            if (_falhasFormato > 0)
            {
                PaletteHost.Log(_falhasFormato + " colagens de formato falharam — " +
                                "a folha pode sair sem bandas nem casas decimais. " +
                                "Desligue e volte a ligar o Excel ao vivo (TSKEXCEL) " +
                                "para recomeçar de uma cópia limpa do modelo.");
                _falhasFormato = 0;
            }

            // A dedução distingue-se pela cor, como já se fazia à mão.
            List<int> deducoes;
            if (porTipo.TryGetValue(TipoLinha.Deducao, out deducoes))
                Realcar(ws, deducoes, false, Vermelho,
                        ColunaLetra(m.PrimeiraColuna), ColunaLetra(m.UltimaColuna));
        }

        private void Estilo(dynamic ws, dynamic modelo,
            Dictionary<TipoLinha, List<int>> porTipo, TipoLinha tipo, int linhaModelo,
            bool copiarAltura = false)
        {
            if (linhaModelo <= 0) return;

            List<int> linhas;
            if (!porTipo.TryGetValue(tipo, out linhas) || linhas.Count == 0) return;

            var m = _mapaItem;
            string cIni = ColunaLetra(m.PrimeiraColuna);
            string cFim = ColunaLetra(m.UltimaColuna);
            string origem = cIni + linhaModelo + ":" + cFim + linhaModelo;

            // Blocos contíguos: as medições vêm quase sempre seguidas, por isso
            // um bloco de 30 linhas custa uma colagem em vez de trinta. O Excel
            // repete a linha de origem ao longo do destino.
            //
            // E UM Copy SÓ para os blocos todos deste tipo. É o Copy que custa,
            // não a colagem: medido no Excel 2016 sobre uma folha com a forma
            // de uma obra real (30 artigos, 150 blocos, ~360 linhas), um Copy
            // por bloco dava 10081 ms e um Copy por tipo dá 643 ms — o mesmo
            // resultado, dezasseis vezes mais depressa. A área de transferência
            // aguenta as colagens todas; é abri-la que custa, uns 60 ms cada.
            //
            // Colar de uma vez para um destino MULTI-ÁREA baixava para 284 ms,
            // e FUNCIONA — mas não se faz, de propósito:
            //
            //   · o separador de união não é o vírgula, é o separador de lista
            //     do Excel do utilizador (Application.International(5)): ";" em
            //     português, "," noutros. Com o errado, todos os endereços
            //     rebentam com 0x800A03EC;
            //   · o endereço tem um tecto de 255 caracteres — 20 áreas passam,
            //     30 não — por isso teria de ser partido por comprimento.
            //
            // São duas dependências das definições regionais da máquina do
            // cliente para poupar ~360 ms, e só na PRIMEIRA escrita: as
            // seguintes são incrementais e formatam meia dúzia de linhas.
            // Se um dia voltar aqui, é isto que tem de resolver primeiro.

            // Sem molde na área de transferência não há o que colar, mas a
            // altura das linhas lê-se do modelo à parte e continua a valer —
            // por isso só se salta o ciclo das colagens, não o resto.
            if (AbrirMolde(modelo, origem))
                foreach (var bloco in Blocos(linhas))
                    ColarFormato(ws, origem, cIni + bloco.Key + ":" + cFim + bloco.Value);

            // A altura da linha não vem na colagem de formatos: é propriedade da
            // linha, não das células. Nos títulos ela importa — é o "espaçamento
            // maior" que os separa do resto.
            if (!copiarAltura) return;
            try
            {
                double altura = (double)modelo.Rows[linhaModelo].RowHeight;

                // A linha de artigo do modelo leva o texto do caderno de
                // encargos — sete ou oito linhas, cem pontos de altura. Copiar
                // essa altura para um título VAZIO abria um buraco no meio da
                // folha. Limita-se a três vezes a altura de uma medição: chega
                // para o título se destacar, não chega para parecer um erro.
                double normal = 15.0;
                try { normal = (double)modelo.Rows[m.LinhaMedicao].RowHeight; }
                catch { }
                if (normal <= 0) normal = 15.0;

                double maximo = normal * 3.0;
                if (altura > maximo) altura = maximo;

                foreach (var bloco in Blocos(linhas))
                    ws.Range[cIni + bloco.Key + ":" + cFim + bloco.Value]
                      .RowHeight = altura;
            }
            catch { }
        }

        /// <summary>Agrupa linhas soltas em intervalos contíguos (início, fim).</summary>
        private static List<KeyValuePair<int, int>> Blocos(List<int> linhas)
        {
            var blocos = new List<KeyValuePair<int, int>>();
            if (linhas.Count == 0) return blocos;

            linhas.Sort();
            int inicio = linhas[0], anterior = linhas[0];

            for (int i = 1; i < linhas.Count; i++)
            {
                if (linhas[i] == anterior + 1) { anterior = linhas[i]; continue; }
                blocos.Add(new KeyValuePair<int, int>(inicio, anterior));
                inicio = anterior = linhas[i];
            }
            blocos.Add(new KeyValuePair<int, int>(inicio, anterior));
            return blocos;
        }

        /// <summary>Quantas colagens de formato falharam nesta passagem.</summary>
        private int _falhasFormato;

        /// <summary>
        /// Põe a linha-molde na área de transferência, para as colagens que se
        /// seguem. Devolve false se falhou — e aí não vale a pena tentar colar,
        /// porque a área de transferência não tem o que colar.
        /// </summary>
        private bool AbrirMolde(dynamic modelo, string origem)
        {
            try
            {
                // Um Copy anterior por fechar faz o PasteSpecial rebentar. Custa
                // nada limpar antes e evita falhas que só aparecem às vezes.
                try { _app.CutCopyMode = false; } catch { }

                modelo.Range[origem].Copy();
                return true;
            }
            catch (Exception ex)
            {
                // Agora um Copy serve as colagens todas de um tipo: se falhar,
                // é o tipo inteiro que sai sem aspecto, não uma linha.
                _falhasFormato++;
                if (_falhasFormato == 1)
                    PaletteHost.Log("Formatação: não se conseguiu copiar a " +
                                    "linha-molde " + origem + " (" + ex.Message + ")");
                return false;
            }
        }

        private void ColarFormato(dynamic ws, string origem, string destino)
        {
            try
            {
                // xlPasteFormats = -4122: só o aspecto, nunca os valores do modelo.
                ws.Range[destino].PasteSpecial(-4122);
            }
            catch (Exception ex)
            {
                // O aspecto é cosmético e não pode deitar a escrita abaixo — mas
                // falhar em silêncio deixa a folha em bruto sem ninguém saber
                // porquê. Conta-se, e no fim diz-se.
                _falhasFormato++;
                if (_falhasFormato == 1)
                    PaletteHost.Log("Formatação: " + origem + " -> " + destino +
                                    " falhou (" + ex.Message + ")");
            }
        }

        /// <summary>
        /// Na coluna Totais, só a linha que contém a soma do capítulo fica a
        /// negrito. A limpeza é deliberada: o estilo copiado do modelo pode
        /// trazer negrito para a coluna inteira, mas não devemos alterar as
        /// restantes colunas nem o conteúdo.
        ///
        /// Só da primeira linha REFORMATADA nesta passagem para baixo. O
        /// negrito é formato, não conteúdo: acima de <paramref name="desde"/>
        /// nem a limpeza de formatos lhe tocou (o LimparDadosItem recebe a
        /// mesma linha) nem a colagem do modelo, por isso o que lá está
        /// continua certo. Repor o negrito da folha inteira só para
        /// acrescentar uma linha custava uma ida ao COM por capítulo — em
        /// obra, uma boa fatia do tempo de cada medição.
        /// </summary>
        private void FormatarColunaTotais(dynamic ws, List<LinhaFolha> linhas,
            int primeira, int desde, int colunaTotais)
        {
            if (linhas == null || linhas.Count == 0 || colunaTotais <= 0) return;

            int ultima = primeira + linhas.Count - 1;
            if (desde < primeira) desde = primeira;
            // Nada mudou de forma: nada foi reformatado, nada há a repor.
            if (desde > ultima) return;

            string col = ColunaLetra(colunaTotais);
            try
            {
                ws.Range[col + desde + ":" + col + ultima].Font.Bold = false;
            }
            catch { }

            for (int i = desde - primeira; i < linhas.Count; i++)
            {
                if (string.IsNullOrEmpty(linhas[i].FormulaTotais)) continue;
                try { ws.Cells[primeira + i, colunaTotais].Font.Bold = true; }
                catch { }
            }
        }

        /// <summary>
        /// Escreve as fórmulas no idioma do próprio modelo: a "unitária" de cada
        /// medição vem em R1C1 da linha-exemplo (por isso serve para qualquer
        /// modelo), e o total de cada artigo soma o seu bloco.
        ///
        /// UMA IDA AO COM POR COLUNA, não uma por artigo.
        ///
        /// As fórmulas têm mesmo de ser reescritas todas em cada passagem: o
        /// LimparDadosItem apaga o conteúdo abaixo do cabeçalho e a escrita de
        /// valores passa por cima das colunas Parcial / Sub total / Totais. O
        /// que NÃO tem de acontecer é fazê-lo célula a célula — era uma ida ao
        /// Excel por artigo, por alçado e por bloco de medições, e numa obra
        /// com trinta artigos passavam de duzentas de cada vez que se media
        /// uma parede. Com a formatação já incremental, eram elas o grosso do
        /// tempo de cada actualização.
        ///
        /// Cada coluna é montada em memória e escrita numa chamada só. As
        /// posições que ficam a null saem células MESMO vazias — e não uma
        /// cadeia vazia, que as fórmulas auxiliares do modelo contariam como
        /// preenchida.
        /// </summary>
        private void EscreverFormulasItem(dynamic ws, List<int> medicoes, List<int> artigos,
            List<int> subgrupos, List<LinhaFolha> linhas, int primeira)
        {
            var m = _mapaItem;
            int n = linhas == null ? 0 : linhas.Count;
            if (n == 0) return;

            object[,] parcial  = m.ColParcial  > 0 ? new object[n, 1] : null;
            object[,] subtotal = m.ColSubTotal > 0 ? new object[n, 1] : null;
            object[,] totais   = m.ColTotais   > 0 ? new object[n, 1] : null;

            // ---- medições: a fórmula do modelo, igual em todas as linhas ----
            if (medicoes != null)
                foreach (int linha in medicoes)
                {
                    int i = linha - primeira;
                    if (i < 0 || i >= n) continue;

                    if (parcial != null && !string.IsNullOrEmpty(m.FormulaUnitariaR1C1))
                        parcial[i, 0] = m.FormulaUnitariaR1C1;
                    if (totais != null && !string.IsNullOrEmpty(m.FormulaTotalLinhaR1C1))
                        totais[i, 0] = m.FormulaTotalLinhaR1C1;
                }

            // ---- contagens: trazem o valor feito, sem fórmula ----
            // Repõe-se aqui o número que a escrita de valores já lá tinha posto.
            // Sem isto, escrever a coluna inteira apagava-o: estas linhas estão
            // de fora da lista das medições, e o null delas limpava a célula.
            if (parcial != null)
                for (int i = 0; i < n; i++)
                    if (linhas[i].ValorParcial.HasValue)
                        parcial[i, 0] = linhas[i].ValorParcial.Value;

            // Sub-total de cada alçado/piso: soma só as medições daquele grupo,
            // na coluna Unitária/Parcial (coluna H), parando no grupo seguinte ou no artigo seguinte.
            int origemSubTotal = m.ColParcial > 0 ? m.ColParcial : (m.ColTotais > 0 ? m.ColTotais : 0);

            if (subtotal != null && origemSubTotal > 0)
                Somar(subtotal, subgrupos, linhas, primeira, m.ColSubTotal, origemSubTotal,
                      TipoLinha.Capitulo, TipoLinha.Alcado, TipoLinha.Piso);

            // Total do artigo: soma a PRÓPRIA coluna Totais, linha a linha, de
            // todo o bloco. Não há referência circular — o intervalo começa na
            // linha a seguir ao artigo e pára no artigo seguinte, portanto
            // nunca apanha outro total pelo caminho.
            if (totais != null)
                SomarArtigos(totais, artigos, linhas, primeira);

            EscreverColunaR1C1(ws, m.ColParcial,  parcial,  primeira, n);
            EscreverColunaR1C1(ws, m.ColSubTotal, subtotal, primeira, n);
            EscreverColunaR1C1(ws, m.ColTotais,   totais,   primeira, n);
        }

        /// <summary>
        /// Despeja uma coluna inteira numa chamada só. Em R1C1 porque é assim
        /// que as fórmulas do modelo são lidas, e porque em R1C1 a mesma
        /// fórmula serve todas as linhas — é o que permite mandar o bloco
        /// inteiro de uma vez em vez de linha a linha.
        /// </summary>
        private void EscreverColunaR1C1(dynamic ws, int coluna, object[,] valores,
            int primeira, int n)
        {
            if (coluna <= 0 || valores == null || n <= 0) return;
            try
            {
                ws.Range[ws.Cells[primeira, coluna], ws.Cells[primeira + n - 1, coluna]]
                  .FormulaR1C1 = valores;
            }
            catch (Exception ex)
            {
                // Uma coluna é agora uma escrita só: se falhar, falha a coluna
                // INTEIRA e a folha sai com a Parcial ou os Totais em branco.
                // Célula a célula perdia-se uma linha e ninguém dava por isso;
                // aqui perde-se uma coluna, e tem de se dizer.
                PaletteHost.Log("Excel: a coluna " + ColunaLetra(coluna) +
                                " ficou sem fórmulas (" + ex.Message +
                                "). Carregue em Atualizar para tentar de novo.");
            }
        }

        /// <summary>
        /// Total de cada artigo, para dentro do array da coluna Totais: soma
        /// essa coluna desde a linha a seguir ao artigo até ao artigo seguinte.
        /// Nas linhas de título e de grupo a coluna está vazia, por isso não há
        /// risco de contar a dobrar nem de somar o total de outro artigo.
        ///
        /// Em R1C1: o intervalo é sempre "da linha a seguir até tantas linhas
        /// abaixo", que se escreve igual em qualquer linha da folha.
        /// </summary>
        private static void SomarArtigos(object[,] coluna, List<int> artigos,
            List<LinhaFolha> linhas, int primeira)
        {
            if (artigos == null) return;

            foreach (int linhaArtigo in artigos)
            {
                int i = linhaArtigo - primeira;
                if (i < 0 || i >= linhas.Count) continue;

                int fim = i;
                for (int j = i + 1; j < linhas.Count; j++)
                {
                    if (linhas[j].Tipo == TipoLinha.Capitulo) break;
                    fim = j;
                }
                if (fim <= i) continue;

                coluna[i, 0] = "=SUM(R[1]C:R[" + (fim - i) + "]C)";
            }
        }

        /// <summary>
        /// Escreve, em cada linha de <paramref name="cabecalhos"/>, a soma da
        /// coluna <paramref name="colunaOrigem"/> desde a linha seguinte até
        /// encontrar um dos tipos que fecham o bloco.
        /// </summary>
        private static void Somar(object[,] coluna, List<int> cabecalhos,
            List<LinhaFolha> linhas, int primeira, int colunaDestino, int colunaOrigem,
            params TipoLinha[] fecham)
        {
            if (colunaOrigem <= 0 || cabecalhos == null) return;

            // Deslocamento em R1C1 da coluna somada em relação à de destino.
            // Com as duas na mesma coluna escreve-se "C" e não "C[0]".
            int d = colunaOrigem - colunaDestino;
            string cRef = d == 0 ? "C" : "C[" + d + "]";

            foreach (int linhaCabecalho in cabecalhos)
            {
                int i = linhaCabecalho - primeira;
                if (i < 0 || i >= linhas.Count) continue;

                int fim = i;
                for (int j = i + 1; j < linhas.Count; j++)
                {
                    bool fecha = false;
                    foreach (var t in fecham) if (linhas[j].Tipo == t) { fecha = true; break; }
                    if (fecha) break;
                    fim = j;
                }
                if (fim <= i) continue;

                coluna[i, 0] = "=SUM(R[1]" + cRef + ":R[" + (fim - i) + "]" + cRef + ")";
            }
        }

        /// <summary>Devolve a folha do capítulo no modo item, duplicando a folha-modelo
        /// se ainda não existir, e limpa a área de dados abaixo do cabeçalho.</summary>
        private dynamic ObterFolhaItem(string nome)
        {
            foreach (dynamic s in _wb.Sheets)
            {
                try
                {
                    if (string.Equals((string)s.Name, nome, StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                catch { }
            }

            dynamic modelo = _wb.Sheets[_folhaModelo];
            int total = (int)_wb.Sheets.Count;
            modelo.Copy(Type.Missing, _wb.Sheets[total]);

            dynamic nova = _wb.Sheets[(int)_wb.Sheets.Count];
            try { nova.Name = nome; } catch { /* nome recusado: fica o automático */ }

            return nova;
        }

        /// <summary>
        /// Deixa a folha em branco abaixo do cabeçalho — sem valores, sem
        /// fórmulas e sem restos de formatação da obra anterior. O bloco de
        /// identificação (obra, empreitada, data) e a linha de cabeçalho ficam
        /// intactos, tal como as larguras de coluna e os painéis fixos.
        ///
        /// É esta limpeza que garante que a folha sai como se fosse nova, e não
        /// com bandas e negritos herdados de onde havia mais linhas do que
        /// agora.
        /// </summary>
        private void LimparDadosItem(dynamic ws, string nome, int formatosApartirDe)
        {
            var m = _mapaItem;
            int primeira = m.LinhaCabecalho + 1;

            int fim = primeira + 2000;
            try
            {
                // Até onde a folha foi mesmo usada: evita limpar 2000 linhas à toa
                // e, ao mesmo tempo, apanha modelos com muito mais do que isso.
                int usado = (int)ws.UsedRange.Row + (int)ws.UsedRange.Rows.Count - 1;
                if (usado > fim) fim = usado;
            }
            catch { }

            int anterior;
            if (_ultimaLinha.TryGetValue(nome, out anterior) && anterior > fim) fim = anterior;

            // A folha-modelo é de onde se copia o aspecto de tudo o resto.
            // Apagar-lhe a formatação destrói as linhas-exemplo e, a partir
            // daí, todas as colagens copiam formato vazio — a folha inteira
            // fica sem bandas, sem limites e com os números em bruto. Já
            // aconteceu; não pode voltar a acontecer.
            bool ehModelo = _folhaModelo != null &&
                string.Equals(nome, _folhaModelo, StringComparison.OrdinalIgnoreCase);
            if (ehModelo)
                PaletteHost.Log("A folha '" + nome + "' é o molde do aspecto: " +
                                "a formatação dela não é tocada.");

            try
            {
                dynamic r = ws.Range[ws.Cells[primeira, 1], ws.Cells[fim, m.UltimaColuna + 4]];
                r.ClearContents();

                // Só se apaga a formatação quando há linhas-molde de onde a
                // repor. Num modelo já limpo, o formato das linhas vazias É o
                // modelo — apagá-lo deixaria a folha sem casas decimais nem
                // bandas, e não havia como recuperá-lo.
                // O conteúdo limpa-se todo (é barato e é reescrito a seguir);
                // a formatação só a partir da primeira linha que mudou de tipo.
                if (m.TemEstilo && !ehModelo && formatosApartirDe <= fim)
                {
                    int desde = formatosApartirDe > primeira ? formatosApartirDe : primeira;
                    ws.Range[ws.Cells[desde, 1], ws.Cells[fim, m.UltimaColuna + 4]]
                      .ClearFormats();
                }
            }
            catch
            {
                // ClearFormats pode falhar em folhas protegidas: ao menos os valores.
                try { ws.Range[ws.Cells[primeira, 1], ws.Cells[fim, m.UltimaColuna + 4]].ClearContents(); }
                catch { }
            }
        }

        private void EscreverFolha(FolhaCapitulo capitulo)
        {
            string nome = FolhaTemplate.NomeDeFolha(ModeloExcel.FolhaDe(capitulo.Nome));
            dynamic ws = ObterFolha(nome);
            if (ws == null) return;

            // No modelo clássico (art/descrição), o que o utilizador escreve
            // nas linhas de artigo é a única coisa que se devolve ao DWG.
            // Antes só o modo item recolhia: aqui tudo se apagava na reescrita
            // seguinte sem chegar ao desenho. Ler ANTES de limpar, como o modo
            // item faz (ver EscreverFolhaItem).
            RecolherArtigosClassicos(ws, nome);

            LimparDados(ws, nome);

            int n = capitulo.Linhas.Count;
            if (n == 0)
            {
                // Sem linhas não há nada a reescrever; o mapa antigo não vale
                // para nada e não pode ficar a apontar para linhas mortas.
                _titulosEscritos.Remove(nome);
                return;
            }

            int primeira = FolhaTemplate.PrimeiraLinha;
            int ultima = primeira + n - 1;
            GarantirFormulas(ws, ultima);

            // H..M de uma vez só: cento e tal chamadas COM passam a uma.
            var dados = new object[n, 6];
            // O modelo clássico usa a coluna A para o código do artigo.
            // Mantemos H:M em lote, como antes, e escrevemos A num segundo
            // bloco para não tocar na coluna B (fórmulas auxiliares do modelo).
            var itens = new object[n, 1];
            var negrito = new List<int>();
            var deducoes = new List<int>();
            // Linhas de artigo desta passagem: linha do Excel -> handle. É o
            // mapa que permite, na próxima escrita, ir buscar o que o
            // utilizador escreveu e devolvê-lo às medições.
            var titulos = new Dictionary<int, string>();

            for (int i = 0; i < n; i++)
            {
                var l = capitulo.Linhas[i];
                int linha = primeira + i;

                if (l.Tipo == TipoLinhaTpl.Vazia) continue;

                // O "@" marca cabeçalho de artigo: o que lá se escrever vale
                // para todas as medições do bloco, como no modo item.
                if (l.Tipo == TipoLinhaTpl.Artigo &&
                    !string.IsNullOrEmpty(l.HandleOrigem))
                    titulos[linha] = "@" + l.HandleOrigem;

                itens[i, 0] = l.Item;
                dados[i, 0] = l.Descricao;
                dados[i, 1] = l.Un;
                dados[i, 2] = l.Qt;
                dados[i, 3] = Arredondar(l.Comp);
                dados[i, 4] = Arredondar(l.Larg);
                dados[i, 5] = Arredondar(l.Alt);

                if (l.Tipo == TipoLinhaTpl.Capitulo ||
                    l.Tipo == TipoLinhaTpl.Artigo ||
                    l.Tipo == TipoLinhaTpl.Alcado ||
                    l.Tipo == TipoLinhaTpl.Piso)
                    negrito.Add(linha);

                if (l.Tipo == TipoLinhaTpl.Deducao)
                    deducoes.Add(linha);
            }

            dynamic destino = ws.Range[
                ws.Cells[primeira, FolhaTemplate.C_DESC],
                ws.Cells[ultima, FolhaTemplate.C_ALT]];
            destino.Value2 = dados;

            // Sem isto, o código editado na grelha era guardado no DWG mas
            // desaparecia do modelo clássico, porque a coluna A ficava sempre
            // vazia. Escrever só A preserva as fórmulas/auxiliares da coluna B.
            try
            {
                ws.Range[
                    ws.Cells[primeira, FolhaTemplate.C_ART],
                    ws.Cells[ultima, FolhaTemplate.C_ART]].Value2 = itens;
            }
            catch { }

            // O texto do articulado pode ser muito longo. A quebra aplica-se
            // à coluna real da descrição, preservando o texto completo sem
            // depender de o utilizador abrir a célula manualmente.
            try
            {
                string colDesc = ColunaLetra(FolhaTemplate.C_DESC);
                ws.Range[colDesc + primeira + ":" + colDesc + ultima].WrapText = true;
                ws.Columns[colDesc + ":" + colDesc].ColumnWidth = 80;
                ws.Range[colDesc + primeira + ":" + colDesc + ultima].VerticalAlignment = -4160;

                // O Excel não calcula sempre a altura depois de uma escrita em
                // lote. AutoFit limita-se à coluna da descrição e depois fica
                // limitado a uma altura razoável para não criar páginas enormes.
                ws.Range[colDesc + primeira + ":" + colDesc + ultima].EntireRow.AutoFit();
                for (int r = primeira; r <= ultima; r++)
                {
                    double altura = 15.0;
                    try { altura = (double)ws.Rows[r].RowHeight; } catch { }
                    if (altura < 15.0) altura = 15.0;
                    if (altura > 409.0) altura = 409.0;
                    ws.Rows[r].RowHeight = altura;
                }
            }
            catch { }

            Realcar(ws, negrito, true, XlAutomatic);
            Realcar(ws, deducoes, false, Vermelho);

            // No modelo clássico J é a coluna de quantidade, não a coluna
            // Totais. Como o realce acima cobre H:M, repõe-se J como normal;
            // não há uma linha de soma em J neste formato.
            try
            {
                ws.Range["J" + primeira + ":J" + ultima].Font.Bold = false;
            }
            catch { }

            _ultimaLinha[nome] = ultima;
            _titulosEscritos[nome] = titulos;
        }

        /// <summary>Devolve a folha do capítulo, duplicando a folha-modelo se ainda não existir.</summary>
        private dynamic ObterFolha(string nome)
        {
            foreach (dynamic s in _wb.Sheets)
            {
                try
                {
                    if (string.Equals((string)s.Name, nome, StringComparison.OrdinalIgnoreCase))
                        return s;
                }
                catch { }
            }

            dynamic modelo = _wb.Sheets[_folhaModelo];
            int total = (int)_wb.Sheets.Count;
            modelo.Copy(Type.Missing, _wb.Sheets[total]);

            dynamic nova = _wb.Sheets[(int)_wb.Sheets.Count];
            try { nova.Name = nome; } catch { /* nome recusado: fica o automático */ }

            // A folha-modelo pode trazer um título de capítulo antigo.
            LimparDados(nova, nome);
            return nova;
        }

        /// <summary>Apaga só o que o plugin escreve: A:B e H:M. C..G, N e O ficam.</summary>
        private void LimparDados(dynamic ws, string nome)
        {
            int primeira = FolhaTemplate.PrimeiraLinha;
            int fim = FolhaTemplate.UltimaLinhaModelo;
            int anterior;
            if (_ultimaLinha.TryGetValue(nome, out anterior) && anterior > fim) fim = anterior;

            try { ws.Range["A" + primeira + ":B" + fim].ClearContents(); } catch { }

            try
            {
                dynamic r = ws.Range["H" + primeira + ":M" + fim];
                r.ClearContents();
                r.Font.Bold = false;
                r.Font.Italic = false;
                r.Font.ColorIndex = XlAutomatic;
            }
            catch { }
        }

        /// <summary>
        /// Se a medição passar da última linha com fórmulas do modelo, estende-as —
        /// é o mesmo que a macro Ctrl+F (ActualizaFormulas) faz à mão.
        /// </summary>
        private void GarantirFormulas(dynamic ws, int ultima)
        {
            if (ultima <= FolhaTemplate.UltimaLinhaModelo) return;
            int p = FolhaTemplate.PrimeiraLinha;
            try { ws.Range["C" + p + ":G" + ultima].FillDown(); } catch { }
            try { ws.Range["N" + p + ":O" + ultima].FillDown(); } catch { }
        }

        /// <summary>Aplica negrito/cor a um conjunto de linhas em poucas chamadas COM.</summary>
        private void Realcar(dynamic ws, List<int> linhas, bool negrito, int cor)
        {
            // Colunas do modelo clássico (art/descrição): H a M.
            Realcar(ws, linhas, negrito, cor, "H", "M");
        }

        /// <summary>
        /// Realça linhas entre duas colunas. As colunas são parâmetro porque no
        /// modelo "Item/Designação" a tabela não está em H:M — estava a pintar
        /// células ao lado das certas.
        /// </summary>
        private void Realcar(dynamic ws, List<int> linhas, bool negrito, int cor,
            string colInicial, string colFinal)
        {
            if (linhas == null || linhas.Count == 0) return;

            // Blocos contíguos, não endereços multi-área. O multi-área tem
            // limite de comprimento e o Excel recusa-o sem avisar — bastava um
            // falhar para quinze deduções ficarem a preto. Com blocos, cada
            // intervalo é um endereço simples que o Excel aceita sempre.
            //
            // Medido desde então: o tecto são 255 caracteres (20 áreas passam,
            // 30 não), e o separador de união é o da máquina do utilizador,
            // não a vírgula — ver a nota no Estilo(). Ambos os motivos se
            // mantêm; isto fica como está.
            foreach (var bloco in Blocos(new List<int>(linhas)))
            {
                string endereco = colInicial + bloco.Key + ":" + colFinal + bloco.Value;
                if (AplicarRealce(ws, endereco, negrito, cor)) continue;

                // Se mesmo assim falhar, linha a linha: mais lento, mas nunca
                // deixa um bloco inteiro sem realce.
                for (int l = bloco.Key; l <= bloco.Value; l++)
                    AplicarRealce(ws, colInicial + l + ":" + colFinal + l, negrito, cor);
            }
        }

        /// <summary>Liga a quebra de linha nesta coluna, por blocos contíguos.</summary>
        private void Quebrar(dynamic ws, List<int> linhas, string coluna)
        {
            if (linhas == null || linhas.Count == 0 || string.IsNullOrEmpty(coluna)) return;
            foreach (var bloco in Blocos(new List<int>(linhas)))
            {
                try
                {
                    ws.Range[coluna + bloco.Key + ":" + coluna + bloco.Value]
                      .WrapText = true;
                }
                catch { }
            }
        }

        /// <summary>Fixa a altura destas linhas, por blocos contíguos.</summary>
        private void AlturaLinhas(dynamic ws, List<int> linhas, double altura)
        {
            if (linhas == null || linhas.Count == 0 || altura <= 0) return;
            foreach (var bloco in Blocos(new List<int>(linhas)))
            {
                try
                {
                    ws.Range["A" + bloco.Key + ":A" + bloco.Value]
                      .EntireRow.RowHeight = altura;
                }
                catch { }
            }
        }

        /// <summary>Tira o negrito destas linhas, por blocos contíguos.</summary>
        /// <summary>Alinhamento horizontal ao centro, no vocabulário do Excel.</summary>
        private const int XlCentro = -4108;

        /// <summary>
        /// Põe as linhas de agrupamento — serviço, alçado, piso — a negrito e
        /// centradas na coluna da designação.
        ///
        /// Numa obra com cinco pisos e vários alçados, estas linhas são o que
        /// permite percorrer a folha de olho. Com o mesmo aspecto das medições
        /// perdem-se no meio delas, e quem confere tem de ler linha a linha
        /// para saber em que piso está.
        ///
        /// Aplicado DEPOIS da colagem de formatos do modelo, senão a colagem
        /// desfazia isto — foi por essa ordem que já se perderam formatações
        /// antes.
        /// </summary>
        private void CentrarNegrito(dynamic ws, List<int> linhas, string coluna)
        {
            if (linhas == null || linhas.Count == 0) return;
            foreach (var bloco in Blocos(new List<int>(linhas)))
            {
                try
                {
                    dynamic r = ws.Range[coluna + bloco.Key + ":" + coluna + bloco.Value];
                    r.Font.Bold = true;
                    r.HorizontalAlignment = XlCentro;
                }
                catch { }
            }
        }

        private void TirarNegrito(dynamic ws, List<int> linhas,
            string colInicial, string colFinal)
        {
            if (linhas == null || linhas.Count == 0) return;
            foreach (var bloco in Blocos(new List<int>(linhas)))
            {
                try
                {
                    ws.Range[colInicial + bloco.Key + ":" + colFinal + bloco.Value]
                      .Font.Bold = false;
                }
                catch { }
            }
        }

        /// <summary>Aplica negrito/cor a um intervalo. Devolve false se falhou.</summary>
        private bool AplicarRealce(dynamic ws, string endereco, bool negrito, int cor)
        {
            try
            {
                dynamic r = ws.Range[endereco];
                if (negrito) r.Font.Bold = true;
                if (cor == XlAutomatic) r.Font.ColorIndex = XlAutomatic;
                else r.Font.Color = cor;
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        // Modo simples — sem modelo registado
        // ------------------------------------------------------------------
        private void EscreverSimples(RegraDesconto regra)
        {
            _ws.Cells.Clear();

            for (int c = 0; c < FolhaMedicao.Cabecalho.Length; c++)
                _ws.Cells[1, c + 1].Value2 = FolhaMedicao.Cabecalho[c];
            dynamic hr = _ws.Range[_ws.Cells[1, 1],
                _ws.Cells[1, FolhaMedicao.Cabecalho.Length]];
            hr.Font.Bold = true;
            hr.Interior.Color = 0xBFBFBF;

            var linhas = FolhaMedicao.Construir(_paredes, _fachadas, _lineares, regra);

            int row = 2;
            foreach (var l in linhas)
            {
                _ws.Cells[row, FolhaMedicao.C_ITEM].Value2 = l.Item;

                if (l.Tipo != TipoLinha.Vazia)
                {
                    _ws.Cells[row, FolhaMedicao.C_DESC].Value2 = l.Designacao;
                    if (!string.IsNullOrEmpty(l.Un))
                        _ws.Cells[row, FolhaMedicao.C_UN].Value2 = l.Un;
                    if (l.Qt.HasValue)
                        _ws.Cells[row, FolhaMedicao.C_QT].Value2 = l.Qt.Value;
                    if (l.Comp.HasValue)
                        _ws.Cells[row, FolhaMedicao.C_COMP].Value2 = Math.Round(l.Comp.Value, 3);
                    if (l.Largura.HasValue)
                        _ws.Cells[row, FolhaMedicao.C_LARG].Value2 = Math.Round(l.Largura.Value, 3);
                    if (l.Altura.HasValue)
                        _ws.Cells[row, FolhaMedicao.C_ALT].Value2 = Math.Round(l.Altura.Value, 3);

                    if (l.ValorParcial.HasValue)
                        _ws.Cells[row, FolhaMedicao.C_PARC].Value2 = l.ValorParcial.Value;
                    else if (l.TemParcial)
                        _ws.Cells[row, FolhaMedicao.C_PARC].Formula = "=" +
                            FolhaMedicao.FormulaParcial(row, l.Largura.HasValue, l.Altura.HasValue);
                    if (!string.IsNullOrEmpty(l.FormulaSubTotal))
                        _ws.Cells[row, FolhaMedicao.C_SUB].Formula = "=" + l.FormulaSubTotal;
                    if (!string.IsNullOrEmpty(l.FormulaTotais))
                        _ws.Cells[row, FolhaMedicao.C_TOT].Formula = "=" + l.FormulaTotais;

                    dynamic faixa = _ws.Range[_ws.Cells[row, 1], _ws.Cells[row, 10]];
                    switch (l.Tipo)
                    {
                        case TipoLinha.Capitulo:
                            faixa.Font.Bold = true;
                            faixa.Interior.Color = 0xDEF1EB;   // BGR
                            break;
                        case TipoLinha.Alcado:
                            faixa.Font.Bold = true;
                            faixa.Interior.Color = 0xF1E6DC;
                            break;
                        case TipoLinha.Piso:
                            _ws.Cells[row, FolhaMedicao.C_DESC].Font.Bold = true;
                            break;
                        case TipoLinha.Medicao:
                            _ws.Cells[row, FolhaMedicao.C_DESC].Font.Italic = true;
                            break;
                        case TipoLinha.Deducao:
                            dynamic verm = _ws.Range[
                                _ws.Cells[row, FolhaMedicao.C_DESC],
                                _ws.Cells[row, FolhaMedicao.C_PARC]];
                            verm.Font.Color = Vermelho;
                            _ws.Cells[row, FolhaMedicao.C_DESC].Font.Italic = true;
                            break;
                    }

                    // Na coluna J, negrito apenas na linha que contém a soma.
                    _ws.Cells[row, FolhaMedicao.C_TOT].Font.Bold =
                        !string.IsNullOrEmpty(l.FormulaTotais);
                }
                row++;
            }

            _ws.Cells[row + 1, FolhaMedicao.C_DESC].Value2 =
                "Regra de vãos: " + Config.RegraDescricao(regra);

            // AutoFit pode deixar a descrição estreita ou criar uma coluna
            // gigantesca. Define-se uma largura confortável e mantém-se a
            // quebra de texto para artigos longos.
            try
            {
                string colDesc = ColunaLetra(FolhaMedicao.C_DESC);
                _ws.Columns[colDesc + ":" + colDesc].ColumnWidth = 80;
                _ws.Range[colDesc + "1:" + colDesc + (row + 1)].WrapText = true;
                _ws.Range[colDesc + "1:" + colDesc + (row + 1)].VerticalAlignment = -4160;
                _ws.Range[colDesc + "1:" + colDesc + (row + 1)].EntireRow.AutoFit();
            }
            catch
            {
                // Folhas protegidas ou células unidas podem recusar AutoFit;
                // nesse caso calcula-se uma altura segura pelo tamanho do texto.
                try
                {
                    for (int i = 0; i < linhas.Count; i++)
                    {
                        int linhasTexto = ((linhas[i].Designacao ?? "").Length + 79) / 80;
                        double altura = Math.Min(409.0,
                            Math.Max(15.0, linhasTexto * 15.0));
                        _ws.Rows[2 + i].RowHeight = altura;
                    }
                }
                catch { }
            }
            _ws.Columns.AutoFit();
            try
            {
                string colDesc = ColunaLetra(FolhaMedicao.C_DESC);
                _ws.Columns[colDesc + ":" + colDesc].ColumnWidth = 80;
            }
            catch { }
        }

        /// <summary>
        /// Medições escritas na última passagem: linha do Excel → handle.
        /// Serve para saber a que medição corresponde a célula seleccionada.
        /// </summary>
        private readonly Dictionary<string, Dictionary<int, string>> _medicoesEscritas =
            new Dictionary<string, Dictionary<int, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Handle da medição correspondente à célula seleccionada no Excel, ou
        /// null se não houver selecção útil.
        ///
        /// É isto que permite carregar em "Artigo" no AutoCAD e o título sair
        /// onde se apontou na folha, em vez de ir sempre para o fim.
        /// </summary>
        /// <summary>
        /// Definições que o Excel tinha antes de lhe tocarmos. Devolvem-se ao
        /// desligar: sem isto, numa sessão ao vivo o Excel do utilizador ficava
        /// sem avisos para o resto do dia (ver Desconectar/ReporDefinicoesExcel).
        /// </summary>
        private object _displayAlertsAntes;
        private object _askToUpdateLinksAntes;
        private object _alertAntes;
        private object _featureInstallAntes;

        /// <summary>Última célula vista, para saber quando o utilizador a mudou.</summary>
        private string _ultimaCelula;
        private DateTime _instanteCelula = DateTime.MinValue;

        /// <summary>
        /// Quando é que a selecção no Excel mudou pela última vez.
        ///
        /// Serve para decidir quem manda: o Excel ou a grelha da paleta. O
        /// Excel tem SEMPRE uma célula activa, mesmo que ninguém lhe toque —
        /// por isso "há uma célula seleccionada" não pode ser o critério. O que
        /// interessa é onde a pessoa mexeu por último.
        /// </summary>
        public DateTime InstanteSeleccao()
        {
            try
            {
                if (!Conectado || !_modoItem) return DateTime.MinValue;

                string agora = (string)_app.ActiveSheet.Name + "!" +
                               (int)_app.ActiveCell.Row;
                if (agora != _ultimaCelula)
                {
                    _ultimaCelula = agora;
                    _instanteCelula = DateTime.UtcNow;
                }
                return _instanteCelula;
            }
            catch { return DateTime.MinValue; }
        }

        public string MedicaoNaCelulaSeleccionada()
        {
            if (!Conectado || !_modoItem) return null;

            try
            {
                if (EmEdicao()) return null;

                string folha = (string)_app.ActiveSheet.Name;
                int linha = (int)_app.ActiveCell.Row;

                Dictionary<int, string> mapa;
                if (!_medicoesEscritas.TryGetValue(folha, out mapa)) return null;

                // A medição ANTERIOR à linha onde se está, nunca a própria.
                //
                // Tudo o que se acrescenta sai DEPOIS da medição-alvo. Se o
                // alvo fosse a própria linha, o que se acrescentasse aparecia
                // sempre uma linha abaixo do sítio apontado. Apontando para a
                // anterior, a coisa nasce exactamente na linha onde se clicou
                // e empurra o resto para baixo — que é o que se espera de
                // "acrescentar aqui".
                //
                // Isto também trata bem os títulos e as linhas em branco: como
                // não estão no mapa, cai-se na mesma no bloco onde se está.
                int melhor = -1;
                foreach (var par in mapa)
                    if (par.Key < linha && par.Key > melhor) melhor = par.Key;

                return melhor > 0 ? mapa[melhor] : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Estado interno em texto, para perceber porque é que a folha não sai
        /// como se espera sem ter de adivinhar.
        /// </summary>
        public string Diagnostico()
        {
            var s = new System.Text.StringBuilder();
            s.AppendLine("Ligado ao Excel: " + (Conectado ? "sim" : "não"));
            s.AppendLine("Modelo registado: " + (ModeloExcel.Caminho ?? "nenhum"));

            if (AvisoModelo != null) s.AppendLine("Aviso do modelo: " + AvisoModelo);
            if (!Conectado) return s.ToString();

            s.AppendLine("Cópia de trabalho: " + (_ficheiroTrabalho ?? "—"));
            s.AppendLine("Modo: " + (!_modoModelo ? "folha simples"
                : _modoItem ? "modelo estilo Item/Designação"
                : "modelo estilo art/descrição"));
            s.AppendLine("Folha-molde: " + (_folhaModelo ?? "—"));

            if (_modoItem && _mapaItem != null)
            {
                var m = _mapaItem;
                s.AppendLine("Cabeçalho na linha: " + m.LinhaCabecalho);
                s.AppendLine("Colunas: item=" + Col(m.ColItem) + " desc=" + Col(m.ColDesc) +
                             " un=" + Col(m.ColUn) + " qt=" + Col(m.ColQt) +
                             " comp=" + Col(m.ColComp) + " larg=" + Col(m.ColLarg) +
                             " alt=" + Col(m.ColAlt) + " parcial=" + Col(m.ColParcial) +
                             " subtotal=" + Col(m.ColSubTotal) + " totais=" + Col(m.ColTotais));
                s.AppendLine("Linhas-molde: capítulo=" + m.LinhaCapitulo +
                             " subtítulo=" + m.LinhaSubtitulo +
                             " artigo=" + m.LinhaArtigo +
                             " medição=" + m.LinhaMedicao);
                s.AppendLine("Copia o aspecto do modelo: " + (m.TemEstilo ? "sim" : "NÃO"));
                s.AppendLine("Fórmula unitária: " + (m.FormulaUnitariaR1C1 ?? "—"));
                s.AppendLine("Fórmula total/linha: " + (m.FormulaTotalLinhaR1C1 ?? "—"));
            }

            s.AppendLine("Medições em memória: " + _paredes.Count + " parede(s), " +
                         _fachadas.Count + " material(ais)");
            foreach (var par in _ultimaLinha)
                s.AppendLine("Folha \"" + par.Key + "\" escrita até à linha " + par.Value);

            return s.ToString();
        }

        private static string Col(int c) { return c > 0 ? ColunaLetra(c) : "—"; }

        // ==================================================================
        // Macros e gravação
        // ==================================================================

        /// <summary>
        /// Corre uma macro do próprio livro (CriarMD_v1, CriarEO_Final, CriarQT…).
        /// Devolve o erro, ou null se correu.
        /// </summary>
        public string CorrerMacro(string nome)
        {
            if (!Conectado) return "O Excel ao vivo não está ligado.";
            if (!_modoModelo)
                return "As macros estão no modelo da casa. Registe-o com TSKMODELO " +
                       "e volte a ligar o Excel ao vivo.";

            try
            {
                _wb.Activate();
                _app.Run(nome);
                return null;
            }
            catch (Exception ex)
            {
                return "A macro '" + nome + "' não correu: " + ex.Message +
                       "\nConfirme que as macros estão activadas no Excel.";
            }
        }

        /// <summary>Guarda a cópia de trabalho no caminho indicado, mantendo as macros.</summary>
        public string GuardarComo(string caminho)
        {
            if (!Conectado) return "O Excel ao vivo não está ligado.";

            try
            {
                _app.DisplayAlerts = false;
                _wb.SaveAs(caminho, FormatoDe(caminho));
                _ficheiroTrabalho = caminho;
                return null;
            }
            catch (Exception ex)
            {
                return "Não foi possível guardar: " + ex.Message;
            }
            finally
            {
                // Repor o valor que tínhamos tirado (e não "true" fixo): se o
                // Excel for o do utilizador e ele o tivesse desligado, pô-lo a
                // true mudava-lhe o comportamento para o resto do dia.
                try
                {
                    if (_displayAlertsAntes != null) _app.DisplayAlerts = _displayAlertsAntes;
                    else _app.DisplayAlerts = true;
                }
                catch { }
            }
        }

        /// <summary>xlExcel8 (.xls) e xlOpenXMLWorkbookMacroEnabled (.xlsm) guardam macros.</summary>
        private static int FormatoDe(string caminho)
        {
            string ext = (Path.GetExtension(caminho) ?? "").ToLowerInvariant();
            if (ext == ".xls") return 56;    // xlExcel8
            if (ext == ".xlsm") return 52;   // xlOpenXMLWorkbookMacroEnabled
            return 51;                       // xlOpenXMLWorkbook (.xlsx — perde macros)
        }

        // ==================================================================
        /// <summary>
        /// Exporta para ficheiro usando o modelo, sem depender de haver uma
        /// sessão ao vivo aberta. Devolve o erro, ou null.
        /// </summary>
        public static string ExportarComModelo(string caminho, IList<Parede> paredes,
            IList<MedFachada> fachadas, RegraDesconto regra)
        {
            return ExportarComModelo(caminho, paredes, fachadas, null,
                                     FolhaMedicao.Contagens, regra);
        }

        public static string ExportarComModelo(string caminho, IList<Parede> paredes,
            IList<MedFachada> fachadas, IList<MedItem> lineares,
            IList<MedContagem> contagens, RegraDesconto regra)
        {
            if (!ModeloExcel.Existe)
                return "Ainda não há um modelo registado. Use o comando TSKMODELO.";

            var sync = new ExcelLiveSync();
            try
            {
                sync.Conectar(Path.GetFileNameWithoutExtension(caminho));
                try { sync._app.Visible = false; } catch { }

                sync._paredes = paredes ?? new List<Parede>();
                sync._fachadas = fachadas ?? new List<MedFachada>();
                sync._lineares = lineares ?? new List<MedItem>();
                sync._contagens = contagens ?? new List<MedContagem>();
                sync.Redesenhar(regra);

                string erro = sync.GuardarComo(caminho);
                if (erro != null) return erro;

                try { sync._wb.Close(false); } catch { }
                try { sync._app.Quit(); } catch { }
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            finally
            {
                sync.Desconectar();
            }
        }

        // ==================================================================
        private static object Arredondar(double? v)
        {
            return v.HasValue ? (object)Math.Round(v.Value, 4) : null;
        }

        private static string Texto(object v)
        {
            return v == null ? "" : v.ToString().Trim().ToLowerInvariant();
        }

        private static void TryRelease(object com)
        {
            try
            {
                if (com != null && Marshal.IsComObject(com))
                    Marshal.ReleaseComObject(com);
            }
            catch { /* melhor esforço */ }
        }
    }
}
