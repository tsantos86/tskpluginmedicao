using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using MessageBox = System.Windows.Forms.MessageBox;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;
using MessageBoxDefaultButton = System.Windows.Forms.MessageBoxDefaultButton;
using DialogResult = System.Windows.Forms.DialogResult;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TSKTakeOff.Commands))]
[assembly: ExtensionApplication(typeof(TSKTakeOff.PluginInit))]

namespace TSKTakeOff
{
    public class PluginInit : IExtensionApplication
    {
        public void Initialize()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            AutocadRuntime.Capturar();

            // A folha vai buscar aqui a ordem do articulado. Sem esta linha ela
            // ordena como se não houvesse mapa importado — e não se queixa,
            // porque não ter mapa é um estado legítimo. Por isso o verificar.py
            // confirma que ela existe.
            FolhaMedicao.OrdemDoArtigo = MapaQuantidades.OrdemDe;
            FolhaMedicao.ArtigoCanonico = MapaQuantidades.ChaveCanonica;

            // Dizer SEMPRE de onde veio a DLL e de quando é. Sem isto é fácil
            // andar a testar a versão antiga sem dar por ela: o bundle em
            // ApplicationPlugins carrega no arranque e um NETLOAD por cima não
            // substitui os comandos já registados.
            ed?.WriteMessage("\n[TSK TakeOff] " + VersaoCarregada() + "\n" +
                             "Separador \"TSK TakeOff\" na ribbon ou comando TSKPAINEL.\n");
            RibbonBuilder.Inicializar();
            Util.GarantirLwDisplay();         // senão o traço só engrossa no papel
            Telemetria.RegistarSessao();      // assíncrono; nunca prende o arranque
            Licenca.VerificarNoArranque();    // idem: revalida em silêncio

            if (!Licenca.Valida)
                ed?.WriteMessage("[TSK] " + Licenca.Resumo() + "\n");
        }

        public void Terminate()
        {
            // Desconectar já guarda os textos escritos no Excel; a chamada
            // explícita fica para o caso de a desconexão falhar a meio.
            try { PaletteHost.Excel.GuardarTextosPendentes(); } catch { }
            PaletteHost.Excel.Desconectar();
        }

        /// <summary>Versão, data de compilação e caminho da DLL em uso.</summary>
        internal static string VersaoCarregada()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string caminho = asm.Location;

                // NÃO usar o GetName().Version aqui: esse é o AssemblyVersion,
                // fixo em 1.2.6.0 de propósito para não partir referências
                // .NET. O número do build vive no InformationalVersion, e é o
                // que o Versao.Curta lê. Era por isto que o arranque anunciava
                // v1.2.6.0 mesmo depois de a DLL passar a levar o build certo.
                string versao = Versao.Curta;

                string quando = "";
                try
                {
                    quando = " · compilada " +
                        File.GetLastWriteTime(caminho).ToString("dd/MM/yyyy HH:mm");
                }
                catch { }

                return "v" + versao + quando + "\n" + caminho;
            }
            catch (System.Exception ex)
            {
                return "versão desconhecida (" + ex.Message + ")";
            }
        }
    }

    public class Commands
    {
        /// <summary>RegApp das medições lineares simples (MEDIR).</summary>
        internal const string AppNameLinear = "CASQUILHO_MED";
        // Marcador técnico das cópias secundárias de TSKAREASEL. Não é
        // CASQUILHO_ALV de propósito: só a portadora é uma medição para o
        // leitor da folha; as outras cópias não podem entrar nos totais.
        internal const string AppNameAreaOrigem = "CASQUILHO_AREA_ORIG";
        internal const string LayerPrefix = "MED_";

        // ------------------------------------------------------------------
        // TSKPAINEL — abre a paleta principal do plugin.
        // ------------------------------------------------------------------
        [CommandMethod("TSKPAINEL")]
        public void MedPainel()
        {
            // A primeira abertura do painel é o momento certo para perguntar
            // sobre dados de utilização: a pessoa veio usar o plugin de livre
            // vontade e está a olhar para o ecrã.
            PrivacidadeDialog.PerguntarSeNecessario();
            PaletteHost.Show();
        }

        // ------------------------------------------------------------------
        // TSKSOBRE — informação do plugin: marca, versão, build e contacto.
        // ------------------------------------------------------------------
        [CommandMethod("TSKSOBRE")]
        public void TskSobre()
        {
            Util.Seguro("TSKSOBRE", SobreDialog.Mostrar);
        }

        // ------------------------------------------------------------------
        // TSKPRIVACIDADE — ver e mudar a decisão sobre dados de utilização.
        // ------------------------------------------------------------------
        [CommandMethod("TSKPRIVACIDADE")]
        public void TskPrivacidade()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;

            string estado;
            switch (Telemetria.Consentimento)
            {
                case Telemetria.Escolha.Aceite:
                    estado = "SIM — o registo de arranque está a ser enviado."; break;
                case Telemetria.Escolha.Recusada:
                    estado = "NÃO — não sai nada desta máquina."; break;
                default:
                    estado = "por responder — até responderes, não sai nada."; break;
            }

            ed?.WriteMessage("\n--- Dados de utilização ---\n" +
                             Telemetria.TextoExplicativo().Replace("\r\n", "\n") +
                             "\n\nEstado actual: " + estado + "\n" +
                             "Identificador desta instalação: " + Telemetria.IdInstalacao() + "\n");

            var r = MessageBox.Show(
                Telemetria.TextoExplicativo() + "\r\n\r\nEstado actual: " + estado +
                "\r\n\r\nQueres enviar o registo de arranque?",
                "TSK TakeOff — dados de utilização",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);

            if (r == DialogResult.Yes)
            {
                Telemetria.GuardarEscolha(true);
                Telemetria.RegistarSessao();
                ed?.WriteMessage("\n[TSK] Envio ligado. Obrigado.\n");
            }
            else
            {
                Telemetria.GuardarEscolha(false);
                ed?.WriteMessage("\n[TSK] Envio desligado. A fila pendente foi apagada.\n");
            }
        }

        /// <summary>Liga/desliga o espelhamento em tempo real no Excel.</summary>
        [CommandMethod("MEDEXCELLIVE")]
        public void MedExcelLive()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            try
            {
                if (PaletteHost.Excel.Conectado)
                {
                    PaletteHost.Excel.Desconectar();
                    ed?.WriteMessage("\nExcel ao vivo desligado.");
                }
                else
                {
                    string nomeBase = NomeBaseDoDesenho();
                    try
                    {
                        PaletteHost.Excel.Conectar(nomeBase);
                    }
                    catch
                    {
                        // O Excel anterior pode ter sido fechado à mão e deixado
                        // uma referência morta: limpar e tentar mais uma vez.
                        PaletteHost.Excel.Desconectar();
                        System.Threading.Thread.Sleep(400);
                        PaletteHost.Excel.Conectar(nomeBase);
                    }
                    PaletteHost.RefreshData();

                    if (PaletteHost.Excel.ModoModelo)
                        ed?.WriteMessage("\nExcel ao vivo ligado sobre o modelo da casa " +
                                         "(macros disponíveis).");
                    else if (PaletteHost.Excel.AvisoModelo != null)
                        ed?.WriteMessage("\nExcel ao vivo ligado em folha simples: " +
                                         PaletteHost.Excel.AvisoModelo);
                    else
                        ed?.WriteMessage("\nExcel ao vivo ligado (folha simples — " +
                                         "use TSKMODELO para usar o modelo da casa).");
                }
            }
            catch (System.Exception ex)
            {
                PaletteHost.Excel.Desconectar();
                ed?.WriteMessage("\nNão foi possível abrir o Excel: " + ex.Message +
                                 "\nFeche o Excel e volte a carregar em Excel ao Vivo.");
            }
        }

        // ------------------------------------------------------------------
        // TSKCONTAR — conta blocos e marca cada um com um círculo.
        //
        // UsePickSet é o que faz o quickselect servir: se já houver blocos
        // seleccionados quando se carrega no botão, usa-se essa selecção. Sem
        // esta flag o AutoCAD descartava-a e obrigava a seleccionar outra vez.
        // ------------------------------------------------------------------
        [CommandMethod("TSKCONTAR", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void TskContar() => Util.Seguro("TSKCONTAR", TskContarImpl);

        private void TskContarImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;
            Util.AvisarSeForaDoModel(doc);

            var db = doc.Database;
            var ed = doc.Editor;

            string nome = (ContagemConfig.Nome ?? "").Trim();
            if (nome.Length == 0)
            {
                ed.WriteMessage("\nEscreva primeiro o nome (ex.: P.01) na aba Contagens.\n");
                return;
            }

            // 1) selecção que já vinha do quickselect; 2) senão, pede-se.
            var sel = ed.SelectImplied();
            if (sel.Status != PromptStatus.OK || sel.Value == null || sel.Value.Count == 0)
            {
                var filtro = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "INSERT")
                });
                var opts = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelecione os blocos a contar como \"" + nome + "\""
                };
                sel = ed.GetSelection(opts, filtro);
                if (sel.Status != PromptStatus.OK) return;
            }

            var ids = sel.Value.GetObjectIds();
            if (ids.Length == 0) { ed.WriteMessage("\nNada seleccionado.\n"); return; }

            string layerName = ContRepo.Layer(nome);
            short cor = ContagemConfig.CorDoNome(nome);
            double raio = ContagemConfig.Raio > 0 ? ContagemConfig.Raio : 0.25;

            int contados = 0, ignorados = 0, repetidos = 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureLayer(tr, db, layerName, cor);
                Util.EnsureRegApp(tr, db, ContRepo.AppName);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                // Centros já marcados, para não contar o mesmo bloco duas vezes
                // quando a selecção se sobrepõe a uma contagem anterior.
                var jaMarcados = CentrosJaMarcados(tr, ms);

                foreach (ObjectId id in ids)
                {
                    var br = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (br == null) { ignorados++; continue; }

                    var centro = ContRepo.CentroDoBloco(br);
                    if (JaMarcado(jaMarcados, centro, raio)) { repetidos++; continue; }

                    var circ = new Circle { Center = centro, Radius = raio };
                    ms.AppendEntity(circ);
                    tr.AddNewlyCreatedDBObject(circ, true);
                    try { circ.Layer = layerName; } catch { }

                    ContRepo.GravarXData(circ, new MedContagem
                    {
                        Nome = nome,
                        Piso = ContagemConfig.Piso,
                        Categoria = ContagemConfig.Categoria,
                        Bloco = NomeDoBloco(tr, br)
                    });

                    if (ContagemConfig.ComTexto)
                        EscreverRotulo(tr, ms, db, centro, raio, nome, layerName);

                    jaMarcados.Add(centro);
                    contados++;
                }

                tr.Commit();
            }

            // Largar a selecção: senão fica realçada e confunde na conta seguinte.
            try { ed.SetImpliedSelection(new ObjectId[0]); } catch { }

            ContagemConfig.RegistarNome(nome);

            ed.WriteMessage("\n{0}: {1} contado(s)", nome, contados);
            if (repetidos > 0) ed.WriteMessage(" · {0} já estavam marcados", repetidos);
            if (ignorados > 0) ed.WriteMessage(" · {0} não eram blocos", ignorados);
            ed.WriteMessage("\n");

            PaletteHost.RefreshData();
        }

        private static void EscreverRotulo(Transaction tr, BlockTableRecord ms, Database db,
            Point3d centro, double raio, string nome, string layerName)
        {
            try
            {
                var texto = new MText
                {
                    Location = new Point3d(centro.X + raio * 1.3, centro.Y + raio * 1.3, 0),
                    Contents = nome,
                    TextHeight = Util.AlturaTexto(db)
                };
                ms.AppendEntity(texto);
                tr.AddNewlyCreatedDBObject(texto, true);
                try { texto.Layer = layerName; } catch { }
            }
            catch { /* o rótulo é acessório: a contagem vale à mesma */ }
        }

        private static string NomeDoBloco(Transaction tr, BlockReference br)
        {
            try
            {
                var btr = (BlockTableRecord)tr.GetObject(
                    br.DynamicBlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            catch { return ""; }
        }

        private static List<Point3d> CentrosJaMarcados(Transaction tr, BlockTableRecord ms)
        {
            var pts = new List<Point3d>();
            foreach (ObjectId id in ms)
            {
                var c = tr.GetObject(id, OpenMode.ForRead) as Circle;
                if (c == null) continue;
                using (var rb = c.GetXDataForApplication(ContRepo.AppName))
                {
                    if (rb != null) pts.Add(c.Center);
                }
            }
            return pts;
        }

        private static bool JaMarcado(List<Point3d> pontos, Point3d centro, double raio)
        {
            double limite = raio * 0.5;
            foreach (var p in pontos)
                if (p.DistanceTo(centro) < limite) return true;
            return false;
        }

        /// <summary>Apaga as contagens de um nome, ou todas.</summary>
        [CommandMethod("TSKCONTLIMPAR")]
        public void TskContLimpar()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var opt = new PromptStringOptions(
                "\nNome a apagar <Enter = todas as contagens>: ") { AllowSpaces = true };
            var res = ed.GetString(opt);
            if (res.Status != PromptStatus.OK) return;

            string nome = (res.StringResult ?? "").Trim();
            int n = ContRepo.Limpar(doc.Database, nome);

            ed.WriteMessage("\n{0} contagem(ns) apagada(s).\n", n);
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Leva para o desenho o que estiver escrito nas linhas de título do
        /// Excel. Corre sozinho ao medir, ao actualizar e ao desligar — este
        /// comando existe para quem quiser garantir sem esperar por nada.
        /// </summary>
        [CommandMethod("TSKGUARDARTEXTOS")]
        public void TskGuardarTextos()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (!PaletteHost.Excel.Conectado)
            {
                ed?.WriteMessage("\nO Excel ao vivo não está ligado.\n");
                return;
            }

            PaletteHost.Excel.GuardarTextosPendentes();
            PaletteHost.RefreshData();
            ed?.WriteMessage("\nTextos da folha guardados no desenho.\n");
        }

        // ------------------------------------------------------------------
        // TSKMODELO — registar o ficheiro-modelo de medições da casa.
        // ------------------------------------------------------------------
        [CommandMethod("TSKMODELO")]
        public void TskModelo()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n[TSK] " + ModeloExcel.Resumo() + "\n");

            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Escolher o modelo de medições (mantém as macros)";
                dlg.Filter = "Livros do Excel (*.xls;*.xlsm;*.xlsx)|*.xls;*.xlsm;*.xlsx|" +
                             "Todos os ficheiros (*.*)|*.*";
                dlg.CheckFileExists = true;

                string actual = ModeloExcel.Caminho;
                if (actual != null)
                {
                    dlg.InitialDirectory = Path.GetDirectoryName(actual);
                    dlg.FileName = Path.GetFileName(actual);
                }

                // OpenFileDialog é CommonDialog, não Form: não passa por ShowModalDialog.
                if (dlg.ShowDialog() != DialogResult.OK) return;

                string erro = ModeloExcel.Definir(dlg.FileName);
                if (erro != null)
                {
                    ed?.WriteMessage("\nModelo não registado: " + erro + "\n");
                    return;
                }
            }

            // A cópia de trabalho é feita no momento da ligação: com o Excel já
            // ligado, trocar o modelo não mudava nada e parecia que o comando
            // não tinha funcionado. Religa-se para o novo modelo entrar.
            if (PaletteHost.Excel.Conectado)
            {
                ed?.WriteMessage("\nA religar o Excel ao vivo sobre o novo modelo…");
                try
                {
                    PaletteHost.Excel.Desconectar();
                    System.Threading.Thread.Sleep(300);
                    PaletteHost.Excel.Conectar(NomeBaseDoDesenho());
                    PaletteHost.RefreshData();

                    ed?.WriteMessage(PaletteHost.Excel.ModoModelo
                        ? "\nExcel ao vivo a usar o novo modelo.\n"
                        : "\nO novo modelo não serviu: " +
                          (PaletteHost.Excel.AvisoModelo ?? "motivo desconhecido") + "\n");
                    return;
                }
                catch (System.Exception ex)
                {
                    ed?.WriteMessage("\nNão foi possível religar o Excel: " + ex.Message +
                                     "\nDesligue e volte a ligar o Excel ao vivo.\n");
                    return;
                }
            }

            ed?.WriteMessage("\nModelo registado. " + ModeloExcel.Resumo() +
                             "\nLigue o Excel ao vivo (TSKEXCEL) para o começar a usar.\n");
        }

        /// <summary>
        /// Tira a marca de "vindo da internet" ao modelo. Sem isto o Excel
        /// abre-o em Vista Protegida: o plugin não lê as folhas e as macros
        /// não correm.
        /// </summary>
        [CommandMethod("TSKMODELODESBLOQUEAR")]
        public void TskModeloDesbloquear()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            string erro = ModeloExcel.Desbloquear();
            ed?.WriteMessage(erro == null
                ? "\nModelo desbloqueado. Volte a ligar o Excel ao vivo.\n"
                : "\n" + erro + "\n");
        }

        /// <summary>
        /// Estado do Excel ao vivo: modo, modelo, colunas detectadas e linhas
        /// que servem de molde. É por aqui que se percebe porque é que a folha
        /// não saiu como se esperava.
        /// </summary>
        [CommandMethod("TSKEXCELDIAG")]
        public void TskExcelDiag()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;

            ed.WriteMessage("\n----- TSK TakeOff · Excel -----\n");
            ed.WriteMessage("DLL: " + PluginInit.VersaoCarregada() + "\n");
            foreach (string linha in PaletteHost.Excel.Diagnostico()
                         .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                ed.WriteMessage(linha + "\n");
            ed.WriteMessage("-------------------------------\n");
        }

        /// <summary>Deixa de usar o modelo e volta à folha simples.</summary>
        [CommandMethod("TSKMODELORESET")]
        public void TskModeloReset()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            ModeloExcel.Esquecer();
            ed?.WriteMessage("\nModelo esquecido. O Excel ao vivo volta à folha simples.\n");
        }

        // ------------------------------------------------------------------
        // Macros do modelo, corridas a partir do AutoCAD.
        // ------------------------------------------------------------------
        [CommandMethod("TSKMD")]
        public void TskMd() => CorrerMacro("CriarMD_v1", "mapa de medições (MD)");

        [CommandMethod("TSKEO")]
        public void TskEo() => CorrerMacro("CriarEO_Final", "estimativa orçamental (EO)");

        [CommandMethod("TSKQT")]
        public void TskQt() => CorrerMacro("CriarQT", "mapa de quantidades (QT)");

        private static void CorrerMacro(string macro, string descricao)
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (!PaletteHost.Excel.Conectado)
            {
                ed?.WriteMessage("\nLigue primeiro o Excel ao vivo (TSKEXCEL).\n");
                return;
            }

            ed?.WriteMessage("\nA gerar o " + descricao + "…");
            string erro = PaletteHost.Excel.CorrerMacro(macro);
            ed?.WriteMessage(erro == null
                ? "\nPronto — o resultado abriu num livro novo no Excel.\n"
                : "\n" + erro + "\n");
        }

        // ------------------------------------------------------------------
        // TSKTEXTO — altura dos rótulos escritos no meio das medições.
        // ------------------------------------------------------------------
        [CommandMethod("TSKTEXTO")]
        public void TskTexto()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed == null) return;

            ed.WriteMessage("\nAltura actual dos rótulos: " +
                (Config.AlturaTexto > 0
                    ? Config.AlturaTexto.ToString("0.####", CultureInfo.CurrentCulture)
                    : "automática (TEXTSIZE do desenho)"));

            var opt = new PromptDoubleOptions(
                "\nNova altura <0 = seguir o TEXTSIZE do desenho>: ")
            {
                AllowNegative = false,
                AllowZero = true,
                DefaultValue = Config.AlturaTexto,
                UseDefaultValue = true
            };

            var res = ed.GetDouble(opt);
            if (res.Status != PromptStatus.OK) return;

            Config.AlturaTexto = res.Value;
            Config.GuardarAlturaTexto();

            ed.WriteMessage(Config.AlturaTexto > 0
                ? "\nRótulos passam a sair com " +
                  Config.AlturaTexto.ToString("0.####", CultureInfo.CurrentCulture) +
                  " de altura.\n"
                : "\nRótulos voltam a seguir o TEXTSIZE de cada desenho.\n");
        }

        // ------------------------------------------------------------------
        // TSKLICENCA — activar ou consultar a licença deste posto.
        // ------------------------------------------------------------------
        [CommandMethod("TSKLICENCA")]
        public void TskLicenca()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage("\n[TSK] Licença: " + Licenca.Resumo());
            ed?.WriteMessage("\n[TSK] Posto: " + Licenca.Maquina + "\n");
            try
            {
                using (var dlg = new LicencaDialog())
                    AcadApp.ShowModalDialog(dlg);
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage("\nNão foi possível abrir a janela da licença: " + ex.Message);
            }
        }

        /// <summary>Apaga a licença deste posto (testes / transferência de máquina).</summary>
        [CommandMethod("TSKLICENCARESET")]
        public void TskLicencaReset()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            var resp = MessageBox.Show(
                "Remover a licença deste posto?\n\n" +
                "As medições não são afectadas — só terá de introduzir o código outra vez.",
                "TSK TakeOff", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (resp != DialogResult.Yes) return;

            Licenca.Limpar();
            ed?.WriteMessage("\nLicença removida deste posto.\n");
        }

        /// <summary>Recria a ribbon (útil após um NETLOAD repetido).</summary>
        [CommandMethod("TSKRIBBON")]
        public void MedRibbon()
        {
            RibbonBuilder.Construir();
        }

        // ------------------------------------------------------------------
        // TSKVAO — vãos à mão: escolhe a medição, depois seleciona os textos
        // ou blocos dos vãos no desenho. Funciona em qualquer convenção.
        // ------------------------------------------------------------------
        [CommandMethod("TSKVAO")]
        public void TskVao()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            var peo = new PromptEntityOptions("\nSelecione a medição (parede, retângulo ou área): ");
            peo.SetRejectMessage("\nTem de ser uma medição criada pelo TSK TakeOff.");
            peo.AddAllowedClass(typeof(Polyline), true);
            // As medições por hachura (TSKAREASEL/TSKMEDSEL) também têm vãos —
            // a grelha da paleta já os aceitava; o comando não podia recusá-las.
            peo.AddAllowedClass(typeof(Hatch), true);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            string handle;
            bool ehFachada;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                handle = ent.Handle.ToString();

                using (var rbF = ent.GetXDataForApplication(FacRepo.AppName))
                using (var rbA = ent.GetXDataForApplication(AlvRepo.AppName))
                {
                    if (rbF == null && rbA == null)
                    {
                        ed.WriteMessage("\nEssa entidade não é uma medição do TSK TakeOff.");
                        return;
                    }
                    ehFachada = rbF != null;
                }
                tr.Commit();
            }

            ed.WriteMessage(ehFachada
                ? "\nAgora selecione os vãos: textos, blocos, ou os próprios " +
                  "rectângulos/polylines (alçado — lê largura e altura do desenho):"
                : "\nAgora selecione os vãos: textos, blocos, ou os próprios " +
                  "rectângulos/polylines (planta — lê a largura do desenho, " +
                  "altura " + Util.N2(Config.AlturaVaoPadrao) + " m a afinar na tabela):");

            var pso = new PromptSelectionOptions
            {
                MessageForAdding = "\nTextos, blocos, rectângulos ou polylines dos vãos"
            };
            var psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            // O tipo da medição decide como se lê a geometria: num alçado o
            // rectângulo é largura × altura, numa planta é largura × espessura
            // da parede e a altura tem de vir daqui. Ver DimensaoVao.DeGeometria.
            var candidatos = VaoDetector.DetectarEmSelecao(
                db, psr.Value.GetObjectIds(), ehFachada, Config.AlturaVaoPadrao);

            if (candidatos.Count == 0)
                ed.WriteMessage("\nNada reconhecido na seleção — escreva os vãos à mão na tabela.");

            var vaos = VaosDialog.Mostrar("Vãos da medição", candidatos, Config.Espessura);
            if (vaos.Count == 0) return;

            if (ehFachada) FacRepo.AdicionarVaos(handle, vaos);
            else AlvRepo.AdicionarVaos(handle, vaos);

            double desconto = 0;
            foreach (var v in vaos) desconto += v.AreaTotal;
            ed.WriteMessage("\n{0} vão(s) adicionado(s): −{1} m².",
                vaos.Count, Util.N2(desconto));

            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Procura rótulos/blocos de vãos na zona da parede sem deixar uma
        /// falha acessória desfazer a medição. É chamado antes de criar a
        /// etiqueta da própria parede, para essa etiqueta não entrar na busca.
        /// </summary>
        private static List<Vao> DetectarEConfirmarVaosDaParede(Database db, Editor ed,
            Extents3d zona, double espessuraParede)
        {
            List<VaoCandidato> candidatos;
            try
            {
                candidatos = VaoDetector.Detectar(db, zona);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nNão foi possível procurar vãos automaticamente: " +
                    ex.Message);
                return new List<Vao>();
            }

            if (candidatos.Count == 0)
            {
                ed.WriteMessage("\nNenhum vão detetado automaticamente nesta parede.");
                return new List<Vao>();
            }

            ed.WriteMessage("\n{0} candidato(s) a vão encontrado(s). Confirme na tabela.",
                candidatos.Count);

            var vaos = VaosDialog.Mostrar("Vãos detetados — confirme esta parede",
                candidatos, espessuraParede);
            if (vaos.Count == 0)
                ed.WriteMessage("\nA parede será guardada sem vãos.");

            return vaos;
        }

        private static void EscreverResumoVaos(Editor ed, IList<Vao> vaos)
        {
            if (vaos == null || vaos.Count == 0) return;
            double desconto = 0;
            foreach (var vao in vaos) desconto += vao.AreaTotal;
            ed.WriteMessage("\n{0} vão(s) gravado(s): −{1} m².",
                vaos.Count, Util.N2(desconto));
        }

        /// <summary>Extensão WCS dos pontos recolhidos no UCS corrente.</summary>
        private static Extents3d ZonaWcs(IList<Point3d> pontosUcs, Matrix3d ucs)
        {
            if (pontosUcs == null || pontosUcs.Count == 0)
                throw new ArgumentException("A parede não tem pontos.", "pontosUcs");

            Point3d primeiro = pontosUcs[0].TransformBy(ucs);
            var zona = new Extents3d(primeiro, primeiro);
            for (int i = 1; i < pontosUcs.Count; i++)
                zona.AddPoint(pontosUcs[i].TransformBy(ucs));
            return zona;
        }

        // ------------------------------------------------------------------
        // TSKVAODIAG — mostra o que o detector lê numa seleção e por que
        // aceita ou rejeita cada texto. Serve para afinar em obras novas.
        // ------------------------------------------------------------------
        [CommandMethod("TSKVAODIAG")]
        public void TskVaoDiag()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            ed.WriteMessage("\nSelecione textos, blocos, rectângulos ou polylines para diagnóstico:");
            var psr = ed.GetSelection();
            if (psr.Status != PromptStatus.OK) return;

            var ids = psr.Value.GetObjectIds();

            // Sem medição escolhida não se sabe se é planta ou alçado. Mostram-se
            // as duas leituras: é para isso que o diagnóstico serve — ver o que
            // o detector faria antes de o deixar mexer na medição.
            foreach (bool alcado in new[] { false, true })
            {
                var lidos = VaoDetector.DetectarEmSelecao(
                    doc.Database, ids, alcado, Config.AlturaVaoPadrao);

                ed.WriteMessage("\n--- Como {0} ---", alcado ? "ALÇADO (fachada)" : "PLANTA (alvenaria)");
                bool algum = false;
                foreach (var c in lidos)
                {
                    if (c.Fonte != "Rectângulo" && c.Fonte != "Polyline" &&
                        c.Fonte != "Hachura" && c.Fonte != "Linha") continue;
                    algum = true;
                    ed.WriteMessage("\n  [{0}] {1} → {2} × {3} m",
                        c.Fonte, c.TextoOriginal, Util.N2(c.LarguraM), Util.N2(c.AlturaM));
                }
                if (!algum) ed.WriteMessage("\n  (nenhuma geometria plausível)");
            }

            // A altura TEM de ir: sem ela a leitura em planta dá altura zero,
            // que é recusada por implausível, e a geometria toda desaparecia
            // do diagnóstico com um "nenhuma dimensão reconhecida" a mentir.
            var candidatos = VaoDetector.DetectarEmSelecao(
                doc.Database, ids, false, Config.AlturaVaoPadrao);

            ed.WriteMessage("\n--- Diagnóstico ({0} entidade(s) selecionada(s)) ---", ids.Length);
            if (candidatos.Count == 0)
            {
                ed.WriteMessage("\nNenhuma dimensão plausível reconhecida.");
                ed.WriteMessage("\nFormatos aceites: 1190x2350 (mm), 200X160 (cm), " +
                                "2,67x2,62 (m). Limites: larg 0,30–8,00 m / alt 0,30–5,00 m.");
                ed.WriteMessage("\nEm geometria: rectângulo, polyline fechada, " +
                                "hachura ou linha, dentro dos mesmos limites.");
            }
            foreach (var c in candidatos)
            {
                ed.WriteMessage("\n[{0}] \"{1}\" → {2} × {3} m (lido em {4}) — {5}",
                    c.Designacao, c.TextoOriginal,
                    Util.N2(c.LarguraM), Util.N2(c.AlturaM), c.Unidade,
                    c.TipoSugerido == TipoVao.Janela ? "janela" : "porta");
            }
            ed.WriteMessage("\n");
        }

        // ------------------------------------------------------------------
        // TSKONDE — diz onde estão as medições e porque podem não se ver:
        // espaço, layer, estado do layer e coordenadas. Faz zoom no fim.
        // ------------------------------------------------------------------
        /// <summary>
        /// Liga/desliga o relatório de tempos. Por omissão só aparece quando a
        /// actualização passa de 250 ms; com isto aparece sempre, para se poder
        /// comparar um desenho pequeno com um grande.
        /// </summary>
        [CommandMethod("TSKTEMPO")]
        public void Tempo()
        {
            Cronometro.Sempre = !Cronometro.Sempre;
            PaletteHost.Log(Cronometro.Sempre
                ? "Relatório de tempos LIGADO — cada actualização diz onde gastou o tempo."
                : "Relatório de tempos desligado (só aparece acima de "
                  + Cronometro.LimiarMs + " ms).");
        }

        [CommandMethod("TSKONDE")]
        public void TskOnde()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\n--- Onde estão as medições ---");
            ed.WriteMessage("\nEspaço activo: {0}   (as medições são sempre criadas no Model Space)",
                db.TileMode ? "MODEL" : "LAYOUT/PAPER");
            if (!db.TileMode)
                ed.WriteMessage("\n>> Estás num LAYOUT. Passa para o separador Model para as ver.");

            var min = new Point3d(double.MaxValue, double.MaxValue, 0);
            var max = new Point3d(double.MinValue, double.MinValue, 0);
            int total = 0;

            // Fora do Model só deviam existir medições feitas por versões
            // anteriores, quando o plugin escrevia no espaço corrente. Se
            // aparecer alguma, tem de se dizer onde está — senão o utilizador
            // procura-a no Model, não a encontra, e dá o trabalho por perdido.
            int forasteiras = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var modelId = Util.EspacoMedicoesId(db);

                foreach (ObjectId btrId in bt)
                {
                    var espaco = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (!espaco.IsLayout) continue;          // blocos normais não interessam

                    bool ehModel = btrId == modelId;
                    string nomeEspaco = "MODEL";
                    if (!ehModel)
                    {
                        try
                        {
                            var lay = (Layout)tr.GetObject(espaco.LayoutId, OpenMode.ForRead);
                            nomeEspaco = "LAYOUT «" + lay.LayoutName + "»";
                        }
                        catch { nomeEspaco = "LAYOUT (?)"; }
                    }

                    foreach (ObjectId id in espaco)
                    {
                        var ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string tipo = Classificar(ent);
                        if (tipo == null) continue;

                        total++;
                        if (!ehModel) forasteiras++;

                        Extents3d ext;
                        try { ext = ent.GeometricExtents; }
                        catch { continue; }               // entidade degenerada: sem extents

                        // O zoom final só faz sentido sobre o Model: juntar as
                        // coordenadas de um layout arrastaria a vista para o
                        // sítio errado, porque são sistemas independentes.
                        if (ehModel)
                        {
                            min = new Point3d(Math.Min(min.X, ext.MinPoint.X),
                                              Math.Min(min.Y, ext.MinPoint.Y), 0);
                            max = new Point3d(Math.Max(max.X, ext.MaxPoint.X),
                                              Math.Max(max.Y, ext.MaxPoint.Y), 0);
                        }

                        string estado = "visível";
                        if (lt.Has(ent.Layer))
                        {
                            var ltr = (LayerTableRecord)tr.GetObject(lt[ent.Layer], OpenMode.ForRead);
                            var problemas = new List<string>();
                            if (ltr.IsOff) problemas.Add("DESLIGADO");
                            if (ltr.IsFrozen) problemas.Add("CONGELADO");
                            if (ltr.IsLocked) problemas.Add("bloqueado");
                            if (problemas.Count > 0) estado = string.Join(" + ", problemas);
                        }

                        ed.WriteMessage("\n[{0}] {1} | {2} | layer {3} ({4}) | X {5} a {6} | Y {7} a {8}",
                            ent.Handle, tipo, nomeEspaco, ent.Layer, estado,
                            Util.N2(ext.MinPoint.X), Util.N2(ext.MaxPoint.X),
                            Util.N2(ext.MinPoint.Y), Util.N2(ext.MaxPoint.Y));
                    }
                }
                tr.Commit();
            }

            if (total == 0)
            {
                ed.WriteMessage("\nNão há nenhuma medição neste desenho.\n");
                return;
            }

            if (forasteiras > 0)
            {
                ed.WriteMessage(
                    "\n\n>> ATENÇÃO: {0} medição(ões) estão num LAYOUT, não no Model Space.",
                    forasteiras);
                ed.WriteMessage(
                    "\n>> Foram feitas por uma versão anterior do plugin. O painel e a " +
                    "exportação só lêem o Model Space, por isso estas NÃO entram na folha.");
                ed.WriteMessage(
                    "\n>> Para as recuperar: selecciona-as no layout, CTRL+X, muda para o " +
                    "separador Model e cola com PASTECLIP no sítio certo.\n");
            }

            ed.WriteMessage("\nTotal: {0} medição(ões).\n", total);

            // Todas as medições estão em layouts: não há nada no Model para
            // enquadrar, e um zoom com os extents por preencher levaria a
            // vista para o infinito.
            if (min.X == double.MaxValue)
                return;

            ed.WriteMessage("\nA fazer zoom ao Model Space…\n");
            try
            {
                double margem = Math.Max(1.0, (max.X - min.X) * 0.15);
                var vista = new Autodesk.AutoCAD.DatabaseServices.ViewTableRecord();
                var centro = new Point2d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);
                vista.CenterPoint = centro;
                vista.Width = Math.Max(1.0, (max.X - min.X) + 2 * margem);
                vista.Height = Math.Max(1.0, (max.Y - min.Y) + 2 * margem);
                ed.SetCurrentView(vista);
                ed.Regen();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nZoom falhou: " + ex.Message);
            }
        }

        /// <summary>
        /// Que tipo de medição é esta entidade, ou <c>null</c> se não for uma.
        ///
        /// A marca é a XData: cada família de medição regista-se com um nome
        /// de aplicação próprio. Verificar a XData em vez do layer é o que faz
        /// o diagnóstico continuar a funcionar depois de alguém renomear
        /// layers — que é exactamente quando se precisa dele.
        /// </summary>
        private static string Classificar(Entity ent)
        {
            foreach (var par in Marcas)
            {
                try
                {
                    using (var rb = ent.GetXDataForApplication(par.Key))
                        if (rb != null) return par.Value;
                }
                catch { /* XData ilegível: tenta a marca seguinte */ }
            }
            return null;
        }

        private static readonly List<KeyValuePair<string, string>> Marcas =
            new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(AlvRepo.AppName, "ALVENARIA"),
                new KeyValuePair<string, string>(FacRepo.AppName, "MATERIAL"),
                new KeyValuePair<string, string>(AppNameLinear,   "LINEAR"),
                new KeyValuePair<string, string>(ContRepo.AppName, "CONTAGEM"),
            };

        // ------------------------------------------------------------------
        // Aliases TSK* — nomes oficiais do produto. Os comandos MED* antigos
        // continuam a funcionar para não quebrar o hábito de quem já usa.
        // ------------------------------------------------------------------
        [CommandMethod("TSKPAREDE")]
        public void TskParede() => MedParede();

        [CommandMethod("TSKPAREDER")]
        public void TskParedeRetAlias() => TskParedeRet();

        [CommandMethod("TSKRET")]
        public void TskRet() => MedRet();

        [CommandMethod("TSKPOLF")]
        public void TskPolF() => MedPolFachada();

        [CommandMethod("TSKLINEAR")]
        public void TskLinear() => Medir();

        [CommandMethod("TSKEXCEL")]
        public void TskExcel() => MedExcelLive();

        [CommandMethod("TSKEXPORT")]
        public void TskExport() => MedExport();

        [CommandMethod("TSKEXPORTBC3")]
        public void TskExportBc3() => Util.Seguro("TSKEXPORTBC3", MedExportBc3);

        // ------------------------------------------------------------------
        // MEDPAREDE — mede uma parede de alvenaria usando serviço/altura
        // configurados na paleta. Clica os pontos, Enter finaliza.
        // ------------------------------------------------------------------
        [CommandMethod("MEDPAREDE")]
        public void MedParede() => Util.Seguro("MEDPAREDE", MedParedeImpl);

        private void MedParedeImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;   // ver e exportar continuam livres
            Util.AvisarSeForaDoModel(doc);
            var db = doc.Database;
            var ed = doc.Editor;

            string layerName = LayerPrefix + Util.NomeLayer(Config.LayerEfectiva());
            ed.WriteMessage("\nMedindo {0} | altura {1} m | layer {2}",
                Config.Servico, Util.N2(Config.Altura), layerName);

            var pts = CollectPoints(ed);
            if (pts == null) return;

            string handleParede = null;
            var ucs = ed.CurrentUserCoordinateSystem;
            var zonaParede = ZonaWcs(pts, ucs);
            var vaosParede = DetectarEConfirmarVaosDaParede(
                db, ed, zonaParede, Config.Espessura);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, AlvRepo.AppName);
                Util.EnsureLayer(tr, db, layerName, colorIndex: 1 /* vermelho */);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                var pl = new Polyline();
                for (int i = 0; i < pts.Count; i++)
                    pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                pl.Layer = layerName;
                Util.AplicarEspessura(pl);

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                pl.TransformBy(ucs);            // UCS -> WCS

                var corPiso = FachadaConfig.CorDoPiso(Config.Piso);
                try { pl.Color = Color.FromRgb(corPiso.R, corPiso.G, corPiso.B); } catch { }

                var parede = new Parede
                {
                    Servico = Config.Servico,
                    Piso = Config.Piso,
                    Bloco = Config.Bloco,
                    // O bloco também etiqueta a linha: "WC1", "quarto 2".
                    Nota = FolhaMedicao.NotaInicial(null, Config.Bloco),
                    Alcado = Config.Alcado,
                    Altura = Config.Altura,
                    Espessura = Config.Espessura,
                    // O artigo escolhido no painel acompanha a medição. Vazio
                    // quando não há mapa importado — e aí tudo se comporta
                    // como antes de o TSKMQT existir.
                    Artigo = MapaQuantidades.ArtigoParaMedicao(Config.Servico),
                    Separador = Config.ConsumirSeparador()
                };
                parede.Vaos.AddRange(vaosParede);
                AlvRepo.GravarXData(pl, parede);

                double comp = pl.Length;
                double area = comp * Config.Altura;
                double volume = area * Config.Espessura;

                // Rótulo no meio do traçado (mesmo formato do retângulo)
                double textHeight = Util.AlturaTexto(db);
                var mid = pl.GetPointAtDist(comp / 2.0);
                var label = new MText
                {
                    Location = mid,
                    Contents = string.Format("{0}\\P{1} × {2} = {3} m²",
                        Config.Servico, Util.N2(comp),
                        Util.N2(Config.Altura), Util.N2(area)),
                    TextHeight = textHeight,
                    Layer = layerName
                };
                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
                try { label.TransformBy(ucs); } catch { }
                try { label.Color = Util.CorTexto; } catch { }

                // Ler ANTES do Commit: depois do commit a entidade está fechada
                // e o acesso lança exceção, o que faz o AutoCAD desfazer o comando.
                handleParede = pl.Handle.ToString();
                tr.Commit();

                ed.WriteMessage("\nParede criada: {0} m lineares, {1} m² bruta.",
                    Util.N2(comp), Util.N2(area));
            }

            // É esta a "última medição" para o Adicionar vão e para os títulos.
            PaletteHost.RegistarMedicao(handleParede);
            EscreverResumoVaos(ed, vaosParede);
            PaletteHost.RefreshData();
        }

        // ------------------------------------------------------------------
        // TSKAREA — área desenhada em planta × altura do painel.
        //
        // Nasceu das camadas: uma betonilha de enchimento é a área do pavimento
        // vezes a espessura, e dá m³. Nem o comprimento de uma polyline nem o
        // rectângulo lá chegavam — o contorno de um piso não tem comprimento
        // que signifique nada — e quem media ia calcular a área à parte e
        // escrevê-la à mão na folha, que é exactamente o que este plugin
        // existe para não ser preciso.
        //
        // Clica-se o contorno, Enter fecha. Sai a polyline fechada, o
        // preenchimento na cor do piso e a medição área × altura.
        // ------------------------------------------------------------------
        [CommandMethod("TSKAREA")]
        public void TskArea() => Util.Seguro("TSKAREA", TskAreaImpl);

        private void TskAreaImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;   // ver e exportar continuam livres
            Util.AvisarSeForaDoModel(doc);
            var db = doc.Database;
            var ed = doc.Editor;

            string layerName = LayerPrefix + Util.NomeLayer(Config.LayerEfectiva());
            ed.WriteMessage(
                "\nÁrea {0} | altura/espessura {1} m | layer {2}" +
                "\nClique o contorno da área; Enter fecha-o.",
                Config.Servico, Util.N2(Config.Altura), layerName);

            var pts = CollectPoints(ed);
            if (pts == null) return;

            // Três pontos é o mínimo que fecha alguma coisa. Com dois, a
            // polyline fechada é um segmento ida e volta e a área dá zero —
            // saía uma medição a zero sem nada que o explicasse.
            if (pts.Count < 3)
            {
                ed.WriteMessage("\nCancelado: uma área precisa de pelo menos 3 pontos.");
                return;
            }

            var corPiso = FachadaConfig.CorDoPiso(Config.Piso);
            var ucs = ed.CurrentUserCoordinateSystem;
            double area = 0;
            bool comHatch = false;
            string handleCriado = null;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, AlvRepo.AppName);
                Util.EnsureLayer(tr, db, layerName, colorIndex: 1);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                var pl = new Polyline();
                for (int i = 0; i < pts.Count; i++)
                    pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                pl.Closed = true;               // é o que dá área à polyline
                pl.Elevation = pts[0].Z;
                pl.Layer = layerName;
                Util.AplicarEspessura(pl);

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                pl.TransformBy(ucs);            // UCS -> WCS
                try { pl.Color = Color.FromRgb(corPiso.R, corPiso.G, corPiso.B); } catch { }

                try { area = Math.Abs(pl.Area); } catch { }

                AlvRepo.GravarXData(pl, new Parede
                {
                    Servico = Config.Servico,
                    Piso = Config.Piso,
                    Bloco = Config.Bloco,
                    // O bloco também etiqueta a linha: "WC1", "quarto 2".
                    Nota = FolhaMedicao.NotaInicial(null, Config.Bloco),
                    Alcado = Config.Alcado,
                    Altura = Config.Altura,
                    // Espessura a zero de propósito: a altura JÁ é a espessura
                    // da camada. Deixar a do painel entrar aqui punha a coluna
                    // Volume a mostrar área × altura × espessura, que não é
                    // grandeza nenhuma.
                    Espessura = 0,
                    AreaVezesAltura = true,
                    Artigo = MapaQuantidades.ArtigoParaMedicao(Config.Servico),
                    Separador = Config.ConsumirSeparador()
                });

                comHatch = CriarHatchGradiente(tr, ms, pl, corPiso);

                double textHeight = Util.AlturaTexto(db);
                var label = new MText
                {
                    Location = CentroDe(pts),
                    Contents = string.Format("{0}\\P{1} m² × {2} = {3} m³",
                        Config.Servico, Util.N2(area),
                        Util.N2(Config.Altura), Util.N2(area * Config.Altura)),
                    TextHeight = textHeight,
                    Attachment = AttachmentPoint.MiddleCenter,
                    Layer = layerName
                };
                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
                try { label.TransformBy(ucs); } catch { }
                try { label.Color = Util.CorTexto; } catch { }

                // Ler ANTES do Commit (ver nota em TSKPAREDE)
                handleCriado = pl.Handle.ToString();

                tr.Commit();
            }

            ed.WriteMessage("\nÁrea {0} criada{1}: {2} m² × {3} m = {4} m³.",
                Config.Servico, comHatch ? " com preenchimento" : " SEM preenchimento",
                Util.N2(area), Util.N2(Config.Altura), Util.N2(area * Config.Altura));

            try { ed.Regen(); } catch { }

            PaletteHost.RegistarMedicao(handleCriado);
            PaletteHost.RefreshData();
        }

        // ------------------------------------------------------------------
        // TSKAREASEL — mede áreas que JÁ existem no desenho.
        //
        // O projecto de arquitectura já traz os pavimentos hachurados e os
        // compartimentos fechados por polyline. Obrigar a redesenhar o contorno
        // por cima do que já lá está é trabalho a dobrar — e trabalho a dobrar
        // com hipótese de sair diferente do original, que é pior do que ser só
        // demorado.
        //
        // Como o TSKMEDSEL, mede sobre CÓPIAS na layer das medições: o desenho
        // do arquitecto não se toca. A hachura copiada perde a associatividade
        // de propósito — ligada ao contorno original, uma alteração dele mudava
        // a medição sozinha e ninguém dava por isso.
        // ------------------------------------------------------------------
        [CommandMethod("TSKAREASEL", CommandFlags.UsePickSet | CommandFlags.Redraw)]
        public void TskAreaSel() => Util.Seguro("TSKAREASEL", TskAreaSelImpl);

        private void TskAreaSelImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;
            Util.AvisarSeForaDoModel(doc);
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage(
                "\nÁrea por selecção — {0} | altura/espessura {1} m." +
                "\nSeleccione hachuras ou polylines fechadas já existentes.",
                Config.Servico, Util.N2(Config.Altura));

            // Só o que tem área. Uma linha ou um texto apanhados por um laço
            // largo davam medições a zero, e uma medição a zero na folha é
            // pior do que não existir: ocupa uma linha e não se sabe de quê.
            var filtro = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Operator, "<OR"),
                new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                new TypedValue((int)DxfCode.Start, "HATCH"),
                new TypedValue((int)DxfCode.Operator, "OR>")
            });

            var sel = ed.GetSelection(
                new PromptSelectionOptions
                {
                    MessageForAdding = "\nSeleccione as áreas a medir: "
                }, filtro);
            if (sel.Status != PromptStatus.OK) return;

            string layerName = LayerPrefix + Util.NomeLayer(Config.LayerEfectiva());
            string artigoAlvo = MapaQuantidades.ArtigoParaMedicao(Config.Servico);
            string handleCriado = null;
            var corPiso = FachadaConfig.CorDoPiso(Config.Piso);
            int feitas = 0, ignoradas = 0, comPreenchimento = 0;
            double areaTotal = 0;

            // A medição sai UMA só, com a soma de todas as áreas escolhidas.
            // A primeira cópia leva a XData; as outras entram nela como área
            // extra e ficam desenhadas na mesma.
            Entity portadora = null;
            double areaPortadora = 0;
            var centro = Point3d.Origin;
            bool temCentro = false;
            string origemPortadora = null;

            // As cópias das medições vivem na nossa layer; a geometria original
            // fica intacta e, por isso, não tem XData a dizer que já foi medida.
            // Indexamos a origem guardada nas cópias para bloquear apenas
            // origem + mesmo artigo. Outro artigo continua permitido.
            var medidasPorOrigem = new Dictionary<string, List<Parede>>(
                StringComparer.OrdinalIgnoreCase);
            var origensAceites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, AlvRepo.AppName);
                Util.EnsureLayer(tr, db, layerName, colorIndex: 1);

                Util.EnsureRegApp(tr, db, AppNameAreaOrigem);
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);
                double textHeight = Util.AlturaTexto(db);

                // Ler as medições existentes antes de processar a selecção.
                // Sem isto, voltar a seleccionar a polyline original — que não
                // foi tocada — criava uma segunda medição igual.
                foreach (ObjectId id in ms)
                {
                    var medida = tr.GetObject(id, OpenMode.ForRead) as Entity;
                    if (medida == null) continue;

                    Parede p = null;
                    var medidaPl = medida as Polyline;
                    var medidaHatch = medida as Hatch;
                    if (medidaPl != null) p = AlvRepo.LerParede(medidaPl);
                    else if (medidaHatch != null) p = AlvRepo.LerParedeDeHatch(medidaHatch);
                    if (p != null)
                        RegistarOrigens(medidasPorOrigem, p, medida.Handle.ToString());
                    else
                    {
                        var marcador = LerMarcadorArea(medida);
                        if (marcador != null)
                            RegistarOrigens(medidasPorOrigem, marcador,
                                medida.Handle.ToString());
                    }
                }

                foreach (SelectedObject so in sel.Value)
                {
                    if (so == null) continue;
                    var ent = tr.GetObject(so.ObjectId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;

                    // A mesma geometria pode ser medida para mais do que um
                    // artigo (por exemplo, camada e revestimento). O XData
                    // sozinho só diz "isto já é nosso"; é preciso ler o artigo
                    // guardado para distinguir duplicação da nova medição
                    // legítima.
                    Parede existente = null;
                    bool jaNossa = false;
                    try
                    {
                        using (var rb = ent.GetXDataForApplication(AlvRepo.AppName))
                            jaNossa = rb != null;
                    }
                    catch { }

                    if (!jaNossa)
                    {
                        existente = LerMarcadorArea(ent);
                        jaNossa = existente != null;
                    }

                    string origem = ent.Handle.ToString();
                    if (jaNossa)
                    {
                        if (existente == null)
                        {
                            var plExistente = ent as Polyline;
                            var hatchExistente = ent as Hatch;
                            existente = plExistente != null
                                ? AlvRepo.LerParede(plExistente)
                                : (hatchExistente != null
                                    ? AlvRepo.LerParedeDeHatch(hatchExistente)
                                    : null);
                        }

                        if (existente == null)
                        {
                            // XData ilegível: não se arrisca duplicar uma
                            // medição que pode já ser nossa.
                            ignoradas++;
                            continue;
                        }

                        if (!string.IsNullOrEmpty(existente.HandleOrigem))
                            origem = existente.HandleOrigem;

                        // A cópia de uma medição já existente pode ser usada
                        // para outro artigo; a origem original mantém a
                        // identidade contra duplicações futuras.
                    }

                    if (TemMedicaoDoMesmoArtigo(medidasPorOrigem, origem,
                            artigoAlvo, Config.Servico) ||
                        origensAceites.Contains(origem))
                    {
                        ignoradas++;
                        continue;
                    }

                    Entity copia;
                    try { copia = ent.Clone() as Entity; }
                    catch { copia = null; }
                    if (copia == null) { ignoradas++; continue; }

                    copia.Layer = layerName;
                    ms.AppendEntity(copia);
                    tr.AddNewlyCreatedDBObject(copia, true);

                    var hatch = copia as Hatch;
                    var pl = copia as Polyline;

                    double area = 0;
                    if (hatch != null)
                    {
                        // Cortar o cordão ao original ANTES de ler a área: uma
                        // hachura associativa reavalia-se e o número podia
                        // mudar debaixo dos pés.
                        try { hatch.Associative = false; } catch { }
                        try { area = Math.Abs(hatch.Area); } catch { }

                        // E vestir a cópia com o gradiente da cor do piso.
                        //
                        // Ela nascia com o padrão do desenho original — as
                        // mesmas linhas cinzentas do arquitecto — e ficava
                        // pousada exactamente por cima dele. Não havia nada
                        // que dissesse que aquela área tinha sido medida: era
                        // igual ao que já lá estava.
                        try
                        {
                            hatch.HatchObjectType = HatchObjectType.GradientObject;
                            hatch.SetGradient(GradientPatternType.PreDefinedGradient, "LINEAR");
                            try { hatch.GradientOneColorMode = false; } catch { }
                            hatch.SetGradientColors(new[]
                            {
                                new GradientColor(
                                    Color.FromRgb(corPiso.R, corPiso.G, corPiso.B), 0f),
                                new GradientColor(Color.FromRgb(255, 255, 255), 1f)
                            });
                            hatch.GradientAngle = Math.PI / 2.0;
                            hatch.EvaluateHatch(true);
                        }
                        catch (System.Exception ex)
                        {
                            PaletteHost.Log("hachura copiada sem gradiente — " + ex.Message);
                        }
                    }
                    else if (pl != null)
                    {
                        // Um contorno aberto não tem área nenhuma que se defenda.
                        // O AutoCAD devolve a da figura fechada à força, e ela
                        // pode não ser nada do que se vê no ecrã.
                        if (!pl.Closed)
                        {
                            PaletteHost.Log("polyline aberta na layer " + ent.Layer +
                                            ": ignorada. Feche-a e volte a medir.");
                            try { copia.Erase(); } catch { }
                            ignoradas++;
                            continue;
                        }
                        Util.AplicarEspessura(pl);
                        try { area = Math.Abs(pl.Area); } catch { }
                    }

                    if (area <= 1e-9)
                    {
                        try { copia.Erase(); } catch { }
                        ignoradas++;
                        continue;
                    }

                    try { copia.Color = Color.FromRgb(corPiso.R, corPiso.G, corPiso.B); }
                    catch { }
                    try
                    {
                        copia.Transparency =
                            new Autodesk.AutoCAD.Colors.Transparency(
                                (byte)Config.TransparenciaHatch);
                    }
                    catch { }

                    // Uma polyline apanhada do projecto é só um contorno: sem
                    // preenchimento não se vê o que já foi medido, que é metade
                    // da razão de o plugin desenhar o que desenha. A hachura
                    // seleccionada já traz o seu, e conta como preenchida.
                    if (hatch != null) comPreenchimento++;
                    else if (pl != null && CriarHatchGradiente(tr, ms, pl, corPiso))
                        comPreenchimento++;

                    // As cópias secundárias não são medições independentes:
                    // só levam um marcador técnico. Gravar CASQUILHO_ALV em
                    // todas faria o leitor da folha somar cada cópia além da
                    // área agregada da portadora.
                    GravarMarcadorArea(copia, origem, artigoAlvo,
                        Config.Servico);

                    areaTotal += area;
                    feitas++;
                    origensAceites.Add(origem);

                    // A PRIMEIRA leva a medição; as outras entram como área
                    // extra dela. Escolher várias áreas é dizer "isto é tudo a
                    // mesma coisa" — sai UMA linha na folha com o total, e não
                    // uma linha por clique que depois alguém tem de somar à
                    // mão. Todas ficam desenhadas: a marca do que foi medido é
                    // o desenho inteiro.
                    if (portadora != null) continue;

                    portadora = copia;
                    origemPortadora = origem;
                    areaPortadora = area;

                    // O rótulo do total fica sobre a PRIMEIRA área, que é a que
                    // leva a medição. A cada uma dava o centro da última, e o
                    // texto acabava pousado numa área qualquer sem relação com
                    // o número que mostra.
                    try
                    {
                        var cx = copia.GeometricExtents;
                        centro = new Point3d(
                            (cx.MinPoint.X + cx.MaxPoint.X) / 2.0,
                            (cx.MinPoint.Y + cx.MaxPoint.Y) / 2.0, 0);
                        temCentro = true;
                    }
                    catch { }
                }

                if (portadora != null)
                {
                    AlvRepo.GravarXData(portadora, new Parede
                    {
                        Servico = Config.Servico,
                        Piso = Config.Piso,
                        Bloco = Config.Bloco,
                        // O bloco também etiqueta a linha: "WC1", "quarto 2".
                        Nota = FolhaMedicao.NotaInicial(null, Config.Bloco),
                        Alcado = Config.Alcado,
                        Altura = Config.Altura,
                        Espessura = 0,          // a altura JÁ é a espessura da camada
                        AreaVezesAltura = true,
                        // O que as OUTRAS áreas somam. A da portadora sai da
                        // geometria dela na leitura; esta é a parcela que falta.
                        ComprimentoExtra = areaTotal - areaPortadora,
                        Artigo = artigoAlvo,
                        // A portadora só precisa da sua própria origem. As
                        // restantes ficam nos marcadores das cópias secundárias;
                        // assim nenhum texto de XData cresce sem limite.
                        HandleOrigem = origemPortadora ?? "",
                        Separador = Config.ConsumirSeparador()
                    });

                    try
                    {
                        var label = new MText
                        {
                            Location = temCentro ? centro : Point3d.Origin,
                            Contents = string.Format("{0}\\P{1} m² × {2} = {3} m³",
                                Config.Servico, Util.N2(areaTotal),
                                Util.N2(Config.Altura), Util.N2(areaTotal * Config.Altura)),
                            TextHeight = textHeight,
                            Attachment = AttachmentPoint.MiddleCenter,
                            Layer = layerName
                        };
                        ms.AppendEntity(label);
                        tr.AddNewlyCreatedDBObject(label, true);
                        try { label.Color = Util.CorTexto; } catch { }
                    }
                    catch { /* sem rótulo; a medição vale na mesma */ }

                    // Ler ANTES do Commit (ver nota em TSKPAREDE). Sai UMA
                    // medição — a portadora — por isso há mesmo "a última".
                    handleCriado = portadora.Handle.ToString();
                }

                tr.Commit();
            }

            if (feitas == 0)
            {
                ed.WriteMessage("\nNenhuma área medida.");
            }
            else
            {
                ed.WriteMessage(
                    "\n{0} área(s) numa medição só: {1} m² × {2} m = {3} m³.",
                    feitas, Util.N2(areaTotal), Util.N2(Config.Altura),
                    Util.N2(areaTotal * Config.Altura));
                if (feitas > comPreenchimento && comPreenchimento > 0)
                    ed.WriteMessage("\n{0} sem preenchimento.", feitas - comPreenchimento);
            }
            if (ignoradas > 0)
                ed.WriteMessage("\n{0} objecto(s) ignorado(s) — abertos, sem área, " +
                                "ou já medidos.", ignoradas);

            try { ed.Regen(); } catch { }

            PaletteHost.RegistarMedicao(handleCriado);
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Centro da nuvem de pontos do contorno, para lá pousar o rótulo.
        ///
        /// A média dos vértices e não o centro da caixa envolvente: num «L»,
        /// o centro da caixa cai fora da área e o texto ficava a flutuar ao
        /// lado do que descreve.
        /// </summary>
        private static void GravarMarcadorArea(Entity entidade,
            string origem, string artigo, string servico)
        {
            if (entidade == null) return;
            entidade.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,
                    AppNameAreaOrigem),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    origem ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    artigo ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    servico ?? ""));
        }

        private static Parede LerMarcadorArea(Entity entidade)
        {
            if (entidade == null) return null;
            try
            {
                using (var rb = entidade.GetXDataForApplication(AppNameAreaOrigem))
                {
                    if (rb == null) return null;
                    var p = new Parede();
                    int i = 0;
                    foreach (TypedValue tv in rb)
                    {
                        if (tv.TypeCode != (int)DxfCode.ExtendedDataAsciiString)
                            continue;
                        string s = Convert.ToString(tv.Value) ?? "";
                        if (i == 0) p.HandleOrigem = s;
                        else if (i == 1) p.Artigo = s;
                        else if (i == 2) p.Servico = s;
                        i++;
                    }
                    return i >= 3 && p.HandleOrigem.Length > 0 ? p : null;
                }
            }
            catch { return null; }
        }

        private static void RegistarOrigens(
            Dictionary<string, List<Parede>> porOrigem,
            Parede parede, string origemFallback)
        {
            if (parede == null) return;
            string texto = parede.HandleOrigem;
            if (string.IsNullOrEmpty(texto)) texto = origemFallback;
            if (string.IsNullOrEmpty(texto)) return;

            foreach (string origem in texto.Split(new[] { ';' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                List<Parede> lista;
                if (!porOrigem.TryGetValue(origem, out lista))
                {
                    lista = new List<Parede>();
                    porOrigem[origem] = lista;
                }
                lista.Add(parede);
            }
        }

        private static bool TemMedicaoDoMesmoArtigo(
            Dictionary<string, List<Parede>> porOrigem,
            string origem, string artigoAlvo, string servicoAlvo)
        {
            if (string.IsNullOrEmpty(origem)) return false;
            List<Parede> existentes;
            if (!porOrigem.TryGetValue(origem, out existentes)) return false;

            foreach (var existente in existentes)
                if (ArtigoDaMesmaMedicao(existente, artigoAlvo, servicoAlvo))
                    return true;
            return false;
        }

        /// <summary>
        /// Verifica se uma medição existente já é do artigo/serviço corrente.
        /// Artigos longos podem aparecer cortados de formas diferentes na
        /// entidade e no mapa; Compativel aplica o limite total da XData antes
        /// de comparar. Sem artigo, o serviço é a única identidade disponível.
        /// </summary>
        private static bool ArtigoDaMesmaMedicao(Parede existente,
            string artigoAlvo, string servicoAlvo)
        {
            if (existente == null) return true;

            string antigo = existente.Artigo ?? "";
            string actual = artigoAlvo ?? "";
            if (antigo.Length > 0 && actual.Length > 0)
                return ChaveArtigo.Compativel(antigo, actual);

            // Se ambas as versões não têm artigo (mapa não importado, ou
            // desenho antigo), o serviço é a única identidade fiável que
            // resta. Se só uma tem artigo, são artigos diferentes e a nova
            // classificação deve ser permitida.
            if (antigo.Length == 0 && actual.Length == 0)
                return string.Equals(existente.Servico ?? "", servicoAlvo ?? "",
                    StringComparison.OrdinalIgnoreCase);
            return false;
        }

        private static Point3d CentroDe(List<Point3d> pts)
        {
            double x = 0, y = 0, z = 0;
            foreach (var p in pts) { x += p.X; y += p.Y; z += p.Z; }
            return new Point3d(x / pts.Count, y / pts.Count, z / pts.Count);
        }

        // ------------------------------------------------------------------
        // TSKPAREDERET — parede por retângulo em planta (2 cliques), igual ao
        // retângulo dos Materiais: mesmo texto e mesmo hatch gradiente.
        // Comprimento = lado maior; espessura = lado menor (ou a do painel).
        // Altura vem sempre do painel.
        // ------------------------------------------------------------------
        [CommandMethod("TSKPAREDERET")]
        public void TskParedeRet() => Util.Seguro("TSKPAREDERET", TskParedeRetImpl);

        private void TskParedeRetImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;   // ver e exportar continuam livres
            Util.AvisarSeForaDoModel(doc);
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\nParede {0} | altura {1} m | espessura {2}",
                Config.Servico, Util.N2(Config.Altura),
                Config.Espessura > 0 ? Util.N2(Config.Espessura) + " m" : "(do desenho)");

            var p1Res = ed.GetPoint(new PromptPointOptions("\nPrimeiro canto da parede: "));
            if (p1Res.Status != PromptStatus.OK) return;
            var p2Res = ed.GetCorner(new PromptCornerOptions("\nCanto oposto: ", p1Res.Value));
            if (p2Res.Status != PromptStatus.OK) return;

            Point3d p1 = p1Res.Value, p2 = p2Res.Value;
            double dx = Math.Abs(p2.X - p1.X), dy = Math.Abs(p2.Y - p1.Y);
            if (dx < 1e-9 || dy < 1e-9)
            {
                ed.WriteMessage("\nRetângulo inválido.");
                return;
            }

            double comp = Math.Max(dx, dy);
            double esp = Config.Espessura > 0 ? Config.Espessura : Math.Min(dx, dy);
            double area = comp * Config.Altura;
            double volume = area * esp;

            string layerName = LayerPrefix + Util.NomeLayer(Config.LayerEfectiva());
            var corPiso = FachadaConfig.CorDoPiso(Config.Piso);
            string handleParede = null;

            // Os pontos vêm no UCS corrente; as entidades nascem em WCS.
            // Sem esta matriz, com o UCS rodado a medição aparece torta e
            // no sítio errado. Construímos no UCS e transformamos no fim.
            var ucs = ed.CurrentUserCoordinateSystem;
            var cantosParede = new List<Point3d>
            {
                p1,
                new Point3d(p2.X, p1.Y, p1.Z),
                p2,
                new Point3d(p1.X, p2.Y, p1.Z)
            };
            var zonaParede = ZonaWcs(cantosParede, ucs);
            var vaosParede = DetectarEConfirmarVaosDaParede(db, ed, zonaParede, esp);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, AlvRepo.AppName);
                Util.EnsureLayer(tr, db, layerName, colorIndex: 1);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(p2.X, p1.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(p1.X, p2.Y), 0, 0, 0);
                pl.Closed = true;
                // Dá corpo ao traço: a medição tem de se ver por cima da
                // planta, que já vem cheia de linhas finas.
                Util.AplicarEspessura(pl);
                pl.Elevation = p1.Z;
                pl.Layer = layerName;

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                pl.TransformBy(ucs);            // UCS -> WCS
                // Cor só depois de estar na base de dados
                try { pl.Color = Color.FromRgb(corPiso.R, corPiso.G, corPiso.B); } catch { }

                var parede = new Parede
                {
                    Servico = Config.Servico,
                    Piso = Config.Piso,
                    Bloco = Config.Bloco,
                    // O bloco também etiqueta a linha: "WC1", "quarto 2".
                    Nota = FolhaMedicao.NotaInicial(null, Config.Bloco),
                    Alcado = Config.Alcado,
                    Altura = Config.Altura,
                    Espessura = Config.Espessura,   // 0 = usar o lado menor do retângulo
                    Retangulo = true,
                    Artigo = MapaQuantidades.ArtigoParaMedicao(Config.Servico),
                    Separador = Config.ConsumirSeparador()
                };
                parede.Vaos.AddRange(vaosParede);
                AlvRepo.GravarXData(pl, parede);

                bool comHatch = CriarHatchGradiente(tr, ms, pl, corPiso);

                double textHeight = Util.AlturaTexto(db);
                // Z vem do ponto: com o desenho a cota ≠ 0, um Z=0 fixo punha
                // a etiqueta invisível de certas vistas.
                var centro = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, p1.Z);
                var label = new MText
                {
                    Location = centro,
                    Contents = string.Format("{0}\\P{1} × {2} = {3} m²",
                        Config.Servico, Util.N2(comp),
                        Util.N2(Config.Altura), Util.N2(area)),
                    TextHeight = textHeight,
                    Attachment = AttachmentPoint.MiddleCenter,
                    Layer = layerName
                };
                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
                try { label.TransformBy(ucs); } catch { }   // UCS -> WCS
                try { label.Color = Util.CorTexto; } catch { }

                // Ler ANTES do Commit (ver nota em TSKPAREDE)
                handleParede = pl.Handle.ToString();
                tr.Commit();

                ed.WriteMessage("\nRetângulo {0} criado{1}: {2} × {3} = {4} m² | esp. {5} m | {6} m³",
                    Config.Servico, comHatch ? " com preenchimento" : " SEM preenchimento",
                    Util.N2(comp), Util.N2(Config.Altura),
                    Util.N2(area), Util.N2(esp), Util.N2(volume));
            }

            try { ed.Regen(); } catch { }

            PaletteHost.RegistarMedicao(handleParede);
            EscreverResumoVaos(ed, vaosParede);
            PaletteHost.RefreshData();
        }

        // ------------------------------------------------------------------
        // MEDRET — mede fachada/ETICS por retângulo (2 cliques no alçado).
        // Cria polyline fechada na cor do piso + hatch gradiente.
        // ------------------------------------------------------------------
        [CommandMethod("MEDRET")]
        public void MedRet() => Util.Seguro("MEDRET", MedRetImpl);

        private void MedRetImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;   // ver e exportar continuam livres
            Util.AvisarSeForaDoModel(doc);
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\nMedindo {0} | {1} | modo retângulo",
                FachadaConfig.Material, FachadaConfig.Piso);

            var p1Res = ed.GetPoint(new PromptPointOptions("\nPrimeiro canto: "));
            if (p1Res.Status != PromptStatus.OK) return;

            var cornerOpt = new PromptCornerOptions("\nCanto oposto: ", p1Res.Value);
            var p2Res = ed.GetCorner(cornerOpt);
            if (p2Res.Status != PromptStatus.OK) return;

            Point3d p1 = p1Res.Value, p2 = p2Res.Value;
            if (Math.Abs(p1.X - p2.X) < 1e-9 || Math.Abs(p1.Y - p2.Y) < 1e-9)
            {
                ed.WriteMessage("\nRetângulo inválido (largura/altura zero).");
                return;
            }

            string layerName = LayerPrefix + Util.NomeLayer(FachadaConfig.Material + "_" +
                FachadaConfig.Piso.Replace(" ", ""));
            var corPiso = FachadaConfig.CorDoPiso(FachadaConfig.Piso);
            string handleCriado = null;
            var zonaMedida = new Extents3d();
            var ucs = ed.CurrentUserCoordinateSystem;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, FacRepo.AppName);
                Util.EnsureLayer(tr, db, layerName, colorIndex: 3);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(p1.X, p1.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(p2.X, p1.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(p2.X, p2.Y), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(p1.X, p2.Y), 0, 0, 0);
                pl.Closed = true;
                // Dá corpo ao traço: a medição tem de se ver por cima da
                // planta, que já vem cheia de linhas finas.
                Util.AplicarEspessura(pl);
                pl.Layer = layerName;

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                // Como nos restantes comandos: a polyline nasce em UCS e só
                // depois passa a WCS. Sem isto, com o UCS rodado o retângulo
                // caía no sítio errado — e a etiqueta, que já era transformada,
                // ficava descolada da geometria.
                pl.TransformBy(ucs);            // UCS -> WCS
                // Cor só depois de estar na base de dados
                try { pl.Color = Color.FromRgb(corPiso.R, corPiso.G, corPiso.B); } catch { }
                FacRepo.GravarXData(pl, FachadaConfig.Material, FachadaConfig.Piso,
                    TipoFachada.Retangulo, 0.0, "", FachadaConfig.Alcado,
                    Config.ConsumirSeparador(), "", "",
                    // O artigo escolhido no painel acompanha o pano, como já
                    // acompanhava a parede. É o que faz a aba Materiais chegar
                    // ao mapa de quantidades.
                    MapaQuantidades.ArtigoParaMedicao(FachadaConfig.Material));

                bool comHatch = CriarHatchGradiente(tr, ms, pl, corPiso);

                double comp = Math.Abs(p2.X - p1.X);
                double alt = Math.Abs(p2.Y - p1.Y);
                double area = comp * alt;

                double textHeight = Util.AlturaTexto(db);
                // Z vem do ponto: com o desenho a cota ≠ 0, um Z=0 fixo punha
                // a etiqueta invisível de certas vistas.
                var centro = new Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, p1.Z);
                var label = new MText
                {
                    Location = centro,
                    Contents = string.Format("{0}\\P{1}\\P{2} × {3} = {4} m²",
                        FachadaConfig.Material, FachadaConfig.Piso,
                        Util.N2(comp), Util.N2(alt), Util.N2(area)),
                    TextHeight = textHeight,
                    Attachment = AttachmentPoint.MiddleCenter,
                    Layer = layerName
                };
                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
                try { label.TransformBy(ucs); } catch { }
                try { label.Color = Util.CorTexto; } catch { }

                // Ler ANTES do Commit (ver nota em TSKPAREDE)
                handleCriado = pl.Handle.ToString();
                zonaMedida = pl.GeometricExtents;

                tr.Commit();
                ed.WriteMessage("\nRetângulo {0} | {1} criado{2}: {3} × {4} = {5} m²",
                    FachadaConfig.Material, FachadaConfig.Piso,
                    comHatch ? " com preenchimento" : " SEM preenchimento",
                    Util.N2(comp), Util.N2(alt), Util.N2(area));
            }

            try { ed.Regen(); } catch { }

            PaletteHost.RegistarMedicao(handleCriado);
            PaletteHost.RefreshData();
        }

        // ------------------------------------------------------------------
        // MEDPOLF — mede fachada por polyline em planta × altura do piso.
        // ------------------------------------------------------------------
        [CommandMethod("MEDPOLF")]
        public void MedPolFachada() => Util.Seguro("MEDPOLF", MedPolFachadaImpl);

        private void MedPolFachadaImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;   // ver e exportar continuam livres
            Util.AvisarSeForaDoModel(doc);
            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\nMedindo {0} | {1} | polyline × altura {2} m",
                FachadaConfig.Material, FachadaConfig.Piso, Util.N2(FachadaConfig.AlturaPiso));

            var pts = CollectPoints(ed);
            if (pts == null) return;

            string layerName = LayerPrefix + Util.NomeLayer(FachadaConfig.Material + "_" +
                FachadaConfig.Piso.Replace(" ", ""));
            var corPiso = FachadaConfig.CorDoPiso(FachadaConfig.Piso);
            var ucs = ed.CurrentUserCoordinateSystem;
            string handleCriado = null;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, FacRepo.AppName);
                Util.EnsureLayer(tr, db, layerName, colorIndex: 3);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                var pl = new Polyline();
                for (int i = 0; i < pts.Count; i++)
                    pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                pl.Layer = layerName;
                Util.AplicarEspessura(pl);

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                pl.TransformBy(ucs);            // UCS -> WCS
                // Cor só depois de estar na base de dados
                try { pl.Color = Color.FromRgb(corPiso.R, corPiso.G, corPiso.B); } catch { }
                FacRepo.GravarXData(pl, FachadaConfig.Material, FachadaConfig.Piso,
                    TipoFachada.PolylineAltura, FachadaConfig.AlturaPiso, "",
                    FachadaConfig.Alcado, Config.ConsumirSeparador(), "", "",
                    MapaQuantidades.ArtigoParaMedicao(FachadaConfig.Material));

                double comp = pl.Length;
                double area = comp * FachadaConfig.AlturaPiso;

                double textHeight = Util.AlturaTexto(db);
                var mid = pl.GetPointAtDist(comp / 2.0);
                var label = new MText
                {
                    Location = mid,
                    Contents = string.Format("{0} {1}: {2} × {3} = {4} m²",
                        FachadaConfig.Material, FachadaConfig.Piso,
                        Util.N2(comp), Util.N2(FachadaConfig.AlturaPiso), Util.N2(area)),
                    TextHeight = textHeight,
                    Layer = layerName
                };
                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
                try { label.TransformBy(ucs); } catch { }
                try { label.Color = Util.CorTexto; } catch { }

                // Ler ANTES do Commit (ver nota em TSKPAREDE)
                handleCriado = pl.Handle.ToString();

                tr.Commit();
                ed.WriteMessage("\n{0} m × {1} m = {2} m².",
                    Util.N2(comp), Util.N2(FachadaConfig.AlturaPiso), Util.N2(area));
            }

            PaletteHost.RegistarMedicao(handleCriado);
            PaletteHost.RefreshData();
        }

        /// <summary>
        /// Preenchimento gradiente associado à polyline fechada.
        /// Sequência canónica: pattern SOLID → gradiente → loop → avaliar →
        /// associativo no fim. Se falhar, tenta sólido simples; se ainda assim
        /// falhar, a medição fica gravada na mesma (sem preenchimento).
        /// </summary>
        private static bool CriarHatchGradiente(Transaction tr, BlockTableRecord ms,
            Polyline contorno, System.Drawing.Color cor)
        {
            var acCor = Color.FromRgb(cor.R, cor.G, cor.B);
            var loop = new ObjectIdCollection { contorno.ObjectId };

            // 1ª tentativa: gradiente
            if (TentarHatch(tr, ms, contorno, loop, h =>
            {
                h.HatchObjectType = HatchObjectType.GradientObject;
                h.SetGradient(GradientPatternType.PreDefinedGradient, "LINEAR");
                // dois tons (cor → branco), não o modo de cor única
                try { h.GradientOneColorMode = false; } catch { }
                h.SetGradientColors(new[]
                {
                    new GradientColor(acCor, 0f),
                    new GradientColor(Color.FromRgb(255, 255, 255), 1f)
                });
                h.GradientAngle = Math.PI / 2.0;
            }))
                return true;

            PaletteHost.Log("Gradiente indisponível — a usar preenchimento sólido.");

            // 2ª tentativa: sólido transparente
            if (TentarHatch(tr, ms, contorno, loop, h =>
            {
                h.HatchObjectType = HatchObjectType.HatchObject;
                h.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
                h.Color = acCor;
            }))
                return true;

            PaletteHost.Log("Sem preenchimento — a medição foi gravada na mesma.");
            return false;
        }

        /// <summary>
        /// Sequência canónica de criação de hatch na API do AutoCAD:
        ///   1. AppendEntity ANTES de tocar em qualquer propriedade
        ///   2. SetDatabaseDefaults
        ///   3. padrão ou gradiente
        ///   4. Associative antes do loop
        ///   5. AppendLoop com HatchLoopTypes.Default
        ///   6. EvaluateHatch
        ///   7. layer/transparência no fim (nunca fatais)
        /// Trocar esta ordem é a causa nº1 de hatch que não aparece.
        /// </summary>
        private static bool TentarHatch(Transaction tr, BlockTableRecord ms,
            Polyline contorno, ObjectIdCollection loop, Action<Hatch> configurar)
        {
            Hatch hatch = null;
            try
            {
                hatch = new Hatch();

                // 1) na base de dados primeiro — tudo o resto depende disto
                ms.AppendEntity(hatch);
                tr.AddNewlyCreatedDBObject(hatch, true);

                // 2) valores por omissão do desenho
                hatch.SetDatabaseDefaults();

                // O plano do CONTORNO, não o do desenho.
                //
                // Estava fixo em ZAxis/0, e enquanto tudo se mediu à cota zero
                // funcionou. Mas uma polyline apanhada de um projecto real vem
                // à cota do piso, e um UCS deslocado em Z põe lá as que se
                // desenham de novo: com o contorno a uma cota e a hachura a
                // outra, o EvaluateHatch não tem sobre o que trabalhar e o
                // preenchimento simplesmente não aparece — sem erro nenhum,
                // que é o pior modo de falhar.
                hatch.Normal = contorno.Normal;
                hatch.Elevation = contorno.Elevation;

                // 3) padrão sólido ou gradiente
                configurar(hatch);

                // 4-6) associatividade, contorno e avaliação
                hatch.Associative = true;
                hatch.AppendLoop(HatchLoopTypes.Default, loop);
                hatch.EvaluateHatch(true);

                // 7) acessórios — se falharem, o preenchimento fica na mesma
                try { hatch.Layer = contorno.Layer; } catch { }
                try
                {
                    hatch.Transparency =
                        new Autodesk.AutoCAD.Colors.Transparency(
                            (byte)Config.TransparenciaHatch);
                }
                catch { }

                return true;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                PaletteHost.Log("Preenchimento falhou [" + ex.ErrorStatus + "]: " + ex.Message);
                Apagar(hatch);
                return false;
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("Preenchimento falhou [" + ex.GetType().Name + "]: " + ex.Message);
                Apagar(hatch);
                return false;
            }
        }

        private static void Apagar(Hatch hatch)
        {
            try { if (hatch != null && !hatch.IsErased) hatch.Erase(); } catch { }
        }

        // ------------------------------------------------------------------
        // MEDIR — medição linear simples (tubos, rodapés etc.).
        // ------------------------------------------------------------------
        [CommandMethod("MEDIR")]
        public void Medir() => Util.Seguro("MEDIR", MedirImpl);

        private void MedirImpl()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            if (!Licenca.PodeMedir()) return;   // ver e exportar continuam livres
            Util.AvisarSeForaDoModel(doc);
            var db = doc.Database;
            var ed = doc.Editor;

            var pso = new PromptStringOptions("\nCategoria da medição (ex.: TUBO, RODAPE) <GERAL>: ")
            {
                AllowSpaces = false
            };
            var psr = ed.GetString(pso);
            if (psr.Status != PromptStatus.OK && psr.Status != PromptStatus.None) return;

            string categoria = string.IsNullOrWhiteSpace(psr.StringResult)
                ? "GERAL"
                : psr.StringResult.Trim().ToUpperInvariant();
            string layerName = LayerPrefix + Util.NomeLayer(categoria);

            var pts = CollectPoints(ed);
            if (pts == null) return;

            var ucs = ed.CurrentUserCoordinateSystem;
            string handleCriado = null;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Util.EnsureRegApp(tr, db, AppNameLinear);
                Util.EnsureLayer(tr, db, layerName, colorIndex: 2 /* amarelo */);

                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForWrite);

                var pl = new Polyline();
                for (int i = 0; i < pts.Count; i++)
                    pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
                pl.Layer = layerName;
                Util.AplicarEspessura(pl);

                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                pl.TransformBy(ucs);            // UCS -> WCS

                pl.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppNameLinear),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, categoria));

                double comp = pl.Length;
                double textHeight = Util.AlturaTexto(db);
                var mid = pl.GetPointAtDist(comp / 2.0);
                var label = new MText
                {
                    Location = mid,
                    Contents = Util.N2(comp) + " m",
                    TextHeight = textHeight,
                    Layer = layerName
                };
                ms.AppendEntity(label);
                tr.AddNewlyCreatedDBObject(label, true);
                try { label.TransformBy(ucs); } catch { }
                try { label.Color = Util.CorTexto; } catch { }

                // Ler ANTES do Commit (ver nota em TSKPAREDE)
                handleCriado = pl.Handle.ToString();

                tr.Commit();
                ed.WriteMessage("\nMedição criada: {0} | {1} m.", categoria, Util.N2(comp));
            }

            // A leitura central já encontrava esta medição e enviava-a ao Excel,
            // mas o painel não era atualizado nem sabia qual linha selecionar.
            // O mesmo contrato das paredes e dos materiais mantém os dois lados
            // sincronizados imediatamente após terminar o comando.
            PaletteHost.RegistarMedicao(handleCriado);
            PaletteHost.RefreshData();
        }

        // ------------------------------------------------------------------
        // MEDEXPORT — gera o .xlsx completo (alvenaria + lineares + resumo).
        // ------------------------------------------------------------------
        [CommandMethod("MEDEXPORT")]
        public void MedExport()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            var paredes = AlvRepo.CarregarParedes(db);
            var lineares = CollectLineares(db);
            var fachadas = FacRepo.Carregar(db);
            // A exportação pode ser chamada diretamente, sem abrir o painel.
            // Ler as contagens aqui evita depender do estado global actualizado
            // por PaletteHost.RefreshData().
            var contagens = ContRepo.Carregar(db);
            FolhaMedicao.Contagens = contagens;

            if (paredes.Count == 0 && lineares.Count == 0 && fachadas.Count == 0 &&
                contagens.Count == 0)
            {
                ed.WriteMessage("\nNenhuma medição encontrada. Use MEDRET, MEDPAREDE, MEDIR ou TSKCONTAR primeiro.");
                return;
            }

            string dwgName = doc.Name;
            string dir = File.Exists(dwgName)
                ? Path.GetDirectoryName(dwgName)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string baseName = NomeBaseDoDesenho();

            // Com modelo registado sai no formato da casa, com macros e tudo.
            if (ModeloExcel.Existe)
            {
                string ext = Path.GetExtension(ModeloExcel.Caminho);
                if (string.IsNullOrEmpty(ext)) ext = ".xls";
                string alvo = Path.Combine(dir, baseName + "_medicoes" + ext);

                ed.WriteMessage("\nA gerar a folha de medições no modelo da casa…");
                string erro = ExcelLiveSync.ExportarComModelo(
                    alvo, paredes, fachadas, lineares, contagens, Config.Regra);

                if (erro == null)
                {
                    ed.WriteMessage("\nExportado: {0} fachada(s), {1} parede(s), {2} contagem(ns) para:\n{3}\n",
                        fachadas.Count, paredes.Count, contagens.Count, alvo);
                    return;
                }
                ed.WriteMessage("\nO modelo falhou ({0}). A exportar em .xlsx simples…", erro);
            }

            string xlsxPath = Path.Combine(dir, baseName + "_medicoes.xlsx");

            try
            {
                ExcelExporter.Export(xlsxPath, paredes, lineares, fachadas, Config.Regra);
                ed.WriteMessage("\nExportado: {0} fachada(s), {1} parede(s), {2} linear(es), {3} contagem(ns) para:\n{4}",
                    fachadas.Count, paredes.Count, lineares.Count, contagens.Count, xlsxPath);
            }
            catch (IOException)
            {
                ed.WriteMessage("\nERRO: feche o arquivo \"{0}\" no Excel e tente novamente.", xlsxPath);
            }
        }

        /// <summary>
        /// Exporta as medições em FIEBDC-3 (.bc3) — o formato de intercâmbio
        /// que Arquimedes, CYPECAD, Presto e TCQ já sabem abrir. Ao LADO do
        /// Excel, não em vez dele: TSKEXPORT continua a ser a saída
        /// principal, e nada aqui toca no desenho.
        ///
        /// Ver a nota no topo de FiebdcExporter.cs: primeira versão, ainda
        /// não confirmada contra uma importação real no Arquimedes/CYPECAD.
        /// </summary>
        public void MedExportBc3()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;
            var ed = doc.Editor;

            if (!MapaQuantidades.Existe)
            {
                ed.WriteMessage("\nEste desenho não tem mapa de quantidades importado. " +
                    "Importe com TSKMQT antes de exportar em FIEBDC-3 — sem artigos " +
                    "classificados não há o que pôr num formato de orçamento.");
                return;
            }

            var paredes = AlvRepo.CarregarParedes(db);
            var lineares = CollectLineares(db);
            var fachadas = FacRepo.Carregar(db);
            var contagens = ContRepo.Carregar(db);

            Func<string, bool> artigoConhecido = a => MapaQuantidades.Procurar(a) != null;
            var medicoes = ResultadosArvore.DeParedes(
                paredes, Config.Regra, artigoConhecido, CultureInfo.CurrentCulture);
            medicoes.AddRange(ResultadosAdaptadores.DeMateriais(
                fachadas, Config.Regra, artigoConhecido, CultureInfo.CurrentCulture));
            medicoes.AddRange(ResultadosAdaptadores.DeLineares(
                lineares, CultureInfo.CurrentCulture));
            medicoes.AddRange(ResultadosAdaptadores.DeContagens(
                contagens, CultureInfo.CurrentCulture));

            // Só o que já tem artigo: sem código não há para onde exportar
            // num formato de orçamento. Fica "Por classificar" tal como já
            // estava — TSKEXPORT (Excel) continua a mostrar tudo.
            var linhas = medicoes
                .Where(m => !string.IsNullOrWhiteSpace(m.Artigo))
                .GroupBy(m => new { m.Piso, m.Artigo })
                .Select(g =>
                {
                    // A chave do artigo é "código\x1fdesignação" — ver
                    // MedicaoResultado.Artigo.
                    var partes = (g.Key.Artigo ?? "").Split('\x1f');
                    string codigo = partes.Length > 0 ? partes[0] : g.Key.Artigo;
                    string designacao = partes.Length > 1 ? partes[1] : "";
                    return new FiebdcExporter.Linha(g.Key.Piso, codigo, designacao,
                        g.First().Unidade, g.Sum(m => m.Quantidade));
                })
                .ToList();

            if (linhas.Count == 0)
            {
                ed.WriteMessage("\nNenhuma medição classificada para exportar. " +
                    "Classifique os artigos (Mais ▸ Reclassificar…) e tente de novo.");
                return;
            }

            string dir = File.Exists(doc.Name)
                ? Path.GetDirectoryName(doc.Name)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string baseName = NomeBaseDoDesenho();
            string alvo = Path.Combine(dir, baseName + "_medicoes.bc3");

            string conteudo = FiebdcExporter.Gerar(linhas, baseName, DateTime.Now);
            try
            {
                File.WriteAllText(alvo, conteudo, FiebdcExporter.CodificacaoFicheiro);
                int artigos = linhas.Select(l => l.Codigo).Distinct().Count();
                ed.WriteMessage("\nExportado: {0} artigo(s) para:\n{1}\n" +
                    "AVISO: primeira versão do exportador FIEBDC-3, ainda não " +
                    "confirmada contra uma importação real no Arquimedes/CYPECAD " +
                    "— confira os totais antes de entregar a um cliente.",
                    artigos, alvo);
            }
            catch (IOException)
            {
                ed.WriteMessage("\nERRO: feche o arquivo \"{0}\" e tente novamente.", alvo);
            }
        }

        /// <summary>Nome do DWG sem extensão, para baptizar os ficheiros gerados.</summary>
        private static string NomeBaseDoDesenho()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            string nome = doc == null ? null : Path.GetFileNameWithoutExtension(doc.Name);
            return string.IsNullOrWhiteSpace(nome) ? "desenho" : nome;
        }

        // ==================================================================
        // Auxiliares
        // ==================================================================

        /// <summary>Coleta pontos até o usuário dar Enter. Null se cancelado/insuficiente.</summary>
        private static List<Point3d> CollectPoints(Editor ed)
        {
            var pts = new List<Point3d>();
            var firstRes = ed.GetPoint(new PromptPointOptions("\nPrimeiro ponto: "));
            if (firstRes.Status != PromptStatus.OK) return null;
            pts.Add(firstRes.Value);

            while (true)
            {
                var opt = new PromptPointOptions("\nPróximo ponto <Enter para finalizar>: ")
                {
                    UseBasePoint = true,
                    BasePoint = pts[pts.Count - 1],
                    AllowNone = true
                };
                var res = ed.GetPoint(opt);
                if (res.Status == PromptStatus.OK) { pts.Add(res.Value); continue; }
                break;
            }

            if (pts.Count < 2)
            {
                ed.WriteMessage("\nCancelado: são necessários pelo menos 2 pontos.");
                return null;
            }
            return pts;
        }

        internal static List<MedItem> CollectLineares(Database db)
        {
            // A mesma leitura do Leitura.Tudo: uma travessia só, e um único
            // sítio onde se sabe ler a XData das lineares. Duas cópias deste
            // parsing viviam lado a lado e divergiam.
            List<Parede> paredes;
            List<MedFachada> fachadas;
            List<MedItem> lineares;
            List<MedContagem> contagens;
            Leitura.Tudo(db, out paredes, out fachadas, out lineares, out contagens);
            return lineares;
        }
    }

    /// <summary>Funções utilitárias de banco de dados e formatação.</summary>
    internal static class Util
    {
        internal static string N2(double v) => v.ToString("N2", CultureInfo.CurrentCulture);

        /// <summary>
        /// As medições vivem sempre no Model Space, aconteça o que acontecer.
        ///
        /// Antes usava-se o espaço corrente, para que a polyline nascesse à
        /// vista de quem estava num Layout. Só que o desenho tem um Model e N
        /// layouts, e cada um é uma base de dados à parte: medir com a folha
        /// "Alçado Sul" activa punha a medição nessa folha e mais nenhuma. O
        /// painel de outro layout não a via, a exportação também não, e o
        /// utilizador concluía — com razão aparente — que o plugin lhe tinha
        /// comido o trabalho.
        ///
        /// Um único espaço acaba com isso. O preço é que, ao medir a partir
        /// de um Layout, a polyline não fica no ecrã: está no Model. Por isso
        /// os comandos de desenho avisam antes (ver <see cref="AvisarSeForaDoModel"/>)
        /// e o TSKONDE varre todos os espaços, para que nada desapareça sem
        /// alguém dizer onde foi parar.
        /// </summary>
        internal static ObjectId EspacoMedicoesId(Database db)
        {
            return SymbolUtilityServices.GetBlockModelSpaceId(db);
        }

        /// <summary>
        /// Diz, uma vez por comando, que a medição vai nascer fora do ecrã.
        ///
        /// Sem este aviso o utilizador clica, não vê nada aparecer e repete o
        /// comando — e fica com a medição em duplicado sem saber.
        /// </summary>
        internal static void AvisarSeForaDoModel(Document doc)
        {
            try
            {
                if (doc == null || doc.Database.TileMode) return;   // já está no Model
                doc.Editor.WriteMessage(
                    "\n>> Estás num LAYOUT. A medição é criada no Model Space e " +
                    "não vai aparecer nesta folha — usa o separador Model para a ver.");
            }
            catch { /* um aviso que falha não pode derrubar a medição */ }
        }

        /// <summary>
        /// Corre um comando e, se falhar, escreve o erro exacto na linha de
        /// comando. Sem isto, uma excepção faz o AutoCAD desfazer tudo em
        /// silêncio e a medição "desaparece" sem explicação.
        /// </summary>
        internal static void Seguro(string comando, Action corpo)
        {
            try { corpo(); }
            catch (Autodesk.AutoCAD.Runtime.Exception ex)
            {
                PaletteHost.Log(comando + " falhou [" + ex.ErrorStatus + "]: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log(comando + " falhou [" + ex.GetType().Name + "]: " + ex.Message);
            }
            finally
            {
                // Sempre — mesmo que o comando tenha rebentado a meio. Deixar
                // ligadas as layers que o utilizador tinha desligado seria pior
                // do que o erro que levou lá.
                ReporLayersDestapadas();
            }
        }

        /// <summary>
        /// Layers que se destaparam para a medição poder ser DESENHADA, e o
        /// estado em que estavam. Ver EnsureLayer: numa layer desligada ou
        /// congelada o EvaluateHatch não corre e a hachura sai vazia.
        /// </summary>
        private sealed class EstadoLayer
        {
            public ObjectId Id;
            public bool Off;
            public bool Frozen;
        }

        private static readonly List<EstadoLayer> _layersDestapadas = new List<EstadoLayer>();

        /// <summary>
        /// Devolve as layers ao estado em que a pessoa as tinha deixado.
        ///
        /// Corre depois de o comando ter fechado a sua transacção, por isso
        /// abre uma própria. A hachura já foi calculada nessa altura: voltar a
        /// esconder a layer esconde-a, não a desfaz — que é a diferença entre
        /// isto e nunca a ter destapado.
        /// </summary>
        internal static void ReporLayersDestapadas()
        {
            if (_layersDestapadas.Count == 0) return;

            var pendentes = _layersDestapadas.ToArray();
            _layersDestapadas.Clear();

            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    foreach (var e in pendentes)
                    {
                        try
                        {
                            var ltr = tr.GetObject(e.Id, OpenMode.ForWrite) as LayerTableRecord;
                            if (ltr == null) continue;
                            // Uma de cada vez: congelar a layer corrente é
                            // recusado, e não pode levar a outra atrás.
                            try { ltr.IsOff = e.Off; } catch { }
                            try { ltr.IsFrozen = e.Frozen; } catch { }
                        }
                        catch { }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("Não foi possível repor a visibilidade das layers: " +
                                ex.Message);
            }
        }

        /// <summary>
        /// Altura dos rótulos de medição. Manda o valor fixo do painel; só
        /// quando ele está a 0 (automático) é que se vai buscar o TEXTSIZE do
        /// desenho, que em ficheiros de arquitectura costuma vir grande demais.
        /// </summary>
        internal static double AlturaTexto(Database db)
        {
            if (Config.AlturaTexto > 0) return Config.AlturaTexto;
            try { if (db != null && db.Textsize > 0) return db.Textsize; }
            catch { }
            return 0.25;
        }

        /// <summary>Cor dos rótulos de medição: ACI 10 (vermelho).</summary>
        internal static Color CorTexto =>
            Color.FromColorIndex(ColorMethod.ByAci, 10);

        internal static void EnsureRegApp(Transaction tr, Database db, string appName)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(appName)) return;

            rat.UpgradeOpen();
            var record = new RegAppTableRecord { Name = appName };
            rat.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        /// <summary>
        /// Cria o layer se não existir e, se já existir, garante que está
        /// visível: desligado/congelado/bloqueado faz a medição parecer que
        /// não foi criada, quando na verdade está lá.
        /// </summary>
        /// <summary>
        /// Dá corpo ao traço de uma medição. Lineweight por omissão; largura
        /// real só se estiver expressamente pedida na configuração.
        /// </summary>
        // Classes das entidades que nos interessam. Comparar o ObjectClass de
        // um ObjectId NÃO abre o objecto — é só uma comparação de ponteiros.
        // Abrir cada entidade do Model Space para depois descobrir que é um
        // texto ou um bloco é o que torna a paleta lenta num desenho de
        // arquitectura, onde as nossas medições são um punhado entre dezenas
        // de milhares de objectos.
        internal static readonly Autodesk.AutoCAD.Runtime.RXClass ClPolyline =
            Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Polyline));
        internal static readonly Autodesk.AutoCAD.Runtime.RXClass ClCircle =
            Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Circle));
        internal static readonly Autodesk.AutoCAD.Runtime.RXClass ClMText =
            Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(MText));
        internal static readonly Autodesk.AutoCAD.Runtime.RXClass ClHatch =
            Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Hatch));
        internal static readonly Autodesk.AutoCAD.Runtime.RXClass ClLine =
            Autodesk.AutoCAD.Runtime.RXObject.GetClass(typeof(Line));

        internal static void AplicarEspessura(Polyline pl)
        {
            if (pl == null) return;

            if (Config.LarguraTraco > 0)
            {
                try { pl.ConstantWidth = Config.LarguraTraco; } catch { }
                return;
            }
            if (Config.EspessuraTracoMm <= 0) return;
            try { pl.LineWeight = (LineWeight)Config.EspessuraTracoMm; } catch { }
        }

        /// <summary>
        /// Liga a apresentação de lineweights, senão o traço fica mais grosso
        /// só no papel e no ecrã não se nota nada — e parece que não funcionou.
        /// </summary>
        internal static void GarantirLwDisplay()
        {
            try
            {
                if (Config.LarguraTraco > 0 || Config.EspessuraTracoMm <= 0) return;
                object v = AcadApp.GetSystemVariable("LWDISPLAY");
                if (v != null && System.Convert.ToInt16(v) == 0)
                    AcadApp.SetSystemVariable("LWDISPLAY", 1);
            }
            catch { }
        }

        /// <summary>
        /// Nome de layer que o AutoCAD aceita, a partir de texto escrito pela
        /// pessoa (o Serviço da Alvenaria, o Material dos Materiais).
        ///
        /// O AutoCAD recusa < > / \ " : ; ? * | , = ` num nome de layer, e
        /// recusa-o a atirar eInvalidInput da atribuição do Name — que sobe
        /// como "MEDPAREDE falhou [InvalidInput]" e deixa a medição por fazer.
        /// Acontece a sério: quem cola a descrição do artigo para o campo do
        /// serviço traz um "com h=2,45m" com o = e a vírgula lá dentro.
        ///
        /// O nome sujo só se limpa AQUI, para o layer. O Serviço continua a ir
        /// para a XData e para o Excel tal como foi escrito — é ele que a
        /// pessoa lê na folha, e trocar-lhe os caracteres estragava a medição
        /// para resolver um problema que é só do layer.
        /// </summary>
        internal static string NomeLayer(string bruto)
        {
            string limpo = (bruto ?? "").Trim();
            foreach (char c in "<>/\\\":;?*|=`,")
                limpo = limpo.Replace(c, '_');

            // O AutoCAD também recusa nomes acima de 255 caracteres, e quem
            // cola a descrição de um artigo passa disso à primeira. Sobra
            // espaço para o "MED_" que vem à frente.
            if (limpo.Length > 240) limpo = limpo.Substring(0, 240).TrimEnd();

            return limpo.Length == 0 ? "GERAL" : limpo;
        }

        internal static void EnsureLayer(Transaction tr, Database db, string name, short colorIndex)
        {
            // Rede de segurança: qualquer caminho que chegue aqui com um nome
            // que o AutoCAD recusa fica com ele limpo em vez de rebentar.
            name = NomeLayer(name);

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (lt.Has(name))
            {
                // A LAYER ACABA COMO A PESSOA A DEIXOU — mas tem de estar
                // visível ENQUANTO se desenha.
                //
                // Houve duas versões erradas disto, e a segunda é subtil:
                //
                //   1ª  forçava a layer a visível e deixava-a assim. Desligar
                //       layers é como se trabalha — esconde-se o que já foi
                //       medido para ver a planta — e o plugin desfazia-o a
                //       cada medição. No TSKMEDSEL voltavam todas de uma vez.
                //
                //   2ª  passou a não lhe tocar, só a avisar. Só que numa layer
                //       desligada ou congelada o AutoCAD não corre o
                //       EvaluateHatch, e a hachura sai sem geometria — ver
                //       abaixo. O preenchimento deixou de aparecer, e nem
                //       ligar a layer a seguir o trazia de volta.
                //
                // Agora destapa-se para desenhar e repõe-se no fim (Seguro →
                // ReporLayersDestapadas). O aviso fica, porque continua a ser
                // verdade que a medição não se vê enquanto a layer estiver
                // desligada.
                var existente = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);

                bool escondida = existente.IsOff || existente.IsFrozen;
                bool bloqueada = existente.IsLocked;
                if (!escondida && !bloqueada) return;

                existente.UpgradeOpen();

                // O BLOQUEIO tem de sair: numa layer bloqueada o AutoCAD recusa
                // acrescentar a entidade, e aí não há medição nenhuma.
                if (bloqueada)
                {
                    existente.IsLocked = false;
                    PaletteHost.Log("Layer " + name + " estava bloqueada — desbloqueada " +
                                    "para receber a medição.");
                }

                // E A LAYER TEM DE ESTAR VISÍVEL ENQUANTO SE DESENHA.
                //
                // Não por estética: numa layer desligada ou congelada o AutoCAD
                // NÃO CORRE O EvaluateHatch — diz "Associative hatch entity on
                // locked or frozen layer. No update performed." e segue. A
                // hachura fica criada mas sem geometria nenhuma, e nem ligar a
                // layer a seguir a faz aparecer, porque nunca chegou a ser
                // calculada. Era isto que fazia "o comando deixou de criar a
                // hatch".
                //
                // Destapa-se só o tempo da medição. O Seguro() repõe o estado
                // no fim do comando, porque desligar layers é como se trabalha
                // — esconde-se o que já foi medido para ver a planta — e o
                // plugin não pode desfazer isso a cada medição. Foi por isso
                // que se deixou de forçar a visibilidade; o que faltava era
                // repor em vez de simplesmente não mexer.
                if (escondida)
                {
                    _layersDestapadas.Add(new EstadoLayer
                    {
                        Id = existente.ObjectId,
                        Off = existente.IsOff,
                        Frozen = existente.IsFrozen
                    });

                    try { existente.IsOff = false; } catch { }
                    // Congelar/descongelar a layer CORRENTE é recusado pelo
                    // AutoCAD; aí fica como está e o aviso explica porquê.
                    try { existente.IsFrozen = false; } catch { }

                    PaletteHost.Log("A layer " + name + " está desligada: a medição " +
                                    "foi criada e conta na folha, mas não se vê no " +
                                    "desenho enquanto não a ligar.");
                }
                return;
            }

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = name,
                Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
                IsOff = false,
                IsFrozen = false,
                IsLocked = false,
                IsPlottable = true
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }
    }
}
