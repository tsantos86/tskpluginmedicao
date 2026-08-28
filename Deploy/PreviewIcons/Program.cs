using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using TSKTakeOff;

namespace PreviewIcons
{
    /// <summary>
    /// Gera uma imagem com todos os ícones do plugin, para ver o resultado
    /// sem abrir o AutoCAD. Desenha cada ícone na fonte (64), na ribbon
    /// (32), na palette (24) e no colapso compacto (16), e repete tudo sobre o fundo
    /// escuro e o fundo claro — o AutoCAD tem os dois temas, e um ícone que
    /// só lê num deles está meio feito.
    /// </summary>
    internal static class Program
    {
        private static readonly string[] Nomes =
        {
            "ParedeRet", "ParedePoly", "Retangulo", "Area", "AreaSel", "MedirSel", "Linear",
            "Polf", "Vao", "Contagem", "Qselect", "Excel", "Exportar", "Modelo",
            "Mapa", "Macro MD", "Macro EO", "Macro QT", "Painel", "Licenca", "Sobre",
            "Atualizar", "Capitulo", "Artigo", "Reclassificar", "LinhaBranca", "Limpar", "Remover"
        };

        private static void Main()
        {
            int cols = 7, cell = 158, faixa = 30;
            int rows = (Nomes.Length + cols - 1) / cols;
            int altura = rows * cell + faixa;

            using (var bmp = new Bitmap(cols * cell, altura * 2))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                Painel(g, 0, cols, cell, faixa, altura,
                       Color.FromArgb(56, 56, 62), Color.White, "TEMA ESCURO  ·  64 / 32 / 24 / 16 px");
                Painel(g, altura, cols, cell, faixa, altura,
                       Color.FromArgb(238, 238, 242), Color.Black, "TEMA CLARO  ·  64 / 32 / 24 / 16 px");

                string saida = System.IO.Path.Combine(
                    AppContext.BaseDirectory, "..\\..\\..\\..", "preview-icones.png");
                bmp.Save(saida, ImageFormat.Png);
                Console.WriteLine("Gravado: " + System.IO.Path.GetFullPath(saida));
            }
        }

        private static void Painel(Graphics g, int topo, int cols, int cell, int faixa,
                                   int altura, Color fundo, Color texto, string titulo)
        {
            using (var pincelFundo = new SolidBrush(fundo))
                g.FillRectangle(pincelFundo, 0, topo, cols * cell, altura);

            using (var pincel = new SolidBrush(texto))
            using (var fonteTitulo = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var fonte = new Font("Segoe UI", 7.5f))
            {
                g.DrawString(titulo, fonteTitulo, pincel, 8, topo + 4);

                var tipo = typeof(IconFactory);
                for (int i = 0; i < Nomes.Length; i++)
                {
                    string nome = Nomes[i];
                    Bitmap icone = nome.StartsWith("Macro ")
                        ? (Bitmap)tipo.GetMethod("Macro").Invoke(null, new object[] { nome.Substring(6) })
                        : (Bitmap)tipo.GetMethod(nome).Invoke(null, null);

                    ValidarIcone(nome, icone);

                    int x = (i % cols) * cell + 8;
                    int y = (i / cols) * cell + topo + faixa + 8;

                    // Escala ótica real: fonte, ribbon, palette e ribbon colapsada.
                    g.DrawImage(icone, x, y, 64, 64);
                    g.DrawImage(icone, x + 68, y + 16, 32, 32);
                    g.DrawImage(icone, x + 104, y + 20, 24, 24);
                    g.DrawImage(icone, x + 132, y + 24, 16, 16);

                    g.DrawString(nome, fonte, pincel, x, y + 68);
                    icone.Dispose();
                }
            }
        }

        private static void ValidarIcone(string nome, Bitmap icone)
        {
            if (icone.Width != 64 || icone.Height != 64)
                throw new InvalidOperationException(nome + " não devolveu um bitmap 64 × 64.");
            if (!Image.IsAlphaPixelFormat(icone.PixelFormat))
                throw new InvalidOperationException(nome + " não preservou o canal alfa.");

            if (icone.GetPixel(0, 0).A != 0 || icone.GetPixel(63, 0).A != 0 ||
                icone.GetPixel(0, 63).A != 0 || icone.GetPixel(63, 63).A != 0)
                throw new InvalidOperationException(nome + " ocupa os cantos do canvas transparente.");
        }
    }
}
