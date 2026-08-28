namespace TSKTakeOff
{
    /// <summary>
    /// Mapa de colunas de uma folha no estilo "Item / Designação / Un / Qt /
    /// Comp / Largura / Altura / Parcial / Sub total / Totais" — a mesma
    /// convenção que a <see cref="FolhaMedicao"/> já usa, só que aqui as
    /// colunas e a linha do cabeçalho vêm de um ficheiro real do cliente, não
    /// são fixas.
    ///
    /// Existe porque nem todo o modelo tem o formato do "XX.xls" (art /
    /// descrição, cabeçalho na linha 5): há ficheiros de obra normais, com
    /// cabeçalho "Item / Designação…" numa linha qualquer, por cima dos quais
    /// pode já vir um bloco com o nome da obra, empreitada, data, etc.
    ///
    /// Além das colunas, guarda as LINHAS-EXEMPLO do próprio modelo. É delas
    /// que se copia o aspecto — faixas de capítulo, negritos, casas decimais,
    /// bandas de cor — em vez de o plugin inventar um estilo próprio. Assim a
    /// folha sai igual à que a casa já usa, seja qual for o modelo.
    /// </summary>
    public class MapaItem
    {
        public int LinhaCabecalho;
        public int ColItem = -1;
        public int ColDesc = -1;
        public int ColUn = -1;
        public int ColQt = -1;
        public int ColComp = -1;
        public int ColLarg = -1;
        public int ColAlt = -1;
        public int ColParcial = -1;
        public int ColSubTotal = -1;
        public int ColTotais = -1;

        // ---- linhas do modelo que servem de molde ao estilo ----
        /// <summary>Faixa de capítulo (linha de título com fundo preenchido).</summary>
        public int LinhaCapitulo = -1;
        /// <summary>Subtítulo sem fundo (sub-capítulo, alçado, piso).</summary>
        public int LinhaSubtitulo = -1;
        /// <summary>Linha de artigo: tem unidade e o total do bloco.</summary>
        public int LinhaArtigo = -1;
        /// <summary>Linha de medição: quantidade, dimensões e fórmula da unitária.</summary>
        public int LinhaMedicao = -1;

        // ---- fórmulas do modelo, em R1C1 (independentes da linha) ----
        /// <summary>Fórmula da coluna "Unitária"/"Parcial" numa linha de medição.</summary>
        public string FormulaUnitariaR1C1;
        /// <summary>Fórmula da coluna "Totais" numa linha de medição (normalmente "=RC[-2]").</summary>
        public string FormulaTotalLinhaR1C1;

        /// <summary>O mínimo para ser utilizável: descrição e as três dimensões.</summary>
        public bool Valido =>
            ColDesc >= 0 && ColComp >= 0 && ColLarg >= 0 && ColAlt >= 0;

        /// <summary>Há linhas-exemplo suficientes para copiar o aspecto do modelo.</summary>
        public bool TemEstilo => LinhaMedicao > 0;

        /// <summary>
        /// Procura este cabeçalho numa linha da folha. <paramref name="celula"/>
        /// devolve o texto (em minúsculas, sem espaços) de uma célula.
        /// </summary>
        public static MapaItem Detectar(int linha, System.Func<int, string> celula, int maxColunas = 15)
        {
            var m = new MapaItem { LinhaCabecalho = linha };

            for (int c = 1; c <= maxColunas; c++)
            {
                string t = celula(c) ?? "";
                if (t.Length == 0) continue;

                if (m.ColItem < 0 && t == "item") m.ColItem = c;
                else if (m.ColDesc < 0 && t.StartsWith("designa")) m.ColDesc = c;
                else if (m.ColUn < 0 && (t == "un" || t.StartsWith("unid"))) m.ColUn = c;
                else if (m.ColQt < 0 && (t == "qt" || t.StartsWith("quant"))) m.ColQt = c;
                else if (m.ColComp < 0 && t.Contains("comp")) m.ColComp = c;
                else if (m.ColLarg < 0 && t.Contains("larg")) m.ColLarg = c;
                else if (m.ColAlt < 0 && t.Contains("alt")) m.ColAlt = c;
                else if (m.ColParcial < 0 && (t.Contains("parcial") || t.Contains("unit"))) m.ColParcial = c;
                else if (m.ColSubTotal < 0 && t.Contains("sub")) m.ColSubTotal = c;
                // "Totais" não contém a substring "total" (falta o "l" antes do "s"),
                // por isso testa-se o prefixo "tot" em vez de "contains".
                else if (m.ColTotais < 0 && t.StartsWith("tot") && !t.Contains("sub")) m.ColTotais = c;
            }

            return m.Valido ? m : null;
        }

        /// <summary>Primeira coluna que o plugin escreve ou formata.</summary>
        public int PrimeiraColuna
        {
            get { return ColItem > 0 ? ColItem : ColDesc; }
        }

        /// <summary>Última coluna da tabela, para limpar e formatar a faixa toda.</summary>
        public int UltimaColuna
        {
            get
            {
                int c = ColAlt;
                if (ColParcial > c) c = ColParcial;
                if (ColSubTotal > c) c = ColSubTotal;
                if (ColTotais > c) c = ColTotais;
                return c;
            }
        }
    }
}
