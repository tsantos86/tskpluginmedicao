using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TSKTakeOff
{
    /// <summary>
    /// Lê descrições de armadura escritas em texto no desenho e devolve
    /// quantidade, diâmetro, comprimento em metros e peso em kg.
    ///
    /// Vive à parte de quem mexe no AutoCAD pela mesma razão do
    /// <see cref="DimensaoVao"/>: aqui não entra uma única linha da API, e por
    /// isso isto corre num projecto de testes normal, sem AutoCAD instalado e
    /// sem desenho nenhum. É a parte onde os erros doem — um varão lido como
    /// 350 m em vez de 3,50 m multiplica o aço por cem — e é a parte que tem
    /// rede.
    ///
    /// Quem sabe ONDE estão os textos é o comando; esta classe só sabe o que
    /// eles querem dizer.
    /// </summary>
    public static class Armadura
    {
        // ------------------------------------------------------------------
        // Tabela de pesos
        // ------------------------------------------------------------------

        /// <summary>
        /// Peso nominal do varão por metro (kg/m), por diâmetro em mm.
        ///
        /// Só estes. Um diâmetro que não esteja aqui é RECUSADO, nunca
        /// interpolado nem calculado pela fórmula da área: o peso nominal é
        /// uma convenção normativa, e inventá-lo para um Ø14 ou Ø40 daria um
        /// número com ar de certo que ninguém conferiria. Acrescentar um
        /// diâmetro é acrescentar uma linha aqui, de propósito.
        /// </summary>
        public static readonly IDictionary<int, double> PesoPorMetro =
            new Dictionary<int, double>
            {
                { 6,  0.222 },
                { 8,  0.395 },
                { 10, 0.617 },
                { 12, 0.888 },
                { 16, 1.578 },
                { 20, 2.466 },
                { 25, 3.853 },
                { 32, 6.313 },
            };

        // ------------------------------------------------------------------
        // Limites de plausibilidade
        // ------------------------------------------------------------------

        /// <summary>
        /// Comprimento plausível de um varão, já desenvolvido (m).
        ///
        /// São estes limites que apanham a leitura absurda. O máximo é
        /// generoso de propósito: um varão comercial vem a 12 m, mas um
        /// desenvolvimento com dobragens pode passar disso numa peça longa.
        /// </summary>
        public const double CompMin = 0.10, CompMax = 18.0;

        /// <summary>Quantidade plausível de varões numa mesma descrição.</summary>
        public const int QtdMin = 1, QtdMax = 9999;

        /// <summary>
        /// Afastamento plausível entre varões de uma malha (m).
        ///
        /// Na prática anda entre 0,075 e 0,30. Os limites são folgados para
        /// não recusar um caso legítimo, mas apertados o suficiente para
        /// apanhar a leitura de unidade errada — um "//125" lido em metros
        /// daria 125 m de afastamento.
        /// </summary>
        public const double EspMin = 0.04, EspMax = 0.60;

        // ------------------------------------------------------------------
        // Expressões
        // ------------------------------------------------------------------

        /// <summary>
        /// Cabeça da descrição: quantidade, separador e diâmetro.
        ///
        /// Cobre as três formas que aparecem nos desenhos:
        ///   15 Ø 16   ·   15Ø16   ·   15 T 16   ·   15N16   ·   12xØ12
        ///
        /// Por partes:
        ///   (?&lt;qtd&gt;\d{1,4})      a quantidade, até 4 dígitos
        ///   (?:\s*[x*]\s*)?       o "x" do 12xØ12, opcional
        ///   [Ø...|T|N|#]     o símbolo do diâmetro: Ø nas suas várias
        ///                         codificações, ou a letra que o substitui
        ///                         quando o desenho não tem o glifo (T, N, #)
        ///   (?&lt;diam&gt;\d{1,2})     o diâmetro em mm, 1 ou 2 dígitos
        ///   (?!\d)                e mais nenhum dígito a seguir — sem isto,
        ///                         um "Ø160" era lido como Ø16
        ///
        /// O símbolo vai em escapes \uXXXX em vez do carácter literal: este
        /// ficheiro passa por editores e consolas com codificações diferentes,
        /// e um Ø que se estrague no caminho parte o parser sem deixar rasto.
        /// </summary>
        public static readonly Regex RxCabeca = new Regex(
            @"(?<qtd>\d{1,4})\s*(?:[x*X]\s*)?" +
            @"(?:[Øø∅Φφ⌀]|(?<![A-Za-z])[TN#])" +
            @"\s*(?<diam>\d{1,2})(?!\d)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Comprimento declarado com etiqueta: C=3.50, L=350, COMP=3,50m.
        ///
        /// A etiqueta com o SINAL DE IGUAL é o que distingue o comprimento do
        /// afastamento. Num "15 Ø 16 c/20 C=3.50", o c/20 é o espaçamento
        /// entre varões — apanhá-lo como comprimento daria 0,20 m e uma
        /// medição errada por um factor de dezassete. O "/" nunca é "=", e é
        /// isso que os separa.
        /// </summary>
        public static readonly Regex RxCompEtiqueta = new Regex(
            @"\b(?:C|L|COMP|CORTE)\s*=\s*(?<comp>\d{1,5}(?:[.,]\d{1,3})?)\s*(?<un>mm|cm|m)?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Comprimento no fim, atrás de um traço: "12xØ12 - 400".
        ///
        /// Só no FIM da linha e só com traço isolado. Tentada depois da
        /// etiquetada, senão um "C=3.50 - 20" daria 20.
        /// </summary>
        public static readonly Regex RxCompTraco = new Regex(
            @"[-–—]\s*(?<comp>\d{1,5}(?:[.,]\d{1,3})?)\s*(?<un>mm|cm|m)?\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>
        /// Malha distribuída: diâmetro e AFASTAMENTO, sem quantidade nenhuma.
        ///
        ///   Ø12//0.125 m   ·   Ø10//0.125   ·   Ø8//0.125   ·   Ø12//12.5cm
        ///
        /// É esta a notação das plantas de laje — a que os desenhos reais do
        /// Casquilho Poente trazem — e é uma grandeza diferente da do mapa de
        /// varões: aqui não há quantidade escrita, porque ela sai da ÁREA que
        /// a malha cobre a dividir pelo afastamento. O texto dá o consumo por
        /// m²; a área dá o resto.
        ///
        /// O "//" é o que a distingue: em português de projecto lê-se
        /// "afastado de". Um único "/" é o afastamento escrito à americana
        /// (c/0.125) e também é aceite.
        /// </summary>
        public static readonly Regex RxMalha = new Regex(
            @"(?:[Øø∅Φφ⌀]|(?<![A-Za-z])[TN#])\s*(?<diam>\d{1,2})(?!\d)" +
            @"\s*(?://|/|c/|@)\s*" +
            @"(?<esp>\d{1,4}(?:[.,]\d{1,4})?)\s*(?<un>mm|cm|m)?",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ------------------------------------------------------------------
        // Leitura
        // ------------------------------------------------------------------

        /// <summary>Porque é que uma descrição não deu medição.</summary>
        public enum Falha
        {
            Nenhuma = 0,
            TextoVazio,
            SemQuantidade,
            SemDiametro,
            SemComprimento,
            QuantidadeForaDeLimites,
            ComprimentoForaDeLimites,
            DiametroNaoSuportado,
            SemAfastamento,
            AfastamentoForaDeLimites
        }

        /// <summary>
        /// Interpreta uma linha de texto. Devolve <c>true</c> só quando tudo
        /// foi lido e passou nos limites; caso contrário o <paramref name="falha"/>
        /// diz o quê, para o comando poder marcar a medição e continuar.
        /// </summary>
        public static bool Ler(string texto, out Leitura leitura, out Falha falha)
        {
            leitura = null;
            falha = Falha.Nenhuma;

            if (string.IsNullOrWhiteSpace(texto)) { falha = Falha.TextoVazio; return false; }

            var cab = RxCabeca.Match(texto);
            if (!cab.Success)
            {
                // Sem cabeça não há maneira de saber se falta a quantidade ou
                // o diâmetro; diz-se o que falta primeiro na leitura humana.
                falha = Regex.IsMatch(texto, @"\d") ? Falha.SemDiametro : Falha.SemQuantidade;
                return false;
            }

            int qtd = (int)DimensaoVao.ParseNum(cab.Groups["qtd"].Value);
            int diam = (int)DimensaoVao.ParseNum(cab.Groups["diam"].Value);

            if (qtd < QtdMin || qtd > QtdMax) { falha = Falha.QuantidadeForaDeLimites; return false; }

            double comp;
            if (!LerComprimento(texto, out comp))
            {
                falha = Falha.SemComprimento;
                return false;
            }
            if (comp < CompMin || comp > CompMax)
            {
                falha = Falha.ComprimentoForaDeLimites;
                return false;
            }

            if (!PesoPorMetro.ContainsKey(diam)) { falha = Falha.DiametroNaoSuportado; return false; }

            leitura = new Leitura
            {
                Quantidade = qtd,
                DiametroMm = diam,
                ComprimentoM = Math.Round(comp, 3),
                TextoOriginal = texto.Trim()
            };
            return true;
        }

        /// <summary>
        /// O comprimento da descrição, em metros.
        ///
        /// COMO SE DECIDE A UNIDADE, quando ela não vem escrita:
        ///
        ///   1. Unidade explícita (m, cm, mm) manda sempre.
        ///   2. Com separador decimal — "C=3.50" — é METRO. Ninguém escreve
        ///      centímetros com casas decimais num quadro de armadura.
        ///   3. Sem separador decimal — "C=350", "- 400" — é CENTÍMETRO.
        ///
        /// A regra 3 é convenção declarada, não adivinhação: é assim que os
        /// quadros de ferro são cotados. E é ela que impede o erro que dá nome
        /// a este comentário — ler "350" como 350 metros. O que sobrar fora dos
        /// limites de plausibilidade é recusado pelo <see cref="Ler"/>, em vez
        /// de passar por bom.
        /// </summary>
        public static bool LerComprimento(string texto, out double metros)
        {
            metros = 0;
            if (string.IsNullOrEmpty(texto)) return false;

            // A etiquetada primeiro: é a forma explícita e a mais fiável.
            var m = RxCompEtiqueta.Match(texto);
            if (!m.Success) m = RxCompTraco.Match(texto);
            if (!m.Success) return false;

            string bruto = m.Groups["comp"].Value;
            double valor = DimensaoVao.ParseNum(bruto);
            if (valor <= 0) return false;

            string un = (m.Groups["un"].Value ?? "").ToLowerInvariant();
            if (un == "m") { metros = valor; return true; }
            if (un == "cm") { metros = valor / 100.0; return true; }
            if (un == "mm") { metros = valor / 1000.0; return true; }

            bool temDecimal = bruto.IndexOfAny(new[] { '.', ',' }) >= 0;
            metros = temDecimal ? valor : valor / 100.0;
            return true;
        }

        /// <summary>
        /// Peso total desta armadura, em kg.
        ///
        ///   kg = quantidade × comprimento (m) × peso nominal (kg/m)
        /// </summary>
        public static double PesoKg(Leitura l)
        {
            if (l == null) return 0;
            double kgm;
            if (!PesoPorMetro.TryGetValue(l.DiametroMm, out kgm)) return 0;
            return Math.Round(l.Quantidade * l.ComprimentoM * kgm, 3);
        }

        // ------------------------------------------------------------------
        // Malha distribuída
        // ------------------------------------------------------------------

        /// <summary>
        /// Interpreta uma malha — "Ø12//0.125 m" — e devolve o consumo por m².
        ///
        /// Não devolve peso: sem a área não há peso nenhum. Quem chama mede a
        /// zona e multiplica.
        /// </summary>
        public static bool LerMalha(string texto, out Malha malha, out Falha falha)
        {
            malha = null;
            falha = Falha.Nenhuma;

            if (string.IsNullOrWhiteSpace(texto)) { falha = Falha.TextoVazio; return false; }

            var m = RxMalha.Match(texto);
            if (!m.Success) { falha = Falha.SemAfastamento; return false; }

            int diam = (int)DimensaoVao.ParseNum(m.Groups["diam"].Value);
            if (!PesoPorMetro.ContainsKey(diam)) { falha = Falha.DiametroNaoSuportado; return false; }

            string bruto = m.Groups["esp"].Value;
            double valor = DimensaoVao.ParseNum(bruto);
            if (valor <= 0) { falha = Falha.SemAfastamento; return false; }

            // A mesma regra do comprimento, pelas mesmas razões: unidade
            // escrita manda; com decimal é metro ("//0.125"); sem decimal é
            // centímetro ("//12" = 12 cm). E os limites apanham o resto.
            string un = (m.Groups["un"].Value ?? "").ToLowerInvariant();
            double esp;
            if (un == "m") esp = valor;
            else if (un == "cm") esp = valor / 100.0;
            else if (un == "mm") esp = valor / 1000.0;
            else esp = bruto.IndexOfAny(new[] { '.', ',' }) >= 0 ? valor : valor / 100.0;

            if (esp < EspMin || esp > EspMax) { falha = Falha.AfastamentoForaDeLimites; return false; }

            malha = new Malha
            {
                DiametroMm = diam,
                AfastamentoM = Math.Round(esp, 4),
                TextoOriginal = texto.Trim()
            };
            return true;
        }

        /// <summary>
        /// Uma malha lida do desenho: diâmetro e afastamento, numa direcção.
        ///
        /// Uma laje leva quatro destas — inferior longitudinal e transversal,
        /// superior longitudinal e transversal (INF1, INF2, SUP1, SUP2 nos
        /// desenhos). Cada uma é medida por si; o total da laje é a soma.
        /// </summary>
        public class Malha
        {
            public int DiametroMm { get; set; }
            public double AfastamentoM { get; set; }
            public string TextoOriginal { get; set; }

            /// <summary>
            /// Metros de varão por m² de laje, nesta direcção.
            ///
            /// Num afastamento de 0,125 m cabem 8 varões por metro, logo 8 m
            /// de varão por m². É só o inverso do afastamento.
            /// </summary>
            public double MetrosPorM2 { get { return 1.0 / AfastamentoM; } }

            /// <summary>Consumo de aço por m² de laje, nesta direcção (kg/m²).</summary>
            public double KgPorM2
            {
                get
                {
                    double kgm;
                    if (!PesoPorMetro.TryGetValue(DiametroMm, out kgm)) return 0;
                    return Math.Round(MetrosPorM2 * kgm, 4);
                }
            }

            /// <summary>O aço desta malha numa zona de <paramref name="areaM2"/> m².</summary>
            public double PesoKg(double areaM2)
            {
                return areaM2 <= 0 ? 0 : Math.Round(areaM2 * KgPorM2, 3);
            }

            public override string ToString()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "Ø{0}//{1:0.###} m  ({2:0.###} kg/m²)",
                    DiametroMm, AfastamentoM, KgPorM2);
            }
        }

        /// <summary>Frase para a linha de comandos, quando a leitura falha.</summary>
        public static string Explicar(Falha f)
        {
            switch (f)
            {
                case Falha.TextoVazio: return "texto vazio";
                case Falha.SemQuantidade: return "não se encontrou a quantidade";
                case Falha.SemDiametro: return "não se encontrou o diâmetro";
                case Falha.SemComprimento: return "não se encontrou o comprimento (C= ou L=)";
                case Falha.QuantidadeForaDeLimites: return "quantidade fora do plausível";
                case Falha.ComprimentoForaDeLimites:
                    return "comprimento fora do plausível (" +
                           CompMin.ToString("0.00", CultureInfo.InvariantCulture) + " a " +
                           CompMax.ToString("0.00", CultureInfo.InvariantCulture) + " m)";
                case Falha.DiametroNaoSuportado: return "diâmetro sem peso nominal na tabela";
                case Falha.SemAfastamento: return "não se encontrou o afastamento (Ø..//..)";
                case Falha.AfastamentoForaDeLimites:
                    return "afastamento fora do plausível (" +
                           EspMin.ToString("0.00", CultureInfo.InvariantCulture) + " a " +
                           EspMax.ToString("0.00", CultureInfo.InvariantCulture) + " m)";
                default: return "";
            }
        }

        /// <summary>Uma descrição de armadura já interpretada.</summary>
        public class Leitura
        {
            public int Quantidade { get; set; }
            public int DiametroMm { get; set; }
            public double ComprimentoM { get; set; }
            public string TextoOriginal { get; set; }

            /// <summary>Metros lineares de varão: quantidade × comprimento.</summary>
            public double MetrosLineares { get { return Quantidade * ComprimentoM; } }

            public override string ToString()
            {
                return string.Format(CultureInfo.CurrentCulture,
                    "{0} Ø{1} C={2:N2} m", Quantidade, DiametroMm, ComprimentoM);
            }
        }
    }
}
