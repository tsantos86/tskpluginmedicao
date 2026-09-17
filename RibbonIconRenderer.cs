using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace TSKTakeOff
{
    /// <summary>
    /// Prepara os bitmaps da Ribbon nos tamanhos nativos do AutoCAD.
    ///
    /// Os desenhos do <see cref="IconFactory"/> vivem numa grelha de 64 px.
    /// A Ribbon, porém, pede 32 px para botões grandes e 16 px quando um painel
    /// é compactado. Gerar cada tamanho explicitamente mantém o alfa e deixa
    /// uma margem ótica constante.
    /// </summary>
    internal static class RibbonIconRenderer
    {
        internal const float EscalaConteudo = 0.84f;

        internal static Bitmap Preparar(Bitmap origem, int lado)
        {
            if (origem == null) throw new ArgumentNullException(nameof(origem));
            if (lado <= 0) throw new ArgumentOutOfRangeException(nameof(lado));

            var destino = new Bitmap(lado, lado, PixelFormat.Format32bppArgb);
            destino.SetResolution(96f, 96f);

            using (var g = Graphics.FromImage(destino))
            {
                g.Clear(Color.Transparent);
                g.CompositingMode = CompositingMode.SourceOver;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                float conteudo = lado * EscalaConteudo;
                float margem = (lado - conteudo) / 2f;
                g.DrawImage(origem,
                    new RectangleF(margem, margem, conteudo, conteudo),
                    new RectangleF(0, 0, origem.Width, origem.Height),
                    GraphicsUnit.Pixel);
            }

            return destino;
        }
    }
}
