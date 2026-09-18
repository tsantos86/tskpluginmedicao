using System;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Windows;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Ribbon "TSK TakeOff" criada por código (sem CUIX): uma DLL só,
    /// sem o utilizador ter de carregar ficheiros de personalização.
    /// </summary>
    public static class RibbonBuilder
    {
        private const string TabId = "TSK_TAKEOFF_TAB";
        private static bool _construida;

        /// <summary>Chamado no arranque; espera a ribbon existir antes de criar a aba.</summary>
        public static void Inicializar()
        {
            if (ComponentManager.Ribbon != null)
            {
                Construir();
                return;
            }
            ComponentManager.ItemInitialized += OnItemInitialized;
        }

        private static void OnItemInitialized(object sender, RibbonItemEventArgs e)
        {
            if (ComponentManager.Ribbon == null) return;
            ComponentManager.ItemInitialized -= OnItemInitialized;
            Construir();
        }

        public static void Construir()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null || _construida) return;

                // Não duplicar se o NETLOAD for repetido
                foreach (RibbonTab t in ribbon.Tabs)
                {
                    if (t.Id == TabId) { _construida = true; return; }
                }

                var tab = new RibbonTab { Title = "TSK TakeOff", Id = TabId };
                ribbon.Tabs.Add(tab);

                // ---------- Projeto ----------
                var pProjeto = NovoPainel(tab, "Projeto");
                pProjeto.Items.Add(BotaoGrande("Painel de\nMedições", IconFactory.Painel(),
                    "TSKPAINEL ", "Abre o painel lateral com as medições do desenho."));
                pProjeto.Items.Add(BotaoGrande("Licença", IconFactory.RibbonLicenca(),
                    "TSKLICENCA ", "Activar ou consultar a licença deste posto."));

                // ---------- Alvenaria ----------
                var pAlv = NovoPainel(tab, "Alvenaria");
                pAlv.Items.Add(BotaoGrande("Parede\nRetângulo", IconFactory.ParedeRet(),
                    "TSKPAREDERET ", "Dois cliques em planta: comprimento e espessura " +
                    "saem do retângulo; altura vem do painel."));
                pAlv.Items.Add(BotaoGrande("Parede\nPolyline", IconFactory.ParedePoly(),
                    "TSKPAREDE ", "Desenha o eixo da parede; altura e espessura vêm do painel."));
                pAlv.Items.Add(BotaoGrande("Área ×\nAltura", IconFactory.Area(),
                    "TSKAREA ", "Desenha o contorno de uma área em planta e mede-a " +
                    "vezes a altura do painel. Para camadas: betonilhas, " +
                    "enchimentos, impermeabilizações (m³)."));
                pAlv.Items.Add(BotaoGrande("Área da\nSelecção", IconFactory.AreaSel(),
                    "TSKAREASEL ", "Mede hachuras ou polylines fechadas que já " +
                    "existem no projeto: área × altura do painel (m³). " +
                    "O desenho original não se toca."));
                pAlv.Items.Add(BotaoGrande("Medir\nSelecção", IconFactory.MedirSel(),
                    "TSKMEDSEL ", "Mede polylines, linhas ou hachuras existentes no desenho."));
                pAlv.Items.Add(BotaoGrande("Medição\nLinear", IconFactory.Linear(),
                    "TSKLINEAR ", "Medição linear simples por categoria (tubos, rodapés…)."));
                pAlv.Items.Add(BotaoGrande("Vãos à\nmão", IconFactory.RibbonVao(),
                    "TSKVAO ", "Escolhe a medição e seleciona os textos/blocos dos vãos."));

                // ---------- Fachadas / ETICS ----------
                var pFac = NovoPainel(tab, "Materiais");
                pFac.Items.Add(BotaoGrande("Retângulo", IconFactory.Retangulo(),
                    "TSKRET ", "Dois cliques no alçado: comp × alt saem da geometria."));
                pFac.Items.Add(BotaoGrande("Polyline\n× Altura", IconFactory.Polf(),
                    "TSKPOLF ", "Polyline em planta multiplicada pela altura do piso."));

                // ---------- Contagens ----------
                var pCont = NovoPainel(tab, "Contagens");
                pCont.Items.Add(BotaoGrande("Contar\nBlocos", IconFactory.RibbonContagem(),
                    "TSKCONTAR ", "Conta os blocos seleccionados (portas, janelas, tomadas, " +
                    "luminárias…) e marca cada um com um círculo. Aceita a selecção do QSELECT."));

                // ---------- Excel ----------
                var pExcel = NovoPainel(tab, "Excel");
                pExcel.Items.Add(BotaoGrande("Excel\nao Vivo", IconFactory.Excel(),
                    "TSKEXCEL ", "Liga/desliga o espelhamento em tempo real no Excel."));
                pExcel.Items.Add(BotaoGrande("Exportar\nFolha", IconFactory.Exportar(),
                    "TSKEXPORT ", "Gera a folha de medições ao lado do DWG, " +
                    "no modelo da casa se houver um registado."));
                pExcel.Items.Add(BotaoGrande("Modelo da\nCasa", IconFactory.Modelo(),
                    "TSKMODELO ", "Escolhe o livro-modelo de medições. O plugin escreve " +
                    "numa cópia, mantendo fórmulas e macros."));
                pExcel.Items.Add(BotaoGrande("Mapa de\nQuantidades", IconFactory.Mapa(),
                    "TSKMQT ", "Importa o articulado do cliente — capítulos e artigos, " +
                    "sem quantidades nem fórmulas. Depois escolhe-se o artigo no painel " +
                    "e as medições saem já arrumadas por ele."));

                // ---------- Macros do modelo ----------
                var pMacros = NovoPainel(tab, "Mapas");
                pMacros.Items.Add(BotaoGrande("MD", IconFactory.Macro("MD"),
                    "TSKMD ", "Corre a macro CriarMD_v1: mapa de medições só com valores."));
                pMacros.Items.Add(BotaoGrande("EO", IconFactory.Macro("EO"),
                    "TSKEO ", "Corre a macro CriarEO_Final: estimativa orçamental."));
                pMacros.Items.Add(BotaoGrande("QT", IconFactory.Macro("QT"),
                    "TSKQT ", "Corre a macro CriarQT: mapa de quantidades."));
                pMacros.Items.Add(BotaoGrande("TSK\nDIGITAL", IconFactory.RibbonSobre(),
                    "TSKSOBRE ", "Sobre o TSK TakeOff: versão, build e informação do produto."));

                _construida = true;
                PaletteHost.Log("Ribbon TSK TakeOff carregada.");
            }
            catch (Exception ex)
            {
                PaletteHost.Log("Não foi possível criar a ribbon: " + ex.Message);
            }
        }

        // ------------------------------------------------------------------
        private static RibbonPanelSource NovoPainel(RibbonTab tab, string titulo)
        {
            var source = new RibbonPanelSource { Title = titulo };
            tab.Panels.Add(new RibbonPanel { Source = source });
            return source;
        }

        private static RibbonButton BotaoGrande(string texto, System.Drawing.Bitmap icone,
            string comando, string dica)
        {
            // A Ribbon usa 32 px no botão grande e 16 px quando o painel é
            // compactado. Criar os dois tamanhos evita que o AutoCAD reduza
            // o mesmo bitmap de 64 px de forma diferente em cada versão.
            ImageSource imagemGrande;
            ImageSource imagemPequena;
            using (icone)
            using (var grande = RibbonIconRenderer.Preparar(icone, 32))
            using (var pequena = RibbonIconRenderer.Preparar(icone, 16))
            {
                imagemGrande = ToImageSource(grande);
                imagemPequena = ToImageSource(pequena);
            }

            var btn = new RibbonButton
            {
                Text = texto,
                ShowText = true,
                ShowImage = true,
                LargeImage = imagemGrande,
                Image = imagemPequena,
                Size = RibbonItemSize.Large,
                Orientation = System.Windows.Controls.Orientation.Vertical,
                CommandParameter = comando,
                CommandHandler = new ComandoRibbon(),
                ToolTip = dica
            };
            return btn;
        }

        /// <summary>
        /// Converte os ícones GDI+ para o formato da ribbon (WPF).
        ///
        /// Isto passava pelo GetHbitmap + CreateBitmapSourceFromHBitmap, e
        /// esse par DEITA FORA o canal alfa: devolve Bgr32, e cada pixel
        /// transparente do ícone chegava à ribbon a PRETO. No tema escuro do
        /// AutoCAD o preto confunde-se com o fundo e ninguém dava por isso;
        /// no tema claro cada botão ficava um quadrado preto com o desenho
        /// lá dentro. Copiar os bits directamente em Bgra32 mantém o alfa e
        /// os ícones assentam no fundo da ribbon, seja ele qual for.
        /// </summary>
        private static ImageSource ToImageSource(System.Drawing.Bitmap bmp)
        {
            var area = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            var bits = bmp.LockBits(area,
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var src = BitmapSource.Create(
                    bmp.Width, bmp.Height, 96, 96,
                    PixelFormats.Bgra32, null,
                    bits.Scan0, bits.Stride * bmp.Height, bits.Stride);
                src.Freeze();
                return src;
            }
            finally
            {
                bmp.UnlockBits(bits);
            }
        }

        /// <summary>Dispara o comando AutoCAD associado ao botão.</summary>
        private class ComandoRibbon : ICommand
        {
            public event EventHandler CanExecuteChanged
            {
                add { }      // a ribbon do AutoCAD não usa este evento
                remove { }
            }
            public bool CanExecute(object parameter) => true;

            public void Execute(object parameter)
            {
                var btn = parameter as RibbonButton;
                string comando = btn?.CommandParameter as string;
                if (!string.IsNullOrEmpty(comando))
                    PaletteHost.RunCommand(comando);
            }
        }
    }
}
