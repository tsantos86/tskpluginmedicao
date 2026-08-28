using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TSKTakeOff.MedirSeleccaoCmd))]

namespace TSKTakeOff
{
    /// <summary>
    /// Transforma o que já está desenhado em medições.
    ///
    /// A planta do arquitecto já tem as paredes traçadas e as superfícies
    /// hachuradas. Voltar a desenhá-las por cima é o grosso do tempo de uma
    /// medição — e é trabalho que não acrescenta nada, porque a geometria já
    /// existe e está certa. Aqui selecciona-se e marca-se.
    ///
    /// Não se escreve na geometria do arquitecto: cada objecto seleccionado é
    /// COPIADO para a nossa layer e é a cópia que leva a XData. Assim a
    /// revisão seguinte do projecto pode substituir o desenho dele sem levar
    /// as medições com ela, e apagar uma medição nunca apaga a planta.
    ///
    /// A mesma selecção pode gerar medições em vários serviços de uma vez. É o
    /// que resolve os dois hábitos que aparecem nos projectos: uns têm uma
    /// hachura por camada — alvenaria, reboco, revestimento — e aí escolhe-se
    /// um serviço de cada vez; outros têm uma hachura só para a parede toda, e
    /// aí marcam-se as três camadas e saem três medições da mesma área.
    /// </summary>
    public static class MedirSeleccao
    {
        /// <summary>
        /// Cria as medições. Devolve quantas ficaram feitas.
        /// </summary>
        /// <summary>
        /// Um objecto a medir, com a transformação que o leva ao espaço do
        /// desenho.
        ///
        /// Existe por causa dos XREFs. As entidades de um XREF vivem nas
        /// coordenadas internas do ficheiro referenciado; o que as põe no
        /// sítio é a <c>BlockTransform</c> da inserção. Copiar sem ela fazia a
        /// medição aterrar noutro ponto do desenho — e, no caso comum de o
        /// XREF estar inserido em 0,0 sem rotação nem escala, funcionava por
        /// acidente. Um erro que passa no ensaio e falha no cliente é pior do
        /// que um erro que falha sempre.
        /// </summary>
        internal class Candidato
        {
            public ObjectId Id;
            public Matrix3d Xform = Matrix3d.Identity;
            /// <summary>Nome do XREF, quando veio de um. Só para relatar.</summary>
            public string Origem = "";

            public bool DeXref { get { return Origem.Length > 0; } }

            /// <summary>
            /// Factor de escala da transformação. Os comprimentos lidos dentro
            /// do XREF vêm na escala dele; as larguras dos vãos são números
            /// soltos e não são transformadas por nada — têm de ser
            /// multiplicadas à mão.
            /// </summary>
            public double Escala
            {
                get
                {
                    try
                    {
                        var v = new Vector3d(1, 0, 0).TransformBy(Xform);
                        return v.Length;
                    }
                    catch { return 1.0; }
                }
            }
        }

        /// <summary>Como antes: tudo no espaço do desenho, sem transformação.</summary>
        public static int Criar(IList<ObjectId> ids, IList<string> servicos,
            bool hachuraDeAlcado, bool juntarNumaSo, double espessura)
        {
            var lista = new List<Candidato>();
            if (ids != null)
                foreach (ObjectId id in ids) lista.Add(new Candidato { Id = id });
            return Criar(lista, servicos, hachuraDeAlcado, juntarNumaSo, espessura);
        }

        internal static int Criar(IList<Candidato> ids, IList<string> servicos,
            bool hachuraDeAlcado, bool juntarNumaSo, double espessura)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || ids == null || ids.Count == 0) return 0;
            if (servicos == null || servicos.Count == 0) return 0;
            if (!Licenca.PodeMedir()) return 0;

            var db = doc.Database;
            int feitas = 0;

            // Os vãos confirmados, guardados por objecto.
            //
            // O ciclo de fora é o dos SERVIÇOS: marcar alvenaria, reboco e
            // revestimento faz a mesma geometria ser percorrida três vezes. Sem
            // isto, a mesma parede perguntava os mesmos vãos três vezes
            // seguidas — e a resposta tinha de ser repetida à mão, com o risco
            // de sair diferente numa delas.
            //
            // Pergunta-se uma vez; as camadas seguintes herdam a resposta.
            var vaosPorObjecto = new Dictionary<string, List<Vao>>();

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, AlvRepo.AppName);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                // UMA LAYER PARA TODOS OS SERVIÇOS DESTA PASSAGEM.
                //
                // Era uma layer por serviço, e é isso que enchia o desenho: o
                // diálogo deixa marcar vários de uma vez — o caso normal de uma
                // hachura que é parede e reboco — e saíam tantas layers quantos
                // os serviços marcados, todas ligadas ao mesmo tempo. A layer é
                // agora a do painel, uma só, e o serviço fica a identificar a
                // medição na folha, que é o que ele é.
                string layer = Commands.LayerPrefix + Util.NomeLayer(Config.LayerEfectiva());
                Util.EnsureLayer(tr, db, layer, colorIndex: 1);

                foreach (string servico in servicos)
                {

                    Entity portadora = null;   // a que leva a marca, se for uma só
                    double extra = 0;          // comprimento dos outros troços
                    var todosVaosLarguras = new List<double>();

                    // As cópias por onde passámos, para no fim se poderem medir
                    // os intervalos ENTRE elas. Uma parede desenhada como duas
                    // hachuras separadas tem o vão no meio das duas, e não
                    // dentro de nenhuma.
                    var copiasJuntas = new List<Entity>();
                    bool portadoraViaRect = false;
                    bool portadoraIsHatch = false;

                    foreach (Candidato cand in ids)
                    {
                        var ent = tr.GetObject(cand.Id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        // Numa hachura de planta desenha-se o rectângulo da
                        // parede inteira, não os troços. Os vãos continuam a
                        // sair do desenho, mas o comprimento passa a vir da
                        // marca — como no TSKPAREDERET.
                        var origem = ent as Hatch;
                        bool viaRectangulo = origem != null && !hachuraDeAlcado;

                        // Uma hachura que NÃO corre toda na mesma direcção não
                        // tem rectângulo que a represente.
                        //
                        // Uma escada é o caso: a rampa é diagonal e os patamares
                        // são horizontais. O eixo sai da rampa, que é o
                        // segmento mais longo, e a caixa nesse eixo tem de
                        // esticar para apanhar os patamares — sai um rectângulo
                        // enorme, na diagonal, que cobre meia planta e não
                        // corresponde a medição nenhuma.
                        //
                        // Nesse caso copia-se a hachura como ela é. A medição
                        // passa a valer pela área dela, que é o número certo:
                        // uma escada mede-se pela área, não por um comprimento
                        // vezes uma altura.
                        if (viaRectangulo)
                        {
                            Vector2d uh, vh;
                            AlvRepo.EixoDaHachura(origem, out uh, out vh);
                            if (!AlvRepo.GeometriaCoerenteComEixo(origem, uh))
                            {
                                viaRectangulo = false;
                                PaletteHost.Log(
                                    "hachura na layer " + origem.Layer + ": os " +
                                    "contornos não correm todos na mesma direcção " +
                                    "(escada, parede em L, ou várias peças numa " +
                                    "hachura só). Não desenho rectângulo — a " +
                                    "medição sai pela área da hachura.");
                            }
                        }

                        List<double> larguraVaos = null;
                        if (viaRectangulo) larguraVaos = VaosEntreTrocos(origem);
                        if (larguraVaos != null && larguraVaos.Count > 0)
                        {
                            // Dentro de um XREF à escala 1:2, uma porta de 0,90
                            // é lida como 0,45. A geometria copiada é corrigida
                            // pela transformação; estas larguras são números
                            // soltos e não passam por ela.
                            double k = cand.Escala;
                            if (System.Math.Abs(k - 1.0) > 1e-9)
                                for (int i = 0; i < larguraVaos.Count; i++)
                                    larguraVaos[i] *= k;

                            todosVaosLarguras.AddRange(larguraVaos);
                        }

                        Entity copia = viaRectangulo
                            ? (Entity)RectanguloDaHachura(origem)
                            : Copiar(ent);
                        if (copia == null) continue;

                        // Do espaço do XREF para o espaço do desenho. Antes do
                        // AppendEntity: transformar depois de a entidade estar
                        // na base de dados obriga a reabri-la para escrita sem
                        // ganhar nada.
                        if (cand.DeXref)
                        {
                            try { copia.TransformBy(cand.Xform); }
                            catch (System.Exception ex)
                            {
                                PaletteHost.Log("XREF: não consegui transformar " +
                                    "um objecto de " + cand.Origem + " — " + ex.Message +
                                    ". Foi ignorado, para não o medir no sítio errado.");
                                try { copia.Dispose(); } catch { }
                                continue;
                            }
                        }

                        copia.Layer = layer;
                        ms.AppendEntity(copia);
                        tr.AddNewlyCreatedDBObject(copia, true);

                        if (viaRectangulo)
                        {
                            var rect = copia as Polyline;
                            if (rect != null) rect.Closed = true;
                        }

                        var hatch = copia as Hatch;
                        if (hatch != null)
                        {
                            // Uma hachura copiada continua ligada ao contorno
                            // do original. Se o arquitecto lhe mexer, a nossa
                            // medição mudava sozinha — e ninguém saberia.
                            try { hatch.Associative = false; } catch { }
                            Pintar(hatch, servico);
                        }
                        else if (viaRectangulo)
                        {
                            Util.AplicarEspessura(copia as Polyline);
                            PreencherRectangulo(tr, ms, copia as Polyline, layer, servico);
                        }
                        else
                        {
                            // POLYLINE APANHADA DO PROJECTO.
                            //
                            // Faltava este ramo. O viaRectangulo só é verdade
                            // quando o que se seleccionou era uma HACHURA; quem
                            // seleccionasse uma polyline ficava com a medição
                            // feita e sem preenchimento nenhum — a planta não
                            // mostrava o que já tinha sido medido, que é metade
                            // da razão de o plugin desenhar o que desenha. Era
                            // isto o "alguns comandos não criam a hachura".
                            //
                            // Só as fechadas: uma polyline aberta é uma medição
                            // linear — um rodapé, um remate — e não há área
                            // nenhuma para preencher.
                            var contorno = copia as Polyline;
                            if (contorno != null && contorno.Closed)
                                PreencherRectangulo(tr, ms, contorno, layer, servico);
                        }

                        if (juntarNumaSo)
                        {
                            copiasJuntas.Add(copia);

                            // Todos os troços ficam desenhados — a marca do que
                            // foi medido é a planta inteira — mas só o primeiro
                            // leva a XData. Os outros entram como comprimento
                            // extra dessa medição, e a folha tem uma linha só.
                            if (portadora == null)
                            {
                                portadora = copia;
                                portadoraViaRect = viaRectangulo;
                                portadoraIsHatch = hatch != null;
                            }
                            else
                            {
                                double parcela = ComprimentoDe(copia, hachuraDeAlcado, espessura);
                                extra += parcela;

                                // Cada troço diz o que contribuiu. Sem isto, um
                                // total errado no juntar não tinha como ser
                                // rastreado — era o único caminho sem relato.
                                var plT = copia as Polyline;
                                PaletteHost.Log(string.Format(
                                    "  troço: {0} vért={1} fechada={2} Length={3:N3} "
                                    + "rect={4} -> soma {5:N3}",
                                    copia.GetType().Name,
                                    plT != null ? plT.NumberOfVertices : 0,
                                    plT != null && plT.Closed,
                                    plT != null ? plT.Length : 0,
                                    plT != null && AlvRepo.EhRectangulo(plT),
                                    parcela));
                            }
                            continue;
                        }

                        // A espessura é a que o utilizador escolheu — é ele que
                        // vai ao desenho ver de que camada se trata. Não se lhe
                        // sobrepõe nada: o comprimento já não depende dela.
                        var med = NovaMedicao(servico,
                            hatch != null && hachuraDeAlcado, false, espessura,
                            viaRectangulo);

                        if (viaRectangulo)
                        {
                            // O rectângulo já cobre os vãos, por isso o
                            // comprimento sai dele e NÃO se repõe nada. Os vãos
                            // descontam-se normalmente, como numa parede
                            // medida à mão.
                            //
                            // Só se pergunta quando há mesmo alguma coisa para
                            // confirmar. Uma janela vazia a cada medição é
                            // ruído, e ruído ensina a carregar em OK sem ler —
                            // que é o contrário do que ela serve.
                            if (larguraVaos != null && larguraVaos.Count > 0)
                                med.Vaos.AddRange(VaosConfirmados(
                                    vaosPorObjecto, cand.Id.ToString(),
                                    larguraVaos, med.Altura));
                        }

                        AlvRepo.GravarXData(copia, med);

                        // A etiqueta no meio da medição. É o que permite a
                        // alguém abrir o desenho daqui a seis meses e conferir
                        // de onde veio o número, sem ter de acreditar em nós.
                        Etiquetar(tr, ms, copia, med, layer);

                        // Confirmar já aqui o que a leitura vai devolver. Este
                        // número passou quatro rondas a sair errado sem que
                        // houvesse maneira de ver onde se perdia.
                        var confere = copia as Polyline;
                        if (confere != null)
                        {
                            double l1, l2;
                            AlvRepo.LadosDoRetangulo(confere, out l1, out l2);
                            PaletteHost.Log(string.Format(
                                "medição criada: vértices={0} fechada={1} " +
                                "lados={2:N3}x{3:N3} Length={4:N3} RET={5} " +
                                "-> comprimento {6:N3}",
                                confere.NumberOfVertices, confere.Closed,
                                l1, l2, confere.Length, med.Retangulo,
                                System.Math.Max(l1, l2)));
                        }
                        feitas++;
                    }

                    if (juntarNumaSo && portadora != null)
                    {
                        var med = NovaMedicao(servico,
                            portadoraIsHatch && hachuraDeAlcado,
                            portadoraIsHatch && !hachuraDeAlcado, espessura, portadoraViaRect);
                        med.ComprimentoExtra = extra;

                        // Os intervalos ENTRE os objectos. Quando a parede vem
                        // desenhada como duas hachuras — uma de cada lado da
                        // porta — o buraco fica entre elas, e o VaosEntreTrocos,
                        // que só olha para dentro de cada uma, não o via. A
                        // medição saía com o comprimento dos dois somado e sem
                        // uma pergunta sobre o vão.
                        //
                        // A soma dos comprimentos NÃO inclui o intervalo, por
                        // isso a geometria fica "sem vãos" e cada vão que se
                        // confirmar repõe a sua largura, como já acontece numa
                        // hachura interrompida.
                        if (copiasJuntas.Count > 1)
                        {
                            var entre = VaosEntreObjectos(copiasJuntas);
                            if (entre.Count > 0)
                            {
                                todosVaosLarguras.AddRange(entre);
                                med.GeometriaSemVaos = true;
                            }
                        }

                        if (todosVaosLarguras.Count > 0)
                        {
                            // Chave fixa: no modo juntar há uma medição só para
                            // a selecção toda, portanto há uma resposta só —
                            // partilhada por todos os serviços marcados.
                            med.Vaos.AddRange(VaosConfirmados(
                                vaosPorObjecto, "@juntos",
                                todosVaosLarguras, med.Altura));
                        }
                        AlvRepo.GravarXData(portadora, med);
                        Etiquetar(tr, ms, portadora, med, layer);

                        var plP = portadora as Polyline;
                        double baseP = 0;
                        if (plP != null && AlvRepo.EhRectangulo(plP))
                        {
                            double a1, a2;
                            AlvRepo.LadosDoRetangulo(plP, out a1, out a2);
                            baseP = System.Math.Max(a1, a2);
                        }
                        else if (plP != null) baseP = plP.Length;

                        PaletteHost.Log(string.Format(
                            "juntas numa medição: base {0:N3} + extra {1:N3} = {2:N3} m"
                            + "   (RET={3}, vãos={4})",
                            baseP, extra, baseP + extra,
                            med.Retangulo, med.Vaos.Count));
                        feitas++;
                    }
                }
                tr.Commit();
            }
            return feitas;
        }

        /// <summary>
        /// Hachura de gradiente por cima do rectângulo da medição.
        ///
        /// A ORDEM AQUI NÃO É ARBITRÁRIA — é a mesma que o TentarHatch, em
        /// Commands.cs, documenta como a causa nº1 de hachura que não aparece.
        /// Isto estava a pôr o SetDatabaseDefaults e o Layer ANTES do
        /// AppendEntity, ou seja a mexer numa entidade que ainda não pertencia
        /// a base de dados nenhuma; e como o catch engolia tudo sem uma
        /// palavra, o resultado era medir e não ver preenchimento, sem erro
        /// nem aviso.
        ///
        /// Faltava também o plano do contorno. Com o contorno a uma cota e a
        /// hachura noutra o EvaluateHatch não tem sobre o que trabalhar, e o
        /// preenchimento não aparece — outra vez em silêncio. Basta um UCS
        /// deslocado em Z, ou uma polyline apanhada de um projecto real que
        /// venha à cota do piso.
        /// </summary>
        private static void PreencherRectangulo(Transaction tr, BlockTableRecord ms,
            Polyline contorno, string layer, string servico)
        {
            if (contorno == null) return;
            Hatch h = null;
            try
            {
                h = new Hatch();

                // 1) na base de dados primeiro — tudo o resto depende disto
                ms.AppendEntity(h);
                tr.AddNewlyCreatedDBObject(h, true);

                // 2) valores por omissão do desenho
                h.SetDatabaseDefaults();

                // 3) o plano do CONTORNO, não o do desenho
                h.Normal = contorno.Normal;
                h.Elevation = contorno.Elevation;

                // 4) gradiente da cor do serviço
                Pintar(h, servico);

                // 5-6) o cordão ao contorno fica cortado de propósito: se o
                // arquitecto mexer no original, a medição não muda sozinha.
                h.Associative = false;
                h.AppendLoop(HatchLoopTypes.Default,
                    new ObjectIdCollection { contorno.ObjectId });
                h.EvaluateHatch(true);

                // 7) acessórios — se falharem, o preenchimento fica na mesma
                try { h.Layer = layer; } catch { }
                try
                {
                    h.Transparency = new Autodesk.AutoCAD.Colors.Transparency(
                        (byte)Config.TransparenciaHatch);
                }
                catch { }
            }
            catch (System.Exception ex)
            {
                // A medição vale na mesma sem preenchimento — mas falhar calado
                // é o que fez isto passar despercebido durante uma versão
                // inteira. Quem mede tem de saber que a hachura não saiu.
                PaletteHost.Log("Preenchimento da medição falhou [" +
                                ex.GetType().Name + "]: " + ex.Message);
                try { if (h != null && !h.IsErased) h.Erase(); } catch { }
            }
        }

        /// <summary>
        /// Um rectângulo que cobre a parede toda, a partir da caixa da hachura.
        ///
        /// A hachura do arquitecto interrompe-se nas portas; a medição, essa, é
        /// a parede inteira. Marcar só os troços deixava a planta com buracos
        /// onde a medição existe. Aqui desenha-se o rectângulo completo, como
        /// faz o TSKPAREDERET — e o comprimento passa a vir da própria marca,
        /// sem precisar de repor os vãos.
        /// </summary>
        private static Polyline RectanguloDaHachura(Hatch h)
        {
            try
            {
                // O rectângulo é construído no EIXO DA PAREDE, não em X/Y.
                // Com a caixa alinhada com os eixos, uma parede rodada dava um
                // rectângulo na diagonal que não cobria a parede nenhuma —
                // era a única das três falhas de rotação que se via a olho.
                Autodesk.AutoCAD.Geometry.Vector2d u, v;
                AlvRepo.EixoDaHachura(h, out u, out v);

                double a0 = double.MaxValue, b0 = double.MaxValue;
                double a1 = double.MinValue, b1 = double.MinValue;

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
                            foreach (var p in new[] { c.EvaluatePoint(iv.LowerBound),
                                                      c.EvaluatePoint(iv.UpperBound) })
                            {
                                double pa = p.X * u.X + p.Y * u.Y;
                                double pb = p.X * v.X + p.Y * v.Y;
                                if (pa < a0) a0 = pa; if (pa > a1) a1 = pa;
                                if (pb < b0) b0 = pb; if (pb > b1) b1 = pb;
                            }
                        }
                        catch { }
                    }
                }

                if (a0 > a1 || b0 > b1) return null;
                if (a1 - a0 <= 0 || b1 - b0 <= 0) return null;

                // De volta ao mundo: cada canto é a combinação das duas
                // projecções ao longo dos respectivos versores.
                var pl = new Polyline();
                pl.AddVertexAt(0, Canto(u, v, a0, b0), 0, 0, 0);
                pl.AddVertexAt(1, Canto(u, v, a1, b0), 0, 0, 0);
                pl.AddVertexAt(2, Canto(u, v, a1, b1), 0, 0, 0);
                pl.AddVertexAt(3, Canto(u, v, a0, b1), 0, 0, 0);

                // O PLANO da hachura, não o do mundo.
                //
                // As curvas dos contornos são 2D no sistema próprio da
                // hachura, definido pelo seu Normal e Elevation — não em WCS.
                // Numa hachura desenhada com o UCS rodado ou com elevação, o
                // Normal deixa de ser Z, e um rectângulo construído com as
                // mesmas coordenadas mas no plano do mundo aterra noutro
                // sítio, ou noutra inclinação. Herdar o plano faz as
                // coordenadas serem lidas onde foram escritas.
                //
                // Na esmagadora maioria dos desenhos o Normal é Z e isto não
                // faz diferença nenhuma — que é justamente porque nunca se deu
                // por ele faltar.
                try
                {
                    pl.Normal = h.Normal;
                    pl.Elevation = h.Elevation;
                }
                catch { }
                // O Closed fica para DEPOIS do AppendEntity: definido antes de
                // a entidade entrar na base de dados nem sempre pega, e uma
                // polyline aberta faz o pl.Length devolver o perímetro em vez
                // do lado — que é exactamente o número errado que saía.
                return pl;
            }
            catch { return null; }
        }

        /// <summary>
        /// Transforma a selecção do utilizador na lista do que há mesmo a
        /// medir, entrando dentro dos XREFs.
        ///
        /// O <c>GetSelection</c> do AutoCAD não alcança entidades aninhadas:
        /// clicar numa parede dentro de um XREF selecciona a INSERÇÃO, não a
        /// parede. Com o filtro a aceitar só polylines, linhas e hachuras, o
        /// comando dizia "0 encontrados" e parecia avariado.
        ///
        /// Aqui aceita-se a inserção e desce-se lá dentro: recolhem-se as
        /// entidades mensuráveis, cada uma com a transformação composta que a
        /// põe no espaço do desenho.
        ///
        /// Só as layers que o utilizador escolher. Um XREF de arquitectura tem
        /// milhares de objectos e medi-los todos não é o que ninguém quer —
        /// mas medir tudo o que está numa layer é exactamente o que se quer.
        /// </summary>
        internal static List<Candidato> ExpandirSeleccao(Transaction tr,
            IList<ObjectId> seleccionados, out int quantosXrefs)
        {
            var saida = new List<Candidato>();
            quantosXrefs = 0;
            if (seleccionados == null) return saida;

            var inseridos = new List<BlockReference>();

            foreach (ObjectId id in seleccionados)
            {
                var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null) continue;

                var br = ent as BlockReference;
                if (br == null)
                {
                    saida.Add(new Candidato { Id = id });
                    continue;
                }
                inseridos.Add(br);
            }

            if (inseridos.Count == 0) return saida;
            quantosXrefs = inseridos.Count;

            // Que layers existem lá dentro, e com que contagem. Sem os números
            // a escolha era às cegas: um XREF tem dezenas de layers e o nome
            // nem sempre diz o que lá está.
            var contagem = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var br in inseridos)
                ContarPorLayer(tr, br, Matrix3d.Identity, contagem, 0);

            if (contagem.Count == 0)
            {
                PaletteHost.Log("XREF: não encontrei polylines, linhas nem " +
                                "hachuras lá dentro (ou o XREF não está carregado).");
                return saida;
            }

            var escolhidas = EscolherLayersDoXref(contagem);
            if (escolhidas == null || escolhidas.Count == 0) return saida;

            foreach (var br in inseridos)
                RecolherDoXref(tr, br, Matrix3d.Identity, escolhidas, saida, 0, NomeDe(tr, br));

            return saida;
        }

        private static string NomeDe(Transaction tr, BlockReference br)
        {
            try
            {
                var def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead)
                          as BlockTableRecord;
                return def == null ? "XREF" : def.Name;
            }
            catch { return "XREF"; }
        }

        /// <summary>É uma entidade que sabemos medir?</summary>
        private static bool Mensuravel(Entity e)
        {
            return e is Polyline || e is Line || e is Hatch;
        }

        private static void ContarPorLayer(Transaction tr, BlockReference br,
            Matrix3d acumulada, Dictionary<string, int> contagem, int profundidade)
        {
            if (profundidade > 3) return;   // XREFs dentro de XREFs, com fundo
            BlockTableRecord def;
            try
            {
                def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            }
            catch { return; }
            if (def == null || def.IsLayout) return;
            if (def.IsFromExternalReference && !def.IsResolved) return;

            foreach (ObjectId id in def)
            {
                Entity e;
                try { e = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                catch { continue; }
                if (e == null) continue;

                var dentro = e as BlockReference;
                if (dentro != null)
                {
                    ContarPorLayer(tr, dentro, acumulada, contagem, profundidade + 1);
                    continue;
                }
                if (!Mensuravel(e)) continue;

                string l = e.Layer ?? "";
                if (!contagem.ContainsKey(l)) contagem[l] = 0;
                contagem[l]++;
            }
        }

        private static void RecolherDoXref(Transaction tr, BlockReference br,
            Matrix3d acumulada, HashSet<string> layers, List<Candidato> saida,
            int profundidade, string origem)
        {
            if (profundidade > 3) return;
            BlockTableRecord def;
            try
            {
                def = tr.GetObject(br.BlockTableRecord, OpenMode.ForRead) as BlockTableRecord;
            }
            catch { return; }
            if (def == null || def.IsLayout) return;
            if (def.IsFromExternalReference && !def.IsResolved) return;

            // A ordem importa: primeiro a do bloco, depois a acumulada de fora.
            Matrix3d x = acumulada * br.BlockTransform;

            foreach (ObjectId id in def)
            {
                Entity e;
                try { e = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                catch { continue; }
                if (e == null) continue;

                var dentro = e as BlockReference;
                if (dentro != null)
                {
                    RecolherDoXref(tr, dentro, x, layers, saida, profundidade + 1, origem);
                    continue;
                }
                if (!Mensuravel(e)) continue;
                if (!layers.Contains(e.Layer ?? "")) continue;

                saida.Add(new Candidato { Id = id, Xform = x, Origem = origem });
            }
        }

        /// <summary>Que layers do XREF medir.</summary>
        private static HashSet<string> EscolherLayersDoXref(Dictionary<string, int> contagem)
        {
            var nomes = new List<string>(contagem.Keys);
            nomes.Sort(System.StringComparer.OrdinalIgnoreCase);

            using (var frm = new Form())
            using (var lista = new CheckedListBox())
            using (var ok = new Button())
            using (var cancelar = new Button())
            using (var rot = new Label())
            {
                frm.Text = "TSK TakeOff — medir dentro do XREF";
                frm.StartPosition = FormStartPosition.CenterScreen;
                frm.FormBorderStyle = FormBorderStyle.Sizable;
                frm.MinimizeBox = false;
                frm.MaximizeBox = false;
                frm.ClientSize = new System.Drawing.Size(520, 440);

                rot.Text = "O que seleccionou é um XREF. As paredes estão lá " +
                           "dentro e o AutoCAD não as deixa apanhar directamente.\n" +
                           "Escolha as layers a medir — sai uma medição por objecto, " +
                           "já colocado no sítio certo do seu desenho.";
                rot.Dock = DockStyle.Top;
                rot.Height = 56;
                rot.Padding = new Padding(10, 8, 10, 0);

                lista.Dock = DockStyle.Fill;
                lista.CheckOnClick = true;
                foreach (string n in nomes)
                    lista.Items.Add(n + "   (" + contagem[n] + " objectos)", false);

                var baixo = new Panel { Dock = DockStyle.Bottom, Height = 46 };
                ok.Text = "Medir";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(320, 9, 90, 28);
                cancelar.Text = "Cancelar";
                cancelar.DialogResult = DialogResult.Cancel;
                cancelar.SetBounds(418, 9, 90, 28);
                baixo.Controls.Add(ok);
                baixo.Controls.Add(cancelar);

                frm.Controls.Add(lista);
                frm.Controls.Add(baixo);
                frm.Controls.Add(rot);
                frm.AcceptButton = ok;
                frm.CancelButton = cancelar;

                if (AcadApp.ShowModalDialog(frm) != DialogResult.OK) return null;

                var r = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                foreach (int i in lista.CheckedIndices) r.Add(nomes[i]);
                return r;
            }
        }

        /// <summary>
        /// Pergunta os vãos deste objecto uma vez só, e devolve a mesma
        /// resposta às camadas seguintes.
        ///
        /// Devolve sempre cópias: cada medição fica com os seus próprios
        /// objectos <see cref="Vao"/>. Partilhar as instâncias entre a
        /// alvenaria e o reboco faria uma alteração num deles mexer no outro
        /// sem ninguém pedir.
        /// </summary>
        private static List<Vao> VaosConfirmados(Dictionary<string, List<Vao>> cache,
            string chave, List<double> larguras, double altura)
        {
            if (larguras == null || larguras.Count == 0) return new List<Vao>();

            List<Vao> guardados;
            if (!cache.TryGetValue(chave, out guardados))
            {
                guardados = VaosDetectados.Confirmar(larguras, altura)
                            ?? new List<Vao>();
                cache[chave] = guardados;
            }

            var copias = new List<Vao>();
            foreach (var v in guardados)
                copias.Add(new Vao
                {
                    Designacao = v.Designacao,
                    Largura = v.Largura,
                    Altura = v.Altura,
                    Quantidade = v.Quantidade,
                    PreAro = v.PreAro,
                    Espessura = v.Espessura,
                    Tipo = v.Tipo
                });
            return copias;
        }

        /// <summary>
        /// Os intervalos ENTRE objectos seleccionados.
        ///
        /// O <see cref="VaosEntreTrocos"/> olha para os contornos DE UMA
        /// hachura. Quando o arquitecto desenha a parede como duas hachuras
        /// separadas — uma de cada lado da porta — o buraco fica ENTRE os dois
        /// objectos, e ninguém olhava para lá. Juntavam-se numa medição só,
        /// somava-se o comprimento dos dois, e o vão desaparecia sem uma
        /// pergunta.
        ///
        /// Ficou mais visível desde que o "juntar" passou a estar marcado por
        /// omissão: é agora o caminho normal.
        ///
        /// A guarda da perpendicular não é um pormenor. Sem ela, duas paredes
        /// PARALELAS — as duas faces de um corredor, por exemplo — apareciam
        /// como uma parede com um vão do tamanho do corredor. Só entram os
        /// objectos que estão na mesma linha, ou seja, cuja extensão
        /// atravessada se sobrepõe à do primeiro.
        /// </summary>
        internal static List<double> VaosEntreObjectos(List<Entity> objectos)
        {
            var larguras = new List<double>();
            if (objectos == null || objectos.Count < 2) return larguras;

            // O eixo comum sai do segmento recto mais longo de TODOS eles.
            Vector2d u = new Vector2d(1, 0);
            double melhor = 0;
            foreach (var e in objectos)
                foreach (var s in Segmentos(e))
                {
                    double d = s.Length;
                    if (d > melhor && d > 1e-9) { melhor = d; u = s / d; }
                }
            var v = new Vector2d(-u.Y, u.X);

            // Caixa de cada objecto no eixo: {aoLongo0, aoLongo1, atrav0, atrav1}
            var caixas = new List<double[]>();
            foreach (var e in objectos)
            {
                double a0 = double.MaxValue, a1 = double.MinValue;
                double b0 = double.MaxValue, b1 = double.MinValue;
                foreach (var p in Pontos(e))
                {
                    double pa = p.X * u.X + p.Y * u.Y;
                    double pb = p.X * v.X + p.Y * v.Y;
                    if (pa < a0) a0 = pa; if (pa > a1) a1 = pa;
                    if (pb < b0) b0 = pb; if (pb > b1) b1 = pb;
                }
                if (a0 <= a1) caixas.Add(new[] { a0, a1, b0, b1 });
            }
            if (caixas.Count < 2) return larguras;

            // Só os que estão na mesma linha de parede.
            double r0 = caixas[0][2], r1 = caixas[0][3];
            var mesma = new List<double[]>();
            foreach (var c in caixas)
                if (!(c[3] < r0 - 1e-9 || c[2] > r1 + 1e-9)) mesma.Add(c);
            if (mesma.Count < 2) return larguras;

            mesma.Sort((x, y) => x[0].CompareTo(y[0]));

            // Fundir os que se sobrepõem, e só depois medir as folgas.
            var fundidos = new List<double[]>();
            fundidos.Add(new[] { mesma[0][0], mesma[0][1] });
            for (int i = 1; i < mesma.Count; i++)
            {
                var ultimo = fundidos[fundidos.Count - 1];
                if (mesma[i][0] <= ultimo[1])
                    ultimo[1] = System.Math.Max(ultimo[1], mesma[i][1]);
                else
                    fundidos.Add(new[] { mesma[i][0], mesma[i][1] });
            }

            for (int i = 1; i < fundidos.Count; i++)
            {
                double folga = fundidos[i][0] - fundidos[i - 1][1];
                if (folga > 0.30 && folga < 4.00) larguras.Add(folga);
            }

            if (larguras.Count > 0)
                PaletteHost.Log("vãos: " + larguras.Count +
                                " intervalo(s) encontrado(s) ENTRE os objectos " +
                                "seleccionados (não dentro deles).");
            return larguras;
        }

        /// <summary>Os pontos que definem esta entidade, no plano dela.</summary>
        private static List<Autodesk.AutoCAD.Geometry.Point2d> Pontos(Entity e)
        {
            var pts = new List<Autodesk.AutoCAD.Geometry.Point2d>();

            var h = e as Hatch;
            if (h != null)
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
                            pts.Add(c.EvaluatePoint(iv.LowerBound));
                            pts.Add(c.EvaluatePoint(iv.UpperBound));
                        }
                        catch { }
                    }
                }
                return pts;
            }

            var pl = e as Polyline;
            if (pl != null)
            {
                try
                {
                    for (int i = 0; i < pl.NumberOfVertices; i++)
                        pts.Add(pl.GetPoint2dAt(i));
                }
                catch { }
            }
            return pts;
        }

        /// <summary>Os segmentos rectos desta entidade, como vectores.</summary>
        private static List<Vector2d> Segmentos(Entity e)
        {
            var segs = new List<Vector2d>();
            var pts = Pontos(e);

            var h = e as Hatch;
            if (h != null)
            {
                // Nas hachuras os pontos vêm aos pares (início, fim) por curva.
                for (int i = 0; i + 1 < pts.Count; i += 2)
                    segs.Add(pts[i + 1] - pts[i]);
                return segs;
            }

            for (int i = 0; i + 1 < pts.Count; i++)
                segs.Add(pts[i + 1] - pts[i]);
            if (pts.Count > 2) segs.Add(pts[0] - pts[pts.Count - 1]);
            return segs;
        }

        /// <summary>
        /// Um canto do rectângulo, a partir das duas projecções.
        /// P = a·u + b·v — a conta inversa da projecção.
        /// </summary>
        private static Autodesk.AutoCAD.Geometry.Point2d Canto(
            Autodesk.AutoCAD.Geometry.Vector2d u,
            Autodesk.AutoCAD.Geometry.Vector2d v, double a, double b)
        {
            return new Autodesk.AutoCAD.Geometry.Point2d(
                a * u.X + b * v.X,
                a * u.Y + b * v.Y);
        }

        /// <summary>
        /// Dá à cópia o aspecto das medições: gradiente com a cor do piso,
        /// igual ao que o TSKPAREDE já faz. Sem isto a cópia ficava com o
        /// padrão do arquitecto e não se via o que já estava medido.
        /// </summary>
        private static void Pintar(Hatch h, string servico)
        {
            try
            {
                // A cor é do SERVIÇO. Assim todas as paredes de um artigo saem
                // iguais, e distinguem-se do reboco e do revestimento que lhes
                // ficam por cima na mesma planta.
                var cor = FachadaConfig.CorDoServico(servico);
                var acad = Autodesk.AutoCAD.Colors.Color.FromRgb(cor.R, cor.G, cor.B);
                var clara = Autodesk.AutoCAD.Colors.Color.FromRgb(
                    (byte)System.Math.Min(255, cor.R + 70),
                    (byte)System.Math.Min(255, cor.G + 70),
                    (byte)System.Math.Min(255, cor.B + 70));

                // Sem isto o SetGradient não pega: a hachura tem de ser
                // declarada como objecto de gradiente antes de o receber.
                h.HatchObjectType = HatchObjectType.GradientObject;
                h.SetGradient(GradientPatternType.PreDefinedGradient, "LINEAR");
                try { h.GradientOneColorMode = false; } catch { }

                // As paradas do gradiente são float, não double: 0f e 1f.
                h.SetGradientColors(new[]
                {
                    new GradientColor(clara, 0f),
                    new GradientColor(acad, 1f)
                });
                h.GradientAngle = 0.0;
                h.GradientShift = 0f;

                // Translúcido, para se continuar a ver a planta por baixo.
                // A Transparency vive no namespace das cores, não no das
                // entidades — daí o nome completo.
                try
                {
                    h.Transparency = new Autodesk.AutoCAD.Colors.Transparency(
                        (byte)Config.TransparenciaHatch);
                }
                catch { }
            }
            catch { /* sem gradiente a medição vale na mesma */ }
        }

        /// <summary>
        /// As aberturas de uma parede, medidas nos intervalos entre os troços
        /// da hachura.
        ///
        /// Não é preciso procurar a porta: a hachura pára nela, portanto o
        /// buraco entre dois troços É a abertura e a sua largura é exacta. O
        /// que a geometria não diz é a altura — essa vem do painel e afina-se
        /// na grelha, ou virá do mapa de vãos quando o importarmos.
        /// </summary>
        internal static List<double> VaosEntreTrocos(Hatch h)
        {
            var larguras = new List<double>();
            if (h == null) return larguras;

            // Cada troço: posição e extensão ao longo da parede.
            var trocos = new List<double[]>();   // {inicio, fim}

            // A parede corre no eixo da sua própria geometria, não no eixo
            // mais longo da caixa. Era aqui que o erro passava despercebido:
            // numa parede a 45° a projecção em X e em Y são iguais e uma
            // porta de 0,90 saía como 0,53 — um número plausível, e errado.
            Autodesk.AutoCAD.Geometry.Vector2d u, vPerp;
            AlvRepo.EixoDaHachura(h, out u, out vPerp);

            // Duas paredes a ângulos diferentes na mesma hachura, ou uma
            // parede em L, não têm eixo nenhum. Projectar sobre o eixo de
            // metade delas dava vãos inventados — mais vale não devolver nada
            // e dizer porquê.
            if (!AlvRepo.GeometriaCoerenteComEixo(h, u))
            {
                PaletteHost.Log("vãos: os contornos desta hachura não correm " +
                                "todos na mesma direcção (parede em L, ou várias " +
                                "paredes na mesma hachura). Não arrisco adivinhar " +
                                "as larguras — acrescente os vãos à mão.");
                return larguras;
            }

            try
            {
                for (int i = 0; i < h.NumberOfLoops; i++)
                {
                    HatchLoop laco;
                    try { laco = h.GetLoopAt(i); } catch { continue; }
                    if (laco == null || laco.Curves == null) continue;

                    double a = double.MaxValue, b = double.MinValue;
                    foreach (Autodesk.AutoCAD.Geometry.Curve2d c in laco.Curves)
                    {
                        try
                        {
                            var iv = c.GetInterval();
                            foreach (var pt in new[] { c.EvaluatePoint(iv.LowerBound),
                                                       c.EvaluatePoint(iv.UpperBound) })
                            {
                                double proj = pt.X * u.X + pt.Y * u.Y;
                                if (proj < a) a = proj;
                                if (proj > b) b = proj;
                            }
                        }
                        catch { }
                    }
                    if (a <= b) trocos.Add(new[] { a, b });
                }
            }
            catch { return larguras; }

            if (trocos.Count < 2)
            {
                // Um contorno só: a hachura corre de ponta a ponta sem
                // interrupção nem ilhas. É o desenho em que os pilares e as
                // portas estão por CIMA da hachura, como objectos à parte —
                // não há nada na hachura que os denuncie.
                //
                // Dizer isto é melhor do que devolver nada em silêncio: assim
                // sabe-se que é preciso acrescentar os vãos à mão, em vez de
                // ficar à espera de uma detecção que nunca vem.
                PaletteHost.Log("vãos: esta hachura tem um contorno só, sem " +
                                "interrupções nem ilhas — não há como ler os vãos " +
                                "da geometria dela. Acrescente-os no painel, ou " +
                                "use o TSKVAO para os apanhar dos textos e blocos.");
                return larguras;
            }

            trocos.Sort((x, y) => x[0].CompareTo(y[0]));

            // ----------------------------------------------------------------
            // Há duas maneiras de um arquitecto desenhar a mesma parede, e o
            // código só sabia ler uma delas.
            //
            //   A) hachura INTERROMPIDA — um contorno por troço, buracos entre
            //      eles. Os vãos são as folgas.
            //
            //   B) contorno EXTERIOR + ILHAS — um contorno à volta da parede
            //      toda e as portas como buracos lá dentro. Os vãos são as
            //      próprias ilhas.
            //
            // Na topologia B o contorno exterior fica em primeiro na ordenação
            // e o seu fim é o fim da parede: todas as folgas seguintes davam
            // negativo e eram rejeitadas. Com um vão às vezes ainda calhava;
            // com dois nunca — e com dois devolvia a distância ENTRE as portas
            // como se fosse uma porta. Um número plausível e errado.
            // ----------------------------------------------------------------
            double inicio = trocos[0][0], fim = trocos[0][1];
            foreach (var t in trocos)
            {
                if (t[0] < inicio) inicio = t[0];
                if (t[1] > fim) fim = t[1];
            }
            double vao = fim - inicio;
            if (vao <= 0) return larguras;

            var exteriores = new List<double[]>();
            var interiores = new List<double[]>();
            foreach (var t in trocos)
            {
                if ((t[1] - t[0]) >= 0.95 * vao) exteriores.Add(t);
                else interiores.Add(t);
            }

            if (exteriores.Count > 0 && interiores.Count > 0)
            {
                // Topologia B: as ilhas SÃO as aberturas.
                foreach (var t in interiores)
                {
                    double w = t[1] - t[0];
                    if (w > 0.30 && w < 4.00) larguras.Add(w);
                }
                PaletteHost.Log("vãos: hachura com contorno exterior e " +
                                interiores.Count + " ilha(s); " + larguras.Count +
                                " aberturas lidas das ilhas.");
                return larguras;
            }

            // Topologia A: os vãos são as folgas — mas primeiro FUNDIR os
            // intervalos que se sobrepõem. Nos encontros e nos umbrais os
            // troços tocam-se e sobrepõem-se uns aos outros; sem fundir, o
            // troço anterior tapava o seguinte e a folga saía negativa.
            var fundidos = new List<double[]>();
            fundidos.Add(new[] { trocos[0][0], trocos[0][1] });
            for (int i = 1; i < trocos.Count; i++)
            {
                var ultimo = fundidos[fundidos.Count - 1];
                if (trocos[i][0] <= ultimo[1])
                    ultimo[1] = System.Math.Max(ultimo[1], trocos[i][1]);
                else
                    fundidos.Add(new[] { trocos[i][0], trocos[i][1] });
            }

            for (int i = 1; i < fundidos.Count; i++)
            {
                double folga = fundidos[i][0] - fundidos[i - 1][1];

                // Folgas minúsculas são junções entre troços, não portas.
                // Folgas enormes são paredes diferentes na mesma hachura.
                if (folga > 0.30 && folga < 4.00) larguras.Add(folga);
            }

            if (trocos.Count != fundidos.Count)
                PaletteHost.Log("vãos: " + trocos.Count + " contornos fundidos em " +
                                fundidos.Count + " troços (sobrepunham-se); " +
                                larguras.Count + " aberturas.");
            return larguras;
        }

        /// <summary>
        /// A espessura de uma hachura em faixa, tirada da própria geometria.
        /// Devolve 0 quando não é possível — aí vale a espessura do diálogo.
        ///
        /// Numa faixa rectangular, área e perímetro chegam para achar os dois
        /// lados: L + e = P/2 e L × e = A, logo
        ///     e = (P/2 − √((P/2)² − 4A)) / 2
        ///
        /// Nem área nem perímetro dependem da orientação, por isso funciona
        /// com a parede rodada — ao contrário da bounding box. É isto que
        /// evita ter de acertar a espessura à mão para cada camada: uma forra
        /// de 2,5 cm e uma alvenaria de 15 cm na mesma selecção saem ambas
        /// certas.
        /// </summary>
        internal static double EspessuraDaHachura(Hatch h)
        {
            if (h == null) return 0;
            try
            {
                double area = System.Math.Abs(h.Area);
                double perimetro = PerimetroDe(h);
                if (area <= 0 || perimetro <= 0) return 0;

                double meio = perimetro / 2.0;
                double dentro = meio * meio - 4.0 * area;
                if (dentro < 0) return 0;                    // não é uma faixa

                double e = (meio - System.Math.Sqrt(dentro)) / 2.0;

                // Uma "espessura" maior do que o comprimento não é espessura
                // nenhuma: a forma não é uma faixa e não se adivinha.
                if (e <= 0 || e * e >= area) return 0;
                return e;
            }
            catch { return 0; }
        }

        /// <summary>Perímetro dos contornos da hachura, quando são polylines.</summary>
        internal static double PerimetroDe(Hatch h)
        {
            double total = 0;
            for (int i = 0; i < h.NumberOfLoops; i++)
            {
                HatchLoop laco;
                try { laco = h.GetLoopAt(i); } catch { continue; }
                if (laco == null) continue;

                if (laco.Polyline != null)
                {
                    var v = laco.Polyline;
                    for (int k = 0; k < v.Count; k++)
                    {
                        var a = v[k].Vertex;
                        var b = v[(k + 1) % v.Count].Vertex;
                        total += a.GetDistanceTo(b);
                    }
                    continue;
                }

                // Contornos feitos de curvas soltas — linhas e arcos — que é
                // o que aparece na prática. Era isto que faltava ler: sem
                // eles o perímetro dava zero e a espessura nunca era detectada.
                if (laco.Curves == null) continue;
                foreach (Autodesk.AutoCAD.Geometry.Curve2d c in laco.Curves)
                {
                    try
                    {
                        var iv = c.GetInterval();
                        total += c.GetLength(iv.LowerBound, iv.UpperBound);
                    }
                    catch { }
                }
            }
            return total;
        }

        /// <summary>
        /// O que este troço vale em comprimento, para se somar ao da medição
        /// que leva a marca. Numa hachura de planta é área ÷ espessura, na de
        /// alçado é a própria área, e numa polyline é o desenvolvimento.
        /// </summary>
        private static double ComprimentoDe(Entity ent, bool alcado, double espessura)
        {
            var hatch = ent as Hatch;
            if (hatch != null)
            {
                double area = 0;
                try { area = System.Math.Abs(hatch.Area); } catch { }
                if (alcado) return area;

                double comp, esp;
                if (AlvRepo.MedidasDaHachura(hatch, out comp, out esp)) return comp;
                return espessura > 0 ? area / espessura : 0;
            }
            var pl = ent as Polyline;
            if (pl != null)
            {
                if (AlvRepo.EhRectangulo(pl))
                {
                    double l1, l2;
                    AlvRepo.LadosDoRetangulo(pl, out l1, out l2);
                    return System.Math.Max(l1, l2);
                }
                return pl.Length;
            }
            return 0;
        }

        /// <summary>
        /// Escreve, no centro da medição, o que ela vale. Igual ao que os
        /// comandos manuais já fazem — é a prova visível do número.
        /// </summary>
        private static void Etiquetar(Transaction tr, BlockTableRecord ms,
            Entity ent, Parede med, string layer)
        {
            try
            {
                var ext = ent.GeometricExtents;
                var centro = new Autodesk.AutoCAD.Geometry.Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0, 0);

                // O comprimento tem de ser lido da mesma maneira que a grelha e
                // o Excel o lêem, senão a etiqueta no desenho diz um número e a
                // folha diz outro — e a etiqueta existe justamente para se
                // poder conferir. Estava a usar a área da hachura e a deixar o
                // comprimento a zero: saía "0,00 × 2,80 = 0,09".
                double area, comp = 0;
                var hatch = ent as Hatch;
                if (hatch != null)
                {
                    if (med.SoArea)
                    {
                        area = System.Math.Abs(hatch.Area) + med.ComprimentoExtra;
                    }
                    else
                    {
                        double c, e;
                        if (AlvRepo.MedidasDaHachura(hatch, out c, out e)) comp = c;

                        // A parede inteira: os troços mais os vãos que ficaram
                        // entre eles, como na folha.
                        if (med.GeometriaSemVaos) comp += med.LarguraDosVaos;
                        comp += med.ComprimentoExtra;
                        area = comp * med.Altura;
                    }
                }
                else
                {
                    var pl = ent as Polyline;
                    if (pl == null) { area = 0; }
                    else if (med.Retangulo)
                    {
                        // Num rectângulo, pl.Length é o PERÍMETRO — dava
                        // 6,93 numa parede de 3,40. O comprimento é o lado
                        // maior, lido como o repositório o lê.
                        double l1, l2;
                        AlvRepo.LadosDoRetangulo(pl, out l1, out l2);
                        comp = System.Math.Max(l1, l2);
                    }
                    else comp = pl.Length;

                    if (med.SoArea)
                    {
                        area = comp + med.ComprimentoExtra;
                    }
                    else
                    {
                        comp += med.ComprimentoExtra;
                        area = comp * med.Altura;
                    }
                }

                // A etiqueta tem de dizer o MESMO número que a folha, senão não
                // serve para conferir — que é a única razão de ela existir.
                // Com vãos, o que conta é a área líquida; mostra-se a bruta por
                // cima para se ver de onde veio.
                double desconto = med.DescontoVaos(Config.Regra);
                string texto;
                if (med.SoArea)
                {
                    texto = med.Servico + "\\P" + Util.N2(area) + " m²";
                }
                else if (desconto > 0)
                {
                    texto = med.Servico + "\\P" +
                            Util.N2(comp) + " × " + Util.N2(med.Altura) +
                            " = " + Util.N2(area) + "\\P− vãos " + Util.N2(desconto) +
                            "\\P= " + Util.N2(area - desconto) + " m²";
                }
                else
                {
                    texto = med.Servico + "\\P" + Util.N2(comp) + " × " +
                            Util.N2(med.Altura) + " = " + Util.N2(area) + " m²";
                }

                var rot = new MText
                {
                    Location = centro,
                    Contents = texto,
                    TextHeight = Util.AlturaTexto(ms.Database),
                    Attachment = AttachmentPoint.MiddleCenter,
                    Layer = layer,

                    // A etiqueta lê-se SEMPRE na horizontal, seja qual for a
                    // inclinação da parede. Uma escada a 30° com o texto a
                    // acompanhá-la obriga a torcer o pescoço, e numa planta
                    // com dezenas de medições isso conta.
                    //
                    // Posto explicitamente e não deixado ao valor por omissão:
                    // o que herda o plano da entidade é o rectângulo, e não há
                    // razão para a etiqueta seguir o mesmo caminho por
                    // distracção de quem editar isto a seguir.
                    Rotation = 0.0,
                    Normal = Vector3d.ZAxis
                };
                ms.AppendEntity(rot);
                tr.AddNewlyCreatedDBObject(rot, true);
                try { rot.Color = Util.CorTexto; } catch { }
            }
            catch { /* sem extents utilizáveis: fica sem etiqueta, medição vale na mesma */ }
        }

        /// <summary>
        /// Cópia própria da entidade. As linhas passam a polyline de dois
        /// vértices: assim há um só tipo de geometria a manter daqui para a
        /// frente, e tudo o resto do plugin já a sabe ler.
        /// </summary>
        private static Entity Copiar(Entity ent)
        {
            var linha = ent as Line;
            if (linha != null)
            {
                var pl = new Polyline();
                pl.AddVertexAt(0, new Autodesk.AutoCAD.Geometry.Point2d(
                    linha.StartPoint.X, linha.StartPoint.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Autodesk.AutoCAD.Geometry.Point2d(
                    linha.EndPoint.X, linha.EndPoint.Y), 0, 0, 0);
                return pl;
            }

            if (ent is Polyline || ent is Hatch)
            {
                try { return ent.Clone() as Entity; }
                catch { return null; }
            }
            return null;
        }

        /// <summary>Uma medição com as definições que estão no painel.</summary>
        private static Parede NovaMedicao(string servico, bool porArea,
            bool deHachuraQueParaNoVao, double espessura, bool retangulo)
        {
            return new Parede
            {
                Servico = servico,
                Piso = Config.Piso,
                Bloco = Config.Bloco,
                // O bloco também etiqueta a linha: "WC1", "quarto 2".
                Nota = FolhaMedicao.NotaInicial(null, Config.Bloco),
                Alcado = Config.Alcado,
                // O artigo DESTE serviço — não o do painel.
                //
                // Aqui cria-se uma medição por serviço marcado, e cada serviço
                // pertence a um artigo diferente do mapa: a alvenaria ao 1.1.1,
                // o reboco ao 4.9.1.1, o revestimento ao 7.2.1. Com o artigo do
                // painel, as três iam parar ao mesmo sítio da folha — o
                // contrário de medir uma parede em três camadas.
                //
                // Sem ligação definida para o serviço, vale o do painel, e
                // tudo se comporta como antes.
                Artigo = MapaQuantidades.ArtigoParaMedicao(servico),
                // Numa hachura a altura não entra na conta: a área já é o
                // número. Fica a 1 para a multiplicação dar a própria área.
                Altura = porArea ? 1.0 : Config.Altura,
                Espessura = espessura,
                SoArea = porArea,
                // Marcado à cabeça, não a seguir: é isto que faz a leitura usar
                // os LADOS do rectângulo em vez do seu perímetro.
                Retangulo = retangulo,
                // A hachura de planta interrompe-se nas portas: o comprimento
                // vem curto. Cada vão que se acrescentar repõe a sua largura,
                // e a medição volta a ser a parede inteira.
                GeometriaSemVaos = deHachuraQueParaNoVao && !porArea,
            };
        }

        /// <summary>
        /// Área das hachuras seleccionadas. Serve para mostrar, antes de criar,
        /// o comprimento que vai sair — foi por não se ver isto que uma forra
        /// de 2,5 cm medida com a espessura da alvenaria deu 0,58 m em vez de
        /// 3,47 e ninguém percebeu porquê.
        /// </summary>
        public static double AreaDasHachuras(IList<ObjectId> ids)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || ids == null) return 0;

            double total = 0;
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in ids)
                {
                    var h = tr.GetObject(id, OpenMode.ForRead) as Hatch;
                    if (h == null) continue;
                    try { total += System.Math.Abs(h.Area); } catch { }
                }
                tr.Commit();
            }
            return total;
        }

        /// <summary>Serviços já usados no desenho, para a lista aparecer preenchida.</summary>
        public static List<string> ServicosConhecidos(Database db)
        {
            var vistos = new List<string>();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string actual = (Config.Servico ?? "").Trim();
            if (actual.Length > 0) { set.Add(actual); vistos.Add(actual); }

            try
            {
                List<Parede> paredes;
                List<MedFachada> fachadas;
                List<MedItem> lineares;
                List<MedContagem> contagens;
                Leitura.Tudo(db, out paredes, out fachadas, out lineares, out contagens);
                foreach (var p in paredes)
                {
                    string s = (p.Servico ?? "").Trim();
                    if (s.Length > 0 && set.Add(s)) vistos.Add(s);
                }
            }
            catch { }

            vistos.Sort(StringComparer.CurrentCultureIgnoreCase);
            return vistos;
        }
    }

    /// <summary>Comando TSKMEDSEL.</summary>
    public class MedirSeleccaoCmd
    {
        /// <summary>
        /// Diz tudo o que se consegue saber de uma hachura seleccionada.
        ///
        /// A conversão de área em comprimento depende da espessura, e três
        /// tentativas de a adivinhar a partir da geometria falharam sem que
        /// houvesse forma de ver porquê. Isto mostra os números em bruto para
        /// se perceber onde é que a leitura se perde.
        /// </summary>
        [CommandMethod("TSKHATCHDIAG")]
        public void HatchDiag()
        {
            Util.Seguro("TSKHATCHDIAG", () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var ed = doc.Editor;

                var opc = new PromptEntityOptions("\nEscolha uma hachura:");
                opc.SetRejectMessage("\nTem de ser uma hachura.");
                opc.AddAllowedClass(typeof(Hatch), true);
                var res = ed.GetEntity(opc);
                if (res.Status != PromptStatus.OK) return;

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var h = tr.GetObject(res.ObjectId, OpenMode.ForRead) as Hatch;
                    if (h == null) return;

                    var sb = new System.Text.StringBuilder();
                    sb.Append("\n--- HACHURA ---");
                    sb.Append("\n  layer            : " + h.Layer);
                    sb.Append("\n  padrão           : " + h.PatternName);
                    sb.Append("\n  associativa      : " + h.Associative);

                    // O plano da hachura. Com Normal diferente de Z, as
                    // coordenadas dos contornos não são WCS — e um rectângulo
                    // construído como se fossem aterra noutro sítio.
                    try
                    {
                        var n = h.Normal;
                        bool deitada = System.Math.Abs(n.Z - 1.0) > 1e-6;
                        sb.Append("\n  normal           : (" + n.X.ToString("N4") + ", " +
                                  n.Y.ToString("N4") + ", " + n.Z.ToString("N4") + ")" +
                                  (deitada ? "   <-- NÃO É Z: hachura fora do plano do mundo"
                                           : "   (plano do mundo)"));
                        sb.Append("\n  elevação         : " + h.Elevation.ToString("N4"));
                    }
                    catch { }

                    double area = 0;
                    try { area = System.Math.Abs(h.Area); }
                    catch (System.Exception ex) { sb.Append("\n  Area FALHOU: " + ex.Message); }
                    sb.Append("\n  área             : " + area.ToString("N6") + " m²");

                    sb.Append("\n  contornos        : " + h.NumberOfLoops);
                    for (int i = 0; i < h.NumberOfLoops; i++)
                    {
                        try
                        {
                            var laco = h.GetLoopAt(i);
                            sb.Append("\n    [" + i + "] tipo=" + laco.LoopType);
                            sb.Append("  polyline=" +
                                (laco.Polyline == null ? "NULL"
                                 : laco.Polyline.Count + " vértices"));
                            sb.Append("  curvas=" +
                                (laco.Curves == null ? "NULL" : laco.Curves.Count.ToString()));

                            // Onde está e que tamanho tem cada pedaço. É o que
                            // falta para saber se são troços em fila ao longo
                            // da parede ou faixas paralelas lado a lado — os
                            // totais sozinhos não distinguem os dois casos.
                            double x0 = double.MaxValue, y0 = double.MaxValue;
                            double x1 = double.MinValue, y1 = double.MinValue;
                            double compr = 0;
                            if (laco.Curves != null)
                            {
                                foreach (Autodesk.AutoCAD.Geometry.Curve2d c in laco.Curves)
                                {
                                    try
                                    {
                                        var iv = c.GetInterval();
                                        compr += c.GetLength(iv.LowerBound, iv.UpperBound);
                                        var pa = c.EvaluatePoint(iv.LowerBound);
                                        var pb = c.EvaluatePoint(iv.UpperBound);
                                        foreach (var pt in new[] { pa, pb })
                                        {
                                            if (pt.X < x0) x0 = pt.X;
                                            if (pt.X > x1) x1 = pt.X;
                                            if (pt.Y < y0) y0 = pt.Y;
                                            if (pt.Y > y1) y1 = pt.Y;
                                        }
                                    }
                                    catch { }
                                }
                            }
                            if (x0 <= x1)
                                sb.Append("\n         caixa " + (x1 - x0).ToString("N4") +
                                          " x " + (y1 - y0).ToString("N4") +
                                          "   em (" + x0.ToString("N2") + ", " +
                                          y0.ToString("N2") + ")   perímetro " +
                                          compr.ToString("N4"));
                        }
                        catch (System.Exception ex)
                        {
                            sb.Append("\n    [" + i + "] FALHOU: " + ex.Message);
                        }
                    }

                    double per = 0;
                    try { per = PerimetroPublico(h); } catch { }
                    sb.Append("\n  perímetro lido   : " + per.ToString("N4"));

                    double esp = TSKTakeOff.MedirSeleccao.EspessuraDaHachura(h);
                    sb.Append("\n  espessura detect.: " +
                        (esp > 0 ? esp.ToString("N4") + " m" : "NÃO DETECTADA"));
                    if (esp > 0)
                        sb.Append("\n  comprimento      : " + (area / esp).ToString("N3") + " m");

                    // A caixa envolvente serve de segunda opinião: numa parede
                    // alinhada com os eixos, o lado menor É a espessura.
                    try
                    {
                        var ext = h.GeometricExtents;
                        double dx = ext.MaxPoint.X - ext.MinPoint.X;
                        double dy = ext.MaxPoint.Y - ext.MinPoint.Y;
                        sb.Append("\n  caixa            : " + dx.ToString("N4") +
                                  " x " + dy.ToString("N4"));
                        double menor = System.Math.Min(dx, dy);
                        if (menor > 0)
                            sb.Append("\n  se espessura=" + menor.ToString("N4") +
                                      " -> comprimento " + (area / menor).ToString("N3"));
                    }
                    catch { }

                    // ---- eixo e topologia ----------------------------------
                    // O que decide se os vãos são legíveis. Sem isto, uma
                    // hachura que não devolve vãos nenhuns não diz porquê, e
                    // não se sabe se é a parede que está rodada, se é o
                    // desenho que não tem interrupções, ou se é bug nosso.
                    try
                    {
                        Autodesk.AutoCAD.Geometry.Vector2d u, v;
                        AlvRepo.EixoDaHachura(h, out u, out v);
                        double graus = System.Math.Atan2(u.Y, u.X) * 180.0 / System.Math.PI;
                        sb.Append("\n  --- eixo da parede ---");
                        sb.Append("\n  direcção         : " + graus.ToString("N2") + "°");
                        int nCons, nFora;
                        bool coerente = AlvRepo.GeometriaCoerenteComEixo(
                            h, u, 2.0, out nCons, out nFora);
                        sb.Append("\n  coerente         : " +
                            (coerente ? "sim" : "NÃO — contornos a ângulos diferentes") +
                            "   (segmentos longos considerados: " + nCons +
                            ", fora do eixo: " + nFora + ")");

                        var proj = new List<double[]>();
                        for (int i = 0; i < h.NumberOfLoops; i++)
                        {
                            HatchLoop laco;
                            try { laco = h.GetLoopAt(i); } catch { continue; }
                            if (laco == null || laco.Curves == null) continue;
                            double a = double.MaxValue, b = double.MinValue;
                            foreach (Autodesk.AutoCAD.Geometry.Curve2d c in laco.Curves)
                            {
                                try
                                {
                                    var iv = c.GetInterval();
                                    foreach (var pt in new[] { c.EvaluatePoint(iv.LowerBound),
                                                               c.EvaluatePoint(iv.UpperBound) })
                                    {
                                        double p = pt.X * u.X + pt.Y * u.Y;
                                        if (p < a) a = p;
                                        if (p > b) b = p;
                                    }
                                }
                                catch { }
                            }
                            if (a <= b) proj.Add(new[] { a, b });
                        }

                        if (proj.Count > 0)
                        {
                            double ini = proj[0][0], fi = proj[0][1];
                            foreach (var t in proj)
                            { if (t[0] < ini) ini = t[0]; if (t[1] > fi) fi = t[1]; }
                            double total = fi - ini;

                            int nExt = 0;
                            foreach (var t in proj)
                                if (total > 0 && (t[1] - t[0]) >= 0.95 * total) nExt++;

                            sb.Append("\n  extensão no eixo : " + total.ToString("N4") + " m");
                            sb.Append("\n  contornos no eixo:");
                            for (int i = 0; i < proj.Count; i++)
                                sb.Append("\n    [" + i + "] de " +
                                    (proj[i][0] - ini).ToString("N4") + " a " +
                                    (proj[i][1] - ini).ToString("N4") + "   (largura " +
                                    (proj[i][1] - proj[i][0]).ToString("N4") + ")");

                            string top;
                            if (proj.Count < 2)
                                top = "CONTORNO ÚNICO — não há vãos legíveis na geometria. " +
                                      "Os pilares e portas devem estar por cima da hachura, " +
                                      "como objectos à parte. Acrescente os vãos à mão.";
                            else if (nExt > 0)
                                top = "EXTERIOR + ILHAS — os vãos são as ilhas (" +
                                      (proj.Count - nExt) + ").";
                            else
                                top = "TROÇOS SEPARADOS — os vãos são as folgas entre eles.";
                            sb.Append("\n  topologia        : " + top);
                        }
                    }
                    catch (System.Exception ex)
                    {
                        sb.Append("\n  eixo/topologia FALHOU: " + ex.Message);
                    }

                    // Correr a própria função que falha, com os números à
                    // vista. Adivinhar onde ela se perde já custou quatro
                    // rondas; isto responde de uma vez.
                    sb.Append("\n  --- vãos entre os troços ---");
                    try
                    {
                        var ext2 = h.GeometricExtents;
                        double ddx = ext2.MaxPoint.X - ext2.MinPoint.X;
                        double ddy = ext2.MaxPoint.Y - ext2.MinPoint.Y;
                        sb.Append("\n  direcção         : " +
                            (ddy >= ddx ? "ao longo de Y" : "ao longo de X") +
                            "  (dx=" + ddx.ToString("N3") + " dy=" + ddy.ToString("N3") + ")");
                    }
                    catch (System.Exception ex)
                    {
                        sb.Append("\n  GeometricExtents FALHOU: " + ex.Message);
                    }

                    var achados = TSKTakeOff.MedirSeleccao.VaosEntreTrocos(h);
                    sb.Append("\n  vãos devolvidos  : " + achados.Count);
                    foreach (double lg in achados)
                        sb.Append("\n     " + lg.ToString("N4") + " m");
                    if (achados.Count == 0)
                        sb.Append("\n     (nada — ou os troços não foram lidos, " +
                                  "ou as folgas ficaram fora de 0,30..4,00)");

                    ed.WriteMessage(sb.ToString() + "\n");
                    tr.Commit();
                }
            });
        }

        [CommandMethod("TSKMEDSEL", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void MedirSeleccao()
        {
            Util.Seguro("TSKMEDSEL", () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                if (!Licenca.PodeMedir()) return;
                var ed = doc.Editor;

                // Polylines, linhas e hachuras. O UsePickSet acima faz com que
                // uma selecção feita antes do comando — por exemplo com o
                // QSELECT por layer — chegue aqui intacta.
                var filtro = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.Start, "LINE"),
                    new TypedValue((int)DxfCode.Start, "HATCH"),
                    // INSERT aceita XREFs e blocos. O AutoCAD não deixa
                    // seleccionar o que está lá dentro, por isso apanha-se a
                    // inserção e desce-se depois. Sem isto, clicar numa parede
                    // dentro de um XREF dava "0 encontrados" e parecia avaria.
                    new TypedValue((int)DxfCode.Start, "INSERT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });

                PromptSelectionResult sel = ed.SelectImplied();
                if (sel.Status != PromptStatus.OK || sel.Value == null)
                {
                    ed.WriteMessage("\nSeleccione polylines, linhas ou hachuras " +
                                    "para transformar em medições.\n");
                    sel = ed.GetSelection(filtro);
                }
                if (sel.Status != PromptStatus.OK || sel.Value == null) return;

                var brutos = new List<ObjectId>(sel.Value.GetObjectIds());
                if (brutos.Count == 0) return;

                // Desce aos XREFs, se houver algum na selecção.
                List<TSKTakeOff.MedirSeleccao.Candidato> ids;
                int nXrefs = 0;
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    ids = TSKTakeOff.MedirSeleccao.ExpandirSeleccao(tr, brutos, out nXrefs);
                    tr.Commit();
                }
                if (ids.Count == 0)
                {
                    if (nXrefs > 0)
                        ed.WriteMessage("\nNada a medir dentro do XREF.\n");
                    return;
                }

                if (nXrefs > 0)
                    ed.WriteMessage("\nTSK — {0} objecto(s) recolhido(s) de dentro " +
                                    "de {1} XREF(s).\n", ids.Count, nXrefs);

                var soIds = new List<ObjectId>();
                foreach (var c in ids) soIds.Add(c.Id);

                bool alcado, juntar;
                double espessura;
                var servicos = EscolherServicos(doc.Database, soIds,
                                                out alcado, out juntar, out espessura);
                if (servicos == null || servicos.Count == 0) return;

                int feitas = TSKTakeOff.MedirSeleccao.Criar(
                    ids, servicos, alcado, juntar, espessura);

                ed.WriteMessage("\nTSK — {0} medições criadas a partir de {1} objectos, " +
                                "em {2} serviço(s).\n", feitas, ids.Count, servicos.Count);

                try { ed.SetImpliedSelection(new ObjectId[0]); } catch { }
                // Várias medições de uma vez: não há "a última". Escolha-se na
                // grelha ou no Excel qual leva o vão.
                PaletteHost.EsquecerUltimaMedicao();
                PaletteHost.RefreshData();
            });
        }

        private static double PerimetroPublico(Hatch h)
        {
            return TSKTakeOff.MedirSeleccao.PerimetroDe(h);
        }

        private static int MedirSeleccao_Criar(IList<ObjectId> ids,
            IList<string> servicos, bool alcado, bool juntar, double espessura)
        {
            return TSKTakeOff.MedirSeleccao.Criar(ids, servicos, alcado, juntar, espessura);
        }

        /// <summary>
        /// Que camadas criar a partir desta selecção. Uma lista com o que já se
        /// usou no desenho, mais espaço para escrever um serviço novo.
        /// </summary>
        private static List<string> EscolherServicos(Database db, IList<ObjectId> ids,
            out bool hachuraDeAlcado, out bool juntarNumaSo, out double espessura)
        {
            hachuraDeAlcado = false;
            // A mesma omissão da caixa no diálogo. Se o utilizador cancelar,
            // este valor não é usado — mas dois sítios a dizer o contrário um
            // do outro é o tipo de coisa que morde meses depois.
            juntarNumaSo = true;
            espessura = Config.Espessura;

            int quantos = ids.Count;
            double areaHachuras = TSKTakeOff.MedirSeleccao.AreaDasHachuras(ids);
            var conhecidos = TSKTakeOff.MedirSeleccao.ServicosConhecidos(db);

            using (var frm = new Form())
            using (var lista = new CheckedListBox())
            using (var novo = new TextBox())
            using (var ok = new Button())
            using (var cancelar = new Button())
            using (var rot = new Label())
            {
                frm.Text = "TSK TakeOff — medir a selecção";
                frm.StartPosition = FormStartPosition.CenterScreen;
                frm.FormBorderStyle = FormBorderStyle.FixedDialog;
                frm.MinimizeBox = false;
                frm.MaximizeBox = false;
                frm.ClientSize = new System.Drawing.Size(470, 460);

                rot.Text = quantos + " objectos seleccionados.\n\n" +
                           "Escolha as camadas a medir. Marcando mais do que uma, " +
                           "cada objecto dá origem a uma medição em cada — é o caso " +
                           "de uma hachura que representa a parede toda.";
                rot.Dock = DockStyle.Top;
                rot.Height = 76;
                rot.Padding = new Padding(10, 10, 10, 0);

                lista.Dock = DockStyle.Fill;
                lista.CheckOnClick = true;
                lista.IntegralHeight = false;
                // Cada serviço mostra o artigo do mapa a que está ligado.
                // Marcar três camadas às cegas e só descobrir na folha que
                // duas foram para o artigo errado é o tipo de erro que se
                // paga a conferir a obra inteira outra vez.
                foreach (string s in conhecidos)
                {
                    string chave = MapaQuantidades.ArtigoDoServico(s);
                    if (chave.Length > 0)
                    {
                        var no = MapaQuantidades.Procurar(chave);
                        lista.Items.Add(s + "   →   " +
                            (no != null ? no.Codigo : "?"));
                    }
                    else if (MapaQuantidades.Existe)
                    {
                        lista.Items.Add(s + "   →   (sem artigo — escolha um no painel)");
                    }
                    else lista.Items.Add(s);
                }
                if (lista.Items.Count > 0) lista.SetItemChecked(0, true);

                // Como interpretar uma hachura. São dois casos reais e
                // diferentes: em planta a hachura é a pegada da parede e o que
                // se quer é o desenvolvimento; em alçado é já a superfície.
                var caixaHachura = new GroupBox
                {
                    Text = "Hachuras",
                    Dock = DockStyle.Bottom,
                    Height = 110
                };
                var rbPlanta = new RadioButton
                {
                    Text = "Planta — comprimento = área ÷ espessura",
                    Checked = true,
                    AutoSize = true
                };
                rbPlanta.SetBounds(12, 18, 300, 20);
                var rbAlcado = new RadioButton
                {
                    Text = "Alçado — a área da hachura é a medição",
                    AutoSize = true
                };
                rbAlcado.SetBounds(12, 40, 300, 20);

                var rotEsp = new Label { Text = "Espessura (m):" };
                rotEsp.SetBounds(12, 66, 90, 20);
                var numEsp = new NumericUpDown
                {
                    DecimalPlaces = 3,
                    Increment = 0.005M,
                    Minimum = 0.001M,
                    Maximum = 2M,
                    Value = (decimal)(Config.Espessura > 0 ? Config.Espessura : 0.15)
                };
                numEsp.SetBounds(105, 64, 70, 22);

                // O resultado à vista ANTES de criar. É isto que faz saltar à
                // vista uma espessura trocada: uma forra de 2,5 cm medida com
                // 0,15 dá um comprimento absurdo, e vê-se aqui.
                var lblConta = new Label { AutoSize = false };
                lblConta.SetBounds(185, 64, 250, 40);
                EventHandler recalcular = (s2, e2) =>
                {
                    if (areaHachuras <= 0) { lblConta.Text = "(sem hachuras)"; return; }
                    double e = (double)numEsp.Value;
                    lblConta.Text = rbAlcado.Checked
                        ? "área " + Util.N2(areaHachuras) + " m²"
                        : "área " + areaHachuras.ToString("N4") + " m²  →  comprimento " +
                          Util.N2(e > 0 ? areaHachuras / e : 0) + " m";
                };
                numEsp.ValueChanged += recalcular;
                rbPlanta.CheckedChanged += recalcular;
                recalcular(null, null);

                caixaHachura.Controls.Add(rbPlanta);
                caixaHachura.Controls.Add(rbAlcado);
                caixaHachura.Controls.Add(rotEsp);
                caixaHachura.Controls.Add(numEsp);
                caixaHachura.Controls.Add(lblConta);

                var baixo = new Panel { Dock = DockStyle.Bottom, Height = 98 };
                var rotNovo = new Label { Text = "Serviço novo (opcional):" };
                rotNovo.SetBounds(10, 6, 150, 20);
                novo.SetBounds(160, 4, 260, 22);

                var cbJuntar = new CheckBox
                {
                    Text = "Juntar tudo numa medição só (troços da mesma parede)",
                    AutoSize = true,
                    // Marcada por omissão: seleccionar uma parede é quase
                    // sempre seleccionar os troços dela, e o que se quer na
                    // folha é UMA linha por parede, não uma por troço. Deixá-la
                    // desmarcada obrigava a lembrar-se dela a cada medição.
                    Checked = true
                };
                cbJuntar.SetBounds(10, 30, 420, 20);

                ok.Text = "Medir";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(240, 56, 90, 28);
                cancelar.Text = "Cancelar";
                cancelar.DialogResult = DialogResult.Cancel;
                cancelar.SetBounds(338, 56, 90, 28);

                baixo.Controls.Add(cbJuntar);
                baixo.Controls.Add(rotNovo);
                baixo.Controls.Add(novo);
                baixo.Controls.Add(ok);
                baixo.Controls.Add(cancelar);

                frm.Controls.Add(lista);
                frm.Controls.Add(baixo);
                frm.Controls.Add(caixaHachura);
                frm.Controls.Add(rot);
                frm.AcceptButton = ok;
                frm.CancelButton = cancelar;

                if (AcadApp.ShowModalDialog(frm) != DialogResult.OK) return null;
                hachuraDeAlcado = rbAlcado.Checked;
                juntarNumaSo = cbJuntar.Checked;
                espessura = (double)numEsp.Value;

                // Por ÍNDICE, não pelo texto do item: o item mostra
                // "ALVENARIA   →   1.1.1" e o nome do serviço é só a primeira
                // parte. Usar o texto criava serviços chamados
                // "ALVENARIA   →   1.1.1", com layer e tudo.
                var escolhidos = new List<string>();
                foreach (int i in lista.CheckedIndices)
                    if (i >= 0 && i < conhecidos.Count) escolhidos.Add(conhecidos[i]);

                string extra = (novo.Text ?? "").Trim();
                if (extra.Length > 0 && !escolhidos.Contains(extra))
                    escolhidos.Add(extra);

                return escolhidos;
            }
        }
    }
}
