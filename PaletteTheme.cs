using System;
using System.Drawing;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// Cores, tipos de letra e medidas da paleta, num sítio só.
    ///
    /// Estavam escritas à mão em cada controlo — o mesmo azul em quatro
    /// ficheiros, cada um com o seu valor ligeiramente diferente, e o realce
    /// de "isto edita-se" em três tons de amarelo consoante quem o escreveu.
    /// Mudar o aspecto da paleta era procurar Color.FromArgb por todo o lado e
    /// esperar não ter falhado nenhum.
    ///
    /// O ALTO CONTRASTE NÃO É UMA VARIANTE DE ESTILO. Quando o Windows está em
    /// alto contraste, uma paleta com cores próprias fica ilegível — texto
    /// escuro sobre o fundo escuro do sistema — e não há nada que o utilizador
    /// possa fazer. Por isso todas as cores passam por aqui e, nesse modo,
    /// devolvem as do sistema. É a razão de isto ser um método e não um campo:
    /// o modo pode mudar com a paleta aberta.
    /// </summary>
    public static class PaletteTheme
    {
        // ------------------------------------------------------------------
        // Clara — a variante aprovada (estilo Eberick/Office)
        // (Deploy/MockupPalette/index-resultados-compacto.html,
        // preview-eberick.png; troca de volta em 2026-09-19, substituindo o
        // grafite de index-premium.html aprovado a 2026-09-05.)
        //
        // Um painel claro lê-se como as ferramentas de referência do setor
        // (Eberick, Office): fundo branco, texto quase preto, e um azul de
        // acento reservado para o que é accionável — selecção, ligações,
        // ícones activos. Os neutros têm o mesmo desvio ligeiro para o azul
        // do acento que tinham no grafite: um cinzento puro lê-se como não
        // escolhido.
        // ------------------------------------------------------------------
        private static readonly Color _acento = Color.FromArgb(0x16, 0x8A, 0xC4);
        private static readonly Color _acentoEscuro = Color.FromArgb(0x11, 0x6F, 0x9D);
        /// <summary>Realce de passagem do rato e de foco: um degrau acima do fundo.</summary>
        private static readonly Color _palido = Color.FromArgb(0xEA, 0xF7, 0xFD);
        private static readonly Color _azulTopo = Color.FromArgb(0xBF, 0xE7, 0xFB);
        private static readonly Color _tinta = Color.FromArgb(0x11, 0x18, 0x20);
        private static readonly Color _apagado = Color.FromArgb(0x5B, 0x68, 0x72);
        private static readonly Color _linha = Color.FromArgb(0xCB, 0xD7, 0xDD);
        private static readonly Color _linhaSuave = Color.FromArgb(0xE5, 0xEC, 0xEF);
        private static readonly Color _guia = Color.FromArgb(0xD8, 0xE2, 0xE6);
        private static readonly Color _branco = Color.FromArgb(0xFF, 0xFF, 0xFF);
        private static readonly Color _verde = Color.FromArgb(0x25, 0x8B, 0x62);
        private static readonly Color _ocre = Color.FromArgb(0xB1, 0x7D, 0x05);
        private static readonly Color _parede = Color.FromArgb(0x65, 0x7F, 0x8E);
        private static readonly Color _perigo = Color.FromArgb(0xB9, 0x4A, 0x43);
        private static readonly Color _fundoSeccao = Color.FromArgb(0xF3, 0xF7, 0xF8);
        private static readonly Color _fundoBarra = Color.FromArgb(0xF6, 0xFB, 0xFD);
        private static readonly Color _fundoGrupo = Color.FromArgb(0xFA, 0xFC, 0xFD);
        private static readonly Color _fundoCabecalhoArvore = Color.FromArgb(0xEE, 0xF4, 0xF6);
        private static readonly Color _seleccionado = Color.FromArgb(0xD9, 0xF0, 0xFC);
        private static readonly Color _contornoSeleccao = Color.FromArgb(0x82, 0xC9, 0xE7);
        /// <summary>Creme âmbar em fundo claro — não o tom abafado da variante grafite.</summary>
        private static readonly Color _fundoPorClassificar = Color.FromArgb(0xFF, 0xFB, 0xEB);
        private static readonly Color _textoPorClassificar = Color.FromArgb(0x7C, 0x5D, 0x05);
        private static readonly Color _fundoAviso = Color.FromArgb(0xFF, 0xF8, 0xDF);
        private static readonly Color _bordaAviso = Color.FromArgb(0xF4, 0xB7, 0x2B);
        private static readonly Color _textoAviso = Color.FromArgb(0x76, 0x57, 0x00);
        /// <summary>
        /// O realce de "isto edita-se": um amarelo pálido, que em fundo claro
        /// se lê sem gritar nem se confundir com o azul da selecção.
        /// </summary>
        private static readonly Color _fundoEditavel = Color.FromArgb(0xFF, 0xF3, 0xC4);
        private static readonly Color _bordaCampo = Color.FromArgb(0xA9, 0xB9, 0xC1);
        private static readonly Color _sombraCartao = Color.FromArgb(0xC7, 0xD2, 0xD8);
        private static readonly Color _realce = Color.FromArgb(0xFF, 0xFF, 0xFF);
        /// <summary>Fundo dos campos: branco, com a borda a marcar o encaixe.</summary>
        private static readonly Color _fundoCampo = Color.FromArgb(0xFF, 0xFF, 0xFF);

        /// <summary>
        /// O Windows está em alto contraste? Lido a cada chamada de propósito:
        /// o utilizador pode ligá-lo com a paleta já aberta.
        /// </summary>
        public static bool AltoContraste
        {
            get
            {
                try { return SystemInformation.HighContrast; }
                catch { return false; }
            }
        }

        private static Color C(Color propria, Color doSistema)
        {
            return AltoContraste ? doSistema : propria;
        }

        // ------------------------------------------------------------------
        // Cores
        // ------------------------------------------------------------------
        public static Color Acento { get { return C(_acento, SystemColors.Highlight); } }
        public static Color AcentoEscuro { get { return C(_acentoEscuro, SystemColors.Highlight); } }
        public static Color Palido { get { return C(_palido, SystemColors.Control); } }
        public static Color AzulTopo { get { return C(_azulTopo, SystemColors.Control); } }
        public static Color Tinta { get { return C(_tinta, SystemColors.ControlText); } }
        public static Color Apagado { get { return C(_apagado, SystemColors.GrayText); } }
        public static Color Linha { get { return C(_linha, SystemColors.ControlDark); } }
        public static Color LinhaSuave { get { return C(_linhaSuave, SystemColors.ControlDark); } }
        public static Color Guia { get { return C(_guia, SystemColors.ControlDark); } }
        public static Color Fundo { get { return C(_branco, SystemColors.Window); } }
        public static Color Verde { get { return C(_verde, SystemColors.ControlText); } }
        public static Color Ocre { get { return C(_ocre, SystemColors.ControlText); } }
        public static Color CorParede { get { return C(_parede, SystemColors.ControlText); } }
        public static Color Perigo { get { return C(_perigo, SystemColors.ControlText); } }
        public static Color FundoSeccao { get { return C(_fundoSeccao, SystemColors.Control); } }
        public static Color FundoBarra { get { return C(_fundoBarra, SystemColors.Control); } }
        public static Color FundoGrupo { get { return C(_fundoGrupo, SystemColors.Window); } }
        public static Color FundoCabecalhoArvore
        {
            get { return C(_fundoCabecalhoArvore, SystemColors.Control); }
        }
        public static Color Seleccionado { get { return C(_seleccionado, SystemColors.Highlight); } }
        public static Color TextoSeleccionado
        {
            get { return AltoContraste ? SystemColors.HighlightText : Tinta; }
        }
        public static Color ContornoSeleccao
        {
            get { return C(_contornoSeleccao, SystemColors.HighlightText); }
        }
        public static Color FundoPorClassificar
        {
            get { return C(_fundoPorClassificar, SystemColors.Window); }
        }
        public static Color TextoPorClassificar
        {
            get { return C(_textoPorClassificar, SystemColors.ControlText); }
        }
        public static Color FundoAviso { get { return C(_fundoAviso, SystemColors.Info); } }
        public static Color BordaAviso { get { return C(_bordaAviso, SystemColors.InfoText); } }
        public static Color TextoAviso { get { return C(_textoAviso, SystemColors.InfoText); } }
        public static Color FundoEditavel { get { return C(_fundoEditavel, SystemColors.Window); } }
        public static Color BordaCampo { get { return C(_bordaCampo, SystemColors.ControlDark); } }
        public static Color SombraCartao { get { return C(_sombraCartao, SystemColors.ControlDark); } }
        public static Color Realce { get { return C(_realce, SystemColors.Window); } }

        /// <summary>
        /// Fundo de uma caixa de texto ou lista: um degrau ABAIXO do painel.
        ///
        /// Em fundo escuro, um campo mais claro do que o painel lê-se como um
        /// realce; mais escuro, lê-se como um encaixe — que é o que um campo é.
        /// </summary>
        public static Color FundoCampo { get { return C(_fundoCampo, SystemColors.Window); } }

        /// <summary>
        /// Vermelho das deduções — o mesmo tom do aviso "excedente" do mockup
        /// claro (--danger), legível sobre fundo branco sem precisar do rosa
        /// que a variante grafite usava para não desaparecer sobre escuro.
        /// </summary>
        public static Color VermelhoDeducao
        {
            get { return C(Color.FromArgb(0xB9, 0x4A, 0x43), SystemColors.ControlText); }
        }

        /// <summary>A cor do ícone de um tipo de nó da árvore.</summary>
        public static Color CorDoNo(TipoNo tipo)
        {
            if (AltoContraste) return SystemColors.ControlText;
            switch (tipo)
            {
                case TipoNo.Raiz:
                case TipoNo.Piso:
                case TipoNo.Tipo: return Color.FromArgb(0x52, 0x6E, 0x7D);
                case TipoNo.Artigo: return _verde;
                case TipoNo.Vao: return _ocre;
                case TipoNo.Titulo: return _acentoEscuro;
                default: return _parede;
            }
        }

        // ------------------------------------------------------------------
        // Medidas
        //
        // Em pixels lógicos. O WinForms escala-os pelo DPI quando o formulário
        // tem AutoScaleMode.Dpi/Font, que é o que a paleta usa.
        // ------------------------------------------------------------------
        public const int AlturaCabecalho = 42;
        public const int AlturaBarra = 30;
        public const int AlturaTituloSeccao = 26;
        public const int AlturaCampo = 24;
        public const int AlturaLinha = 22;
        /// <summary>
        /// Altura do mosaico de MEDIR. 56 lia grande de mais (utilizador,
        /// 2026-09-20); cortado para 28. Depois, com o ícone maior (ver
        /// LadoIconeAccao), 28 já não sobrava espaço nenhum para o rótulo —
        /// subido para 32 só o suficiente para não voltar a apertar o texto.
        /// </summary>
        public const int AlturaBotaoAccao = 32;
        public const int Margem = 8;
        public const int MargemPequena = 4;
        /// <summary>Recuo por nível de profundidade, na coluna da árvore.</summary>
        public const int RecuoPorNivel = 15;
        /// <summary>Largura da zona do [+]/[−] de expandir e recolher.</summary>
        public const int LarguraTwisty = 14;
        public const int LarguraIconeNo = 14;
        /// <summary>Largura mínima do painel antes de o conteúdo deixar de caber.</summary>
        public const int LarguraMinima = 480;

        // ------------------------------------------------------------------
        // Tipos de letra
        //
        // Criados uma vez. Uma Font é um recurso do GDI: criá-las por linha, a
        // cada actualização, alocava e deitava fora centenas por medição.
        // ------------------------------------------------------------------
        private static Font _base, _negrito, _italico, _pequeno, _pequenoNegrito, _seccao;

        private static Font Criar(ref Font campo, float tamanho, FontStyle estilo)
        {
            if (campo == null)
            {
                try { campo = new Font("Segoe UI", tamanho, estilo); }
                catch { campo = new Font(SystemFonts.DefaultFont, estilo); }
            }
            return campo;
        }

        public static Font Normal { get { return Criar(ref _base, 8.5f, FontStyle.Regular); } }
        public static Font Negrito { get { return Criar(ref _negrito, 8.5f, FontStyle.Bold); } }
        public static Font Italico { get { return Criar(ref _italico, 8.5f, FontStyle.Italic); } }
        public static Font Pequeno { get { return Criar(ref _pequeno, 7.5f, FontStyle.Regular); } }
        public static Font PequenoNegrito
        {
            get { return Criar(ref _pequenoNegrito, 7.5f, FontStyle.Bold); }
        }
        /// <summary>Título de secção: "CONFIGURAÇÃO", "MEDIR", "RESULTADOS".</summary>
        public static Font TituloSeccao { get { return Criar(ref _seccao, 8f, FontStyle.Bold); } }

        private static Font _numero;

        /// <summary>
        /// Os números, em monoespaçado.
        ///
        /// Uma medição lê-se em coluna: doze linhas de área líquida, uma por
        /// baixo da outra. Com letra proporcional, o "1" ocupa metade do "8" e
        /// os algarismos não alinham entre linhas — obriga a ler número a
        /// número em vez de correr a coluna com os olhos, que é como se
        /// confere. Em monoespaçado alinham, e a coluna lê-se de uma vez.
        /// </summary>
        public static Font Numero
        {
            get
            {
                if (_numero == null)
                {
                    // Consolas está em todo o Windows desde o Vista; o Courier
                    // New é o último recurso e existe desde sempre.
                    try { _numero = new Font("Consolas", 9f, FontStyle.Regular); }
                    catch
                    {
                        try { _numero = new Font("Courier New", 9f, FontStyle.Regular); }
                        catch { _numero = Normal; }
                    }
                }
                return _numero;
            }
        }

        // ------------------------------------------------------------------
        // Fábricas
        // ------------------------------------------------------------------

        /// <summary>Rótulo de campo, alinhado com a caixa que descreve.</summary>
        public static Label Rotulo(string texto)
        {
            return new Label
            {
                Text = texto,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Apagado,
                Font = Normal,
                Margin = new Padding(0, 2, 4, 2)
            };
        }

        /// <summary>Texto secundário: a layer efectiva, o resumo, a contagem.</summary>
        public static Label Nota(string texto)
        {
            return new Label
            {
                Text = texto,
                Dock = DockStyle.Fill,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Apagado,
                Font = Pequeno
            };
        }

        /// <summary>
        /// Aplica a moldura de foco visível a um controlo.
        ///
        /// O WinForms desenha foco em botões mas não em caixas de texto nem em
        /// combos com tema — e sem ele, quem navega por teclado não sabe onde
        /// está. Pinta-se a borda com o acento enquanto o controlo tem foco.
        /// </summary>
        /// <summary>Cria um painel visualmente elevado sem depender de WPF.</summary>
        public static Panel Cartao(Control conteudo, int margem = 8)
        {
            var cartao = new Panel
            {
                Dock = DockStyle.Top,
                BackColor = Fundo,
                Padding = new Padding(margem),
                Margin = new Padding(0, 0, 0, 6)
            };
            if (conteudo != null) cartao.Controls.Add(conteudo);
            cartao.Paint += (s, e) =>
            {
                using (var caneta = new Pen(SombraCartao))
                    e.Graphics.DrawRectangle(caneta, 0, 0, cartao.Width - 1, cartao.Height - 1);
            };
            return cartao;
        }

        public static void ComFoco(Control c)
        {
            if (c == null) return;
            c.Enter += Controlo_Enter;
            c.Leave += Controlo_Leave;
            c.Paint += Controlo_Paint;
            c.Invalidate();
        }

        private static void Controlo_Enter(object sender, EventArgs e)
        {
            var c = sender as Control;
            if (c == null) return;
            c.AccessibleDescription = AcrescentarEstado(c.AccessibleDescription, "Com foco.");
            c.Invalidate();
        }

        private static void Controlo_Leave(object sender, EventArgs e)
        {
            var c = sender as Control;
            if (c == null) return;
            c.Invalidate();
        }

        private static void Controlo_Paint(object sender, PaintEventArgs e)
        {
            var c = sender as Control;
            if (c == null || !c.Focused || !c.TabStop || c.Width < 4 || c.Height < 4) return;
            using (var caneta = new Pen(Acento, 2f))
            {
                var r = new Rectangle(1, 1, c.Width - 3, c.Height - 3);
                e.Graphics.DrawRectangle(caneta, r);
            }
        }

        private static string AcrescentarEstado(string texto, string estado)
        {
            if (string.IsNullOrEmpty(texto)) return estado;
            return texto.EndsWith(estado, StringComparison.Ordinal) ? texto : texto + " " + estado;
        }

        /// <summary>Aplica foco visível e descrição a todos os descendentes interativos.</summary>
        /// <summary>
        /// Pinta um painel inteiro com o tema, descendo a todos os filhos.
        ///
        /// SEM ISTO O TEMA ESCURO NÃO EXISTE. Uma TextBox, uma ComboBox ou uma
        /// NumericUpDown nascem com as cores do sistema — branco — e ficariam
        /// como buracos claros num painel grafite. E são dezenas: pintá-las uma
        /// a uma no sítio onde são criadas é a garantia de esquecer metade.
        ///
        /// Só toca no que NÃO foi pintado à mão: quem já definiu uma cor
        /// própria — os botões de acção, as linhas da árvore — fica como está.
        /// </summary>
        /// <summary>
        /// Esta cor de texto seria ilegível sobre o fundo do painel?
        ///
        /// Compara luminosidades: se o texto e o fundo estiverem a menos de um
        /// terço de distância, não se lê. Serve para apanhar cores herdadas de
        /// fora — o AutoCAD põe as suas em controlos alojados — sem ter de
        /// enumerar todas as que o tema usa de propósito.
        /// </summary>
        private static bool TextoIlegivel(Color texto)
        {
            if (AltoContraste) return false;    // aí manda o sistema
            return Math.Abs(texto.GetBrightness() - Fundo.GetBrightness()) < 0.33f;
        }

        public static void AplicarTema(Control raiz)
        {
            if (raiz == null) return;

            foreach (Control c in raiz.Controls)
            {
                var caixa = c as TextBoxBase;
                if (caixa != null)
                {
                    caixa.BackColor = FundoCampo;
                    caixa.ForeColor = Tinta;
                    caixa.BorderStyle = BorderStyle.FixedSingle;
                    continue;
                }

                var combo = c as ComboBox;
                if (combo != null)
                {
                    // FlatStyle.Flat é o que faz a ComboBox respeitar as cores:
                    // com o estilo do sistema, o Windows pinta-a por cima.
                    combo.FlatStyle = FlatStyle.Flat;
                    combo.BackColor = FundoCampo;
                    combo.ForeColor = Tinta;
                    continue;
                }

                var numero = c as NumericUpDown;
                if (numero != null)
                {
                    numero.BackColor = FundoCampo;
                    numero.ForeColor = Tinta;
                    numero.BorderStyle = BorderStyle.FixedSingle;
                    continue;
                }

                var visto = c as CheckBox;
                if (visto != null)
                {
                    visto.ForeColor = Tinta;
                    visto.BackColor = Color.Transparent;
                    continue;
                }

                var rotulo = c as Label;
                if (rotulo != null)
                {
                    // TEXTO ESCURO EM FUNDO ESCURO É SEMPRE UM ERRO.
                    //
                    // A condição era "só se ainda for a cor do sistema", e isso
                    // deixava passar tudo o que herdasse uma cor escura de um
                    // controlo pai — que, dentro do AutoCAD, é o que acontece.
                    // Os rótulos da configuração ficaram cinzento-escuro sobre
                    // grafite e não se liam.
                    //
                    // Agora a regra é sobre o RESULTADO, não sobre a origem: no
                    // tema escuro todas as tintas próprias são claras, portanto
                    // qualquer cor escura que aqui chegue está errada, venha de
                    // onde vier.
                    if (TextoIlegivel(rotulo.ForeColor)) rotulo.ForeColor = Tinta;
                    rotulo.BackColor = Color.Transparent;
                    continue;
                }

                var barra = c as ToolStrip;
                if (barra != null)
                {
                    barra.BackColor = FundoBarra;
                    barra.ForeColor = Tinta;
                    foreach (ToolStripItem item in barra.Items) item.ForeColor = Tinta;
                    continue;
                }

                var grelha = c as DataGridView;
                if (grelha != null) continue;      // já é pintada por inteiro

                if (c is Button) { AplicarTema(c); continue; }   // pintados à mão

                // Painéis e contentores: fundo do painel, salvo quem já
                // escolheu o seu (as barras e os cabeçalhos de secção).
                if (c.BackColor == SystemColors.Control) c.BackColor = Fundo;
                if (c.ForeColor == SystemColors.ControlText) c.ForeColor = Tinta;
                AplicarTema(c);
            }
        }

        public static void PrepararInteraccao(Control raiz, ToolTip dicas = null)
        {
            if (raiz == null) return;
            int indice = 0;
            PrepararInteraccao(raiz, dicas, ref indice);
        }

        private static void PrepararInteraccao(Control controlo, ToolTip dicas, ref int indice)
        {
            foreach (Control filho in controlo.Controls)
            {
                if (filho is Label || filho is Panel || filho is TableLayoutPanel || filho is UserControl)
                {
                    PrepararInteraccao(filho, dicas, ref indice);
                    continue;
                }

                if (filho is DataGridView)
                {
                    filho.TabIndex = indice++;
                    filho.TabStop = true;
                    if (string.IsNullOrEmpty(filho.AccessibleName)) filho.AccessibleName = filho.Name;
                    ComFoco(filho);
                    continue;
                }

                if (filho is ToolStrip)
                {
                    filho.TabIndex = indice++;
                    filho.TabStop = true;
                    if (string.IsNullOrEmpty(filho.AccessibleName)) filho.AccessibleName = "Barra de ações";
                    ComFoco(filho);
                    continue;
                }

                if (filho is TextBoxBase || filho is ComboBox || filho is NumericUpDown ||
                    filho is CheckBox || filho is Button)
                {
                    filho.TabIndex = indice++;
                    filho.TabStop = true;
                    if (string.IsNullOrEmpty(filho.AccessibleName)) filho.AccessibleName = filho.Text;
                    if (string.IsNullOrEmpty(filho.AccessibleDescription))
                        filho.AccessibleDescription = filho.AccessibleName;
                    if (dicas != null && string.IsNullOrEmpty(dicas.GetToolTip(filho)))
                        dicas.SetToolTip(filho, filho.AccessibleName);
                    ComFoco(filho);
                }

                if (filho.Controls.Count > 0)
                    PrepararInteraccao(filho, dicas, ref indice);
            }
        }

        /// <summary>
        /// Botão de barra, com o clique protegido: um erro nunca abre caixa
        /// vermelha por cima do desenho — vai para a linha de comandos.
        /// </summary>
        public static ToolStripButton Botao(string texto, Image icone, EventHandler aoClicar,
                                            bool comTexto = true)
        {
            EventHandler seguro = (s, e) =>
            {
                try { if (aoClicar != null) aoClicar(s, e); }
                catch (Exception ex) { PaletteHost.Log(texto + ": " + ex.Message); }
            };

            return new ToolStripButton(texto, icone, seguro)
            {
                TextImageRelation = TextImageRelation.ImageAboveText,
                DisplayStyle = comTexto
                    ? ToolStripItemDisplayStyle.ImageAndText
                    : ToolStripItemDisplayStyle.Image,
                AutoSize = true,
                Padding = new Padding(2, 1, 2, 1),
                Margin = new Padding(1, 0, 1, 0),
                ToolTipText = texto,
                AccessibleName = texto
            };
        }

        /// <summary>
        /// O texto cinzento que uma caixa mostra enquanto está vazia
        /// ("Pesquisar…").
        ///
        /// O <c>TextBox.PlaceholderText</c> só existe a partir do .NET Core
        /// 3.0, e a DLL do AutoCAD 2021 é net48 — usá-lo parte a compilação
        /// para metade dos AutoCAD suportados. O equivalente do Framework é a
        /// cue banner nativa da caixa de edição do Windows, que é o mesmo que
        /// o WinForms moderno faz por baixo.
        ///
        /// Aplica-se quando a janela existir: antes de o handle ser criado a
        /// mensagem não tem para onde ir e perder-se-ia em silêncio.
        /// </summary>
        public static void TextoDeSugestao(TextBox caixa, string texto)
        {
            if (caixa == null) return;
            caixa.AccessibleName = texto;

            EventHandler aplicar = null;
            aplicar = (s, e) =>
            {
                try { SendMessage(caixa.Handle, EmSetCueBanner, (IntPtr)1, texto); }
                catch { /* cosmético: sem cue banner a caixa funciona na mesma */ }
            };

            if (caixa.IsHandleCreated) aplicar(caixa, EventArgs.Empty);
            caixa.HandleCreated += aplicar;
        }

        private const int EmSetCueBanner = 0x1501;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet =
            System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        /// <summary>
        /// O ícone reduzido ao tamanho pedido.
        ///
        /// O IconFactory desenha tudo a 64×64, e um <see cref="Button"/> pinta
        /// a imagem no TAMANHO NATIVO — não tem nada como o
        /// <c>ImageScalingSize</c> da ToolStrip. Um ícone de 64 px numa célula
        /// de 44 transbordava por cima do texto e saía cortado em baixo, que é
        /// exactamente o que se via na barra MEDIR.
        ///
        /// Com interpolação de alta qualidade: reduzir 64 para 18 sem ela dá um
        /// traço serrilhado, e num painel denso nota-se mais do que num botão
        /// grande.
        /// </summary>
        public static Image Icone(Image origem, int lado)
        {
            if (origem == null) return null;
            if (origem.Width == lado && origem.Height == lado) return origem;

            try
            {
                var destino = new Bitmap(lado, lado,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(destino))
                {
                    g.InterpolationMode =
                        System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.PixelOffsetMode =
                        System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.CompositingQuality =
                        System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    g.DrawImage(origem, new Rectangle(0, 0, lado, lado));
                }
                return destino;
            }
            catch { return origem; }
        }

        /// <summary>
        /// Lado do ícone nos botões de acção da barra MEDIR. Baixou para 10
        /// junto com o corte do mosaico para 28 px (2026-09-20); depois de o
        /// bloco ficar do tamanho certo, o utilizador pediu o ícone maior
        /// duas vezes seguidas — 13, depois 17, este já com
        /// AlturaBotaoAccao subida para 32 para o rótulo continuar a caber.
        /// </summary>
        public const int LadoIconeAccao = 17;

        /// <summary>Barra de ferramentas com o aspecto da paleta.</summary>
        public static ToolStrip Barra()
        {
            return new ToolStrip
            {
                Dock = DockStyle.Top,
                GripStyle = ToolStripGripStyle.Hidden,
                ImageScalingSize = new Size(20, 20),
                Padding = new Padding(4, 2, 4, 2),
                ShowItemToolTips = true,
                RenderMode = ToolStripRenderMode.System,
                BackColor = FundoBarra,
                CanOverflow = true,
                LayoutStyle = ToolStripLayoutStyle.Flow,
                AutoSize = true
            };
        }
    }
}
