using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TSKTakeOff
{
    /// <summary>
    /// A árvore de resultados: quem constrói a hierarquia, quem a achata em
    /// linhas e quem soma os totais.
    ///
    /// Vive fora da paleta de propósito. O WinForms não tem árvore com colunas
    /// — o TreeView não tem colunas e o DataGridView não tem hierarquia —, por
    /// isso a grelha vai continuar a ser uma lista achatada que se recalcula
    /// quando alguém expande ou recolhe. Se essa lista for calculada DENTRO do
    /// controlo, a única forma de verificar um total é abrir o AutoCAD e olhar
    /// para ele. Aqui não toca no AutoCAD nem no WinForms, e está sob teste.
    ///
    /// Três responsabilidades, por esta ordem:
    ///
    ///   1. <see cref="DeParedes"/>  — projecção do modelo de alvenaria para a
    ///      forma neutra <see cref="MedicaoResultado"/>;
    ///   2. <see cref="Construir"/>  — a hierarquia Pavimentos > Piso >
    ///      Serviço > Artigo > Medição > Vão/Título, com os totais completos;
    ///   3. <see cref="Projetar"/>   — pesquisa, filtros, recolhas e os totais
    ///      do que está à vista.
    ///
    /// A ordem visual que daqui sai NÃO manda na folha do Excel. Essa continua
    /// a ser decidida pelo FolhaMedicao, e é por isso que estas duas coisas
    /// estão em ficheiros diferentes.
    /// </summary>
    public static class ResultadosArvore
    {
        public const string RotuloRaiz = "Pavimentos";
        public const string SemPiso = "Sem piso";
        public const string SemServico = "Sem serviço";
        /// <summary>Medições sem tipo de medida declarado — não devia acontecer.</summary>
        public const string SemTipo = "Sem tipo";
        public const string PorClassificar = "Por classificar";

        // ------------------------------------------------------------------
        // 1. Projecção da alvenaria
        // ------------------------------------------------------------------

        /// <summary>
        /// Traduz paredes e vãos para a forma neutra de que a árvore vive.
        ///
        /// O <paramref name="artigoConhecido"/> é como o mapa de quantidades
        /// entra aqui sem o ficheiro ter de o conhecer: nulo quer dizer que não
        /// há mapa importado, e sem mapa não ter artigo é o normal — não é um
        /// alerta. É a mesma regra que a grelha actual já usa; a diferença é
        /// que agora se pode escrever um teste para ela.
        /// </summary>
        public static List<MedicaoResultado> DeParedes(
            IEnumerable<Parede> paredes, RegraDesconto regra,
            Func<string, bool> artigoConhecido, IFormatProvider cultura)
        {
            if (cultura == null) cultura = CultureInfo.CurrentCulture;
            var saida = new List<MedicaoResultado>();
            if (paredes == null) return saida;

            foreach (var p in paredes)
            {
                if (p == null) continue;

                double bruta = p.AreaBruta;
                double desconto = p.DescontoVaos(regra);
                double liquida = p.AreaLiquida(regra);

                var m = new MedicaoResultado
                {
                    Handle = p.Handle,
                    Piso = p.Piso,
                    Servico = p.Servico,
                    Artigo = p.Artigo,
                    Bloco = p.Bloco,
                    Alcado = p.Alcado,
                    Nota = p.Nota,
                    Layer = p.Layer,
                    Rotulo = RotuloDaParede(p),
                    // A ÚNICA quantidade da parede. O comprimento dela fica nas
                    // propriedades, onde é uma dimensão: se entrasse aqui,
                    // caía no mesmo balde de metros que a quantidade de um
                    // rodapé — e passavam a somar-se duas coisas diferentes.
                    Unidade = p.Unidade,
                    Quantidade = liquida,
                    // Uma parede e uma camada são tipos de medida DIFERENTES,
                    // e é isso que explica a unidade: comprimento × altura dá
                    // m²; área em planta × espessura dá m³. Juntá-las no mesmo
                    // grupo era pôr duas grandezas debaixo do mesmo rótulo.
                    TipoMedida = p.AreaVezesAltura ? "Camadas" : "Alvenaria",

                    // Os dois factores, para as colunas da árvore. Numa
                    // camada o primeiro é uma ÁREA em planta e o segundo a
                    // espessura — é o par que explica por que dá m³.
                    Comprimento = p.AreaVezesAltura ? p.Largura : p.ComprimentoTotal,
                    UnidadeComprimento = p.AreaVezesAltura ? Unidades.M2 : Unidades.Metro,
                    Altura = p.Altura,
                    UnidadeAltura = Unidades.Metro
                };

                if (artigoConhecido != null)
                {
                    if (string.IsNullOrEmpty(p.Artigo))
                        m.Alertas |= AlertaNo.PorClassificar;
                    else if (!artigoConhecido(p.Artigo))
                        m.Alertas |= AlertaNo.ArtigoDesconhecido;
                }

                // Os vãos descontam mais do que a parede tem: erro de medição,
                // e dos que não rebentam — sai um número com ar de estar bem.
                if (desconto > bruta + 1e-9) m.Alertas |= AlertaNo.VaosExcessivos;

                PropriedadesDaParede(m, p, regra, bruta, desconto, liquida, cultura);

                for (int i = 0; i < p.Vaos.Count; i++)
                    m.Deducoes.Add(DeducaoDoVao(p, p.Vaos[i], i, regra, cultura));

                var marcas = p.Marcas;
                for (int i = 0; i < marcas.Count; i++)
                    m.Titulos.Add(TituloDaMarca(marcas[i], p.TextoDaMarca(i), i));

                saida.Add(m);
            }
            return saida;
        }

        public static List<MedicaoResultado> DeParedes(
            IEnumerable<Parede> paredes, RegraDesconto regra)
        {
            return DeParedes(paredes, regra, null, CultureInfo.CurrentCulture);
        }

        /// <summary>
        /// Como a medição se chama na coluna. A nota escrita à mão ganha ao
        /// nome genérico: quem escreveu "empena norte" quer ler isso, não
        /// "Parede 2A7".
        /// </summary>
        private static string RotuloDaParede(Parede p)
        {
            string nota = (p.Nota ?? "").Trim();
            if (nota.Length > 0) return nota;

            string tipo = p.AreaVezesAltura ? "Camada" : (p.SoArea ? "Área" : "Parede");
            string handle = (p.Handle ?? "").Trim();
            return handle.Length > 0 ? tipo + " " + handle : tipo;
        }

        private static void PropriedadesDaParede(
            MedicaoResultado m, Parede p, RegraDesconto regra,
            double bruta, double desconto, double liquida, IFormatProvider cultura)
        {
            var pr = m.Propriedades;

            pr.Add(new Propriedade("servico", "Serviço", p.Servico ?? "", true, true));
            pr.Add(new Propriedade("artigo", "Artigo",
                TextoDoArtigo(p.Artigo), true, true));
            pr.Add(new Propriedade("piso", "Piso", p.Piso ?? "", true, true));

            // O comprimento é geometria: vem da polyline e não se escreve. Era
            // aqui que a árvore antiga o punha numa coluna com ar de quantidade.
            pr.Add(new Propriedade("comprimento", "Comprimento",
                Unidades.Texto(Unidades.Metro, p.ComprimentoTotal, cultura), true, false));
            pr.Add(new Propriedade("altura", "Altura",
                Unidades.Texto(Unidades.Metro, p.Altura, cultura), true, true));
            if (p.Largura > 0)
                pr.Add(new Propriedade("largura", "Largura",
                    Unidades.Texto(Unidades.Metro, p.Largura, cultura), false, true));
            pr.Add(new Propriedade("espessura", "Espessura",
                Unidades.Texto(Unidades.Metro, p.Espessura, cultura), true, true));

            pr.Add(new Propriedade("areaBruta", "Área bruta",
                Unidades.Texto(Unidades.M2, bruta, cultura), false, false));
            pr.Add(new Propriedade("desconto", "Desconto de vãos",
                desconto > 0 ? "−" + Unidades.Texto(Unidades.M2, desconto, cultura)
                             : Unidades.Texto(Unidades.M2, 0.0, cultura), false, false));
            pr.Add(new Propriedade("quantidade", "Quantidade",
                Unidades.Texto(p.Unidade, liquida, cultura), true, false));
            pr.Add(new Propriedade("volume", "Volume",
                Unidades.Texto(Unidades.M3, p.Volume(regra), cultura), false, false));

            pr.Add(new Propriedade("preAroUn", "Pré-aros",
                Unidades.Texto(Unidades.Unidade, p.PreAroUn, cultura), false, false));
            pr.Add(new Propriedade("preAroMl", "Pré-aros (desenvolvimento)",
                Unidades.Texto(Unidades.Metro, p.PreAroMl, cultura), false, false));

            if (!string.IsNullOrEmpty(p.Bloco))
                pr.Add(new Propriedade("bloco", "Bloco / etiqueta", p.Bloco, false, false));
            if (!string.IsNullOrEmpty(p.Alcado))
                pr.Add(new Propriedade("alcado", "Alçado / zona", p.Alcado, false, false));
            if (!string.IsNullOrEmpty(p.Nota))
                pr.Add(new Propriedade("nota", "Designação", p.Nota, false, false));
            if (p.LinhasEmBrancoDepois > 0)
                pr.Add(new Propriedade("linhasBranco", "Linhas em branco depois",
                    p.LinhasEmBrancoDepois.ToString(CultureInfo.InvariantCulture),
                    false, false));

            pr.Add(new Propriedade("layer", "Layer", p.Layer ?? "", false, false));
            pr.Add(new Propriedade("handle", "Handle", p.Handle ?? "", false, false));
        }

        private static DeducaoResultado DeducaoDoVao(
            Parede p, Vao v, int indice, RegraDesconto regra, IFormatProvider cultura)
        {
            string designacao = (v.Designacao ?? "").Trim();
            string tipo = v.Tipo == TipoVao.Porta ? "Porta" : "Janela";

            var d = new DeducaoResultado
            {
                Indice = indice,
                Designacao = designacao,
                Desconto = v.Desconto(regra),
                // A dedução sai na unidade em que foi descontada — a da parede.
                Unidade = p.Unidade,
                Rotulo = "Vão · " + (designacao.Length > 0 ? designacao : tipo),
                Propriedades = new List<Propriedade>(),
                TextoPesquisa = Normalizar(designacao + " " + tipo + " vao")
            };

            d.Propriedades.Add(new Propriedade("designacao", "Designação", designacao, true, true));
            d.Propriedades.Add(new Propriedade("tipoVao", "Tipo", tipo, true, true));
            d.Propriedades.Add(new Propriedade("larguraVao", "Largura",
                Unidades.Texto(Unidades.Metro, v.Largura, cultura), true, true));
            d.Propriedades.Add(new Propriedade("alturaVao", "Altura",
                Unidades.Texto(Unidades.Metro, v.Altura, cultura), true, true));
            d.Propriedades.Add(new Propriedade("quantidadeVao", "Quantidade",
                Unidades.Texto(Unidades.Unidade, v.Quantidade, cultura), true, true));
            d.Propriedades.Add(new Propriedade("areaVao", "Área do vão",
                Unidades.Texto(Unidades.M2, v.AreaTotal, cultura), false, false));
            d.Propriedades.Add(new Propriedade("descontoVao", "Desconto",
                "−" + Unidades.Texto(p.Unidade, d.Desconto, cultura), true, false));
            d.Propriedades.Add(new Propriedade("preAro", "Pré-aro",
                v.PreAro ? "Sim" : "Não", false, true));
            if (v.PreAro)
                d.Propriedades.Add(new Propriedade("preAroVaoMl", "Pré-aro (desenvolvimento)",
                    Unidades.Texto(Unidades.Metro, v.PreAroMetros, cultura), false, false));

            return d;
        }

        private static TituloResultado TituloDaMarca(string nivel, string texto, int indice)
        {
            string nome = nivel == "CAP" ? "Capítulo" : "Artigo";
            string codigo = ChaveArtigo.Codigo(texto ?? "");
            string descricao = ChaveArtigo.Designacao(texto ?? "");

            var t = new TituloResultado
            {
                Indice = indice,
                Nivel = nivel,
                Codigo = codigo,
                Descricao = descricao,
                Propriedades = new List<Propriedade>(),
                TextoPesquisa = Normalizar("titulo " + nome + " " + codigo + " " + descricao)
            };

            var sb = new StringBuilder("Título · ").Append(nome);
            if (codigo.Length > 0) sb.Append(" · ").Append(codigo);
            if (descricao.Length > 0) sb.Append(" ").Append(descricao);
            t.Rotulo = sb.ToString();

            t.Propriedades.Add(new Propriedade("nivelTitulo", "Nível", nome, true, false));
            t.Propriedades.Add(new Propriedade("codigoTitulo", "Código", codigo, true, true));
            t.Propriedades.Add(new Propriedade("descricaoTitulo", "Descrição", descricao, true, true));
            return t;
        }

        // ------------------------------------------------------------------
        // 2. Construção da hierarquia
        // ------------------------------------------------------------------

        /// <summary>
        /// Constrói a árvore completa, com os totais de tudo. Os filtros não
        /// entram aqui: esta árvore é a fonte, e é sobre ela que o
        /// <see cref="Projetar"/> decide o que se vê.
        ///
        /// É essa separação que garante o que o plano pede em dois sítios:
        /// o "Limpar tudo" opera sobre a FONTE — handles distintos, a árvore
        /// toda — e nunca sobre as linhas que o filtro deixou à vista.
        /// </summary>
        public static NoResultado Construir(
            IEnumerable<MedicaoResultado> medicoes, IFormatProvider cultura)
        {
            if (cultura == null) cultura = CultureInfo.CurrentCulture;

            var raiz = new NoResultado
            {
                Id = "R",
                Tipo = TipoNo.Raiz,
                Rotulo = RotuloRaiz,
                TextoPesquisa = Normalizar(RotuloRaiz)
            };

            // Índices por Id: os grupos criam-se à medida que aparecem, e a
            // ordem de chegada é a ordem da folha — que é quem os ordena.
            var grupos = new Dictionary<string, NoResultado>(StringComparer.Ordinal);

            foreach (var m in medicoes ?? new List<MedicaoResultado>())
            {
                if (m == null) continue;

                string piso = Rotulo(m.Piso, SemPiso);
                // O segundo nível é o TIPO DE MEDIDA, não o serviço: é ele que
                // determina a unidade de tudo o que fica por baixo. O serviço
                // continua a identificar a medição na folha e vive nas
                // propriedades. Ver TipoNo.Tipo.
                string tipo = Rotulo(m.TipoMedida, SemTipo);
                string servico = Rotulo(m.Servico, SemServico);
                string codigo = ChaveArtigo.Codigo(m.Artigo ?? "");
                bool semArtigo = string.IsNullOrEmpty((m.Artigo ?? "").Trim());

                var noPiso = Grupo(grupos, raiz, TipoNo.Piso,
                    raiz.Id + "/P:" + Chave(piso), piso,
                    piso, null, null);

                var noTipo = Grupo(grupos, noPiso, TipoNo.Tipo,
                    noPiso.Id + "/T:" + Chave(tipo), tipo,
                    piso, null, null);
                noTipo.TipoMedida = tipo;

                string idArtigo = noTipo.Id + "/A:" +
                    (semArtigo ? "" : ChaveDoArtigo(m.Artigo));
                var noArtigo = Grupo(grupos, noTipo, TipoNo.Artigo, idArtigo,
                    semArtigo ? PorClassificar : TextoDoArtigo(m.Artigo),
                    piso, servico, m.Artigo);
                if (semArtigo) noArtigo.Alertas |= AlertaNo.PorClassificar;

                var noMed = new NoResultado
                {
                    // Só o handle. A medição continua a ser a mesma depois de
                    // ser reclassificada para outro artigo ou mudada de piso —
                    // se o Id trouxesse o caminho, mudar de grupo perdia a
                    // selecção e o scroll de quem estava a editar.
                    Id = "M:" + (m.Handle ?? ""),
                    Tipo = TipoNo.Medicao,
                    Rotulo = m.Rotulo,
                    Handle = m.Handle,
                    Piso = piso,
                    Servico = servico,
                    TipoMedida = tipo,
                    Comprimento = m.Comprimento,
                    UnidadeComprimento = m.UnidadeComprimento,
                    Altura = m.Altura,
                    UnidadeAltura = m.UnidadeAltura,
                    Artigo = m.Artigo,
                    Alertas = m.Alertas,
                    Propriedades = m.Propriedades ?? new List<Propriedade>()
                };
                noMed.Quantidades.Somar(m.Unidade, m.Quantidade);
                noArtigo.Acrescentar(noMed);

                var busca = new StringBuilder();
                Juntar(busca, m.Rotulo); Juntar(busca, m.Servico);
                Juntar(busca, codigo); Juntar(busca, ChaveArtigo.Designacao(m.Artigo ?? ""));
                Juntar(busca, m.Piso); Juntar(busca, m.Bloco);
                Juntar(busca, m.Alcado); Juntar(busca, m.Nota);
                Juntar(busca, m.Handle); Juntar(busca, m.Layer);
                if (semArtigo) Juntar(busca, PorClassificar);

                foreach (var d in m.Deducoes ?? new List<DeducaoResultado>())
                {
                    var noVao = new NoResultado
                    {
                        Id = noMed.Id + "/V:" +
                             d.Indice.ToString(CultureInfo.InvariantCulture),
                        Tipo = TipoNo.Vao,
                        Rotulo = d.Rotulo,
                        Handle = m.Handle,
                        Indice = d.Indice,
                        Piso = piso,
                        Servico = servico,
                        Artigo = m.Artigo,
                        Propriedades = d.Propriedades ?? new List<Propriedade>(),
                        TextoPesquisa = d.TextoPesquisa,
                        // O desconto JÁ está dentro da área líquida da parede.
                        // Mostra-se aqui para se ver de onde ele vem; somá-lo
                        // ao grupo descontava-o uma segunda vez.
                        ContaParaTotal = false
                    };
                    noVao.Quantidades.Somar(d.Unidade, -d.Desconto);
                    noMed.Acrescentar(noVao);
                    Juntar(busca, d.Designacao);
                }

                foreach (var t in m.Titulos ?? new List<TituloResultado>())
                {
                    var noTitulo = new NoResultado
                    {
                        Id = noMed.Id + "/T:" +
                             t.Indice.ToString(CultureInfo.InvariantCulture),
                        Tipo = TipoNo.Titulo,
                        Rotulo = t.Rotulo,
                        Handle = m.Handle,
                        Indice = t.Indice,
                        Piso = piso,
                        Servico = servico,
                        Artigo = m.Artigo,
                        Propriedades = t.Propriedades ?? new List<Propriedade>(),
                        TextoPesquisa = t.TextoPesquisa,
                        // Um título é uma linha de texto na folha, não uma
                        // quantidade. Dar-lhe um total era inventá-lo.
                        ContaParaTotal = false
                    };
                    noMed.Acrescentar(noTitulo);
                    Juntar(busca, t.Codigo);
                    Juntar(busca, t.Descricao);
                }

                noMed.TextoPesquisa = Normalizar(busca.ToString());
            }

            OrdenarResiduaisNoFim(raiz);
            Agregar(raiz);
            RotularTipos(raiz);
            PropriedadesDosGrupos(raiz, cultura);
            return raiz;
        }

        public static NoResultado Construir(IEnumerable<MedicaoResultado> medicoes)
        {
            return Construir(medicoes, CultureInfo.CurrentCulture);
        }

        /// <summary>Atalho para a alvenaria: projectar e construir de uma vez.</summary>
        public static NoResultado DeAlvenaria(
            IEnumerable<Parede> paredes, RegraDesconto regra,
            Func<string, bool> artigoConhecido, IFormatProvider cultura)
        {
            return Construir(
                DeParedes(paredes, regra, artigoConhecido, cultura), cultura);
        }

        public static NoResultado DeAlvenaria(IEnumerable<Parede> paredes, RegraDesconto regra)
        {
            return DeAlvenaria(paredes, regra, null, CultureInfo.CurrentCulture);
        }

        private static NoResultado Grupo(
            Dictionary<string, NoResultado> indice, NoResultado pai, TipoNo tipo,
            string id, string rotulo, string piso, string servico, string artigo)
        {
            NoResultado no;
            if (indice.TryGetValue(id, out no)) return no;

            no = new NoResultado
            {
                Id = id,
                Tipo = tipo,
                Rotulo = rotulo,
                Piso = piso,
                Servico = servico,
                Artigo = artigo,
                TextoPesquisa = Normalizar(rotulo)
            };
            pai.Acrescentar(no);
            indice[id] = no;
            return no;
        }

        /// <summary>
        /// "Sem piso", "Sem serviço" e "Por classificar" vão para o fim dos
        /// seus irmãos.
        ///
        /// Pela ordem de chegada, uma parede por classificar medida em primeiro
        /// empurrava o trabalho arrumado para baixo dela. O que falta
        /// classificar lê-se no fim, como na folha.
        /// </summary>
        private static void OrdenarResiduaisNoFim(NoResultado no)
        {
            if (no.Filhos.Count > 1)
            {
                var normais = new List<NoResultado>();
                var residuais = new List<NoResultado>();
                foreach (var f in no.Filhos)
                    (EhResidual(f) ? residuais : normais).Add(f);

                if (residuais.Count > 0)
                {
                    no.Filhos.Clear();
                    no.Filhos.AddRange(normais);
                    no.Filhos.AddRange(residuais);
                }
            }
            foreach (var f in no.Filhos) OrdenarResiduaisNoFim(f);
        }

        private static bool EhResidual(NoResultado no)
        {
            if (!no.EhGrupo) return false;
            return no.Rotulo == SemPiso || no.Rotulo == SemServico || no.Rotulo == SemTipo
                || no.Rotulo == PorClassificar;
        }

        /// <summary>
        /// Totais de baixo para cima. Um grupo NUNCA tem um total escrito à
        /// mão: é sempre a soma dos filhos que contam, e cada unidade no seu
        /// balde.
        /// </summary>
        private static void Agregar(NoResultado no)
        {
            foreach (var f in no.Filhos)
            {
                Agregar(f);
                // Os alertas sobem sempre — é assim que um piso recolhido
                // consegue dizer que tem qualquer coisa por classificar lá
                // dentro sem obrigar a abri-lo.
                no.Alertas |= f.Alertas;
            }

            if (no.Tipo == TipoNo.Medicao) return;   // já traz a sua quantidade

            foreach (var f in no.Filhos)
                if (f.ContaParaTotal) no.Quantidades.Somar(f.Quantidades);

            AgregarFactores(no);
        }

        /// <summary>
        /// Os factores de um grupo: o comprimento SOMA-SE, a altura NÃO.
        ///
        /// Somar comprimentos dá uma coisa que existe — o desenvolvimento total
        /// daquele conjunto de paredes. Somar alturas não dá nada: três paredes
        /// de 2,80 m não fazem uma de 8,40, e o número que saía era só a
        /// contagem disfarçada de medida.
        ///
        /// A altura de um grupo só aparece quando é a MESMA em todos os filhos,
        /// que é o caso corrente num piso; havendo mais do que uma, fica o
        /// traço. E a soma dos comprimentos só se faz entre a MESMA unidade —
        /// somar os metros de uma parede com os metros quadrados em planta de
        /// uma camada seria o mesmo erro das quantidades.
        /// </summary>
        private static void AgregarFactores(NoResultado no)
        {
            double soma = 0.0;
            bool algumComprimento = false;
            string unidadeComp = null;
            bool comprimentoCoerente = true;

            double altura = double.NaN;
            string unidadeAlt = null;
            bool alturaUnica = true;

            foreach (var f in no.Filhos)
            {
                if (!f.ContaParaTotal) continue;

                if (!double.IsNaN(f.Comprimento))
                {
                    if (!algumComprimento) { unidadeComp = f.UnidadeComprimento; algumComprimento = true; }
                    else if (f.UnidadeComprimento != unidadeComp) comprimentoCoerente = false;
                    soma += f.Comprimento;
                }

                if (!double.IsNaN(f.Altura))
                {
                    if (double.IsNaN(altura)) { altura = f.Altura; unidadeAlt = f.UnidadeAltura; }
                    else if (Math.Abs(altura - f.Altura) > 1e-9 ||
                             f.UnidadeAltura != unidadeAlt) alturaUnica = false;
                }
            }

            no.Comprimento = algumComprimento && comprimentoCoerente ? soma : double.NaN;
            no.UnidadeComprimento = unidadeComp;
            no.Altura = alturaUnica ? altura : double.NaN;
            no.UnidadeAltura = unidadeAlt;
        }

        private static void PropriedadesDosGrupos(NoResultado no, IFormatProvider cultura)
        {
            if (no.EhGrupo)
            {
                int medicoes = ContarMedicoes(no);
                no.Propriedades.Clear();
                no.Propriedades.Add(new Propriedade("grupo", TipoDeGrupo(no.Tipo),
                    no.Rotulo, true, false));
                no.Propriedades.Add(new Propriedade("medicoes", "Medições",
                    medicoes.ToString("N0", cultura), true, false));
                no.Propriedades.Add(new Propriedade("total", "Total",
                    no.Quantidades.Texto(cultura), true, false));
            }
            foreach (var f in no.Filhos) PropriedadesDosGrupos(f, cultura);
        }

        /// <summary>
        /// Põe a unidade no nome do grupo de tipo: "Alvenaria · m²",
        /// "Camadas · m³", "Contagens · un.".
        ///
        /// Só depois de agregar, porque é da soma dos filhos que a unidade
        /// sai. E é honesto pô-la ali: o tipo de medida É o que determina a
        /// unidade, e ler "Alvenaria" sem saber em que se mede obriga a descer
        /// à primeira linha para descobrir.
        ///
        /// Um tipo tem sempre UMA unidade — é essa a definição. Se algum dia
        /// aparecer com duas, o rótulo mostra as duas em vez de escolher uma,
        /// porque nesse caso o erro está no adaptador e convém vê-lo.
        /// </summary>
        private static void RotularTipos(NoResultado no)
        {
            if (no.Tipo == TipoNo.Tipo && !no.Quantidades.Vazio)
            {
                var partes = new List<string>();
                foreach (var u in no.Quantidades.UnidadesPresentes)
                    partes.Add(Unidades.Escrita(u));
                no.Rotulo = no.Rotulo + " · " + string.Join(" · ", partes.ToArray());
            }
            foreach (var f in no.Filhos) RotularTipos(f);
        }

        private static string TipoDeGrupo(TipoNo t)
        {
            if (t == TipoNo.Piso) return "Pavimento";
            if (t == TipoNo.Tipo) return "Tipo de medida";
            if (t == TipoNo.Artigo) return "Artigo";
            return "Grupo";
        }

        /// <summary>Quantas medições existem por baixo deste nó.</summary>
        public static int ContarMedicoes(NoResultado no)
        {
            if (no == null) return 0;
            if (no.Tipo == TipoNo.Medicao) return 1;
            int n = 0;
            foreach (var f in no.Filhos) n += ContarMedicoes(f);
            return n;
        }

        // ------------------------------------------------------------------
        // Valores disponíveis para os filtros
        // ------------------------------------------------------------------

        /// <summary>
        /// Os valores que cada filtro pode tomar, tirados da árvore.
        ///
        /// Vêm dos dados e não de uma lista escrita à mão: um filtro que
        /// ofereça "PISO 3" num desenho que não tem PISO 3 nenhum só produz
        /// vistas vazias, e quem o escolhe fica a pensar que perdeu medições.
        ///
        /// A ordem é a da árvore — que é a da folha —, e não alfabética: é
        /// assim que o PISO 0 aparece antes do PISO 1 e o "Por classificar"
        /// fica no fim, onde já está na árvore.
        /// </summary>
        public static List<string> Pavimentos(NoResultado raiz)
        {
            return Distintos(raiz, TipoNo.Medicao, no => no.Piso);
        }

        public static List<string> Servicos(NoResultado raiz)
        {
            return Distintos(raiz, TipoNo.Medicao, no => no.Servico);
        }

        /// <summary>
        /// Os tipos de medida presentes: Alvenaria, Camadas, Materiais,
        /// Lineares, Contagens. Sem a unidade colada — essa é para ler no
        /// rótulo do grupo, não para comparar num filtro.
        /// </summary>
        public static List<string> TiposDeMedida(NoResultado raiz)
        {
            return Distintos(raiz, TipoNo.Medicao, no => no.TipoMedida);
        }

        /// <summary>Os códigos de artigo presentes, com `Por classificar` incluído.</summary>
        public static List<string> Artigos(NoResultado raiz)
        {
            return Distintos(raiz, TipoNo.Medicao, no =>
            {
                string codigo = ChaveArtigo.Codigo(no.Artigo ?? "").Trim();
                return codigo.Length > 0 ? codigo : PorClassificar;
            });
        }

        /// <summary>As unidades que existem mesmo nesta árvore: "m2", "m3", "m", "un".</summary>
        public static List<string> UnidadesPresentes(NoResultado raiz)
        {
            var saida = new List<string>();
            var vistas = new HashSet<string>(StringComparer.Ordinal);
            Percorrer(raiz, TipoNo.Medicao, no =>
            {
                foreach (var u in no.Quantidades.UnidadesPresentes)
                    if (vistas.Add(u)) saida.Add(u);
            });
            return saida;
        }

        /// <summary>Os estados que fazem sentido oferecer nesta árvore.</summary>
        public static EstadoResultado EstadosPresentes(NoResultado raiz)
        {
            EstadoResultado estados = EstadoResultado.Nenhum;
            Percorrer(raiz, TipoNo.Medicao, no =>
            {
                if ((no.Alertas & AlertaNo.PorClassificar) != 0)
                    estados |= EstadoResultado.PorClassificar;
                if ((no.Alertas & (AlertaNo.VaosExcessivos | AlertaNo.ArtigoDesconhecido)) != 0)
                    estados |= EstadoResultado.ComAlerta;
                foreach (var f in no.Filhos)
                    if (f.Tipo == TipoNo.Vao) { estados |= EstadoResultado.ComVaos; break; }
            });
            return estados;
        }

        private static List<string> Distintos(
            NoResultado raiz, TipoNo tipo, Func<NoResultado, string> valor)
        {
            var saida = new List<string>();
            var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Percorrer(raiz, tipo, no =>
            {
                string v = valor(no) ?? "";
                if (v.Length > 0 && vistos.Add(v)) saida.Add(v);
            });
            return saida;
        }

        private static void Percorrer(NoResultado no, TipoNo tipo, Action<NoResultado> accao)
        {
            if (no == null) return;
            if (no.Tipo == tipo) accao(no);
            foreach (var f in no.Filhos) Percorrer(f, tipo, accao);
        }

        /// <summary>Os handles distintos por baixo deste nó, pela ordem da árvore.</summary>
        public static List<string> Handles(NoResultado no)
        {
            var vistos = new HashSet<string>(StringComparer.Ordinal);
            var saida = new List<string>();
            Handles(no, vistos, saida);
            return saida;
        }

        private static void Handles(NoResultado no, HashSet<string> vistos, List<string> saida)
        {
            if (no == null) return;
            if (no.Tipo == TipoNo.Medicao && !string.IsNullOrEmpty(no.Handle)
                && vistos.Add(no.Handle))
                saida.Add(no.Handle);
            foreach (var f in no.Filhos) Handles(f, vistos, saida);
        }

        // ------------------------------------------------------------------
        // 3. Vista: pesquisa, filtros, recolhas e totais visíveis
        // ------------------------------------------------------------------

        /// <summary>
        /// Achata a árvore nas linhas a desenhar, aplicando pesquisa, filtros
        /// e recolhas — por esta ordem, e sem tocar na árvore de origem.
        ///
        /// Recolher NÃO é filtrar: um grupo fechado continua a mostrar o total
        /// de tudo o que tem lá dentro. Se recolher mudasse o total, fechar um
        /// piso para ver melhor dava a impressão de a medição ter encolhido.
        /// </summary>
        public static VistaResultados Projetar(NoResultado raiz, EstadoVista estado)
        {
            var vista = new VistaResultados();
            if (raiz == null) return vista;
            if (estado == null) estado = new EstadoVista();

            vista.TotaisGlobais.Somar(raiz.Quantidades);
            vista.MedicoesTotais = ContarMedicoes(raiz);
            vista.AFiltrar = estado.AFiltrar;

            var termos = Termos(estado.Pesquisa);
            var filtro = estado.Filtro ?? new FiltroResultados();

            // Passo 1 — quem passa nos filtros. Avalia-se na MEDIÇÃO: um grupo
            // existe porque tem filhos, e não tem estado próprio nenhum para
            // ser filtrado à parte.
            var passa = new HashSet<string>(StringComparer.Ordinal);
            MarcarFiltro(raiz, filtro, passa);

            // Passo 2 — quem corresponde à pesquisa, e quem fica visível por
            // parentesco: os antepassados de quem corresponde, e tudo o que
            // esteja por baixo.
            var visivel = new HashSet<string>(StringComparer.Ordinal);
            var correspondeu = new HashSet<string>(StringComparer.Ordinal);
            if (termos.Count == 0)
            {
                foreach (var id in passa) visivel.Add(id);
            }
            else
            {
                MarcarPesquisa(raiz, termos, passa, visivel, correspondeu, false);
            }

            // Passo 3 — totais do que está à vista, de baixo para cima.
            var qtd = new Dictionary<string, Quantidades>(StringComparer.Ordinal);
            QuantidadesVisiveis(raiz, visivel, qtd);

            vista.MedicoesVisiveis = ContarVisiveis(raiz, visivel);
            var totalRaiz = Obter(qtd, raiz.Id);
            if (totalRaiz != null) vista.TotaisVisiveis.Somar(totalRaiz);

            // Passo 4 — achatar. A raiz "Pavimentos" é uma linha como as
            // outras: é ela que dá o total geral no topo da árvore.
            if (visivel.Contains(raiz.Id))
                Achatar(raiz, estado, visivel, correspondeu, qtd, vista.Nos);

            return vista;
        }

        private static bool MarcarFiltro(
            NoResultado no, FiltroResultados filtro, HashSet<string> passa)
        {
            if (no.Tipo == TipoNo.Medicao)
            {
                if (!Aceita(no, filtro)) return false;
                passa.Add(no.Id);
                // Vãos e títulos acompanham a medição a que pertencem: não são
                // resultados por si, são detalhe dela.
                foreach (var f in no.Filhos) passa.Add(f.Id);
                return true;
            }

            bool algum = false;
            foreach (var f in no.Filhos)
                if (MarcarFiltro(f, filtro, passa)) algum = true;

            // Um grupo que ficou sem filhos visíveis desaparece com eles. Um
            // piso vazio a dizer "0,00 m²" só ocupa a lista.
            if (algum) passa.Add(no.Id);
            return algum;
        }

        private static bool Aceita(NoResultado med, FiltroResultados filtro)
        {
            if (filtro == null || filtro.Vazio) return true;

            if (filtro.Pavimentos.Count > 0 && !filtro.Pavimentos.Contains(med.Piso ?? ""))
                return false;
            if (filtro.Servicos.Count > 0 && !filtro.Servicos.Contains(med.Servico ?? ""))
                return false;

            if (filtro.Artigos.Count > 0)
            {
                string codigo = ChaveArtigo.Codigo(med.Artigo ?? "");
                if (string.IsNullOrEmpty(codigo.Trim())) codigo = PorClassificar;
                if (!filtro.Artigos.Contains(codigo)) return false;
            }

            if (filtro.UnidadesFiltradas.Count > 0)
            {
                bool tem = false;
                foreach (var u in med.Quantidades.UnidadesPresentes)
                    if (filtro.UnidadesFiltradas.Contains(u)) { tem = true; break; }
                if (!tem) return false;
            }

            if (filtro.Estados != EstadoResultado.Nenhum)
            {
                bool algum = false;
                if ((filtro.Estados & EstadoResultado.PorClassificar) != 0
                    && (med.Alertas & AlertaNo.PorClassificar) != 0) algum = true;
                if (!algum && (filtro.Estados & EstadoResultado.ComVaos) != 0
                    && TemVaos(med)) algum = true;
                if (!algum && (filtro.Estados & EstadoResultado.ComAlerta) != 0
                    && (med.Alertas & (AlertaNo.VaosExcessivos | AlertaNo.ArtigoDesconhecido)) != 0)
                    algum = true;
                if (!algum) return false;
            }

            return true;
        }

        private static bool TemVaos(NoResultado med)
        {
            foreach (var f in med.Filhos) if (f.Tipo == TipoNo.Vao) return true;
            return false;
        }

        /// <summary>
        /// Marca o que a pesquisa deixa ver.
        ///
        /// Uma correspondência arrasta consigo os antepassados — senão o
        /// resultado aparecia solto, sem se saber de que piso é — e tudo o que
        /// tem por baixo: quem procura um artigo quer ver as medições dele.
        /// </summary>
        private static bool MarcarPesquisa(
            NoResultado no, List<string> termos, HashSet<string> passa,
            HashSet<string> visivel, HashSet<string> correspondeu, bool herdado)
        {
            if (!passa.Contains(no.Id)) return false;

            bool proprio = Corresponde(no.TextoPesquisa, termos);
            if (proprio) correspondeu.Add(no.Id);

            bool sob = herdado || proprio;
            bool algumFilho = false;
            foreach (var f in no.Filhos)
                if (MarcarPesquisa(f, termos, passa, visivel, correspondeu, sob))
                    algumFilho = true;

            if (proprio || sob || algumFilho)
            {
                visivel.Add(no.Id);
                return true;
            }
            return false;
        }

        private static bool Corresponde(string texto, List<string> termos)
        {
            if (string.IsNullOrEmpty(texto)) return false;
            foreach (var t in termos)
                if (texto.IndexOf(t, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        private static Quantidades QuantidadesVisiveis(
            NoResultado no, HashSet<string> visivel,
            Dictionary<string, Quantidades> destino)
        {
            if (!visivel.Contains(no.Id)) return null;

            var q = new Quantidades();
            if (no.Tipo == TipoNo.Medicao)
            {
                q.Somar(no.Quantidades);
                foreach (var f in no.Filhos) QuantidadesVisiveis(f, visivel, destino);
            }
            else if (no.Tipo == TipoNo.Vao || no.Tipo == TipoNo.Titulo)
            {
                q.Somar(no.Quantidades);
            }
            else
            {
                foreach (var f in no.Filhos)
                {
                    var qf = QuantidadesVisiveis(f, visivel, destino);
                    if (qf != null && f.ContaParaTotal) q.Somar(qf);
                }
            }

            destino[no.Id] = q;
            return q;
        }

        private static int ContarVisiveis(NoResultado no, HashSet<string> visivel)
        {
            if (!visivel.Contains(no.Id)) return 0;
            if (no.Tipo == TipoNo.Medicao) return 1;
            int n = 0;
            foreach (var f in no.Filhos) n += ContarVisiveis(f, visivel);
            return n;
        }

        private static void Achatar(
            NoResultado no, EstadoVista estado, HashSet<string> visivel,
            HashSet<string> correspondeu, Dictionary<string, Quantidades> qtd,
            List<NoVisivel> saida)
        {
            var filhosVisiveis = new List<NoResultado>();
            foreach (var f in no.Filhos)
                if (visivel.Contains(f.Id)) filhosVisiveis.Add(f);

            // Enquanto se pesquisa ou filtra, a recolha é ignorada: tudo o que
            // sobreviveu ao filtro É um resultado ou o caminho até um, e deixar
            // um grupo fechado por cima escondia aquilo que se foi procurar.
            bool expandido = estado.AFiltrar || estado.Expandido(no.Id);

            saida.Add(new NoVisivel
            {
                No = no,
                Quantidades = Obter(qtd, no.Id) ?? new Quantidades(),
                TemFilhos = filhosVisiveis.Count > 0,
                Expandido = expandido,
                Correspondeu = correspondeu.Contains(no.Id)
            });

            if (!expandido) return;
            foreach (var f in filhosVisiveis)
                Achatar(f, estado, visivel, correspondeu, qtd, saida);
        }

        private static Quantidades Obter(Dictionary<string, Quantidades> d, string id)
        {
            Quantidades q;
            return id != null && d.TryGetValue(id, out q) ? q : null;
        }

        // ------------------------------------------------------------------
        // Texto
        // ------------------------------------------------------------------

        /// <summary>
        /// A forma em que a pesquisa compara: minúsculas e sem acentos.
        ///
        /// Sem isto, procurar "alcado" não encontrava "Alçado" e procurar
        /// "PISO" não encontrava "Piso" — e quem escreve depressa não vai
        /// buscar o cedilha ao teclado.
        /// </summary>
        public static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            string d = s.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(d.Length);
            bool espaco = false;
            for (int i = 0; i < d.Length; i++)
            {
                char c = d[i];
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                    continue;

                if (char.IsWhiteSpace(c) || c == ChaveArtigo.Sep[0])
                {
                    espaco = sb.Length > 0;
                    continue;
                }
                if (espaco) { sb.Append(' '); espaco = false; }
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Os termos de uma pesquisa. Vários termos são um E, não um OU:
        /// "piso 1 tijolo" procura o que tenha as três coisas.
        /// </summary>
        public static List<string> Termos(string pesquisa)
        {
            var termos = new List<string>();
            string n = Normalizar(pesquisa);
            if (n.Length == 0) return termos;
            foreach (var t in n.Split(' '))
                if (t.Length > 0) termos.Add(t);
            return termos;
        }

        private static void Juntar(StringBuilder sb, string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(s);
        }

        private static string Rotulo(string valor, string vazio)
        {
            string v = (valor ?? "").Trim();
            return v.Length > 0 ? v : vazio;
        }

        private static string Chave(string s)
        {
            return Normalizar(s);
        }

        /// <summary>
        /// A chave do grupo de um artigo, para o Id do nó.
        ///
        /// Usa a forma <see cref="ChaveArtigo.Normalizada"/> — a mesma que o
        /// mapa usa para indexar — e não o <see cref="Normalizar"/> da
        /// pesquisa: aqui não se querem acentos removidos nem maiúsculas
        /// baralhadas, quer-se que duas medições do MESMO artigo caiam no mesmo
        /// grupo e as de artigos diferentes não. O separador é trocado por "|"
        /// para o Id não levar um caracter de controlo lá dentro.
        /// </summary>
        private static string ChaveDoArtigo(string artigo)
        {
            string n = ChaveArtigo.Normalizada(artigo ?? "") ?? "";
            return n.Replace(ChaveArtigo.Sep, "|").ToUpperInvariant();
        }

        /// <summary>"11.2.1 · Parede interior", ou só o código quando não há descrição.</summary>
        public static string TextoDoArtigo(string artigo)
        {
            string codigo = ChaveArtigo.Codigo(artigo ?? "").Trim();
            string descricao = ChaveArtigo.Designacao(artigo ?? "").Trim();
            if (codigo.Length == 0 && descricao.Length == 0) return "";
            if (descricao.Length == 0) return codigo;
            if (codigo.Length == 0) return descricao;
            return codigo + " · " + descricao;
        }
    }
}
