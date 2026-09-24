using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>Candidato a vão encontrado no DWG.</summary>
    public class VaoCandidato
    {
        public string Designacao { get; set; }
        public double LarguraM { get; set; }
        public double AlturaM { get; set; }
        public string Fonte { get; set; }        // Texto / MText / Bloco
        public string TextoOriginal { get; set; }
        public string Unidade { get; set; }      // mm / cm / m
        public Point3d Posicao { get; set; }
        public double Distancia { get; set; }    // à medição, em m
        public bool Selecionado { get; set; } = true;
        public bool PreAro { get; set; }

        /// <summary>Janela quando a designação sugere caixilho/vão exterior.</summary>
        public TipoVao TipoSugerido
        {
            get
            {
                string d = (Designacao ?? "").ToUpperInvariant();
                bool janela = d.StartsWith("VE") || d.StartsWith("J") ||
                              d.StartsWith("AL") || d.StartsWith("C.") ||
                              d.StartsWith("FO");
                return janela ? TipoVao.Janela : TipoVao.Porta;
            }
        }
    }

    /// <summary>
    /// Localiza vãos por textos e blocos. Independente da convenção do projeto:
    /// aceita "VE.02 1190x2350", "PC.04 200X160", "AL205 2,67x2,62 m", "0.90x2.10".
    /// A unidade (mm/cm/m) é inferida por plausibilidade, não assumida.
    /// </summary>
    public static class VaoDetector
    {
        /// <summary>
        /// Margem em torno da medição onde procurar rótulos (m). Os XREFs nem
        /// sempre usam a mesma escala do modelo, pelo que uma margem curta
        /// perde etiquetas que visualmente estão encostadas à parede.
        /// </summary>
        public static double Tolerancia = 2.50;

        // A leitura das cotas e a inferência de unidade estão em DimensaoVao,
        // que não depende do AutoCAD e por isso tem testes. Aqui ficou o que
        // só faz sentido com um desenho aberto: onde procurar os textos.
        private static readonly Regex RxDim = DimensaoVao.RxDim;
        private static readonly Regex RxDesignacao = DimensaoVao.RxDesignacao;

        private class TextEntry
        {
            public Point3d Pos;
            public string[] Linhas;
            public string Fonte;
            public bool EhBloco;
        }

        private class RotuloVao
        {
            public string Designacao;
            public Point3d Pos;
            public bool DeBloco;
        }

        // Só é usada para uma etiqueta de vão sem cota legível. A tabela
        // continua a mostrar a origem para que a largura seja confirmada.
        private const double LarguraVaoProvisoria = 0.90;

        /// <summary>Deteta na zona da medição (com margem).</summary>
        public static List<VaoCandidato> Detectar(Database db, Extents3d zona,
            double? tolerancia = null)
        {
            // Mantém esta sobrecarga para integrações que usavam o detector
            // directamente. A chamada interna indica sempre o contexto.
            return Detectar(db, zona, true, 0, tolerancia);
        }

        /// <summary>
        /// Deteta na zona da medição, sabendo se ela é uma planta ou um
        /// alçado. Em planta, a altura proposta é a do painel.
        /// </summary>
        public static List<VaoCandidato> Detectar(Database db, Extents3d zona,
            bool emAlcado, double alturaPainel, double? tolerancia = null)
        {
            double tol = tolerancia ?? Tolerancia;
            var min = new Point3d(zona.MinPoint.X - tol, zona.MinPoint.Y - tol, 0);
            var max = new Point3d(zona.MaxPoint.X + tol, zona.MaxPoint.Y + tol, 0);
            var centro = new Point3d(
                (zona.MinPoint.X + zona.MaxPoint.X) / 2.0,
                (zona.MinPoint.Y + zona.MaxPoint.Y) / 2.0, 0);

            var entries = Recolher(db, p => Dentro(p, min, max));
            return MontarCandidatos(entries, centro, emAlcado, alturaPainel, true, tol);
        }

        /// <summary>
        /// Deteta a partir de entidades escolhidas à mão pelo utilizador.
        ///
        /// Aceita duas coisas diferentes na mesma selecção:
        ///   · ROTULAGEM — textos, MText e blocos, de onde se lê "VE.02 1190x2350";
        ///   · GEOMETRIA — rectângulos, polylines, linhas e hachuras que SÃO o
        ///     vão, e cujas medidas se leem do próprio desenho.
        ///
        /// A segunda faltava, e era o que obrigava a escrever à mão os vãos de
        /// desenhos onde as aberturas estão desenhadas mas não cotadas.
        /// </summary>
        /// <param name="emAlcado">
        /// A medição a que estes vãos vão pertencer é de fachada (alçado). Ver
        /// <see cref="DimensaoVao.DeGeometria"/>: é isto que decide se a
        /// segunda dimensão do desenho é a altura ou a espessura da parede.
        /// </param>
        /// <param name="alturaPainel">
        /// Altura a dar aos vãos lidos em planta. SEM VALOR POR OMISSÃO de
        /// propósito: um zero aqui faz a leitura em planta dar altura zero, que
        /// é recusada por implausível — e a geometria toda desaparecia sem uma
        /// palavra, com o detector a dizer que não reconheceu nada. Já
        /// aconteceu; quem chama tem de dizer que altura quer.
        /// </param>
        public static List<VaoCandidato> DetectarEmSelecao(Database db, ObjectId[] ids,
            bool emAlcado, double alturaPainel)
        {
            var entries = new List<TextEntry>();
            var geometricos = new List<VaoCandidato>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids)
                {
                    DBObject obj;
                    try { obj = tr.GetObject(id, OpenMode.ForRead); }
                    catch { continue; }

                    var entry = Extrair(obj, tr);
                    if (entry != null) entries.Add(entry);

                    var geo = ExtrairGeometria(obj, emAlcado, alturaPainel);
                    if (geo != null) geometricos.Add(geo);

                    // Se selecionou o XREF/bloco inteiro, lê o que está lá dentro
                    if (obj is BlockReference br)
                    {
                        try
                        {
                            var def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)
                                as BlockTableRecord;
                            if (def != null && !def.IsLayout)
                                Percorrer(def, tr, br.BlockTransform,
                                    _ => true, entries, 1, new HashSet<ObjectId>());
                        }
                        catch { }
                    }
                }
                tr.Commit();
            }
            // Sem zona de medição não há centro: null em vez de um Point3d de
            // sentinela (Point3d.Origin) — comparar com igualdade exata a um
            // centro real na origem seria frágil e errado.
            var lidos = MontarCandidatos(entries, null, emAlcado, alturaPainel, false,
                Tolerancia);

            // A geometria à frente do que se leu em texto: quem seleccionou o
            // próprio rectângulo do vão foi mais explícito do que quem apanhou
            // um rótulo por perto, e é a primeira linha da tabela que se
            // confere com mais atenção.
            geometricos.AddRange(lidos);
            return geometricos;
        }

        /// <summary>
        /// Lê um vão da GEOMETRIA de uma entidade desenhada, quando ela é o
        /// próprio vão e não um rótulo dele.
        ///
        /// Devolve null para tudo o que não seja geometria fechada plausível —
        /// incluindo o que dê medidas fora dos limites de um vão real. Recusar
        /// é melhor do que inventar: um número mau aqui entra na medição sem
        /// dar nas vistas.
        /// </summary>
        private static VaoCandidato ExtrairGeometria(DBObject obj, bool emAlcado,
            double alturaPainel)
        {
            var ent = obj as Entity;
            if (ent == null) return null;

            double lado1 = 0, lado2 = 0;
            string fonte;

            var pl = obj as Polyline;
            if (pl != null)
            {
                // O rectângulo é o caso bom: dá os dois lados sem depender da
                // orientação. É o mesmo teste que o TSKMEDSEL usa na parede.
                if (AlvRepo.EhRectangulo(pl))
                {
                    AlvRepo.LadosDoRetangulo(pl, out lado1, out lado2);
                    fonte = "Rectângulo";
                }
                else fonte = "Polyline";
            }
            else if (obj is Hatch) fonte = "Hachura";
            else if (obj is Line) fonte = "Linha";
            else return null;      // texto, bloco e o resto seguem o outro caminho

            double dx, dy;
            Point3d centro;
            try
            {
                var ext = ent.GeometricExtents;
                dx = ext.MaxPoint.X - ext.MinPoint.X;
                dy = ext.MaxPoint.Y - ext.MinPoint.Y;
                centro = new Point3d((ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                                     (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0, 0);
            }
            catch { return null; }   // entidade sem extensão (vazia ou degenerada)

            double larg, alt;
            if (!DimensaoVao.DeGeometria(dx, dy, lado1, lado2, emAlcado,
                                         alturaPainel, out larg, out alt))
                return null;

            return new VaoCandidato
            {
                Designacao = "VÃO",
                LarguraM = larg,
                AlturaM = alt,
                Unidade = "m",       // a geometria já está nas unidades do desenho
                Fonte = fonte,
                TextoOriginal = fonte + " " + Util.N2(larg) + " × " + Util.N2(alt) +
                                (emAlcado ? "" : " (altura do painel)"),
                Posicao = centro,
                Distancia = 0
            };
        }

        // ------------------------------------------------------------------
        /// <summary>Percorre o Model Space e, recursivamente, XREFs e blocos.</summary>
        private static List<TextEntry> Recolher(Database db, Func<Point3d, bool> filtro)
        {
            var entries = new List<TextEntry>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);

                Percorrer(ms, tr, Matrix3d.Identity, filtro, entries, 0,
                    new HashSet<ObjectId>());
                tr.Commit();
            }
            return entries;
        }

        /// <summary>
        /// Recursão em profundidade limitada. As coordenadas dentro de um XREF
        /// ou bloco são convertidas para o espaço do desenho pela BlockTransform.
        /// </summary>
        private static void Percorrer(BlockTableRecord btr, Transaction tr, Matrix3d xform,
            Func<Point3d, bool> filtro, List<TextEntry> entries, int profundidade,
            HashSet<ObjectId> ancestrais)
        {
            // Um XREF de arquitetura costuma conter blocos dentro de blocos
            // (caixilho -> tipo -> etiqueta). O limite antigo de três níveis
            // cortava precisamente essas etiquetas: o VE.02 era visível no
            // ecrã, mas nunca chegava ao detector. Evita-se recursão circular
            // pela pilha de definições; 32 fica apenas como fusível para DWGs
            // corrompidos, não como regra funcional.
            if (profundidade > 32) return;
            if (ancestrais == null) ancestrais = new HashSet<ObjectId>();
            var caminho = new HashSet<ObjectId>(ancestrais);
            if (!caminho.Add(btr.ObjectId)) return;

            foreach (ObjectId id in btr)
            {
                DBObject obj;
                try { obj = tr.GetObject(id, OpenMode.ForRead); }
                catch { continue; }

                var entidade = obj as Entity;
                var entry = Extrair(obj, tr);
                // Não reler o resumo criado pelo TSK ("C × A = área m²") como
                // se fosse uma dimensão de vão. A layer MED_* não basta para
                // excluir: desenhos de arquitetura também a usam para cotas e
                // rótulos VE/VI, e isso fazia desaparecer vãos verdadeiros.
                if (entry != null && entidade != null)
                {
                    try
                    {
                        if (FiltroDeteccaoVaos.EhEtiquetaGeradaPeloPlugin(
                            entidade.Layer, Commands.LayerPrefix,
                            string.Join(" ", entry.Linhas)))
                            entry = null;
                    }
                    catch { }
                }
                if (entry != null)
                {
                    entry.Pos = entry.Pos.TransformBy(xform);
                    if (filtro(entry.Pos)) entries.Add(entry);
                }

                // Entrar dentro de XREFs e blocos (as etiquetas de vão vivem lá)
                if (obj is BlockReference br)
                {
                    try
                    {
                        var def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)
                            as BlockTableRecord;
                        if (def == null || def.IsLayout) continue;
                        if (def.IsFromExternalReference && !def.IsResolved) continue;

                        Percorrer(def, tr, xform * br.BlockTransform,
                            filtro, entries, profundidade + 1, caminho);
                    }
                    catch { }
                }
            }
        }

        private static TextEntry Extrair(DBObject obj, Transaction tr)
        {
            if (obj is DBText txt)
            {
                return new TextEntry
                {
                    Pos = txt.Position,
                    Linhas = new[] { txt.TextString },
                    Fonte = "Texto"
                };
            }
            if (obj is MText mt)
            {
                return new TextEntry
                {
                    Pos = mt.Location,
                    Linhas = mt.Text.Split(new[] { '\n', '\r' },
                        StringSplitOptions.RemoveEmptyEntries),
                    Fonte = "MText"
                };
            }
            if (obj is MLeader ml)
            {
                try
                {
                    if (ml.ContentType == ContentType.MTextContent)
                    {
                        var texto = ml.MText;
                        if (texto != null && !string.IsNullOrWhiteSpace(texto.Text))
                            return new TextEntry
                            {
                                Pos = texto.Location,
                                Linhas = texto.Text.Split(new[] { '\n', '\r' },
                                    StringSplitOptions.RemoveEmptyEntries),
                                Fonte = "Multileader"
                            };
                    }
                }
                catch { }
            }
            if (obj is Dimension dim)
            {
                try
                {
                    string texto = dim.DimensionText;
                    // "<>" é apenas o valor medido automaticamente, não uma
                    // designação escrita pelo projetista.
                    if (!string.IsNullOrWhiteSpace(texto) && texto != "<>")
                        return new TextEntry
                        {
                            Pos = dim.TextPosition,
                            Linhas = new[] { texto },
                            Fonte = "Cota"
                        };
                }
                catch { }
            }
            if (obj is BlockReference br)
            {
                var linhas = new List<string>();
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    var att = tr.GetObject(attId, OpenMode.ForRead) as AttributeReference;
                    if (att != null && !string.IsNullOrWhiteSpace(att.TextString))
                        linhas.Add(att.TextString);
                }
                try
                {
                    var btr = (BlockTableRecord)tr.GetObject(
                        br.DynamicBlockTableRecord, OpenMode.ForRead);
                    // O nome de um XREF é o nome do ficheiro, não texto visível
                    // no desenho. Ficheiros/blocos chamados VI04 estavam a ser
                    // inventados como vãos e associados a cotas 200x160/40x40.
                    // Nos blocos locais o nome pode, legitimamente, ser o tipo
                    // da porta/janela e continua a ser aproveitado.
                    if (!btr.IsFromExternalReference)
                        linhas.Add(btr.Name);
                }
                catch { }

                if (linhas.Count > 0)
                    return new TextEntry
                    {
                        Pos = br.Position,
                        Linhas = linhas.ToArray(),
                        Fonte = "Bloco",
                        EhBloco = true
                    };
            }
            return null;
        }

        // ------------------------------------------------------------------
        private static List<VaoCandidato> MontarCandidatos(List<TextEntry> entries,
            Point3d? centro, bool emAlcado, double alturaPainel,
            bool filtrarGenericosSemRotulo, double raioAssociacao)
        {
            var candidatos = new List<VaoCandidato>();

            foreach (var entry in entries)
            {
                string designacao = entry.Linhas
                    .Select(l => RxDesignacao.Match(l))
                    .Where(m => m.Success)
                    .Select(m => m.Value.ToUpperInvariant().Replace(" ", ""))
                    .FirstOrDefault();

                foreach (var linha in entry.Linhas)
                {
                    foreach (Match m in RxDim.Matches(linha))
                    {
                        string sa = m.Groups["a"].Value, sb = m.Groups["b"].Value;
                        if (!TryConverter(sa, sb, emAlcado, alturaPainel,
                            out double larg, out double alt, out string un))
                            continue; // dimensão implausível para um vão

                        candidatos.Add(new VaoCandidato
                        {
                            Designacao = designacao ?? "VÃO",
                            LarguraM = larg,
                            AlturaM = alt,
                            Unidade = un,
                            Fonte = entry.Fonte,
                            TextoOriginal = linha.Trim(),
                            Posicao = entry.Pos,
                            Distancia = centro.HasValue
                                ? entry.Pos.DistanceTo(centro.Value)
                                : 0
                        });
                    }
                }
            }

            // Em muitos DWG a designação (VE.04.02, P01, ...) e a cota vivem
            // em textos separados. Cada etiqueta é uma ocorrência real de
            // vão, mesmo quando todas usam o mesmo tipo. A mesma cota não
            // pode ser atribuída duas vezes: era isso que fazia sete VE.02
            // acabarem reduzidos a uma linha, ou multiplicados por cotas
            // vizinhas de mobiliário.
            var rotulos = new List<RotuloVao>();
            foreach (var entry in entries)
            {
                string designacao = entry.Linhas
                    .Select(l => RxDesignacao.Match(l))
                    .Where(m => m.Success)
                    .Select(m => m.Value.ToUpperInvariant().Replace(" ", ""))
                    .FirstOrDefault();
                if (designacao == null) continue;

                var igual = rotulos.FirstOrDefault(r =>
                    string.Equals(r.Designacao, designacao,
                        StringComparison.OrdinalIgnoreCase) &&
                    r.Pos.DistanceTo(entry.Pos) <= 0.15);
                if (igual == null)
                    rotulos.Add(new RotuloVao
                    {
                        Designacao = designacao,
                        Pos = entry.Pos,
                        DeBloco = entry.EhBloco
                    });
                else if (entry.EhBloco)
                    igual.DeBloco = true;
            }

            if (filtrarGenericosSemRotulo)
                rotulos = PreferirFamiliaExterior(rotulos);

            if (filtrarGenericosSemRotulo && rotulos.Count > 0)
            {
                var porRotulo = new List<VaoCandidato>();
                var cotasUsadas = new HashSet<VaoCandidato>();
                foreach (var rotulo in rotulos.OrderByDescending(r => r.DeBloco))
                {
                    VaoCandidato maisProximo = null;
                    double distancia = double.MaxValue;
                    foreach (var candidato in candidatos)
                    {
                        if (cotasUsadas.Contains(candidato)) continue;
                        double d = candidato.Posicao.DistanceTo(rotulo.Pos);
                        if (d < distancia)
                        {
                            distancia = d;
                            maisProximo = candidato;
                        }
                    }

                    if (maisProximo != null && distancia <= raioAssociacao)
                    {
                        maisProximo.Designacao = rotulo.Designacao;
                        cotasUsadas.Add(maisProximo);
                        porRotulo.Add(maisProximo);
                        continue;
                    }

                    porRotulo.Add(new VaoCandidato
                    {
                        Designacao = rotulo.Designacao,
                        LarguraM = LarguraVaoProvisoria,
                        AlturaM = alturaPainel,
                        Unidade = "m",
                        Fonte = "Etiqueta sem cota",
                        TextoOriginal = rotulo.Designacao + " (sem cota — confirmar largura)",
                        Posicao = rotulo.Pos,
                        Distancia = centro.HasValue
                            ? rotulo.Pos.DistanceTo(centro.Value)
                            : 0
                    });
                }
                candidatos = porRotulo;
            }
            else
            {
                // Na selecção manual mantém-se a associação mais próxima sem
                // remover as cotas que o utilizador escolheu explicitamente.
                foreach (var rotulo in rotulos)
                {
                    if (candidatos.Count == 0) break;

                    VaoCandidato maisProximo = null;
                    double distancia = double.MaxValue;
                    foreach (var candidato in candidatos)
                    {
                        double d = candidato.Posicao.DistanceTo(rotulo.Pos);
                        if (d < distancia)
                        {
                            distancia = d;
                            maisProximo = candidato;
                        }
                    }

                    if (maisProximo != null && distancia <= raioAssociacao)
                        maisProximo.Designacao = rotulo.Designacao;
                }
            }

            if (filtrarGenericosSemRotulo)
                candidatos = ConsolidarDuplicados(candidatos);

            // Rótulo com 2 cotas (tosco + aro): só a primeira vem marcada
            foreach (var grupo in candidatos.GroupBy(c => c.TextoOriginal + "|" + c.Posicao))
            {
                bool primeiro = true;
                foreach (var c in grupo) { c.Selecionado = primeiro; primeiro = false; }
            }

            return candidatos
                .OrderBy(c => c.Distancia)
                .ThenBy(c => c.Designacao)
                .ToList();
        }

        /// <summary>
        /// A mesma cota pode ser lida no atributo da inserção e outra vez na
        /// definição do bloco/XREF. Conserva uma só quando designação, medidas
        /// e posição confirmam que se trata da mesma abertura; vãos iguais em
        /// sítios diferentes continuam a entrar todos.
        /// </summary>
        private static List<VaoCandidato> ConsolidarDuplicados(
            List<VaoCandidato> candidatos)
        {
            var unicos = new List<VaoCandidato>();
            foreach (var candidato in candidatos)
            {
                bool repetido = unicos.Any(existente =>
                    FiltroDeteccaoVaos.SaoLeiturasDuplicadas(
                        existente.Designacao, existente.LarguraM, existente.AlturaM,
                        candidato.Designacao, candidato.LarguraM, candidato.AlturaM,
                        existente.Posicao.DistanceTo(candidato.Posicao)));
                if (!repetido) unicos.Add(candidato);
            }
            return unicos;
        }

        /// <summary>
        /// Quando a parede exterior tem várias etiquetas VE, as VI/P apanhadas
        /// pela margem de pesquisa pertencem normalmente às divisões vizinhas.
        /// Conserva todas as ocorrências VE — incluindo tipos diferentes como
        /// 3 × VE.02 e 2 × VE.01 — e não as agrupa por designação.
        /// </summary>
        private static List<RotuloVao> PreferirFamiliaExterior(List<RotuloVao> rotulos)
        {
            int exteriores = rotulos.Count(r => r.Designacao.StartsWith("VE",
                StringComparison.OrdinalIgnoreCase));
            if (exteriores < 2) return rotulos;

            return rotulos.Where(r => r.Designacao.StartsWith("VE",
                StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private static bool TryConverter(string sa, string sb, bool emAlcado,
            double alturaPainel, out double larg, out double alt, out string unidade) =>
            DimensaoVao.DeTexto(sa, sb, emAlcado, alturaPainel,
                out larg, out alt, out unidade);

        private static bool Dentro(Point3d p, Point3d min, Point3d max) =>
            p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;
    }

}
