using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Regista uma linha por sessão: em que versão do AutoCAD e do TSK TakeOff
    /// o plugin arrancou, e em que sistema.
    ///
    /// Regras que não se quebram:
    ///  - nada sai da máquina antes de a pessoa dizer que sim;
    ///  - nunca bloqueia o arranque do AutoCAD (corre em thread própria);
    ///  - nunca mostra erros ao utilizador (falha em silêncio);
    ///  - sem configuração de servidor, não envia nada — o plugin funciona igual;
    ///  - guarda sempre uma cópia local, mesmo quando o envio falha.
    ///
    /// O que NÃO é enviado, por decisão e não por esquecimento: o nome de
    /// utilizador do Windows, o nome da máquina e o domínio. Num cliente
    /// empresarial esses três campos juntos identificam uma pessoa concreta, e
    /// não são precisos para nada do que interessa saber — quantas instalações
    /// há, em que versões de AutoCAD, com que frequência. Para isso basta um
    /// identificador estável e opaco: ver <see cref="IdInstalacao"/>.
    /// </summary>
    public static class Telemetria
    {
        private const string Tabela = "plugin_sessoes";
        private static bool _jaRegistou;

        private static string Pasta => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TSKTakeOff");

        private static string FicheiroConfig => Path.Combine(Pasta, "telemetria.json");
        private static string FicheiroLog => Path.Combine(Pasta, "sessoes.log");
        private static string FicheiroFila => Path.Combine(Pasta, "sessoes_por_enviar.jsonl");
        private static string FicheiroEscolha => Path.Combine(Pasta, "privacidade.txt");

        // ==================================================================
        //  Consentimento
        // ==================================================================

        /// <summary>Resposta da pessoa à pergunta sobre envio de dados.</summary>
        public enum Escolha
        {
            /// <summary>Ainda não foi perguntado. Não se envia nada.</summary>
            PorPerguntar = 0,
            /// <summary>Disse que sim.</summary>
            Aceite = 1,
            /// <summary>Disse que não. Não se volta a perguntar.</summary>
            Recusada = 2
        }

        private static Escolha? _escolha;

        /// <summary>
        /// Escolha actual, lida do disco à primeira vez e guardada em memória.
        ///
        /// O valor vive num ficheiro à parte do <c>telemetria.json</c> de
        /// propósito: o `.json` traz a configuração do servidor e pode ser
        /// substituído por uma instalação ou por suporte, e uma reinstalação
        /// não pode ter como efeito lateral voltar a ligar o envio de dados
        /// contra a vontade de quem já disse que não.
        /// </summary>
        public static Escolha Consentimento
        {
            get
            {
                if (_escolha.HasValue) return _escolha.Value;
                try
                {
                    if (File.Exists(FicheiroEscolha))
                    {
                        string txt = File.ReadAllText(FicheiroEscolha).Trim().ToLowerInvariant();
                        if (txt.StartsWith("sim")) _escolha = Escolha.Aceite;
                        else if (txt.StartsWith("nao") || txt.StartsWith("não"))
                            _escolha = Escolha.Recusada;
                    }
                }
                catch { }
                if (!_escolha.HasValue) _escolha = Escolha.PorPerguntar;
                return _escolha.Value;
            }
        }

        /// <summary>
        /// Grava a decisão. Ao recusar, apaga também o que estava em fila:
        /// deixar lá ficar seria enviar mais tarde aquilo que a pessoa acabou
        /// de recusar.
        /// </summary>
        public static void GuardarEscolha(bool aceita)
        {
            _escolha = aceita ? Escolha.Aceite : Escolha.Recusada;
            try
            {
                Garantir();
                File.WriteAllText(FicheiroEscolha,
                    (aceita ? "sim" : "nao") + "  " +
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + Environment.NewLine);
            }
            catch { }

            if (!aceita)
            {
                try { if (File.Exists(FicheiroFila)) File.Delete(FicheiroFila); }
                catch { }
            }
        }

        /// <summary>
        /// Texto mostrado no pedido de consentimento e no comando
        /// TSKPRIVACIDADE. Está aqui, e não no diálogo, para que a lista de
        /// campos não possa divergir do que <see cref="MontarJson"/> envia.
        /// </summary>
        public static string TextoExplicativo()
        {
            return
                "O TSK TakeOff pode enviar um registo por cada arranque, para " +
                "sabermos em que versões de AutoCAD o plugin está a ser usado e " +
                "onde vale a pena investir.\r\n\r\n" +
                "É enviado:\r\n" +
                "   · um identificador da instalação, sem retorno ao teu nome\r\n" +
                "   · a versão do AutoCAD e do TSK TakeOff\r\n" +
                "   · a versão do Windows e o idioma\r\n" +
                "   · a data e a hora do arranque\r\n\r\n" +
                "NÃO é enviado:\r\n" +
                "   · o teu nome de utilizador, da máquina ou do domínio\r\n" +
                "   · nada dos teus desenhos, medições ou ficheiros Excel\r\n\r\n" +
                "O plugin funciona exactamente da mesma maneira se recusares, e " +
                "podes mudar de ideias a qualquer momento com o comando " +
                "TSKPRIVACIDADE.";
        }

        // ==================================================================
        //  Registo da sessão
        // ==================================================================

        /// <summary>
        /// Dispara o registo da sessão sem prender o arranque. Se ainda não
        /// houver resposta ao pedido de consentimento, não faz nada — a
        /// pergunta é feita quando a pessoa abre o painel, não a meio do
        /// arranque do AutoCAD.
        /// </summary>
        public static void RegistarSessao()
        {
            if (_jaRegistou) return;
            if (Consentimento != Escolha.Aceite) return;
            _jaRegistou = true;

            try
            {
                var t = new Thread(Executar) { IsBackground = true };
                t.Start();
            }
            catch { }
        }

        // ------------------------------------------------------------------
        private static void Executar()
        {
            try
            {
                string json = MontarJson();
                GravarLocal(json);

                if (!LerConfig(out string url, out string chave))
                    return;                       // sem configuração: fica só o local

                // Envia primeiro o que ficou em fila de sessões anteriores
                EnviarFila(url, chave);

                if (!Enviar(url, chave, json))
                    Enfileirar(json);
            }
            catch { /* telemetria nunca incomoda o utilizador */ }
        }

        /// <summary>
        /// Identificador estável e opaco desta instalação.
        ///
        /// É o mesmo identificador persistente usado pelo licenciamento:
        /// um GUID gerado na primeira utilização, cifrado com DPAPI. Não leva
        /// o nome da máquina, do utilizador nem do domínio para o servidor, e
        /// mantém-se igual quando o Windows é renomeado.
        /// </summary>
        internal static string IdInstalacao()
        {
            try { return Licenca.IdentificadorInstalacao; }
            catch { return "desconhecido"; }
        }

        private static string MontarJson()
        {
            // O contexto AutoCAD foi capturado no thread principal antes de
            // esta thread de background ser criada.
            string versaoAcad = AutocadRuntime.VersaoAutoCAD;
            string nomeAcad = AutocadRuntime.Produto;

            string versaoPlugin = "?";
            try
            {
                versaoPlugin = Assembly.GetExecutingAssembly()
                    .GetName().Version.ToString();
            }
            catch { }

            var sb = new StringBuilder();
            sb.Append("{");
            Campo(sb, "instalacao", IdInstalacao());
            sb.Append(",");
            Campo(sb, "produto", nomeAcad);
            sb.Append(",");
            Campo(sb, "versao_autocad", versaoAcad);
            sb.Append(",");
            Campo(sb, "versao_plugin", versaoPlugin);
            sb.Append(",");
            Campo(sb, "sistema", Environment.OSVersion.VersionString);
            sb.Append(",");
            Campo(sb, "cultura", CultureInfo.CurrentCulture.Name);
            sb.Append(",");
            Campo(sb, "inicio", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            sb.Append("}");
            return sb.ToString();
        }

        private static void Campo(StringBuilder sb, string nome, string valor)
        {
            sb.Append('"').Append(nome).Append("\":\"").Append(Escapar(valor)).Append('"');
        }

        private static string Escapar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\r", "").Replace("\n", " ");
        }

        // ------------------------------------------------------------------
        /// <summary>
        /// telemetria.json: {"url":"https://xxx.supabase.co","chave":"sb_publishable_..."}
        /// </summary>
        private static bool LerConfig(out string url, out string chave)
        {
            url = null; chave = null;
            try
            {
                if (!File.Exists(FicheiroConfig)) return false;
                string txt = File.ReadAllText(FicheiroConfig);
                url = ExtrairValor(txt, "url");
                chave = ExtrairValor(txt, "chave");
                return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(chave);
            }
            catch { return false; }
        }

        private static string ExtrairValor(string json, string campo)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                json, "\"" + campo + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        // ------------------------------------------------------------------
        private static bool Enviar(string url, string chave, string json)
        {
            try
            {
                ServicePointManager.SecurityProtocol =
                    SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

                var req = (HttpWebRequest)WebRequest.Create(
                    url.TrimEnd('/') + "/rest/v1/" + Tabela);
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers.Add("apikey", chave);
                req.Headers.Add("Authorization", "Bearer " + chave);
                req.Headers.Add("Prefer", "return=minimal");
                req.Timeout = 8000;
                req.ReadWriteTimeout = 8000;

                byte[] dados = Encoding.UTF8.GetBytes(json);
                req.ContentLength = dados.Length;
                using (var s = req.GetRequestStream()) s.Write(dados, 0, dados.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                    return (int)resp.StatusCode < 300;
            }
            catch
            {
                return false;   // sem rede, servidor em baixo, proxy… fica em fila
            }
        }

        private static void EnviarFila(string url, string chave)
        {
            try
            {
                if (!File.Exists(FicheiroFila)) return;
                var linhas = File.ReadAllLines(FicheiroFila);
                var falhadas = new System.Collections.Generic.List<string>();

                foreach (var linha in linhas)
                {
                    if (string.IsNullOrWhiteSpace(linha)) continue;
                    if (!Enviar(url, chave, linha)) falhadas.Add(linha);
                }

                if (falhadas.Count == 0) File.Delete(FicheiroFila);
                else File.WriteAllLines(FicheiroFila, falhadas);
            }
            catch { }
        }

        private static void Enfileirar(string json)
        {
            try
            {
                Garantir();
                // não deixa a fila crescer sem fim
                if (File.Exists(FicheiroFila) &&
                    new FileInfo(FicheiroFila).Length > 200 * 1024)
                    File.Delete(FicheiroFila);

                File.AppendAllText(FicheiroFila, json + Environment.NewLine);
            }
            catch { }
        }

        private static void GravarLocal(string json)
        {
            try
            {
                Garantir();
                File.AppendAllText(FicheiroLog,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + json +
                    Environment.NewLine);
            }
            catch { }
        }

        private static void Garantir()
        {
            if (!Directory.Exists(Pasta)) Directory.CreateDirectory(Pasta);
        }
    }
}
