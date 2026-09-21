using System;
using System.Collections.Generic;
using System.Linq;

namespace TSKTakeOff
{
    /// <summary>
    /// Ordena candidatos a artigo pela relevância a um contexto — tipicamente
    /// o Serviço da medição por classificar.
    ///
    /// NÃO É IA: é uma pontuação determinística por sobreposição de palavras,
    /// sem chamada externa nem custo de rede. Existe para o ArtigoDialog não
    /// obrigar a escrever o código de cor — os candidatos mais prováveis já
    /// aparecem no topo antes de se escrever nada na procura — mas continua a
    /// ser a pessoa a escolher: isto ordena, nunca classifica sozinho.
    ///
    /// Ficheiro puro de propósito (sem Autodesk.*/System.Windows.Forms), como
    /// ResultadosModelo.cs/ResultadosArvore.cs — testável sem AutoCAD.
    /// </summary>
    public static class SugestaoArtigo
    {
        /// <summary>Um candidato a artigo, reduzido ao que a pontuação precisa.</summary>
        public struct Candidato
        {
            public readonly string Codigo;
            public readonly string Designacao;

            public Candidato(string codigo, string designacao)
            {
                Codigo = codigo ?? "";
                Designacao = designacao ?? "";
            }
        }

        /// <summary>
        /// Pontos de <paramref name="candidato"/> face a <paramref name="contexto"/>:
        /// soma do comprimento de cada palavra do contexto (3+ letras, para
        /// "de"/"em"/"um" não votarem em tudo) que aparece na designação ou
        /// no código do candidato. Palavras maiores pesam mais — "ALVENARIA"
        /// diz mais do que "COM".
        /// </summary>
        public static int Pontuacao(string contexto, Candidato candidato)
        {
            if (string.IsNullOrWhiteSpace(contexto)) return 0;

            string alvo = (candidato.Designacao + " " + candidato.Codigo).ToUpperInvariant();
            int pontos = 0;
            foreach (var palavra in Palavras(contexto))
                if (alvo.Contains(palavra)) pontos += palavra.Length;
            return pontos;
        }

        /// <summary>
        /// Reordena a lista pela pontuação, do maior para o menor. ESTÁVEL:
        /// entre empates — incluindo pontuação 0, quando não há contexto ou
        /// nenhuma palavra bate — mantém a ordem de entrada, que já é a
        /// ordem do articulado. Sem contexto, devolve a lista tal como veio.
        /// </summary>
        public static List<T> Ordenar<T>(string contexto, IList<T> candidatos,
            Func<T, Candidato> paraCandidato)
        {
            if (candidatos == null) return new List<T>();
            if (string.IsNullOrWhiteSpace(contexto)) return candidatos.ToList();

            return candidatos
                .Select((item, indice) => new
                {
                    item,
                    indice,
                    pontos = Pontuacao(contexto, paraCandidato(item))
                })
                .OrderByDescending(x => x.pontos)
                .ThenBy(x => x.indice)
                .Select(x => x.item)
                .ToList();
        }

        private static IEnumerable<string> Palavras(string texto)
        {
            foreach (var p in texto.ToUpperInvariant().Split(
                new[] { ' ', '-', '/', ',', '.', '_' }, StringSplitOptions.RemoveEmptyEntries))
                if (p.Length >= 3) yield return p;
        }
    }
}
