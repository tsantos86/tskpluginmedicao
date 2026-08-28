using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// O mapa de quantidades do cliente, guardado dentro do desenho.
    ///
    /// Quem mede não escolhe os artigos: recebe-os. O cliente manda um mapa
    /// com o articulado todo — capítulos, subcapítulos e artigos — e a medição
    /// tem de sair arrumada exactamente por ali. Escrever o código e a
    /// designação à mão em cada medição é onde se perde tempo e onde entram
    /// gralhas que só aparecem na conferência.
    ///
    /// Aqui importa-se o articulado UMA vez, LIMPO de quantidades e fórmulas,
    /// e a partir daí escolhe-se o artigo de uma lista. Os números nascem da
    /// medição, nunca do ficheiro do cliente.
    ///
    /// A chave é <c>código + designação</c>, não o código sozinho. Nos mapas
    /// reais o código repete-se — num ficheiro de obra encontrei o 7.4 duas
    /// vezes, o 12.3.1 duas, o 18.1 três e dois artigos com o código
    /// "Sem Ref". Guardar as medições pelo código mandava-as para o artigo
    /// errado, calado. Já o par código+designação é único nos 262 nós desse
    /// mesmo ficheiro — e é exactamente o que o <see cref="Parede.Artigo"/> já
    /// guardava antes de isto existir, portanto as medições antigas continuam
    /// a valer sem migração nenhuma.
    /// </summary>
    public static class MapaQuantidades
    {
        /// <summary>Onde o articulado vive dentro do DWG.</summary>
        private const string DicionarioMqt = "TSK_MQT";

        /// <summary>Onde vive a ligação serviço → artigo.</summary>
        private const string DicionarioServicos = "TSK_MQT_SERVICOS";

        // Marca a versão com continuação de descrição dentro do Xrecord.
        // Sem um marcador, um novo campo de texto acrescentado no futuro seria
        // confundido com mais caracteres da descrição antiga.
        private const short MarcadorDescricaoLonga = 21587;

        /// <summary>
        /// Separador entre código e designação — o mesmo 0x1F que o
        /// <see cref="Parede.Artigo"/> já usa. Escrito em escape de propósito:
        /// o caractere em cru é invisível no editor e desaparece numa cópia
        /// mal feita sem ninguém dar por ela.
        /// </summary>
        public const string Sep = "\u001f";

        // ==================================================================
        // Modelo
        // ==================================================================

        /// <summary>
        /// Uma linha do articulado. Pode ser capítulo, subcapítulo ou artigo:
        /// a diferença é o <see cref="EhArtigo"/>, e só nos artigos é que se
        /// medem coisas.
        /// </summary>
        public class No
        {
            /// <summary>Posição no articulado. É por isto que a folha é ordenada.</summary>
            public int Ordem;

            /// <summary>"1.1.3", "10.1.1.306A", "Sem Ref"… tal como vem.</summary>
            public string Codigo = "";

            public string Designacao = "";

            /// <summary>m2, m3, ml, un… vazio quando o mapa não a declara.</summary>
            public string Unidade = "";

            /// <summary>1 = capítulo, 2 = subcapítulo, e por aí fora.</summary>
            public int Nivel = 1;

            /// <summary>
            /// Só nos artigos se pode medir. Os capítulos existem para dar
            /// contexto na lista e para a folha sair com os títulos certos.
            /// </summary>
            public bool EhArtigo;

            /// <summary>Folha do livro de onde veio, para se saber a origem.</summary>
            public string Origem = "";

            /// <summary>A chave: código + designação.</summary>
            public string Chave
            {
                get { return (Codigo ?? "") + Sep + (Designacao ?? ""); }
            }

            /// <summary>Como aparece na lista de escolha.</summary>
            public override string ToString()
            {
                string u = string.IsNullOrEmpty(Unidade) ? "" : "  [" + Unidade + "]";
                return (string.IsNullOrEmpty(Codigo) ? "" : Codigo + "  ")
                     + Designacao + u;
            }
        }

        // ==================================================================
        // Memória de trabalho
        // ==================================================================

        private static List<No> _nos = new List<No>();
        private static Dictionary<string, No> _porChave =
            new Dictionary<string, No>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Índice do <see cref="Procurar"/>, pela forma normalizada da chave.
        /// Um valor a null quer dizer "esta forma serve mais do que um artigo",
        /// e nesse caso a medição fica órfã de propósito. Ver Indexar().
        /// </summary>
        private static Dictionary<string, No> _porNormalizada =
            new Dictionary<string, No>(StringComparer.OrdinalIgnoreCase);

        // A cache é por DOCUMENTO, não por nome: dois desenhos com o mesmo
        // nome (dois "sem título", ou o mesmo ficheiro copiado para pastas
        // diferentes) partilhavam a cache e um mostrava os artigos do outro.
        private static Document _docCarregado;
        private static Document _docServicos;

        /// <summary>O articulado corrente. Carrega do desenho se for preciso.</summary>
        public static List<No> Nos
        {
            get { GarantirCarregado(); return _nos; }
        }

        /// <summary>Há articulado importado neste desenho?</summary>
        public static bool Existe
        {
            get { GarantirCarregado(); return _nos.Count > 0; }
        }

        /// <summary>Só os artigos — o que se pode medir.</summary>
        public static List<No> Artigos
        {
            get
            {
                GarantirCarregado();
                var r = new List<No>();
                foreach (var n in _nos) if (n.EhArtigo) r.Add(n);
                return r;
            }
        }

        /// <summary>
        /// Onde este artigo fica no articulado. <c>int.MaxValue</c> quando não
        /// pertence ao mapa — assim as medições de artigos escritos à mão vão
        /// para o fim em vez de se intrometerem no meio do articulado.
        /// </summary>
        public static int OrdemDe(string chaveArtigo)
        {
            var n = Procurar(chaveArtigo);
            return n == null ? int.MaxValue : n.Ordem;
        }

        /// <summary>
        /// A chave que o MAPA usa para este artigo, seja qual for a forma como
        /// a medição a tem guardada.
        ///
        /// Duas medições do mesmo artigo podem ter chaves diferentes: as feitas
        /// antes de a designação passar a ser cortada em memória guardaram-na
        /// inteira, as de agora guardam-na cortada. Agrupadas pela chave em
        /// cru, davam DOIS blocos do mesmo artigo na folha, com o cabeçalho
        /// repetido — e os dois a dizer "2.6", que é o que torna isto
        /// desconcertante de ver.
        ///
        /// Fora do mapa, devolve o que recebeu: um artigo escrito à mão é o
        /// que está lá escrito.
        /// </summary>
        public static string ChaveCanonica(string chaveArtigo)
        {
            if (string.IsNullOrEmpty(chaveArtigo)) return "";
            var n = Procurar(chaveArtigo);
            return n != null ? n.Chave : chaveArtigo;
        }

        /// <summary>O nó deste artigo, ou null se não for do mapa.</summary>
        public static No Procurar(string chaveArtigo)
        {
            if (string.IsNullOrEmpty(chaveArtigo)) return null;
            GarantirCarregado();

            // O mapa guarda a designação no seu próprio campo, enquanto a
            // entidade guarda código + separador + designação num único texto
            // de XData. Logo, a chave do mapa pode ser maior do que 255 e a
            // da entidade pode ter só os caracteres que couberam depois do
            // código. Comparar todas as chaves normalizadas ao limite total
            // resolve a diferença sem aceitar prefixos curtos como artigos
            // diferentes.
            //
            // Não se devolve logo a primeira igualdade: dois artigos com o
            // mesmo código podem coincidir nos primeiros 255 caracteres e a
            // XData não contém informação para decidir qual deles era. Nesse
            // caso, ficar órfã é deliberado e seguro.
            // Consulta, não varrimento. O índice guarda já a ambiguidade
            // resolvida: uma chave que sirva dois artigos entra com null, que
            // é exactamente o que o varrimento devolvia nesse caso.
            No achado;
            return _porNormalizada.TryGetValue(
                ChaveArtigo.Normalizada(chaveArtigo), out achado) ? achado : null;
        }

        /// <summary>
        /// O nó cujo código é exactamente o escrito ("3.1.1"), ou null.
        ///
        /// Existe para o que se escreve à mão na coluna Artigo da grelha valer
        /// o artigo INTEIRO do mapa. A chave de uma medição é código+designação:
        /// escrever o código novo por cima e deixar ficar a designação antiga
        /// produzia um par que não existe em mapa nenhum — a medição passava a
        /// órfã, saía no fim da folha e não havia nada que o explicasse.
        ///
        /// Onde o mesmo código serve um capítulo e um artigo, ganha o artigo:
        /// é nele que se mede, e um capítulo não é destino de medição.
        /// </summary>
        public static No PorCodigo(string codigo)
        {
            string c = (codigo ?? "").Trim();
            if (c.Length == 0) return null;
            GarantirCarregado();

            No qualquer = null;
            foreach (var n in _nos)
            {
                if (!string.Equals((n.Codigo ?? "").Trim(), c,
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (n.EhArtigo) return n;
                if (qualquer == null) qualquer = n;
            }
            return qualquer;
        }

        /// <summary>
        /// Os nós cujo código ou designação contêm todos os pedaços escritos.
        /// Escrever "1.1 alven" encontra o artigo sem se saber o código de cor.
        /// </summary>
        public static List<No> Filtrar(string texto, bool soArtigos)
        {
            GarantirCarregado();
            var r = new List<No>();
            string[] pedacos = (texto ?? "").Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var n in _nos)
            {
                if (soArtigos && !n.EhArtigo) continue;
                if (pedacos.Length == 0) { r.Add(n); continue; }

                string alvo = (n.Codigo + " " + n.Designacao);
                bool todos = true;
                foreach (string p in pedacos)
                {
                    if (alvo.IndexOf(p, StringComparison.OrdinalIgnoreCase) < 0)
                    { todos = false; break; }
                }
                if (todos) r.Add(n);
            }
            return r;
        }

        /// <summary>Esquece o que está em memória. Ao mudar de desenho, por ex.</summary>
        public static void Invalidar()
        {
            _nos = new List<No>();
            _porChave = new Dictionary<string, No>(StringComparer.OrdinalIgnoreCase);
            _porNormalizada = new Dictionary<string, No>(StringComparer.OrdinalIgnoreCase);
            _docCarregado = null;
            _carregado = false;
        }

        // ==================================================================
        // A ordem do articulado
        // ==================================================================

        /// <summary>
        /// Põe o articulado por ordem de CÓDIGO e renumera a <see cref="No.Ordem"/>
        /// por aí.
        ///
        /// A ordem vinha da importação, e a importação percorre as folhas do
        /// livro pela ordem em que estão no Excel. Num ficheiro em que a folha
        /// do capítulo 3 vem antes da do capítulo 1 — e vêm, os livros de obra
        /// são montados por quem os montou — o artigo 3.1.1 ficava com uma
        /// Ordem MENOR que a do 1.1.1 e saía à frente dele na folha. A medição
        /// entregava-se fora da ordem do mapa do cliente, que é a única coisa
        /// que a folha tem de respeitar.
        ///
        /// O código É o articulado: 1.1.1 vem antes de 1.1.2, e 1.1.4 antes de
        /// 3.1.1. Não é a folha do Excel que decide isso.
        ///
        /// Corre na gravação E na leitura. Na leitura porque os desenhos já
        /// importados têm a ordem antiga guardada dentro deles, e ninguém devia
        /// ter de reimportar o mapa para a folha sair pela ordem certa.
        /// </summary>
        private static void OrdenarPeloCodigo(List<No> nos)
        {
            if (nos == null || nos.Count < 2) return;

            // Posição de partida, para desempatar. O List.Sort não é estável:
            // sem isto, dois nós com o mesmo código trocavam de sítio entre
            // carregamentos e a folha mudava de forma sem ninguém lhe tocar.
            var posicao = new Dictionary<No, int>();
            for (int i = 0; i < nos.Count; i++) posicao[nos[i]] = i;

            // A comparação vive no CodigoArticulado, que não sabe o que é o
            // AutoCAD e por isso pode ser posta à prova sem ele. É a peça que
            // decide a ordem por que a medição se entrega, e essa engana-se em
            // silêncio: uma folha mal ordenada continua a parecer bem feita.
            nos.Sort((a, b) =>
            {
                int c = CodigoArticulado.Comparar(a.Codigo, b.Codigo);
                return c != 0 ? c : posicao[a].CompareTo(posicao[b]);
            });

            for (int i = 0; i < nos.Count; i++) nos[i].Ordem = i + 1;
        }

        private static void Indexar()
        {
            _porChave = new Dictionary<string, No>(StringComparer.OrdinalIgnoreCase);
            _porNormalizada = new Dictionary<string, No>(StringComparer.OrdinalIgnoreCase);

            foreach (var n in _nos)
            {
                // Se o par se repetir — não aconteceu nos ficheiros reais, mas
                // um mapa mal feito pode — fica o primeiro. Perder o segundo é
                // menos mau do que rebentar a importação toda.
                if (!_porChave.ContainsKey(n.Chave)) _porChave[n.Chave] = n;

                // Índice do Procurar. A AMBIGUIDADE FICA RESOLVIDA AQUI: dois
                // artigos que caiam na mesma forma normalizada entram como
                // null, porque a XData não guarda informação para os
                // distinguir e devolver um deles seria adivinhar. É a mesma
                // decisão que o varrimento tomava, tomada uma vez em vez de a
                // cada consulta.
                string k = ChaveArtigo.Normalizada(n.Chave);
                if (k == null) continue;
                if (_porNormalizada.ContainsKey(k)) _porNormalizada[k] = null;
                else _porNormalizada[k] = n;
            }
        }

        // ==================================================================
        // Persistência no DWG
        // ==================================================================

        /// <summary>
        /// Guarda o articulado no desenho. Um Xrecord por nó, dentro de um
        /// dicionário nomeado.
        ///
        /// Não é XData por uma razão de tamanho: o mapa do Lumare tem 262 nós
        /// e as designações passam facilmente dos 40 KB, quando a XData de um
        /// objecto está limitada a 16 KB. O dicionário não tem esse tecto, e
        /// um Xrecord por nó permite ainda mexer num artigo sem reescrever o
        /// articulado inteiro.
        /// </summary>
        public static bool Guardar(List<No> nos)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            // Pela ordem do código antes de gravar: assim o que fica no desenho
            // já é a ordem do articulado, e não a ordem das folhas do livro.
            OrdenarPeloCodigo(nos);

            // E cortar AGORA o que o desenho vai cortar de qualquer maneira.
            //
            // Isto era o defeito mais caro deste ficheiro. A designação ia para
            // o DWG cortada aos 255 (limite de um texto em XData), mas ficava
            // INTEIRA em memória. A chave de um artigo é código+designação, e
            // é ela que cada medição guarda — portanto uma medição feita logo
            // a seguir à importação nascia com a chave LONGA. Ao reabrir o
            // desenho, o mapa vinha cortado, a chave longa deixava de existir
            // nele, e a medição passava a órfã: sem lugar no articulado.
            //
            // Neste mapa a primeira designação tem 263 caracteres. Ou seja: a
            // ordenação por artigo não estava a falhar — não chegava a haver
            // artigo nenhum reconhecido para ordenar. E nada disto dava erro.
            foreach (var n in nos)
            {
                n.Codigo = Curto(n.Codigo, 255);
                // A descrição vive em vários DxfCode.Text quando ultrapassa
                // 255 caracteres. Não a cortar aqui: o Xrecord suporta vários
                // campos de texto e o Excel precisa do texto completo.
                n.Unidade = Curto(n.Unidade, 32);
                n.Origem = Curto(n.Origem, 128);
            }

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(
                        db.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    // Substituir por inteiro: um MQT meio antigo meio novo era
                    // pior do que não ter nenhum. O Remove tira a entrada do
                    // dicionário-mãe e apaga o objecto; apagar só o objecto
                    // deixava a entrada pendurada.
                    if (nod.Contains(DicionarioMqt))
                    {
                        try { nod.Remove(DicionarioMqt); }
                        catch
                        {
                            var velho = (DBDictionary)tr.GetObject(
                                nod.GetAt(DicionarioMqt), OpenMode.ForWrite);
                            velho.Erase();
                        }
                    }

                    var dic = new DBDictionary();
                    nod.SetAt(DicionarioMqt, dic);
                    tr.AddNewlyCreatedDBObject(dic, true);

                    for (int i = 0; i < nos.Count; i++)
                    {
                        var n = nos[i];
                        var valores = new List<TypedValue>
                        {
                            new TypedValue((int)DxfCode.Int32, n.Ordem),
                            // Os primeiros sete campos mantêm exactamente o
                            // formato antigo. Assim, versões anteriores do
                            // plugin continuam a ler o início do artigo.
                            new TypedValue((int)DxfCode.Text, Curto(n.Codigo, 255)),
                            new TypedValue((int)DxfCode.Text, Curto(n.Designacao, 255)),
                            new TypedValue((int)DxfCode.Text, Curto(n.Unidade, 32)),
                            new TypedValue((int)DxfCode.Int16, (short)n.Nivel),
                            new TypedValue((int)DxfCode.Int16, (short)(n.EhArtigo ? 1 : 0)),
                            new TypedValue((int)DxfCode.Text, Curto(n.Origem, 128))
                        };

                        // DxfCode.Text tem limite por entrada. Só se acrescenta
                        // a extensão quando é necessária: descrições curtas
                        // continuam byte a byte iguais às dos DWG antigos.
                        if ((n.Designacao ?? "").Length > 255)
                        {
                            valores.Add(new TypedValue((int)DxfCode.Int16,
                                MarcadorDescricaoLonga));
                            foreach (string bloco in BlocosDeTexto(n.Designacao, 255))
                                valores.Add(new TypedValue((int)DxfCode.Text, bloco));
                        }

                        var xr = new Xrecord { Data = new ResultBuffer(valores.ToArray()) };

                        // A chave do dicionário tem de ser única e não pode
                        // levar tudo o que um código traz. O índice serve, e a
                        // ordem de leitura fica garantida pelo campo Ordem.
                        dic.SetAt("N" + i.ToString("D5"), xr);
                        tr.AddNewlyCreatedDBObject(xr, true);
                    }

                    tr.Commit();
                }

                _nos = new List<No>(nos);
                Indexar();
                _docCarregado = doc;
                _carregado = true;
                return true;
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("MQT: não consegui guardar no desenho — " + ex.Message);
                return false;
            }
        }

        /// <summary>Apaga o articulado deste desenho.</summary>
        public static bool Apagar()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(
                        doc.Database.NamedObjectsDictionaryId, OpenMode.ForWrite);
                    if (nod.Contains(DicionarioMqt))
                    {
                        var dic = (DBDictionary)tr.GetObject(
                            nod.GetAt(DicionarioMqt), OpenMode.ForWrite);
                        dic.Erase();
                    }
                    tr.Commit();
                }
                Invalidar();
                return true;
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("MQT: não consegui apagar — " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Carrega uma vez por desenho. O <c>_carregado</c> existe para
        /// distinguir "ainda não fui ver" de "fui ver e não há nada": sem ele,
        /// um desenho sem MQT ia à base de dados a cada tecla escrita no campo
        /// de procura.
        /// </summary>
        private static bool _carregado;

        private static void GarantirCarregado()
        {
            var doc = DocumentoActivo();
            if (_carregado && ReferenceEquals(_docCarregado, doc)) return;
            Carregar();
        }

        /// <summary>Lê o articulado do desenho para memória.</summary>
        public static void Carregar()
        {
            var lidos = new List<No>();
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { _nos = lidos; Indexar(); return; }

            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(
                        doc.Database.NamedObjectsDictionaryId, OpenMode.ForRead);
                    if (nod.Contains(DicionarioMqt))
                    {
                        var dic = (DBDictionary)tr.GetObject(
                            nod.GetAt(DicionarioMqt), OpenMode.ForRead);

                        foreach (DBDictionaryEntry e in dic)
                        {
                            var xr = tr.GetObject(e.Value, OpenMode.ForRead) as Xrecord;
                            if (xr == null || xr.Data == null) continue;
                            var n = DesXrecord(xr);
                            if (n != null) lidos.Add(n);
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("MQT: não consegui ler do desenho — " + ex.Message);
            }

            // O dicionário não promete ordem nenhuma; a ordem é a que gravámos.
            lidos.Sort((a, b) => a.Ordem.CompareTo(b.Ordem));

            // E depois pela do CÓDIGO, que é a do articulado. Aqui e não só na
            // gravação: os desenhos importados antes disto têm a ordem antiga
            // guardada lá dentro, e reimportar o mapa só para a folha sair pela
            // ordem certa seria um pedido difícil de justificar.
            OrdenarPeloCodigo(lidos);

            _nos = lidos;
            Indexar();
            _docCarregado = doc;
            _carregado = true;
        }

        private static No DesXrecord(Xrecord xr)
        {
            try
            {
                var v = xr.Data.AsArray();
                if (v.Length < 6) return null;
                string designacao = Convert.ToString(v[2].Value) ?? "";
                // Os campos 0..6 são o formato original. A extensão nova só
                // é lida quando começa pelo marcador explícito; assim, campos
                // de texto futuros não são anexados por engano à descrição.
                if (v.Length > 8 &&
                    v[7].TypeCode == (int)DxfCode.Int16 &&
                    Convert.ToInt16(v[7].Value) == MarcadorDescricaoLonga)
                {
                    for (int i = 8; i < v.Length; i++)
                        if (v[i].TypeCode == (int)DxfCode.Text)
                            designacao += Convert.ToString(v[i].Value) ?? "";
                }

                return new No
                {
                    Ordem = Convert.ToInt32(v[0].Value),
                    Codigo = Convert.ToString(v[1].Value) ?? "",
                    Designacao = designacao,
                    Unidade = Convert.ToString(v[3].Value) ?? "",
                    Nivel = Convert.ToInt32(v[4].Value),
                    EhArtigo = Convert.ToInt32(v[5].Value) != 0,
                    Origem = v.Length > 6 ? (Convert.ToString(v[6].Value) ?? "") : ""
                };
            }
            catch { return null; }
        }

        private static string Curto(string s, int max)
        {
            s = s ?? "";
            return s.Length <= max ? s : s.Substring(0, max);
        }

        private static IEnumerable<string> BlocosDeTexto(string texto, int tamanho)
        {
            texto = texto ?? "";
            if (tamanho <= 0) yield break;
            for (int inicio = tamanho; inicio < texto.Length; inicio += tamanho)
                yield return texto.Substring(inicio, Math.Min(tamanho, texto.Length - inicio));
        }

        /// <summary>Documento activo, ou null se não houver (ou se falhar).</summary>
        private static Document DocumentoActivo()
        {
            try
            {
                return AcadApp.DocumentManager.MdiActiveDocument;
            }
            catch { return null; }
        }

        // ==================================================================
        // Que artigo pertence a cada serviço
        // ==================================================================

        /// <summary>
        /// O artigo de cada serviço, neste desenho.
        ///
        /// A mesma parede é medida três vezes — alvenaria, reboco,
        /// revestimento — e cada uma pertence a um artigo DIFERENTE do mapa do
        /// cliente. Um artigo só no painel não chegava: o TSKMEDSEL cria as
        /// três medições de uma vez e punha as três no mesmo artigo.
        ///
        /// A ligação é por serviço porque é assim que se mantém ao longo da
        /// obra: "ALVENARIA TIJOLO 15" é o artigo 1.1.1 do princípio ao fim.
        /// Diz-se uma vez e não se repete.
        /// </summary>
        private static Dictionary<string, string> _artigoDoServico =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _servicosCarregados;

        /// <summary>O artigo deste serviço, ou "" se ainda não foi dito.</summary>
        public static string ArtigoDoServico(string servico)
        {
            if (string.IsNullOrEmpty(servico)) return "";
            GarantirServicosCarregados();
            string chave;
            return _artigoDoServico.TryGetValue(servico.Trim(), out chave) ? chave : "";
        }

        /// <summary>Liga (ou desliga, com chave vazia) um serviço a um artigo.</summary>
        public static void DefinirArtigoDoServico(string servico, string chaveArtigo)
        {
            if (string.IsNullOrEmpty(servico)) return;
            GarantirServicosCarregados();

            servico = servico.Trim();
            string actual;
            _artigoDoServico.TryGetValue(servico, out actual);
            if ((actual ?? "") == (chaveArtigo ?? "")) return;   // nada mudou

            if (string.IsNullOrEmpty(chaveArtigo)) _artigoDoServico.Remove(servico);
            else _artigoDoServico[servico] = chaveArtigo;

            GuardarServicos();
        }

        private static void GarantirServicosCarregados()
        {
            var doc = DocumentoActivo();
            if (_servicosCarregados && ReferenceEquals(_docServicos, doc)) return;
            CarregarServicos();
        }

        private static void CarregarServicos()
        {
            var lidos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) { _artigoDoServico = lidos; return; }

            try
            {
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(
                        doc.Database.NamedObjectsDictionaryId, OpenMode.ForRead);
                    if (nod.Contains(DicionarioServicos))
                    {
                        var dic = (DBDictionary)tr.GetObject(
                            nod.GetAt(DicionarioServicos), OpenMode.ForRead);

                        foreach (DBDictionaryEntry e in dic)
                        {
                            var xr = tr.GetObject(e.Value, OpenMode.ForRead) as Xrecord;
                            if (xr == null || xr.Data == null) continue;
                            var v = xr.Data.AsArray();
                            if (v.Length < 2) continue;
                            string serv = Convert.ToString(v[0].Value) ?? "";
                            string art = Convert.ToString(v[1].Value) ?? "";
                            if (serv.Length > 0) lidos[serv] = art;
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("MQT: não consegui ler os serviços — " + ex.Message);
            }

            _artigoDoServico = lidos;
            _docServicos = doc;
            _servicosCarregados = true;
        }

        private static void GuardarServicos()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            try
            {
                using (doc.LockDocument())
                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var nod = (DBDictionary)tr.GetObject(
                        doc.Database.NamedObjectsDictionaryId, OpenMode.ForWrite);

                    if (nod.Contains(DicionarioServicos))
                    {
                        try { nod.Remove(DicionarioServicos); }
                        catch
                        {
                            var velho = (DBDictionary)tr.GetObject(
                                nod.GetAt(DicionarioServicos), OpenMode.ForWrite);
                            velho.Erase();
                        }
                    }

                    var dic = new DBDictionary();
                    nod.SetAt(DicionarioServicos, dic);
                    tr.AddNewlyCreatedDBObject(dic, true);

                    int i = 0;
                    foreach (var kv in _artigoDoServico)
                    {
                        var xr = new Xrecord();
                        xr.Data = new ResultBuffer(
                            new TypedValue((int)DxfCode.Text, Curto(kv.Key, 255)),
                            new TypedValue((int)DxfCode.Text, Curto(kv.Value, 500)));
                        dic.SetAt("S" + (i++).ToString("D4"), xr);
                        tr.AddNewlyCreatedDBObject(xr, true);
                    }

                    tr.Commit();
                }
                _docServicos = doc;
                _servicosCarregados = true;
            }
            catch (System.Exception ex)
            {
                PaletteHost.Log("MQT: não consegui guardar os serviços — " + ex.Message);
            }
        }

        /// <summary>
        /// O artigo a usar para uma medição deste serviço: o do serviço se
        /// existir, senão o que estiver escolhido no painel.
        ///
        /// É por aqui que passam TODAS as medições, e é o que faz o TSKMEDSEL
        /// pôr a alvenaria num artigo e o reboco noutro na mesma passagem.
        /// </summary>
        public static string ArtigoParaMedicao(string servico)
        {
            // MANDA O PAINEL, não a ligação por serviço.
            //
            // A ligação serviço→artigo é guardada POR DESENHO, e isso parte-se
            // no caso real: o mesmo DWG pode ter dois projectos em coordenadas
            // diferentes, e aí "REBOCO" pertence a artigos diferentes conforme
            // a zona. Uma ligação global dava a resposta errada em metade do
            // ficheiro, sem avisar.
            //
            // Assim, quem manda é o artigo que está escolhido no painel no
            // momento de medir. Custa repetir a escolha ao mudar de camada —
            // e repetir a pergunta dos vãos — mas é sempre o artigo que a
            // pessoa está a ver quando carrega no botão.
            //
            // A ligação por serviço continua guardada e serve de sugestão na
            // paleta: trocar de serviço traz o último artigo usado com ele.
            // Sugerir é útil; decidir sozinho não é.
            if (!string.IsNullOrEmpty(Config.Artigo)) return Config.Artigo;
            return ArtigoDoServico(servico);
        }

        // ==================================================================
        // Coerência entre a medição e o artigo
        // ==================================================================

        /// <summary>
        /// A unidade deste artigo bate certo com o que se está a medir?
        /// Devolve o aviso a escrever, ou "" quando está tudo bem.
        ///
        /// Avisa mas não trava: os mapas reais vêm com a unidade em branco
        /// (dois artigos no ficheiro do Palácio) e travar a meio de uma
        /// medição por causa disso seria pior do que o erro que evitava.
        /// </summary>
        public static string AvisoDeUnidade(string chaveArtigo, string unidadeMedida)
        {
            var n = Procurar(chaveArtigo);
            if (n == null) return "";
            if (string.IsNullOrEmpty(n.Unidade)) return "";
            if (string.IsNullOrEmpty(unidadeMedida)) return "";

            if (Igual(n.Unidade, unidadeMedida)) return "";

            return string.Format(
                "atenção: o artigo {0} está em «{1}» e esta medição é em «{2}».",
                n.Codigo, n.Unidade, unidadeMedida);
        }

        /// <summary>m² e m2 são a mesma unidade escrita de duas maneiras.</summary>
        public static bool Igual(string a, string b)
        {
            return Normalizar(a) == Normalizar(b);
        }

        private static string Normalizar(string u)
        {
            u = (u ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            u = u.Replace("²", "2").Replace("³", "3");
            if (u == "ml") u = "m";
            if (u == "und" || u == "unid") u = "un";
            return u;
        }
    }
}
