using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TSKTakeOff
{
    /// <summary>
    /// Lê dimensões de vãos escritas em texto e devolve-as em metros.
    ///
    /// Vive à parte do <c>VaoDetector</c> por uma razão prática: aqui não
    /// entra uma única linha de AutoCAD, e por isso isto pode ser testado com
    /// um projecto de testes normal, sem abrir o AutoCAD e sem desenho
    /// nenhum. É a parte do detector onde os erros doem — uma porta lida como
    /// 800 m em vez de 0,80 m estraga a medição toda — e agora é a parte que
    /// tem rede.
    ///
    /// O detector continua a ser quem sabe onde estão os textos; esta classe
    /// só sabe o que eles querem dizer.
    /// </summary>
    public static class DimensaoVao
    {
        // Limites plausíveis de um vão real (m). São eles que permitem
        // adivinhar a unidade: ver Interpretar.
        public const double LargMin = 0.30, LargMax = 8.0;
        public const double AltMin = 0.30, AltMax = 5.0;

        /// <summary>"1190x2350", "200X160", "2,67x2,62", "0.90 x 2.10"</summary>
        public static readonly Regex RxDim = new Regex(
            @"(?<a>\d{1,4}(?:[.,]\d{1,3})?)\s*[xX×]\s*(?<b>\d{1,4}(?:[.,]\d{1,3})?)",
            RegexOptions.Compiled);

        /// <summary>Designação genérica: VE.02, VI04, PC.04, C.04, FO.01, AL205, P1, J2</summary>
        public static readonly Regex RxDesignacao = new Regex(
            @"\b[A-Z]{1,3}\.?\s?\d{1,3}(?:\.\d{1,2})?\b",
            RegexOptions.Compiled);

        /// <summary>
        /// Infere a unidade por plausibilidade: testa m, cm e mm e aceita a
        /// primeira que dê um vão realista. "800x2100"→mm; "200X160"→cm;
        /// "2,67x2,62"→m.
        ///
        /// Adivinhar a unidade é feio, mas a alternativa é pior: cada gabinete
        /// cota como quer, e obrigar o cliente a declarar a convenção antes de
        /// medir seria trocar um erro raro por uma pergunta em todas as
        /// medições. A rede de segurança são os limites: o que não couber num
        /// vão plausível é recusado em vez de adivinhado à sorte.
        /// </summary>
        public static bool Interpretar(string sa, string sb,
            out double larg, out double alt, out string unidade)
        {
            larg = alt = 0; unidade = null;
            if (sa == null || sb == null) return false;

            double a = ParseNum(sa), b = ParseNum(sb);
            if (a <= 0 || b <= 0) return false;

            bool temDecimal = sa.IndexOfAny(new[] { '.', ',' }) >= 0 ||
                              sb.IndexOfAny(new[] { '.', ',' }) >= 0;

            // Com separador decimal é quase sempre metro; senão testa mm → cm → m
            var tentativas = temDecimal
                ? new[] { new { d = 1.0, u = "m" }, new { d = 100.0, u = "cm" } }
                : new[] { new { d = 1000.0, u = "mm" }, new { d = 100.0, u = "cm" },
                          new { d = 1.0, u = "m" } };

            foreach (var t in tentativas)
            {
                double w = a / t.d, h = b / t.d;
                if (w >= LargMin && w <= LargMax && h >= AltMin && h <= AltMax)
                {
                    larg = Math.Round(w, 3);
                    alt = Math.Round(h, 3);
                    unidade = t.u;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// As duas dimensões de um vão desenhado — um rectângulo, uma polyline
        /// fechada, uma hachura — em vez de escrito num texto.
        ///
        /// O DESENHO NÃO DIZ QUAL DAS DUAS É A ALTURA, e é aí que isto se
        /// decide errado com facilidade:
        ///
        ///   · num ALÇADO o rectângulo é mesmo largura × altura — uma porta é
        ///     0,90 × 2,10 e as duas cotas estão no desenho;
        ///   · numa PLANTA o rectângulo é largura × ESPESSURA DA PAREDE —
        ///     0,90 × 0,20. Tomar 0,20 por altura dava um desconto de 0,18 m²
        ///     onde deviam estar 1,89 m², e o número sairia com ar de estar bem.
        ///
        /// Por isso quem manda é o TIPO DE MEDIÇÃO a que o vão vai ser
        /// agarrado, que o TSKVAO já sabe antes de perguntar a geometria:
        /// fachada = alçado, alvenaria = planta. Na planta lê-se só a largura e
        /// a altura vem do painel — exactamente o que o VaosEntreTrocos já faz
        /// com as ilhas das hachuras.
        ///
        /// Na planta usa-se o LADO, não a extensão: um vão numa parede rodada
        /// tem uma caixa envolvente maior do que ele próprio, e o lado é imune
        /// à rotação.
        /// </summary>
        /// <param name="dx">Extensão horizontal da geometria (m).</param>
        /// <param name="dy">Extensão vertical da geometria (m).</param>
        /// <param name="lado1">Um lado do rectângulo (m); 0 se não for rectângulo.</param>
        /// <param name="lado2">O outro lado (m); 0 se não for rectângulo.</param>
        /// <param name="emAlcado">A medição é de fachada/alçado.</param>
        /// <param name="alturaPainel">Altura a usar em planta, vinda do painel.</param>
        /// <returns>false quando o resultado não é um vão plausível.</returns>
        public static bool DeGeometria(double dx, double dy, double lado1, double lado2,
            bool emAlcado, double alturaPainel, out double larg, out double alt)
        {
            larg = alt = 0;
            if (dx <= 0 && dy <= 0 && lado1 <= 0 && lado2 <= 0) return false;

            if (emAlcado)
            {
                // O alçado desenha-se ao direito: o horizontal é a largura e o
                // vertical é a altura. Não se usa o lado aqui — num alçado o
                // que interessa é qual é qual, não o comprimento de cada um.
                larg = dx;
                alt = dy;
            }
            else
            {
                // Planta: a largura do vão é o maior dos dois lados. Sem
                // rectângulo (polyline solta, hachura) fica a maior extensão.
                double maior = Math.Max(lado1, lado2);
                if (maior <= 0) maior = Math.Max(dx, dy);
                larg = maior;
                alt = alturaPainel;
            }

            larg = Math.Round(larg, 3);
            alt = Math.Round(alt, 3);

            return larg >= LargMin && larg <= LargMax &&
                   alt >= AltMin && alt <= AltMax;
        }

        /// <summary>
        /// Número escrito à portuguesa ou à inglesa. Devolve 0 no que não for
        /// número — quem chama trata o 0 como "não serve".
        /// </summary>
        public static double ParseNum(string s)
        {
            if (s == null) return 0;
            s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        /// <summary>
        /// Designação encontrada numa linha de texto, em maiúsculas e sem
        /// espaços, ou <c>null</c>.
        /// </summary>
        public static string Designacao(string linha)
        {
            if (string.IsNullOrEmpty(linha)) return null;
            var m = RxDesignacao.Match(linha);
            return m.Success ? m.Value.ToUpperInvariant().Replace(" ", "") : null;
        }
    }
}
