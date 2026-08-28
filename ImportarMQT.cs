using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TSKTakeOff.MqtCmd))]

namespace TSKTakeOff
{
    /// <summary>
    /// Importa o mapa de quantidades do cliente — só o articulado, sem números.
    ///
    /// Há dois feitios de mapa em circulação e este leitor conhece os dois,
    /// porque foram medidos contra ficheiros de obra a sério:
    ///
    /// FORMATO A — "Item / Designação / Un / Qt / Comp / Largura / Altura".
    ///   O cabeçalho aparece a meio da folha, por baixo do bloco de
    ///   identificação da obra, e por isso procura-se em vez de se assumir a
    ///   linha. A hierarquia está nos pontos do código. Um artigo distingue-se
    ///   de um capítulo por ter unidade.
    ///
    /// FORMATO B — "art / art_parciais / auxiliar / … / descrição / = / comp".
    ///   É o das macros MD/EO/QT. Muito melhor construído: traz o NÍVEL escrito
    ///   numa coluna, e uma segunda coluna "art" que só está preenchida nos
    ///   artigos. Vale a pena preferi-lo — no mapa do Palácio há dois artigos
    ///   legítimos com a unidade em branco, que a regra do formato A perdia.
    ///
    /// O que NUNCA se importa: quantidades, dimensões, fórmulas, totais. O
    /// mapa do cliente entra vazio de números. Quem os põe é a medição.
    /// </summary>
    public static class ImportarMQT
    {
        /// <summary>O que se encontrou numa folha, antes de decidir importar.</summary>
        public class ResumoFolha
        {
            public string Nome = "";
            public string Formato = "";      // "A", "B" ou ""
            public int Nos;
            public int Artigos;
            public string Motivo = "";       // porque foi ignorada
            public List<MapaQuantidades.No> Conteudo = new List<MapaQuantidades.No>();

            public bool Serve { get { return Artigos > 0 || Nos > 0; } }

            public override string ToString()
            {
                if (!Serve)
                    return Nome + "   —   " + (Motivo == "" ? "nada reconhecível" : Motivo);
                return string.Format("{0}   —   formato {1}, {2} linhas, {3} artigos",
                                     Nome, Formato, Nos, Artigos);
            }
        }

        private static readonly string[] Unidades =
        {
            "m", "m2", "m3", "ml", "un", "und", "unid", "vg", "kg", "ton",
            "h", "dia", "cj", "pa", "km", "l"
        };

        // ==================================================================
        // Leitura do livro
        // ==================================================================

        /// <summary>
        /// Espreita todas as folhas do livro e diz o que cada uma tem, sem
        /// tocar no desenho. É com isto que se escolhe o que importar — um
        /// mapa de quantidades costuma vir espalhado por uma folha por
        /// capítulo, com folhas de resumo pelo meio que não são articulado.
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

                // Se o utilizador já tem o Excel aberto, o COM devolve essa
                // instância. Nesse caso não se esconde nem se fecha nada —
                // seria o comando de importar a fechar o trabalho dele.
                try { nossa = ((int)app.Workbooks.Count) == 0; } catch { nossa = false; }
                if (nossa) app.Visible = false;
                try { app.DisplayAlerts = false; } catch { }
                try { app.AskToUpdateLinks = false; } catch { }

                // ReadOnly. O ficheiro do cliente não se toca.
                wb = app.Workbooks.Open(ficheiro, 0, true);

                foreach (dynamic ws in wb.Worksheets)
                {
                    var r = new ResumoFolha { Nome = (string)ws.Name };
                    LerFolha(ws, r);
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

        private static void LerFolha(dynamic ws, ResumoFolha r)
        {
            object[,] celulas;
            int nLinhas, nColunas;
            if (!LerBloco(ws, out celulas, out nLinhas, out nColunas))
            { r.Motivo = "vazia"; return; }

            // O formato B declara-se sem ambiguidade quando existe; o A é o
            // caso geral. Por isso tenta-se o B primeiro.
            var nos = LerFormatoB(celulas, nLinhas, nColunas, r.Nome);
            if (nos != null) r.Formato = "B";
            else
            {
                nos = LerFormatoA(celulas, nLinhas, nColunas, r.Nome);
                if (nos != null) r.Formato = "A";
            }

            if (nos == null) { r.Motivo = "sem cabeçalho reconhecível"; return; }

            r.Conteudo = nos;
            r.Nos = nos.Count;
            foreach (var n in nos) if (n.EhArtigo) r.Artigos++;
            if (r.Nos == 0) r.Motivo = "cabeçalho encontrado mas sem artigos";
        }

        // ==================================================================
        // FORMATO A — Item / Designação / Un
        // ==================================================================

        private static List<MapaQuantidades.No> LerFormatoA(
            object[,] c, int nLinhas, int nColunas, string origem)
        {
            int linhaCab = -1, colItem = -1, colDesc = -1, colUn = -1;

            // O cabeçalho não está na linha 1: por cima dele vem o bloco de
            // identificação da obra. Procura-se nas primeiras 40 linhas.
            int ate = Math.Min(40, nLinhas);
            for (int r = 1; r <= ate && linhaCab < 0; r++)
            {
                int ci = -1, cd = -1, cu = -1;
                for (int col = 1; col <= nColunas; col++)
                {
                    string t = Texto(c, r, col).ToLowerInvariant();
                    if (t.Length == 0) continue;
                    if (ci < 0 && t.StartsWith("item")) ci = col;
                    else if (cd < 0 && t.StartsWith("designa")) cd = col;
                    else if (cu < 0 && (t == "un" || t == "und" ||
                                        t == "unid" || t == "unidade")) cu = col;
                }
                if (ci > 0 && cd > 0)
                { linhaCab = r; colItem = ci; colDesc = cd; colUn = cu; }
            }
            if (linhaCab < 0) return null;

            var nos = new List<MapaQuantidades.No>();
            int ordem = 0;

            for (int r = linhaCab + 1; r <= nLinhas; r++)
            {
                string cod = Texto(c, r, colItem);
                if (cod.Length == 0) continue;      // linha de medição: fora

                // Alguns livros repetem o cabeçalho a meio da folha.
                string baixo = cod.ToLowerInvariant();
                if (baixo == "item" || baixo == "art") continue;

                string des = Texto(c, r, colDesc);
                if (des.Length == 0) continue;      // código órfão, sem texto

                string un = colUn > 0 ? Texto(c, r, colUn) : "";
                bool temUn = EhUnidade(un);

                ordem++;
                nos.Add(new MapaQuantidades.No
                {
                    Ordem = ordem,
                    Codigo = cod,
                    Designacao = des,
                    Unidade = temUn ? un : "",
                    Nivel = NivelPorPontos(cod),
                    // Neste formato não há coluna que declare o que é artigo.
                    // Ter unidade é o único sinal disponível — e funciona:
                    // 177 dos 262 nós do mapa do Lumare.
                    EhArtigo = temUn,
                    Origem = origem
                });
            }

            return nos.Count > 0 ? nos : null;
        }

        // ==================================================================
        // FORMATO B — art / auxiliar / nível / art / descrição / =
        // ==================================================================

        private static List<MapaQuantidades.No> LerFormatoB(
            object[,] c, int nLinhas, int nColunas, string origem)
        {
            int linhaCab = -1, colCod = -1, colFlag = -1;
            int colNivel = -1, colDesc = -1, colUn = -1;

            int ate = Math.Min(20, nLinhas);
            for (int r = 1; r <= ate && linhaCab < 0; r++)
            {
                var arts = new List<int>();
                int cd = -1, caux = -1, cun = -1;
                for (int col = 1; col <= nColunas; col++)
                {
                    string t = Texto(c, r, col).ToLowerInvariant();
                    if (t.Length == 0) continue;
                    if (t == "art") arts.Add(col);
                    else if (cd < 0 && t.StartsWith("descri")) cd = col;
                    else if (caux < 0 && t == "auxiliar") caux = col;
                    else if (cun < 0 && t == "=") cun = col;
                }

                // Duas colunas "art" e uma "descrição" é a assinatura deste
                // formato. Com uma só "art" ainda serve, mas perde-se a
                // bandeira e cai-se na unidade como no formato A.
                if (arts.Count >= 1 && cd > 0)
                {
                    linhaCab = r;
                    colCod = arts[0];
                    colFlag = arts.Count > 1 ? arts[1] : -1;
                    colNivel = caux > 0 ? caux + 1 : -1;
                    colDesc = cd;
                    colUn = cun;
                }
            }
            if (linhaCab < 0) return null;

            var nos = new List<MapaQuantidades.No>();
            int ordem = 0;

            for (int r = linhaCab + 1; r <= nLinhas; r++)
            {
                string cod = Texto(c, r, colCod);
                if (cod.Length == 0) continue;

                string des = Texto(c, r, colDesc);
                if (des.Length == 0) continue;

                // O nível vem escrito numa coluna. É melhor do que contar
                // pontos, e é o que permite a este formato ter capítulos com
                // códigos que não seguem a numeração da hierarquia.
                int nivel = 0;
                if (colNivel > 0)
                {
                    double? v = Numero(c, r, colNivel);
                    if (v.HasValue) nivel = (int)Math.Round(v.Value);
                }
                if (nivel <= 0) nivel = NivelPorPontos(cod);

                string un = colUn > 0 ? Texto(c, r, colUn) : "";
                bool temUn = EhUnidade(un);

                // A bandeira: a segunda coluna "art" só está preenchida nos
                // artigos. Onde ela existe, manda ela — apanha os artigos que
                // vêm com a unidade em branco, que a regra da unidade perdia.
                bool ehArtigo = colFlag > 0
                    ? Texto(c, r, colFlag).Length > 0
                    : temUn;

                ordem++;
                nos.Add(new MapaQuantidades.No
                {
                    Ordem = ordem,
                    Codigo = cod,
                    Designacao = des,
                    Unidade = temUn ? un : "",
                    Nivel = nivel,
                    EhArtigo = ehArtigo,
                    Origem = origem
                });
            }

            return nos.Count > 0 ? nos : null;
        }

        // ==================================================================
        // Auxiliares de leitura
        // ==================================================================

        /// <summary>
        /// Traz a folha inteira numa só chamada COM. Ler célula a célula uma
        /// folha de 1500 linhas por 26 colunas são 39 000 idas ao Excel e
        /// demora minutos; assim é uma.
        /// </summary>
        private static bool LerBloco(dynamic ws, out object[,] celulas,
            out int nLinhas, out int nColunas)
        {
            celulas = null; nLinhas = 0; nColunas = 0;
            try
            {
                dynamic usado = ws.UsedRange;

                // Rows.Count é a ALTURA do intervalo, não a última linha. Numa
                // folha que só começa a meio, somar a linha de início é o que
                // evita cortar o fim.
                nLinhas = (int)usado.Row + (int)usado.Rows.Count - 1;
                nColunas = (int)usado.Column + (int)usado.Columns.Count - 1;
                if (nLinhas < 2 || nColunas < 2) return false;

                // Folhas absurdas acontecem — uma célula perdida lá em baixo
                // faz o UsedRange ir a 100 000 linhas. Corta-se, com aviso.
                if (nLinhas > 20000)
                {
                    PaletteHost.Log("MQT: folha com " + nLinhas +
                                    " linhas; a ler só as primeiras 20000.");
                    nLinhas = 20000;
                }

                // O cabeçalho de um mapa de quantidades nunca passa da coluna
                // 30 — o formato mais largo que encontrei usa 26. Ler 60
                // colunas de 20 000 linhas seriam 1,2 milhões de células
                // atadas em objectos: perto de 50 MB, por folha, e o
                // inventário percorre TODAS as folhas do livro. Num portátil
                // de 8 GB com o AutoCAD e o Excel abertos, isso conta.
                //
                // Lê-se o que é preciso: nenhuma coluna à direita do cabeçalho
                // reconhecido serve para nada.
                const int ColunasUteis = 30;
                if (nColunas > ColunasUteis) nColunas = ColunasUteis;

                dynamic bloco = ws.Range[ws.Cells[1, 1], ws.Cells[nLinhas, nColunas]];
                celulas = bloco.Value2 as object[,];

                // Largar já o intervalo. Um RCW por folha fica à espera do GC,
                // e o inventário cria um por cada — em livros grandes é o
                // suficiente para se notar.
                Libertar(bloco);
                Libertar(usado);

                return celulas != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Larga um objecto COM sem fazer barulho se não der.
        ///
        /// O .NET acaba por os libertar sozinho, mas «acaba por» pode ser
        /// muito depois — e entretanto o Excel fica com referências vivas que
        /// o impedem de fechar. Num livro com dezenas de folhas isso é a
        /// diferença entre o Excel sair e ficar pendurado em memória.
        /// </summary>
        private static void Libertar(object com)
        {
            if (com == null) return;
            try
            {
                if (System.Runtime.InteropServices.Marshal.IsComObject(com))
                    System.Runtime.InteropServices.Marshal.ReleaseComObject(com);
            }
            catch { }
        }

        private static string Texto(object[,] c, int linha, int coluna)
        {
            if (c == null || coluna < 1) return "";
            try
            {
                object v = c[linha, coluna];
                if (v == null) return "";
                // As designações vêm com quebras de linha lá dentro (os vãos
                // "AL208\nVão de duas folhas…"). Numa lista de escolha isso
                // parte a linha ao meio.
                return v.ToString()
                        .Replace("\r", " ").Replace("\n", " ")
                        .Replace("  ", " ").Trim();
            }
            catch { return ""; }
        }

        private static double? Numero(object[,] c, int linha, int coluna)
        {
            if (c == null || coluna < 1) return null;
            try
            {
                object v = c[linha, coluna];
                if (v == null) return null;
                if (v is double) return (double)v;
                double d;
                return double.TryParse(v.ToString().Replace(',', '.'),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out d)
                    ? d : (double?)null;
            }
            catch { return null; }
        }

        private static bool EhUnidade(string s)
        {
            string u = (s ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (u.Length == 0) return false;
            u = u.Replace("²", "2").Replace("³", "3");
            foreach (string x in Unidades) if (u == x) return true;
            return false;
        }

        /// <summary>
        /// Profundidade pelo número de segmentos do código. Tolera o ponto
        /// final ("8.3.3.") e o sufixo de letra ("10.1.1.306A"), que existem
        /// nos ficheiros reais. Um código que não comece por dígito — como o
        /// "Sem Ref" que aparece duas vezes no mapa do Lumare — fica no
        /// nível 1, porque não há nada nele que diga onde encaixa.
        /// </summary>
        private static int NivelPorPontos(string codigo)
        {
            string c = (codigo ?? "").Trim().TrimEnd('.');
            if (c.Length == 0) return 1;
            if (!char.IsDigit(c[0])) return 1;

            int n = 0;
            foreach (string p in c.Split('.')) if (p.Length > 0) n++;
            return n < 1 ? 1 : n;
        }

        // ==================================================================
        // Renumeração
        // ==================================================================

        /// <summary>
        /// Junta o que veio de várias folhas numa lista só, renumerando a
        /// ordem de fim a fim. Cada folha vinha numerada a partir de 1; sem
        /// isto, o capítulo 4.9 e o 1.1 disputavam a mesma posição e a folha
        /// saía baralhada.
        /// </summary>
        public static List<MapaQuantidades.No> Juntar(IList<ResumoFolha> folhas)
        {
            var todos = new List<MapaQuantidades.No>();
            int ordem = 0;
            foreach (var f in folhas)
                foreach (var n in f.Conteudo)
                {
                    n.Ordem = ++ordem;
                    todos.Add(n);
                }
            return todos;
        }
    }

    // ======================================================================
    // Comando
    // ======================================================================

    public class MqtCmd
    {
        [CommandMethod("TSKMQT")]
        public void ImportarMapa()
        {
            Util.Seguro("TSKMQT", () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var ed = doc.Editor;

                string ficheiro = EscolherFicheiro();
                if (ficheiro == null) return;

                ed.WriteMessage("\nTSK — a ler o mapa de quantidades…\n");

                List<ImportarMQT.ResumoFolha> folhas;
                try { folhas = ImportarMQT.Inventario(ficheiro); }
                catch (System.Exception ex)
                {
                    ed.WriteMessage("\nNão consegui abrir o ficheiro: " + ex.Message + "\n");
                    return;
                }

                var escolhidas = EscolherFolhas(folhas);
                if (escolhidas == null || escolhidas.Count == 0) return;

                var nos = ImportarMQT.Juntar(escolhidas);
                if (nos.Count == 0)
                {
                    ed.WriteMessage("\nNão encontrei artigos nessas folhas.\n");
                    return;
                }

                int quantosArtigos = 0;
                foreach (var n in nos) if (n.EhArtigo) quantosArtigos++;

                if (MapaQuantidades.Existe)
                {
                    var r = MessageBox.Show(
                        "Este desenho já tem um mapa de quantidades com " +
                        MapaQuantidades.Nos.Count + " linhas.\n\n" +
                        "Substituir pelo novo?\n\n" +
                        "As medições já feitas não se perdem — continuam ligadas " +
                        "ao artigo pelo código e designação. As que não " +
                        "encontrarem o seu artigo no mapa novo saem no fim do " +
                        "bloco do seu serviço, e o comando diz quais são.",
                        "TSK TakeOff — mapa de quantidades",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                    if (r != DialogResult.OK) return;
                }

                if (!MapaQuantidades.Guardar(nos))
                {
                    ed.WriteMessage("\nNão consegui guardar o mapa no desenho.\n");
                    return;
                }

                ed.WriteMessage(string.Format(
                    "\nTSK — mapa importado: {0} linhas, {1} artigos, de {2} folha(s).\n" +
                    "Escreva no campo Artigo da paleta para procurar.\n",
                    nos.Count, quantosArtigos, escolhidas.Count));

                // Mapa novo, unidades novas: o que já foi avisado pode ter
                // deixado de ser problema, ou passado a sê-lo.
                AlvRepo.EsquecerAvisos();

                RelatarOrfas(ed);
                PaletteHost.RefreshData();
            });
        }

        /// <summary>Apaga o mapa deste desenho.</summary>
        [CommandMethod("TSKMQTLIMPAR")]
        public void LimparMapa()
        {
            Util.Seguro("TSKMQTLIMPAR", () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                if (!MapaQuantidades.Existe)
                {
                    doc.Editor.WriteMessage("\nEste desenho não tem mapa importado.\n");
                    return;
                }

                var r = MessageBox.Show(
                    "Apagar o mapa de quantidades deste desenho?\n\n" +
                    "As medições ficam como estão — o artigo continua escrito " +
                    "em cada uma. O que se perde é a lista de escolha e a ordem " +
                    "do articulado na folha.",
                    "TSK TakeOff", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;

                MapaQuantidades.Apagar();
                doc.Editor.WriteMessage("\nTSK — mapa apagado.\n");
                PaletteHost.RefreshData();
            });
        }

        /// <summary>
        /// Diz quais das medições já feitas têm um artigo que o mapa novo não
        /// conhece. Sem isto, elas iam para o fim da folha em silêncio e só se
        /// dava por isso na conferência.
        /// </summary>
        private static void RelatarOrfas(Autodesk.AutoCAD.EditorInput.Editor ed)
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                List<Parede> paredes;
                List<MedFachada> fachadas;
                List<MedItem> lineares;
                List<MedContagem> contagens;
                Leitura.Tudo(doc.Database, out paredes, out fachadas,
                             out lineares, out contagens);

                var orfas = new Dictionary<string, int>();
                int semArtigo = 0;
                foreach (var p in paredes)
                {
                    string a = p.Artigo ?? "";
                    if (a.Length == 0) { semArtigo++; continue; }
                    if (MapaQuantidades.Procurar(a) != null) continue;

                    int i = a.IndexOf('\u001f');
                    string rotulo = i >= 0 ? a.Substring(0, i) + " " + a.Substring(i + 1) : a;
                    if (rotulo.Length > 60) rotulo = rotulo.Substring(0, 60) + "…";
                    if (!orfas.ContainsKey(rotulo)) orfas[rotulo] = 0;
                    orfas[rotulo]++;
                }

                // As que nunca tiveram artigo. É o caso de quem mediu ANTES de
                // o mapa chegar — e era aqui que a folha parecia baralhada: as
                // classificadas sobem para o sítio delas no articulado e estas
                // ficam para trás, sem que nada o dissesse.
                if (semArtigo > 0)
                    ed.WriteMessage(string.Format(
                        "\n{0} medição(ões) ainda sem artigo. Saem no FIM da folha, " +
                        "depois de tudo o que já está classificado.\n" +
                        "Para as arrumar: escolha o artigo em «Artigo (mapa)» no " +
                        "painel, seleccione-as na grelha (Ctrl ou Shift para várias) " +
                        "e carregue em «Reclassificar».\n", semArtigo));

                if (orfas.Count == 0) return;

                ed.WriteMessage("\nArtigos que este mapa não conhece " +
                                "(saem no fim do bloco do seu serviço):\n");
                foreach (var kv in orfas)
                    ed.WriteMessage("  " + kv.Key + "   (" + kv.Value + " medições)\n");
            }
            catch { }
        }

        private static string EscolherFicheiro()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "TSK TakeOff — importar mapa de quantidades do cliente";
                dlg.Filter = "Folhas de Excel (*.xlsx;*.xlsm;*.xls)|*.xlsx;*.xlsm;*.xls|" +
                             "Todos os ficheiros (*.*)|*.*";
                dlg.CheckFileExists = true;
                // ShowDialog e não ShowModalDialog: um OpenFileDialog é um
                // CommonDialog, não um Form, e o ShowModalDialog do AutoCAD só
                // aceita Forms.
                return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
            }
        }

        /// <summary>
        /// Que folhas importar. Um mapa de quantidades vem quase sempre com
        /// uma folha por capítulo e folhas de resumo pelo meio; por omissão
        /// ficam marcadas as que têm artigos e desmarcadas as outras.
        /// </summary>
        private static List<ImportarMQT.ResumoFolha> EscolherFolhas(
            List<ImportarMQT.ResumoFolha> folhas)
        {
            using (var frm = new Form())
            using (var lista = new CheckedListBox())
            using (var ok = new Button())
            using (var cancelar = new Button())
            using (var rot = new Label())
            using (var previa = new ListBox())
            {
                frm.Text = "TSK TakeOff — que folhas do mapa importar";
                frm.StartPosition = FormStartPosition.CenterScreen;
                frm.FormBorderStyle = FormBorderStyle.Sizable;
                frm.MinimizeBox = false;
                frm.MaximizeBox = true;
                frm.ClientSize = new Size(980, 720);

                // O MinimumSize é o que garante o botão Importar à vista.
                // Sem ele, o painel de baixo saía do ecrã em monitores mais
                // pequenos ou com o Windows a 125% — e ficava uma janela sem
                // maneira aparente de a confirmar.
                frm.MinimumSize = new Size(700, 520);

                // Acompanha a escala do Windows. Num portátil a 150% a janela
                // vinha com o conteúdo maior do que ela própria.
                frm.AutoScaleMode = AutoScaleMode.Dpi;

                int comArtigos = 0;
                foreach (var f in folhas) if (f.Artigos > 0) comArtigos++;

                rot.Text = folhas.Count + " folha(s) no livro, " + comArtigos +
                           " com articulado.\n" +
                           "Só entra o articulado: código, designação e unidade. " +
                           "Quantidades, dimensões e fórmulas ficam de fora — os " +
                           "números nascem da medição.";
                rot.Dock = DockStyle.Top;
                rot.Height = 56;
                rot.Padding = new Padding(10, 8, 10, 0);

                lista.Dock = DockStyle.Top;
                // Mais alta: um livro de obra traz sete ou mais folhas e não
                // vale a pena obrigar a rolar para as ver todas.
                lista.Height = 230;
                lista.CheckOnClick = true;
                foreach (var f in folhas)
                {
                    lista.Items.Add(f, f.Artigos > 0);
                }

                var rot2 = new Label
                {
                    Text = "Pré-visualização do que vai ser importado:",
                    Dock = DockStyle.Top,
                    Height = 22,
                    Padding = new Padding(10, 4, 10, 0)
                };

                previa.Dock = DockStyle.Fill;
                previa.Font = new Font(FontFamily.GenericMonospace, 8.25f);
                previa.IntegralHeight = false;

                EventHandler refazer = (s, e) =>
                {
                    previa.BeginUpdate();
                    previa.Items.Clear();
                    int n = 0;
                    foreach (var obj in lista.CheckedItems)
                    {
                        var f = obj as ImportarMQT.ResumoFolha;
                        if (f == null) continue;
                        foreach (var no in f.Conteudo)
                        {
                            // A pré-visualização é para se ver a árvore, não
                            // para se ler tudo: 262 linhas numa ListBox são
                            // lentas de construir a cada clique.
                            if (n++ > 400) { previa.Items.Add("   …"); break; }
                            previa.Items.Add(
                                new string(' ', Math.Max(0, (no.Nivel - 1) * 3)) +
                                (no.EhArtigo ? "• " : "  ") + no.ToString());
                        }
                        if (n > 400) break;
                    }
                    if (previa.Items.Count == 0)
                        previa.Items.Add("   (nenhuma folha marcada)");
                    previa.EndUpdate();
                };
                lista.ItemCheck += (s, e) =>
                {
                    // O ItemCheck dispara ANTES de o estado mudar. Sem o
                    // BeginInvoke, a pré-visualização mostrava sempre a
                    // selecção anterior.
                    frm.BeginInvoke((MethodInvoker)(() => refazer(s, EventArgs.Empty)));
                };

                // FlowLayoutPanel com fluxo da direita para a esquerda, em vez
                // de coordenadas fixas.
                //
                // Com SetBounds, a largura do painel ainda é a de omissão no
                // momento em que se calculam as posições — os botões ficavam
                // no sítio errado, e numa janela redimensionável encalhavam a
                // meio ao alargá-la. Assim ficam sempre encostados à direita,
                // seja qual for o tamanho.
                var baixo = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 58,
                    FlowDirection = FlowDirection.RightToLeft,
                    Padding = new Padding(10, 10, 12, 10)
                };

                ok.Text = "Importar";
                ok.DialogResult = DialogResult.OK;
                ok.Size = new Size(130, 34);
                cancelar.Text = "Cancelar";
                cancelar.DialogResult = DialogResult.Cancel;
                cancelar.Size = new Size(120, 34);
                cancelar.Margin = new Padding(8, 0, 0, 0);

                // Primeiro o Cancelar: com o fluxo da direita para a esquerda,
                // o primeiro fica MAIS à direita. O Importar vem a seguir, à
                // esquerda dele — a ordem habitual do Windows.
                baixo.Controls.Add(cancelar);
                baixo.Controls.Add(ok);

                frm.Controls.Add(previa);
                frm.Controls.Add(rot2);
                frm.Controls.Add(lista);
                frm.Controls.Add(rot);
                frm.Controls.Add(baixo);
                frm.AcceptButton = ok;
                frm.CancelButton = cancelar;

                refazer(null, EventArgs.Empty);

                if (AcadApp.ShowModalDialog(frm) != DialogResult.OK) return null;

                var escolhidas = new List<ImportarMQT.ResumoFolha>();
                foreach (var obj in lista.CheckedItems)
                {
                    var f = obj as ImportarMQT.ResumoFolha;
                    if (f != null && f.Conteudo.Count > 0) escolhidas.Add(f);
                }
                return escolhidas;
            }
        }
    }
}
