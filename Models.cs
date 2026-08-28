using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TSKTakeOff
{
    /// <summary>Regra de desconto dos vãos na área de alvenaria.</summary>
    public enum RegraDesconto
    {
        DescontarTudo = 0,   // Área = parede − todos os vãos
        Sinapi2m2 = 1,       // Vãos ≤ 2 m² não descontam; acima, desconta só o excedente
        NaoDescontar = 2     // Área bruta
    }

    public enum TipoVao { Porta = 0, Janela = 1 }

    /// <summary>Vão (porta/janela) associado a uma parede.</summary>
    public class Vao
    {
        /// <summary>Designação do desenho: VE.01, PC.04, VI.02…</summary>
        public string Designacao { get; set; } = "";
        public double Largura { get; set; }
        public double Altura { get; set; }
        public int Quantidade { get; set; } = 1;
        public bool PreAro { get; set; }
        /// <summary>Espessura do pré-aro (= espessura da parede), em m.</summary>
        public double Espessura { get; set; }
        public TipoVao Tipo { get; set; } = TipoVao.Porta;

        public double Area => Largura * Altura;
        public double AreaTotal => Area * Quantidade;

        /// <summary>Metros lineares de pré-aro: porta = 2H+L; janela = perímetro completo.</summary>
        public double PreAroMetros => !PreAro ? 0.0 :
            (Tipo == TipoVao.Porta ? 2 * Altura + Largura : 2 * (Largura + Altura)) * Quantidade;

        public int PreAroUnidades => PreAro ? Quantidade : 0;

        /// <summary>Área de pré-aro = desenvolvimento × espessura da parede (m²).</summary>
        public double PreAroArea => PreAroMetros * Espessura;

        public double Desconto(RegraDesconto regra)
        {
            switch (regra)
            {
                case RegraDesconto.DescontarTudo: return AreaTotal;
                case RegraDesconto.Sinapi2m2: return Math.Max(0.0, Area - 2.0) * Quantidade;
                default: return 0.0;
            }
        }

        // ---- XData: "L|A|Q|preAro|tipo|espessura|designacao" ----
        public string Serialize()
        {
            return string.Join("|",
                Largura.ToString(CultureInfo.InvariantCulture),
                Altura.ToString(CultureInfo.InvariantCulture),
                Quantidade.ToString(CultureInfo.InvariantCulture),
                PreAro ? "1" : "0",
                Tipo == TipoVao.Porta ? "P" : "J",
                Espessura.ToString(CultureInfo.InvariantCulture),
                (Designacao ?? "").Replace("|", "").Replace(";", ""));
        }

        /// <summary>Aceita o formato antigo (5 campos) e o novo (7).</summary>
        public static Vao Deserialize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            var p = s.Split('|');
            if (p.Length < 5) return null;

            var vao = new Vao
            {
                Largura = ParseD(p[0]),
                Altura = ParseD(p[1]),
                Quantidade = int.TryParse(p[2], NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int q) ? q : 1,
                PreAro = p[3] == "1",
                Tipo = p[4] == "J" ? TipoVao.Janela : TipoVao.Porta
            };
            if (p.Length >= 6) vao.Espessura = ParseD(p[5]);
            if (p.Length >= 7) vao.Designacao = p[6];
            return vao;
        }

        private static double ParseD(string s)
        {
            return double.TryParse(s, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v) ? v : 0.0;
        }
    }

    /// <summary>Uma parede medida no DWG (vinculada à polyline pelo Handle).</summary>
    public class Parede
    {
        public string Handle { get; set; }
        public string Servico { get; set; }
        public string Piso { get; set; } = "";
        /// <summary>Bloco/torre/fração — opcional.</summary>
        public string Bloco { get; set; } = "";
        /// <summary>Alçado / zona (ex.: "Alçado Tardoz"). Nível acima do piso.</summary>
        public string Alcado { get; set; } = "";
        /// <summary>
        /// Emite uma linha em branco ANTES desta medição. Mantido para os
        /// desenhos que já o usam e para o separador pendente, que serve
        /// justamente para abrir um bloco novo antes de medir.
        /// </summary>
        public bool Separador { get; set; }

        /// <summary>
        /// Emite uma linha em branco DEPOIS desta medição, dos seus vãos e do
        /// seu título.
        ///
        /// É este que o botão da paleta usa. Com o "antes", seleccionar uma
        /// linha no Excel e pedir linha em branco fazia-a aparecer por cima da
        /// medição — ao contrário dos botões de artigo, que saem por baixo.
        /// Dois botões vizinhos a interpretar a mesma selecção ao contrário um
        /// do outro é indefensável; agora ambos escrevem para baixo.
        /// </summary>
        /// <summary>
        /// Quantas linhas em branco saem DEPOIS desta medição.
        ///
        /// Era um booleano e não chegava: carregar duas vezes no botão fazia
        /// <c>!true</c> e TIRAVA a linha em vez de acrescentar a segunda. Quem
        /// quer dois espaços entre blocos não tinha como o pedir.
        ///
        /// Agora o botão soma, e ao chegar ao tecto volta a zero — é isso que
        /// mantém a maneira de as remover sem inventar um segundo botão.
        /// </summary>
        public int LinhasEmBrancoDepois { get; set; }

        /// <summary>Máximo de linhas em branco seguidas que o botão oferece.</summary>
        public const int MaxLinhasEmBranco = 3;

        /// <summary>
        /// Há pelo menos uma linha em branco por baixo? Mantido porque meia
        /// dúzia de sítios só querem saber sim ou não.
        /// </summary>
        public bool SeparadorDepois
        {
            get { return LinhasEmBrancoDepois > 0; }
            set { LinhasEmBrancoDepois = value ? 1 : 0; }
        }
        /// <summary>
        /// Título a emitir DEPOIS desta medição: "CAP" (capítulo), "ART" (artigo)
        /// ou vazio. A linha sai com o estilo do modelo e fica pronta para a
        /// pessoa lá escrever o texto.
        /// </summary>
        public string MarcaDepois { get; set; } = "";

        /// <summary>
        /// Texto que o utilizador escreveu na linha de título, no Excel:
        /// código do artigo e descrição, separados por 0x1F.
        ///
        /// Vive aqui e não no Excel porque a folha é reescrita de cada vez que
        /// se mede — tudo o que fosse escrito à mão desaparecia na medição
        /// seguinte. Guardado no DWG, escreve-se uma vez e viaja com o desenho.
        /// </summary>
        public string TextoTitulo { get; set; } = "";

        /// <summary>
        /// Os títulos desta medição, por ordem de saída: capítulo e artigo.
        /// Valores antigos de outros níveis são ignorados ao ler o DWG, para
        /// que desenhos existentes continuem a abrir sem voltar a oferecer
        /// esse nível na interface.
        /// </summary>
        public List<string> Marcas
        {
            get
            {
                var r = new List<string>();
                foreach (var m in (MarcaDepois ?? "").Split(';'))
                    if (m == "CAP" || m == "ART") r.Add(m);
                return r;
            }
        }

        /// <summary>O texto do título válido que está nesta posição da lista.</summary>
        public string TextoDaMarca(int indice)
        {
            if (indice < 0) return "";
            var marcas = (MarcaDepois ?? "").Split(';');
            var textos = (TextoTitulo ?? "").Split(';');
            int valido = 0;
            for (int i = 0; i < marcas.Length; i++)
            {
                if (marcas[i] != "CAP" && marcas[i] != "ART") continue;
                if (valido == indice)
                    return i < textos.Length ? textos[i] : "";
                valido++;
            }
            return "";
        }

        /// <summary>
        /// Acrescenta ou remove capítulo/artigo, mantendo essa ordem.
        /// Valores antigos de outros níveis não podem ser criados novamente.
        /// </summary>
        public void AlternarMarca(string marca)
        {
            if (marca != "CAP" && marca != "ART") return;

            var marcas = new List<string>();
            var textos = new List<string>();
            var antigas = (MarcaDepois ?? "").Split(';');
            var textosAntigos = (TextoTitulo ?? "").Split(';');
            for (int k = 0; k < antigas.Length; k++)
            {
                if (antigas[k] != "CAP" && antigas[k] != "ART") continue;
                marcas.Add(antigas[k]);
                textos.Add(k < textosAntigos.Length ? textosAntigos[k] : "");
            }

            int i = marcas.IndexOf(marca);
            if (i >= 0)
            {
                marcas.RemoveAt(i);
                textos.RemoveAt(i);
            }
            else
            {
                marcas.Add(marca);
                textos.Add("");
                var ordem = new List<string> { "CAP", "ART" };
                var pares = new List<KeyValuePair<string, string>>();
                for (int k = 0; k < marcas.Count; k++)
                    pares.Add(new KeyValuePair<string, string>(marcas[k], textos[k]));
                pares.Sort((a, b) => ordem.IndexOf(a.Key).CompareTo(ordem.IndexOf(b.Key)));
                marcas.Clear(); textos.Clear();
                foreach (var par in pares) { marcas.Add(par.Key); textos.Add(par.Value); }
            }

            MarcaDepois = string.Join(";", marcas.ToArray());
            TextoTitulo = string.Join(";", textos.ToArray());
        }

        /// <summary>Grava o texto do título que está nesta posição.</summary>
        public void DefinirTextoDaMarca(int indice, string texto)
        {
            var marcas = Marcas;
            var textos = new List<string>((TextoTitulo ?? "").Split(';'));
            while (textos.Count < marcas.Count) textos.Add("");
            if (indice < 0 || indice >= marcas.Count) return;

            // O ";" separa títulos e o "\u001f" separa código de descrição:
            // nenhum dos dois pode viajar dentro do texto.
            textos[indice] = (texto ?? "").Replace(";", ",");
            TextoTitulo = string.Join(";", textos.ToArray());
        }

        /// <summary>
        /// Artigo a que esta medição pertence, como "código\x1fdescrição".
        ///
        /// Diferente do <see cref="TextoTitulo"/>: aquele é um título que a
        /// pessoa mandou sair DEPOIS desta medição; este diz de que artigo a
        /// medição É. A mesma parede pode ser medida para alvenaria e para
        /// revestimento — são dois artigos, duas medições, e sem este campo
        /// caíam ambas no mesmo sítio da folha.
        /// </summary>
        public string Artigo { get; set; } = "";

        /// <summary>
        /// Posição na folha de origem. Zero em tudo o que é medido no desenho
        /// (aí manda a ordem por que se mediu); só a importação a preenche,
        /// para a folha sair pela ordem que tinha antes de se perder.
        /// </summary>
        public int Ordem { get; set; }

        /// <summary>
        /// Texto escrito à mão na coluna Designação DESTA medição, no Excel.
        /// Vive no desenho pela mesma razão que o título: a folha é reescrita
        /// de cada vez que se mede, e o que estivesse só no Excel morria aí.
        /// </summary>
        public string Nota { get; set; } = "";

        /// <summary>
        /// Handle da geometria original de onde esta cópia foi medida.
        /// Opcional para medições antigas; permite medir a mesma geometria
        /// para artigos diferentes sem duplicar o mesmo artigo.
        /// </summary>
        public string HandleOrigem { get; set; } = "";

        public string Layer { get; set; }
        public double Comprimento { get; set; }
        public double Altura { get; set; }
        /// <summary>Largura (coluna do mapa de medições). 0 = não se aplica.</summary>
        public double Largura { get; set; }
        public double Espessura { get; set; }
        /// <summary>Retângulo em planta (comp × espessura) ou polyline de eixo.</summary>
        public bool Retangulo { get; set; }

        /// <summary>
        /// A medição é uma área directa, não um comprimento × altura.
        ///
        /// É o caso das hachuras: a hachura já representa a superfície toda, e
        /// a sua área É o número. O comprimento guarda a área e a altura fica a
        /// 1 para as contas baterem, mas a coluna Altura não sai na folha —
        /// escrever lá "1,000" só confundia quem lê a medição.
        /// </summary>
        public bool SoArea { get; set; }

        /// <summary>
        /// A medição é uma ÁREA desenhada em planta, multiplicada pela altura
        /// do painel — área × altura, e não comprimento × altura.
        ///
        /// É o caso das camadas: uma betonilha de enchimento mede-se pela área
        /// do pavimento vezes a espessura da camada, e o resultado é m³. Com o
        /// comprimento de uma polyline não havia como lá chegar — o contorno de
        /// um piso não tem "comprimento" nenhum que signifique alguma coisa, e
        /// quem media tinha de ir calcular a área à parte e escrevê-la à mão.
        ///
        /// O <see cref="Comprimento"/> guarda a área, que é o que sai na coluna
        /// «Comp / Area» da folha; a <see cref="Altura"/> é a espessura da
        /// camada. A conta é a mesma de sempre — por isso não foi preciso
        /// mexer no <see cref="AreaBruta"/> —, só o significado dos factores é
        /// que muda, e é isso que a unidade m³ diz a quem lê.
        ///
        /// Distinto do <see cref="SoArea"/>: ali a área É a medição e a altura
        /// não conta (vale 1); aqui a altura conta, e é ela que faz o volume.
        /// </summary>
        public bool AreaVezesAltura { get; set; }

        /// <summary>
        /// A geometria de origem interrompe-se nos vãos, portanto o comprimento
        /// medido vem curto — falta-lhe a largura de cada porta e janela.
        ///
        /// É o caso das hachuras de planta, que param nas portas. Aqui o
        /// comprimento dos vãos é somado de volta, e a medição volta a ser a
        /// parede INTEIRA — que depois desconta os vãos como sempre. Assim a
        /// folha lê-se exactamente como a de quem mede à mão: a parede a
        /// direito e as deduções por baixo.
        /// </summary>
        public bool GeometriaSemVaos { get; set; }

        /// <summary>
        /// Comprimento de outros troços da mesma parede, somado a este.
        ///
        /// Uma parede com vários vãos aparece no desenho partida em troços —
        /// e um deles pode ter cinco centímetros. Medidos à parte davam cinco
        /// linhas na folha e perdiam-se as vergas. Juntam-se numa medição só:
        /// um dos troços leva a marca e os outros entram por aqui.
        /// </summary>
        public double ComprimentoExtra { get; set; }

        /// <summary>Largura total dos vãos, a repor no comprimento.</summary>
        public double LarguraDosVaos =>
            Vaos.Sum(v => v.Largura * v.Quantidade);

        /// <summary>
        /// O comprimento a medir. Igual ao da geometria, excepto quando esta
        /// vem interrompida pelos vãos — aí repõe-se o que falta.
        /// </summary>
        public double ComprimentoTotal =>
            Comprimento + ComprimentoExtra +
            (GeometriaSemVaos ? LarguraDosVaos : 0.0);
        public List<Vao> Vaos { get; } = new List<Vao>();

        /// <summary>Comp × Altura; se houver Largura preenchida, entra no produto.</summary>
        public double AreaBruta => ComprimentoTotal * Altura * (Largura > 0 ? Largura : 1.0);
        public double DescontoVaos(RegraDesconto r) => Vaos.Sum(v => v.Desconto(r));
        public double AreaLiquida(RegraDesconto r) => Math.Max(0.0, AreaBruta - DescontoVaos(r));
        /// <summary>Volume de alvenaria = área líquida × espessura (m³).</summary>
        public double Volume(RegraDesconto r) => AreaLiquida(r) * Espessura;

        /// <summary>
        /// A unidade em que esta medição FATURA — a mesma que a folha escreve.
        ///
        /// A quantidade é sempre a <see cref="AreaLiquida"/>; o que muda é o
        /// significado dos factores. Numa parede são comprimento × altura e dá
        /// m²; numa camada (<see cref="AreaVezesAltura"/>) são área em planta ×
        /// espessura da camada e dá m³.
        ///
        /// Existe para não haver dois sítios a decidir isto. Estava escrito à
        /// mão no FolhaMedicao ("m3" ou "m2"), fixo em "m2" no aviso de unidade
        /// do AlvRepo, e em lado nenhum na paleta — que somava paredes com
        /// camadas e chamava m² ao resultado.
        /// </summary>
        public string Unidade => AreaVezesAltura ? "m3" : "m2";
        public int PreAroUn => Vaos.Sum(v => v.PreAroUnidades);
        public double PreAroMl => Vaos.Sum(v => v.PreAroMetros);
        /// <summary>Área de pré-aro (desenvolvimento × espessura), em m².</summary>
        public double PreAroM2 => Vaos.Sum(v => v.PreAroArea);

        public string SerializeVaos() => string.Join(";", Vaos.Select(v => v.Serialize()));

        public void DeserializeVaos(string s)
        {
            Vaos.Clear();
            if (string.IsNullOrWhiteSpace(s)) return;
            foreach (var part in s.Split(';'))
            {
                var v = Vao.Deserialize(part);
                if (v != null) Vaos.Add(v);
            }
        }
    }

    /// <summary>Configuração atual escolhida na paleta (usada pelos comandos).</summary>
    public static class Config
    {
        public static string Servico = "ALVENARIA";
        /// <summary>Piso da alvenaria (partilha as cores com a aba Materiais).</summary>
        public static string Piso = "PISO 0";
        /// <summary>Bloco/torre/fração — opcional, vazio = não usar.</summary>
        public static string Bloco = "";
        /// <summary>Alçado / zona corrente.</summary>
        public static string Alcado = "";

        /// <summary>
        /// Artigo do mapa de quantidades a que as próximas medições pertencem,
        /// como "código + 0x1F + descrição".
        ///
        /// É isto que permite medir dez paredes seguidas sem tocar no teclado:
        /// escolhe-se o artigo uma vez e as medições que se fizerem a seguir
        /// vão todas para lá. Trocar de artigo é trocar este campo.
        ///
        /// Não substitui o <see cref="Servico"/>, que continua a identificar a
        /// medição na folha. A layer é outra coisa e tem campo próprio — ver
        /// <see cref="Layer"/>.
        /// </summary>
        public static string Artigo = "";

        /// <summary>
        /// Layer onde as próximas medições são desenhadas, SEM o prefixo MED_.
        /// Vazio = calculada a partir do artigo e do bloco.
        ///
        /// Era o Serviço que mandava na layer, e isso juntava duas coisas que
        /// não são a mesma: o Serviço identifica a medição na folha do cliente
        /// — "COM_H=2.45" — e a layer é como se separa o desenho para o poder
        /// ver. Quem media uma obra inteira acabava com uma layer por variante
        /// de serviço, e no TSKMEDSEL, que deixa marcar vários serviços de uma
        /// vez, saíam todas ao mesmo tempo.
        /// </summary>
        public static string Layer = "";

        /// <summary>
        /// O nome da layer a usar agora: o do campo, ou o calculado.
        ///
        /// O calculado é os SEIS primeiros caracteres do código do artigo mais
        /// o bloco/etiqueta — "7.2.2_CCC". Seis chegam para um código de mapa
        /// ("7.2.2", "12.4.1") e cortam a descrição, que não cabe num nome de
        /// layer. Sem artigo nem bloco, fica o serviço, que é o que isto
        /// sempre fez.
        /// </summary>
        public static string LayerEfectiva()
        {
            string escrita = (Layer ?? "").Trim();
            if (escrita.Length > 0) return escrita;

            string codigo = (Artigo ?? "").Trim();
            int sep = codigo.IndexOf('\u001f');
            if (sep >= 0) codigo = codigo.Substring(0, sep);
            if (codigo.Length > 6) codigo = codigo.Substring(0, 6);
            codigo = codigo.Trim();

            string bloco = (Bloco ?? "").Trim();

            if (codigo.Length > 0 && bloco.Length > 0) return codigo + "_" + bloco;
            if (codigo.Length > 0) return codigo;
            if (bloco.Length > 0) return bloco;
            return Servico ?? "ALVENARIA";
        }
        public static double Altura = 2.80;
        /// <summary>Espessura da parede (m). 0 = usar a medida do retângulo desenhado.</summary>
        public static double Espessura = 0.15;

        /// <summary>
        /// Espessura do traço das medições, em centésimos de milímetro
        /// (50 = 0,50 mm). É um lineweight: propriedade de desenho, não
        /// geometria, por isso o AutoCAD desenha-a sem custo.
        /// 0 = deixar como está no desenho.
        /// </summary>
        public static int EspessuraTracoMm = 50;

        /// <summary>
        /// Largura real (global width) das polylines, em unidades do desenho.
        ///
        /// Acima de zero manda sobre o lineweight. Atenção ao custo: uma
        /// polyline com largura real é geometria preenchida e o AutoCAD
        /// desenha-a como sólido. A 0,05 notava-se ao criar cada medição; a
        /// 0,02 é bem menos, mas se voltar a arrastar num desenho pesado,
        /// põe-se isto a 0 e o lineweight assume — o aspecto é semelhante e
        /// não custa nada a desenhar.
        /// </summary>
        public static double LarguraTraco = 0.02;

        /// <summary>
        /// Altura das linhas de medição e dedução no Excel, em pontos.
        /// 0 = deixar como o modelo as tiver.
        /// </summary>
        public static double AlturaLinhaExcel = 10.2;

        /// <summary>
        /// Espera MÍNIMA, em milissegundos, antes de reescrever o Excel.
        ///
        /// Enquanto se mede parede atrás de parede, cada uma pedia uma
        /// reescrita completa da folha — e o AutoCAD ficava à espera do Excel
        /// entre cliques. Com esta espera, medições seguidas juntam-se numa
        /// única escrita, feita quando se pára. 0 desliga e volta a escrever
        /// a cada medição.
        ///
        /// ESTEVE EM 400 ms, e desceu por uma razão medida.
        ///
        /// Esse valor foi escolhido quando uma escrita custava uns 600 ms:
        /// esperar 400 para evitar 600 compensava. Depois de a escrita passar
        /// a 191 ms (índice das fórmulas, colagens de formato e grelha), a
        /// espera passou a ser MAIOR DO QUE AQUILO QUE EVITA — e o que se
        /// sentia deixou de ser o AutoCAD a prender e passou a ser a folha a
        /// demorar a responder depois de confirmar a medição.
        ///
        /// 150 ms chega para juntar cliques seguidos e é curto de mais para se
        /// dar por ele. E quem protege o caso caro deixou de ser este número:
        /// é o <see cref="AtrasoExcelMaxMs"/>, que faz a espera crescer sozinha
        /// só quando a escrita é mesmo lenta.
        /// </summary>
        public static int AtrasoExcelMs = 150;

        /// <summary>
        /// Tecto da espera adaptativa antes de escrever no Excel (ms).
        ///
        /// A espera real é o maior entre o <see cref="AtrasoExcelMs"/> e o que
        /// a última escrita demorou — ver PaletteHost.AgendarExcel. Isso impede
        /// que o Excel ocupe mais de metade do tempo de quem mede, mas sem um
        /// tecto uma escrita muito lenta empurraria a seguinte para tão longe
        /// que a folha pareceria ter deixado de acompanhar.
        ///
        /// Cinco segundos é o limite do que se aceita esperar por uma folha que
        /// se quer "ao vivo". Acima disso o problema é a escrita ser lenta, e é
        /// aí que tem de ser resolvido, não no adiamento.
        /// </summary>
        public static int AtrasoExcelMaxMs = 5000;

        /// <summary>
        /// Altura de referência dos vãos detectados nos intervalos da hachura.
        /// A largura vem do desenho e é exacta; a altura não está lá, por isso
        /// entra este valor e afina-se na grelha.
        /// </summary>
        public static double AlturaVaoPadrao = 2.10;

        /// <summary>
        /// Tudo numa folha do Excel, em vez de uma por aba da paleta.
        ///
        /// As abas Alvenaria, Materiais e Contagens são uma divisão da PALETA,
        /// para se medir uma coisa de cada vez. Não são uma divisão da medição:
        /// o trabalho de um dia lê-se todo seguido, e com folhas separadas a
        /// numeração recomeçava em cada uma.
        /// </summary>
        public static bool UmaFolhaSo = true;

        /// <summary>Nome da folha única no Excel.</summary>
        public static string NomeDaFolha = "MEDIÇÕES";

        /// <summary>
        /// Transparência das hachuras de medição (0-255; 0 = opaca).
        /// Translúcidas deixam ver a planta por baixo — sem isso a medição
        /// tapa o desenho que ela própria serve para conferir.
        /// </summary>
        public static int TransparenciaHatch = 150;
        public static RegraDesconto Regra = RegraDesconto.DescontarTudo;

        /// <summary>
        /// Linha em branco à espera da próxima medição. Serve para separar
        /// blocos enquanto se mede: carrega-se no botão depois de fechar um
        /// conjunto e a medição seguinte já nasce com o separador.
        ///
        /// Sem isto só se podia marcar uma medição já existente — o que não
        /// serve quando o que se quer é separar do que vem a seguir.
        /// </summary>
        public static bool SeparadorPendente = false;

        /// <summary>Consome o separador pendente: devolve-o e desarma-o.</summary>
        public static bool ConsumirSeparador()
        {
            bool pendente = SeparadorPendente;
            SeparadorPendente = false;
            return pendente;
        }


        /// <summary>
        /// Altura dos rótulos escritos no meio das medições, em unidades do
        /// desenho. Fixa de propósito: o TEXTSIZE dos ficheiros de arquitectura
        /// costuma estar preparado para outra escala e saía enorme.
        /// 0 = voltar a seguir o TEXTSIZE do desenho.
        /// </summary>
        public static double AlturaTexto = LerAlturaTexto();

        private const double AlturaTextoOmissao = 0.03;

        private static string FicheiroAlturaTexto
        {
            get
            {
                string pasta = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TSKTakeOff");
                System.IO.Directory.CreateDirectory(pasta);
                return System.IO.Path.Combine(pasta, "texto.txt");
            }
        }

        private static double LerAlturaTexto()
        {
            try
            {
                string f = FicheiroAlturaTexto;
                if (System.IO.File.Exists(f))
                {
                    double v;
                    if (double.TryParse(System.IO.File.ReadAllText(f).Trim(),
                            NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 0)
                        return v;
                }
            }
            catch { /* fica a de omissão */ }
            return AlturaTextoOmissao;
        }

        /// <summary>Guarda a altura escolhida para os próximos arranques.</summary>
        public static void GuardarAlturaTexto()
        {
            try
            {
                System.IO.File.WriteAllText(FicheiroAlturaTexto,
                    AlturaTexto.ToString(CultureInfo.InvariantCulture));
            }
            catch { /* melhor esforço */ }
        }

        public static string RegraDescricao(RegraDesconto r)
        {
            switch (r)
            {
                case RegraDesconto.DescontarTudo: return "Descontar todos os vãos";
                case RegraDesconto.Sinapi2m2: return "SINAPI: descontar excedente de 2 m²";
                default: return "Não descontar vãos";
            }
        }
    }

    /// <summary>Medição linear simples (comando MEDIR, v1).</summary>
    public class MedItem
    {
        public string Handle { get; set; }
        public string Categoria { get; set; }
        public string Layer { get; set; }
        public double Comprimento { get; set; }
        public int Vertices { get; set; }
    }

    /// <summary>
    /// Totais de um conjunto de medições, separados por unidade.
    ///
    /// NUNCA somar unidades diferentes. A barra da paleta somava a área
    /// líquida de TODAS as medições e escrevia "m²" ao lado — mas as camadas
    /// (área em planta × espessura) faturam m³, e entravam na mesma conta. O
    /// número que saía não era m² nem m³: era a soma de duas grandezas que se
    /// não somam, com ar de estar bem.
    ///
    /// Fica aqui, e não na paleta, porque é aritmética: não toca no AutoCAD e
    /// pode estar sob teste.
    /// </summary>
    public static class TotaisMedicao
    {
        /// <summary>
        /// Total por unidade, pela ordem em que a unidade aparece na lista.
        ///
        /// A ordem é a de chegada de propósito: quem mede uma obra de alvenaria
        /// vê m² à frente; quem mede camadas vê m³. Ordenar por nome punha o m³
        /// primeiro sempre, que é o caso menos comum.
        /// </summary>
        public static List<KeyValuePair<string, double>> PorUnidade(
            IEnumerable<Parede> paredes, RegraDesconto regra)
        {
            var ordem = new List<string>();
            var soma = new Dictionary<string, double>();

            foreach (var p in paredes ?? new List<Parede>())
            {
                if (p == null) continue;
                string u = p.Unidade;
                if (!soma.ContainsKey(u)) { soma[u] = 0.0; ordem.Add(u); }
                soma[u] += p.AreaLiquida(regra);
            }

            var saida = new List<KeyValuePair<string, double>>();
            foreach (var u in ordem)
                saida.Add(new KeyValuePair<string, double>(u, soma[u]));
            return saida;
        }
    }
}
