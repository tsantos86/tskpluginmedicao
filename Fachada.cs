using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    // O TipoFachada e o MedFachada passaram para o Medicoes.cs: são dados, e
    // ali a folha de medição pode ser posta sob teste sem arrastar o AutoCAD.

    /// <summary>Configuração atual da aba Materiais (definida na paleta).</summary>
    public static class FachadaConfig
    {
        public static string Material = "ETICS";
        public static string Piso = "PISO 0";
        /// <summary>Alçado / zona corrente (partilhado com a alvenaria).</summary>
        public static string Alcado = "";
        /// <summary>Alçados já usados, guardados entre sessões.</summary>
        public static readonly List<string> Alcados = new List<string>();
        public static double AlturaPiso = 3.00;

        /// <summary>Materiais criados pelo utilizador, guardados entre sessões.</summary>
        public static readonly List<string> Materiais = new List<string>();

        /// <summary>Cor escolhida para cada piso (usada na polyline e no gradiente).</summary>
        public static readonly Dictionary<string, System.Drawing.Color> CoresPiso =
            new Dictionary<string, System.Drawing.Color>
            {
                { "PISO 0", System.Drawing.Color.FromArgb(0, 150, 90) },
                { "PISO 1", System.Drawing.Color.FromArgb(0, 110, 190) },
                { "PISO 2", System.Drawing.Color.FromArgb(210, 120, 0) },
            };

        /// <summary>
        /// Cor de cada serviço, escolhida pelo utilizador. Uma por artigo, não
        /// uma por piso: numa planta o que se quer distinguir é a alvenaria do
        /// reboco e do revestimento, que convivem no mesmo piso e por vezes na
        /// mesma parede. A cor do piso ficou para as fachadas, onde faz sentido.
        /// </summary>
        public static readonly Dictionary<string, System.Drawing.Color> CoresServico =
            new Dictionary<string, System.Drawing.Color>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// A cor deste serviço. Sem nenhuma escolhida, gera uma estável a
        /// partir do nome — o mesmo serviço dá sempre a mesma cor, em qualquer
        /// desenho e em qualquer sessão. Antes variava, e duas paredes do
        /// mesmo artigo apareciam de cores diferentes.
        /// </summary>
        public static System.Drawing.Color CorDoServico(string servico)
        {
            System.Drawing.Color c;
            if (CoresServico.TryGetValue(servico ?? "", out c)) return c;

            int h = 0;
            foreach (char ch in (servico ?? "")) h = h * 31 + ch;
            h = System.Math.Abs(h);

            // Tons médios: escuros não se vêem sobre a planta, claros somem-se.
            return System.Drawing.Color.FromArgb(
                60 + (h % 140), 60 + ((h / 7) % 140), 60 + ((h / 53) % 140));
        }

        public static System.Drawing.Color CorDoPiso(string piso)
        {
            return CoresPiso.TryGetValue(piso, out var c)
                ? c
                : System.Drawing.Color.FromArgb(0, 150, 90);
        }

        // ---------------- persistência ----------------
        private static string Ficheiro => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TSKTakeOff", "materiais.txt");

        /// <summary>Formato por linha: MATERIAL  ou  PISO|R,G,B</summary>
        public static void Guardar()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Ficheiro);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

                var linhas = new List<string>();
                foreach (var m in Materiais) linhas.Add("M|" + m);
                foreach (var a in Alcados) linhas.Add("A|" + a);
                foreach (var kv in CoresPiso)
                    linhas.Add(string.Format("P|{0}|{1},{2},{3}",
                        kv.Key, kv.Value.R, kv.Value.G, kv.Value.B));

                System.IO.File.WriteAllLines(Ficheiro, linhas);
            }
            catch { /* preferência é acessório: nunca interromper a medição */ }
        }

        public static void Carregar()
        {
            try
            {
                if (!System.IO.File.Exists(Ficheiro)) return;
                foreach (var linha in System.IO.File.ReadAllLines(Ficheiro))
                {
                    var p = linha.Split('|');
                    if (p.Length >= 2 && p[0] == "M")
                    {
                        if (!Materiais.Contains(p[1])) Materiais.Add(p[1]);
                    }
                    else if (p.Length >= 2 && p[0] == "A")
                    {
                        if (!Alcados.Contains(p[1])) Alcados.Add(p[1]);
                    }
                    else if (p.Length >= 3 && p[0] == "P")
                    {
                        var rgb = p[2].Split(',');
                        if (rgb.Length == 3 &&
                            byte.TryParse(rgb[0], out byte r) &&
                            byte.TryParse(rgb[1], out byte g) &&
                            byte.TryParse(rgb[2], out byte b))
                            CoresPiso[p[1]] = System.Drawing.Color.FromArgb(r, g, b);
                    }
                }
            }
            catch { }
        }

        /// <summary>Regista um alçado novo assim que é usado.</summary>
        public static void RegistarAlcado(string alcado)
        {
            if (string.IsNullOrWhiteSpace(alcado)) return;
            if (!Alcados.Contains(alcado))
            {
                Alcados.Add(alcado);
                Alcados.Sort();
                Guardar();
            }
        }

        /// <summary>Regista um material novo assim que é usado.</summary>
        public static void RegistarMaterial(string material)
        {
            if (string.IsNullOrWhiteSpace(material)) return;
            if (!Materiais.Contains(material))
            {
                Materiais.Add(material);
                Materiais.Sort();
                Guardar();
            }
        }
    }

    /// <summary>Repositório das medições de fachada (XData no DWG).</summary>
    public static class FacRepo
    {
        public const string AppName = "CASQUILHO_FAC";

        public static List<MedFachada> Carregar(Database db)
        {
            var lista = new List<MedFachada>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass != Util.ClPolyline) continue;
                    var m = LerFachada(tr.GetObject(id, OpenMode.ForRead) as Polyline);
                    if (m != null) lista.Add(m);
                }
                tr.Commit();
            }
            return lista;
        }

        /// <summary>Lê um pano de fachada de uma polyline aberta, ou null.</summary>
        internal static MedFachada LerFachada(Polyline pl)
        {
            if (pl == null) return null;
            using (ResultBuffer rb = pl.GetXDataForApplication(AppName))
            {
                if (rb == null) return null;

                var med = ParseXData(rb);
                med.Handle = pl.Handle.ToString();

                if (med.Tipo == TipoFachada.Retangulo)
                {
                    // Lados reais do retângulo (imune a UCS rodado):
                    // o 1º lado é o horizontal no momento do desenho.
                    double lado1, lado2;
                    AlvRepo.LadosDoRetangulo(pl, out lado1, out lado2);
                    med.Comp = lado1;
                    med.Alt = lado2;
                    med.Area = pl.Closed ? System.Math.Abs(pl.Area) : lado1 * lado2;
                }
                else
                {
                    med.Comp = pl.Length;
                    med.Area = med.Comp * med.Alt;
                }
                return med;
            }
        }

        public static bool Remover(string handle)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (!long.TryParse(handle, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value))
                return false;
            if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased)
                return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                ent.Erase();
                tr.Commit();
            }
            return true;
        }

        public static void GravarXData(Polyline pl, string material, string piso,
            TipoFachada tipo, double alturaPiso, string vaosSerializados = "",
            string alcado = "", bool separador = false, string marca = "", string textoTitulo = "",
            string artigo = "")
        {
            pl.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, material),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, piso),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString,
                    tipo == TipoFachada.Retangulo ? "RET" : "POL"),
                new TypedValue((int)DxfCode.ExtendedDataReal, alturaPiso),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, vaosSerializados),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, alcado ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, separador ? "1" : "0"),
                // s6 — título depois desta medição. No fim de propósito:
                // desenhos medidos antes disto continuam a ler bem.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, marca ?? ""),
                // s7 — texto que o utilizador escreveu na linha de título.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, textoTitulo ?? ""),
                // s8 — artigo do mapa. No fim pela mesma razão do s6: um
                // desenho medido antes disto lê-se na mesma, fica é sem artigo.
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, artigo ?? ""));
        }

        /// <summary>
        /// Acrescenta ou tira um nível de título (CAP/ART), mantendo os outros.
        /// É o que os botões Capítulo / Artigo da aba Materiais usam — o mesmo
        /// contrato da Alvenaria.
        /// </summary>
        public static bool AlternarTitulo(string handle, string marca)
        {
            return Editar(handle, (pl, med) => med.AlternarMarca(marca));
        }

        /// <summary>Grava o texto de um dos títulos desta medição (por posição).</summary>
        public static bool DefinirTextoDeTitulo(string handle, int indice, string texto)
        {
            return Editar(handle, (pl, med) => med.DefinirTextoDaMarca(indice, texto));
        }

        /// <summary>Abre a medição pelo handle e aplica uma alteração.</summary>
        private static bool Editar(string handle,
            Action<Polyline, MedFachada> alteracao)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (!long.TryParse(handle, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value)) return false;
            if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased) return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = tr.GetObject(id, OpenMode.ForWrite) as Polyline;
                if (pl == null) return false;

                MedFachada med;
                using (ResultBuffer rb = pl.GetXDataForApplication(AppName))
                {
                    if (rb == null) return false;
                    med = ParseXData(rb);
                }

                alteracao(pl, med);

                // GRAVAR. A alteração mexe no objecto lido, que é uma cópia —
                // sem esta linha o Commit não levava nada e os botões Capítulo
                // / Artigo da aba Materiais não faziam absolutamente nada, sem
                // sequer dar erro.
                GravarXData(pl, med.Material, med.Piso, med.Tipo, med.Alt,
                    med.SerializeVaos(), med.Alcado, med.Separador,
                    med.MarcaDepois, med.TextoTitulo, med.Artigo);

                tr.Commit();
            }
            return true;
        }

        /// <summary>Grava o artigo do mapa neste pano. O par do AlvRepo.DefinirArtigo.</summary>
        public static bool DefinirArtigo(string handle, string artigo)
        {
            return Editar(handle, (pl, med) => med.Artigo = artigo ?? "");
        }

        /// <summary>Define (ou limpa) o título que sai depois desta medição.</summary>
        public static bool DefinirMarca(string handle, string marca)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            // Mesma forma que os outros métodos deste repositório usam: o
            // FacRepo não tem o TryGetId do AlvRepo.
            if (!long.TryParse(handle, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value)) return false;
            if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased) return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = tr.GetObject(id, OpenMode.ForWrite) as Polyline;
                if (pl == null) return false;

                MedFachada med;
                using (ResultBuffer rb = pl.GetXDataForApplication(AppName))
                {
                    if (rb == null) return false;
                    med = ParseXData(rb);
                }

                // Sem marca não há título: o texto sai com ela, senão
                // reaparecia sozinho ao pôr um título novo nesta medição.
                string texto = string.IsNullOrEmpty(marca) ? "" : med.TextoTitulo;

                GravarXData(pl, med.Material, med.Piso, med.Tipo, med.Alt,
                    med.SerializeVaos(), med.Alcado, med.Separador, marca ?? "", texto,
                    med.Artigo);
                tr.Commit();
            }
            return true;
        }

        /// <summary>Liga/desliga a linha em branco antes desta medição.</summary>
        public static bool AlternarSeparador(string handle)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (!long.TryParse(handle, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value)) return false;
            if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased) return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = tr.GetObject(id, OpenMode.ForWrite) as Polyline;
                if (pl == null) return false;

                MedFachada med;
                using (ResultBuffer rb = pl.GetXDataForApplication(AppName))
                {
                    if (rb == null) return false;
                    med = ParseXData(rb);
                }
                GravarXData(pl, med.Material, med.Piso, med.Tipo, med.Alt,
                    med.SerializeVaos(), med.Alcado, !med.Separador, med.MarcaDepois,
                    med.TextoTitulo, med.Artigo);
                tr.Commit();
            }
            return true;
        }

        /// <summary>Acrescenta vãos à medição identificada pelo handle.</summary>
        public static bool AdicionarVaos(string handle, IEnumerable<Vao> vaos)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return false;
            var db = doc.Database;

            if (!long.TryParse(handle, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out long value))
                return false;
            if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased)
                return false;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = tr.GetObject(id, OpenMode.ForWrite) as Polyline;
                if (pl == null) return false;

                MedFachada med;
                using (ResultBuffer rb = pl.GetXDataForApplication(AppName))
                {
                    if (rb == null) return false;
                    med = ParseXData(rb);
                }

                foreach (var v in vaos) med.Vaos.Add(v);
                GravarXData(pl, med.Material, med.Piso, med.Tipo, med.Alt,
                    med.SerializeVaos(), med.Alcado, med.Separador, med.MarcaDepois,
                    med.TextoTitulo, med.Artigo);
                tr.Commit();
            }
            return true;
        }

        private static MedFachada ParseXData(ResultBuffer rb)
        {
            var med = new MedFachada { Material = "ETICS", Piso = "PISO 0" };
            int stringIndex = 0;

            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                {
                    string s = tv.Value.ToString();
                    if (stringIndex == 0) med.Material = s;
                    else if (stringIndex == 1) med.Piso = s;
                    else if (stringIndex == 2)
                        med.Tipo = s == "POL" ? TipoFachada.PolylineAltura : TipoFachada.Retangulo;
                    else if (stringIndex == 3) med.DeserializeVaos(s);
                    else if (stringIndex == 4) med.Alcado = s;
                    else if (stringIndex == 5) med.Separador = (s == "1");
                    else if (stringIndex == 6) med.MarcaDepois = s;
                    else if (stringIndex == 7) med.TextoTitulo = s;
                    else if (stringIndex == 8) med.Artigo = s;
                    stringIndex++;
                }
                else if (tv.TypeCode == (int)DxfCode.ExtendedDataReal)
                {
                    med.Alt = (double)tv.Value; // altura do piso (usada no modo POL)
                }
            }
            return med;
        }
    }
}
