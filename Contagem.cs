using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>Estado da aba Contagens.</summary>
    public static class ContagemConfig
    {
        public static string Nome = "P.01";
        public static string Categoria = "";
        public static string Piso = "PISO 0";

        /// <summary>Raio do círculo, em unidades do desenho.</summary>
        public static double Raio = 0.25;

        /// <summary>Escreve o nome ao lado do círculo.</summary>
        public static bool ComTexto = true;

        /// <summary>Nomes já usados, para a lista da paleta.</summary>
        public static readonly List<string> Nomes = new List<string>();

        private static string Ficheiro => System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TSKTakeOff", "contagens.txt");

        public static void Guardar()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Ficheiro);
                if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);

                var linhas = new List<string>
                {
                    "R|" + Raio.ToString(CultureInfo.InvariantCulture),
                    "T|" + (ComTexto ? "1" : "0")
                };
                foreach (var n in Nomes) linhas.Add("N|" + n);
                System.IO.File.WriteAllLines(Ficheiro, linhas);
            }
            catch { /* preferência é acessório */ }
        }

        public static void Carregar()
        {
            try
            {
                if (!System.IO.File.Exists(Ficheiro)) return;
                foreach (var linha in System.IO.File.ReadAllLines(Ficheiro))
                {
                    var p = linha.Split('|');
                    if (p.Length < 2) continue;

                    if (p[0] == "N" && !Nomes.Contains(p[1])) Nomes.Add(p[1]);
                    else if (p[0] == "R" && double.TryParse(p[1], NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out double r) && r > 0) Raio = r;
                    else if (p[0] == "T") ComTexto = (p[1] == "1");
                }
            }
            catch { }
        }

        public static void RegistarNome(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return;
            if (!Nomes.Contains(nome))
            {
                Nomes.Add(nome);
                Nomes.Sort();
            }
            Guardar();
        }

        /// <summary>
        /// Cor determinística a partir do nome: dois nomes diferentes saem com
        /// cores diferentes, e o mesmo nome sai sempre igual em qualquer sessão.
        /// </summary>
        public static short CorDoNome(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return 1;
            int h = 0;
            foreach (char c in nome) h = (h * 31 + c) & 0x7FFFFFFF;

            // Paleta ACI legível sobre fundo escuro e claro, sem cinzentos.
            short[] cores = { 1, 2, 3, 4, 5, 6, 30, 40, 50, 90, 130, 170, 190, 210, 230 };
            return cores[h % cores.Length];
        }
    }

    /// <summary>Repositório das contagens (XData no círculo).</summary>
    public static class ContRepo
    {
        public const string AppName = "CASQUILHO_CONT";
        public const string LayerPrefix = "MED_CONT_";

        public static List<MedContagem> Carregar(Database db)
        {
            var lista = new List<MedContagem>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass != Util.ClCircle) continue;
                    var m = LerContagem(tr.GetObject(id, OpenMode.ForRead) as Circle);
                    if (m != null) lista.Add(m);
                }
                tr.Commit();
            }
            return lista;
        }

        /// <summary>Lê uma contagem de um círculo já aberto, ou null.</summary>
        internal static MedContagem LerContagem(Circle c)
        {
            if (c == null) return null;
            using (ResultBuffer rb = c.GetXDataForApplication(AppName))
            {
                if (rb == null) return null;
                var m = ParseXData(rb);
                m.Handle = c.Handle.ToString();
                return m;
            }
        }

        public static void GravarXData(Circle c, MedContagem m)
        {
            c.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, m.Nome ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, m.Piso ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, m.Categoria ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, m.Bloco ?? ""));
        }

        private static MedContagem ParseXData(ResultBuffer rb)
        {
            var m = new MedContagem();
            int i = 0;
            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode != (int)DxfCode.ExtendedDataAsciiString) continue;
                string s = tv.Value.ToString();
                if (i == 0) m.Nome = s;
                else if (i == 1) m.Piso = s;
                else if (i == 2) m.Categoria = s;
                else if (i == 3) m.Bloco = s;
                i++;
            }
            return m;
        }

        /// <summary>
        /// Centro geométrico do bloco. O ponto de inserção serve de recurso,
        /// mas raramente é o centro: numa porta costuma estar na ombreira.
        /// </summary>
        public static Point3d CentroDoBloco(BlockReference br)
        {
            try
            {
                var ext = br.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                    0.0);
            }
            catch
            {
                return new Point3d(br.Position.X, br.Position.Y, 0.0);
            }
        }

        /// <summary>Apaga todas as contagens de um nome (ou todas, se nome vazio).</summary>
        public static int Limpar(Database db, string nome)
        {
            int apagadas = 0;
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return 0;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(
                    Util.EspacoMedicoesId(db), OpenMode.ForRead);

                var apagar = new List<ObjectId>();
                foreach (ObjectId id in ms)
                {
                    if (id.ObjectClass != Util.ClCircle) continue;
                    var c = tr.GetObject(id, OpenMode.ForRead) as Circle;
                    if (c == null) continue;
                    using (ResultBuffer rb = c.GetXDataForApplication(AppName))
                    {
                        if (rb == null) continue;
                        // Sem maiúsculas/minúsculas: "P.01" tem de apagar as
                        // contagens guardadas como "p.01" — o nome é o mesmo.
                        if (!string.IsNullOrEmpty(nome) &&
                            !string.Equals(ParseXData(rb).Nome, nome,
                                StringComparison.OrdinalIgnoreCase)) continue;
                        apagar.Add(id);
                    }
                }

                foreach (var id in apagar)
                {
                    var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                    ent.Erase();
                    apagadas++;
                }

                // Os rótulos vivem na mesma layer da contagem: saem com ela.
                if (apagadas > 0) apagadas += ApagarTextos(tr, ms, nome);

                tr.Commit();
            }
            return apagadas;
        }

        private static int ApagarTextos(Transaction tr, BlockTableRecord ms, string nome)
        {
            string layer = string.IsNullOrEmpty(nome) ? null : Layer(nome);
            var apagar = new List<ObjectId>();

            foreach (ObjectId id in ms)
            {
                if (id.ObjectClass != Util.ClMText) continue;
                var t = tr.GetObject(id, OpenMode.ForRead) as MText;
                if (t == null) continue;
                if (!t.Layer.StartsWith(LayerPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (layer != null &&
                    !string.Equals(t.Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;
                apagar.Add(id);
            }

            foreach (var id in apagar)
                ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase();

            return 0;   // não contam para o total de contagens
        }

        /// <summary>Layer própria por nome, para se poder isolar ou desligar.</summary>
        public static string Layer(string nome)
        {
            string limpo = (nome ?? "").Trim();
            foreach (char c in "<>/\\\":;?*|=`,")
                limpo = limpo.Replace(c, '_');
            return LayerPrefix + (limpo.Length == 0 ? "GERAL" : limpo);
        }
    }
}
