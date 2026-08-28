using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TSKTakeOff.ImportarCmd))]

namespace TSKTakeOff
{
    /// <summary>
    /// Lê uma folha de medições já preenchida e reconstrói as medições no DWG.
    ///
    /// Existe por uma razão prática: as medições vivem na XData das polylines,
    /// portanto um desenho fechado sem gravar leva-as com ele. O Excel, esse,
    /// costuma estar guardado. Este importador fecha esse buraco — pega nos
    /// números que sobreviveram e devolve-os ao plugin.
    ///
    /// O que NÃO consegue devolver: a geometria original. O Excel não guarda
    /// coordenadas. Por isso cada medição renasce como um rectângulo de
    /// comprimento × espessura, arrumado numa coluna à parte do desenho. As
    /// contas ficam certas e tudo o resto (paleta, Excel ao vivo, exportação)
    /// funciona na mesma; o que se perde é saber ONDE no projeto foi medido.
    /// </summary>
    public static class ImportarExcel
    {
        /// <summary>Uma folha do livro e o que lá se encontrou.</summary>
        public class ResumoFolha
        {
            public string Nome;
            public int Medicoes;
            public int Vaos;
            public int Artigos;

            public override string ToString()
            {
                if (Medicoes == 0) return Nome + "   (sem medições)";
                return string.Format("{0}   —   {1} medições, {2} vãos, {3} artigos",
                                     Nome, Medicoes, Vaos, Artigos);
            }
        }

        /// <summary>
        /// Espreita o livro e diz o que tem cada folha, sem tocar no desenho.
        /// É com isto que se escolhe a folha certa antes de importar.
        /// </summary>
        public static List<ResumoFolha> Inventario(string ficheiro)
        {
            var resumo = new List<ResumoFolha>();
            var tipo = Type.GetTypeFromProgID("Excel.Application");
            if (tipo == null)
                throw new InvalidOperationException("O Excel não está instalado nesta máquina.");

            dynamic app = null, wb = null;
            bool nossa = true;
            try
            {
                app = Activator.CreateInstance(tipo);
                try { nossa = ((int)app.Workbooks.Count) == 0; } catch { nossa = false; }
                if (nossa) app.Visible = false;
                try { app.DisplayAlerts = false; } catch { }
                try { app.AskToUpdateLinks = false; } catch { }

                wb = app.Workbooks.Open(ficheiro, 0, true);

                foreach (dynamic ws in wb.Worksheets)
                {
                    var r = new ResumoFolha { Nome = (string)ws.Name };
                    var desta = new List<Parede>();
                    LerFolha(ws, desta);
                    r.Medicoes = desta.Count;
                    foreach (var p in desta)
                    {
                        r.Vaos += p.Vaos.Count;
                        if (!string.IsNullOrEmpty(p.Artigo)) r.Artigos++;
                    }
                    resumo.Add(r);
                }
            }
            finally
            {
                if (nossa)
                {
                    try { if (wb != null) wb.Close(false); } catch { }
                    try { if (app != null) app.Quit(); } catch { }
                }
            }
            return resumo;
        }

        /// <summary>Quantas entidades de importações anteriores há no desenho.</summary>
        public static int ContarImportadas(Autodesk.AutoCAD.DatabaseServices.Database db)
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.Layer == LayerImportado) n++;
                }
                tr.Commit();
            }
            return n;
        }

        /// <summary>Nome da layer onde nascem as medições recuperadas.</summary>
        public const string LayerImportado = "MED_IMPORTADO";

        /// <summary>Espaço entre rectângulos na arrumação (m).</summary>
        private const double Folga = 1.0;

        // ==================================================================
        // Leitura
        // ==================================================================

        /// <summary>
        /// Lê o ficheiro e devolve as paredes reconstruídas, sem tocar no DWG.
        /// Separado da escrita de propósito: assim dá para diagnosticar uma
        /// folha estranha sem sujar o desenho.
        /// </summary>
        public static List<Parede> Ler(string ficheiro, out string diagnostico)
        {
            return Ler(ficheiro, null, out diagnostico);
        }

        /// <summary>
        /// Como o outro <c>Ler</c>, mas só da folha indicada. Um livro de obra
        /// costuma ter uma folha por apartamento ou por fase; ler todas juntava
        /// medições de sítios diferentes no mesmo desenho.
        /// </summary>
        public static List<Parede> Ler(string ficheiro, string soEstaFolha,
            out string diagnostico)
        {
            diagnostico = "";
            var paredes = new List<Parede>();

            var tipo = Type.GetTypeFromProgID("Excel.Application");
            if (tipo == null)
                throw new InvalidOperationException("O Excel não está instalado nesta máquina.");

            dynamic app = null, wb = null;
            bool instanciaNossa = true;
            try
            {
                app = Activator.CreateInstance(tipo);

                // O COM nem sempre dá uma instância nova: às vezes devolve o
                // Excel que o utilizador já tem aberto. Se já lá estiverem
                // livros, a instância não é nossa — não se esconde nem se fecha,
                // senão o comando que serve para recuperar trabalho era o mesmo
                // que fazia perder o que estivesse por gravar.
                try { instanciaNossa = ((int)app.Workbooks.Count) == 0; }
                catch { instanciaNossa = false; }

                if (instanciaNossa) app.Visible = false;
                try { app.DisplayAlerts = false; } catch { }
                try { app.AskToUpdateLinks = false; } catch { }

                // ReadOnly: nunca mexer no ficheiro do cliente ao importar.
                // Se o livro já estiver aberto nesta instância, o Excel devolve
                // o que já lá está em vez de o reabrir — e por isso é que a
                // seguir só se fecha o que fomos nós a abrir.
                wb = app.Workbooks.Open(ficheiro, 0, true);

                var log = new List<string>();
                foreach (dynamic ws in wb.Worksheets)
                {
                    string nome = (string)ws.Name;
                    if (soEstaFolha != null &&
                        !string.Equals(nome, soEstaFolha, StringComparison.OrdinalIgnoreCase))
                        continue;
                    log.Add("  " + nome + ": " + LerFolha(ws, paredes));
                }
                diagnostico = string.Join("\n", log.ToArray());
            }
            finally
            {
                // Só se mexe no que é nosso. Numa instância do utilizador, o
                // livro fica aberto como ele o tinha e o Excel continua vivo.
                if (instanciaNossa)
                {
                    try { if (wb != null) wb.Close(false); } catch { }
                    try { if (app != null) app.Quit(); } catch { }
                }
            }

            return paredes;
        }

        /// <summary>
        /// Percorre uma folha. Devolve o motivo de a ter ignorado, ou "" se leu.
        /// </summary>
        private static string LerFolha(dynamic ws, List<Parede> paredes)
        {
            object[,] celulas;
            int nLinhas, nColunas;
            if (!LerBloco(ws, out celulas, out nLinhas, out nColunas))
                return "vazia";

            var mapa = ProcurarCabecalho(celulas, nLinhas, nColunas);
            if (mapa == null) return "sem cabeçalho reconhecível";

            string servico = string.IsNullOrWhiteSpace(Config.Servico)
                ? "ALVENARIA" : Config.Servico;
            string piso = "";
            string artigo = "";
            Parede ultima = null;
            int nMed = 0, nVaos = 0, nTitulos = 0, orfaos = 0, ordem = 0;

            for (int r = mapa.LinhaCabecalho + 1; r <= nLinhas; r++)
            {
                string item = Texto(celulas, r, mapa.ColItem);
                string desc = Texto(celulas, r, mapa.ColDesc);
                double? qt = Numero(celulas, r, mapa.ColQt);
                double? comp = Numero(celulas, r, mapa.ColComp);
                double? larg = Numero(celulas, r, mapa.ColLarg);
                double? alt = Numero(celulas, r, mapa.ColAlt);

                bool temDimensao = (comp.HasValue && comp.Value != 0) ||
                                   (alt.HasValue && alt.Value != 0);

                // ---- medição ou dedução -------------------------------------
                if (qt.HasValue && qt.Value != 0 && temDimensao)
                {
                    if (qt.Value < 0)
                    {
                        // Quantidade negativa é um vão a descontar. Pertence à
                        // medição imediatamente acima — é assim que a folha é
                        // escrita, e é assim que se lê de volta.
                        // Dedução antes de qualquer medição: não há a quem a
                        // ligar. Antes desaparecia calada — agora conta-se e
                        // diz-se, porque uma dedução perdida altera a área.
                        if (ultima == null) { orfaos++; continue; }
                        nVaos++;
                        ultima.Vaos.Add(new Vao
                        {
                            Designacao = desc,
                            Largura = comp ?? 0,
                            Altura = alt ?? 0,
                            Quantidade = (int)Math.Round(Math.Abs(qt.Value)),
                            Tipo = EhJanela(desc) ? TipoVao.Janela : TipoVao.Porta,
                            Espessura = Config.Espessura
                        });
                        continue;
                    }

                    ultima = new Parede
                    {
                        Servico = servico,
                        Piso = piso,
                        Bloco = "",
                        Alcado = "",
                        Comprimento = comp ?? 0,
                        Largura = (larg.HasValue && larg.Value > 0) ? larg.Value : 0,
                        Altura = alt ?? 0,
                        // A espessura não vai para o Excel em coluna nenhuma, por
                        // isso não há de onde a ler. Fica a da paleta — que é a
                        // que o utilizador tinha quando mediu.
                        Espessura = Config.Espessura > 0 ? Config.Espessura : 0.15,
                        Retangulo = true,
                        // O artigo em vigor e a posição na folha. São estes dois
                        // que fazem a folha voltar a sair como estava, em vez de
                        // ser reagrupada por ordem alfabética.
                        Artigo = artigo,
                        Ordem = ++ordem
                    };
                    paredes.Add(ultima);
                    nMed++;
                    continue;
                }

                if (desc.Length == 0 && item.Length == 0) continue;

                // ---- título escrito à mão -----------------------------------
                // Um artigo traz código na coluna Item. Quando o utilizador
                // troca, por exemplo, 4.9.1.1 por 1.0 ou 4.9, esse código pode
                // ser curto; nunca se deve decidir que é "piso" só porque a
                // descrição tem menos de 25 caracteres.
                //
                // A descrição longa continua a ser aceite para folhas antigas
                // que não tinham o código na coluna Item. Para descrições
                // curtas sem código mantém-se a regra de piso/alçado, evitando
                // transformar "Piso 1" num artigo.
                bool temCodigoArtigo = item.Length > 0 && PareceCodigoArtigo(item);
                if (temCodigoArtigo || desc.Length > 25)
                {
                    // O artigo manda no que vem A SEGUIR, não no que ficou para
                    // trás. Era isto que estava trocado: as medições da
                    // alvenaria apanhavam o texto do revestimento, porque o
                    // título era colado à medição de cima em vez de abrir o
                    // bloco de baixo.
                    artigo = item + "\u001f" + desc;
                    nTitulos++;
                    continue;
                }

                // ---- subgrupo (alçado / piso) -------------------------------
                // Texto curto e sozinho: é um cabeçalho de grupo, do género
                // "h 2.98 m" ou "Piso 0". Passa a valer para o que vem abaixo.
                piso = desc;
            }

            var relato = new System.Text.StringBuilder();
            relato.Append(nMed + " medições, " + nVaos + " vãos, " + nTitulos + " artigos");
            relato.Append(" (cabeçalho na linha " + mapa.LinhaCabecalho);
            relato.Append(", Qt=" + Coluna(mapa.ColQt) + " Comp=" + Coluna(mapa.ColComp));
            relato.Append(" Larg=" + Coluna(mapa.ColLarg) + " Alt=" + Coluna(mapa.ColAlt) + ")");
            if (orfaos > 0)
                relato.Append("\n     ATENÇÃO: " + orfaos + " deduções ignoradas — " +
                              "apareceram antes da primeira medição da folha.");
            return relato.ToString();
        }

        /// <summary>Letra da coluna, para o relatório sair legível.</summary>
        private static string Coluna(int c)
        {
            if (c <= 0) return "—";
            string s = "";
            while (c > 0) { int r = (c - 1) % 26; s = (char)('A' + r) + s; c = (c - 1) / 26; }
            return s;
        }

        /// <summary>Lê o intervalo usado da folha para memória, de uma vez só.</summary>
        private static bool LerBloco(dynamic ws, out object[,] celulas,
            out int nLinhas, out int nColunas)
        {
            celulas = null; nLinhas = 0; nColunas = 0;
            try
            {
                dynamic usado = ws.UsedRange;

                // Rows.Count é a ALTURA do intervalo, não a última linha. Numa
                // folha que começa a meio — cabeçalho da obra por cima, linhas
                // em branco antes — somar a linha de início é o que evita ler
                // menos do que existe e cortar o fim da folha.
                nLinhas = (int)usado.Row + (int)usado.Rows.Count - 1;
                nColunas = (int)usado.Column + (int)usado.Columns.Count - 1;
                if (nLinhas < 2 || nColunas < 4) return false;

                // Tecto de segurança: uma folha com formatação perdida lá em
                // baixo pode dar um UsedRange de dezenas de milhar de linhas.
                if (nLinhas > 20000) nLinhas = 20000;
                if (nColunas > 30) nColunas = 30;

                celulas = (object[,])ws.Range[
                    ws.Cells[1, 1], ws.Cells[nLinhas, nColunas]].Value2;
                return celulas != null;
            }
            catch { return false; }
        }

        /// <summary>Procura o cabeçalho nas primeiras linhas da folha.</summary>
        private static MapaItem ProcurarCabecalho(object[,] celulas, int nLinhas, int nColunas)
        {
            int limite = Math.Min(nLinhas, 40);
            for (int r = 1; r <= limite; r++)
            {
                int rr = r;
                var m = MapaItem.Detectar(r,
                    c => Normalizar(Texto(celulas, rr, c)), Math.Min(nColunas, 15));
                if (m != null) return m;

                // O modelo clássico não usa os nomes "Item / Designação".
                // Tem Art na coluna A e Descrição na H, com as dimensões
                // fixas em J:M. Sem este fallback, um código editado no modelo
                // antigo podia ser exportado corretamente e depois recusado
                // pelo próprio comando TSKIMPORTAR.
                bool temArt = false, temDescricao = false;
                for (int c = 1; c <= Math.Min(nColunas, 15); c++)
                {
                    string t = Normalizar(Texto(celulas, rr, c));
                    if (t == "art" || t == "artigo") temArt = true;
                    if (t.StartsWith("descri")) temDescricao = true;
                }
                if (temArt && temDescricao)
                {
                    return new MapaItem
                    {
                        LinhaCabecalho = r,
                        ColItem = 1,
                        ColDesc = 8,
                        ColQt = 10,
                        ColComp = 11,
                        ColLarg = 12,
                        ColAlt = 13
                    };
                }
            }
            return null;
        }

        private static string Normalizar(string s)
        {
            return (s ?? "").Trim().ToLowerInvariant().Replace(" ", "");
        }

        private static string Texto(object[,] celulas, int linha, int coluna)
        {
            if (coluna <= 0) return "";
            try
            {
                object v = celulas[linha, coluna];
                return v == null ? "" : Convert.ToString(v, CultureInfo.CurrentCulture).Trim();
            }
            catch { return ""; }
        }

        private static double? Numero(object[,] celulas, int linha, int coluna)
        {
            if (coluna <= 0) return null;
            try
            {
                object v = celulas[linha, coluna];
                if (v == null) return null;
                if (v is double) return (double)v;

                string s = Convert.ToString(v, CultureInfo.CurrentCulture).Trim();
                if (s.Length == 0) return null;

                double d;
                if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out d))
                    return d;
                if (double.TryParse(s.Replace(',', '.'), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out d))
                    return d;
                return null;
            }
            catch { return null; }
        }

        private static bool EhJanela(string designacao)
        {
            string d = (designacao ?? "").ToUpperInvariant();
            return d.StartsWith("V") || d.Contains("JANELA");
        }

        /// <summary>
        /// Reconhece códigos de articulado, incluindo códigos curtos como
        /// "1.0" e "4.9". Não exige uma profundidade fixa: há mapas com
        /// capítulos, artigos e sub-artigos de níveis diferentes.
        /// </summary>
        private static bool PareceCodigoArtigo(string texto)
        {
            string s = (texto ?? "").Trim();
            if (s.Length == 0) return false;

            bool tinhaDigito = false;
            bool esperavaDigito = true;
            foreach (char c in s)
            {
                if (char.IsDigit(c))
                {
                    tinhaDigito = true;
                    esperavaDigito = false;
                    continue;
                }
                if (c == '.' || c == '-' || c == '_')
                {
                    if (esperavaDigito) return false;
                    esperavaDigito = true;
                    continue;
                }
                // Alguns articulados usam um sufixo como 10.1.1.306A.
                if (char.IsLetter(c) && !esperavaDigito)
                {
                    esperavaDigito = false;
                    continue;
                }
                return false;
            }
            return tinhaDigito && !esperavaDigito;
        }

        // ==================================================================
        // Reconstrução no desenho
        // ==================================================================

        /// <summary>
        /// Desenha cada parede como um rectângulo comprimento × espessura,
        /// arrumado numa coluna a partir de <paramref name="origem"/>, e grava
        /// a XData. Devolve quantas ficaram no desenho.
        /// </summary>
        public static int Reconstruir(List<Parede> paredes, Point3d origem)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || paredes.Count == 0) return 0;
            if (!Licenca.PodeMedir()) return 0;
            var db = doc.Database;

            int criadas = 0;
            double y = origem.Y;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, AlvRepo.AppName);
                Util.EnsureLayer(tr, db, LayerImportado, colorIndex: 8);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);
                double alturaTexto = Util.AlturaTexto(db);

                foreach (var p in paredes)
                {
                    double comp = p.Comprimento;
                    double esp = p.Espessura > 0 ? p.Espessura : 0.15;
                    if (comp <= 0) continue;

                    double x0 = origem.X, y0 = y;
                    double x1 = x0 + comp, y1 = y0 + esp;

                    var pl = new Polyline();
                    pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                    pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                    pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                    pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                    pl.Closed = true;
                // Global width: a medição tem de se ver por cima da planta,
                // que já vem cheia de traços finos.
                Util.AplicarEspessura(pl);
                    pl.Layer = LayerImportado;

                    ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    // O comprimento vem da geometria quando se relê a parede,
                    // por isso o rectângulo TEM de ter a medida certa — não é
                    // decoração, é onde o número passa a viver.
                    AlvRepo.GravarXData(pl, p);

                    var label = new MText
                    {
                        Location = new Point3d(x1 + Folga, y0 + esp / 2.0, 0),
                        Contents = string.Format("{0} × {1} m",
                            Util.N2(comp), Util.N2(p.Altura)),
                        TextHeight = alturaTexto,
                        Attachment = AttachmentPoint.MiddleLeft,
                        Layer = LayerImportado
                    };
                    ms.AppendEntity(label);
                    tr.AddNewlyCreatedDBObject(label, true);
                    try { label.Color = Util.CorTexto; } catch { }

                    y = y1 + Folga;
                    criadas++;
                }

                tr.Commit();
            }

            return criadas;
        }
    }

    /// <summary>Comando TSKIMPORTAR.</summary>
    public class ImportarCmd
    {
        [CommandMethod("TSKIMPORTAR")]
        public void Importar()
        {
            Util.Seguro("TSKIMPORTAR", () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                if (!Licenca.PodeMedir()) return;
                var ed = doc.Editor;

                string ficheiro = EscolherFicheiro();
                if (ficheiro == null) return;

                // Espreitar o livro antes de importar: um ficheiro de obra tem
                // muitas vezes uma folha por apartamento, e importar todas
                // juntava medições de sítios diferentes no mesmo desenho.
                var folhas = ImportarExcel.Inventario(ficheiro);
                var comMedicoes = folhas.FindAll(f => f.Medicoes > 0);

                if (comMedicoes.Count == 0)
                {
                    ed.WriteMessage(
                        "\nNenhuma folha deste ficheiro tem medições reconhecíveis. " +
                        "É preciso o cabeçalho 'Designação / Qt / Comp / Largura / Altura'.\n");
                    return;
                }

                string folhaEscolhida = comMedicoes.Count == 1
                    ? comMedicoes[0].Nome
                    : EscolherFolha(comMedicoes);
                if (folhaEscolhida == null) return;

                string diagnostico;
                var paredes = ImportarExcel.Ler(ficheiro, folhaEscolhida, out diagnostico);

                ed.WriteMessage("\nTSK — folhas lidas:\n" + diagnostico + "\n");

                if (paredes.Count == 0)
                {
                    ed.WriteMessage(
                        "\nNão foi encontrada nenhuma medição. A folha tem de ter " +
                        "o cabeçalho 'Designação / Qt / Comp / Largura / Altura'.\n");
                    return;
                }

                int vaos = 0;
                foreach (var p in paredes) vaos += p.Vaos.Count;

                // Importar duas vezes o mesmo ficheiro duplicava tudo em
                // silêncio — e três vezes triplicava. O desenho não tem como
                // saber que aquelas medições já lá estavam, por isso pergunta-se.
                int antigas = ImportarExcel.ContarImportadas(doc.Database);
                if (antigas > 0)
                {
                    var pko = new PromptKeywordOptions(
                        "\nJá existem " + antigas + " entidades de uma importação " +
                        "anterior. Substituir ou acrescentar?");
                    pko.Keywords.Add("Substituir");
                    pko.Keywords.Add("Acrescentar");
                    pko.Keywords.Add("Cancelar");
                    pko.Keywords.Default = "Substituir";
                    pko.AllowNone = true;

                    var pkr = ed.GetKeywords(pko);
                    if (pkr.Status != PromptStatus.OK ||
                        pkr.StringResult == "Cancelar") return;

                    if (pkr.StringResult == "Substituir")
                    {
                        int apagadas = AlvRepo.LimparImportadas(doc.Database);
                        ed.WriteMessage("\nTSK — {0} entidades da importação anterior apagadas.\n",
                                        apagadas);
                    }
                }

                var ppo = new PromptPointOptions(
                    string.Format(
                        "\n{0} medições e {1} vãos lidos. " +
                        "Ponto onde arrumar as medições recuperadas",
                        paredes.Count, vaos));
                var ppr = ed.GetPoint(ppo);
                if (ppr.Status != PromptStatus.OK) return;

                int criadas = ImportarExcel.Reconstruir(paredes, ppr.Value);

                ed.WriteMessage(
                    "\nTSK — {0} medições repostas na layer {1}. " +
                    "As dimensões estão certas; a posição no projeto não, " +
                    "porque o Excel não guarda coordenadas.\n",
                    criadas, ImportarExcel.LayerImportado);

                PaletteHost.RefreshData();
            });
        }

        /// <summary>
        /// Deixa escolher a folha a importar. Mostra o que cada uma tem, para
        /// se distinguir "Medições_2apart" de "Medições_Unico" sem ter de abrir
        /// o Excel ao lado e contar linhas.
        /// </summary>
        private static string EscolherFolha(System.Collections.Generic.List<ImportarExcel.ResumoFolha> folhas)
        {
            using (var frm = new System.Windows.Forms.Form())
            using (var lista = new System.Windows.Forms.ListBox())
            using (var ok = new System.Windows.Forms.Button())
            using (var cancelar = new System.Windows.Forms.Button())
            using (var rot = new System.Windows.Forms.Label())
            {
                frm.Text = "TSK TakeOff — que folha importar?";
                frm.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
                frm.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
                frm.MinimizeBox = false;
                frm.MaximizeBox = false;
                frm.ClientSize = new System.Drawing.Size(560, 300);

                rot.Text = "Este ficheiro tem mais do que uma folha com medições. " +
                           "Escolha uma — importar todas juntaria medições diferentes.";
                rot.Dock = System.Windows.Forms.DockStyle.Top;
                rot.Height = 40;
                rot.Padding = new System.Windows.Forms.Padding(8, 8, 8, 0);

                lista.Dock = System.Windows.Forms.DockStyle.Fill;
                lista.IntegralHeight = false;
                foreach (var f in folhas) lista.Items.Add(f);
                lista.SelectedIndex = 0;
                lista.DoubleClick += (s, e) =>
                {
                    frm.DialogResult = System.Windows.Forms.DialogResult.OK;
                };

                var rodape = new System.Windows.Forms.Panel
                {
                    Dock = System.Windows.Forms.DockStyle.Bottom,
                    Height = 44
                };
                ok.Text = "Importar";
                ok.DialogResult = System.Windows.Forms.DialogResult.OK;
                ok.SetBounds(360, 8, 90, 28);
                cancelar.Text = "Cancelar";
                cancelar.DialogResult = System.Windows.Forms.DialogResult.Cancel;
                cancelar.SetBounds(458, 8, 90, 28);
                rodape.Controls.Add(ok);
                rodape.Controls.Add(cancelar);

                frm.Controls.Add(lista);
                frm.Controls.Add(rodape);
                frm.Controls.Add(rot);
                frm.AcceptButton = ok;
                frm.CancelButton = cancelar;

                // ShowModalDialog e não ShowDialog: sem dono, a janela vai parar
                // atrás do AutoCAD e fica tudo à espera de um clique invisível.
                if (AcadApp.ShowModalDialog(frm) != System.Windows.Forms.DialogResult.OK)
                    return null;
                var escolhida = lista.SelectedItem as ImportarExcel.ResumoFolha;
                return escolhida == null ? null : escolhida.Nome;
            }
        }

        private static string EscolherFicheiro()
        {
            using (var dlg = new System.Windows.Forms.OpenFileDialog())
            {
                dlg.Title = "TSK TakeOff — importar medições de uma folha guardada";
                dlg.Filter = "Folhas de Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|" +
                             "Todos os ficheiros (*.*)|*.*";
                dlg.CheckFileExists = true;
                // ShowDialog e não ShowModalDialog: este é um CommonDialog, não
                // um Form, e o ShowModalDialog do AutoCAD só aceita Forms. Os
                // diálogos comuns do Windows já vêm à frente sozinhos — quem
                // precisa de ajuda é a janela de escolha de folha, que é nossa.
                return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK
                    ? dlg.FileName : null;
            }
        }
    }
}
