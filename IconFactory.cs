using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace TSKTakeOff
{
    /// <summary>
    /// Família de ícones TSK desenhada em GDI+ numa grelha ótica de 64 × 64.
    /// A linguagem visual combina geometria CAD, keyline escura, cor luminosa
    /// e uma aresta de realce para manter definição nos temas claro e escuro.
    /// </summary>
    public static class IconFactory
    {
        private const int Size = 64;
        private const float MainStroke = 3.2f;
        private const float DetailStroke = 2.6f;
        private const float KeylineExtra = 2.2f;

        private static readonly Color Ink = Color.FromArgb(226, 24, 34, 48);
        private static readonly Color Cobalt = Color.FromArgb(77, 157, 244);
        private static readonly Color CobaltDeep = Color.FromArgb(39, 97, 171);
        private static readonly Color Sky = Color.FromArgb(166, 215, 255);
        private static readonly Color Slate = Color.FromArgb(174, 190, 209);
        private static readonly Color SlateDeep = Color.FromArgb(99, 118, 143);
        private static readonly Color Emerald = Color.FromArgb(62, 199, 130);
        private static readonly Color Amber = Color.FromArgb(244, 188, 62);
        private static readonly Color Coral = Color.FromArgb(255, 105, 94);
        private static readonly Color Paper = Color.FromArgb(238, 244, 250);

        private static Bitmap NewCanvas(out Graphics g)
        {
            var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
            bmp.SetResolution(96f, 96f);
            g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.CompositingMode = CompositingMode.SourceOver;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            return bmp;
        }

        private static Pen RoundPen(Color color, float width)
        {
            return new Pen(color, width)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath PolygonPath(PointF[] points)
        {
            var path = new GraphicsPath();
            path.AddPolygon(points);
            return path;
        }

        private static GraphicsPath LinesPath(PointF[] points)
        {
            var path = new GraphicsPath();
            path.AddLines(points);
            return path;
        }

        private static void StrokePath(Graphics g, GraphicsPath path, Color color, float width)
        {
            using (var keyline = RoundPen(Ink, width + KeylineExtra))
            using (var stroke = RoundPen(color, width))
            {
                g.DrawPath(keyline, path);
                g.DrawPath(stroke, path);
            }
        }

        private static void StrokePath(Graphics g, GraphicsPath path, Color color)
        {
            StrokePath(g, path, color, MainStroke);
        }

        /// <summary>
        /// Traço para detalhes internos. Conserva uma keyline mínima, apenas
        /// para estabilizar o contraste, sem duplicar o peso do contorno exterior.
        /// </summary>
        private static void InnerStrokePath(Graphics g, GraphicsPath path, Color color, float width)
        {
            using (var edge = RoundPen(Color.FromArgb(155, Ink), width + 0.9f))
            using (var stroke = RoundPen(color, width))
            {
                g.DrawPath(edge, path);
                g.DrawPath(stroke, path);
            }
        }

        private static void FillPath(Graphics g, GraphicsPath path, RectangleF bounds,
                                     Color color, int alphaTop, int alphaBottom)
        {
            using (var fill = new LinearGradientBrush(
                bounds,
                Color.FromArgb(alphaTop, color),
                Color.FromArgb(alphaBottom, color),
                LinearGradientMode.Vertical))
            {
                g.FillPath(fill, path);
            }
        }

        private static void Shape(Graphics g, GraphicsPath path, RectangleF bounds,
                                  Color color, int alphaTop, int alphaBottom, float width)
        {
            FillPath(g, path, bounds, color, alphaTop, alphaBottom);
            StrokePath(g, path, color, width);
        }

        private static void Shape(Graphics g, GraphicsPath path, RectangleF bounds, Color color)
        {
            Shape(g, path, bounds, color, 126, 46, MainStroke);
        }

        private static void Line(Graphics g, Color color, float width,
                                 float x1, float y1, float x2, float y2)
        {
            using (var path = new GraphicsPath())
            {
                path.AddLine(x1, y1, x2, y2);
                StrokePath(g, path, color, width);
            }
        }

        private static void Line(Graphics g, Color color,
                                 float x1, float y1, float x2, float y2)
        {
            Line(g, color, MainStroke, x1, y1, x2, y2);
        }

        private static void InnerLine(Graphics g, Color color, float width,
                                      float x1, float y1, float x2, float y2)
        {
            using (var path = new GraphicsPath())
            {
                path.AddLine(x1, y1, x2, y2);
                InnerStrokePath(g, path, color, width);
            }
        }

        private static void InnerPolyline(Graphics g, Color color, PointF[] points, float width)
        {
            using (var path = LinesPath(points))
                InnerStrokePath(g, path, color, width);
        }

        private static void Polyline(Graphics g, Color color, PointF[] points, float width)
        {
            using (var path = LinesPath(points))
                StrokePath(g, path, color, width);
        }

        private static void Polyline(Graphics g, Color color, PointF[] points)
        {
            Polyline(g, color, points, MainStroke);
        }

        private static void FilledPolygon(Graphics g, Color color, PointF[] points,
                                          RectangleF bounds, int alphaTop, int alphaBottom)
        {
            using (var path = PolygonPath(points))
                Shape(g, path, bounds, color, alphaTop, alphaBottom, MainStroke);
        }

        private static void FilledPolygon(Graphics g, Color color, PointF[] points, RectangleF bounds)
        {
            FilledPolygon(g, color, points, bounds, 126, 46);
        }

        private static void Panel(Graphics g, RectangleF r, float radius, Color color,
                                  int alphaTop, int alphaBottom)
        {
            using (var path = RoundedRect(r, radius))
                Shape(g, path, r, color, alphaTop, alphaBottom, MainStroke);
        }

        private static void Panel(Graphics g, RectangleF r, float radius, Color color)
        {
            Panel(g, r, radius, color, 126, 46);
        }

        private static void Ellipse(Graphics g, RectangleF r, Color color,
                                    int alphaTop, int alphaBottom, float width)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(r);
                Shape(g, path, r, color, alphaTop, alphaBottom, width);
            }
        }

        private static void Ellipse(Graphics g, RectangleF r, Color color)
        {
            Ellipse(g, r, color, 126, 46, MainStroke);
        }

        private static void Node(Graphics g, float x, float y, Color color)
        {
            using (var halo = new SolidBrush(Ink))
            using (var fill = new SolidBrush(color))
            using (var glint = new SolidBrush(Sky))
            {
                g.FillEllipse(halo, x - 4.1f, y - 4.1f, 8.2f, 8.2f);
                g.FillEllipse(fill, x - 2.8f, y - 2.8f, 5.6f, 5.6f);
                g.FillEllipse(glint, x - 1.6f, y - 1.7f, 2.2f, 2.2f);
            }
        }

        private static void CornerNode(Graphics g, float x, float y, Color color)
        {
            using (var halo = new SolidBrush(Ink))
            using (var fill = new SolidBrush(color))
            {
                g.FillRectangle(halo, x - 4f, y - 4f, 8f, 8f);
                g.FillRectangle(fill, x - 2.7f, y - 2.7f, 5.4f, 5.4f);
            }
        }

        private static void Highlight(Graphics g, float x1, float y1, float x2, float y2)
        {
            using (var pen = RoundPen(Color.FromArgb(220, Sky), DetailStroke))
                g.DrawLine(pen, x1, y1, x2, y2);
        }

        private static void SelectionCorners(Graphics g, RectangleF r, Color color, float length)
        {
            using (var path = new GraphicsPath())
            {
                path.AddLines(new[]
                {
                    new PointF(r.Left, r.Top + length), new PointF(r.Left, r.Top),
                    new PointF(r.Left + length, r.Top)
                });
                path.StartFigure();
                path.AddLines(new[]
                {
                    new PointF(r.Right - length, r.Top), new PointF(r.Right, r.Top),
                    new PointF(r.Right, r.Top + length)
                });
                path.StartFigure();
                path.AddLines(new[]
                {
                    new PointF(r.Left, r.Bottom - length), new PointF(r.Left, r.Bottom),
                    new PointF(r.Left + length, r.Bottom)
                });
                path.StartFigure();
                path.AddLines(new[]
                {
                    new PointF(r.Right - length, r.Bottom), new PointF(r.Right, r.Bottom),
                    new PointF(r.Right, r.Bottom - length)
                });
                InnerStrokePath(g, path, color, 2.8f);
            }
        }

        private static void Cursor(Graphics g, float x, float y, float scale)
        {
            var points = new[]
            {
                new PointF(x, y), new PointF(x + 1f * scale, y + 18f * scale),
                new PointF(x + 5f * scale, y + 14f * scale),
                new PointF(x + 9f * scale, y + 22f * scale),
                new PointF(x + 13f * scale, y + 20f * scale),
                new PointF(x + 9f * scale, y + 12f * scale),
                new PointF(x + 15f * scale, y + 11f * scale)
            };
            using (var path = PolygonPath(points))
            using (var fill = new SolidBrush(Paper))
            {
                g.FillPath(fill, path);
                InnerStrokePath(g, path, Cobalt, DetailStroke);
            }
        }

        private static void ArrowHead(Graphics g, PointF tip, PointF a, PointF b, Color color)
        {
            var points = new[] { tip, a, b };
            using (var path = PolygonPath(points))
            using (var keyline = new SolidBrush(Ink))
            using (var fill = new SolidBrush(color))
            {
                g.FillPath(keyline, path);
                using (var inner = (GraphicsPath)path.Clone())
                using (var matrix = new Matrix())
                {
                    matrix.Translate(-tip.X, -tip.Y);
                    matrix.Scale(0.72f, 0.72f);
                    matrix.Translate(tip.X, tip.Y);
                    inner.Transform(matrix);
                    g.FillPath(fill, inner);
                }
            }
        }

        private static void DimensionH(Graphics g, float x1, float x2, float y, Color color)
        {
            Line(g, color, DetailStroke, x1 + 4f, y, x2 - 4f, y);
            ArrowHead(g, new PointF(x1, y), new PointF(x1 + 7f, y - 4f),
                      new PointF(x1 + 7f, y + 4f), color);
            ArrowHead(g, new PointF(x2, y), new PointF(x2 - 7f, y - 4f),
                      new PointF(x2 - 7f, y + 4f), color);
        }

        private static void DimensionV(Graphics g, float x, float y1, float y2, Color color)
        {
            Line(g, color, DetailStroke, x, y1 + 4f, x, y2 - 4f);
            ArrowHead(g, new PointF(x, y1), new PointF(x - 4f, y1 + 7f),
                      new PointF(x + 4f, y1 + 7f), color);
            ArrowHead(g, new PointF(x, y2), new PointF(x - 4f, y2 - 7f),
                      new PointF(x + 4f, y2 - 7f), color);
        }

        private static GraphicsPath DocumentPath(float x, float y, float width, float height)
        {
            float fold = 11f;
            var path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(x + 2f, y), new PointF(x + width - fold, y),
                new PointF(x + width, y + fold), new PointF(x + width, y + height - 2f),
                new PointF(x + width - 2f, y + height), new PointF(x + 2f, y + height),
                new PointF(x, y + height - 2f), new PointF(x, y + 2f)
            });
            return path;
        }

        private static void Document(Graphics g, RectangleF r, Color color)
        {
            using (var path = DocumentPath(r.X, r.Y, r.Width, r.Height))
                Shape(g, path, r, color, 98, 34, MainStroke);
            InnerLine(g, SlateDeep, DetailStroke,
                      r.Right - 11f, r.Top + 1f, r.Right - 1f, r.Top + 11f);
        }

        private static void Grid(Graphics g, RectangleF r, Color color)
        {
            Document(g, r, color);
            using (var header = new SolidBrush(Color.FromArgb(205, color)))
                g.FillRectangle(header, r.X + 4f, r.Y + 5f, r.Width - 13f, 7f);

            float left = r.X + 5f;
            float right = r.Right - 5f;
            float top = r.Y + 18f;
            float bottom = r.Bottom - 5f;
            InnerLine(g, color, DetailStroke, left + 9f, top, left + 9f, bottom);
            InnerLine(g, color, DetailStroke, left, top + 9f, right, top + 9f);
            InnerLine(g, color, DetailStroke, left, top + 18f, right, top + 18f);
            InnerLine(g, color, DetailStroke, left, top, right, top);
        }

        private static void Prism(Graphics g, Color color)
        {
            var top = new[]
            {
                new PointF(9, 23), new PointF(31, 12),
                new PointF(55, 24), new PointF(33, 35)
            };
            var left = new[]
            {
                new PointF(9, 23), new PointF(33, 35),
                new PointF(33, 54), new PointF(9, 42)
            };
            var right = new[]
            {
                new PointF(33, 35), new PointF(55, 24),
                new PointF(55, 43), new PointF(33, 54)
            };
            using (var leftPath = PolygonPath(left))
                FillPath(g, leftPath, new RectangleF(9, 23, 24, 31), color, 74, 24);
            using (var rightPath = PolygonPath(right))
                FillPath(g, rightPath, new RectangleF(33, 24, 22, 30), color, 104, 34);
            using (var topPath = PolygonPath(top))
                FillPath(g, topPath, new RectangleF(9, 12, 46, 23), color, 150, 52);

            using (var silhouette = PolygonPath(new[]
            {
                new PointF(9, 23), new PointF(31, 12), new PointF(55, 24),
                new PointF(55, 43), new PointF(33, 54), new PointF(9, 42)
            }))
                StrokePath(g, silhouette, color, MainStroke);

            InnerPolyline(g, color, new[]
            {
                new PointF(9, 23), new PointF(33, 35), new PointF(55, 24)
            }, DetailStroke);
            InnerLine(g, color, DetailStroke, 33, 35, 33, 54);
            Highlight(g, 12, 22, 31, 14);
        }

        private static void FittedMonogram(Graphics g, string text, RectangleF bounds, Color color)
        {
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (var brush = new SolidBrush(color))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;

                Font fitted = null;
                for (float size = 18f; size >= 13.5f; size -= 0.5f)
                {
                    var candidate = new Font("Segoe UI", size, FontStyle.Bold, GraphicsUnit.Pixel);
                    SizeF measured = g.MeasureString(text, candidate, PointF.Empty, format);
                    if (measured.Width <= bounds.Width - 2f && measured.Height <= bounds.Height - 2f)
                    {
                        fitted = candidate;
                        break;
                    }
                    candidate.Dispose();
                }

                if (fitted == null)
                    fitted = new Font("Segoe UI", 13.5f, FontStyle.Bold, GraphicsUnit.Pixel);

                using (fitted)
                    g.DrawString(text, fitted, brush, bounds, format);
            }
        }

        private static void TechnicalPlate(Graphics g, string code, Color color)
        {
            var r = new RectangleF(9, 6, 46, 52);
            Document(g, r, Slate);

            using (var rail = new SolidBrush(Color.FromArgb(225, color)))
                g.FillRectangle(rail, 13, 12, 5, 38);
            Highlight(g, 21, 13, 38, 13);

            var plaqueBounds = new RectangleF(19, 20, 33, 24);
            using (var plaque = RoundedRect(plaqueBounds, 4f))
            {
                FillPath(g, plaque, plaqueBounds, color, 86, 34);
                InnerStrokePath(g, plaque, color, DetailStroke);
            }

            FittedMonogram(g, code, new RectangleF(19, 18, 33, 28), color);

            Node(g, 23, 51, color);
            InnerLine(g, color, DetailStroke, 30, 51, 47, 51);
        }

        // Alvenaria e geometria -------------------------------------------------

        public static Bitmap ParedeRet()
        {
            var bmp = NewCanvas(out Graphics g);
            var r = new RectangleF(9, 22, 46, 20);
            Panel(g, r, 2.5f, Slate, 110, 40);

            GraphicsState state = g.Save();
            g.SetClip(new RectangleF(11, 24, 42, 16));
            Line(g, SlateDeep, DetailStroke, 5, 40, 22, 23);
            Line(g, SlateDeep, DetailStroke, 19, 43, 38, 24);
            Line(g, SlateDeep, DetailStroke, 36, 43, 55, 24);
            g.Restore(state);

            Highlight(g, 13, 25, 51, 25);
            CornerNode(g, 10, 23, Cobalt);
            CornerNode(g, 54, 41, Cobalt);
            g.Dispose();
            return bmp;
        }

        public static Bitmap ParedePoly()
        {
            var bmp = NewCanvas(out Graphics g);
            var band = new[]
            {
                new PointF(7, 38), new PointF(22, 25), new PointF(41, 30),
                new PointF(57, 13), new PointF(57, 29), new PointF(43, 46),
                new PointF(24, 41), new PointF(7, 55)
            };
            FilledPolygon(g, Slate, band, new RectangleF(7, 13, 50, 42), 106, 38);
            Polyline(g, Cobalt, new[]
            {
                new PointF(8, 47), new PointF(23, 33),
                new PointF(42, 38), new PointF(56, 21)
            });
            Node(g, 8, 47, Cobalt);
            Node(g, 23, 33, Cobalt);
            Node(g, 42, 38, Cobalt);
            Node(g, 56, 21, Cobalt);
            Highlight(g, 11, 37, 22, 28);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Area()
        {
            var bmp = NewCanvas(out Graphics g);
            Prism(g, Cobalt);
            Node(g, 9, 23, Cobalt);
            Node(g, 31, 12, Cobalt);
            Node(g, 55, 24, Cobalt);
            g.Dispose();
            return bmp;
        }

        public static Bitmap AreaSel()
        {
            var bmp = NewCanvas(out Graphics g);
            Prism(g, Slate);
            SelectionCorners(g, new RectangleF(6, 8, 52, 48), Cobalt, 9f);
            Cursor(g, 38, 31, 0.68f);
            g.Dispose();
            return bmp;
        }

        public static Bitmap MedirSel()
        {
            var bmp = NewCanvas(out Graphics g);
            SelectionCorners(g, new RectangleF(7, 8, 50, 47), Cobalt, 9f);
            Polyline(g, Slate, new[]
            {
                new PointF(13, 35), new PointF(25, 22),
                new PointF(39, 29), new PointF(52, 18)
            }, 3.6f);
            InnerLine(g, SlateDeep, DetailStroke, 14, 41, 14, 51);
            InnerLine(g, SlateDeep, DetailStroke, 50, 35, 50, 51);
            DimensionH(g, 14, 50, 48, Cobalt);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Linear()
        {
            var bmp = NewCanvas(out Graphics g);
            Polyline(g, Cobalt, new[]
            {
                new PointF(7, 26), new PointF(22, 13),
                new PointF(41, 19), new PointF(57, 10)
            }, 3.6f);
            Node(g, 7, 26, Cobalt);
            Node(g, 57, 10, Cobalt);
            Line(g, SlateDeep, DetailStroke, 7, 34, 7, 51);
            Line(g, SlateDeep, DetailStroke, 57, 18, 57, 51);
            DimensionH(g, 7, 57, 47, Cobalt);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Vao()
        {
            var bmp = NewCanvas(out Graphics g);
            Panel(g, new RectangleF(5, 42, 15, 12), 2f, Slate, 118, 40);
            Panel(g, new RectangleF(47, 42, 12, 12), 2f, Slate, 118, 40);
            Line(g, Cobalt, 3.6f, 20, 48, 20, 17);

            using (var arc = new GraphicsPath())
            {
                arc.AddArc(-8, 18, 56, 60, 270, 87);
                StrokePath(g, arc, Sky, DetailStroke);
            }
            Node(g, 20, 48, Cobalt);
            Highlight(g, 8, 44, 17, 44);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Retangulo()
        {
            var bmp = NewCanvas(out Graphics g);
            Panel(g, new RectangleF(9, 8, 35, 34), 2.5f, Cobalt, 105, 32);
            Highlight(g, 13, 12, 39, 12);
            Line(g, SlateDeep, DetailStroke, 9, 46, 9, 56);
            Line(g, SlateDeep, DetailStroke, 44, 46, 44, 56);
            DimensionH(g, 9, 44, 53, Cobalt);
            Line(g, SlateDeep, DetailStroke, 48, 8, 57, 8);
            Line(g, SlateDeep, DetailStroke, 48, 42, 57, 42);
            DimensionV(g, 54, 8, 42, Cobalt);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Polf()
        {
            var bmp = NewCanvas(out Graphics g);
            var band = new[]
            {
                new PointF(14, 27), new PointF(29, 18), new PointF(43, 23),
                new PointF(57, 11), new PointF(57, 35), new PointF(44, 47),
                new PointF(29, 42), new PointF(14, 51)
            };
            FilledPolygon(g, Cobalt, band, new RectangleF(14, 11, 43, 40), 102, 28);
            Line(g, CobaltDeep, DetailStroke, 29, 18, 29, 42);
            Line(g, CobaltDeep, DetailStroke, 43, 23, 44, 47);
            DimensionV(g, 7, 27, 51, Sky);
            Node(g, 14, 51, Cobalt);
            Highlight(g, 17, 27, 29, 20);
            g.Dispose();
            return bmp;
        }

        // Seleção, contagem e dados --------------------------------------------

        public static Bitmap Contagem()
        {
            var bmp = NewCanvas(out Graphics g);
            var centers = new[]
            {
                new PointF(20, 20), new PointF(44, 20),
                new PointF(20, 44), new PointF(44, 44)
            };
            for (int i = 0; i < centers.Length; i++)
            {
                var r = new RectangleF(centers[i].X - 6f, centers[i].Y - 6f, 12, 12);
                Panel(g, r, 2f, Slate, 118, 42);
                if (i < 3)
                {
                    Ellipse(g, new RectangleF(centers[i].X - 10f, centers[i].Y - 10f, 20, 20),
                            Amber, 42, 12, DetailStroke);
                    Node(g, centers[i].X + 7f, centers[i].Y - 7f, Amber);
                }
            }
            g.Dispose();
            return bmp;
        }

        public static Bitmap Qselect()
        {
            var bmp = NewCanvas(out Graphics g);
            SelectionCorners(g, new RectangleF(7, 7, 50, 50), Slate, 7f);
            var funnel = new[]
            {
                new PointF(13, 15), new PointF(51, 15),
                new PointF(37, 34), new PointF(37, 54),
                new PointF(27, 48), new PointF(27, 34)
            };
            FilledPolygon(g, Cobalt, funnel, new RectangleF(13, 15, 38, 39), 142, 48);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Excel()
        {
            var bmp = NewCanvas(out Graphics g);
            Grid(g, new RectangleF(6, 6, 39, 46), Emerald);

            using (var arc = new GraphicsPath())
            {
                arc.AddArc(34, 33, 24, 24, 32, 135);
                StrokePath(g, arc, Emerald, MainStroke);
            }
            ArrowHead(g, new PointF(34, 44), new PointF(40, 40), new PointF(41, 47), Emerald);
            using (var arc = new GraphicsPath())
            {
                arc.AddArc(34, 33, 24, 24, 212, 135);
                StrokePath(g, arc, Emerald, MainStroke);
            }
            ArrowHead(g, new PointF(58, 46), new PointF(52, 50), new PointF(51, 43), Emerald);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Exportar()
        {
            var bmp = NewCanvas(out Graphics g);
            Grid(g, new RectangleF(18, 5, 29, 31), Slate);
            Line(g, Emerald, 3.6f, 32, 33, 32, 50);
            ArrowHead(g, new PointF(32, 54), new PointF(25, 45), new PointF(39, 45), Emerald);
            Polyline(g, Emerald, new[]
            {
                new PointF(8, 45), new PointF(8, 58),
                new PointF(56, 58), new PointF(56, 45)
            }, MainStroke);
            Highlight(g, 12, 55, 27, 55);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Modelo()
        {
            var bmp = NewCanvas(out Graphics g);
            var r = new RectangleF(8, 6, 43, 52);
            Document(g, r, Slate);
            using (var header = new SolidBrush(Color.FromArgb(205, Emerald)))
                g.FillRectangle(header, 13, 13, 25, 7);
            InnerLine(g, SlateDeep, DetailStroke, 14, 28, 38, 28);
            InnerLine(g, SlateDeep, DetailStroke, 14, 37, 34, 37);

            var ribbon = new[]
            {
                new PointF(37, 7), new PointF(56, 7),
                new PointF(56, 43), new PointF(46.5f, 36),
                new PointF(37, 43)
            };
            FilledPolygon(g, Emerald, ribbon, new RectangleF(37, 7, 19, 36), 210, 125);
            Highlight(g, 41, 11, 52, 11);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Mapa()
        {
            var bmp = NewCanvas(out Graphics g);
            var r = new RectangleF(8, 6, 48, 52);
            Document(g, r, Slate);
            using (var header = new SolidBrush(Color.FromArgb(215, Cobalt)))
                g.FillRectangle(header, 13, 13, 31, 8);
            InnerLine(g, SlateDeep, DetailStroke, 17, 27, 17, 50);
            InnerLine(g, SlateDeep, DetailStroke, 17, 32, 25, 32);
            InnerLine(g, SlateDeep, DetailStroke, 17, 42, 25, 42);
            InnerLine(g, SlateDeep, DetailStroke, 17, 50, 25, 50);
            InnerLine(g, Cobalt, DetailStroke, 28, 32, 48, 32);
            InnerLine(g, Cobalt, DetailStroke, 28, 42, 48, 42);
            InnerLine(g, Cobalt, DetailStroke, 28, 50, 43, 50);
            Node(g, 17, 27, Cobalt);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Macro(string texto)
        {
            var bmp = NewCanvas(out Graphics g);
            Color color = Cobalt;
            if (texto == "EO") color = Amber;
            else if (texto == "QT") color = Emerald;
            TechnicalPlate(g, texto, color);
            g.Dispose();
            return bmp;
        }

        // Produto e navegação ---------------------------------------------------

        public static Bitmap Painel()
        {
            var bmp = NewCanvas(out Graphics g);
            Panel(g, new RectangleF(6, 8, 52, 48), 5f, Slate, 90, 28);
            using (var top = new SolidBrush(Color.FromArgb(210, SlateDeep)))
                g.FillRectangle(top, 10, 12, 44, 7);

            Panel(g, new RectangleF(34, 19, 24, 37), 2f, Cobalt, 136, 46);
            Line(g, Cobalt, DetailStroke, 39, 28, 53, 28);
            Line(g, Cobalt, DetailStroke, 39, 38, 53, 38);
            Line(g, Cobalt, DetailStroke, 39, 48, 49, 48);
            Highlight(g, 10, 23, 28, 23);
            Node(g, 14, 15, Sky);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Licenca()
        {
            var bmp = NewCanvas(out Graphics g);
            Ellipse(g, new RectangleF(7, 17, 29, 29), Amber, 146, 46, MainStroke);
            Ellipse(g, new RectangleF(16, 26, 11, 11), Ink, 255, 255, DetailStroke);
            Line(g, Amber, 4f, 35, 32, 57, 32);
            Line(g, Amber, MainStroke, 48, 32, 48, 43);
            Line(g, Amber, MainStroke, 56, 32, 56, 39);
            Highlight(g, 12, 23, 20, 19);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Sobre()
        {
            var bmp = NewCanvas(out Graphics g);
            Ellipse(g, new RectangleF(7, 7, 50, 50), Cobalt, 228, 150, MainStroke);
            using (var dotHalo = new SolidBrush(Ink))
            using (var mark = new SolidBrush(Paper))
            {
                g.FillEllipse(dotHalo, 27, 14, 10, 10);
                g.FillEllipse(mark, 29, 16, 6, 6);
                using (var body = RoundedRect(new RectangleF(27, 27, 10, 24), 3f))
                {
                    g.FillPath(dotHalo, body);
                    using (var inner = RoundedRect(new RectangleF(29, 29, 6, 20), 2f))
                        g.FillPath(mark, inner);
                }
            }
            Highlight(g, 15, 18, 23, 12);
            g.Dispose();
            return bmp;
        }

        // Variantes exclusivas da Ribbon ---------------------------------------

        /// <summary>
        /// Vão introduzido manualmente: a porta mantém a leitura arquitetónica
        /// e o lápis distingue este comando da simples deteção de aberturas.
        /// </summary>
        public static Bitmap RibbonVao()
        {
            var bmp = NewCanvas(out Graphics g);

            Line(g, SlateDeep, DetailStroke, 6, 54, 58, 54);
            Line(g, Cobalt, 3.6f, 18, 53, 18, 14);
            using (var arc = new GraphicsPath())
            {
                arc.AddArc(-11, 15, 58, 76, 270, 82);
                InnerStrokePath(g, arc, Cobalt, DetailStroke);
            }

            var corpo = new[]
            {
                new PointF(36, 43), new PointF(49, 30),
                new PointF(55, 36), new PointF(42, 49)
            };
            using (var path = PolygonPath(corpo))
            {
                using (var fill = new SolidBrush(Amber)) g.FillPath(fill, path);
                InnerStrokePath(g, path, Amber, DetailStroke);
            }

            var ponta = new[]
            {
                new PointF(36, 43), new PointF(42, 49), new PointF(34, 51)
            };
            using (var path = PolygonPath(ponta))
            using (var fill = new SolidBrush(Paper))
            {
                g.FillPath(fill, path);
                InnerStrokePath(g, path, SlateDeep, 1.8f);
            }

            Node(g, 18, 53, Cobalt);
            g.Dispose();
            return bmp;
        }

        /// <summary>Blocos CAD acompanhados por um contador inequívoco.</summary>
        public static Bitmap RibbonContagem()
        {
            var bmp = NewCanvas(out Graphics g);

            Panel(g, new RectangleF(8, 31, 17, 17), 1.5f, Slate, 86, 30);
            Panel(g, new RectangleF(23, 12, 17, 17), 1.5f, Amber, 118, 42);
            Panel(g, new RectangleF(34, 35, 15, 15), 1.5f, Slate, 86, 30);
            Highlight(g, 26, 15, 36, 15);

            Ellipse(g, new RectangleF(37, 29, 23, 23), Amber, 205, 112, DetailStroke);
            FittedMonogram(g, "3", new RectangleF(39, 30, 19, 20), Ink);

            g.Dispose();
            return bmp;
        }

        /// <summary>Chave de licença com confirmação visual de ativação.</summary>
        public static Bitmap RibbonLicenca()
        {
            var bmp = NewCanvas(out Graphics g);

            Ellipse(g, new RectangleF(5, 12, 29, 29), Amber, 146, 46, MainStroke);
            Ellipse(g, new RectangleF(14, 21, 11, 11), Ink, 255, 255, DetailStroke);
            Line(g, Amber, 4f, 32, 27, 56, 27);
            Line(g, Amber, MainStroke, 47, 27, 47, 37);
            Line(g, Amber, MainStroke, 55, 27, 55, 34);

            Ellipse(g, new RectangleF(37, 36, 22, 22), Emerald, 218, 132, DetailStroke);
            InnerPolyline(g, Paper, new[]
            {
                new PointF(42, 47), new PointF(47, 52), new PointF(55, 42)
            }, 3f);

            g.Dispose();
            return bmp;
        }

        /// <summary>Marca do produto com um pequeno indicador de informação.</summary>
        public static Bitmap RibbonSobre()
        {
            var bmp = NewCanvas(out Graphics g);

            Panel(g, new RectangleF(5, 12, 50, 39), 3f, Slate, 84, 28);
            FittedMonogram(g, "TSK", new RectangleF(8, 17, 43, 26), Cobalt);
            InnerLine(g, SlateDeep, 1.8f, 11, 45, 39, 45);

            Ellipse(g, new RectangleF(40, 37, 20, 20), Cobalt, 225, 145, DetailStroke);
            using (var brush = new SolidBrush(Paper))
            {
                g.FillEllipse(brush, 48, 41, 4, 4);
                g.FillRectangle(brush, 48, 47, 4, 7);
            }

            g.Dispose();
            return bmp;
        }

        /// <summary>
        /// Roda dentada — o botão «Definições…» do painel único.
        ///
        /// Estava a reutilizar o ícone do «Atualizar», que é uma seta em
        /// círculo: dizia "isto refresca" a um botão que abre um diálogo de
        /// configuração, e as duas ações não têm nada em comum além de
        /// ambas serem redondas.
        /// </summary>
        public static Bitmap Definicoes()
        {
            var bmp = NewCanvas(out Graphics g);

            const float cx = 32f, cy = 32f;
            const float rMiolo = 8f;
            const float rDenteIn = 15f, rDenteOut = 21f;
            const int dentes = 8;

            for (int i = 0; i < dentes; i++)
            {
                double ang = i * Math.PI * 2.0 / dentes;
                float dx = (float)Math.Cos(ang), dy = (float)Math.Sin(ang);
                Line(g, Cobalt, MainStroke,
                    cx + dx * rDenteIn, cy + dy * rDenteIn,
                    cx + dx * rDenteOut, cy + dy * rDenteOut);
            }

            Ellipse(g, new RectangleF(cx - rDenteIn, cy - rDenteIn, rDenteIn * 2, rDenteIn * 2),
                Cobalt, 126, 46, DetailStroke);
            Ellipse(g, new RectangleF(cx - rMiolo, cy - rMiolo, rMiolo * 2, rMiolo * 2),
                SlateDeep, 126, 46, DetailStroke);

            g.Dispose();
            return bmp;
        }

        public static Bitmap Atualizar()
        {
            var bmp = NewCanvas(out Graphics g);
            using (var arc = new GraphicsPath())
            {
                arc.AddArc(8, 8, 48, 48, 200, 140);
                StrokePath(g, arc, Cobalt, 4.4f);
            }
            ArrowHead(g, new PointF(54, 23), new PointF(43, 20), new PointF(48, 33), Cobalt);
            using (var arc = new GraphicsPath())
            {
                arc.AddArc(8, 8, 48, 48, 20, 140);
                StrokePath(g, arc, Sky, 4.4f);
            }
            ArrowHead(g, new PointF(10, 41), new PointF(21, 44), new PointF(16, 31), Sky);
            g.Dispose();
            return bmp;
        }

        // Folha de medições -----------------------------------------------------

        public static Bitmap Capitulo()
        {
            var bmp = NewCanvas(out Graphics g);
            var r = new RectangleF(8, 7, 48, 50);
            Document(g, r, Slate);
            using (var band = RoundedRect(new RectangleF(13, 14, 35, 15), 3f))
            {
                FillPath(g, band, new RectangleF(13, 14, 35, 15), Cobalt, 220, 155);
                StrokePath(g, band, Cobalt, DetailStroke);
            }
            Line(g, Paper, DetailStroke, 18, 21.5f, 37, 21.5f);
            Line(g, SlateDeep, DetailStroke, 14, 38, 48, 38);
            Line(g, SlateDeep, DetailStroke, 14, 48, 42, 48);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Artigo()
        {
            var bmp = NewCanvas(out Graphics g);
            var r = new RectangleF(8, 7, 48, 50);
            Document(g, r, Slate);
            using (var row = RoundedRect(new RectangleF(12, 18, 40, 17), 3f))
            {
                FillPath(g, row, new RectangleF(12, 18, 40, 17), Emerald, 135, 48);
                StrokePath(g, row, Emerald, DetailStroke);
            }
            Node(g, 18, 26.5f, Emerald);
            Line(g, Emerald, DetailStroke, 25, 26.5f, 38, 26.5f);
            Line(g, Emerald, DetailStroke, 44, 22, 44, 31);
            Line(g, SlateDeep, DetailStroke, 14, 44, 47, 44);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Reclassificar()
        {
            var bmp = NewCanvas(out Graphics g);
            Panel(g, new RectangleF(7, 8, 24, 20), 3f, Slate, 110, 36);
            InnerLine(g, SlateDeep, DetailStroke, 12, 15, 26, 15);
            InnerLine(g, SlateDeep, DetailStroke, 12, 22, 22, 22);
            Panel(g, new RectangleF(33, 36, 24, 20), 3f, Emerald, 130, 42);
            InnerLine(g, Emerald, DetailStroke, 38, 43, 52, 43);
            InnerLine(g, Emerald, DetailStroke, 38, 50, 48, 50);
            Polyline(g, Emerald, new[]
            {
                new PointF(17, 34), new PointF(17, 43), new PointF(29, 43),
                new PointF(38, 32)
            }, MainStroke);
            ArrowHead(g, new PointF(41, 29), new PointF(29, 33), new PointF(37, 41), Emerald);
            Node(g, 17, 34, Cobalt);
            g.Dispose();
            return bmp;
        }

        public static Bitmap LinhaBranca()
        {
            var bmp = NewCanvas(out Graphics g);
            Panel(g, new RectangleF(8, 8, 48, 14), 3f, Slate, 80, 25);
            Panel(g, new RectangleF(8, 42, 48, 14), 3f, Slate, 80, 25);
            Line(g, SlateDeep, DetailStroke, 14, 15, 45, 15);
            Line(g, SlateDeep, DetailStroke, 14, 49, 45, 49);
            DimensionV(g, 32, 24, 40, Cobalt);
            Highlight(g, 12, 11, 25, 11);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Limpar()
        {
            var bmp = NewCanvas(out Graphics g);
            using (var body = RoundedRect(new RectangleF(17, 20, 30, 36), 3f))
                Shape(g, body, new RectangleF(17, 20, 30, 36), Coral, 118, 34, MainStroke);
            Line(g, Coral, 3.8f, 10, 16, 54, 16);
            Polyline(g, Coral, new[]
            {
                new PointF(24, 15), new PointF(26, 9), new PointF(38, 9),
                new PointF(40, 15)
            }, MainStroke);
            Line(g, Coral, DetailStroke, 27, 28, 27, 48);
            Line(g, Coral, DetailStroke, 37, 28, 37, 48);
            Highlight(g, 21, 24, 42, 24);
            g.Dispose();
            return bmp;
        }

        public static Bitmap Remover()
        {
            var bmp = NewCanvas(out Graphics g);
            Panel(g, new RectangleF(7, 18, 33, 28), 4f, Slate, 90, 28);
            InnerLine(g, SlateDeep, DetailStroke, 13, 27, 31, 27);
            InnerLine(g, SlateDeep, DetailStroke, 13, 37, 27, 37);
            Ellipse(g, new RectangleF(31, 27, 26, 26), Coral, 232, 168, MainStroke);
            Line(g, Paper, 3.4f, 39, 35, 49, 45);
            Line(g, Paper, 3.4f, 49, 35, 39, 45);
            Highlight(g, 35, 31, 41, 29);
            g.Dispose();
            return bmp;
        }
    }
}
