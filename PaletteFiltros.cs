using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// O painel de filtros dos resultados.
    ///
    /// TRABALHA SOBRE UM RASCUNHO, e não sobre o filtro em vigor. Mexer em
    /// cinco listas com a árvore a refazer-se a cada clique é insuportável num
    /// painel estreito — e pior do que insuportável: a vista muda por baixo de
    /// quem ainda está a escolher, e a escolha seguinte já é feita sobre outra
    /// lista. Aqui escolhe-se tudo, carrega-se em `Aplicar`, e só então a
    /// árvore muda uma vez.
    ///
    /// O `Repor` desfaz o rascunho — devolve-o ao filtro que está em vigor —
    /// e NÃO limpa nada. Quem quer limpar tem o `Limpar` na barra, que apaga
    /// pesquisa e filtros de uma vez. São duas operações diferentes e é por
    /// isso que têm nomes diferentes.
    ///
    /// Os valores oferecidos saem da árvore (ver `ResultadosArvore.Pavimentos`
    /// e companhia): um filtro que ofereça o que não existe só produz vistas
    /// vazias.
    /// </summary>
    public sealed class FiltrosPopup : ToolStripDropDown
    {
        private readonly FiltroResultados _rascunho;
        private readonly FiltroResultados _aplicado;
        private readonly Action<FiltroResultados> _aoAplicar;

        private readonly CheckedListBox _pavimentos;
        private readonly CheckedListBox _servicos;
        private readonly CheckedListBox _artigos;
        private readonly CheckedListBox _unidades;
        private readonly CheckBox _porClassificar;
        private readonly CheckBox _comVaos;
        private readonly CheckBox _comAlerta;

        public FiltrosPopup(NoResultado raiz, FiltroResultados aplicado,
                            Action<FiltroResultados> aoAplicar)
        {
            _aplicado = aplicado ?? new FiltroResultados();
            _rascunho = _aplicado.Clonar();
            _aoAplicar = aoAplicar;

            AutoSize = false;
            Padding = Padding.Empty;
            DropShadowEnabled = true;
            BackColor = PaletteTheme.Fundo;

            var corpo = new Panel
            {
                Width = 288,
                BackColor = PaletteTheme.Fundo,
                Padding = new Padding(0)
            };

            var rodape = Rodape();
            var conteudo = new Panel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 6, 8, 6),
                BackColor = PaletteTheme.Fundo
            };

            var grelha = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = PaletteTheme.Fundo
            };

            _pavimentos = Lista(grelha, "Pavimento",
                ResultadosArvore.Pavimentos(raiz), _rascunho.Pavimentos);
            _servicos = Lista(grelha, "Serviço",
                ResultadosArvore.Servicos(raiz), _rascunho.Servicos);
            _artigos = Lista(grelha, "Artigo",
                ResultadosArvore.Artigos(raiz), _rascunho.Artigos);
            _unidades = Lista(grelha, "Tipo / unidade",
                Escritas(ResultadosArvore.UnidadesPresentes(raiz)), _rascunho.UnidadesFiltradas);

            // Estado: três valores fixos, mas só se oferecem os que existem
            // mesmo. Um "Com alerta" num desenho sem alertas nenhuns é um
            // clique que não pode dar resultado.
            var presentes = ResultadosArvore.EstadosPresentes(raiz);
            grelha.Controls.Add(Titulo("Estado"));

            var estados = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(0, 0, 0, 6),
                WrapContents = false
            };
            _porClassificar = Estado(estados, "Por classificar",
                (presentes & EstadoResultado.PorClassificar) != 0,
                (_rascunho.Estados & EstadoResultado.PorClassificar) != 0);
            _comVaos = Estado(estados, "Com vãos",
                (presentes & EstadoResultado.ComVaos) != 0,
                (_rascunho.Estados & EstadoResultado.ComVaos) != 0);
            _comAlerta = Estado(estados, "Com alerta",
                (presentes & EstadoResultado.ComAlerta) != 0,
                (_rascunho.Estados & EstadoResultado.ComAlerta) != 0);
            grelha.Controls.Add(estados);

            conteudo.Controls.Add(grelha);

            corpo.Controls.Add(conteudo);
            corpo.Controls.Add(rodape);
            corpo.Controls.Add(Cabecalho());
            corpo.Height = conteudo.PreferredSize.Height + rodape.Height +
                           PaletteTheme.AlturaTituloSeccao + 4;

            Items.Add(new ToolStripControlHost(corpo)
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                AutoSize = false,
                Size = corpo.Size
            });
            Size = new Size(corpo.Width + 2, corpo.Height + 2);
        }

        // ------------------------------------------------------------------
        // Peças
        // ------------------------------------------------------------------

        private Panel Cabecalho()
        {
            var p = new Panel
            {
                Dock = DockStyle.Top,
                Height = PaletteTheme.AlturaTituloSeccao,
                BackColor = PaletteTheme.FundoSeccao
            };
            p.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                Text = "FILTROS",
                Font = PaletteTheme.TituloSeccao,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            });

            var fechar = new Button
            {
                Dock = DockStyle.Right,
                Width = 26,
                Text = "✕",
                FlatStyle = FlatStyle.Flat,
                Font = PaletteTheme.Pequeno,
                BackColor = PaletteTheme.FundoSeccao,
                AccessibleName = "Fechar filtros"
            };
            fechar.FlatAppearance.BorderSize = 0;
            fechar.Click += (s, e) => Close();
            p.Controls.Add(fechar);
            return p;
        }

        private Panel Rodape()
        {
            var p = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = PaletteTheme.FundoSeccao,
                Padding = new Padding(8, 5, 8, 5)
            };

            var aplicar = new Button
            {
                Dock = DockStyle.Right,
                Width = 82,
                Text = "Aplicar",
                FlatStyle = FlatStyle.Flat,
                BackColor = PaletteTheme.Acento,
                ForeColor = PaletteTheme.AltoContraste
                    ? SystemColors.HighlightText : Color.White,
                Font = PaletteTheme.Negrito,
                AccessibleName = "Aplicar filtros"
            };
            aplicar.FlatAppearance.BorderColor = PaletteTheme.AcentoEscuro;
            aplicar.Click += (s, e) =>
            {
                RecolherRascunho();
                if (_aoAplicar != null) _aoAplicar(_rascunho);
                Close();
            };

            // Repor devolve o rascunho ao que está EM VIGOR. Não limpa: quem
            // quer limpar tem o «Limpar» da barra, que também apaga a pesquisa.
            var repor = new Button
            {
                Dock = DockStyle.Right,
                Width = 74,
                Text = "Repor",
                FlatStyle = FlatStyle.Flat,
                BackColor = PaletteTheme.Fundo,
                Font = PaletteTheme.Normal,
                Margin = new Padding(0, 0, 6, 0),
                AccessibleName = "Repor",
                AccessibleDescription =
                    "Devolve as escolhas ao filtro que está em vigor. Não limpa."
            };
            repor.Click += (s, e) => ReporRascunho();

            p.Controls.Add(aplicar);
            p.Controls.Add(new Label { Dock = DockStyle.Right, Width = 6 });
            p.Controls.Add(repor);
            return p;
        }

        private static Label Titulo(string texto)
        {
            return new Label
            {
                Dock = DockStyle.Top,
                Text = texto,
                AutoSize = false,
                Height = 16,
                Font = PaletteTheme.PequenoNegrito,
                ForeColor = PaletteTheme.Apagado,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 2, 0, 0)
            };
        }

        /// <summary>
        /// Uma lista de valores com caixa de visto.
        ///
        /// Caixas de visto e não uma combo: o modelo guarda um CONJUNTO por
        /// filtro, e escolher dois pisos de três é o caso normal numa obra.
        /// Com uma combo só se podia escolher um, e a alternativa — "Todos" —
        /// não é a mesma coisa.
        ///
        /// Sem valores, a lista não aparece de todo: uma caixa vazia com um
        /// rótulo por cima é um filtro que promete o que não tem.
        /// </summary>
        private static CheckedListBox Lista(TableLayoutPanel destino, string rotulo,
                                            List<string> valores, HashSet<string> escolhidos)
        {
            if (valores == null || valores.Count == 0) return null;

            destino.Controls.Add(Titulo(rotulo));

            var lista = new CheckedListBox
            {
                Dock = DockStyle.Top,
                Height = Math.Min(valores.Count, 4) * 17 + 4,
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = PaletteTheme.Normal,
                IntegralHeight = false,
                BackColor = PaletteTheme.Fundo,
                AccessibleName = rotulo,
                Margin = new Padding(0, 0, 0, 6)
            };
            foreach (var v in valores)
                lista.Items.Add(v, escolhidos.Contains(v));

            destino.Controls.Add(lista);
            return lista;
        }

        private static CheckBox Estado(Control destino, string texto,
                                       bool existe, bool marcado)
        {
            var c = new CheckBox
            {
                Text = texto,
                AutoSize = true,
                Checked = marcado && existe,
                Enabled = existe,
                Font = PaletteTheme.Normal,
                AccessibleName = texto,
                Margin = new Padding(0, 1, 0, 1)
            };
            // Desactivado e não escondido: assim vê-se que o estado existe como
            // conceito e que este desenho é que não tem nenhum caso.
            if (!existe)
                c.AccessibleDescription = "Não há medições neste estado neste desenho.";
            destino.Controls.Add(c);
            return c;
        }

        /// <summary>As unidades na forma que se lê: "m2" -> "m²".</summary>
        private static List<string> Escritas(List<string> unidades)
        {
            var saida = new List<string>();
            foreach (var u in unidades) saida.Add(Unidades.Escrita(u));
            return saida;
        }

        // ------------------------------------------------------------------
        // Rascunho
        // ------------------------------------------------------------------

        private void RecolherRascunho()
        {
            Recolher(_pavimentos, _rascunho.Pavimentos);
            Recolher(_servicos, _rascunho.Servicos);
            Recolher(_artigos, _rascunho.Artigos);

            // As unidades vão para o modelo na forma simples — "m2" —, que é a
            // que ele compara; ao ecrã mostrou-se "m²".
            _rascunho.UnidadesFiltradas.Clear();
            if (_unidades != null)
                foreach (var item in _unidades.CheckedItems)
                    _rascunho.UnidadesFiltradas.Add(Simples(item.ToString()));

            EstadoResultado estados = EstadoResultado.Nenhum;
            if (_porClassificar.Checked) estados |= EstadoResultado.PorClassificar;
            if (_comVaos.Checked) estados |= EstadoResultado.ComVaos;
            if (_comAlerta.Checked) estados |= EstadoResultado.ComAlerta;
            _rascunho.Estados = estados;
        }

        private static void Recolher(CheckedListBox lista, HashSet<string> destino)
        {
            destino.Clear();
            if (lista == null) return;
            foreach (var item in lista.CheckedItems) destino.Add(item.ToString());
        }

        private static string Simples(string escrita)
        {
            if (escrita == "m²") return Unidades.M2;
            if (escrita == "m³") return Unidades.M3;
            if (escrita == "un.") return Unidades.Unidade;
            return escrita;
        }

        private void ReporRascunho()
        {
            Marcar(_pavimentos, _aplicado.Pavimentos);
            Marcar(_servicos, _aplicado.Servicos);
            Marcar(_artigos, _aplicado.Artigos);

            if (_unidades != null)
                for (int i = 0; i < _unidades.Items.Count; i++)
                    _unidades.SetItemChecked(i,
                        _aplicado.UnidadesFiltradas.Contains(
                            Simples(_unidades.Items[i].ToString())));

            _porClassificar.Checked =
                (_aplicado.Estados & EstadoResultado.PorClassificar) != 0;
            _comVaos.Checked = (_aplicado.Estados & EstadoResultado.ComVaos) != 0;
            _comAlerta.Checked = (_aplicado.Estados & EstadoResultado.ComAlerta) != 0;
        }

        private static void Marcar(CheckedListBox lista, HashSet<string> escolhidos)
        {
            if (lista == null) return;
            for (int i = 0; i < lista.Items.Count; i++)
                lista.SetItemChecked(i, escolhidos.Contains(lista.Items[i].ToString()));
        }

        /// <summary>
        /// O Escape fecha e devolve o foco a quem abriu — que é o que o
        /// ToolStripDropDown já faz, mas só se a tecla lhe chegar. Dentro de
        /// uma CheckedListBox não chega.
        /// </summary>
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(ToolStripDropDownCloseReason.Keyboard); return true; }
            return base.ProcessDialogKey(keyData);
        }
    }
}
