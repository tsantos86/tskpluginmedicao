using System;
using System.IO;

namespace TSKTakeOff
{
    /// <summary>
    /// Guarda e serve o ficheiro-modelo de medições da casa. O modelo
    /// distribuído por omissão é .xlsx; modelos personalizados .xls/.xlsm
    /// continuam suportados, incluindo macros quando o formato as permite.
    ///
    /// O plugin nunca reescreve o modelo: copia-o para um ficheiro de trabalho
    /// e escreve na cópia. Assim as macros, as fórmulas auxiliares, as larguras
    /// de coluna e o cabeçalho do projecto chegam intactos ao resultado.
    ///
    /// Caminho registado em %AppData%\TSKTakeOff\modelo.txt.
    /// Se não houver registo, procura modelo.xlsx / .xlsm / .xls na mesma pasta.
    /// </summary>
    public static class ModeloExcel
    {
        private const string NomeRegisto = "modelo.txt";

        /// <summary>
        /// Por esta ordem. O .xlsx vem primeiro de propósito: é o formato do
        /// modelo distribuído por omissão, e assim um modelo.xls antigo
        /// esquecido na pasta não ganha ao novo.
        /// </summary>
        private static readonly string[] NomesPorOmissao =
            { "modelo.xlsx", "modelo.xlsm", "modelo.xls", "modelo.xltm", "modelo.xlt" };

        public static string Pasta
        {
            get
            {
                string p = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TSKTakeOff");
                Directory.CreateDirectory(p);
                return p;
            }
        }

        /// <summary>Caminho do modelo em uso, ou null se ainda não houver nenhum.</summary>
        public static string Caminho
        {
            get
            {
                try
                {
                    string reg = Path.Combine(Pasta, NomeRegisto);
                    if (File.Exists(reg))
                    {
                        string alvo = (File.ReadAllText(reg) ?? "").Trim();
                        if (alvo.Length > 0 && File.Exists(alvo)) return alvo;
                    }

                    foreach (string nome in NomesPorOmissao)
                    {
                        string alvo = Path.Combine(Pasta, nome);
                        if (File.Exists(alvo)) return alvo;
                    }
                }
                catch { /* sem modelo: o plugin usa a folha simples */ }
                return null;
            }
        }

        public static bool Existe { get { return Caminho != null; } }

        /// <summary>Regista um modelo escolhido pelo utilizador. Devolve o erro, ou null.</summary>
        public static string Definir(string caminho)
        {
            if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
                return "Ficheiro não encontrado.";

            string ext = (Path.GetExtension(caminho) ?? "").ToLowerInvariant();
            if (ext != ".xls" && ext != ".xlsm" && ext != ".xlsx" && ext != ".xltm" && ext != ".xlt")
                return "O modelo tem de ser um ficheiro Excel (.xls, .xlsm ou .xlsx).";

            try
            {
                File.WriteAllText(Path.Combine(Pasta, NomeRegisto), caminho);
                return null;
            }
            catch (Exception ex)
            {
                return "Não foi possível guardar a escolha: " + ex.Message;
            }
        }

        public static void Esquecer()
        {
            try
            {
                string reg = Path.Combine(Pasta, NomeRegisto);
                if (File.Exists(reg)) File.Delete(reg);
            }
            catch { /* melhor esforço */ }
        }

        /// <summary>
        /// Cria uma cópia de trabalho do modelo e devolve o caminho.
        /// Lança se não houver modelo registado ou se a cópia falhar.
        /// </summary>
        public static string CopiaDeTrabalho(string nomeBase)
        {
            string origem = Caminho;
            if (origem == null)
                throw new InvalidOperationException(
                    "Ainda não há um modelo de medições registado. Use o comando TSKMODELO.");

            string destinoPasta = Path.Combine(Path.GetTempPath(), "TSKTakeOff");
            Directory.CreateDirectory(destinoPasta);

            if (string.IsNullOrWhiteSpace(nomeBase)) nomeBase = "medicoes";
            foreach (char c in Path.GetInvalidFileNameChars())
                nomeBase = nomeBase.Replace(c, '_');

            string ext = Path.GetExtension(origem);
            string destino = Path.Combine(destinoPasta,
                nomeBase + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ext);

            // Cópia byte a byte de propósito, em vez de File.Copy: assim a marca
            // "ficheiro vindo da internet" (Zone.Identifier) não viaja com ele.
            // Com essa marca o Excel abre em Vista Protegida — o livro fica
            // inacessível ao plugin e as macros ficam bloqueadas.
            using (var entrada = new FileStream(origem, FileMode.Open,
                       FileAccess.Read, FileShare.ReadWrite))
            using (var saida = new FileStream(destino, FileMode.Create,
                       FileAccess.Write, FileShare.None))
                entrada.CopyTo(saida);

            try { File.SetAttributes(destino, FileAttributes.Normal); } catch { }
            return destino;
        }

        /// <summary>
        /// Apaga as cópias de trabalho antigas (mais de um dia). Cada ligação
        /// "Excel ao vivo" cria uma cópia; sem limpeza, acumulavam-se para
        /// sempre em %TEMP%\TSKTakeOff. Nunca apaga a que está a ser usada:
        /// tem menos de um dia, ou o ficheiro está bloqueado e o Delete falha
        /// em silêncio — nos dois casos fica para a próxima.
        /// </summary>
        public static void LimparCopiasAntigas()
        {
            try
            {
                string pasta = Path.Combine(Path.GetTempPath(), "TSKTakeOff");
                if (!Directory.Exists(pasta)) return;

                DateTime limite = DateTime.Now.AddDays(-1);
                foreach (string f in Directory.GetFiles(pasta))
                {
                    try
                    {
                        if (File.GetLastWriteTime(f) < limite) File.Delete(f);
                    }
                    catch { /* em uso ou sem permissão: deixa para a próxima */ }
                }
            }
            catch { /* limpeza é acessório, nunca pode falhar a ligação */ }
        }

        /// <summary>
        /// Tira a marca de "vindo da internet" ao próprio modelo, para o Excel
        /// deixar de o abrir em Vista Protegida e as macros voltarem a correr.
        /// </summary>
        public static string Desbloquear()
        {
            string origem = Caminho;
            if (origem == null) return "Ainda não há um modelo registado.";

            try
            {
                string temporario = origem + ".tsk";
                using (var entrada = new FileStream(origem, FileMode.Open,
                           FileAccess.Read, FileShare.ReadWrite))
                using (var saida = new FileStream(temporario, FileMode.Create,
                           FileAccess.Write, FileShare.None))
                    entrada.CopyTo(saida);

                // Substituir SEM apagar o original primeiro: apagava-se e só
                // depois se movia, e se o Move falhasse (antivírus, permissões)
                // o modelo do cliente ficava perdido.
                try
                {
                    // Atómico e no mesmo volume: se falhar, o original fica
                    // intacto e a cópia .tsk pode ser apagada à mão.
                    File.Replace(temporario, origem, null);
                }
                catch
                {
                    // Partições que não suportam Replace (ex.: FAT): cópia de
                    // segurança antes da troca, com restauro em caso de falha.
                    string backup = origem + ".bak";
                    File.Copy(origem, backup, true);
                    try
                    {
                        File.Delete(origem);
                        File.Move(temporario, origem);
                        try { File.Delete(backup); } catch { }
                    }
                    catch
                    {
                        // Se a troca falhou a meio, repõe o original a partir
                        // do backup — nunca ficar sem modelo.
                        if (!File.Exists(origem) && File.Exists(backup))
                        {
                            try { File.Move(backup, origem); } catch { }
                        }
                        throw;
                    }
                }

                File.SetAttributes(origem, FileAttributes.Normal);
                return null;
            }
            catch (Exception ex)
            {
                return "Não foi possível desbloquear o modelo: " + ex.Message;
            }
        }

        // ------------------------------------------------------------------
        // Nomes de folha
        //
        // As macros da casa trabalham sobre uma lista fixa de folhas
        // ("Resumo MD", "1.1" … "1.15"). Para as medições do plugin entrarem
        // nesses mapas, basta dizer aqui em que folha do modelo cai cada
        // capítulo, num ficheiro de texto simples:
        //
        //     %AppData%\TSKTakeOff\folhas.txt
        //     ALVENARIAS=1.1
        //     MATERIAIS=2.1
        //
        // Sem ficheiro, o capítulo dá o nome à folha.
        // ------------------------------------------------------------------
        private const string NomeMapaFolhas = "folhas.txt";

        /// <summary>Folha de destino de um capítulo, segundo o folhas.txt.</summary>
        public static string FolhaDe(string capitulo)
        {
            if (string.IsNullOrWhiteSpace(capitulo)) return capitulo;
            try
            {
                string mapa = Path.Combine(Pasta, NomeMapaFolhas);
                if (!File.Exists(mapa)) return capitulo;

                foreach (string bruta in File.ReadAllLines(mapa))
                {
                    string linha = (bruta ?? "").Trim();
                    if (linha.Length == 0 || linha.StartsWith("#")) continue;

                    int igual = linha.IndexOf('=');
                    if (igual <= 0) continue;

                    string chave = linha.Substring(0, igual).Trim();
                    string valor = linha.Substring(igual + 1).Trim();
                    if (valor.Length > 0 &&
                        string.Equals(chave, capitulo, StringComparison.OrdinalIgnoreCase))
                        return valor;
                }
            }
            catch { /* mapa inválido: usar o nome do capítulo */ }
            return capitulo;
        }

        /// <summary>Descrição curta para mostrar ao utilizador.</summary>
        public static string Resumo()
        {
            string c = Caminho;
            return c == null
                ? "Sem modelo registado — o Excel ao vivo usa a folha simples."
                : "Modelo: " + c;
        }
    }
}
