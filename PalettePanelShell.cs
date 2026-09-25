using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// O que se passa quando alguém edita uma propriedade no painel.
    ///
    /// O painel NÃO escreve no desenho. Quem manda no desenho (Palette.cs,
    /// via AlvRepo) é que decide o que fazer com o que foi editado.
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
    /// As peças de composição da paleta compacta: cabeçalho, secções que se
    /// recolhem e títulos de secção.
    ///
    /// Existem para as quatro abas terem a mesma forma sem a copiarem quatro
    /// vezes. O painel é estreito e usado o dia inteiro: o que importa é a
    /// densidade — que cada linha de píxeis diga alguma coisa — e que o mesmo
    /// gesto esteja sempre no mesmo sítio.
    /// </summary>
    public static class PalettePanelShell
    {
        /// <summary>
        /// O cabeçalho: que desenho está activo e o que vai sair na próxima
        /// medição.
        ///
        /// A próxima medição aparece DUAS vezes de propósito — aqui e no
        /// resumo da CONFIGURAÇÃO recolhida. É a única coisa da paleta que
        /// muda o que acontece a seguir sem ninguém estar a olhar para ela, e
        /// medir dez paredes para o artigo errado descobre-se tarde de mais.
        /// </summary>
        public sealed class Cabecalho : Panel
        {
            private readonly Label _dwg;
            private readonly Label _proxima;

            public Cabecalho()
            {
                Dock = DockStyle.Top;
                Height = PaletteTheme.AlturaCabecalho;
                BackColor = PaletteTheme.AzulTopo;
                Padding = new Padding(PaletteTheme.Margem, 3, PaletteTheme.Margem, 3);

                _proxima = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = PaletteTheme.Pequeno,
                    ForeColor = PaletteTheme.AcentoEscuro,
                    AutoEllipsis = true,
                    AccessibleName = "Próxima medição"
                };
                _dwg = new Label
                {
                    Dock = DockStyle.Top,
                    Height = 16,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = PaletteTheme.PequenoNegrito,
                    ForeColor = PaletteTheme.Tinta,
                    AutoEllipsis = true,
                    AccessibleName = "Desenho activo"
                };

                Controls.Add(_proxima);
                Controls.Add(_dwg);
            }

            /// <summary>O DWG activo. Sem desenho aberto di-lo, em vez de ficar vazio.</summary>
            public void DefinirDesenho(string nome)
            {
                bool ha = !string.IsNullOrEmpty(nome);
                _dwg.Text = ha ? "● " + nome : "○ sem desenho aberto";
                _dwg.ForeColor = ha ? PaletteTheme.Tinta : PaletteTheme.Apagado;
            }

            public void DefinirProxima(string resumo)
            {
                _proxima.Text = "Próxima medição: " + (resumo ?? "");
                _proxima.AccessibleDescription = _proxima.Text;
                ToolTip().SetToolTip(_proxima, _proxima.Text);
            }

            private ToolTip _dica;
            private ToolTip ToolTip()
            {
                return _dica ?? (_dica = new ToolTip { AutoPopDelay = 15000 });
            }
        }

        /// <summary>
        /// Título de uma secção que não se recolhe: "MEDIR", "RESULTADOS".
        /// </summary>
        public sealed class Titulo : Panel
        {
            private readonly Label _texto;
            private readonly Label _meta;

            public Titulo(string texto, string meta)
            {
                Dock = DockStyle.Top;
                Height = PaletteTheme.AlturaTituloSeccao;
                BackColor = PaletteTheme.FundoSeccao;
                Padding = new Padding(PaletteTheme.Margem, 0, PaletteTheme.Margem, 0);

                _meta = new Label
                {
                    Dock = DockStyle.Right,
                    AutoSize = false,
                    Width = 150,
                    TextAlign = ContentAlignment.MiddleRight,
                    Font = PaletteTheme.Pequeno,
                    ForeColor = PaletteTheme.Apagado,
                    AutoEllipsis = true,
                    Text = meta ?? ""
                };
                _texto = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = PaletteTheme.TituloSeccao,
                    ForeColor = PaletteTheme.Tinta,
                    Text = texto
                };

                Controls.Add(_texto);
                Controls.Add(_meta);
            }

            public string Meta
            {
                get { return _meta.Text; }
                set { _meta.Text = value ?? ""; }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var caneta = new Pen(PaletteTheme.Linha))
                    e.Graphics.DrawLine(caneta, 0, Height - 1, Width, Height - 1);
            }
        }

        /// <summary>
        /// Uma secção que se recolhe, com resumo no cabeçalho.
        ///
        /// O RESUMO É O QUE TORNA A RECOLHA ACEITÁVEL. Uma secção fechada que
        /// não diz o que tem lá dentro obriga a abri-la para confirmar, e aí
        /// mais valia estar sempre aberta. Fechada, esta continua a dizer o
        /// essencial — "PISO 0 · ALVENARIA · 11.2.1 · h 2,80 m" —, que é
        /// precisamente o que a pessoa iria lá ver.
        /// </summary>
        public sealed class Seccao : Panel
        {
            private readonly Button _cabecalho;
            private readonly Panel _conteudo;
            private readonly string _titulo;
            private string _resumo = "";
            private bool _recolhida;

            /// <summary>Disparado depois de recolher ou expandir.</summary>
            public event EventHandler EstadoMudou;

            public Seccao(string titulo, bool comecaRecolhida)
            {
                _titulo = titulo;
                Dock = DockStyle.Top;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                BackColor = PaletteTheme.Fundo;

                _conteudo = new Panel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    BackColor = PaletteTheme.Fundo,
                    Padding = new Padding(PaletteTheme.Margem, PaletteTheme.MargemPequena,
                                          PaletteTheme.Margem, PaletteTheme.Margem)
                };

                _cabecalho = new Button
                {
                    Dock = DockStyle.Top,
                    Height = PaletteTheme.AlturaTituloSeccao,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = PaletteTheme.FundoSeccao,
                    ForeColor = PaletteTheme.Tinta,
                    Font = PaletteTheme.TituloSeccao,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(PaletteTheme.Margem, 0, PaletteTheme.Margem, 0),
                    UseVisualStyleBackColor = false,
                    TabStop = true
                };
                _cabecalho.FlatAppearance.BorderSize = 0;
                _cabecalho.FlatAppearance.MouseOverBackColor = PaletteTheme.Palido;
                PaletteTheme.ComFoco(_cabecalho);
                _cabecalho.Click += (s, e) => Alternar();

                // O conteúdo entra PRIMEIRO para o cabeçalho ficar por cima
                // dele: num Dock=Top, o último a entrar é o que fica no topo.
                Controls.Add(_conteudo);
                Controls.Add(_cabecalho);

                Recolhida = comecaRecolhida;
            }

            /// <summary>Onde os campos da secção são postos.</summary>
            public Panel Conteudo { get { return _conteudo; } }

            public bool Recolhida
            {
                get { return _recolhida; }
                set
                {
                    _recolhida = value;
                    _conteudo.Visible = !value;
                    Actualizar();
                    var h = EstadoMudou;
                    if (h != null) h(this, EventArgs.Empty);
                }
            }

            /// <summary>
            /// O que a secção diz quando está fechada. Escrito também quando
            /// está aberta: assim o texto não aparece do nada ao recolher.
            /// </summary>
            public string Resumo
            {
                get { return _resumo; }
                set { _resumo = value ?? ""; Actualizar(); }
            }

            public void Alternar() { Recolhida = !Recolhida; }

            private void Actualizar()
            {
                string seta = _recolhida ? "▶" : "▼";
                _cabecalho.Text = _recolhida && _resumo.Length > 0
                    ? seta + "  " + _titulo + "    " + _resumo
                    : seta + "  " + _titulo;

                // Nem só a seta: quem usa leitor de ecrã, ou quem não distingue
                // a diferença entre ▶ e ▼ num painel denso, precisa da palavra.
                _cabecalho.AccessibleName = _titulo;
                _cabecalho.AccessibleDescription =
                    (_recolhida ? "Recolhida. " : "Expandida. ") + _resumo;

                ToolTip().SetToolTip(_cabecalho,
                    (_recolhida ? "Expandir " : "Recolher ") + _titulo +
                    (_resumo.Length > 0 ? "\n" + _resumo : ""));
            }

            private ToolTip _dica;
            private ToolTip ToolTip()
            {
                return _dica ?? (_dica = new ToolTip { AutoPopDelay = 15000 });
            }
        }

        /// <summary>
        /// Um cartão com cantos arredondados: cabeçalho por cima, corpo por
        /// baixo, borda de 1 px à volta — a linguagem do mockup "claro
        /// refinado" (Deploy/MockupPalette/index-claro-refinado.html).
        ///
        /// O arredondamento é por recorte de <see cref="Control.Region"/>, a
        /// técnica standard do WinForms para isto. Ressalva conhecida: o
        /// recorte em si não tem anti-aliasing — a curva corta em blocos de
        /// pixel, não suave como num browser. Num raio pequeno (8 px) isso
        /// quase não se nota à distância normal de uso.
        ///
        /// Nada a ver com o bug de 2026-09-19 (commit 1377f0b): aquele era
        /// uma TableLayoutPanel de linhas AutoSize a receber um Panel-célula
        /// sem AutoSize próprio. Aqui o cartão é um Panel Dock=Top simples,
        /// com AutoSize=true e filhos também Dock=Top — o mesmo padrão que
        /// já sustenta o resto do painel a empilhar-se sozinho.
        /// </summary>
        public static Panel CartaoArredondado(Control cabecalho, Control corpo, int raio = 8)
        {
            var cartao = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = PaletteTheme.Fundo,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, PaletteTheme.Margem)
            };
            cabecalho.Dock = DockStyle.Top;
            corpo.Dock = DockStyle.Top;
            cartao.Controls.Add(corpo);
            cartao.Controls.Add(cabecalho);

            EventHandler recortar = (s, e) =>
            {
                if (cartao.Width <= 0 || cartao.Height <= 0) return;
                using (var caminho = CaminhoArredondado(0, 0, cartao.Width, cartao.Height, raio))
                    cartao.Region = new Region(caminho);
            };
            cartao.Resize += recortar;
            cartao.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var caminho = CaminhoArredondado(0, 0, cartao.Width - 1, cartao.Height - 1, raio))
                using (var caneta = new Pen(PaletteTheme.Linha))
                    e.Graphics.DrawPath(caneta, caminho);
            };
            recortar(cartao, EventArgs.Empty);
            return cartao;
        }

        private static GraphicsPath CaminhoArredondado(int x, int y, int largura, int altura, int raio)
        {
            var caminho = new GraphicsPath();
            int d = Math.Max(1, raio * 2);
            caminho.AddArc(x, y, d, d, 180, 90);
            caminho.AddArc(x + largura - d, y, d, d, 270, 90);
            caminho.AddArc(x + largura - d, y + altura - d, d, d, 0, 90);
            caminho.AddArc(x, y + altura - d, d, d, 90, 90);
            caminho.CloseFigure();
            return caminho;
        }

        /// <summary>
        /// A grelha de acções da secção MEDIR, como no mockup aprovado: seis
        /// células iguais, separadas por um fio de 1 px, cada uma com o ícone
        /// por cima do texto.
        ///
        /// NÃO É UMA ToolStrip, e a diferença não é estética. Uma ToolStrip
        /// com ícones de 24 px e texto por baixo ocupa uns sessenta píxeis de
        /// altura e alinha os botões à esquerda, deixando o resto da faixa
        /// vazio. Num painel de 480 px usado o dia inteiro, essa faixa é
        /// espaço que a árvore de resultados não tem — e era isso que fazia a
        /// paleta parecer um formulário em vez de um instrumento.
        ///
        /// O fundo da grelha é a cor da linha: as células ficam por cima com
        /// uma margem de 1 px, e o que se vê entre elas é o fundo. É como se
        /// desenha uma grelha sem pintar trinta bordas.
        /// </summary>
        public static TableLayoutPanel GrelhaDeAccoes(int colunas)
        {
            var g = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = colunas,
                RowCount = 1,
                BackColor = PaletteTheme.Linha,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            for (int i = 0; i < colunas; i++)
                g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / colunas));
            g.RowStyles.Add(new RowStyle(SizeType.Absolute, PaletteTheme.AlturaBotaoAccao));
            return g;
        }

        /// <summary>
        /// Uma acção da grelha: ícone pequeno por cima, texto por baixo, sem
        /// borda. O clique é protegido — um erro vai para a linha de comandos,
        /// nunca abre uma caixa vermelha por cima do desenho.
        /// </summary>
        /// <summary>
        /// Um botão de acção que se DESENHA A SI PRÓPRIO.
        ///
        /// Um Button normal, mesmo com FlatStyle.Flat e BackColor definido,
        /// continua a passar pelo renderizador do Windows — e dentro de uma
        /// paleta alojada no AutoCAD ele impunha o cinzento claro do sistema
        /// por cima da cor que lhe tínhamos dado. O resultado eram células
        /// brancas num painel grafite, com o texto escuro por cima.
        ///
        /// Com UserPaint ligado, o que se vê é exactamente o que aqui se
        /// desenha: fundo, ícone e texto. Deixa de haver terceiro a opinar.
        /// </summary>
        private sealed class BotaoAccao : Button
        {
            private readonly bool _destaque;
            private bool _sobre;

            public BotaoAccao(bool destaque)
            {
                _destaque = destaque;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                MouseEnter += (s, e) => { _sobre = true; Invalidate(); };
                MouseLeave += (s, e) => { _sobre = false; Invalidate(); };
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                Color fundo = _sobre ? PaletteTheme.Palido
                            : _destaque ? PaletteTheme.FundoSeccao : PaletteTheme.Fundo;

                using (var pincel = new SolidBrush(fundo))
                    g.FillRectangle(pincel, ClientRectangle);

                // ÍCONE EM CIMA, RÓTULO POR BAIXO — ao contrário do
                // ícone-ao-lado que este botão tinha antes.
                //
                // Uma tentativa anterior disto, num Button NATIVO com
                // TextImageRelation.ImageAboveText, cortava as descidas do
                // texto ("g", "p", "q") porque a fila da grelha só tinha 30 px
                // — altura para uma linha ao lado do ícone, não para ícone
                // MAIS uma etiqueta por baixo. Aqui o texto é desenhado à mão
                // numa caixa com a altura que sobra do botão INTEIRO, não a
                // que o WinForms decidisse sozinho — por isso não há o mesmo
                // corte, mesmo com o mosaico agora a só 28 px: as margens
                // ficam no mínimo e o rótulo é o texto CURTO (Accao(rotulo:)),
                // desenhado para caber numa linha só a essa altura.
                int yIcone = 1;
                if (Image != null)
                {
                    g.DrawImage(Image, (Width - Image.Width) / 2, yIcone,
                        Image.Width, Image.Height);
                    yIcone += Image.Height + 1;
                }

                var caixa = new Rectangle(2, yIcone, Width - 4, Height - yIcone);
                TextRenderer.DrawText(g, Text, Font, caixa, PaletteTheme.Tinta,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top |
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

                // O foco por teclado tem de se ver: sem isto, quem navega com
                // o Tab não sabe onde está.
                if (Focused)
                    using (var caneta = new Pen(PaletteTheme.Acento, 2f))
                        g.DrawRectangle(caneta, 1, 1, Width - 3, Height - 3);
            }
        }

        /// <param name="texto">
        /// Nome completo: vai para a dica e para a acessibilidade, SEMPRE.
        /// </param>
        /// <param name="rotulo">
        /// O que se lê no mosaico, se precisar de ser mais curto do que
        /// <paramref name="texto"/> para caber numa linha só num mosaico
        /// baixo (28 px — ver PaletteTheme.AlturaBotaoAccao). Nulo usa
        /// <paramref name="texto"/> por extenso. Encurtar SÓ o rótulo, nunca
        /// a dica, é o que evita perder a descrição completa de quem passa o
        /// rato ou usa leitor de ecrã.
        /// </param>
        public static Button Accao(string texto, Image icone, EventHandler aoClicar,
                                   ToolTip dicas = null, bool destaque = false, string rotulo = null)
        {
            var fundo = destaque ? PaletteTheme.Palido : PaletteTheme.Fundo;

            var b = new BotaoAccao(destaque)
            {
                Text = rotulo ?? texto,
                // Reduzido: o Button pinta a imagem no tamanho NATIVO, e os
                // ícones nascem a 64×64. Sem isto transbordavam por cima do
                // texto e saíam cortados em baixo.
                Image = PaletteTheme.Icone(icone, PaletteTheme.LadoIconeAccao),
                Dock = DockStyle.Fill,
                // A margem é o fio da grelha: o fundo do contentor aparece por
                // aqui e desenha as separações sem pintar borda nenhuma.
                Margin = new Padding(0, 0, 1, 1),
                FlatStyle = FlatStyle.Flat,
                BackColor = fundo,
                ForeColor = PaletteTheme.Tinta,
                Font = destaque ? PaletteTheme.PequenoNegrito : PaletteTheme.Pequeno,
                // TextImageRelation/ImageAlign/TextAlign/Padding do WinForms
                // não se aplicam aqui: o ícone e o texto são desenhados à mão
                // em BotaoAccao.OnPaint (ícone em cima, rótulo por baixo).
                UseVisualStyleBackColor = false,
                AccessibleName = texto
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = PaletteTheme.Palido;
            b.FlatAppearance.MouseDownBackColor = PaletteTheme.AzulTopo;

            if (dicas != null) dicas.SetToolTip(b, texto);

            b.Click += (s, e) =>
            {
                try { if (aoClicar != null) aoClicar(s, e); }
                catch (Exception ex) { PaletteHost.Log(texto + ": " + ex.Message); }
            };
            return b;
        }

        /// <summary>
        /// Um botão de barra compacto: só texto, baixo, sem borda. Para as
        /// acções sobre resultados, que são frequentes mas secundárias — ao
        /// contrário das de MEDIR, que merecem o ícone.
        /// </summary>
        public static Button BotaoCompacto(string texto, EventHandler aoClicar,
                                           ToolTip dicas = null, string descricao = null)
        {
            var b = new Button
            {
                Text = texto,
                Dock = DockStyle.Left,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Height = PaletteTheme.AlturaCampo,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaletteTheme.Fundo,
                ForeColor = PaletteTheme.Tinta,
                Font = PaletteTheme.Pequeno,
                Padding = new Padding(7, 0, 7, 0),
                Margin = new Padding(0, 0, 3, 0),
                UseVisualStyleBackColor = false,
                AccessibleName = texto,
                AccessibleDescription = descricao
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = PaletteTheme.Linha;
            b.FlatAppearance.MouseOverBackColor = PaletteTheme.Palido;

            if (dicas != null) dicas.SetToolTip(b, descricao ?? texto);

            b.Click += (s, e) =>
            {
                try { if (aoClicar != null) aoClicar(s, e); }
                catch (Exception ex) { PaletteHost.Log(texto + ": " + ex.Message); }
            };
            return b;
        }

        /// <summary>
        /// As propriedades como MOSAICOS DE MÉTRICA, e não como lista.
        ///
        /// Uma lista de dezassete linhas obriga a percorrer para encontrar o
        /// número que se procura. Em mosaico, o comprimento, a altura, a área
        /// bruta, a líquida, o volume e os pré-aros lêem-se todos de uma
        /// passagem — que é como se confere uma medição de que se desconfia.
        ///
        /// DESENHADO POR INTEIRO, num só OnPaint. São seis a doze valores que
        /// mudam a cada selecção: com um controlo por mosaico seriam dezenas de
        /// criações e destruições por clique, e cada um deles à mercê do
        /// renderizador do Windows — que é exactamente o que pintou de branco
        /// os botões de MEDIR.
        /// </summary>
        public sealed class MosaicoMetricas : Panel
        {
            private readonly List<Propriedade> _metricas = new List<Propriedade>();
            private readonly List<Rectangle> _caixas = new List<Rectangle>();
            private readonly TextBox _editor;
            private int _aEditar = -1;
            private int _sobre = -1;

            /// <summary>O mosaico "com foco" por teclado — Left/Right/Up/Down move-o,
            /// Enter/Espaço edita-o. Independente de <see cref="_sobre"/> (rato).</summary>
            private int _foco = -1;

            /// <summary>Largura a que um mosaico deixa de caber com folga.</summary>
            private const int LarguraMinima = 104;
            private const int AlturaMosaico = 38;

            /// <summary>Alguém acabou de escrever num mosaico editável.</summary>
            public event EventHandler<PropriedadeEditadaEventArgs> Editado;

            public MosaicoMetricas()
            {
                Dock = DockStyle.Fill;
                BackColor = PaletteTheme.Fundo;
                DoubleBuffered = true;
                SetStyle(ControlStyles.ResizeRedraw, true);
                // Um Panel normal não entra na ordem de Tab nem aceita foco: é
                // um canvas pintado à mão, sem controlos filho por mosaico.
                // Sem isto, PROPRIEDADES ficava fora do alcance do teclado.
                SetStyle(ControlStyles.Selectable, true);
                TabStop = true;

                // Um editor só, reposicionado sobre o mosaico que se está a
                // editar. Criar uma caixa por mosaico seria doze caixas
                // invisíveis à espera de uma que raramente acontece.
                _editor = new TextBox
                {
                    Visible = false,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = PaletteTheme.FundoCampo,
                    ForeColor = PaletteTheme.Tinta,
                    Font = PaletteTheme.Normal
                };
                _editor.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter) { Confirmar(); VoltarAoMosaico(); e.Handled = e.SuppressKeyPress = true; }
                    else if (e.KeyCode == Keys.Escape) { Cancelar(); VoltarAoMosaico(); e.Handled = e.SuppressKeyPress = true; }
                };
                _editor.Leave += (s, e) => Confirmar();
                Controls.Add(_editor);

                MouseMove += (s, e) =>
                {
                    int i = MosaicoEm(e.Location);
                    if (i == _sobre) return;
                    _sobre = i;
                    Cursor = i >= 0 && _metricas[i].Editavel ? Cursors.IBeam : Cursors.Default;
                    Invalidate();
                };
                MouseLeave += (s, e) => { _sobre = -1; Invalidate(); };
                MouseDown += (s, e) =>
                {
                    int i = MosaicoEm(e.Location);
                    if (i < 0) return;
                    _foco = i;
                    if (_metricas[i].Editavel) Editar(i);
                    else Invalidate();
                };
            }

            /// <summary>
            /// Mostra estas métricas. Substitui as anteriores.
            ///
            /// Grava primeiro qualquer edição em curso — <see cref="Confirmar"/>,
            /// não <see cref="Cancelar"/>. Isto é chamado sempre que a selecção
            /// muda ou a árvore é reconstruída, e não só quando o campo perde o
            /// foco por um clique: alternar Essenciais/Tudo ou um refresco em
            /// fundo enquanto se edita um mosaico não pode apagar em silêncio o
            /// que já estava escrito na caixa.
            /// </summary>
            public void Definir(IEnumerable<Propriedade> metricas)
            {
                Confirmar();
                _metricas.Clear();
                if (metricas != null) _metricas.AddRange(metricas);
                _foco = _metricas.Count > 0 ? 0 : -1;
                Invalidate();
            }

            /// <summary>O nó a que estas métricas pertencem, para o evento.</summary>
            public NoResultado No { get; set; }

            /// <summary>Os handles abrangidos pela selecção, para a edição em lote.</summary>
            public List<string> Handles { get; set; }

            // ----------------------------------------------------------
            // Disposição
            // ----------------------------------------------------------

            private int Colunas()
            {
                int cabem = Math.Max(1, ClientSize.Width / LarguraMinima);
                return Math.Min(cabem, 6);
            }

            private void Medir()
            {
                _caixas.Clear();
                if (_metricas.Count == 0) return;

                int cols = Colunas();
                int largura = ClientSize.Width / cols;

                for (int i = 0; i < _metricas.Count; i++)
                {
                    int col = i % cols, lin = i / cols;
                    // A última coluna leva o resto da divisão, para a faixa
                    // fechar direita na margem em vez de deixar uma fresta.
                    int w = col == cols - 1 ? ClientSize.Width - largura * col : largura;
                    _caixas.Add(new Rectangle(largura * col, AlturaMosaico * lin, w, AlturaMosaico));
                }
            }

            private int MosaicoEm(Point p)
            {
                for (int i = 0; i < _caixas.Count; i++)
                    if (_caixas[i].Contains(p)) return i;
                return -1;
            }

            /// <summary>A altura que estas métricas precisam, para o pai a dar.</summary>
            public int AlturaNecessaria
            {
                get
                {
                    if (_metricas.Count == 0) return AlturaMosaico;
                    int cols = Colunas();
                    return ((_metricas.Count + cols - 1) / cols) * AlturaMosaico;
                }
            }

            // ----------------------------------------------------------
            // Desenho
            // ----------------------------------------------------------

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                using (var pincel = new SolidBrush(PaletteTheme.Fundo))
                    g.FillRectangle(pincel, ClientRectangle);

                Medir();
                if (_metricas.Count == 0)
                {
                    TextRenderer.DrawText(g, "Escolha uma linha da árvore para ver as medidas.",
                        PaletteTheme.Normal, ClientRectangle, PaletteTheme.Apagado,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    return;
                }

                using (var fio = new Pen(PaletteTheme.LinhaSuave))
                for (int i = 0; i < _caixas.Count; i++)
                {
                    var r = _caixas[i];
                    var m = _metricas[i];

                    // O QUE SE EDITA TEM FUNDO AMARELO — sempre, não só ao
                    // passar o rato. Era um sublinhado tracejado fino de mais
                    // para se notar (relatado pelo utilizador ao ver "Altura"
                    // editável sem nenhum sinal visível). O mesmo amarelo já
                    // existe como PaletteTheme.FundoEditavel — a convenção
                    // antiga da grelha larga, agora aplicada aqui também.
                    if (m.Editavel)
                        using (var pincel = new SolidBrush(PaletteTheme.FundoEditavel))
                            g.FillRectangle(pincel, r);

                    // Ao passar o rato, um contorno de acento por cima do
                    // amarelo — não troca o fundo, senão perdia-se o sinal
                    // "isto edita-se" precisamente quando se está prestes a
                    // editar.
                    if (i == _sobre && m.Editavel)
                        using (var caneta = new Pen(PaletteTheme.AcentoEscuro))
                            g.DrawRectangle(caneta, r.X, r.Y, r.Width - 1, r.Height - 1);

                    // O rótulo pequeno por cima, o valor grande por baixo: o
                    // número é o que se procura, o nome só o identifica.
                    var rNome = new Rectangle(r.X + 9, r.Y + 4, r.Width - 12, 13);
                    TextRenderer.DrawText(g, m.Nome, PaletteTheme.Pequeno, rNome,
                        PaletteTheme.Apagado,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);

                    Color tinta = Negativo(m.Valor) ? PaletteTheme.VermelhoDeducao
                                : m.Editavel ? PaletteTheme.Tinta : PaletteTheme.CorParede;

                    var rValor = new Rectangle(r.X + 9, r.Y + 17, r.Width - 12, 17);
                    TextRenderer.DrawText(g, m.Valor, PaletteTheme.Numero, rValor, tinta,
                        TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);

                    g.DrawLine(fio, r.Right - 1, r.Y + 5, r.Right - 1, r.Bottom - 5);
                    g.DrawLine(fio, r.X, r.Bottom - 1, r.Right, r.Bottom - 1);
                }

                // O mesmo contorno de acento usado em todo o resto da paleta
                // (PaletteTheme.ComFoco), aqui à volta do mosaico alvo em vez
                // do controlo inteiro — é o mosaico, não o painel, que se edita.
                if (Focused && _foco >= 0 && _foco < _caixas.Count)
                {
                    var rFoco = _caixas[_foco];
                    using (var caneta = new Pen(PaletteTheme.Acento, 2f))
                        g.DrawRectangle(caneta, rFoco.X + 1, rFoco.Y + 1, rFoco.Width - 3, rFoco.Height - 3);
                }

                // Por último, e não ao início: é assim que o resto da paleta
                // sobrepõe o contorno de PaletteTheme.ComFoco ao desenho do
                // próprio controlo (botão, grelha, caixa). Ao início, o
                // FillRectangle do fundo, logo a seguir, apagava-o.
                base.OnPaint(e);
            }

            private static bool Negativo(string valor)
            {
                return !string.IsNullOrEmpty(valor) && valor.TrimStart().StartsWith("−");
            }

            // ----------------------------------------------------------
            // Teclado
            // ----------------------------------------------------------

            /// <summary>
            /// Sem isto, um Panel devolve as setas/Enter/Espaço ao ciclo de
            /// navegação por Tab do formulário (viram mnemónicas ou saltos de
            /// foco) em vez de chegarem a <see cref="OnKeyDown"/>.
            /// </summary>
            protected override bool IsInputKey(Keys keyData)
            {
                switch (keyData)
                {
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Home:
                    case Keys.End:
                    case Keys.Enter:
                    case Keys.Space:
                        return true;
                    default:
                        return base.IsInputKey(keyData);
                }
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                Medir();
                if (_caixas.Count == 0) return;

                int cols = Colunas();
                switch (e.KeyCode)
                {
                    case Keys.Left:
                        MoverFoco(-1);
                        break;
                    case Keys.Right:
                        MoverFoco(1);
                        break;
                    case Keys.Up:
                        MoverFoco(-cols);
                        break;
                    case Keys.Down:
                        MoverFoco(cols);
                        break;
                    case Keys.Home:
                        DefinirFoco(0);
                        break;
                    case Keys.End:
                        DefinirFoco(_caixas.Count - 1);
                        break;
                    case Keys.Enter:
                    case Keys.Space:
                        if (_foco >= 0 && _foco < _metricas.Count && _metricas[_foco].Editavel)
                            Editar(_foco);
                        break;
                    default:
                        return;
                }
                e.Handled = e.SuppressKeyPress = true;
            }

            private void MoverFoco(int passo)
            {
                DefinirFoco((_foco < 0 ? 0 : _foco) + passo);
            }

            private void DefinirFoco(int indice)
            {
                if (_caixas.Count == 0) { _foco = -1; return; }
                _foco = Math.Max(0, Math.Min(_caixas.Count - 1, indice));
                Invalidate();
            }

            // ----------------------------------------------------------
            // Edição
            // ----------------------------------------------------------

            private void Editar(int i)
            {
                Medir();
                if (i < 0 || i >= _caixas.Count) return;

                _aEditar = i;
                var r = _caixas[i];
                _editor.Bounds = new Rectangle(r.X + 8, r.Y + 15, Math.Min(r.Width - 14, 110), 20);
                _editor.Text = ValorCru(_metricas[i]);
                _editor.Visible = true;
                _editor.BringToFront();
                _editor.Focus();
                _editor.SelectAll();
            }

            private void Cancelar()
            {
                _aEditar = -1;
                _editor.Visible = false;
            }

            /// <summary>
            /// Devolve o foco ao mosaico depois de Enter/Escape no editor —
            /// só quando é o PRÓPRIO utilizador a terminar a edição por
            /// teclado. Um Tab ou um clique fora do editor dispara o mesmo
            /// <see cref="Confirmar"/> por <c>_editor.Leave</c>, mas aí o
            /// destino do foco já é a escolha do utilizador; roubar-lho de
            /// volta para o mosaico prendia quem tentasse sair por Tab.
            /// </summary>
            private void VoltarAoMosaico()
            {
                if (CanFocus) { try { Focus(); } catch { } }
            }

            private void Confirmar()
            {
                if (_aEditar < 0 || !_editor.Visible) return;

                int i = _aEditar;
                string escrito = (_editor.Text ?? "").Trim();
                Cancelar();

                var h = Editado;
                if (h == null) return;
                h(this, new PropriedadeEditadaEventArgs
                {
                    No = No,
                    Propriedade = _metricas[i],
                    Valor = escrito,
                    Handles = Handles
                });
            }

            /// <summary>
            /// O valor sem a unidade, para se editar o número e não o texto.
            /// "2,80 m" abre como "2,80".
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
        }

        /// <summary>
        /// Grelha de campos de duas colunas — rótulo e caixa — que é a forma
        /// da CONFIGURAÇÃO em todas as abas.
        /// </summary>
        public static TableLayoutPanel GrelhaDeCampos()
        {
            var t = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                BackColor = PaletteTheme.Fundo,
                Margin = new Padding(0)
            };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        /// <summary>
        /// Acrescenta uma linha "rótulo + campo" à grelha, com o terceiro
        /// lugar opcional para um botão (a cor do piso, por exemplo).
        /// </summary>
        public static void Campo(TableLayoutPanel grelha, string rotulo,
                                 Control campo, Control extra = null)
        {
            int linha = grelha.RowCount++;
            grelha.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lbl = PaletteTheme.Rotulo(rotulo);
            lbl.AccessibleName = rotulo;
            if (campo != null)
            {
                // Só herda o nome do rótulo quando o campo ainda não tem um
                // seu. Uma linha sem rótulo — o caso da checkbox solta, que
                // já traz o próprio texto como nome acessível — apagava esse
                // nome com uma cadeia vazia, e uma cadeia vazia explícita lê-se
                // pelo leitor de ecrã como "sem nome", não como "usa o texto".
                if (string.IsNullOrEmpty(campo.AccessibleName))
                    campo.AccessibleName = rotulo.TrimEnd(':', ' ');
                campo.Margin = new Padding(0, 1, 0, 1);
            }

            grelha.Controls.Add(lbl, 0, linha);
            grelha.Controls.Add(campo ?? new Label(), 1, linha);
            grelha.Controls.Add(extra ?? new Label { Width = 0, Height = 0 }, 2, linha);
        }

        /// <summary>Uma linha de nota por baixo de um campo, sem rótulo à esquerda.</summary>
        public static void NotaDeCampo(TableLayoutPanel grelha, Label nota)
        {
            int linha = grelha.RowCount++;
            grelha.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            grelha.Controls.Add(new Label { Width = 0, Height = 0 }, 0, linha);
            grelha.Controls.Add(nota, 1, linha);
            grelha.Controls.Add(new Label { Width = 0, Height = 0 }, 2, linha);
        }

        /// <summary>
        /// Faixa de aviso, com texto e não só cor.
        ///
        /// A cor sozinha não serve: em alto contraste desaparece, e há quem não
        /// a distinga. O símbolo e a frase é que carregam a informação.
        /// </summary>
        public sealed class Aviso : Panel
        {
            private readonly Label _texto;

            public Aviso()
            {
                Dock = DockStyle.Top;
                Height = 0;
                Visible = false;
                BackColor = PaletteTheme.FundoAviso;
                Padding = new Padding(PaletteTheme.Margem, 3, PaletteTheme.Margem, 3);

                _texto = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = PaletteTheme.Pequeno,
                    ForeColor = PaletteTheme.TextoAviso,
                    AutoEllipsis = true
                };
                Controls.Add(_texto);
            }

            public void Mostrar(string texto)
            {
                if (string.IsNullOrEmpty(texto)) { Esconder(); return; }
                _texto.Text = "⚠  " + texto;
                _texto.AccessibleName = "Aviso";
                _texto.AccessibleDescription = texto;
                Height = 24;
                Visible = true;
            }

            public void Esconder()
            {
                Visible = false;
                Height = 0;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pincel = new SolidBrush(PaletteTheme.BordaAviso))
                    e.Graphics.FillRectangle(pincel, 0, 0, 3, Height);
            }
        }
    }
}
