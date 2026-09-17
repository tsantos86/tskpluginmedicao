using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// O que se passa quando alguém edita uma propriedade no painel.
    ///
    /// O painel NÃO escreve no desenho. Cada aba tem o seu repositório —
    /// AlvRepo, FacRepo, ContRepo — e as regras de validação são de cada uma;
    /// se a escrita vivesse aqui, este controlo teria de conhecer as quatro.
    /// Ele diz o que foi editado e em quê; quem manda no desenho decide.
    /// </summary>
    public class PropriedadeEditadaEventArgs : EventArgs
    {
        public NoResultado No { get; set; }
        public Propriedade Propriedade { get; set; }
        /// <summary>O que foi escrito, já sem espaços à volta.</summary>
        public string Valor { get; set; }
        /// <summary>
        /// Todos os handles abrangidos pela selecção. Com mais do que um, a
        /// edição é em lote.
        /// </summary>
        public List<string> Handles { get; set; }

        public bool EmLote
        {
            get { return Handles != null && Handles.Count > 1; }
        }
    }

    /// <summary>
    /// A vista de resultados: barra de pesquisa e filtros, árvore compacta,
    /// painel de propriedades e estado vazio.
    ///
    /// Existe para as QUATRO abas terem a mesma vista sem a copiarem quatro
    /// vezes. Nasceu dentro da aba de Arquitetura e saiu de lá quando as
    /// outras precisaram dela — que é a ordem certa: primeiro fez-se uma que
    /// funcionasse, e só depois se generalizou o que se aprendeu.
    ///
    /// CADA INSTÂNCIA TEM O SEU ESTADO. Filtros, recolhas e selecção são por
    /// aba: filtrar os Materiais não pode mexer no que a Arquitetura está a
    /// mostrar, e voltar a uma aba tem de a encontrar como se deixou.
    ///
    /// O WinForms não tem árvore com colunas — o TreeView não tem colunas e o
    /// DataGridView não tem hierarquia —, por isso a hierarquia é desenhada à
    /// mão sobre um DataGridView achatado. As contas de quem está visível, de
    /// quanto soma cada nível e do que a pesquisa encontra NÃO estão aqui:
    /// estão no ResultadosArvore, que não toca no WinForms e está sob teste.
    /// </summary>
    public class ResultadosPainel : UserControl
    {
        private readonly DataGridView _arvore;
        private readonly DataGridView _propriedades;
        private readonly Label _resumo;
        private readonly Label _chips;
        private readonly Label _vazio;
        private readonly Label _tituloPropriedades;
        private readonly TextBox _pesquisa;
        private readonly Button _btnFiltros;
        private readonly Button _btnLimpar;
        private readonly Button _btnModo;
        private readonly Button _btnMedirAqui;
        private readonly Timer _adiar;
        private readonly ToolStrip _accoes;

        private NoResultado _raiz;
        private bool _essenciais = true;
        private bool _aCarregar;

        /// <summary>O estado da vista desta aba: pesquisa, filtros e recolhas.</summary>
        public EstadoVista Estado { get; private set; }

        /// <summary>A árvore completa que está a ser mostrada.</summary>
        public NoResultado Raiz { get { return _raiz; } }

        /// <summary>Disparado quando o nó escolhido muda.</summary>
        public event EventHandler SeleccaoMudou;

        /// <summary>Pedido de `Medir aqui` sobre o nó de artigo escolhido.</summary>
        public event EventHandler MedirAquiPedido;

        /// <summary>Alguém escreveu numa propriedade editável.</summary>
        public event EventHandler<PropriedadeEditadaEventArgs> PropriedadeEditada;

        /// <summary>
        /// Mostrar o botão `Medir aqui`? Só faz sentido onde a próxima medição
        /// se configura por artigo — hoje, a Arquitetura e os Materiais. Numa
        /// aba onde não signifique nada, é melhor não existir do que existir
        /// desactivado a vida toda.
        /// </summary>
        public bool PermiteMedirAqui { get; set; }

        /// <summary>A barra onde a aba põe as suas próprias acções.</summary>
        public ToolStrip BarraDeAccoes { get { return _accoes; } }

        public ResultadosPainel(string titulo)
        {
            Estado = new EstadoVista();
            PermiteMedirAqui = true;
            Dock = DockStyle.Fill;
            BackColor = PaletteTheme.Fundo;

            // ---------- barra de pesquisa e filtros ----------
            var barra = new Panel
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaBarra,
                BackColor = PaletteTheme.FundoSeccao
            };

            _resumo = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding = new Padding(0, 0, 8, 0),
                ForeColor = PaletteTheme.Apagado,
                Font = PaletteTheme.Pequeno,
                AutoEllipsis = true
            };

            _chips = new Label
            {
                Dock = DockStyle.Right,
                Width = 0,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Font = PaletteTheme.Pequeno,
                ForeColor = PaletteTheme.AcentoEscuro,
                AutoEllipsis = true
            };

            // «Limpar» apaga pesquisa E filtros — as duas coisas que escondem
            // resultados. As recolhas ficam onde estavam: a vista a que se
            // volta tem de ser a de antes de procurar.
            _btnLimpar = new Button
            {
                Dock = DockStyle.Right, Width = 56, Text = "Limpar",
                FlatStyle = FlatStyle.Flat, Font = PaletteTheme.Pequeno,
                Visible = false, TabStop = true,
                AccessibleName = "Limpar pesquisa e filtros"
            };
            _btnLimpar.FlatAppearance.BorderSize = 0;
            _btnLimpar.Click += (s, e) =>
            {
                Estado.LimparVista();
                _aCarregar = true;
                try { _pesquisa.Text = ""; } finally { _aCarregar = false; }
                Actualizar();
            };

            _btnFiltros = new Button
            {
                Dock = DockStyle.Right, Width = 66, Text = "Filtros",
                FlatStyle = FlatStyle.Flat, Font = PaletteTheme.Pequeno,
                TabStop = true, AccessibleName = "Filtros"
            };
            _btnFiltros.Click += (s, e) => AbrirFiltros();

            _pesquisa = new TextBox { Dock = DockStyle.Right, Width = 130, TabStop = true };
            PaletteTheme.TextoDeSugestao(_pesquisa, "Pesquisar…");

            // Debounce: a árvore não se refaz a cada tecla. Escrever
            // "alvenaria" são nove reconstruções da lista inteira, e num
            // desenho grande sente-se o painel a arrastar-se atrás de quem
            // escreve.
            _adiar = new Timer { Interval = 220 };
            _adiar.Tick += (s, e) =>
            {
                _adiar.Stop();
                Estado.Pesquisa = _pesquisa.Text;
                Actualizar();
            };
            _pesquisa.TextChanged += (s, e) =>
            {
                if (_aCarregar) return;
                _adiar.Stop();
                _adiar.Start();
            };
            _pesquisa.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    // Quem carrega em Enter já decidiu: não espera pela pausa.
                    _adiar.Stop();
                    Estado.Pesquisa = _pesquisa.Text;
                    Actualizar();
                    e.Handled = e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    _pesquisa.Text = "";
                    e.Handled = e.SuppressKeyPress = true;
                }
            };

            // UMA GRELHA, e não seis Dock=Right encavalitados.
            //
            // Com o Dock, a soma das larguras fixas passava a largura do painel
            // e o último a entrar ficava cortado — era isso que deixava a caixa
            // de pesquisa a meio, encostada à margem. Numa grelha, a coluna que
            // cede é escolhida: cede a pesquisa, que encolhe mas continua
            // inteira, e o resumo, que tem reticências.
            var grelha = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 5,
                RowCount = 1,
                BackColor = PaletteTheme.FundoSeccao,
                Padding = new Padding(PaletteTheme.Margem, 3, PaletteTheme.Margem, 3),
                Margin = new Padding(0)
            };
            grelha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // título
            grelha.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));// pesquisa
            grelha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // filtros
            grelha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // limpar
            grelha.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));    // resumo
            grelha.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _pesquisa.Dock = DockStyle.Fill;
            _pesquisa.Margin = new Padding(6, 1, 6, 1);
            _btnFiltros.Dock = DockStyle.Fill;
            _btnFiltros.Margin = new Padding(0, 1, 3, 1);
            _btnLimpar.Dock = DockStyle.Fill;
            _btnLimpar.Margin = new Padding(0, 1, 3, 1);
            _resumo.Dock = DockStyle.Fill;
            _resumo.AutoSize = true;
            _resumo.Margin = new Padding(0, 1, 0, 1);
            _resumo.TextAlign = ContentAlignment.MiddleRight;

            grelha.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = titulo,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = PaletteTheme.TituloSeccao,
                Margin = new Padding(0, 1, 0, 1)
            }, 0, 0);
            grelha.Controls.Add(_pesquisa, 1, 0);
            grelha.Controls.Add(_btnFiltros, 2, 0);
            grelha.Controls.Add(_btnLimpar, 3, 0);
            grelha.Controls.Add(_resumo, 4, 0);

            // Os chips ficam numa faixa própria, por baixo, e só aparecem
            // quando há filtros. Espremidos na mesma linha, ou comiam a
            // pesquisa ou ficavam ilegíveis com três caracteres à vista.
            _chips.Dock = DockStyle.Top;
            _chips.Height = 0;
            _chips.Visible = false;
            _chips.TextAlign = ContentAlignment.MiddleLeft;
            _chips.Padding = new Padding(PaletteTheme.Margem, 0, PaletteTheme.Margem, 0);
            _chips.BackColor = PaletteTheme.Palido;

            barra.Controls.Add(grelha);

            // ---------- barra de acções da aba ----------
            _accoes = new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(20, 20),
                Padding = new Padding(6, 2, 6, 2),
                ShowItemToolTips = true,
                RenderMode = ToolStripRenderMode.System,
                BackColor = PaletteTheme.FundoBarra
            };

            // ---------- árvore ----------
            _arvore = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                // Multi-selecção por causa da reclassificação e da edição em
                // lote: um artigo trocado a meio da obra são dezenas de
                // medições, e passá-las uma a uma é onde se desiste e se vai
                // fazer à mão no Excel.
                MultiSelect = true,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                ColumnHeadersHeightSizeMode =
                    DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                BackgroundColor = PaletteTheme.Fundo,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                Font = PaletteTheme.Normal,
                TabStop = true,
                AccessibleName = "Resultados"
            };

            // DUAS colunas, e não cinco. Comprimento e altura são DIMENSÕES,
            // não quantidades: com três colunas fixas para cinco grandezas, a
            // contagem de duas portas aparecia debaixo de "Comp.". As
            // dimensões vivem em PROPRIEDADES.
            var colNome = new DataGridViewTextBoxColumn
            {
                Name = "estrutura",
                HeaderText = "Estrutura / elemento",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = 100,
                MinimumWidth = 150
            };
            var colQtd = new DataGridViewTextBoxColumn
            {
                Name = "quantidade",
                HeaderText = "Quantidade",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 132,
                MinimumWidth = 96
            };
            colQtd.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

            // As duas colunas de FACTORES. Não se chamam sempre a mesma coisa
            // na cabeça de quem lê — numa camada o "Comp." é uma área em
            // planta — e é por isso que a UNIDADE VIAJA DENTRO DA CÉLULA. A
            // coluna que não se aplica mostra "—", nunca um zero.
            var colComp = new DataGridViewTextBoxColumn
            {
                Name = "comp",
                HeaderText = "COMP.",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 72,
                MinimumWidth = 56
            };
            var colAlt = new DataGridViewTextBoxColumn
            {
                Name = "altura",
                HeaderText = "ALTURA",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 72,
                MinimumWidth = 56
            };
            colComp.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colAlt.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            colComp.DefaultCellStyle.ForeColor = PaletteTheme.Apagado;
            colAlt.DefaultCellStyle.ForeColor = PaletteTheme.Apagado;

            _arvore.Columns.Add(colNome);
            _arvore.Columns.Add(colComp);
            _arvore.Columns.Add(colAlt);
            _arvore.Columns.Add(colQtd);

            // O cabeçalho da árvore: baixo, cinzento, em maiúsculas pequenas.
            // Rotula as colunas sem competir com o conteúdo — a linha que se lê
            // é a medição, não o título dela.
            _arvore.EnableHeadersVisualStyles = false;
            _arvore.ColumnHeadersHeight = 22;
            _arvore.ColumnHeadersDefaultCellStyle.BackColor = PaletteTheme.FundoCabecalhoArvore;
            _arvore.ColumnHeadersDefaultCellStyle.ForeColor = PaletteTheme.Apagado;
            _arvore.ColumnHeadersDefaultCellStyle.Font = PaletteTheme.PequenoNegrito;
            _arvore.ColumnHeadersDefaultCellStyle.SelectionBackColor =
                PaletteTheme.FundoCabecalhoArvore;
            _arvore.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
            colNome.HeaderText = "ESTRUTURA / ELEMENTO";
            colQtd.HeaderText = "QUANTIDADE";
            colQtd.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;

            // Selecção discreta: a linha escolhida ganha um fundo azul-claro e
            // mantém a tinta. O azul cheio do sistema apagava as cores que
            // distinguem um vão de um título de uma parede.
            _arvore.DefaultCellStyle.SelectionBackColor = PaletteTheme.Seleccionado;
            _arvore.DefaultCellStyle.SelectionForeColor = PaletteTheme.TextoSeleccionado;
            _arvore.DefaultCellStyle.Padding = new Padding(2, 0, 4, 0);
            _arvore.GridColor = PaletteTheme.LinhaSuave;
            _arvore.RowTemplate.Height = PaletteTheme.AlturaLinha;

            DuploBuffer(_arvore);
            _arvore.CellPainting += DesenharCelula;
            _arvore.CellMouseDown += (s, e) =>
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                var no = NoDaLinha(e.RowIndex);
                if (no == null || no.Filhos.Count == 0) return;
                // A zona sensível é a mesma que o desenho usa, para o alvo do
                // rato ser exactamente o que se vê.
                if (e.X > Recuo(no) + PaletteTheme.LarguraTwisty) return;

                Estado.Alternar(no.Id);
                Actualizar();
            };
            _arvore.CellClick += (s, e) =>
            {
                var no = e.RowIndex < 0 ? null : NoDaLinha(e.RowIndex);
                if (no != null && no.Handle != null) PaletteHost.MarcarSeleccaoGrelha();
            };
            _arvore.SelectionChanged += (s, e) =>
            {
                var no = NoSeleccionado;
                // Seleccionar consulta; não muda a próxima medição. Só o
                // «Medir aqui» faz isso, e só em nós de artigo.
                Estado.Seleccionado = no == null ? null : no.Id;
                MostrarPropriedades();
                var h = SeleccaoMudou;
                if (h != null) h(this, EventArgs.Empty);
            };
            _arvore.KeyDown += TeclasDaArvore;

            _vazio = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = PaletteTheme.Normal,
                ForeColor = PaletteTheme.Apagado,
                BackColor = PaletteTheme.Fundo,
                Visible = false,
                AccessibleName = "Sem resultados"
            };

            // ---------- propriedades ----------
            var painelProps = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 116,
                BackColor = PaletteTheme.Fundo
            };

            var cabProps = new Panel
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaTituloSeccao,
                BackColor = PaletteTheme.FundoSeccao
            };

            _btnModo = new Button
            {
                Dock = DockStyle.Right, Width = 76, Text = "Essenciais",
                FlatStyle = FlatStyle.Flat, Font = PaletteTheme.Pequeno,
                BackColor = PaletteTheme.Palido, ForeColor = PaletteTheme.Acento,
                TabStop = true,
                AccessibleName = "Detalhe das propriedades"
            };
            _btnModo.FlatAppearance.BorderSize = 0;
            _btnModo.Click += (s, e) =>
            {
                _essenciais = !_essenciais;
                _btnModo.Text = _essenciais ? "Essenciais" : "Tudo";
                _btnModo.BackColor = _essenciais ? PaletteTheme.Palido : PaletteTheme.Fundo;
                _btnModo.ForeColor = _essenciais ? PaletteTheme.Acento : PaletteTheme.Apagado;
                MostrarPropriedades();
            };

            _btnMedirAqui = new Button
            {
                Dock = DockStyle.Right, Width = 82, Text = "Medir aqui",
                FlatStyle = FlatStyle.Flat, Font = PaletteTheme.PequenoNegrito,
                ForeColor = PaletteTheme.Acento, BackColor = PaletteTheme.FundoSeccao,
                Visible = false, TabStop = true,
                AccessibleName = "Medir aqui",
                AccessibleDescription =
                    "Copia o piso, o serviço e o artigo deste nó para a próxima medição."
            };
            _btnMedirAqui.FlatAppearance.BorderSize = 0;
            _btnMedirAqui.Click += (s, e) =>
            {
                var h = MedirAquiPedido;
                if (h != null) h(this, EventArgs.Empty);
            };

            // PROPRIEDADES recolhe-se. Num painel a 480 px, as três linhas de
            // propriedades são espaço que a árvore não tem — e quem está a
            // conferir totais não precisa delas abertas.
            var alternarProps = new Button
            {
                Dock = DockStyle.Fill,
                Text = "▼  PROPRIEDADES",
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(PaletteTheme.Margem, 0, 0, 0),
                Font = PaletteTheme.TituloSeccao,
                BackColor = PaletteTheme.FundoSeccao,
                TabStop = true,
                AccessibleName = "Propriedades"
            };
            alternarProps.FlatAppearance.BorderSize = 0;
            _tituloPropriedades = new Label { Visible = false };   // rótulo do nó, no texto do botão

            _propriedades = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                ColumnHeadersVisible = false,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BackgroundColor = PaletteTheme.Fundo,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                Font = PaletteTheme.Normal,
                EditMode = DataGridViewEditMode.EditOnEnter,
                TabStop = true,
                AccessibleName = "Propriedades"
            };
            var colCampo = new DataGridViewTextBoxColumn
            {
                Name = "nome", HeaderText = "Propriedade",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                Width = 148, ReadOnly = true
            };
            colCampo.DefaultCellStyle.ForeColor = PaletteTheme.Apagado;
            var colValor = new DataGridViewTextBoxColumn
            {
                Name = "valor", HeaderText = "Valor",
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
            };
            _propriedades.Columns.Add(colCampo);
            _propriedades.Columns.Add(colValor);
            DuploBuffer(_propriedades);

            _propriedades.CellBeginEdit += (s, e) =>
            {
                var p = PropriedadeDaLinha(e.RowIndex);
                if (p == null || !p.Editavel || e.ColumnIndex != 1) { e.Cancel = true; return; }
                // Abre com o valor CRU: "2,80 m" volta a ser "2,80". Deixar a
                // unidade lá dentro obrigava a apagá-la antes de escrever, e
                // quem se esquecesse gravava lixo.
                _propriedades.Rows[e.RowIndex].Cells[1].Value = ValorCru(p);
            };
            _propriedades.CellEndEdit += (s, e) =>
            {
                if (_aCarregar) return;
                var p = PropriedadeDaLinha(e.RowIndex);
                var no = NoSeleccionado;
                if (p == null || !p.Editavel || no == null || no.EhGrupo) return;

                var h = PropriedadeEditada;
                if (h == null) return;
                h(this, new PropriedadeEditadaEventArgs
                {
                    No = no,
                    Propriedade = p,
                    Valor = (_propriedades.Rows[e.RowIndex].Cells[1].Value ?? "")
                            .ToString().Trim(),
                    Handles = HandlesSeleccionados()
                });
            };

            bool propsAbertas = true;
            alternarProps.Click += (s, e) =>
            {
                propsAbertas = !propsAbertas;
                _propriedades.Visible = propsAbertas;
                painelProps.Height = propsAbertas ? 116 : PaletteTheme.AlturaTituloSeccao;
                AlternarTextoDasPropriedades(alternarProps, propsAbertas);
            };
            _alternarProps = alternarProps;
            _propsAbertas = () => propsAbertas;

            cabProps.Controls.Add(alternarProps);
            cabProps.Controls.Add(_btnMedirAqui);
            cabProps.Controls.Add(_btnModo);

            painelProps.Controls.Add(_propriedades);
            painelProps.Controls.Add(cabProps);

            // ---------- montagem ----------
            //
            // Num Dock, o último a entrar fica mais perto da margem. A ordem
            // aqui é: árvore a preencher, propriedades em baixo, e as duas
            // barras no topo — acções por baixo da pesquisa.
            Controls.Add(_vazio);
            Controls.Add(_arvore);
            Controls.Add(painelProps);
            Controls.Add(_accoes);
            Controls.Add(_chips);
            Controls.Add(barra);

            OrdenarTabulacao();
        }

        private readonly Button _alternarProps;
        private readonly Func<bool> _propsAbertas;

        private void AlternarTextoDasPropriedades(Button botao, bool aberto)
        {
            var no = NoSeleccionado;
            botao.Text = (aberto ? "▼" : "▶") + "  PROPRIEDADES" +
                (no == null ? "  —  nada selecionado" : "  —  " + no.Rotulo);
            botao.AccessibleDescription =
                (aberto ? "Expandida. " : "Recolhida. ") +
                (no == null ? "Nada selecionado." : no.Rotulo);
        }

        /// <summary>
        /// A ordem por que o Tab percorre o painel: pesquisa, filtros, limpar,
        /// acções, árvore, propriedades. É a ordem por que se trabalha, e não a
        /// ordem por que os controlos foram criados — que é o que o WinForms
        /// usaria e que aqui daria a árvore antes da pesquisa.
        /// </summary>
        private void OrdenarTabulacao()
        {
            int i = 0;
            _pesquisa.TabIndex = i++;
            _btnFiltros.TabIndex = i++;
            _btnLimpar.TabIndex = i++;
            _accoes.TabIndex = i++;
            _arvore.TabIndex = i++;
            _alternarProps.TabIndex = i++;
            _btnModo.TabIndex = i++;
            _btnMedirAqui.TabIndex = i++;
            _propriedades.TabIndex = i;
        }

        private static void DuploBuffer(DataGridView g)
        {
            // A propriedade que liga o duplo buffer é protegida — daí a
            // reflexão. Sem ela a lista pisca a cada medição, e numa obra
            // grande vê-se. É cosmético: se um runtime futuro não a tiver,
            // segue-se sem ela em vez de deitar a paleta abaixo.
            try
            {
                typeof(DataGridView)
                    .GetProperty("DoubleBuffered",
                                 System.Reflection.BindingFlags.Instance |
                                 System.Reflection.BindingFlags.NonPublic)
                    ?.SetValue(g, true, null);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // Ligação aos dados
        // ------------------------------------------------------------------

        /// <summary>
        /// Mostra esta árvore, preservando selecção e scroll PELO ID do nó.
        ///
        /// Não pelo índice da linha: esta lista é refeita a cada medição e os
        /// índices mudam todos. Com o índice, quem estava numa parede a meio da
        /// obra dava por si noutra qualquer, e as acções contextuais iam atrás.
        /// </summary>
        public void Vincular(NoResultado raiz, string handleNovo)
        {
            _raiz = raiz;

            string idSel = Estado.Seleccionado;
            string idTopo = null;
            try
            {
                int topo = _arvore.FirstDisplayedScrollingRowIndex;
                if (topo >= 0 && topo < _arvore.Rows.Count)
                {
                    var n = NoDaLinha(topo);
                    if (n != null) idTopo = n.Id;
                }
            }
            catch { }

            // Acabou de se medir: o cursor vai para a medição nova. É ela o
            // alvo do vão e do título que venham a seguir.
            bool seguir = false;
            if (!string.IsNullOrEmpty(handleNovo) && raiz != null
                && ResultadosArvore.Handles(raiz).Contains(handleNovo))
            {
                idSel = "M:" + handleNovo;
                seguir = true;
            }

            // A seguir a medição nova, o scroll NÃO é reposto: pôr o cursor na
            // linha nova já a traz à vista, e repor a rolagem antiga mandava-a
            // outra vez para fora do ecrã.
            Desenhar(idSel, seguir ? (string)null : idTopo);
        }

        /// <summary>Refaz a vista a partir da árvore que já está ligada.</summary>
        public void Actualizar()
        {
            Desenhar(Estado.Seleccionado, null);
        }

        private void Desenhar(string idSeleccionado, string idTopo)
        {
            _aCarregar = true;
            _arvore.SuspendLayout();
            try
            {
                _arvore.Rows.Clear();

                if (_raiz == null)
                {
                    _resumo.Text = "0 medições";
                    MostrarVazio(true, "Ainda não há nada medido neste desenho.");
                    return;
                }

                var vista = ResultadosArvore.Projetar(_raiz, Estado);

                var linhas = new List<DataGridViewRow>();
                foreach (var item in vista.Nos) linhas.Add(Linha(item));

                // Uma entrega só. Um Rows.Add por linha faz o DataGridView
                // reajustar-se a cada uma — 152 ms contra 20 ms numa lista
                // desta dimensão, medido na grelha antiga.
                if (linhas.Count > 0) _arvore.Rows.AddRange(linhas.ToArray());

                _resumo.Text = vista.Resumo(CultureInfo.CurrentCulture);
                ActualizarBarra();

                // O estado vazio diz POR QUE está vazio. A diferença entre
                // "não há nada medido" e "o filtro escondeu tudo" é toda, e é
                // a segunda que assusta quem acabou de medir a manhã.
                if (vista.Vazia)
                {
                    MostrarVazio(true, Estado.AFiltrar
                        ? "Nenhum resultado corresponde à pesquisa ou aos filtros.\r\n" +
                          "As " + vista.MedicoesTotais + " medições continuam no desenho — " +
                          "carregue em «Limpar» para as ver todas."
                        : "Ainda não há nada medido neste desenho.");
                }
                else MostrarVazio(false, null);

                ReporSeleccao(idSeleccionado, idTopo);
            }
            finally
            {
                _arvore.ResumeLayout();
                _aCarregar = false;
                MostrarPropriedades();
            }
        }

        private DataGridViewRow Linha(NoVisivel item)
        {
            var no = item.No;
            var linha = new DataGridViewRow();
            linha.CreateCells(_arvore, no.Rotulo,
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
                // Vermelho, como as deduções no Excel: o que desconta lê-se à
                // primeira, sem ter de reparar no sinal.
                linha.DefaultCellStyle.ForeColor = PaletteTheme.VermelhoDeducao;
                linha.DefaultCellStyle.Font = PaletteTheme.Italico;
            }
            if (no.Tipo == TipoNo.Titulo)
                linha.DefaultCellStyle.ForeColor = PaletteTheme.Apagado;

            if (no.Alertas != AlertaNo.Nenhum)
            {
                linha.Cells[0].ToolTipText = DescreverAlertas(no.Alertas);
                if ((no.Alertas & AlertaNo.PorClassificar) != 0 && !no.EhGrupo)
                {
                    linha.DefaultCellStyle.BackColor = PaletteTheme.FundoPorClassificar;
                    linha.DefaultCellStyle.ForeColor = PaletteTheme.TextoPorClassificar;
                }
                if ((no.Alertas & AlertaNo.VaosExcessivos) != 0 && no.Tipo == TipoNo.Medicao)
                    linha.DefaultCellStyle.BackColor = Color.FromArgb(255, 224, 224);
            }

            if (item.Correspondeu && Estado.AFiltrar)
                linha.DefaultCellStyle.BackColor = PaletteTheme.Palido;

            return linha;
        }

        private void MostrarVazio(bool mostrar, string texto)
        {
            // Um de cada vez: os dois são Dock=Fill, e com dois Fill ao mesmo
            // tempo quem fica com o espaço depende da ordem-z.
            if (mostrar)
            {
                _vazio.Text = texto;
                _arvore.Visible = false;
                _vazio.Visible = true;
                _vazio.BringToFront();
            }
            else
            {
                _vazio.Visible = false;
                _arvore.Visible = true;
            }
        }

        /// <summary>
        /// Volta a pôr o cursor no mesmo NÓ e a lista onde estava.
        ///
        /// Se o nó já não está visível — foi apagado, ou um filtro escondeu-o —
        /// a selecção é LIMPA, e não empurrada para o vizinho. Uma selecção
        /// invisível é pior do que nenhuma: as acções contextuais continuavam
        /// apontadas a uma medição que quem carrega no botão não está a ver.
        /// </summary>
        private void ReporSeleccao(string id, string idTopo)
        {
            try
            {
                int linha = Indice(id);
                if (linha >= 0)
                {
                    _arvore.CurrentCell = _arvore.Rows[linha].Cells[0];
                    Estado.Seleccionado = id;
                }
                else
                {
                    _arvore.ClearSelection();
                    Estado.Seleccionado = null;
                }

                int topo = Indice(idTopo);
                if (topo >= 0 && topo < _arvore.Rows.Count)
                    _arvore.FirstDisplayedScrollingRowIndex = topo;
            }
            catch { /* a lista pode ter encolhido: fica onde está */ }
        }

        private int Indice(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _arvore.Rows.Count; i++)
            {
                var no = NoDaLinha(i);
                if (no != null && no.Id == id) return i;
            }
            return -1;
        }

        private NoResultado NoDaLinha(int i)
        {
            if (i < 0 || i >= _arvore.Rows.Count) return null;
            return _arvore.Rows[i].Tag as NoResultado;
        }

        // ------------------------------------------------------------------
        // Selecção
        // ------------------------------------------------------------------

        /// <summary>O nó em que a árvore está, ou nulo.</summary>
        public NoResultado NoSeleccionado
        {
            get
            {
                return _arvore.CurrentRow == null
                    ? null : _arvore.CurrentRow.Tag as NoResultado;
            }
        }

        /// <summary>
        /// As medições abrangidas pela selecção, sem repetições.
        ///
        /// Um GRUPO não entra: não tem handle nenhum — não há entidade no
        /// desenho para onde apontar — e é isso que o impede de ser removido,
        /// editado ou reclassificado. Um TÍTULO é texto da folha, não uma
        /// medição. Um VÃO aponta para a parede a que pertence, que é o que se
        /// espera de quem o escolhe.
        /// </summary>
        public List<string> HandlesSeleccionados()
        {
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            var handles = new List<string>();

            var linhas = new SortedSet<int>();
            foreach (DataGridViewCell c in _arvore.SelectedCells) linhas.Add(c.RowIndex);
            foreach (DataGridViewRow r in _arvore.SelectedRows) linhas.Add(r.Index);
            if (linhas.Count == 0 && _arvore.CurrentRow != null)
                linhas.Add(_arvore.CurrentRow.Index);

            foreach (int i in linhas)
            {
                var no = NoDaLinha(i);
                if (no == null || no.EhGrupo || no.Tipo == TipoNo.Titulo) continue;
                if (no.Handle != null && vistos.Add(no.Handle)) handles.Add(no.Handle);
            }
            return handles;
        }

        /// <summary>Põe o cursor neste nó, se ele estiver à vista.</summary>
        public void Seleccionar(string id)
        {
            int linha = Indice(id);
            if (linha < 0) return;
            try { _arvore.CurrentCell = _arvore.Rows[linha].Cells[0]; }
            catch { }
        }

        /// <summary>Leva o foco para a caixa de pesquisa (o `Ctrl+F` da aba).</summary>
        public void FocarPesquisa()
        {
            _pesquisa.Focus();
            _pesquisa.SelectAll();
        }

        // ------------------------------------------------------------------
        // Propriedades
        // ------------------------------------------------------------------

        private Propriedade PropriedadeDaLinha(int linha)
        {
            if (linha < 0 || linha >= _propriedades.Rows.Count) return null;
            return _propriedades.Rows[linha].Tag as Propriedade;
        }

        /// <summary>
        /// O valor sem a unidade, para se editar o número e não o texto.
        /// "2,80 m" abre como "2,80"; "11.2.1 · Parede interior" como "11.2.1".
        /// </summary>
        private static string ValorCru(Propriedade p)
        {
            string v = (p.Valor ?? "").Trim();
            switch (p.Campo)
            {
                case "altura":
                case "largura":
                case "espessura":
                case "larguraVao":
                case "alturaVao":
                case "quantidadeVao":
                    int esp = v.IndexOf(' ');
                    return esp > 0 ? v.Substring(0, esp) : v;
                case "artigo":
                    int sep = v.IndexOf(" · ", StringComparison.Ordinal);
                    return sep > 0 ? v.Substring(0, sep) : v;
                default:
                    return v;
            }
        }

        private void MostrarPropriedades()
        {
            bool antes = _aCarregar;
            _aCarregar = true;
            try
            {
                _propriedades.Rows.Clear();

                var no = NoSeleccionado;
                AlternarTextoDasPropriedades(_alternarProps, _propsAbertas());

                // «Medir aqui» só em nós de artigo, e só onde a aba o permite.
                _btnMedirAqui.Visible = PermiteMedirAqui && no != null && no.PermiteMedirAqui;

                if (no == null || no.Propriedades == null) return;

                var linhas = new List<DataGridViewRow>();
                foreach (var p in no.Propriedades)
                {
                    if (_essenciais && !p.Essencial) continue;

                    var linha = new DataGridViewRow();
                    linha.CreateCells(_propriedades, p.Nome, p.Valor);
                    linha.Height = PaletteTheme.AlturaLinha;
                    linha.Tag = p;
                    // O realce diz "isto edita-se".
                    if (p.Editavel) linha.Cells[1].Style.BackColor = PaletteTheme.FundoEditavel;
                    else linha.Cells[1].ReadOnly = true;
                    linhas.Add(linha);
                }
                if (linhas.Count > 0) _propriedades.Rows.AddRange(linhas.ToArray());
            }
            finally { _aCarregar = antes; }
        }

        // ------------------------------------------------------------------
        // Filtros
        // ------------------------------------------------------------------

        private void AbrirFiltros()
        {
            if (_raiz == null)
            {
                PaletteHost.Log("Não há resultados para filtrar.");
                return;
            }

            var popup = new FiltrosPopup(_raiz, Estado.Filtro, novo =>
            {
                Estado.Filtro = novo;
                Actualizar();
            });
            // Devolver o foco a quem abriu, ao fechar. Sem isto o foco ficava
            // preso num painel que já não existe.
            popup.Closed += (s, e) => { try { _btnFiltros.Focus(); } catch { } };
            popup.Show(_btnFiltros, new Point(0, _btnFiltros.Height));
        }

        /// <summary>
        /// O badge, os chips e o estado do «Limpar».
        ///
        /// O badge conta GRUPOS de filtro, não valores: escolher três pisos é
        /// um filtro, não três.
        /// </summary>
        private void ActualizarBarra()
        {
            var filtro = Estado.Filtro;
            int n = filtro.Contagem;

            _btnFiltros.Text = n > 0 ? "Filtros (" + n + ")" : "Filtros";
            _btnFiltros.Font = n > 0 ? PaletteTheme.PequenoNegrito : PaletteTheme.Pequeno;
            _btnFiltros.BackColor = n > 0 ? PaletteTheme.Palido : PaletteTheme.Fundo;
            _btnFiltros.ForeColor = n > 0 ? PaletteTheme.Acento : PaletteTheme.Tinta;
            _btnFiltros.FlatAppearance.BorderColor =
                n > 0 ? PaletteTheme.Acento : PaletteTheme.BordaCampo;
            _btnFiltros.AccessibleDescription = n == 0
                ? "Nenhum filtro aplicado."
                : n + " filtro(s): " + string.Join("; ", filtro.Chips().ToArray());

            // A faixa dos chips só existe quando há filtros: uma linha vazia
            // permanente rouba altura à árvore sem dizer nada.
            var chips = filtro.Chips();
            if (chips.Count == 0)
            {
                _chips.Text = "";
                _chips.Visible = false;
                _chips.Height = 0;
            }
            else
            {
                // Até dois nomeados; o resto conta-se. Com todos escritos, a
                // faixa passava a duas linhas e o badge já diz quantos são.
                var mostrar = new List<string>();
                for (int i = 0; i < chips.Count && i < 2; i++) mostrar.Add(chips[i]);
                if (chips.Count > 2) mostrar.Add("+" + (chips.Count - 2));

                _chips.Text = "▾  " + string.Join("   ·   ", mostrar.ToArray());
                _chips.AccessibleDescription = string.Join("; ", chips.ToArray());
                _chips.Height = 18;
                _chips.Visible = true;
            }

            // Escondido e não desactivado: um botão flat desactivado sobre
            // grafite fica indistinguível do fundo e lê-se como defeito.
            _btnLimpar.Visible = Estado.AFiltrar;
        }

        // ------------------------------------------------------------------
        // Teclado
        // ------------------------------------------------------------------

        private void TeclasDaArvore(object sender, KeyEventArgs e)
        {
            var no = NoSeleccionado;

            switch (e.KeyCode)
            {
                case Keys.Enter:
                case Keys.Space:
                    if (no == null) return;
                    if (no.Filhos.Count > 0) { Estado.Alternar(no.Id); Actualizar(); }
                    else if (no.PermiteMedirAqui && PermiteMedirAqui)
                    {
                        var h = MedirAquiPedido;
                        if (h != null) h(this, EventArgs.Empty);
                    }
                    e.Handled = e.SuppressKeyPress = true;
                    return;

                case Keys.Left:
                    // Fechado já, sobe ao pai — o Left leva sempre para "menos
                    // fundo", que é o que se espera de uma árvore.
                    if (no == null) return;
                    if (no.Filhos.Count > 0 && Estado.Expandido(no.Id))
                    {
                        Estado.Recolher(no.Id);
                        Actualizar();
                    }
                    else if (no.Pai != null) Seleccionar(no.Pai.Id);
                    e.Handled = e.SuppressKeyPress = true;
                    return;

                case Keys.Right:
                    if (no == null || no.Filhos.Count == 0) return;
                    if (!Estado.Expandido(no.Id)) { Estado.Expandir(no.Id); Actualizar(); }
                    else Seleccionar(no.Filhos[0].Id);
                    e.Handled = e.SuppressKeyPress = true;
                    return;

                case Keys.Home:
                case Keys.End:
                    if (_arvore.Rows.Count == 0) return;
                    int alvo = e.KeyCode == Keys.Home ? 0 : _arvore.Rows.Count - 1;
                    _arvore.CurrentCell = _arvore.Rows[alvo].Cells[0];
                    e.Handled = e.SuppressKeyPress = true;
                    return;
            }
        }

        // ------------------------------------------------------------------
        // Desenho da árvore
        // ------------------------------------------------------------------

        private static int Recuo(NoResultado no)
        {
            return PaletteTheme.MargemPequena + no.Profundidade * PaletteTheme.RecuoPorNivel;
        }

        private static string DescreverAlertas(AlertaNo alertas)
        {
            var partes = new List<string>();
            if ((alertas & AlertaNo.PorClassificar) != 0)
                partes.Add("Por classificar — sai no fim da folha.");
            if ((alertas & AlertaNo.ArtigoDesconhecido) != 0)
                partes.Add("O mapa não conhece este artigo — sai no fim da folha.");
            if ((alertas & AlertaNo.VaosExcessivos) != 0)
                partes.Add("Os vãos descontam mais do que a medição tem.");
            return string.Join("\n", partes.ToArray());
        }

        /// <summary>
        /// Desenha a coluna da estrutura: guias, [▸]/[▾], ícone e texto.
        ///
        /// À mão porque o DataGridView não tem hierarquia nenhuma — e com
        /// espaços não dava: a letra é proporcional, e o nível 3 acabava
        /// alinhado com o 2 consoante o texto que estivesse por cima.
        /// </summary>
        private void DesenharCelula(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var no = NoDaLinha(e.RowIndex);
            if (no == null) return;

            e.PaintBackground(e.CellBounds, true);

            var g = e.Graphics;
            Color tinta = (e.State & DataGridViewElementStates.Selected) != 0
                ? PaletteTheme.TextoSeleccionado
                : (e.CellStyle.ForeColor.IsEmpty ? PaletteTheme.Tinta : e.CellStyle.ForeColor);

            using (var caneta = new Pen(PaletteTheme.Guia))
                for (int n = 1; n <= no.Profundidade; n++)
                {
                    int x = e.CellBounds.X + PaletteTheme.MargemPequena +
                            (n - 1) * PaletteTheme.RecuoPorNivel + 6;
                    g.DrawLine(caneta, x, e.CellBounds.Top, x, e.CellBounds.Bottom);
                }

            int xc = e.CellBounds.X + Recuo(no);

            if (no.Filhos.Count > 0)
            {
                bool aberto = Estado.AFiltrar || Estado.Expandido(no.Id);
                using (var pincel = new SolidBrush(PaletteTheme.Acento))
                    g.DrawString(aberto ? "▾" : "▸", PaletteTheme.Pequeno, pincel,
                                 xc, e.CellBounds.Y + 4);
            }
            xc += PaletteTheme.LarguraTwisty;

            // Um quadrado da cor do tipo do nó, à falta de ícones vectoriais
            // para seis tipos. Diz o mesmo — a que família a linha pertence —
            // e não custa um recurso do GDI por linha.
            using (var pincel = new SolidBrush(PaletteTheme.CorDoNo(no.Tipo)))
                g.FillRectangle(pincel, xc + 1, e.CellBounds.Y + 7, 7, 7);
            xc += PaletteTheme.LarguraIconeNo;

            // Os alertas em TEXTO, não só em cor: em alto contraste a cor
            // desaparece, e há quem não a distinga.
            string texto = no.Rotulo ?? "";
            if ((no.Alertas & AlertaNo.PorClassificar) != 0 && !no.EhGrupo) texto += "  ⚑";
            if ((no.Alertas & AlertaNo.VaosExcessivos) != 0) texto += "  ⚠";

            int largura = e.CellBounds.Right - xc - 2;
            if (EhProxima != null && EhProxima(no))
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

                largura = selo.Left - xc - 4;
            }

            var caixa = new Rectangle(xc, e.CellBounds.Y,
                                      largura < 10 ? 10 : largura, e.CellBounds.Height);
            TextRenderer.DrawText(g, texto, e.CellStyle.Font ?? PaletteTheme.Normal,
                caixa, tinta,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);

            e.Handled = true;
        }

        /// <summary>
        /// Diz se este nó é o destino da próxima medição, para levar a etiqueta
        /// `PRÓXIMA`. Cada aba responde à sua maneira — a Arquitetura compara
        /// com o Config, as Contagens não têm o conceito — por isso é a aba que
        /// o define. Nulo quer dizer "nenhum".
        /// </summary>
        public Func<NoResultado, bool> EhProxima { get; set; }
    }
}
