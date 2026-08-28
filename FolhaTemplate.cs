using System.Collections.Generic;
using System.Linq;

namespace TSKTakeOff
{
    /// <summary>Tipo de linha na folha do modelo da casa.</summary>
    public enum TipoLinhaTpl
    {
        Capitulo,   // nível 1 — título da folha (ex.: ALVENARIAS)
        Artigo,     // nível 3 — o que é medido; leva a unidade na coluna I
        Alcado,     // subtítulo livre
        Piso,       // subtítulo livre
        Medicao,    // linha com = / comp / larg / alt
        Deducao,    // igual, mas com = negativo (vãos, pilares…)
        Vazia
    }

    /// <summary>Uma linha já traduzida para as colunas do modelo.</summary>
    public class LinhaTpl
    {
        public TipoLinhaTpl Tipo;
        public string Item;        // A — código do artigo/capítulo
        public string Descricao;   // H
        public string Un;          // I
        public double? Qt;         // J  ("=")
        public double? Comp;       // K
        public double? Larg;       // L
        public double? Alt;        // M

        /// <summary>
        /// Handle da medição que gerou esta linha. Só nas linhas de ARTIGO do
        /// modelo clássico — é por aqui que se vai buscar, antes de a folha ser
        /// limpa, o que o utilizador escreveu no código/designação e se devolve
        /// ao DWG. Sem isto, o que se escrevesse na folha perdia-se na
        /// reescrita seguinte.
        /// </summary>
        public string HandleOrigem;
    }

    /// <summary>Conjunto de linhas destinado a uma folha do livro.</summary>
    public class FolhaCapitulo
    {
        public string Nome;
        public readonly List<LinhaTpl> Linhas = new List<LinhaTpl>();
    }

    /// <summary>
    /// Traduz as medições para o formato das folhas de medição da casa:
    ///
    ///   A art | B art_parciais | C..G auxiliares (fórmulas do modelo)
    ///   H descrição | I un | J "=" | K comp | L larg | M alt
    ///   N elementares | O parciais  (fórmulas do modelo)
    ///
    /// O plugin só escreve H..M. As colunas de fórmula ficam intactas, para
    /// as macros Ctrl+M (MD), Ctrl+E (EO) e Ctrl+Q (QT) continuarem a funcionar.
    /// A coluna A fica sempre em branco: os códigos de artigo são do mapa de
    /// trabalhos do projecto e escrevem-se à mão.
    /// </summary>
    public static class FolhaTemplate
    {
        // Colunas do modelo (1-based)
        public const int C_ART = 1, C_ART_PARC = 2,
                         C_DESC = 8, C_UN = 9, C_QT = 10,
                         C_COMP = 11, C_LARG = 12, C_ALT = 13;

        /// <summary>
        /// Separador código+designação — o mesmo 0x1F do MapaQuantidades.Sep e
        /// do FolhaMedicao. É o protocolo entre o DWG, o mapa do cliente e a
        /// folha: estável de propósito. Constante local para este ficheiro não
        /// depender do MapaQuantidades (que toca no AutoCAD) e poder correr
        /// nos testes, como o FolhaMedicao.
        /// </summary>
        private const string Sep = "\u001f";

        /// <summary>Primeira linha de dados (5 = cabeçalho, 6 = fórmulas de arranque).</summary>
        public const int PrimeiraLinha = 7;

        /// <summary>Até onde o modelo traz as fórmulas C:G e N:O já preenchidas.</summary>
        public const int UltimaLinhaModelo = 1462;

        public const string FolhaAlvenarias = "ALVENARIAS";
        public const string FolhaMateriais = "MATERIAIS";

        /// <summary>
        /// Folha das medições lineares (MEDIR/TSKLINEAR) no modelo clássico.
        /// Nome usado também no modo item, para que os dois não divirjam.
        /// </summary>
        public const string FolhaLineares = "MEDIÇÕES LINEARES";

        // ==================================================================
        /// <summary>Uma folha por capítulo; folhas sem medições não são criadas.</summary>
        public static List<FolhaCapitulo> Construir(IList<Parede> paredes,
            IList<MedFachada> fachadas, RegraDesconto regra)
        {
            return Construir(paredes, fachadas, null, null, regra);
        }

        /// <summary>Constrói também a folha clássica de contagens, quando existirem.</summary>
        public static List<FolhaCapitulo> Construir(IList<Parede> paredes,
            IList<MedFachada> fachadas, IList<MedContagem> contagens, RegraDesconto regra)
        {
            return Construir(paredes, fachadas, null, contagens, regra);
        }

        /// <summary>Constrói também as folhas de lineares e contagens, quando existirem.</summary>
        public static List<FolhaCapitulo> Construir(IList<Parede> paredes,
            IList<MedFachada> fachadas, IList<MedItem> lineares,
            IList<MedContagem> contagens, RegraDesconto regra)
        {
            var folhas = new List<FolhaCapitulo>();

            // Mesma ordem do FolhaMedicao.Construir: alvenaria primeiro.
            if (paredes != null && paredes.Count > 0)
                folhas.Add(DasParedes(paredes, regra));

            if (fachadas != null && fachadas.Count > 0)
                folhas.Add(DasFachadas(fachadas));

            if (lineares != null && lineares.Count > 0)
                folhas.Add(DasLineares(lineares));

            if (contagens != null && contagens.Count > 0)
                folhas.Add(DasContagens(contagens));

            return folhas;
        }

        // ------------------------------------------------------------------
        private static FolhaCapitulo DasParedes(IList<Parede> paredes, RegraDesconto regra)
        {
            var f = new FolhaCapitulo { Nome = FolhaAlvenarias };
            Titulo(f, TipoLinhaTpl.Capitulo, FolhaAlvenarias);
            Vazia(f);

            // O código do artigo/capítulo tem de ser o nível de agrupamento
            // desta folha. Antes o modelo clássico agrupava apenas por serviço
            // e deixava a coluna A vazia; assim um código editado na grelha
            // ficava guardado no DWG, mas nunca chegava ao Excel.
            foreach (var gArtigo in paredes.GroupBy(p => p.Artigo ?? "")
                                            .OrderBy(g => g.Key))
            foreach (var gServico in gArtigo.GroupBy(p => p.Servico)
                                             .OrderBy(g => g.Key))
            {
                string codigo, descricao;
                SepararArtigo(gArtigo.Key, out codigo, out descricao);
                Artigo(f, codigo,
                    string.IsNullOrWhiteSpace(descricao) ? gServico.Key : descricao,
                    UnidadeDe(gServico),
                    // A linha de artigo nasce de uma medição do bloco: é o
                    // handle dela que permite devolver ao DWG o que se escrever
                    // aqui na folha (ver RecolherArtigosClassicos).
                    gServico.First().Handle);

                foreach (var gAlcado in gServico
                    .GroupBy(p => Chave(p.Bloco, p.Alcado)).OrderBy(g => g.Key))
                {
                    if (!string.IsNullOrWhiteSpace(gAlcado.Key))
                        Titulo(f, TipoLinhaTpl.Alcado, gAlcado.Key);

                    foreach (var gPiso in gAlcado.GroupBy(p => p.Piso).OrderBy(g => g.Key))
                    {
                        Titulo(f, TipoLinhaTpl.Piso, Ou(gPiso.Key, "Geral"));

                        int n = 1;
                        foreach (var p in gPiso)
                        {
                            if (p.Separador) Vazia(f);

                            f.Linhas.Add(new LinhaTpl
                            {
                                Tipo = TipoLinhaTpl.Medicao,
                                Descricao = "parede " + n++,
                                Qt = 1,
                                Comp = p.Comprimento,
                                Larg = p.Largura > 0 ? p.Largura : (double?)null,
                                Alt = p.Altura > 0 ? p.Altura : (double?)null
                            });

                            foreach (var v in p.Vaos)
                            {
                                if (v.Desconto(regra) <= 0) continue;
                                f.Linhas.Add(Deducao(v));
                            }
                        }
                        Vazia(f);
                    }
                }
            }
            return f;
        }

        // ------------------------------------------------------------------
        private static FolhaCapitulo DasLineares(IList<MedItem> items)
        {
            var f = new FolhaCapitulo { Nome = FolhaLineares };
            Titulo(f, TipoLinhaTpl.Capitulo, FolhaLineares);
            Vazia(f);

            foreach (var g in items.GroupBy(i => i.Categoria).OrderBy(x => x.Key))
            {
                Artigo(f, g.Key, "m");

                int n = 1;
                foreach (var it in g)
                {
                    f.Linhas.Add(new LinhaTpl
                    {
                        Tipo = TipoLinhaTpl.Medicao,
                        Descricao = "medição " + n++,
                        Qt = 1,
                        Comp = it.Comprimento
                    });
                }
                Vazia(f);
            }
            return f;
        }

        // ------------------------------------------------------------------
        private static FolhaCapitulo DasContagens(IList<MedContagem> contagens)
        {
            var f = new FolhaCapitulo { Nome = "CONTAGENS" };
            Titulo(f, TipoLinhaTpl.Capitulo, "CONTAGENS");
            Vazia(f);

            foreach (var gCategoria in contagens
                .GroupBy(c => string.IsNullOrWhiteSpace(c.Categoria) ? "CONTAGENS" : c.Categoria)
                .OrderBy(g => g.Key))
            {
                Artigo(f, gCategoria.Key, "un");

                foreach (var gPiso in gCategoria.GroupBy(c => c.Piso ?? "").OrderBy(g => g.Key))
                {
                    Titulo(f, TipoLinhaTpl.Piso, Ou(gPiso.Key, "Geral"));
                    foreach (var gNome in gPiso.GroupBy(c => c.Nome ?? "").OrderBy(g => g.Key))
                    {
                        f.Linhas.Add(new LinhaTpl
                        {
                            Tipo = TipoLinhaTpl.Medicao,
                            Descricao = gNome.Key,
                            Qt = gNome.Count()
                        });
                    }
                    Vazia(f);
                }
            }
            return f;
        }

        // ------------------------------------------------------------------
        private static FolhaCapitulo DasFachadas(IList<MedFachada> fachadas)
        {
            var f = new FolhaCapitulo { Nome = FolhaMateriais };
            Titulo(f, TipoLinhaTpl.Capitulo, FolhaMateriais);
            Vazia(f);

            // Pelo artigo primeiro, como na folha das alvenarias: o código do
            // mapa é que manda na coluna A. Sem artigo cai-se no que isto
            // sempre fez — o material como descrição e a coluna A vazia.
            foreach (var gArtigo in fachadas.GroupBy(x => x.Artigo ?? "")
                                            .OrderBy(g => g.Key))
            foreach (var gMat in gArtigo.GroupBy(x => x.Material).OrderBy(g => g.Key))
            {
                string codigo, descricao;
                SepararArtigo(gArtigo.Key, out codigo, out descricao);
                Artigo(f, codigo,
                    string.IsNullOrWhiteSpace(descricao) ? gMat.Key : descricao,
                    "m2",
                    gMat.First().Handle);

                foreach (var gAlcado in gMat.GroupBy(x => x.Alcado ?? "").OrderBy(g => g.Key))
                {
                    if (!string.IsNullOrWhiteSpace(gAlcado.Key))
                        Titulo(f, TipoLinhaTpl.Alcado, gAlcado.Key);

                    foreach (var gPiso in gAlcado.GroupBy(x => x.Piso).OrderBy(g => g.Key))
                    {
                        Titulo(f, TipoLinhaTpl.Piso, Ou(gPiso.Key, "Geral"));

                        int n = 1;
                        foreach (var med in gPiso)
                        {
                            if (med.Separador) Vazia(f);

                            f.Linhas.Add(new LinhaTpl
                            {
                                Tipo = TipoLinhaTpl.Medicao,
                                Descricao = "pano " + n++,
                                Qt = 1,
                                Comp = med.Comp,
                                Alt = med.Alt > 0 ? med.Alt : (double?)null
                            });

                            foreach (var v in med.Vaos)
                                f.Linhas.Add(Deducao(v));
                        }
                        Vazia(f);
                    }
                }
            }
            return f;
        }

        // ------------------------------------------------------------------
        private static LinhaTpl Deducao(Vao v)
        {
            return new LinhaTpl
            {
                Tipo = TipoLinhaTpl.Deducao,
                Descricao = string.IsNullOrWhiteSpace(v.Designacao)
                    ? (v.Tipo == TipoVao.Janela ? "janela" : "porta")
                    : v.Designacao,
                Qt = -v.Quantidade,
                Comp = v.Largura,
                Alt = v.Altura > 0 ? v.Altura : (double?)null
            };
        }

        /// <summary>
        /// A unidade sai das dimensões que ficam mesmo preenchidas: o modelo
        /// multiplica comp × larg × alt conforme as células que encontra, por
        /// isso uma parede com largura dá volume e não área.
        /// </summary>
        private static string UnidadeDe(IEnumerable<Parede> paredes)
        {
            bool todasComLargura = true, todasComAltura = true;
            foreach (var p in paredes)
            {
                if (p.Largura <= 0) todasComLargura = false;
                if (p.Altura <= 0) todasComAltura = false;
            }

            if (todasComAltura && todasComLargura) return "m3";
            if (todasComAltura) return "m2";
            return "m";
        }

        private static void Artigo(FolhaCapitulo f, string descricao, string un)
        {
            Artigo(f, "", descricao, un, "");
        }

        private static void Artigo(FolhaCapitulo f, string codigo,
            string descricao, string un, string handleOrigem = "")
        {
            f.Linhas.Add(new LinhaTpl
            {
                Tipo = TipoLinhaTpl.Artigo,
                Item = codigo ?? "",
                Descricao = Ou(descricao, "Artigo"),
                Un = un,
                HandleOrigem = handleOrigem ?? ""
            });
        }

        private static void SepararArtigo(string artigo, out string codigo,
            out string descricao)
        {
            codigo = "";
            descricao = artigo ?? "";
            int separador = descricao.IndexOf(Sep, System.StringComparison.Ordinal);
            if (separador < 0) return;

            codigo = descricao.Substring(0, separador);
            descricao = descricao.Substring(separador + Sep.Length);
        }

        private static void Titulo(FolhaCapitulo f, TipoLinhaTpl tipo, string texto)
        {
            f.Linhas.Add(new LinhaTpl { Tipo = tipo, Descricao = texto });
        }

        private static void Vazia(FolhaCapitulo f)
        {
            // nunca duas linhas em branco seguidas, nem uma logo a abrir
            if (f.Linhas.Count == 0) return;
            if (f.Linhas[f.Linhas.Count - 1].Tipo == TipoLinhaTpl.Vazia) return;
            f.Linhas.Add(new LinhaTpl { Tipo = TipoLinhaTpl.Vazia });
        }

        private static string Ou(string valor, string alternativa)
        {
            return string.IsNullOrWhiteSpace(valor) ? alternativa : valor;
        }

        private static string Chave(string bloco, string alcado)
        {
            string a = string.IsNullOrWhiteSpace(alcado) ? "" : alcado;
            if (string.IsNullOrWhiteSpace(bloco)) return a;
            return string.IsNullOrWhiteSpace(a) ? bloco : bloco + " · " + a;
        }

        // ------------------------------------------------------------------
        /// <summary>Nome aceite pelo Excel: sem : \ / ? * [ ] e no máximo 31 caracteres.</summary>
        public static string NomeDeFolha(string nome)
        {
            if (string.IsNullOrWhiteSpace(nome)) return "Medições";
            var limpo = new System.Text.StringBuilder();
            foreach (char c in nome.Trim())
                limpo.Append(":\\/?*[]".IndexOf(c) >= 0 ? ' ' : c);

            string s = limpo.ToString().Trim();
            if (s.Length > 31) s = s.Substring(0, 31).Trim();
            return s.Length == 0 ? "Medições" : s;
        }
    }
}
