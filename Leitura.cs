using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace TSKTakeOff
{
    /// <summary>
    /// Lê tudo o que o plugin tem no desenho numa passagem só.
    ///
    /// Antes eram três: uma para as paredes, outra para os materiais, outra
    /// para as contagens. Cada uma percorria o Model Space inteiro e abria uma
    /// transacção própria. Como isto corre a cada medição, a cada vão e a cada
    /// célula editada na grelha, num projecto de arquitectura o custo
    /// multiplicava-se por três sem qualquer necessidade: a lista de entidades
    /// é a mesma nas três.
    /// </summary>
    public static class Leitura
    {
        /// <summary>Tudo o que está medido no desenho, numa travessia só.</summary>
        public static void Tudo(Database db,
            out List<Parede> paredes,
            out List<MedFachada> fachadas,
            out List<MedItem> lineares,
            out List<MedContagem> contagens)
        {
            paredes = new List<Parede>();
            fachadas = new List<MedFachada>();
            lineares = new List<MedItem>();
            contagens = new List<MedContagem>();
            if (db == null) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    // O tipo sabe-se sem abrir a entidade. Só se abre o que
                    // pode ser nosso — num desenho de arquitectura isso é um
                    // punhado entre dezenas de milhares de objectos.
                    if (id.ObjectClass == Util.ClPolyline)
                    {
                        var pl = tr.GetObject(id, OpenMode.ForRead) as Polyline;
                        if (pl == null) continue;

                        // Uma polyline é parede, pano de material OU medição
                        // linear, nunca as duas: se já se sabe o que é, não se
                        // procura mais.
                        var parede = AlvRepo.LerParede(pl);
                        if (parede != null) { paredes.Add(parede); continue; }

                        var fachada = FacRepo.LerFachada(pl);
                        if (fachada != null) { fachadas.Add(fachada); continue; }

                        // Medições lineares simples (MEDIR/TSKLINEAR): polyline
                        // com XData próprio. Sem isto, o que se media a linear
                        // sumia da folha ao vivo e da exportação pelo modelo —
                        // só aparecia no .xlsx simples.
                        using (ResultBuffer rb =
                            pl.GetXDataForApplication(Commands.AppNameLinear))
                        {
                            if (rb == null) continue;

                            string categoria = "GERAL";
                            foreach (TypedValue tv in rb)
                            {
                                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                                {
                                    categoria = tv.Value.ToString();
                                    break;
                                }
                            }

                            lineares.Add(new MedItem
                            {
                                Handle = pl.Handle.ToString(),
                                Categoria = categoria,
                                Layer = pl.Layer,
                                Comprimento = pl.Length,
                                Vertices = pl.NumberOfVertices
                            });
                        }
                    }
                    else if (id.ObjectClass == Util.ClHatch)
                    {
                        // Hachuras marcadas como medição de área.
                        var porArea = AlvRepo.LerParedeDeHatch(
                            tr.GetObject(id, OpenMode.ForRead) as Hatch);
                        if (porArea != null) paredes.Add(porArea);
                    }
                    else if (id.ObjectClass == Util.ClCircle)
                    {
                        var contagem = ContRepo.LerContagem(
                            tr.GetObject(id, OpenMode.ForRead) as Circle);
                        if (contagem != null) contagens.Add(contagem);
                    }
                }
                tr.Commit();
            }
        }
    }
}
