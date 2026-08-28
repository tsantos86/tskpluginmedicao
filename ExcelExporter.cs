using System.Collections.Generic;
using ClosedXML.Excel;
using static TSKTakeOff.FolhaMedicao;

namespace TSKTakeOff
{
    /// <summary>
    /// Escreve a folha de medição em .xlsx com o mesmo aspecto do modelo da casa:
    /// cabeçalho Articulado / Dimensões, numeração A001…, designação em itálico
    /// à direita, deduções a vermelho e Sub total / Totais por alçado e capítulo.
    /// </summary>
    public static class ExcelExporter
    {
        private const string CorCabGrupo = "#ED7D31";  // laranja
        private const string CorCabCols = "#BFBFBF";   // cinzento
        private const string CorCapitulo = "#EBF1DE";  // verde muito claro
        private const string CorAlcado = "#DCE6F1";    // azul claro

        public static void Export(string path, IList<Parede> paredes,
            IList<MedItem> lineares, IList<MedFachada> fachadas, RegraDesconto regra)
        {
            var linhas = FolhaMedicao.Construir(paredes, fachadas, lineares, regra);

            using (var wb = new XLWorkbook())
            {
                var ws = wb.Worksheets.Add("Medições");
                Cabecalho(ws);

                int row = 2;
                foreach (var l in linhas)
                {
                    Escrever(ws, row, l);
                    row++;
                }

                ws.Cell(row + 1, C_DESC).Value = "Regra de vãos: " + Config.RegraDescricao(regra);
                ws.Cell(row + 1, C_DESC).Style.Font.Italic = true;

                Formatar(ws, row - 1);
                wb.SaveAs(path);
            }
        }

        // ------------------------------------------------------------------
        private static void Cabecalho(IXLWorksheet ws)
        {
            for (int c = 0; c < Cabecalho_.Length; c++)
                ws.Cell(1, c + 1).Value = Cabecalho_[c];

            var r = ws.Range(1, 1, 1, Cabecalho_.Length);
            r.Style.Font.Bold = true;
            r.Style.Font.FontSize = 11;
            r.Style.Fill.BackgroundColor = XLColor.FromHtml(CorCabCols);
            r.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            r.Style.Alignment.WrapText = true;
            r.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Row(1).Height = 30;
            ws.SheetView.FreezeRows(1);
        }

        private static string[] Cabecalho_ => FolhaMedicao.Cabecalho;

        // ------------------------------------------------------------------
        private static void Escrever(IXLWorksheet ws, int row, LinhaFolha l)
        {
            if (l.Tipo == TipoLinha.Vazia)
            {
                // Nunca atribuir null a uma célula: em certas versões do
                // ClosedXML isso lança InvalidOperationException e parte a
                // exportação a meio do ficheiro.
                ws.Cell(row, C_ITEM).Value = l.Item ?? "";
                EstiloItem(ws, row);
                return;
            }

            ws.Cell(row, C_ITEM).Value = l.Item ?? "";
            EstiloItem(ws, row);
            ws.Cell(row, C_DESC).Value = l.Designacao ?? "";

            if (!string.IsNullOrEmpty(l.Un)) ws.Cell(row, C_UN).Value = l.Un;
            if (l.Qt.HasValue) ws.Cell(row, C_QT).Value = l.Qt.Value;
            if (l.Comp.HasValue) ws.Cell(row, C_COMP).Value = l.Comp.Value;
            if (l.Largura.HasValue) ws.Cell(row, C_LARG).Value = l.Largura.Value;
            if (l.Altura.HasValue) ws.Cell(row, C_ALT).Value = l.Altura.Value;

            if (l.ValorParcial.HasValue)
                ws.Cell(row, C_PARC).Value = l.ValorParcial.Value;
            else if (l.TemParcial)
                ws.Cell(row, C_PARC).FormulaA1 =
                    FormulaParcial(row, l.Largura.HasValue, l.Altura.HasValue);

            if (!string.IsNullOrEmpty(l.FormulaSubTotal))
                ws.Cell(row, C_SUB).FormulaA1 = l.FormulaSubTotal;
            if (!string.IsNullOrEmpty(l.FormulaTotais))
                ws.Cell(row, C_TOT).FormulaA1 = l.FormulaTotais;

            switch (l.Tipo)
            {
                // Títulos inseridos à mão: saem vazios, só com o realce, para a
                // pessoa lá escrever o texto.
                case TipoLinha.TituloCapitulo:
                    {
                        var r = ws.Range(row, 1, row, C_TOT);
                        r.Style.Font.Bold = true;
                        r.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9D9D9");
                        ws.Row(row).Height = 22;
                        break;
                    }
                case TipoLinha.TituloArtigo:
                    {
                        var r = ws.Range(row, 1, row, C_TOT);
                        r.Style.Font.Bold = true;
                        r.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                        ws.Row(row).Height = 28;
                        break;
                    }
                case TipoLinha.Capitulo:
                    {
                        var r = ws.Range(row, 1, row, C_TOT);
                        r.Style.Font.Bold = true;
                        r.Style.Font.FontSize = 13;
                        r.Style.Fill.BackgroundColor = XLColor.FromHtml(CorCapitulo);
                        ws.Cell(row, C_DESC).Style.Alignment.Horizontal =
                            XLAlignmentHorizontalValues.Center;
                        ws.Row(row).Height = 24;
                        break;
                    }
                case TipoLinha.Alcado:
                    {
                        var r = ws.Range(row, 1, row, C_TOT);
                        r.Style.Font.Bold = true;
                        r.Style.Fill.BackgroundColor = XLColor.FromHtml(CorAlcado);
                        ws.Cell(row, C_DESC).Style.Alignment.Horizontal =
                            XLAlignmentHorizontalValues.Center;
                        break;
                    }
                case TipoLinha.Piso:
                    ws.Cell(row, C_DESC).Style.Font.Bold = true;
                    ws.Cell(row, C_DESC).Style.Alignment.Horizontal =
                        XLAlignmentHorizontalValues.Center;
                    break;

                case TipoLinha.Medicao:
                    ws.Cell(row, C_DESC).Style.Font.Italic = true;
                    ws.Cell(row, C_DESC).Style.Alignment.Horizontal =
                        XLAlignmentHorizontalValues.Right;
                    break;

                case TipoLinha.Deducao:
                    {
                        var r = ws.Range(row, C_DESC, row, C_PARC);
                        r.Style.Font.FontColor = XLColor.Red;
                        ws.Cell(row, C_DESC).Style.Font.Italic = true;
                        ws.Cell(row, C_DESC).Style.Alignment.Horizontal =
                            XLAlignmentHorizontalValues.Right;
                        break;
                    }
            }

            // Descrições longas devem continuar legíveis no ficheiro entregue:
            // quebra de linha e altura proporcional, sem alterar o texto.
            var descricao = ws.Cell(row, C_DESC);
            descricao.Style.Alignment.WrapText = true;
            descricao.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            if (!string.IsNullOrEmpty(l.Designacao))
            {
                int linhasDescricao = (l.Designacao.Length + 69) / 70;
                double altura = System.Math.Min(409.0,
                    System.Math.Max(15.0, linhasDescricao * 15.0));
                if (ws.Row(row).Height < altura) ws.Row(row).Height = altura;
            }

            // A coluna Totais só fica a negrito na linha que contém a soma.
            // As restantes linhas podem ter outros realces, mas J não os herda.
            ws.Cell(row, C_TOT).Style.Font.Bold =
                !string.IsNullOrEmpty(l.FormulaTotais);
        }

        private static void EstiloItem(IXLWorksheet ws, int row)
        {
            var c = ws.Cell(row, C_ITEM);
            c.Style.Font.FontSize = 9;
            c.Style.Font.FontColor = XLColor.FromHtml("#7F7F7F");
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        // ------------------------------------------------------------------
        private static void Formatar(IXLWorksheet ws, int ultima)
        {
            if (ultima >= 2)
            {
                ws.Range(2, C_QT, ultima, C_QT).Style.NumberFormat.Format = "0";
                foreach (int c in new[] { C_COMP, C_LARG, C_ALT, C_PARC, C_SUB, C_TOT })
                    ws.Range(2, c, ultima, c).Style.NumberFormat.Format = "0.00";

                ws.Range(2, C_UN, ultima, C_TOT).Style.Alignment.Horizontal =
                    XLAlignmentHorizontalValues.Center;
            }

            ws.Column(C_ITEM).Width = 8;
            // A descrição do articulado pode ter várias centenas de caracteres.
            // 80 dá espaço suficiente sem esmagar as colunas de medição; o
            // WrapText/altura acima garante que o texto não fica escondido.
            ws.Column(C_DESC).Width = 80;
            ws.Column(C_UN).Width = 7;
            foreach (int c in new[] { C_QT, C_COMP, C_LARG, C_ALT, C_PARC, C_SUB, C_TOT })
                ws.Column(c).Width = 12;
        }
    }
}
