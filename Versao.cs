using System;
using System.IO;
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: CommandClass(typeof(TSKTakeOff.VersaoCmd))]

namespace TSKTakeOff
{
    /// <summary>
    /// Que build é este, exactamente.
    ///
    /// Existe por causa do suporte. Um cliente escreve "não me aparecem os
    /// vãos" e a primeira pergunta é sempre a mesma: que versão tem? Sem uma
    /// resposta exacta, começa-se por adivinhar — e a correcção que se manda
    /// pode já lá estar, ou o defeito pode ser de uma versão que já não
    /// existe.
    ///
    /// Os números vêm dos atributos gravados pelo compilador a partir do
    /// Version.props. Não há aqui nada escrito à mão, e por isso não há como
    /// isto discordar da DLL que está mesmo a correr.
    /// </summary>
    public static class Versao
    {
        /// <summary>"1.2.6.11" — o que se diz a um cliente.</summary>
        public static string Curta
        {
            get
            {
                // Corta no PRIMEIRO separador, seja ele qual for.
                //
                // Cortava só no '+' (metadados de build do SemVer), mas o que
                // esta compilação grava é "1.2.6.11 (2026-08-13 16:32:37 UTC)"
                // — sem '+' nenhum. A "versão curta" saía com a data colada e
                // deixava de ser curta em todo o lado onde é mostrada.
                string s = Informativa ?? "";
                int corte = s.IndexOfAny(new[] { '+', ' ' });
                return corte > 0 ? s.Substring(0, corte) : s;
            }
        }

        /// <summary>
        /// "1.2.6.1 (2026-08-13 13:37:56 UTC)" — versão e instante da compilação.
        /// É esta que vai no licenciamento e na telemetria.
        /// </summary>
        public static string Informativa
        {
            get
            {
                try
                {
                    var a = Assembly.GetExecutingAssembly();
                    var at = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (at != null && !string.IsNullOrEmpty(at.InformationalVersion))
                        return at.InformationalVersion;

                    // Sem o atributo — compilação fora do fluxo normal — vale a
                    // versão do ficheiro. Menos informação, mas nunca vazio.
                    return a.GetName().Version.ToString();
                }
                catch { return "?"; }
            }
        }

        /// <summary>Ainda é candidata a versão, ou já é de produção?</summary>
        public static bool Candidata
        {
            get { return Curta.IndexOf("-rc", StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        /// <summary>O runtime em que esta DLL foi compilada.</summary>
        public static string Runtime
        {
            get
            {
#if NET48
                return ".NET Framework 4.8  (AutoCAD 2021-2024)";
#else
                return ".NET 8  (AutoCAD 2025-2026)";
#endif
            }
        }

        /// <summary>Onde está a DLL que está mesmo a correr.</summary>
        public static string Caminho
        {
            get
            {
                try { return Assembly.GetExecutingAssembly().Location; }
                catch { return "?"; }
            }
        }

        /// <summary>Uma linha, para o arranque e para os relatórios.</summary>
        public static string Resumo
        {
            get
            {
                return "TSK TakeOff " + Curta +
                       (Candidata ? "  (versão candidata — não é de produção)" : "");
            }
        }
    }

    public class VersaoCmd
    {
        [CommandMethod("TSKVERSAO")]
        public void MostrarVersao()
        {
            Util.Seguro("TSKVERSAO", () =>
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null) return;
                var ed = doc.Editor;

                var sb = new System.Text.StringBuilder();
                sb.Append("\n--- TSK TakeOff ---");
                sb.Append("\n  versão           : " + Versao.Curta);
                sb.Append("\n  build            : " + Versao.Informativa);
                sb.Append("\n  runtime          : " + Versao.Runtime);

                try
                {
                    string acad = AcadApp.GetSystemVariable("ACADVER") as string ?? "?";
                    sb.Append("\n  AutoCAD          : " + acad);
                }
                catch { }

                sb.Append("\n  DLL              : " + Versao.Caminho);

                // A data do ficheiro é a prova final: se alguém instalou por
                // cima sem reiniciar o AutoCAD, a DLL em memória é a antiga e
                // a do disco é a nova. Já aconteceu, e leva meia hora a
                // perceber sem este número.
                try
                {
                    var fi = new FileInfo(Versao.Caminho);
                    if (fi.Exists)
                        sb.Append("\n  gravada em       : " +
                                  fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                }
                catch { }

                sb.Append("\n  licença          : " + Licenca.Resumo());

                if (MapaQuantidades.Existe)
                {
                    sb.Append("\n  mapa importado   : " +
                              MapaQuantidades.Nos.Count + " linhas, " +
                              MapaQuantidades.Artigos.Count + " artigos");

                    // Por que ORDEM o mapa está, não só quantos são.
                    //
                    // A folha entrega-se por esta ordem, e quando ela sai
                    // errada não havia como distinguir "o código não ordena"
                    // de "esta DLL ainda não tem a correcção". Discutiu-se isso
                    // durante uma tarde inteira sem dado nenhum. O primeiro e o
                    // último artigo respondem à pergunta num relance.
                    var arts = MapaQuantidades.Artigos;
                    if (arts.Count > 1)
                        sb.Append("\n  ordem do mapa    : " +
                                  arts[0].Codigo + "  …  " +
                                  arts[arts.Count - 1].Codigo +
                                  "   (é por esta ordem que a folha sai)");
                }
                else
                    sb.Append("\n  mapa importado   : nenhum (TSKMQT para importar)");

                if (Versao.Candidata)
                    sb.Append("\n\n  ATENÇÃO: versão candidata. Não deve ser entregue a um" +
                              "\n  cliente sem passar a Fase 3 do plano de testes.");

                ed.WriteMessage(sb.ToString() + "\n");
            });
        }
    }
}
