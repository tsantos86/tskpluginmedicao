using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Repositório das paredes de alvenaria: os dados vivem no próprio DWG
    /// (XData na polyline), então salvar o desenho salva as medições.
    /// </summary>
    public static class AlvRepo
    {
        public const string AppName = "CASQUILHO_ALV";

        /// <summary>Lê todas as paredes do Model Space.</summary>
        public static List<Parede> CarregarParedes(Database db)
        {
            var paredes = new List<Parede>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass != Util.ClPolyline) continue;
                    var p = LerParede(tr.GetObject(id, OpenMode.ForRead) as Polyline);
                    if (p != null) paredes.Add(p);
                }
                tr.Commit();
            }
            return paredes;
        }

        /// <summary>
        /// Lê uma parede de uma polyline já aberta, ou null se não for nossa.
        /// Separado para a leitura poder ser feita numa passagem única sobre o
        /// Model Space, partilhada com os outros repositórios.
        /// </summary>
        internal static Parede LerParede(Polyline pl)
        {
            if (pl == null) return null;
            using (ResultBuffer rb = pl.GetXDataForApplication(AppName))
            {
                if (rb == null) return null;
                var parede = ParseXData(rb);
                parede.Handle = pl.Handle.ToString();
                parede.Layer = pl.Layer;

                // Uma polyline fechada de quatro lados, com os lados opostos
                // iguais, É um rectângulo — diga a XData o que disser. Sem esta
                // verificação, medições criadas antes de a marca existir ficavam
                // para sempre a devolver o PERÍMETRO em vez do comprimento, e
                // recompilar não as corrigia.
                // A área tem de ser vista ANTES do rectângulo: um pavimento
                // desenhado a quatro cliques É um rectângulo, e o ramo de baixo
                // devolvia-lhe o lado maior em vez da área.
                if (parede.AreaVezesAltura)
                {
                    // Da geometria, não da XData: esticar o contorno no desenho
                    // passa a valer na folha sem se voltar a medir nada.
                    double area = 0;
                    try { area = System.Math.Abs(pl.Area); } catch { }
                    parede.Comprimento = area;
                }
                else if (parede.Retangulo || EhRectangulo(pl))
                {
                    // Pelos lados do próprio retângulo, não pela bounding box:
                    // com o UCS rodado a caixa é maior que a parede.
                    double lado1, lado2;
                    LadosDoRetangulo(pl, out lado1, out lado2);
                    parede.Comprimento = System.Math.Max(lado1, lado2);
                    if (parede.Espessura <= 0)
                        parede.Espessura = System.Math.Min(lado1, lado2);
                }
                else
                {
                    parede.Comprimento = pl.Length;
                }
                return parede;
            }
        }

        /// <summary>
        /// Lê uma medição de uma hachura. A área da hachura é o número; a
        /// altura fica a 1 para que área bruta = comprimento × altura × 1.
        /// </summary>
        internal static Parede LerParedeDeHatch(Hatch h)
        {
            if (h == null) return null;
            using (ResultBuffer rb = h.GetXDataForApplication(AppName))
            {
                if (rb == null) return null;
                var parede = ParseXData(rb);
                parede.Handle = h.Handle.ToString();
                parede.Layer = h.Layer;
                double area = 0;
                try { area = System.Math.Abs(h.Area); } catch { }

                if (parede.AreaVezesAltura)
                {
                    // Área do desenho × espessura da camada. Ao contrário do
                    // SoArea, a altura NÃO se mexe: é ela o segundo factor.
                    parede.Comprimento = area;
                }
                else if (parede.SoArea)
                {
                    // Hachura de alçado: a área É a medição.
                    parede.Altura = 1.0;
                    parede.Comprimento = area;
                }
                else
                {
                    // Hachura de planta: os troços vêm das caixas dos contornos.
                    double comp, esp;
                    if (MedidasDaHachura(h, out comp, out esp))
                    {
                        // Só o comprimento vem da geometria. A espessura é a
                        // que o utilizador definiu no painel: é ele que vai ao
                        // desenho ver de que camada se trata, e essa decisão
                        // não se lhe tira. Aqui ela só entra no volume em m³ —
                        // a área em m² não depende dela.
                        parede.Comprimento = comp;
                    }
                    else
                    {
                        // Contorno que não se deixa ler: resta a área a dividir
                        // pela espessura escolhida. Dá menos, mas dá alguma coisa.
                        parede.Comprimento = parede.Espessura > 0
                            ? area / parede.Espessura : 0;
                    }
                }
                return parede;
            }
        }

        /// <summary>
        /// Comprimento e espessura de uma hachura de parede, tirados da caixa
        /// de CADA contorno.
        ///
        /// Foi preciso três tentativas falhadas para chegar aqui, e a razão é
        /// esta: a área da hachura não serve. Uma hachura de parede tem os
        /// troços entre as portas como contornos separados, e esses troços não
        /// enchem as suas próprias caixas — têm recortes nos topos, nos
        /// encontros e nos umbrais. Dividir a área pela espessura dava sempre
        /// menos do que a realidade.
        ///
        /// A caixa de cada contorno, essa, é fiável: o lado maior é o
        /// comprimento daquele troço. Somados, dão a parede maciça; os vãos
        /// entram depois pelo <see cref="Parede.GeometriaSemVaos"/>.
        ///
        /// A espessura devolvida é só informativa — quem manda nela é o painel.
        /// Repare-se que o comprimento NÃO depende dela: é medido, não
        /// calculado. Foi essa dependência que fez as três tentativas
        /// anteriores falharem em silêncio quando a espessura não batia certo.
        /// </summary>
        internal static bool MedidasDaHachura(Hatch h,
            out double comprimento, out double espessura)
        {
            comprimento = 0; espessura = 0;
            if (h == null) return false;

            // A caixa de cada troço é medida NO EIXO DA PAREDE, não em X/Y.
            // Uma parede a 45° dava menos 25% do comprimento com a caixa
            // alinhada com os eixos do desenho, e a 30° menos 11% — sem erro
            // nenhum, sem aviso nenhum. Ver EixoDaHachura.
            Vector2d u, v;
            EixoDaHachura(h, out u, out v);

            var espessuras = new List<double>();
            try
            {
                for (int i = 0; i < h.NumberOfLoops; i++)
                {
                    HatchLoop laco;
                    try { laco = h.GetLoopAt(i); } catch { continue; }
                    if (laco == null || laco.Curves == null) continue;

                    double a0 = double.MaxValue, b0 = double.MaxValue;
                    double a1 = double.MinValue, b1 = double.MinValue;

                    foreach (Autodesk.AutoCAD.Geometry.Curve2d c in laco.Curves)
                    {
                        try
                        {
                            var iv = c.GetInterval();
                            foreach (var p in new[] { c.EvaluatePoint(iv.LowerBound),
                                                      c.EvaluatePoint(iv.UpperBound) })
                            {
                                double pa = p.X * u.X + p.Y * u.Y;   // ao longo
                                double pb = p.X * v.X + p.Y * v.Y;   // atravessado
                                if (pa < a0) a0 = pa; if (pa > a1) a1 = pa;
                                if (pb < b0) b0 = pb; if (pb > b1) b1 = pb;
                            }
                        }
                        catch { }
                    }
                    if (a0 > a1) continue;

                    double du = a1 - a0, dv = b1 - b0;
                    double menor = System.Math.Min(du, dv);
                    double maior = System.Math.Max(du, dv);

                    // Num troço quase quadrado não se sabe qual é a espessura e
                    // qual é o comprimento. Conta para o comprimento mas não
                    // opina sobre a espessura.
                    if (menor > 0 && maior > menor * 1.5) espessuras.Add(menor);
                    comprimento += maior;
                }
            }
            catch { return false; }

            if (comprimento <= 0 || espessuras.Count == 0) return false;

            // A espessura é a mesma em todos os troços da parede; a mediana
            // ignora um troço estranho sem se deixar levar por ele.
            espessuras.Sort();
            espessura = espessuras[espessuras.Count / 2];
            return espessura > 0;
        }

        /// <summary>
        /// O eixo em que a parede corre, tirado da própria geometria.
        ///
        /// Porque não se usa a caixa alinhada com X/Y: uma parede rodada não
        /// enche a sua caixa. Um troço de 3,00 × 0,15 a 30° dá uma caixa de
        /// 2,673 × 1,630 — o lado maior deixa de ser o comprimento. Medido:
        ///
        ///     15°  →  −1,9 %        45°  →  −25,3 %
        ///     30°  →  −10,5 %      127°  →  −16,7 %
        ///
        /// E nos vãos é pior, porque nem sequer parece errado: uma porta de
        /// 0,90 numa parede a 45° saía como 0,53.
        ///
        /// Porque não se usa o UCS: o utilizador pode estar a desenhar num UCS
        /// rodado e a parede estar a outro ângulo qualquer, ou o contrário. O
        /// UCS é um palpite; a geometria é o facto.
        ///
        /// O eixo é a direcção do segmento recto mais longo de todos os
        /// contornos. Numa parede — mesmo com recortes, umbrais e encontros —
        /// o segmento mais longo é sempre uma das faces.
        /// </summary>
        internal static void EixoDaHachura(Hatch h, out Vector2d u, out Vector2d v)
        {
            u = new Vector2d(1, 0);
            v = new Vector2d(0, 1);
            if (h == null) return;

            double melhor = 0;
            try
            {
                for (int i = 0; i < h.NumberOfLoops; i++)
                {
                    HatchLoop laco;
                    try { laco = h.GetLoopAt(i); } catch { continue; }
                    if (laco == null || laco.Curves == null) continue;

                    foreach (Autodesk.AutoCAD.Geometry.Curve2d c in laco.Curves)
                    {
                        try
                        {
                            var iv = c.GetInterval();
                            var pa = c.EvaluatePoint(iv.LowerBound);
                            var pb = c.EvaluatePoint(iv.UpperBound);
                            double dx = pb.X - pa.X, dy = pb.Y - pa.Y;
                            double d = System.Math.Sqrt(dx * dx + dy * dy);
                            if (d > melhor && d > 1e-9)
                            {
                                melhor = d;
                                u = new Vector2d(dx / d, dy / d);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            v = new Vector2d(-u.Y, u.X);
        }

        /// <summary>
        /// Os contornos correm todos na direcção do eixo (ou perpendiculares
        /// a ele)?
        ///
        /// Uma parede em L, ou duas paredes a ângulos diferentes na mesma
        /// hachura, não têm eixo nenhum — e projectar sobre um eixo escolhido
        /// a partir de metade delas dá um número que parece bom e não é. Aqui
        /// detecta-se, para se poder avisar em vez de inventar.
        /// </summary>
        internal static bool GeometriaCoerenteComEixo(Hatch h, Vector2d u,
            double toleranciaGraus = 2.0)
        {
            int consideradas, fora;
            return GeometriaCoerenteComEixo(h, u, toleranciaGraus,
                                            out consideradas, out fora);
        }

        internal static bool GeometriaCoerenteComEixo(Hatch h, Vector2d u,
            double toleranciaGraus, out int consideradas, out int fora)
        {
            consideradas = 0; fora = 0;
            if (h == null) return true;
            double angU = System.Math.Atan2(u.Y, u.X) * 180.0 / System.Math.PI;

            // Só contam os segmentos LONGOS o suficiente para definirem a
            // direcção da parede.
            //
            // A primeira versão desta guarda testava todas as curvas e
            // condenava paredes perfeitamente rectas. Uma parede real não tem
            // quatro curvas por contorno: tem sete, nove, doze — porque os
            // umbrais, os chanfros e os encontros acrescentam segmentos
            // curtos, e as portas acrescentam arcos. A corda de um arco aponta
            // para onde calha, e um umbral de 2 cm também. Bastava um deles
            // para a parede inteira ser dada como incoerente e os vãos
            // desaparecerem sem explicação.
            //
            // O critério passa a ser o comprimento: um segmento que define a
            // direcção de uma parede é comparável ao tamanho dela, não a um
            // pormenor de canto.
            // A extensão é medida NO PLANO DA HACHURA, a partir das próprias
            // curvas — e não pelo GeometricExtents, que vem em WCS. Numa
            // hachura com Normal diferente de Z as duas não coincidem, e o
            // limiar sairia calculado sobre a medida errada.
            double extensao = 0;
            try
            {
                double x0 = double.MaxValue, y0 = double.MaxValue;
                double x1 = double.MinValue, y1 = double.MinValue;
                for (int i = 0; i < h.NumberOfLoops; i++)
                {
                    HatchLoop l2;
                    try { l2 = h.GetLoopAt(i); } catch { continue; }
                    if (l2 == null || l2.Curves == null) continue;
                    foreach (Autodesk.AutoCAD.Geometry.Curve2d c in l2.Curves)
                    {
                        try
                        {
                            var iv = c.GetInterval();
                            foreach (var p in new[] { c.EvaluatePoint(iv.LowerBound),
                                                      c.EvaluatePoint(iv.UpperBound) })
                            {
                                if (p.X < x0) x0 = p.X; if (p.X > x1) x1 = p.X;
                                if (p.Y < y0) y0 = p.Y; if (p.Y > y1) y1 = p.Y;
                            }
                        }
                        catch { }
                    }
                }
                if (x0 <= x1) extensao = System.Math.Max(x1 - x0, y1 - y0);
            }
            catch { }
            double minimo = extensao > 0 ? extensao * 0.10 : 0.30;

            try
            {
                for (int i = 0; i < h.NumberOfLoops; i++)
                {
                    HatchLoop laco;
                    try { laco = h.GetLoopAt(i); } catch { continue; }
                    if (laco == null || laco.Curves == null) continue;

                    foreach (Autodesk.AutoCAD.Geometry.Curve2d c in laco.Curves)
                    {
                        try
                        {
                            // Arcos e splines não definem direcção nenhuma.
                            if (!(c is LineSegment2d)) continue;

                            var iv = c.GetInterval();
                            var pa = c.EvaluatePoint(iv.LowerBound);
                            var pb = c.EvaluatePoint(iv.UpperBound);
                            double dx = pb.X - pa.X, dy = pb.Y - pa.Y;
                            double comp = System.Math.Sqrt(dx * dx + dy * dy);
                            if (comp < minimo) continue;

                            consideradas++;

                            double ang = System.Math.Atan2(dy, dx) * 180.0 / System.Math.PI;
                            // Só interessa o desvio dentro de um quadrante: um
                            // lado a 0° e outro a 90° são ambos coerentes.
                            double d = (ang - angU) % 90.0;
                            if (d < 0) d += 90.0;
                            if (System.Math.Min(d, 90.0 - d) > toleranciaGraus) fora++;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // Sem segmentos longos não há opinião a dar — e não se trava por
            // falta de opinião. Só se recusa quando há prova do contrário.
            if (consideradas == 0) return true;
            return fora == 0;
        }

        /// <summary>
        /// Comprimentos dos dois lados de um retângulo, medidos nos vértices.
        /// Imune a rotação do UCS (a bounding box não é).
        /// </summary>
        /// <summary>
        /// Quatro vértices, fechada, lados opostos iguais. Não basta ter quatro
        /// cantos: um quadrilátero qualquer não tem "comprimento" nem
        /// "espessura", e tratá-lo como rectângulo daria um número inventado.
        /// </summary>
        internal static bool EhRectangulo(Polyline pl)
        {
            if (pl == null || !pl.Closed || pl.NumberOfVertices != 4) return false;
            try
            {
                var a = pl.GetPoint2dAt(0);
                var b = pl.GetPoint2dAt(1);
                var c = pl.GetPoint2dAt(2);
                var d = pl.GetPoint2dAt(3);

                double ab = a.GetDistanceTo(b), bc = b.GetDistanceTo(c);
                double cd = c.GetDistanceTo(d), da = d.GetDistanceTo(a);
                if (ab <= 0 || bc <= 0) return false;

                double tol = 1e-6 + (ab + bc) * 1e-4;
                return System.Math.Abs(ab - cd) < tol &&
                       System.Math.Abs(bc - da) < tol;
            }
            catch { return false; }
        }

        internal static void LadosDoRetangulo(Polyline pl, out double lado1, out double lado2)
        {
            lado1 = lado2 = 0;
            if (pl.NumberOfVertices < 3)
            {
                var ext = pl.GeometricExtents;
                lado1 = ext.MaxPoint.X - ext.MinPoint.X;
                lado2 = ext.MaxPoint.Y - ext.MinPoint.Y;
                return;
            }
            var p0 = pl.GetPoint2dAt(0);
            var p1 = pl.GetPoint2dAt(1);
            var p2 = pl.GetPoint2dAt(2);
            lado1 = p0.GetDistanceTo(p1);
            lado2 = p1.GetDistanceTo(p2);
        }

        /// <summary>
        /// Diz, uma vez por artigo e unidade, quando a unidade do mapa não bate
        /// certo com o que se está a medir.
        ///
        /// Uma parede sai em m² — ou em m³, quando é área em planta × altura do
        /// painel — portanto atribuí-la a um artigo de "un" ou de "ml" é quase
        /// de certeza engano. Quase — daí avisar em vez de recusar. O
        /// <c>_avisados</c> existe para não encher a linha de comandos: medir
        /// vinte paredes para o mesmo artigo errado dá um aviso, não vinte.
        /// </summary>
        private static readonly HashSet<string> _avisados = new HashSet<string>();

        private static void AvisarUnidade(Parede parede)
        {
            try
            {
                if (parede == null || string.IsNullOrEmpty(parede.Artigo)) return;

                // A unidade tem de ser a MESMA que a folha vai escrever, senão
                // o aviso acusa o que está certo. O FolhaMedicao escreve "m3"
                // quando AreaVezesAltura — área em planta × altura do painel — e
                // aqui ia sempre "m2": medir um painel contra um artigo em m³,
                // correctamente declarado, dava aviso de engano.
                string unidade = parede.AreaVezesAltura ? "m3" : "m2";

                // A chave leva a unidade. Sem ela, medir o mesmo artigo primeiro
                // em m² e depois em m³ calava o segundo aviso — que é justamente
                // o que interessava ouvir.
                string chave = parede.Artigo + "|" + unidade;
                if (_avisados.Contains(chave)) return;

                string aviso = MapaQuantidades.AvisoDeUnidade(parede.Artigo, unidade);
                if (aviso.Length == 0) return;

                _avisados.Add(chave);
                PaletteHost.Log(aviso);
            }
            catch { /* um aviso que rebenta é pior do que aviso nenhum */ }
        }

        /// <summary>Volta a avisar. Chamado quando se importa um mapa novo.</summary>
        public static void EsquecerAvisos()
        {
            try { _avisados.Clear(); } catch { }
        }

        /// <summary>Adiciona um vão à parede identificada pelo handle.</summary>
        public static bool AdicionarVao(string handle, Vao vao)
        {
            return AdicionarVaos(handle, new[] { vao });
        }

        /// <summary>
        /// Adiciona vários vãos numa transação só. Um-a-um (como se fazia no
        /// TSKVAO) eram N transações e N aberturas da parede para a mesma coisa.
        /// </summary>
        public static bool AdicionarVaos(string handle, IList<Vao> vaos)
        {
            if (vaos == null || vaos.Count == 0) return false;
            return EditarParede(handle, p =>
            {
                foreach (var v in vaos) p.Vaos.Add(v);
            });
        }

        /// <summary>Remove o último vão da parede.</summary>
        public static bool RemoverUltimoVao(string handle)
        {
            return EditarParede(handle, p =>
            {
                if (p.Vaos.Count > 0) p.Vaos.RemoveAt(p.Vaos.Count - 1);
            });
        }

        /// <summary>
        /// Altera uma dimensão de um vão já criado ("larg", "alt" ou "qt").
        /// O vão identifica-se pela posição na lista, que é a mesma ordem por
        /// que aparece na grelha e no Excel.
        /// </summary>
        public static bool AlterarVao(string handle, int indice, string campo, double valor)
        {
            return EditarParede(handle, p =>
            {
                if (indice < 0 || indice >= p.Vaos.Count) return;
                var v = p.Vaos[indice];
                if (campo == "larg") v.Largura = valor;
                else if (campo == "alt") v.Altura = valor;
                else if (campo == "qt") v.Quantidade = (int)System.Math.Round(valor);
            });
        }

        /// <summary>Muda o nome do vão (VE.01, PI.04…), o que sai no Excel.</summary>
        public static bool DefinirDesignacaoVao(string handle, int indice, string texto)
        {
            return EditarParede(handle, p =>
            {
                if (indice < 0 || indice >= p.Vaos.Count) return;
                // Os separadores da XData não podem viajar dentro do texto.
                p.Vaos[indice].Designacao =
                    (texto ?? "").Replace("|", "").Replace(";", "").Trim();
            });
        }

        /// <summary>Remove o vão que está na posição indicada.</summary>
        public static bool RemoverVao(string handle, int indice)
        {
            return EditarParede(handle, p =>
            {
                if (indice >= 0 && indice < p.Vaos.Count) p.Vaos.RemoveAt(indice);
            });
        }

        /// <summary>Altera a altura da parede.</summary>
        public static bool AlterarAltura(string handle, double novaAltura)
        {
            return EditarParede(handle, p => p.Altura = novaAltura);
        }

        /// <summary>Altera uma dimensão editada na grade ("alt", "larg" ou "esp").</summary>
        public static bool AlterarDimensao(string handle, string campo, double valor)
        {
            return EditarParede(handle, p => AplicarDimensao(p, campo, valor));
        }

        /// <summary>
        /// Põe a dimensão na medição. Devolve false quando o campo não é dos
        /// três, ou quando o valor já era esse — para a edição em bloco não
        /// contar como alterada uma medição em que não mexeu.
        /// </summary>
        private static bool AplicarDimensao(Parede p, string campo, double valor)
        {
            if (campo == "alt")
            {
                if (p.Altura == valor) return false;
                p.Altura = valor; return true;
            }
            if (campo == "larg")
            {
                if (p.Largura == valor) return false;
                p.Largura = valor; return true;
            }
            if (campo == "esp")
            {
                if (p.Espessura == valor) return false;
                p.Espessura = valor; return true;
            }
            return false;
        }

        /// <summary>Liga/desliga a linha em branco antes desta medição.</summary>
        public static bool AlternarSeparador(string handle)
        {
            return EditarParede(handle, p => p.Separador = !p.Separador);
        }

        /// <summary>
        /// Acrescenta mais uma linha em branco DEPOIS desta medição.
        ///
        /// Soma em vez de alternar: carregar duas vezes dá duas linhas, três
        /// vezes dá três. Ao passar do tecto volta a zero, e é assim que se
        /// tiram — sem precisar de um botão só para isso.
        /// </summary>
        public static bool AcrescentarSeparadorDepois(string handle)
        {
            return EditarParede(handle, p =>
            {
                int n = p.LinhasEmBrancoDepois + 1;
                if (n > Parede.MaxLinhasEmBranco) n = 0;
                p.LinhasEmBrancoDepois = n;
            });
        }


        /// <summary>
        /// Define (ou limpa) o título que sai depois desta medição.
        /// Ao remover a marca, o texto vai com ela — senão reaparecia sozinho
        /// da próxima vez que se pusesse um título nessa medição.
        /// </summary>
        public static bool DefinirMarca(string handle, string marca)
        {
            return EditarParede(handle, p =>
            {
                p.MarcaDepois = marca ?? "";
                if (p.MarcaDepois.Length == 0) p.TextoTitulo = "";
            });
        }

        /// <summary>
        /// Acrescenta ou tira um nível de título a esta medição, mantendo os
        /// outros. É o que os botões Capítulo / Artigo usam.
        /// </summary>
        public static bool AlternarTitulo(string handle, string marca)
        {
            return EditarParede(handle, p => p.AlternarMarca(marca));
        }

        /// <summary>Grava o texto de um dos títulos desta medição.</summary>
        public static bool DefinirTextoDeTitulo(string handle, int indice, string texto)
        {
            return EditarParede(handle, p => p.DefinirTextoDaMarca(indice, texto));
        }

        /// <summary>Guarda o texto escrito na linha desta medição, no Excel.</summary>
        public static bool DefinirNota(string handle, string nota)
        {
            return EditarParede(handle, p => p.Nota = nota ?? "");
        }

        /// <summary>
        /// Muda o serviço de uma medição (editado na grelha). O serviço é o
        /// capítulo da folha, por isso mudá-lo passa a medição para outro bloco
        /// do Excel — e leva a polyline para a layer correspondente, senão o
        /// desenho e a folha ficavam a dizer coisas diferentes.
        /// </summary>
        public static bool DefinirServico(string handle, string servico)
        {
            servico = (servico ?? "").Trim();
            if (servico.Length == 0) return false;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (!TryGetId(db, handle, out ObjectId id)) return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Entity, pela mesma razão do EditarParede: uma medição pode
                // ser uma hachura, e essas saíam daqui sem o serviço mudado.
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent == null) return false;

                Parede parede;
                using (ResultBuffer rb = ent.GetXDataForApplication(AppName))
                {
                    if (rb == null) return false;
                    parede = ParseXData(rb);
                }

                parede.Servico = servico;
                GravarXData(ent, parede);

                // As importadas ficam na sua layer: é por ela que o TSKIMPORTAR
                // sabe o que pode substituir. Mudar-lhes a layer aqui deixava-as
                // órfãs e a importação seguinte duplicava tudo outra vez.
                if (ent.Layer != ImportarExcel.LayerImportado)
                {
                    try
                    {
                        string layer = Commands.LayerPrefix + Util.NomeLayer(servico);
                        Util.EnsureLayer(tr, db, layer, 1);
                        ent.Layer = layer;
                    }
                    catch { /* layer bloqueada ou nome inválido: os dados chegam */ }
                }

                tr.Commit();
            }
            return true;
        }

        /// <summary>Muda o piso de uma medição (editado na grelha).</summary>
        public static bool DefinirPiso(string handle, string piso)
        {
            return EditarParede(handle, p => p.Piso = piso ?? "");
        }

        /// <summary>
        /// Apaga do desenho tudo o que veio de uma importação anterior.
        /// Só toca no que está na layer das importadas — o que foi medido no
        /// desenho fica onde está. Devolve quantas entidades removeu.
        /// </summary>
        public static int LimparImportadas(Database db)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;

            var apagar = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.Layer == ImportarExcel.LayerImportado)
                        apagar.Add(id);
                }
                tr.Commit();
            }
            if (apagar.Count == 0) return 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in apagar)
                    ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();
                tr.Commit();
            }
            return apagar.Count;
        }

        /// <summary>
        /// Aplica a mesma alteração a várias medições, numa transacção só.
        ///
        /// Uma transacção por medição custava caro: um artigo com trinta
        /// medições dava trinta LockDocument em cada actualização da folha.
        ///
        /// A <paramref name="edicao"/> devolve <c>true</c> quando mexeu mesmo
        /// em alguma coisa. O que não mudou não se regrava — é o que evita
        /// reescrever XData igual e faz a contagem devolvida significar
        /// "quantas mudaram", que é o que se diz ao utilizador.
        /// </summary>
        public static int EditarVarias(IEnumerable<string> handles,
            System.Func<Parede, bool> edicao)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || handles == null || edicao == null) return 0;
            var db = doc.Database;

            var ids = new List<ObjectId>();
            foreach (string h in handles)
            {
                ObjectId id;
                if (TryGetId(db, h, out id)) ids.Add(id);
            }
            if (ids.Count == 0) return 0;

            int feitas = 0;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    // Entity, pela mesma razão do EditarParede: uma medição
                    // pode ser uma hachura, e com o cast a Polyline estas
                    // eram saltadas caladamente.
                    var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                    if (ent == null) continue;

                    Parede parede;
                    using (ResultBuffer rb = ent.GetXDataForApplication(AppName))
                    {
                        if (rb == null) continue;
                        parede = ParseXData(rb);
                    }

                    if (!edicao(parede)) continue;
                    GravarXData(ent, parede);
                    feitas++;
                }
                tr.Commit();
            }
            return feitas;
        }

        /// <summary>Define o mesmo artigo em várias medições, de uma vez.</summary>
        public static int DefinirArtigoEmVarias(IEnumerable<string> handles, string artigo)
        {
            string alvo = artigo ?? "";
            return EditarVarias(handles, p =>
            {
                if ((p.Artigo ?? "") == alvo) return false;
                p.Artigo = alvo;
                return true;
            });
        }

        /// <summary>
        /// Define o mesmo piso em várias medições, de uma vez. Piso vazio
        /// tira-o — e é esse o caso que mais dá jeito: um projecto medido
        /// todo em "PISO 3" que afinal não quer o piso na folha são dezenas
        /// de linhas, e uma a uma a grelha reordena-se debaixo do rato.
        /// </summary>
        public static int DefinirPisoEmVarias(IEnumerable<string> handles, string piso)
        {
            string alvo = (piso ?? "").Trim();
            return EditarVarias(handles, p =>
            {
                if ((p.Piso ?? "") == alvo) return false;
                p.Piso = alvo;
                return true;
            });
        }

        /// <summary>Define a mesma dimensão (alt/larg/esp) em várias medições.</summary>
        public static int AlterarDimensaoEmVarias(IEnumerable<string> handles,
            string campo, double valor)
        {
            return EditarVarias(handles, p => AplicarDimensao(p, campo, valor));
        }

        /// <summary>Define o artigo a que esta medição pertence.</summary>
        public static bool DefinirArtigo(string handle, string artigo)
        {
            return EditarParede(handle, p => p.Artigo = artigo ?? "");
        }

        /// <summary>Guarda o texto que o utilizador escreveu na linha de título.</summary>
        public static bool DefinirTextoTitulo(string handle, string texto)
        {
            return EditarParede(handle, p => p.TextoTitulo = texto ?? "");
        }

        /// <summary>Apaga a polyline da parede do desenho.</summary>
        public static bool RemoverParede(string handle)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (!TryGetId(db, handle, out ObjectId id)) return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                ent.Erase();
                tr.Commit();
            }
            return true;
        }

        /// <summary>
        /// Grava a XData da medição. Aceita qualquer entidade: além das
        /// polylines, uma hachura também pode ser uma medição (por área).
        /// </summary>
        public static void GravarXData(Entity pl, Parede parede)
        {
            // Único sítio por onde TODAS as medições de alvenaria passam, e
            // por isso o sítio certo para o aviso de unidade. Avisa e deixa
            // passar: os mapas reais trazem artigos com a unidade em branco e
            // travar a medição por causa disso seria pior do que o erro que
            // evitava. Fica escrito no log, que é onde se confere.
            AvisarUnidade(parede);

            pl.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.Servico ?? "ALVENARIA"),
                new TypedValue((int)DxfCode.ExtendedDataReal, parede.Altura),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.SerializeVaos()),
                new TypedValue((int)DxfCode.ExtendedDataReal, parede.Espessura),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    parede.Retangulo ? "RET" : "POL"),
                new TypedValue((int)DxfCode.ExtendedDataReal, parede.Largura),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.Piso ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.Bloco ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.Alcado ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.Separador ? "1" : "0"),
                // s7 — título antes desta medição. Acrescentado no fim de
                // propósito: desenhos medidos antes disto continuam a ler bem.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.MarcaDepois ?? ""),
                // s8 — texto que o utilizador escreveu na linha de título.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.TextoTitulo ?? ""),
                // s9 — artigo a que a medição pertence. Também no fim, pela
                // mesma razão de sempre: desenhos antigos continuam a ler.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.Artigo ?? ""),
                // r3 — ordem de origem (só usada no que vem importado).
                new TypedValue((int)DxfCode.ExtendedDataReal, (double)parede.Ordem),
                // r4 — comprimento dos outros troços da mesma parede.
                new TypedValue((int)DxfCode.ExtendedDataReal, parede.ComprimentoExtra),
                // s10 — o que a pessoa escreveu na linha desta medição.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, parede.Nota ?? ""),
                // s11 — QUANTAS linhas em branco a seguir a esta medição.
                // Era "1"/"0"; passou a número. Os desenhos antigos continuam
                // a ler bem porque "1" e "0" são números válidos — não foi
                // preciso campo novo nem migração.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    parede.LinhasEmBrancoDepois.ToString(CultureInfo.InvariantCulture)),
                // s12 — medição por área (hachura), em vez de comp × altura.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    parede.SoArea ? "1" : "0"),
                // s13 — a geometria interrompe-se nos vãos (hachura de planta).
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    parede.GeometriaSemVaos ? "1" : "0"),
                // s14 — área desenhada em planta × altura do painel (m³).
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    parede.AreaVezesAltura ? "1" : "0"),
                // Acrescentado no fim para manter a leitura de desenhos
                // antigos: handle da geometria original, quando existir.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    parede.HandleOrigem ?? "")
            );
        }

        // ------------------------------------------------------------------

        private static bool EditarParede(string handle, System.Action<Parede> edicao)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (!TryGetId(db, handle, out ObjectId id)) return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Entity e NAO Polyline.
                //
                // Uma medição também pode ser uma HACHURA — o GravarXData
                // aceita-as desde sempre e o Leitura.Tudo lê-as. Mas aqui o
                // cast a Polyline devolvia null nessas, e o método saía com
                // false SEM ALTERAR NADA e sem se queixar. O efeito na
                // paleta era este: limpar o Piso funcionava nas medições
                // desenhadas à polyline e não funcionava nas que vieram de
                // hachuras do projecto, na mesma grelha, sem nada que o
                // distinguisse. Vale para tudo o que passa por aqui — piso,
                // nota, artigo, alturas, larguras, separadores.
                var ent = tr.GetObject(id, OpenMode.ForWrite) as Entity;
                if (ent == null) return false;

                Parede parede;
                using (ResultBuffer rb = ent.GetXDataForApplication(AppName))
                {
                    if (rb == null) return false;
                    parede = ParseXData(rb);
                }

                edicao(parede);
                GravarXData(ent, parede);
                tr.Commit();
            }
            return true;
        }

        /// <summary>
        /// Ordem: serviço(s0), altura(r0), vãos(s1), espessura(r1), tipo(s2),
        /// largura(r2), piso(s3). Desenhos antigos leem-se na mesma.
        /// Desenhos medidos antes da espessura existir continuam a ler bem.
        /// </summary>
        private static Parede ParseXData(ResultBuffer rb)
        {
            var parede = new Parede { Servico = "ALVENARIA", Altura = 0.0 };
            int stringIndex = 0, realIndex = 0;

            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    string s = tv.Value.ToString();
                    if (stringIndex == 0) parede.Servico = s;
                    else if (stringIndex == 1) parede.DeserializeVaos(s);
                    else if (stringIndex == 2) parede.Retangulo = (s == "RET");
                    else if (stringIndex == 3) parede.Piso = s;
                    else if (stringIndex == 4) parede.Bloco = s;
                    else if (stringIndex == 5) parede.Alcado = s;
                    else if (stringIndex == 6) parede.Separador = (s == "1");
                    else if (stringIndex == 7) parede.MarcaDepois = s;
                    else if (stringIndex == 8) parede.TextoTitulo = s;
                    else if (stringIndex == 9) parede.Artigo = s;
                    else if (stringIndex == 10) parede.Nota = s;
                    else if (stringIndex == 11)
                    {
                        // Lê o número novo e o "1"/"0" antigo pelo mesmo
                        // caminho. Um valor estranho vale zero: mais vale
                        // faltar uma linha em branco do que rebentar a
                        // leitura de uma medição inteira por causa dela.
                        int n;
                        if (!int.TryParse(s, NumberStyles.Integer,
                                CultureInfo.InvariantCulture, out n)) n = 0;
                        if (n < 0) n = 0;
                        if (n > Parede.MaxLinhasEmBranco) n = Parede.MaxLinhasEmBranco;
                        parede.LinhasEmBrancoDepois = n;
                    }
                    else if (stringIndex == 12) parede.SoArea = (s == "1");
                    else if (stringIndex == 13) parede.GeometriaSemVaos = (s == "1");
                    else if (stringIndex == 14) parede.AreaVezesAltura = (s == "1");
                    else if (stringIndex == 15) parede.HandleOrigem = s;
                    stringIndex++;
                }
                else if (tv.TypeCode == (int)DxfCode.ExtendedDataReal)
                {
                    if (realIndex == 0) parede.Altura = (double)tv.Value;
                    else if (realIndex == 1) parede.Espessura = (double)tv.Value;
                    else if (realIndex == 2) parede.Largura = (double)tv.Value;
                    else if (realIndex == 3) parede.Ordem = (int)(double)tv.Value;
                    else if (realIndex == 4) parede.ComprimentoExtra = (double)tv.Value;
                    realIndex++;
                }
            }
            return parede;
        }

        private static bool TryGetId(Database db, string handleHex, out ObjectId id)
        {
            id = ObjectId.Null;
            if (!long.TryParse(handleHex, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value))
                return false;
            return db.TryGetObjectId(new Handle(value), out id) && !id.IsErased;
        }
    }
}
