using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Licenciamento por código, contra o Supabase.
    ///
    /// Princípios que não se quebram:
    ///  - nunca bloqueia nem atrasa o arranque do AutoCAD (verifica em thread
    ///    própria e só quando é preciso);
    ///  - sem internet, vale a validade guardada localmente — ninguém fica
    ///    parado em obra por causa de rede;
    ///  - expirada, bloqueia MEDIR mas deixa ver o painel e exportar: nunca
    ///    se retém trabalho já feito;
    ///  - o ficheiro local é cifrado com DPAPI (preso ao utilizador Windows
    ///    daquela máquina), por isso não se copia de posto para posto.
    /// </summary>
    public static class Licenca
    {
        private const string RpcActivar = "activar_licenca";
        private const string RpcVerificar = "verificar_licenca";
        private const string RpcTrial = "pedir_trial";

        /// <summary>Contacto comercial mostrado quando a avaliação termina.</summary>
        public const string EmailContacto = "tsantos.fullstack@gmail.com";

        /// <summary>Código guardado localmente quando a licença é um trial.</summary>
        private const string CodigoTrial = "TRIAL";

        /// <summary>Dias sem conseguir falar com o servidor antes de bloquear.</summary>
        private const int DiasToleranciaOffline = 30;

        private static Estado _estado;
        private static bool _verificado;
        private static readonly object _lock = new object();

        // ------------------------------------------------------------------
        private class Estado
        {
            public string Codigo = "";
            public string Cliente = "";
            public string Email = "";
            public DateTime ExpiraEm = DateTime.MinValue;   // UTC
            public DateTime UltimaVerificacao = DateTime.MinValue;
            public string Motivo = "";
        }

        private static string Pasta => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TSKTakeOff");

        private static string Ficheiro => Path.Combine(Pasta, "licenca.dat");

        /// <summary>Identificador histórico de activações pagas: máquina + utilizador.</summary>
        public static string Maquina =>
            (Environment.MachineName + "\\" + Environment.UserName).ToUpperInvariant();

        /// <summary>Identidade estável usada pelo trial (mantido para compatibilidade).</summary>
        public static string NomePC => IdentificadorInstalacao;

        private static string FicheiroInstalacao => Path.Combine(Pasta, "instalacao.dat");
        private static string _identificadorInstalacao;

        /// <summary>Obtém ou cria a identidade persistente deste posto.</summary>
        public static string IdentificadorInstalacao
        {
            get
            {
                lock (_lock)
                {
                    if (!string.IsNullOrEmpty(_identificadorInstalacao))
                        return _identificadorInstalacao;

                    try
                    {
                        if (File.Exists(FicheiroInstalacao))
                        {
                            byte[] cifrado = File.ReadAllBytes(FicheiroInstalacao);
                            byte[] claro = ProtectedData.Unprotect(
                                cifrado, null, DataProtectionScope.CurrentUser);
                            string lido = Encoding.UTF8.GetString(claro).Trim();
                            Guid ignorar;
                            if (Guid.TryParseExact(lido, "N", out ignorar))
                            {
                                _identificadorInstalacao = lido.ToUpperInvariant();
                                return _identificadorInstalacao;
                            }
                        }
                    }
                    catch { }

                    _identificadorInstalacao = Guid.NewGuid().ToString("N").ToUpperInvariant();
                    try
                    {
                        if (!Directory.Exists(Pasta)) Directory.CreateDirectory(Pasta);
                        byte[] cifrado = ProtectedData.Protect(
                            Encoding.UTF8.GetBytes(_identificadorInstalacao),
                            null, DataProtectionScope.CurrentUser);
                        File.WriteAllBytes(FicheiroInstalacao, cifrado);
                    }
                    catch { }

                    return _identificadorInstalacao;
                }
            }
        }

        // ==================================================================
        // Estado público
        // ==================================================================

        /// <summary>Há licença válida (com a tolerância offline aplicada)?</summary>
        public static bool Valida
        {
            get
            {
                var e = Carregar();
                if (e == null || string.IsNullOrEmpty(e.Codigo)) return false;
                if (RelogioAndouParaTras(e)) return false;
                return e.ExpiraEm > DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Margem antes de se considerar que o relógio recuou. Existe porque
        /// há razões inocentes para o relógio andar uns segundos para trás:
        /// sincronização NTP, hibernação, mudança de fuso mal feita.
        /// </summary>
        private static readonly TimeSpan FolgaDoRelogio = TimeSpan.FromHours(6);

        /// <summary>
        /// O relógio do computador foi atrasado desde a última verificação?
        ///
        /// A validade era só <c>ExpiraEm &gt; UtcNow</c>, comparada com o
        /// relógio LOCAL. Bastava pôr a data do Windows atrás para esticar a
        /// avaliação — ou uma licença revogada — indefinidamente, sem rede e
        /// sem deixar rasto. A <c>UltimaVerificacao</c> já estava guardada;
        /// só não era lida por ninguém.
        ///
        /// A ideia é simples: o tempo não anda para trás. Se o relógio está
        /// antes do instante em que falámos com o servidor pela última vez,
        /// alguém lhe mexeu — e a validade local deixa de valer até haver uma
        /// verificação online que reponha a verdade.
        /// </summary>
        private static bool RelogioAndouParaTras(Estado e)
        {
            try
            {
                if (e.UltimaVerificacao == DateTime.MinValue) return false;
                return DateTime.UtcNow + FolgaDoRelogio < e.UltimaVerificacao;
            }
            catch { return false; }
        }

        public static string Cliente => Carregar()?.Cliente ?? "";

        /// <summary>E-mail informado no início da avaliação deste posto.</summary>
        public static string Email => Carregar()?.Email ?? "";

        public static DateTime ExpiraEm => Carregar()?.ExpiraEm ?? DateTime.MinValue;

        public static int DiasRestantes
        {
            get
            {
                var e = Carregar();
                if (e == null || e.ExpiraEm <= DateTime.UtcNow) return 0;
                return (int)Math.Ceiling((e.ExpiraEm - DateTime.UtcNow).TotalDays);
            }
        }

        public static string UltimoMotivo => Carregar()?.Motivo ?? "";

        /// <summary>Descrição curta para a linha de comando e para o painel.</summary>
        public static string Resumo()
        {
            var e = Carregar();
            if (e == null || string.IsNullOrEmpty(e.Codigo))
                return "sem licença — use TSKLICENCA para activar";
            if (e.ExpiraEm <= DateTime.UtcNow)
                return "licença expirada em " + e.ExpiraEm.ToLocalTime().ToString("dd/MM/yyyy") +
                       (string.IsNullOrEmpty(e.Motivo) ? "" : " (" + e.Motivo + ")") +
                       ". Contacte " + EmailContacto + " para continuar.";

            if (e.Codigo == CodigoTrial)
                return "avaliação gratuita · " + DiasRestantes + " dia(s) restantes (até " +
                       e.ExpiraEm.ToLocalTime().ToString("dd/MM/yyyy") + ")";

            string quem = string.IsNullOrEmpty(e.Cliente) ? "" : e.Cliente + " · ";
            return quem + DiasRestantes + " dia(s) restantes (até " +
                   e.ExpiraEm.ToLocalTime().ToString("dd/MM/yyyy") + ")";
        }

        // ==================================================================
        // Porta de entrada dos comandos de medição
        // ==================================================================

        /// <summary>
        /// Chamar no início de cada comando que CRIA medições. Devolve false
        /// e explica ao utilizador quando não há licença.
        /// Ver e exportar nunca passam por aqui — de propósito.
        /// </summary>
        public static bool PodeMedir()
        {
            if (Valida) return true;

            var ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            var e = Carregar();

            if (e == null || string.IsNullOrEmpty(e.Codigo))
            {
                // Primeira utilização: tenta a avaliação gratuita em silêncio.
                // Se resultar, a pessoa começa a trabalhar sem falar com ninguém —
                // é aqui que se ganha ou perde a adopção.
                // O primeiro pedido exige o e-mail e, por isso, só pode ser
                // feito num contexto interactivo seguro (comando do utilizador).
                ed?.WriteMessage("\n[TSK] Este posto ainda não está licenciado. " +
                                 "Será pedido o e-mail para iniciar a avaliação de 15 dias.\n");
                return PedirCodigoAgora();
            }

            ed?.WriteMessage("\n[TSK] Licença expirada" +
                (string.IsNullOrEmpty(e.Motivo) ? "" : " — " + e.Motivo) +
                ". Contacte " + EmailContacto + " para continuar.\n" +
                "As medições existentes continuam visíveis e exportáveis.\n");

            // O trial é uma janela única emitida pelo servidor; nunca é
            // renovado silenciosamente depois de expirar.
            //
            // A verificação online é síncrona (o resultado decide se se mede),
            // mas com um tempo de espera curto e um aviso antes: quem está a
            // medir não pode ficar 12 s congelado sem saber porquê.
            if (e.Codigo != CodigoTrial)
            {
                ed?.WriteMessage("A verificar a licença junto do servidor…\n");
                if (VerificarOnline(e.Codigo, silencioso: true, timeoutMs: 4000))
                    return true;
            }

            return PedirCodigoAgora();
        }

        private static bool PedirCodigoAgora()
        {
            try
            {
                using (var dlg = new LicencaDialog())
                {
                    AcadApp.ShowModalDialog(dlg);
                }
            }
            catch { }
            return Valida;
        }

        // ==================================================================
        // Arranque
        // ==================================================================

        /// <summary>
        /// Revalidação silenciosa no arranque, em thread própria.
        /// Nunca prende o AutoCAD nem mostra caixas.
        /// </summary>
        public static void VerificarNoArranque()
        {
            if (_verificado) return;
            _verificado = true;

            try
            {
                var t = new Thread(() =>
                {
                    try
                    {
                        var e = Carregar();

                        // Instalação nova: não abrir diálogos nem pedir dados
                        // numa thread durante o NETLOAD. O primeiro comando de
                        // medição abre o fluxo interactivo de forma segura.
                        if (e == null || string.IsNullOrEmpty(e.Codigo))
                            return;

                        // Não martelar o servidor: uma vez por dia chega
                        if ((DateTime.UtcNow - e.UltimaVerificacao).TotalHours < 24 &&
                            e.ExpiraEm > DateTime.UtcNow)
                            return;

                        // O trial não se revalida por código: ou está dentro do
                        // prazo, ou acabou.
                        if (e.Codigo == CodigoTrial) return;

                        VerificarOnline(e.Codigo, silencioso: true);
                    }
                    catch { }
                })
                { IsBackground = true };
                t.Start();
            }
            catch { }
        }

        // ==================================================================
        // Avaliação gratuita
        // ==================================================================

        /// <summary>Verdadeiro quando a licença activa é a avaliação gratuita.</summary>
        public static bool EmAvaliacao
        {
            get
            {
                var e = Carregar();
                return e != null && e.Codigo == CodigoTrial && e.ExpiraEm > DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Pede ao servidor a avaliação gratuita para este posto e e-mail.
        /// O servidor é que manda: guarda uma linha por instalação e por
        /// e-mail, por isso renomear o computador não dá uma avaliação nova.
        /// </summary>
        public static string PedirTrial(string email)
        {
            // Este método é chamado pelo diálogo aberto a partir de um
            // comando AutoCAD; é seguro completar um snapshot que falhou no
            // arranque, mas nunca fazê-lo na thread de rede.
            AutocadRuntime.CapturarSeNecessario();
            email = (email ?? "").Trim();
            if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
                return "Indique um e-mail válido para iniciar a avaliação.";

            if (!ConfigSupabase.Ler(out string url, out string chave))
                return "Falta a configuração do servidor.\n" +
                       "Coloque supabase.json junto da DLL ou em " + ConfigSupabase.CaminhoAppData + ".";

            string versaoAcad = AutocadRuntime.VersaoAutoCAD, versaoPlugin = "?";
            try { versaoPlugin = Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
            catch { }

            string corpo = "{" +
                // O nome do computador não é uma identidade: pode ser alterado
                // pelo utilizador. Envia-se o identificador persistente da
                // instalação; o servidor continua a chamar o parâmetro p_maquina
                // por compatibilidade com a função RPC já publicada.
                Campo("p_maquina", IdentificadorInstalacao) + "," +
                Campo("p_utilizador", Environment.UserName) + "," +
                Campo("p_email", email) + "," +
                Campo("p_versao_autocad", versaoAcad) + "," +
                Campo("p_versao_plugin", versaoPlugin) +
            "}";

            string resposta;
            try
            {
                resposta = Post(url, chave, RpcTrial, corpo);
            }
            catch (WebException)
            {
                return "Não foi possível contactar o servidor.";
            }
            catch (Exception ex)
            {
                return "Erro ao pedir a avaliação: " + ex.Message;
            }

            return AplicarResposta(CodigoTrial, resposta, email);
        }

        /// <summary>Compatibilidade: sem e-mail não inicia avaliação silenciosa.</summary>
        public static string PedirTrial()
        {
            return PedirTrial(Email);
        }

        // ==================================================================
        // Comunicação com o Supabase
        // ==================================================================

        /// <summary>Activa este posto com um código. Devolve mensagem de erro ou null.</summary>
        public static string Activar(string codigo)
        {
            // Activar ocorre no contexto interativo do diálogo/licença.
            AutocadRuntime.CapturarSeNecessario();
            codigo = Normalizar(codigo);
            if (codigo.Length < 6)
                return "O código parece incompleto.";

            if (!ConfigSupabase.Ler(out string url, out string chave))
                return "Falta a configuração do servidor.\n" +
                       "Crie " + ConfigSupabase.Caminho + " com a url e a chave.";

            string versaoAcad = AutocadRuntime.VersaoAutoCAD, versaoPlugin = "?";
            try { versaoPlugin = Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
            catch { }

            string corpo = "{" +
                Campo("p_codigo", codigo) + "," +
                Campo("p_maquina", Maquina) + "," +
                Campo("p_utilizador", Environment.UserName) + "," +
                Campo("p_versao_autocad", versaoAcad) + "," +
                Campo("p_versao_plugin", versaoPlugin) +
            "}";

            string resposta;
            try
            {
                resposta = Post(url, chave, RpcActivar, corpo);
            }
            catch (WebException)
            {
                return "Não foi possível contactar o servidor.\n" +
                       "Verifique a ligação à internet e tente de novo.";
            }
            catch (Exception ex)
            {
                return "Erro ao activar: " + ex.Message;
            }

            return AplicarResposta(codigo, resposta);
        }

        /// <summary>Revalida um código já activado. Devolve true se continua válido.</summary>
        private static bool VerificarOnline(string codigo, bool silencioso,
            int timeoutMs = 12000)
        {
            if (!ConfigSupabase.Ler(out string url, out string chave)) return false;

            try
            {
                string corpo = "{" + Campo("p_codigo", codigo) + "," +
                                     Campo("p_maquina", Maquina) + "}";
                string resposta = Post(url, chave, RpcVerificar, corpo, timeoutMs);
                string erro = AplicarResposta(codigo, resposta);

                if (erro == null) return true;

                if (!silencioso)
                    PaletteHost.Log("Licença: " + erro);
                return false;
            }
            catch
            {
                // Offline: mantém o que estiver guardado. A tolerância é a
                // validade local — em obra sem rede continua a trabalhar.
                return Valida;
            }
        }

        /// <summary>Interpreta o JSON da função e grava. Devolve erro ou null.</summary>
        private static string AplicarResposta(string codigo, string json, string email = null)
        {
            if (string.IsNullOrWhiteSpace(json))
                return "Resposta vazia do servidor.";

            bool ok = Regex.IsMatch(json, "\"ok\"\\s*:\\s*true");
            string motivo = Extrair(json, "motivo");

            if (!ok)
            {
                var e = Carregar() ?? new Estado();
                e.Motivo = motivo ?? "Licença recusada.";
                // Para um trial expirado, preserva a data original para que
                // a interface mostre quando terminou e o contacto comercial.
                // Para uma licença paga recusada/revogada, bloqueia já.
                if (e.Codigo != CodigoTrial)
                    e.ExpiraEm = DateTime.MinValue;
                Guardar(e);
                return e.Motivo;
            }

            string expira = Extrair(json, "expira_em");
            DateTime dt;
            if (!DateTime.TryParse(expira, CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt))
                dt = DateTime.UtcNow.AddDays(DiasToleranciaOffline);

            Guardar(new Estado
            {
                Codigo = codigo,
                Cliente = Extrair(json, "cliente") ?? "",
                Email = email ?? (Carregar()?.Email ?? ""),
                ExpiraEm = dt,
                UltimaVerificacao = DateTime.UtcNow,
                Motivo = ""
            });
            return null;
        }

        private static string Post(string url, string chave, string funcao,
            string corpo, int timeoutMs = 12000)
        {
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;

            var req = (HttpWebRequest)WebRequest.Create(
                url.TrimEnd('/') + "/rest/v1/rpc/" + funcao);
            req.Method = "POST";
            req.ContentType = "application/json";
            req.Headers.Add("apikey", chave);
            req.Headers.Add("Authorization", "Bearer " + chave);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;

            byte[] dados = Encoding.UTF8.GetBytes(corpo);
            req.ContentLength = dados.Length;
            using (var s = req.GetRequestStream()) s.Write(dados, 0, dados.Length);

            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }

        // ==================================================================
        // Estado local (cifrado com DPAPI)
        // ==================================================================

        private static Estado Carregar()
        {
            lock (_lock)
            {
                if (_estado != null) return _estado;
                try
                {
                    if (!File.Exists(Ficheiro)) return null;
                    _estado = Interpretar(File.ReadAllBytes(Ficheiro));
                    if (_estado != null) return _estado;
                }
                catch { }

                // Ficheiro corrompido ou de outro utilizador: a licença não se
                // perde por isso. Sem esta cópia, um .dat danificado era tratado
                // como "sem licença" e o cliente voltava a pedir trial.
                try
                {
                    string backup = Ficheiro + ".bak";
                    if (File.Exists(backup))
                    {
                        _estado = Interpretar(File.ReadAllBytes(backup));
                        if (_estado != null)
                        {
                            // Repara o principal com o backup: senão o Guardar
                            // seguinte copiaria o ficheiro corrompido por cima
                            // do backup bom antes de escrever o novo.
                            try { File.Copy(backup, Ficheiro, true); } catch { }
                        }
                    }
                }
                catch { }
                return _estado;
            }
        }

        /// <summary>Interpreta o ficheiro cifrado. Devolve null se não for legível.</summary>
        private static Estado Interpretar(byte[] cifrado)
        {
            try
            {
                byte[] claro = ProtectedData.Unprotect(
                    cifrado, null, DataProtectionScope.CurrentUser);
                string txt = Encoding.UTF8.GetString(claro);

                return new Estado
                {
                    Codigo = Extrair(txt, "codigo") ?? "",
                    Cliente = Extrair(txt, "cliente") ?? "",
                    Email = Extrair(txt, "email") ?? "",
                    Motivo = Extrair(txt, "motivo") ?? "",
                    ExpiraEm = Data(Extrair(txt, "expira_em")),
                    UltimaVerificacao = Data(Extrair(txt, "verificado_em"))
                };
            }
            catch
            {
                return null;
            }
        }

        private static void Guardar(Estado e)
        {
            lock (_lock)
            {
                _estado = e;
                try
                {
                    if (!Directory.Exists(Pasta)) Directory.CreateDirectory(Pasta);

                    // Cópia de segurança do ficheiro actual ANTES de o
                    // substituir: se a gravação ficar a meio (disco cheio,
                    // falha de energia), a licença não se perde — o Carregar
                    // lê o .bak como recurso.
                    if (File.Exists(Ficheiro))
                    {
                        try { File.Copy(Ficheiro, Ficheiro + ".bak", true); } catch { }
                    }

                    string txt = "{" +
                        Campo("codigo", e.Codigo) + "," +
                        Campo("cliente", e.Cliente) + "," +
                        Campo("email", e.Email) + "," +
                        Campo("motivo", e.Motivo) + "," +
                        Campo("expira_em", e.ExpiraEm.ToString("o", CultureInfo.InvariantCulture)) + "," +
                        Campo("verificado_em", e.UltimaVerificacao.ToString("o", CultureInfo.InvariantCulture)) +
                    "}";

                    byte[] cifrado = ProtectedData.Protect(
                        Encoding.UTF8.GetBytes(txt), null, DataProtectionScope.CurrentUser);
                    File.WriteAllBytes(Ficheiro, cifrado);
                }
                catch { }
            }
        }

        /// <summary>Apaga a licença deste posto (para testes ou transferência).</summary>
        public static void Limpar()
        {
            lock (_lock)
            {
                _estado = null;
                try { if (File.Exists(Ficheiro)) File.Delete(Ficheiro); } catch { }
                try { if (File.Exists(Ficheiro + ".bak")) File.Delete(Ficheiro + ".bak"); } catch { }
            }
        }

        // ==================================================================
        private static string Normalizar(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return "";
            return Regex.Replace(codigo, "[^A-Za-z0-9]", "").ToUpperInvariant();
        }

        private static string Campo(string nome, string valor)
        {
            string v = (valor ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")
                                    .Replace("\r", "").Replace("\n", " ");
            return "\"" + nome + "\":\"" + v + "\"";
        }

        private static string Extrair(string json, string campo)
        {
            var m = Regex.Match(json, "\"" + campo + "\"\\s*:\\s*\"([^\"]*)\"");
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(json, "\"" + campo + "\"\\s*:\\s*([^,}\\s]+)");
            return m.Success && m.Groups[1].Value != "null" ? m.Groups[1].Value : null;
        }

        private static DateTime Data(string s)
        {
            DateTime dt;
            return DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out dt)
                ? dt : DateTime.MinValue;
        }
    }

    /// <summary>
    /// Configuração do servidor, partilhada pela licença e pela telemetria.
    /// Fica fora da DLL de propósito: assim mudas de projeto Supabase sem
    /// recompilar, e cada instalação pode apontar para onde quiseres.
    /// </summary>
    public static class ConfigSupabase
    {
        /// <summary>Configuração no mesmo diretório da DLL distribuída.</summary>
        public static string CaminhoDll
        {
            get
            {
                try
                {
                    string pasta = Path.GetDirectoryName(
                        Assembly.GetExecutingAssembly().Location);
                    return Path.Combine(pasta ?? "", "supabase.json");
                }
                catch { return "supabase.json"; }
            }
        }

        /// <summary>Local alternativo para instalações antigas.</summary>
        public static string CaminhoAppData => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TSKTakeOff", "supabase.json");

        // Mantém o nome público antigo, agora apontando para o local recomendado.
        public static string Caminho => CaminhoDll;

        /// <summary>Formato: {"url":"https://xxx.supabase.co","chave":"sb_publishable_..."}</summary>
        public static bool Ler(out string url, out string chave)
        {
            url = null; chave = null;
            try
            {
                string f = File.Exists(CaminhoDll) ? CaminhoDll : CaminhoAppData;
                if (!File.Exists(f))
                {
                    // compatibilidade com o ficheiro antigo da telemetria
                    string antigo = Path.Combine(Path.GetDirectoryName(CaminhoAppData), "telemetria.json");
                    if (!File.Exists(antigo)) return false;
                    f = antigo;
                }

                string txt = File.ReadAllText(f);
                url = Valor(txt, "url");
                chave = Valor(txt, "chave") ?? Valor(txt, "key") ?? Valor(txt, "anon");
                return !string.IsNullOrWhiteSpace(url) && !string.IsNullOrWhiteSpace(chave);
            }
            catch { return false; }
        }

        private static string Valor(string json, string campo)
        {
            var m = Regex.Match(json, "\"" + campo + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
