using System;

namespace TSKTakeOff
{
    /// <summary>
    /// Comparação de códigos do articulado: "1.1.1", "3.1", "10.1.1.306A".
    ///
    /// Vive num ficheiro próprio, sem uma linha de AutoCAD, porque é a peça
    /// que decide a ORDEM POR QUE A MEDIÇÃO SE ENTREGA — e essa engana-se em
    /// silêncio. Comparar códigos como texto põe o "10" antes do "9" e o
    /// resultado continua a parecer uma folha bem feita: ninguém desconfia de
    /// uma lista ordenada, só se dá por isso ao conferir contra o mapa do
    /// cliente, linha a linha. Aqui pode ser posto à prova sem abrir o
    /// AutoCAD, e está (ver Tests/CodigoArticuladoTests.cs).
    /// </summary>
    public static class CodigoArticulado
    {
        /// <summary>
        /// Compara dois códigos como quem lê o mapa: segmento a segmento, e
        /// cada segmento pelo número que lá está.
        ///
        /// Negativo se <paramref name="a"/> vem primeiro, positivo se vem
        /// depois, zero se ocupam o mesmo lugar.
        /// </summary>
        public static int Comparar(string a, string b)
        {
            string ca = (a ?? "").Trim().TrimEnd('.');
            string cb = (b ?? "").Trim().TrimEnd('.');

            // Um código que não começa por dígito não pertence à numeração — é
            // o "Sem Ref" que aparece nos ficheiros reais. Vai para o fim, onde
            // não se intromete na hierarquia de quem tem código a sério.
            bool na = ca.Length > 0 && char.IsDigit(ca[0]);
            bool nb = cb.Length > 0 && char.IsDigit(cb[0]);
            if (na != nb) return na ? -1 : 1;
            if (!na) return string.Compare(ca, cb, StringComparison.OrdinalIgnoreCase);

            string[] pa = ca.Split('.');
            string[] pb = cb.Split('.');
            int n = Math.Min(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int c = CompararSegmento(pa[i], pb[i]);
                if (c != 0) return c;
            }

            // "1.1" antes de "1.1.1": o capítulo abre o que lhe pertence.
            return pa.Length.CompareTo(pb.Length);
        }

        private static int CompararSegmento(string a, string b)
        {
            int na, nb; string sa, sb;
            Partir(a, out na, out sa);
            Partir(b, out nb, out sb);
            if (na != nb) return na.CompareTo(nb);
            return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// "306A" dá 306 e "A". O sufixo de letra existe mesmo: o
        /// "10.1.1.306A" veio de um mapa de obra a sério.
        /// </summary>
        private static void Partir(string s, out int numero, out string resto)
        {
            s = (s ?? "").Trim();
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;

            numero = -1;
            // Um segmento que não caiba num int não existe em mapa nenhum, mas
            // se aparecer vale -1 e desempata-se pelo texto: ordenar mal um
            // caso impossível e melhor do que rebentar a leitura do mapa.
            if (i > 0 && !int.TryParse(s.Substring(0, i), out numero)) numero = -1;

            resto = s.Substring(i);
        }
    }
}
