using System.Collections.Generic;

namespace TSKTakeOff
{
    /// <summary>
    /// Decisões de disposição da folha que não dependem do COM do Excel.
    /// Mantê-las aqui permite testar casos que, de outro modo, só apareceriam
    /// ao abrir um modelo real no Excel.
    /// </summary>
    public static class ExcelLayout
    {
        /// <summary>
        /// Todas as linhas geradas cujo tamanho deve ser normalizado antes de
        /// receber o estilo do modelo. Inclui deliberadamente as linhas vazias:
        /// ClearFormats não repõe RowHeight e uma linha vazia pode herdar os
        /// 120+ pontos de uma descrição longa existente no ficheiro-modelo.
        /// </summary>
        public static List<int> LinhasParaNormalizar(
            Dictionary<TipoLinha, List<int>> porTipo)
        {
            var unicas = new HashSet<int>();
            if (porTipo != null)
                foreach (var grupo in porTipo.Values)
                    if (grupo != null)
                        foreach (int linha in grupo)
                            if (linha > 0) unicas.Add(linha);

            var linhas = new List<int>(unicas);
            linhas.Sort();
            return linhas;
        }
    }
}
