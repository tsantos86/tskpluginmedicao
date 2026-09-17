using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TSKTakeOff
{
    /// <summary>
    /// O que cada nó da árvore de resultados É.
    ///
    /// A hierarquia aprovada é Pavimentos > Piso > Serviço > Artigo > Medição >
    /// Vão/Título, e a profundidade de um nó decorre do tipo. Os valores são
    /// explícitos porque a indentação da grelha se calcula a partir deles: se
    /// alguém intercalar um nível novo no meio, a renumeração tem de ser uma
    /// decisão, não um efeito lateral de acrescentar uma linha ao enum.
    /// </summary>
    public enum TipoNo
    {
        Raiz = 0,
        Piso = 1,
        /// <summary>
        /// O TIPO DE MEDIDA: alvenaria, camadas, materiais, lineares,
        /// contagens.
        ///
        /// Não é uma etiqueta nem uma especialidade — mede-se arquitetura e
        /// mais nada. É COMO se mede, e é isso que determina a unidade de tudo
        /// o que tem por baixo: uma parede é comprimento × altura e dá m²; uma
        /// camada é área em planta × espessura e dá m³; num linear o percurso
        /// É a quantidade; numa contagem cada marca vale uma.
        ///
        /// Chamou-se Servico enquanto o painel tinha quatro abas e este nível
        /// só via uma delas. Com um painel só, o serviço passou a ser o que
        /// sempre foi — o que identifica a medição na folha — e vive nas
        /// propriedades.
        /// </summary>
        Tipo = 2,
        Artigo = 3,
        Medicao = 4,
        Vao = 5,
        Titulo = 6
    }

    /// <summary>
    /// Aquilo que numa medição precisa de ser visto sem se ir lá procurar.
    ///
    /// São bandeiras e não um valor único porque se acumulam: uma medição pode
    /// estar por classificar E ter vãos que excedem a parede. Escolher só uma
    /// para mostrar escondia a outra.
    /// </summary>
    [Flags]
    public enum AlertaNo
    {
        Nenhum = 0,
        /// <summary>Sem artigo do mapa: sai no fim da folha.</summary>
        PorClassificar = 1,
        /// <summary>Tem artigo escrito, mas o mapa importado não o conhece.</summary>
        ArtigoDesconhecido = 2,
        /// <summary>Os vãos descontam mais do que a parede tem.</summary>
        VaosExcessivos = 4
    }

    /// <summary>Estados filtráveis, na redação da Fase 4 do plano.</summary>
    [Flags]
    public enum EstadoResultado
    {
        Nenhum = 0,
        PorClassificar = 1,
        ComVaos = 2,
        ComAlerta = 4
    }

    /// <summary>
    /// Como se escreve uma unidade, e como se escreve uma quantidade nela.
    ///
    /// O modelo guarda a forma simples — "m2" — porque é essa que o mapa
    /// compara. Ao ecrã vai a forma composta, que é a que quem mede espera
    /// ler. Estava escrito à mão em cada sítio que mostrava um total.
    /// </summary>
    public static class Unidades
    {
        public const string M2 = "m2";
        public const string M3 = "m3";
        public const string Metro = "m";
        public const string Unidade = "un";

        /// <summary>"m2" -> "m²"; "un" -> "un.".</summary>
        public static string Escrita(string u)
        {
            if (u == M2) return "m²";
            if (u == M3) return "m³";
            if (u == Unidade) return "un.";
            return u ?? "";
        }

        /// <summary>
        /// A quantidade com a sua unidade: "12,67 m²", "2 un.".
        ///
        /// As unidades contam-se inteiras. Escrever "2,00 un." a uma pessoa que
        /// contou duas portas é dar-lhe uma casa decimal que não existe no que
        /// ela mediu.
        /// </summary>
        public static string Texto(string unidade, double valor, IFormatProvider cultura)
        {
            if (cultura == null) cultura = CultureInfo.CurrentCulture;
            string n = unidade == Unidade
                ? valor.ToString("N0", cultura)
                : valor.ToString("N2", cultura);
            string escrita = Escrita(unidade);
            return escrita.Length > 0 ? n + " " + escrita : n;
        }

        public static string Texto(string unidade, double valor)
        {
            return Texto(unidade, valor, CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// Totais separados por unidade — nunca um total só.
    ///
    /// É a mesma regra do <see cref="TotaisMedicao"/>, levada da barra do
    /// rodapé para dentro da árvore: um grupo que junte paredes (m²), camadas
    /// (m³) e rodapés (m) tem três totais, não um. Somá-los dava um número que
    /// não é nenhuma das três grandezas e que tem ar de estar bem.
    ///
    /// A ordem é a de chegada, e a de chegada é a ordem da folha: quem mede
    /// alvenaria vê m² à frente. Por nome, o m³ vinha sempre primeiro.
    /// </summary>
    public sealed class Quantidades
    {
        private readonly List<string> _ordem = new List<string>();
        private readonly Dictionary<string, double> _soma =
            new Dictionary<string, double>(StringComparer.Ordinal);

        public int Count { get { return _ordem.Count; } }
        public bool Vazio { get { return _ordem.Count == 0; } }

        /// <summary>As unidades pela ordem em que apareceram.</summary>
        public IList<string> UnidadesPresentes { get { return _ordem.AsReadOnly(); } }

        public void Somar(string unidade, double valor)
        {
            if (string.IsNullOrEmpty(unidade)) return;
            if (!_soma.ContainsKey(unidade))
            {
                _soma[unidade] = 0.0;
                _ordem.Add(unidade);
            }
            _soma[unidade] += valor;
        }

        /// <summary>Junta outro acumulador, preservando a ordem de chegada.</summary>
        public void Somar(Quantidades outras)
        {
            if (outras == null) return;
            foreach (var u in outras._ordem) Somar(u, outras._soma[u]);
        }

        /// <summary>O total nessa unidade; zero se a unidade não estiver presente.</summary>
        public double De(string unidade)
        {
            double v;
            return unidade != null && _soma.TryGetValue(unidade, out v) ? v : 0.0;
        }

        public bool Tem(string unidade)
        {
            return unidade != null && _soma.ContainsKey(unidade);
        }

        public List<KeyValuePair<string, double>> Lista()
        {
            var saida = new List<KeyValuePair<string, double>>();
            foreach (var u in _ordem)
                saida.Add(new KeyValuePair<string, double>(u, _soma[u]));
            return saida;
        }

        /// <summary>
        /// "62,93 m² · 36,90 m · 2 un." — lado a lado, em vez de somado.
        /// </summary>
        public string Texto(IFormatProvider cultura)
        {
            if (_ordem.Count == 0) return "";
            var sb = new StringBuilder();
            foreach (var u in _ordem)
            {
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(Unidades.Texto(u, _soma[u], cultura));
            }
            return sb.ToString();
        }

        public string Texto() { return Texto(CultureInfo.CurrentCulture); }

        public Quantidades Clonar()
        {
            var c = new Quantidades();
            c.Somar(this);
            return c;
        }
    }

    /// <summary>
    /// Uma linha do painel PROPRIEDADES.
    ///
    /// O <see cref="Campo"/> é o que a Fase 5 vai usar para saber que método do
    /// AlvRepo chamar: é um identificador estável, não o rótulo, que é texto
    /// para ler e pode mudar sem partir nada.
    /// </summary>
    public sealed class Propriedade
    {
        public string Campo { get; set; }
        public string Nome { get; set; }
        public string Valor { get; set; }
        /// <summary>Aparece no modo "Essenciais"; as restantes só em "Tudo".</summary>
        public bool Essencial { get; set; }
        /// <summary>Editável na Fase 5. Geometria e cálculos ficam a falso.</summary>
        public bool Editavel { get; set; }

        public Propriedade() { }

        public Propriedade(string campo, string nome, string valor,
                           bool essencial, bool editavel)
        {
            Campo = campo;
            Nome = nome;
            Valor = valor;
            Essencial = essencial;
            Editavel = editavel;
        }
    }

    /// <summary>
    /// Um vão, já reduzido ao que a árvore precisa de saber dele.
    ///
    /// Não é o <see cref="Vao"/>: aquele sabe serializar-se para a XData e
    /// calcular pré-aros; este é o que sobra depois de a regra de desconto ter
    /// sido aplicada — a dedução JÁ CALCULADA, com sinal, e a unidade em que
    /// ela foi descontada.
    /// </summary>
    public sealed class DeducaoResultado
    {
        public int Indice { get; set; }
        public string Designacao { get; set; }
        /// <summary>Valor JÁ descontado, positivo. A árvore mostra-o negativo.</summary>
        public double Desconto { get; set; }
        public string Unidade { get; set; }
        public List<Propriedade> Propriedades { get; set; }
        /// <summary>Tudo o que o vão tem de pesquisável (designação, tipo).</summary>
        public string TextoPesquisa { get; set; }
        public string Rotulo { get; set; }
    }

    /// <summary>Um título (capítulo/artigo) que sai por baixo de uma medição.</summary>
    public sealed class TituloResultado
    {
        public int Indice { get; set; }
        /// <summary>"CAP" ou "ART".</summary>
        public string Nivel { get; set; }
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public List<Propriedade> Propriedades { get; set; }
        public string Rotulo { get; set; }
        public string TextoPesquisa { get; set; }
    }

    /// <summary>
    /// Uma medição, na forma neutra de que a árvore vive.
    ///
    /// Existe para a árvore não saber o que é uma <see cref="Parede"/>. A
    /// Fase 6 tem de pendurar Materiais, Lineares e Contagens na mesma
    /// estrutura, e cada um desses tem o seu modelo próprio; se a árvore
    /// falasse Parede, seriam quatro árvores.
    ///
    /// Repare-se no par <see cref="Unidade"/>/<see cref="Quantidade"/>: é o
    /// ÚNICO sítio por onde um número entra num total. O comprimento de uma
    /// parede é uma dimensão e vive nas <see cref="Propriedades"/> — não tem
    /// como cair no balde dos metros de um rodapé, que é a quantidade dele.
    /// </summary>
    public sealed class MedicaoResultado
    {
        public string Handle { get; set; }
        public string Piso { get; set; }
        public string Servico { get; set; }
        /// <summary>
        /// O tipo de medida: Alvenaria, Camadas, Materiais, Lineares,
        /// Contagens. 00c9 o segundo n00edvel da 00e1rvore e o que determina a
        /// unidade 2014 ver <see cref="TipoNo.Tipo"/>.
        /// </summary>
        public string TipoMedida { get; set; }
        /// <summary>Chave do artigo, "código\x1fdescrição".</summary>
        public string Artigo { get; set; }
        public string Bloco { get; set; }
        public string Alcado { get; set; }
        public string Nota { get; set; }
        public string Layer { get; set; }

        /// <summary>Como a medição se chama na coluna "Estrutura / elemento".</summary>
        public string Rotulo { get; set; }

        /// <summary>A unidade que FATURA: "m2", "m3", "m" ou "un".</summary>
        public string Unidade { get; set; }
        /// <summary>A quantidade nessa unidade, já líquida de deduções.</summary>
        public double Quantidade { get; set; }

        /// <summary>
        /// Os dois factores da medição, como números — para as colunas
        /// «Comp.» e «Altura» da árvore.
        ///
        /// NaN quer dizer QUE NÃO SE APLICA, e a coluna mostra "—". Não é o
        /// mesmo que zero: um linear não tem altura nenhuma, e escrever lá
        /// "0,00 m" seria inventar uma dimensão que a medição não tem. Foi
        /// assim que uma contagem de duas portas apareceu debaixo de "Comp.".
        ///
        /// A unidade de cada factor viaja à parte, porque nem sempre é metro:
        /// numa camada o primeiro factor é uma ÁREA em planta (m²) e o segundo
        /// é a espessura (m) — e é o par que explica por que o resultado é m³.
        /// </summary>
        public double Comprimento { get; set; }
        public string UnidadeComprimento { get; set; }
        public double Altura { get; set; }
        public string UnidadeAltura { get; set; }

        public AlertaNo Alertas { get; set; }

        public List<DeducaoResultado> Deducoes { get; set; }
        public List<TituloResultado> Titulos { get; set; }
        public List<Propriedade> Propriedades { get; set; }

        public MedicaoResultado()
        {
            Deducoes = new List<DeducaoResultado>();
            Titulos = new List<TituloResultado>();
            Propriedades = new List<Propriedade>();
            // "Não se aplica" por omissão. Quem tem factores declara-os; quem
            // não tem — um linear, uma contagem — fica com o traço, e não com
            // um zero que parecia uma medida.
            Comprimento = double.NaN;
            Altura = double.NaN;
        }
    }

    /// <summary>
    /// Um nó da árvore de resultados.
    ///
    /// O <see cref="Id"/> é o que sobrevive a recolher, filtrar e reconstruir.
    /// NÃO é o índice da linha: a grelha é reconstruída a cada medição e os
    /// índices mudam todos: era assim que a selecção se perdia e o alvo dos
    /// botões caía silenciosamente na última medição.
    ///
    /// Um nó de grupo NÃO tem <see cref="Handle"/>, e é isso — e não um
    /// booleano à parte — que impede que ele seja removido, editado ou
    /// reclassificado: não há entidade nenhuma no desenho para onde apontar.
    /// </summary>
    public sealed class NoResultado
    {
        public string Id { get; set; }
        public TipoNo Tipo { get; set; }
        public string Rotulo { get; set; }

        /// <summary>Nulo em todos os nós de grupo.</summary>
        public string Handle { get; set; }
        /// <summary>Índice do vão ou do título dentro da medição; -1 nos outros.</summary>
        public int Indice { get; set; }

        public NoResultado Pai { get; set; }
        public List<NoResultado> Filhos { get; private set; }

        /// <summary>Contexto herdado, para o "Medir aqui" e para os filtros.</summary>
        public string Piso { get; set; }
        public string Servico { get; set; }
        /// <summary>
        /// O tipo de medida: Alvenaria, Camadas, Materiais, Lineares,
        /// Contagens. 00c9 o segundo n00edvel da 00e1rvore e o que determina a
        /// unidade 2014 ver <see cref="TipoNo.Tipo"/>.
        /// </summary>
        public string TipoMedida { get; set; }
        public string Artigo { get; set; }

        /// <summary>Totais completos, sem filtro nenhum aplicado.</summary>
        public Quantidades Quantidades { get; private set; }

        /// <summary>
        /// Os factores da medição, para as colunas «Comp.» e «Altura».
        /// NaN = não se aplica, e a coluna mostra "—".
        ///
        /// NUM GRUPO, o comprimento SOMA-SE e a altura NÃO.
        ///
        /// Somar comprimentos de paredes dá uma coisa que existe: o
        /// desenvolvimento total. Somar alturas não dá nada — três paredes de
        /// 2,80 m não fazem uma de 8,40. Por isso a altura de um grupo só
        /// aparece quando é a MESMA em todos os filhos, que é o caso corrente
        /// num piso; havendo mais do que uma, mostra o traço.
        /// </summary>
        public double Comprimento { get; set; }
        public string UnidadeComprimento { get; set; }
        public double Altura { get; set; }
        public string UnidadeAltura { get; set; }

        /// <summary>O factor formatado, ou "—" quando não se aplica.</summary>
        public string TextoComprimento(IFormatProvider cultura)
        {
            return double.IsNaN(Comprimento) ? "—"
                : Unidades.Texto(UnidadeComprimento ?? Unidades.Metro, Comprimento, cultura);
        }

        public string TextoAltura(IFormatProvider cultura)
        {
            return double.IsNaN(Altura) ? "—"
                : Unidades.Texto(UnidadeAltura ?? Unidades.Metro, Altura, cultura);
        }

        public AlertaNo Alertas { get; set; }

        /// <summary>
        /// Entra nos totais dos antepassados?
        ///
        /// Falso nos vãos e nos títulos. O vão JÁ foi descontado dentro da área
        /// líquida da parede — mostra-se por baixo para se ver de onde vem o
        /// desconto, mas somá-lo outra vez descontava-o duas vezes. O título
        /// não é quantidade nenhuma: é uma linha de texto que sai na folha.
        /// </summary>
        public bool ContaParaTotal { get; set; }

        /// <summary>
        /// Só os nós de Artigo. É a única operação que altera a próxima
        /// medição, e é preciso ser explícito — seleccionar não chega.
        /// </summary>
        public bool PermiteMedirAqui { get { return Tipo == TipoNo.Artigo; } }

        public List<Propriedade> Propriedades { get; set; }

        /// <summary>Texto já normalizado onde a pesquisa procura.</summary>
        public string TextoPesquisa { get; set; }

        /// <summary>Nível de indentação: decorre do tipo.</summary>
        public int Profundidade { get { return (int)Tipo; } }

        public bool EhGrupo
        {
            get
            {
                return Tipo == TipoNo.Raiz || Tipo == TipoNo.Piso
                    || Tipo == TipoNo.Tipo || Tipo == TipoNo.Artigo;
            }
        }

        public NoResultado()
        {
            Filhos = new List<NoResultado>();
            Quantidades = new Quantidades();
            Propriedades = new List<Propriedade>();
            Indice = -1;
            ContaParaTotal = true;
            Comprimento = double.NaN;
            Altura = double.NaN;
        }

        public void Acrescentar(NoResultado filho)
        {
            if (filho == null) return;
            filho.Pai = this;
            Filhos.Add(filho);
        }

        public override string ToString()
        {
            return Tipo + " " + Rotulo;
        }
    }

    /// <summary>
    /// Os filtros da Fase 4. Afectam a VISTA e mais nada: não tocam no DWG,
    /// na exportação nem no que vai para o Excel.
    ///
    /// Conjuntos vazios querem dizer "todos" — e não "nenhum". É a diferença
    /// entre um filtro por estrear e um filtro que esconde tudo.
    /// </summary>
    public sealed class FiltroResultados
    {
        public HashSet<string> Pavimentos { get; private set; }
        public HashSet<string> Servicos { get; private set; }
        /// <summary>Códigos de artigo, não a chave completa.</summary>
        public HashSet<string> Artigos { get; private set; }
        public HashSet<string> UnidadesFiltradas { get; private set; }
        public EstadoResultado Estados { get; set; }

        public FiltroResultados()
        {
            Pavimentos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Servicos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Artigos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            UnidadesFiltradas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Estados = EstadoResultado.Nenhum;
        }

        public bool Vazio
        {
            get
            {
                return Pavimentos.Count == 0 && Servicos.Count == 0
                    && Artigos.Count == 0 && UnidadesFiltradas.Count == 0
                    && Estados == EstadoResultado.Nenhum;
            }
        }

        /// <summary>
        /// Quantos filtros estão activos — o número do badge.
        /// Conta GRUPOS de filtro, não valores: escolher três pisos é um
        /// filtro (Pavimento), não três.
        /// </summary>
        public int Contagem
        {
            get
            {
                int n = 0;
                if (Pavimentos.Count > 0) n++;
                if (Servicos.Count > 0) n++;
                if (Artigos.Count > 0) n++;
                if (UnidadesFiltradas.Count > 0) n++;
                if (Estados != EstadoResultado.Nenhum) n++;
                return n;
            }
        }

        /// <summary>
        /// Os rótulos dos filtros activos, para os chips da barra.
        /// A ordem é a da interface, não a de utilização, para os chips não
        /// saltarem de sítio entre actualizações.
        /// </summary>
        public List<string> Chips()
        {
            var chips = new List<string>();
            if (Pavimentos.Count > 0) chips.Add(Chip("Pavimento", Pavimentos));
            if (Servicos.Count > 0) chips.Add(Chip("Serviço", Servicos));
            if (Artigos.Count > 0) chips.Add(Chip("Artigo", Artigos));
            if (UnidadesFiltradas.Count > 0) chips.Add(Chip("Tipo", UnidadesFiltradas));
            if (Estados != EstadoResultado.Nenhum)
            {
                var estados = new List<string>();
                if ((Estados & EstadoResultado.PorClassificar) != 0) estados.Add("Por classificar");
                if ((Estados & EstadoResultado.ComVaos) != 0) estados.Add("Com vãos");
                if ((Estados & EstadoResultado.ComAlerta) != 0) estados.Add("Com alerta");
                chips.Add(estados.Count == 1
                    ? "Estado: " + estados[0]
                    : "Estado: " + estados.Count + " valores");
            }
            return chips;
        }

        private static string Chip(string nome, HashSet<string> valores)
        {
            if (valores.Count == 1)
            {
                foreach (var v in valores) return nome + ": " + v;
            }
            return nome + ": " + valores.Count + " valores";
        }

        /// <summary>
        /// Cópia independente. É o que permite o rascunho da Fase 4: mexe-se
        /// na cópia e só o "Aplicar" a promove a filtro em vigor.
        /// </summary>
        public FiltroResultados Clonar()
        {
            var c = new FiltroResultados();
            foreach (var v in Pavimentos) c.Pavimentos.Add(v);
            foreach (var v in Servicos) c.Servicos.Add(v);
            foreach (var v in Artigos) c.Artigos.Add(v);
            foreach (var v in UnidadesFiltradas) c.UnidadesFiltradas.Add(v);
            c.Estados = Estados;
            return c;
        }

        public void Limpar()
        {
            Pavimentos.Clear();
            Servicos.Clear();
            Artigos.Clear();
            UnidadesFiltradas.Clear();
            Estados = EstadoResultado.Nenhum;
        }
    }

    /// <summary>
    /// O que a pessoa tem aberto, escrito e escolhido — e que sobrevive à
    /// reconstrução da árvore.
    ///
    /// Guarda-se o conjunto dos RECOLHIDOS, não o dos expandidos: a árvore
    /// nasce aberta, e um nó novo — a medição que se acabou de fazer — tem de
    /// nascer visível sem ninguém ter de o registar em lado nenhum.
    /// </summary>
    public sealed class EstadoVista
    {
        public string Pesquisa { get; set; }
        public FiltroResultados Filtro { get; set; }
        public HashSet<string> Recolhidos { get; private set; }
        /// <summary>Id do nó seleccionado, se ainda existir e estiver visível.</summary>
        public string Seleccionado { get; set; }

        public EstadoVista()
        {
            Pesquisa = "";
            Filtro = new FiltroResultados();
            Recolhidos = new HashSet<string>(StringComparer.Ordinal);
        }

        public bool TemPesquisa
        {
            get { return !string.IsNullOrEmpty((Pesquisa ?? "").Trim()); }
        }

        /// <summary>Há pesquisa ou filtros a esconder alguma coisa?</summary>
        public bool AFiltrar
        {
            get { return TemPesquisa || !Filtro.Vazio; }
        }

        public bool Expandido(string id)
        {
            return id == null || !Recolhidos.Contains(id);
        }

        public void Alternar(string id)
        {
            if (id == null) return;
            if (!Recolhidos.Remove(id)) Recolhidos.Add(id);
        }

        public void Recolher(string id)
        {
            if (id != null) Recolhidos.Add(id);
        }

        public void Expandir(string id)
        {
            if (id != null) Recolhidos.Remove(id);
        }

        /// <summary>
        /// Limpa pesquisa e filtros, e SÓ isso.
        ///
        /// As recolhas ficam onde estavam de propósito: a vista a que se volta
        /// tem de ser a de antes de pesquisar, não uma árvore toda aberta que
        /// a pessoa nunca pediu.
        /// </summary>
        public void LimparVista()
        {
            Pesquisa = "";
            Filtro.Limpar();
        }
    }

    /// <summary>Um nó tal como aparece na grelha, depois de filtrado.</summary>
    public sealed class NoVisivel
    {
        public NoResultado No { get; set; }
        /// <summary>
        /// Totais calculados só sobre os filhos VISÍVEIS. Num grupo filtrado,
        /// o total que se lê tem de ser o dos itens que estão à vista — senão
        /// a soma das linhas não bate com o cabeçalho delas.
        /// </summary>
        public Quantidades Quantidades { get; set; }
        public bool TemFilhos { get; set; }
        public bool Expandido { get; set; }
        /// <summary>Correspondeu à pesquisa (para destacar), ou só veio pelo parentesco.</summary>
        public bool Correspondeu { get; set; }

        public string Id { get { return No == null ? null : No.Id; } }
        public int Profundidade { get { return No == null ? 0 : No.Profundidade; } }
    }

    /// <summary>
    /// O resultado achatado: as linhas a desenhar, os totais e a contagem.
    /// </summary>
    public sealed class VistaResultados
    {
        public List<NoVisivel> Nos { get; private set; }
        /// <summary>Totais dos que estão à vista.</summary>
        public Quantidades TotaisVisiveis { get; private set; }
        /// <summary>Totais de tudo, mesmo do que o filtro esconde.</summary>
        public Quantidades TotaisGlobais { get; private set; }
        public int MedicoesVisiveis { get; set; }
        public int MedicoesTotais { get; set; }
        public bool AFiltrar { get; set; }

        public VistaResultados()
        {
            Nos = new List<NoVisivel>();
            TotaisVisiveis = new Quantidades();
            TotaisGlobais = new Quantidades();
        }

        /// <summary>Não há nada para mostrar (com ou sem filtro).</summary>
        public bool Vazia { get { return Nos.Count == 0; } }

        /// <summary>
        /// "12 visíveis de 47" — ou só "47 medições" quando nada está filtrado.
        /// O número é sempre calculado; não há texto fixo herdado do mockup.
        /// </summary>
        public string Resumo(IFormatProvider cultura)
        {
            if (cultura == null) cultura = CultureInfo.CurrentCulture;
            if (!AFiltrar || MedicoesVisiveis == MedicoesTotais)
                return MedicoesTotais == 1 ? "1 medição" :
                    MedicoesTotais.ToString("N0", cultura) + " medições";

            return MedicoesVisiveis.ToString("N0", cultura) + " visíveis de "
                 + MedicoesTotais.ToString("N0", cultura);
        }

        public string Resumo() { return Resumo(CultureInfo.CurrentCulture); }

        /// <summary>O nó visível com este Id, ou nulo se o filtro o escondeu.</summary>
        public NoVisivel Procurar(string id)
        {
            if (id == null) return null;
            foreach (var n in Nos)
                if (string.Equals(n.Id, id, StringComparison.Ordinal)) return n;
            return null;
        }

        /// <summary>A posição da linha desse nó, ou -1. Para repor o scroll.</summary>
        public int Indice(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < Nos.Count; i++)
                if (string.Equals(Nos[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
