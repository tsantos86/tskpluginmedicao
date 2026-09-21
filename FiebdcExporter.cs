using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace TSKTakeOff
{
    /// <summary>
    /// Exporta quantidades para FIEBDC-3 (.bc3) — o formato de intercâmbio de
    /// medições/orçamentos que Arquimedes, CYPECAD, Presto e TCQ já sabem
    /// abrir em Portugal e Espanha. Dá ao TSK TakeOff uma saída para esse
    /// ecossistema, ao lado do Excel — sem mexer em nada do que já existe.
    ///
    /// PRIMEIRA VERSÃO, POR CIMA DE DOCUMENTAÇÃO PÚBLICA — NÃO TESTADA
    /// CONTRA IMPORTAÇÃO REAL.
    ///
    /// A estrutura de registos (~V, ~C, ~D, ~M, ~T: separador de campo `|`,
    /// separador de subcampo `\`, registo termina no `~` seguinte ou fim de
    /// ficheiro) foi confirmada contra a documentação do parser open-source
    /// ogorhc/BC3 (github.com/ogorhc/BC3, docs/parser/grammar.md e
    /// record-parsers.md) — não foi possível ler o PDF oficial da associação
    /// FIEBDC directamente nesta sessão (stream comprimido; sem
    /// poppler-utils instalado para o extrair). A codificação Latin-1
    /// (ISO-8859-1) e o separador `|` estão confirmados; o detalhe exacto
    /// dos subcampos de UMA linha de medição por factores (comprimento ×
    /// altura, etc.) tem menos confirmação — por isso esta versão exporta só
    /// o TOTAL por artigo em ~M, sem tentar reproduzir a decomposição em
    /// factores. Antes de entregar a um cliente, importe o .bc3 gerado no
    /// Arquimedes/CYPECAD que tiverem à mão e confiram os totais.
    /// </summary>
    public static class FiebdcExporter
    {
        /// <summary>Codificação exigida pelo standard — nunca UTF-8.</summary>
        public static readonly Encoding CodificacaoFicheiro = Encoding.GetEncoding("ISO-8859-1");

        /// <summary>Uma quantidade medida, já agregada por piso e artigo.</summary>
        public struct Linha
        {
            public readonly string Piso;
            public readonly string Codigo;
            public readonly string Designacao;
            public readonly string Unidade;
            public readonly double Quantidade;

            public Linha(string piso, string codigo, string designacao,
                string unidade, double quantidade)
            {
                Piso = piso ?? "";
                Codigo = codigo ?? "";
                Designacao = designacao ?? "";
                Unidade = unidade ?? "";
                Quantidade = quantidade;
            }
        }

        /// <summary>O código do conceito-raiz, sob o qual todos os artigos se decompõem.</summary>
        public const string CodigoRaiz = "TSK";

        /// <summary>
        /// Gera o conteúdo do ficheiro .bc3 (texto; o chamador grava em disco
        /// com <see cref="CodificacaoFicheiro"/>, nunca UTF-8).
        /// </summary>
        public static string Gerar(IEnumerable<Linha> linhas, string nomeObra, DateTime data)
        {
            var validas = (linhas ?? Enumerable.Empty<Linha>())
                .Where(l => !string.IsNullOrWhiteSpace(l.Codigo))
                .ToList();

            // Um artigo pode aparecer em vários pisos — o conceito só entra
            // uma vez; as quantidades desse artigo somam-se no total do ~M.
            var porArtigo = new List<KeyValuePair<string, List<Linha>>>();
            var indice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in validas)
            {
                int i;
                if (!indice.TryGetValue(l.Codigo, out i))
                {
                    i = porArtigo.Count;
                    indice[l.Codigo] = i;
                    porArtigo.Add(new KeyValuePair<string, List<Linha>>(
                        l.Codigo, new List<Linha>()));
                }
                porArtigo[i].Value.Add(l);
            }

            var sb = new StringBuilder();
            EscreverCabecalho(sb, nomeObra, data);
            EscreverConceito(sb, CodigoRaiz, "", LimitarTexto(nomeObra, 80));

            if (porArtigo.Count > 0)
                EscreverDecomposicao(sb, CodigoRaiz, porArtigo.Select(p => p.Key).ToList());

            foreach (var par in porArtigo)
            {
                var primeira = par.Value[0];
                EscreverConceito(sb, par.Key, primeira.Unidade,
                    LimitarTexto(primeira.Designacao, 250));

                double total = par.Value.Sum(l => l.Quantidade);
                EscreverMedicao(sb, par.Key, total);

                // O detalhe por piso fica em texto — ver a nota no topo do
                // ficheiro sobre a incerteza dos subcampos de factores do
                // ~M. Preserva a informação sem arriscar um número errado
                // por causa de um subcampo mal colocado.
                string detalhe = string.Join(" · ", par.Value
                    .Where(l => !string.IsNullOrWhiteSpace(l.Piso))
                    .Select(l => l.Piso + ": " + Num(l.Quantidade) + " " + primeira.Unidade));
                if (detalhe.Length > 0)
                    EscreverTexto(sb, par.Key, detalhe);
            }

            return sb.ToString();
        }

        private static void EscreverCabecalho(StringBuilder sb, string nomeObra, DateTime data)
        {
            // ~V|PROPRIEDADE|VERSAO\DATA|PROGRAMA|CABECALHO|CHARSET|COMENTARIO|TIPO|
            sb.Append("~V|Q|FIEBDC-3/2020\\").Append(data.ToString("ddMMyyyy"))
              .Append("|TSK TakeOff|").Append(EscaparTexto(nomeObra))
              .Append("|ANSI||1|\r\n");
        }

        private static void EscreverConceito(StringBuilder sb, string codigo,
            string unidade, string resumo)
        {
            // ~C|CODIGO|UNIDADE|RESUMO|PRECO|DATA|TIPO|
            sb.Append("~C|").Append(EscaparTexto(codigo)).Append('|')
              .Append(EscaparTexto(unidade)).Append('|')
              .Append(EscaparTexto(resumo)).Append("||||\r\n");
        }

        private static void EscreverDecomposicao(StringBuilder sb, string pai,
            IList<string> filhos)
        {
            // ~D|PAI|{FILHO\FACTOR\RENDIMENTO\PERCENTAGEM\}|
            sb.Append("~D|").Append(EscaparTexto(pai)).Append('|');
            foreach (var f in filhos)
                sb.Append(EscaparTexto(f)).Append("\\1\\1\\100\\");
            sb.Append("|\r\n");
        }

        private static void EscreverMedicao(StringBuilder sb, string codigo, double total)
        {
            // ~M|CODIGO|POSICOES|TOTAL|FACTORES|ETIQUETA|
            // POSICOES e FACTORES ficam vazios nesta versão — ver nota no
            // topo do ficheiro.
            sb.Append("~M|").Append(EscaparTexto(codigo)).Append("||")
              .Append(Num(total)).Append("||\r\n");
        }

        private static void EscreverTexto(StringBuilder sb, string codigo, string texto)
        {
            // ~T|CODIGO|TEXTO|
            sb.Append("~T|").Append(EscaparTexto(codigo)).Append('|')
              .Append(EscaparTexto(texto)).Append("|\r\n");
        }

        /// <summary>
        /// Números com ponto decimal (InvariantCulture) — um formato de
        /// intercâmbio entre sistemas não pode depender da vírgula decimal
        /// portuguesa/espanhola, que aqui colidiria com o separador de
        /// campo.
        /// </summary>
        private static string Num(double v)
        {
            return Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Os separadores do formato (`|`, `~`, `\`) não podem sobreviver
        /// dentro de um valor — a especificação consultada não descreve um
        /// mecanismo de escape para eles, por isso a única forma segura é
        /// substituí-los por um espaço em vez de os deixar partir o registo
        /// a meio. Quebras de linha também saem, pela mesma razão.
        /// </summary>
        private static string EscaparTexto(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', ' ').Replace('~', ' ').Replace('\\', ' ')
                     .Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private static string LimitarTexto(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }
    }
}
