using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TSKTakeOff
{
    /// <summary>Tipo de linha da folha de medição.</summary>
    public enum TipoLinha
    {
        Capitulo,     // ETIC'S Fachada  | m2 | ... | Totais
        Alcado,       // Alçado Tardoz   | ...      | Sub total
        Piso,         // Piso+0
        Medicao,      // Letra A   m2  1  35.53   2.44  =D*E*G
        Deducao,      // VE.10     m2 -3   3.77   2.10  =D*E*G   (vermelho)
        Vazia,

        // Títulos inseridos à mão pelo utilizador, a partir dos botões da
        // paleta. Saem vazios, com o estilo e a altura do modelo, prontos
        // para lá escrever o texto. Fecham os somatórios: um título novo
        // começa um bloco novo.
        TituloCapitulo,
        TituloArtigo
    }

    /// <summary>Uma linha da folha, independente de como é escrita (ClosedXML ou COM).</summary>
    public class LinhaFolha
    {
        public TipoLinha Tipo;
        public string Item;            // A001, A002…
        public string Designacao;
        public string Un;
        public double? Qt;
        public double? Comp;
        public double? Largura;
        public double? Altura;
        public bool TemParcial;        // escreve a fórmula em H
        public string FormulaSubTotal; // I — só nas linhas de alçado
        public string FormulaTotais;   // J — só na linha de capítulo

        /// <summary>
        /// Valor a escrever directamente na coluna Parcial, em vez de fórmula.
        /// Usado nas contagens: não têm comprimento nem altura, e a fórmula do
        /// modelo (comp × larg × alt) daria zero com essas células vazias.
        /// </summary>
        public double? ValorParcial;

        /// <summary>
        /// Handle da medição que gerou esta linha. Só preenchido nas linhas de
        /// título — é por aqui que o texto escrito no Excel volta à medição
        /// certa antes de a folha ser reescrita.
        /// </summary>
        public string HandleOrigem;

        /// <summary>Handle da medição desta linha (nas linhas de medição).</summary>
        public string Handle;

        /// <summary>
        /// Verdadeiro quando este título é o cabeçalho de um artigo, e não um
        /// título solto posto à mão. Muda o destino do que lá for escrito: um
        /// cabeçalho de artigo pertence a todas as medições desse artigo.
        /// </summary>
        public bool DeArtigo;
    }

    /// <summary>
    /// Constrói a folha de medição no formato da casa:
    /// Capítulo → Alçado (Sub total) → Piso → medições, com linha em branco
    /// entre pisos e numeração sequencial A001… em todas as linhas.
    /// Usado tanto pelo ficheiro exportado como pelo Excel ao vivo, para
    /// os dois nunca divergirem.
    /// </summary>
    public static class FolhaMedicao
    {
        public const int C_ITEM = 1, C_DESC = 2, C_UN = 3, C_QT = 4,
                         C_COMP = 5, C_LARG = 6, C_ALT = 7,
                         C_PARC = 8, C_SUB = 9, C_TOT = 10;

        public static readonly string[] Cabecalho =
        {
            "Item", "Designação", "Un", "Qt", "Comp / Area", "Largura", "Altura",
            "Parcial", "Sub total", "Totais"
        };

        /// <summary>Parcial = Qt × Comp × (Largura) × Altura, só com as colunas preenchidas.</summary>
        public static string FormulaParcial(int linha, bool temLargura, bool temAltura)
        {
            var partes = new List<string> { "D" + linha, "E" + linha };
            if (temLargura) partes.Add("F" + linha);
            if (temAltura) partes.Add("G" + linha);
            return string.Join("*", partes);
        }

        // ==================================================================
        public static List<LinhaFolha> Construir(IList<Parede> paredes,
            IList<MedFachada> fachadas, IList<MedItem> lineares, RegraDesconto regra)
        {
            return Construir(paredes, fachadas, lineares, Contagens, regra, 2);
        }

        /// <summary>Contagens actuais, injectadas pelo Excel ao vivo antes de construir.</summary>
        public static IList<MedContagem> Contagens = new List<MedContagem>();

        /// <summary>
        /// Como o outro <c>Construir</c>, mas permitindo escrever a partir de
        /// uma linha diferente da 2 — usado quando o destino é uma folha que já
        /// traz um cabeçalho de projecto próprio acima da tabela.
        /// </summary>
        public static List<LinhaFolha> Construir(IList<Parede> paredes,
            IList<MedFachada> fachadas, IList<MedItem> lineares, RegraDesconto regra,
            int primeiraLinhaExcel)
        {
            return Construir(paredes, fachadas, lineares, Contagens, regra, primeiraLinhaExcel);
        }

        /// <summary>Constrói a folha com uma coleção explícita de contagens.</summary>
        public static List<LinhaFolha> Construir(IList<Parede> paredes,
            IList<MedFachada> fachadas, IList<MedItem> lineares,
            IList<MedContagem> contagens, RegraDesconto regra, int primeiraLinhaExcel)
        {
            var linhas = new List<LinhaFolha>();

            // A ALVENARIA PRIMEIRO. Os materiais saíam à frente de tudo, e um
            // pano medido agora aparecia no topo da folha, por cima de dezenas
            // de linhas de alvenaria que já lá estavam — parecia que a medição
            // se tinha enfiado no sítio errado.
            if (paredes != null && paredes.Count > 0)
                Capitulo(linhas, AgruparParedes(paredes, regra), "m2");

            if (fachadas != null && fachadas.Count > 0)
                Capitulo(linhas, AgruparFachadas(fachadas), "m2");

            if (lineares != null && lineares.Count > 0)
                Capitulo(linhas, AgruparLineares(lineares), "m");

            if (contagens != null && contagens.Count > 0)
                Capitulo(linhas, AgruparContagens(contagens), "un");

            Numerar(linhas, primeiraLinhaExcel);
            return linhas;
        }

        // ------------------------------------------------------------------
        // Estruturas intermédias
        // ------------------------------------------------------------------
        private class Bloco
        {
            public string Nome;                       // capítulo
            public string Unidade;

            /// <summary>
            /// Artigo do mapa a que este bloco pertence, como
            /// "código + 0x1F + descrição". Vazio quando não há mapa.
            ///
            /// Sobe até aqui porque o artigo é o nível de TOPO da folha: uma
            /// obra tem o mesmo artigo medido no piso 0, no piso 1 e em vários
            /// alçados, e o que se entrega é o artigo com tudo isso por baixo.
            /// Com o artigo lá dentro, ao pé das medições, o mesmo artigo
            /// repetia-se em cada piso e a folha deixava de se ler.
            /// </summary>
            public string Artigo = "";

            /// <summary>Handle de uma medição do bloco — o título precisa de um.</summary>
            public string HandleArtigo = "";

            public List<BlocoAlcado> Alcados = new List<BlocoAlcado>();
        }

        private class BlocoAlcado
        {
            public string Nome;
            public List<BlocoPiso> Pisos = new List<BlocoPiso>();
        }

        private class BlocoPiso
        {
            public string Nome;
            public List<LinhaFolha> Linhas = new List<LinhaFolha>();
        }

        private static void Capitulo(List<LinhaFolha> saida, List<Bloco> blocos, string un)
        {
            // O artigo em que vamos. Só muda de artigo faz sair título novo —
            // dois serviços do mesmo artigo ficam por baixo do mesmo cabeçalho.
            string artigoActual = null;

            foreach (var b in blocos)
            {
                string artigo = b.Artigo ?? "";

                // Um bloco SEM artigo é sempre um bloco novo: não há artigo que
                // o agrupe com o anterior, e sem isto os serviços de um desenho
                // sem mapa saíam todos colados uns aos outros.
                bool novoArtigo = artigo.Length == 0 || artigo != artigoActual;

                // A LINHA EM BRANCO SÓ QUANDO MUDA DE ARTIGO.
                //
                // É o artigo que estrutura a folha entregue: por baixo dele vem
                // tudo o que lhe pertence, e é entre artigos que se quer o
                // espaço. Chegou a sair por cima de cada serviço, e num artigo
                // com dois serviços — o RV.06 e o REV.06, ambos do 2.6 —
                // partia ao meio um bloco que é um só.
                //
                // O saida.Count trava a primeira de todas: abrir a folha com
                // uma linha morta.
                if (saida.Count > 0 && novoArtigo)
                    saida.Add(new LinhaFolha { Tipo = TipoLinha.Vazia });

                // O ARTIGO PRIMEIRO, e os serviços e os pisos por baixo dele.
                if (artigo.Length > 0 && artigo != artigoActual)
                    Titulo(saida, "ART", artigo, b.HandleArtigo, true);

                artigoActual = artigo;

                var cap = new LinhaFolha
                {
                    Tipo = TipoLinha.Capitulo,
                    Designacao = b.Nome,
                    Un = b.Unidade ?? un
                };
                saida.Add(cap);

                var linhasAlcado = new List<LinhaFolha>();

                foreach (var a in b.Alcados)
                {
                    // Sem alçado preenchido não se inventa uma linha "Geral":
                    // era só ruído numa folha que já tem o capítulo por cima.
                    // Com um único alçado sem nome, o sub-total coincidiria com
                    // o total do capítulo, por isso também não faz falta.
                    if (!string.IsNullOrWhiteSpace(a.Nome))
                    {
                        var lin = new LinhaFolha
                        {
                            Tipo = TipoLinha.Alcado,
                            Designacao = a.Nome
                        };
                        saida.Add(lin);
                        linhasAlcado.Add(lin);

                        // Sem linha em branco a seguir ao alçado. Ela ficava
                        // entre um cabeçalho e o seu próprio sub-cabeçalho — o
                        // alçado e o piso que lhe pertence — e não separava
                        // nada: o piso já é uma linha destacada. Quem quiser
                        // espaço ali põe-no com o botão «Linha branca», que é
                        // o único sítio onde o espaçamento se decide.
                    }

                    foreach (var piso in a.Pisos)
                    {
                        if (!string.IsNullOrWhiteSpace(piso.Nome))
                        {
                            var linPiso = new LinhaFolha
                            {
                                Tipo = TipoLinha.Piso,
                                Designacao = piso.Nome
                            };
                            saida.Add(linPiso);

                            // O piso também soma o que é seu. Sem isto, uma folha
                            // sem alçados ficava sem sub-totais nenhuns — era o
                            // alçado que os carregava.
                            linPiso.FormulaSubTotal = "@ALC";
                            linhasAlcado.Add(linPiso);
                        }

                        // E também não sai linha em branco no fim do piso. O
                        // que vem a seguir é sempre um cabeçalho — outro piso,
                        // outro alçado, outro serviço ou outro artigo — e todos
                        // eles já se destacam sozinhos. As únicas linhas em
                        // branco que restam são as que alguém pediu: o
                        // separador da medição e a que separa artigos.
                        foreach (var l in piso.Linhas) saida.Add(l);
                    }
                }

                // marca para preencher as fórmulas depois de saber as linhas reais
                cap.FormulaTotais = "@CAP";

                // O SUB TOTAL TEM SEMPRE DE APARECER, num sítio ou noutro.
                //
                // Ele vive nas linhas de alçado e de piso, que são as divisões
                // de um serviço. Mas um serviço pode não ter divisão nenhuma —
                // e tem-nas mesmo: um RV.06 medido de uma ponta à outra, sem
                // piso, saía com a coluna Sub total inteiramente vazia. Ficava
                // uma coluna do modelo da casa em branco na folha entregue, sem
                // nada que explicasse porquê.
                //
                // Sem divisões, quem soma é o próprio serviço. Com elas, são
                // elas — repetir o mesmo número nos dois sítios só levantava a
                // dúvida de saber qual dos dois manda.
                if (linhasAlcado.Count == 0)
                    cap.FormulaSubTotal = "@ALC";
                else
                    foreach (var la in linhasAlcado) la.FormulaSubTotal = "@ALC";
            }
        }

        /// <summary>
        /// As medições pela ordem em que saem na folha.
        ///
        /// A grelha da paleta mostrava-as por ordem de criação no desenho, e a
        /// folha mostra-as agrupadas — as duas discordavam, e "a última
        /// medição" queria dizer coisas diferentes em cada lado. Passa a haver
        /// uma ordem só, definida aqui, e a grelha lê-a daqui.
        ///
        /// Tem de usar exactamente a mesma cadeia que o <c>AgruparParedes</c>;
        /// há um teste que compara as duas para garantir que não divergem.
        /// </summary>
        public static List<Parede> OrdenarComoFolha(IList<Parede> paredes)
        {
            var saida = new List<Parede>();
            if (paredes == null) return saida;

            // Tem de ser a MESMA cadeia do AgruparParedes, artigo à cabeça.
            // Se divergirem, a grelha da paleta e a folha deixam de concordar
            // sobre qual é "a última medição", e os botões passam a agir sobre
            // outra linha que não a que está seleccionada.
            foreach (var gArtigo in paredes.GroupBy(p => ChaveArtigo(p))
                        .OrderBy(g => OrdemArtigo(g.First()))
                        .ThenBy(g => g.Min(p => Ordem(p))))
                foreach (var gServico in gArtigo.GroupBy(p => p.Servico)
                            .OrderBy(g => g.Min(p => Ordem(p))).ThenBy(g => g.Key))
                    foreach (var gAlcado in gServico.GroupBy(p => p.Alcado ?? "")
                                .OrderBy(g => g.Min(p => Ordem(p))).ThenBy(g => g.Key))
                        foreach (var gPiso in gAlcado.GroupBy(p => p.Piso)
                                    .OrderBy(g => g.Min(x => Ordem(x))).ThenBy(g => g.Key))
                            foreach (var p in gPiso.OrderBy(x => Ordem(x)))
                                saida.Add(p);

            return saida;
        }

        /// <summary>
        /// O mesmo, para os panos da aba Materiais.
        ///
        /// Tem de ser a MESMA cadeia do <see cref="AgruparFachadas"/>, pela
        /// mesma razão: sem isto o Nº da grelha dos Materiais não coincide com
        /// a linha do Excel, e um pano medido agora aparece no fim da grelha
        /// mas no meio da folha.
        /// </summary>
        public static List<MedFachada> OrdenarComoFolha(IList<MedFachada> fachadas)
        {
            var saida = new List<MedFachada>();
            if (fachadas == null) return saida;

            foreach (var gArtigo in fachadas.GroupBy(f => ChaveArtigo(f))
                        .OrderBy(g => OrdemArtigo(g.First())))
                foreach (var gMat in gArtigo.GroupBy(f => f.Material).OrderBy(g => g.Key))
                    foreach (var gAlcado in gMat.GroupBy(f => f.Alcado ?? "").OrderBy(g => g.Key))
                        foreach (var gPiso in gAlcado.GroupBy(f => f.Piso).OrderBy(g => g.Key))
                            foreach (var f in gPiso)
                                saida.Add(f);

            return saida;
        }

        /// <summary>
        /// Chave de ordenação de uma medição.
        ///
        /// Ordem 0 significa "sem ordem de origem" — é o que têm todas as
        /// medições feitas no desenho. Ordenar por 0 mandava-as para ANTES de
        /// tudo o que veio importado, e uma parede medida agora aparecia no
        /// topo da folha, à frente de artigos que já lá estavam. Vão para o
        /// fim, que é onde se espera encontrar o que se acabou de medir; entre
        /// elas o OrderBy é estável, portanto mantêm a ordem por que se mediram.
        /// </summary>
        private static int Ordem(Parede p)
        {
            return p.Ordem == 0 ? int.MaxValue : p.Ordem;
        }

        /// <summary>
        /// Posição do artigo desta medição no mapa de quantidades do cliente.
        ///
        /// É a chave de ordenação de topo: uma medição entrega-se pela ordem do
        /// articulado, não pela ordem por que se andou a medir na planta. Quem
        /// confere a folha segue-a de cima a baixo contra o mapa que enviou.
        ///
        /// Sem mapa importado — ou para os artigos escritos à mão que o mapa
        /// não conhece — devolve <c>int.MaxValue</c>. Ficando todos iguais, o
        /// desempate cai no <see cref="Ordem"/> de sempre e o resultado é
        /// exactamente o de antes de isto existir. É essa a garantia de que os
        /// desenhos já feitos não mudam de forma.
        ///
        /// Onde uma medição sem artigo vai parar: ao FIM DO SEU BLOCO de
        /// serviço/alçado/piso, não ao fim da folha. O agrupamento por serviço
        /// continua a ser o de topo — só a ordem dentro dele e entre blocos é
        /// que passou a seguir o articulado. Levá-la até ao fim da folha
        /// obrigava a refazer o <c>AgruparParedes</c>, e uma medição arrancada
        /// do seu serviço é mais difícil de encontrar do que uma que ficou
        /// onde faz sentido. O TSKMQT diz quais são, para se poderem corrigir.
        /// </summary>
        /// <summary>
        /// Onde um artigo fica no articulado. Ligado ao
        /// <c>MapaQuantidades.OrdemDe</c> no arranque do plugin.
        ///
        /// É um ponto de ligação e não uma chamada directa porque o mapa vive
        /// dentro do DWG, e a folha — que é aritmética e arrumação — deixava
        /// de se poder testar sem o AutoCAD por causa desta única linha. Como
        /// os erros da folha não rebentam (saem num ficheiro que vai para o
        /// cliente com ar de estar bem), poder prová-la vale a indirecção.
        ///
        /// Por omissão devolve <c>int.MaxValue</c> para tudo: sem mapa ligado,
        /// nenhum artigo tem lugar no articulado e a folha comporta-se como
        /// antes de o TSKMQT existir. O verificar.py confirma que a ligação
        /// está escrita, para ninguém a perder numa limpeza.
        /// </summary>
        public static Func<string, int> OrdemDoArtigo;

        /// <summary>
        /// A chave que o mapa reconhece para um artigo. Ligada ao
        /// <c>MapaQuantidades.ChaveCanonica</c> no arranque do plugin.
        ///
        /// A folha agrupa por artigo, e o agrupamento tem de ser feito sobre
        /// esta chave e não sobre a que cada medição traz: duas medições do
        /// mesmo artigo podem tê-la guardada de formas diferentes, e em cru
        /// davam dois blocos com o mesmo cabeçalho repetido.
        /// </summary>
        public static Func<string, string> ArtigoCanonico;

        /// <summary>Por que artigo esta medição é agrupada.</summary>
        private static string ChaveArtigo(Parede p)
        {
            // Nome próprio e não "a": este ficheiro já usa "a" para o alçado,
            // uns ciclos abaixo, e dois "a" de tipos diferentes no mesmo sítio
            // é como se lê mal um agrupamento.
            string chave = (p == null ? "" : p.Artigo) ?? "";
            if (chave.Length == 0) return "";
            var f = ArtigoCanonico;
            return f == null ? chave : (f(chave) ?? chave);
        }

        private static int OrdemArtigo(Parede p)
        {
            if (p == null || string.IsNullOrEmpty(p.Artigo)) return int.MaxValue;
            var f = OrdemDoArtigo;
            return f == null ? int.MaxValue : f(p.Artigo);
        }

        /// <summary>
        /// O mesmo que <see cref="ChaveArtigo(Parede)"/>, para os panos da aba
        /// Materiais. Duas sobrecargas em vez de uma genérica porque a Parede e
        /// a MedFachada não partilham base — e uma interface só para isto era
        /// mais peça do que problema.
        /// </summary>
        private static string ChaveArtigo(MedFachada f)
        {
            string chave = (f == null ? "" : f.Artigo) ?? "";
            if (chave.Length == 0) return "";
            var fn = ArtigoCanonico;
            return fn == null ? chave : (fn(chave) ?? chave);
        }

        private static int OrdemArtigo(MedFachada f)
        {
            if (f == null || string.IsNullOrEmpty(f.Artigo)) return int.MaxValue;
            var fn = OrdemDoArtigo;
            return fn == null ? int.MaxValue : fn(f.Artigo);
        }

        // ------------------------------------------------------------------
        private static List<Bloco> AgruparParedes(IList<Parede> paredes, RegraDesconto regra)
        {
            var blocos = new List<Bloco>();
            // Ordena primeiro pela ordem de origem, só depois por nome. Em tudo
            // o que é medido no desenho a ordem é 0 em todas as medições, o
            // OrderBy é estável e fica exactamente como estava. No que vem
            // importado, é isto que impede a folha de ser baralhada por ordem
            // alfabética — uma obra não se lê por ordem alfabética.
            // O ARTIGO é o nível de topo. Dentro dele vem o serviço, o alçado
            // e o piso — que é como uma medição se entrega: o artigo do mapa
            // do cliente uma vez, e por baixo dele todos os sítios onde foi
            // medido. Estava ao contrário, e o mesmo artigo repetia-se em cada
            // piso e em cada alçado.
            foreach (var gArtigo in paredes.GroupBy(p => ChaveArtigo(p))
                        .OrderBy(g => OrdemArtigo(g.First()))
                        .ThenBy(g => g.Min(p => Ordem(p))))
            foreach (var gServico in gArtigo.GroupBy(p => p.Servico)
                        .OrderBy(g => g.Min(p => Ordem(p))).ThenBy(g => g.Key))
            {
                var b = new Bloco
                {
                    Nome = gServico.Key,
                    // Um bloco só é m³ quando TODAS as suas medições são área ×
                    // altura. Misturado, fica m² — que é o que a esmagadora
                    // maioria das linhas é, e o total misturado é um erro de
                    // quem mediu, não algo para o cabeçalho disfarçar.
                    Unidade = gServico.All(x => x.AreaVezesAltura) ? "m3" : "m2",
                    Artigo = gArtigo.Key,
                    HandleArtigo = gServico.First().Handle
                };
                // O BLOCO JÁ NÃO AGRUPA.
                //
                // Ele entrava aqui pela chave "bloco · alçado" e era isso que
                // fazia sair uma linha-cabeçalho por bloco. Mas o campo passou
                // a servir para etiquetar o compartimento — "WC1", "quarto 2" —
                // e um cabeçalho por compartimento enche a folha de títulos com
                // uma medição cada. O texto vai agora para a designação da
                // própria medição, semeado no momento de medir (ver os sítios
                // onde o Config.Bloco é lido, em Commands.cs e MedirSeleccao.cs).
                //
                // Quem usava o Bloco como torre/fracção perde o agrupamento por
                // torre: essas medições passam a sair pela ordem do alçado e do
                // piso, com a torre escrita na designação de cada linha.
                foreach (var gAlcado in gServico.GroupBy(p => p.Alcado ?? "")
                            .OrderBy(g => g.Min(p => OrdemArtigo(p)))
                            .ThenBy(g => g.Min(p => Ordem(p))).ThenBy(g => g.Key))
                {
                    var a = new BlocoAlcado { Nome = gAlcado.Key };
                    foreach (var gPiso in gAlcado.GroupBy(p => p.Piso)
                                .OrderBy(g => g.Min(x => OrdemArtigo(x)))
                                .ThenBy(g => g.Min(x => Ordem(x))).ThenBy(g => g.Key))
                    {
                        var bp = new BlocoPiso { Nome = gPiso.Key };
                        // (sem numeração: as medições saem sem designação)
                        // A ordem do articulado tem de ser a mesma que o
                        // OrdenarComoFolha usa, senão a grelha da paleta e a
                        // folha discordam e "a última medição" passa a querer
                        // dizer coisas diferentes nos dois lados.
                        foreach (var p in gPiso.OrderBy(x => OrdemArtigo(x))
                                              .ThenBy(x => Ordem(x)))
                        {
                            // A linha em branco primeiro: ela separa do bloco
                            // anterior, portanto tem de ficar POR CIMA do
                            // cabeçalho do artigo, não entre ele e as medições.
                            if (p.Separador) bp.Linhas.Add(new LinhaFolha { Tipo = TipoLinha.Vazia });

                            // O cabeçalho do artigo já NÃO sai aqui: subiu para
                            // o topo do bloco, no Capitulo(). Aqui dentro ele
                            // repetia-se a cada piso e a cada alçado do mesmo
                            // artigo, e a folha ficava ilegível numa obra com
                            // cinco pisos.

                            bp.Linhas.Add(new LinhaFolha
                            {
                                Tipo = TipoLinha.Medicao,
                                Handle = p.Handle,
                                // Sem "parede 1", "parede 2": a parede já está
                                // identificada pelo artigo ou sub-artigo por
                                // cima. Só os vãos precisam de nome próprio —
                                // e o que a pessoa tenha escrito aqui à mão.
                                //
                                // O bloco entra quando a nota está vazia, e é o
                                // que salva as medições ANTIGAS: elas têm o
                                // bloco gravado no DWG e a nota por escrever, e
                                // como o bloco deixou de fazer cabeçalho
                                // sairiam agora sem identificação nenhuma.
                                //
                                // Não acumula: só preenche o que está vazio. Se
                                // a pessoa escrever na célula, essa passa a ser
                                // a nota e é ela que manda daí em diante.
                                Designacao = NotaInicial(p.Nota, p.Bloco),
                                // Área × altura dá volume. Escrever m² num
                                // número que são m³ é o tipo de engano que só
                                // se apanha na obra, com o material comprado.
                                Un = p.AreaVezesAltura ? "m3" : "m2",
                                Qt = 1,
                                // O comprimento da parede INTEIRA. Quando a
                                // geometria vem interrompida pelos vãos, é aqui
                                // que eles são repostos — a folha mostra a
                                // parede a direito e desconta por baixo, como
                                // quem mede à mão.
                                Comp = p.ComprimentoTotal,
                                Largura = p.Largura > 0 ? p.Largura : (double?)null,
                                // Numa medição por área, a altura interna é 1 e
                                // não tem significado nenhum para quem lê.
                                Altura = p.SoArea ? (double?)null : p.Altura,
                                TemParcial = true
                            });

                            foreach (var v in p.Vaos)
                            {
                                string nomeVao = string.IsNullOrWhiteSpace(v.Designacao)
                                    ? (v.Tipo == TipoVao.Janela ? "janela" : "porta")
                                    : v.Designacao;

                                if (v.Desconto(regra) <= 0) continue;
                                bp.Linhas.Add(new LinhaFolha
                                {
                                    Tipo = TipoLinha.Deducao,
                                    Designacao = nomeVao,
                                    Un = "m2",
                                    Qt = -v.Quantidade,
                                    Comp = v.Largura,
                                    Altura = v.Altura,
                                    TemParcial = true
                                });
                            }

                            // Título pedido pelo utilizador: sai DEPOIS desta
                            // medição e dos seus vãos. A folha cresce para
                            // baixo, por isso o título abre o bloco seguinte.
                            // Títulos desta medição, por ordem: capítulo e artigo. O índice
                            // viaja no handle para o texto escrito no Excel voltar
                            // ao título certo quando são vários.
                            var marcas = p.Marcas;
                            for (int mi = 0; mi < marcas.Count; mi++)
                                Titulo(bp.Linhas, marcas[mi], p.TextoDaMarca(mi),
                                       p.Handle + "#" + mi);

                            // E as linhas em branco fecham o bloco, por baixo
                            // do título. Saem depois de tudo o que é desta
                            // medição. São quantas o utilizador pediu — o botão
                            // soma de cada vez que se carrega nele.
                            for (int i = 0; i < p.LinhasEmBrancoDepois; i++)
                                bp.Linhas.Add(new LinhaFolha { Tipo = TipoLinha.Vazia });
                        }
                        a.Pisos.Add(bp);
                    }
                    b.Alcados.Add(a);
                }
                blocos.Add(b);
            }
            return blocos;
        }

        /// <summary>
        /// Acrescenta a linha de título correspondente à marca, se houver, já
        /// com o texto que o utilizador tinha escrito no Excel.
        ///
        /// <paramref name="handle"/> viaja na linha para se saber, na leitura
        /// seguinte, a que medição pertence o que lá estiver escrito.
        /// </summary>
        private static void Titulo(List<LinhaFolha> destino, string marca,
            string texto, string handle, bool deArtigo = false)
        {
            TipoLinha tipo;
            if (marca == "CAP") tipo = TipoLinha.TituloCapitulo;
            else if (marca == "ART") tipo = TipoLinha.TituloArtigo;
            else return;

            // O texto vem como "código\u001f descrição": o código para a coluna
            // Item, a descrição para a Designação.
            string codigo = "", descricao = texto ?? "";
            int sep = descricao.IndexOf('\u001f');
            if (sep >= 0)
            {
                codigo = descricao.Substring(0, sep);
                descricao = descricao.Substring(sep + 1);
            }

            destino.Add(new LinhaFolha
            {
                Tipo = tipo,
                Item = codigo,
                Designacao = descricao,
                HandleOrigem = handle,
                DeArtigo = deArtigo
            });
        }

        private static List<Bloco> AgruparFachadas(IList<MedFachada> fachadas)
        {
            var blocos = new List<Bloco>();
            // O ARTIGO é o nível de topo, como na Alvenaria: o artigo do mapa
            // uma vez, e por baixo dele o material, o alçado e o piso onde foi
            // medido. Sem este nível, um pano com artigo escolhido no painel
            // saía debaixo do material e nunca chegava ao artigo no Excel.
            foreach (var gArtigo in fachadas.GroupBy(f => ChaveArtigo(f))
                        .OrderBy(g => OrdemArtigo(g.First())))
            foreach (var gMat in gArtigo.GroupBy(f => f.Material).OrderBy(g => g.Key))
            {
                var b = new Bloco
                {
                    Nome = gMat.Key,
                    Unidade = "m2",
                    Artigo = gArtigo.Key,
                    HandleArtigo = gMat.First().Handle
                };
                foreach (var gAlcado in gMat.GroupBy(f => f.Alcado ?? "").OrderBy(g => g.Key))
                {
                    var a = new BlocoAlcado { Nome = gAlcado.Key };
                    foreach (var gPiso in gAlcado.GroupBy(f => f.Piso).OrderBy(g => g.Key))
                    {
                        var bp = new BlocoPiso { Nome = gPiso.Key };
                        // (sem numeração: as medições saem sem designação)
                        foreach (var f in gPiso)
                        {
                            if (f.Separador) bp.Linhas.Add(new LinhaFolha { Tipo = TipoLinha.Vazia });

                            bp.Linhas.Add(new LinhaFolha
                            {
                                // Como nas paredes: o pano já está identificado
                                // pelo artigo por cima, não precisa de nome.
                                Tipo = TipoLinha.Medicao,
                                Handle = f.Handle,
                                Designacao = "",
                                Un = "m2",
                                Qt = 1,
                                Comp = f.Comp,
                                Altura = f.Alt,
                                TemParcial = true
                            });

                            foreach (var v in f.Vaos)
                            {
                                bp.Linhas.Add(new LinhaFolha
                                {
                                    Tipo = TipoLinha.Deducao,
                                    Designacao = string.IsNullOrWhiteSpace(v.Designacao)
                                        ? "vão" : v.Designacao,
                                    Un = "m2",
                                    Qt = -v.Quantidade,
                                    Comp = v.Largura,
                                    Altura = v.Altura,
                                    TemParcial = true
                                });
                            }

                            // Títulos desta medição, por ordem: capítulo e
                            // artigo — o mesmo contrato da Alvenaria. O índice
                            // viaja no handle para o texto escrito no Excel
                            // voltar ao título certo quando são vários.
                            var marcasF = f.Marcas;
                            for (int mi = 0; mi < marcasF.Count; mi++)
                                Titulo(bp.Linhas, marcasF[mi], f.TextoDaMarca(mi),
                                       f.Handle + "#" + mi);
                        }
                        a.Pisos.Add(bp);
                    }
                    b.Alcados.Add(a);
                }
                blocos.Add(b);
            }
            return blocos;
        }

        /// <summary>
        /// Contagens: agrupa por categoria → piso → nome, e cada nome dá uma
        /// linha com a quantidade. Serve para o que for contado por unidade —
        /// portas, janelas, tomadas, luminárias, louças, equipamentos.
        ///
        /// A quantidade vai no Parcial como VALOR, não como fórmula: não há
        /// comprimento nem altura, e a fórmula do modelo daria zero.
        /// </summary>
        private static List<Bloco> AgruparContagens(IList<MedContagem> contagens)
        {
            var blocos = new List<Bloco>();

            foreach (var gCat in contagens
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Categoria) ? "CONTAGENS" : c.Categoria)
                .OrderBy(g => g.Key))
            {
                var b = new Bloco { Nome = gCat.Key, Unidade = "un" };
                var a = new BlocoAlcado { Nome = "" };

                foreach (var gPiso in gCat.GroupBy(c => c.Piso ?? "").OrderBy(g => g.Key))
                {
                    var bp = new BlocoPiso { Nome = gPiso.Key };

                    foreach (var gNome in gPiso.GroupBy(c => c.Nome ?? "").OrderBy(g => g.Key))
                    {
                        int quantos = gNome.Count();
                        bp.Linhas.Add(new LinhaFolha
                        {
                            Tipo = TipoLinha.Medicao,
                            Designacao = gNome.Key,
                            Un = "un",
                            Qt = quantos,
                            ValorParcial = quantos
                        });
                    }
                    a.Pisos.Add(bp);
                }

                b.Alcados.Add(a);
                blocos.Add(b);
            }
            return blocos;
        }

        private static List<Bloco> AgruparLineares(IList<MedItem> items)
        {
            var blocos = new List<Bloco>();
            foreach (var g in items.GroupBy(i => i.Categoria).OrderBy(x => x.Key))
            {
                var b = new Bloco { Nome = g.Key, Unidade = "m" };
                var a = new BlocoAlcado { Nome = "" };
                var bp = new BlocoPiso { Nome = "" };
                int n = 1;
                foreach (var it in g)
                {
                    bp.Linhas.Add(new LinhaFolha
                    {
                        Tipo = TipoLinha.Medicao,
                        Designacao = "medição " + n++,
                        Un = "m",
                        Qt = 1,
                        Comp = it.Comprimento,
                        TemParcial = true
                    });
                }
                a.Pisos.Add(bp);
                b.Alcados.Add(a);
                blocos.Add(b);
            }
            return blocos;
        }

        /// <summary>
        /// Texto de arranque da designação de uma medição feita com o campo
        /// Bloco preenchido: é assim que o "WC1" ou o "quarto 2" chega à folha.
        ///
        /// Semeia-se AQUI, uma vez, no momento de medir — e não se compõe na
        /// escrita do Excel. A coluna Designação é lida de volta pelo
        /// RecolherNotasEscritas e gravada como nota da medição; se a etiqueta
        /// fosse colada à nota a cada escrita, a leitura seguinte trazia-a
        /// dentro da nota e a escrita a seguir colava-a outra vez —
        /// "em Pavimento - CCC - CCC", a crescer em cada sincronização.
        ///
        /// Assim a célula é sempre e só a nota: quem quiser reescrevê-la no
        /// Excel reescreve-a, e o que lá ficar é o que fica.
        /// </summary>
        public static string NotaInicial(string nota, string bloco)
        {
            string n = (nota ?? "").Trim();
            if (n.Length > 0) return n;
            return (bloco ?? "").Trim();
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// Numera A001… e resolve as fórmulas de Sub total (por alçado)
        /// e Totais (por capítulo) com as linhas reais do Excel.
        /// </summary>
        private static void Numerar(List<LinhaFolha> linhas, int primeiraLinhaExcel)
        {
            int item = 0;
            for (int i = 0; i < linhas.Count; i++)
            {
                var l = linhas[i];
                if (l.Tipo == TipoLinha.Capitulo) { item = 0; continue; }

                // Uma linha em branco é um separador, não uma medição: consumir
                // número nela ("A004" vazio) e deixar a medição seguinte como
                // "A005" fazia a folha saltar letras sem razão nenhuma.
                if (l.Tipo == TipoLinha.Vazia) continue;

                // Os títulos ficam com o código que a pessoa escreveu ("1.1.1").
                // Antes a numeração automática escrevia "A023" por cima — o
                // artigo do cliente desaparecia a cada actualização da folha.
                if (l.Tipo == TipoLinha.TituloCapitulo ||
                    l.Tipo == TipoLinha.TituloArtigo) continue;

                item++;
                l.Item = "A" + item.ToString("000", CultureInfo.InvariantCulture);
            }

            // Sub total de cada alçado ou piso: soma H desde a linha seguinte
            // até ao próximo grupo. Tem de parar também nos pisos, senão o
            // sub-total de um piso engolia os pisos seguintes.
            for (int i = 0; i < linhas.Count; i++)
            {
                if (linhas[i].FormulaSubTotal != "@ALC") continue;

                int fim = i;
                for (int j = i + 1; j < linhas.Count; j++)
                {
                    if (linhas[j].Tipo == TipoLinha.Alcado ||
                        linhas[j].Tipo == TipoLinha.Piso ||
                        linhas[j].Tipo == TipoLinha.Capitulo ||
                        // O título do artigo também fecha o bloco. Desde que
                        // o artigo subiu para cima do capítulo, o intervalo do
                        // último piso de um artigo passava por cima da linha em
                        // branco e do título do artigo SEGUINTE. Hoje somam
                        // zero e o total sai certo — mas basta alguém escrever
                        // um número nessa linha em branco para o piso errado
                        // o apanhar, e ninguém o iria procurar ali.
                        linhas[j].Tipo == TipoLinha.TituloArtigo ||
                        linhas[j].Tipo == TipoLinha.TituloCapitulo) break;
                    fim = j;
                }
                int exIni = primeiraLinhaExcel + i + 1;
                int exFim = primeiraLinhaExcel + fim;
                linhas[i].FormulaSubTotal = exFim >= exIni
                    ? $"SUM(H{exIni}:H{exFim})" : null;
            }

            // Totais do capítulo: soma I até ao próximo capítulo
            for (int i = 0; i < linhas.Count; i++)
            {
                if (linhas[i].FormulaTotais != "@CAP") continue;

                int fim = i;
                for (int j = i + 1; j < linhas.Count; j++)
                {
                    // Pela mesma razão do sub-total: o título do artigo
                    // seguinte fecha o bloco deste capítulo.
                    if (linhas[j].Tipo == TipoLinha.Capitulo ||
                        linhas[j].Tipo == TipoLinha.TituloArtigo ||
                        linhas[j].Tipo == TipoLinha.TituloCapitulo) break;
                    fim = j;
                }
                int exIni = primeiraLinhaExcel + i + 1;
                int exFim = primeiraLinhaExcel + fim;
                // Soma a coluna Parcial (H), não a dos sub-totais: assim conta
                // também as medições que ficaram fora de qualquer piso, e não
                // há risco de somar um sub-total duas vezes.
                linhas[i].FormulaTotais = exFim >= exIni
                    ? $"SUM(H{exIni}:H{exFim})" : null;
            }
        }
    }
}
