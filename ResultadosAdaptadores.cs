using System;
using System.Collections.Generic;
using System.Globalization;

namespace TSKTakeOff
{
    /// <summary>
    /// Traduz os modelos das outras três abas — Materiais, Lineares e
    /// Contagens — para a forma neutra de que a árvore de resultados vive.
    ///
    /// A projecção da alvenaria está no `ResultadosArvore.DeParedes`, e é do
    /// mesmo tipo. Estão em ficheiros separados por uma razão simples: a da
    /// alvenaria é a que define a hierarquia aprovada, e estas seguem-na. Se
    /// um dia divergirem, é melhor que a divergência seja visível.
    ///
    /// AS UNIDADES SÃO O QUE AQUI IMPORTA. Um pano de material fatura m², um
    /// rodapé fatura METRO e uma porta fatura UNIDADE — e o `Quantidades`
    /// nunca as soma entre si. É por isso que o comprimento de um pano entra
    /// como DIMENSÃO (vive nas propriedades) e o comprimento de um linear
    /// entra como QUANTIDADE: são a mesma grandeza física e coisas
    /// completamente diferentes na folha.
    ///
    /// Nenhum destes toca no AutoCAD: estão sob teste.
    /// </summary>
    public static class ResultadosAdaptadores
    {
        // ------------------------------------------------------------------
        // Materiais (panos de fachada)
        // ------------------------------------------------------------------

        /// <summary>
        /// Um pano de material: quantidade em m², com os vãos a descontar
        /// como na alvenaria.
        ///
        /// O `Material` faz de Serviço na hierarquia — é o que agrupa os panos
        /// da mesma natureza, tal como o Serviço agrupa a alvenaria. Não se
        /// inventou um campo novo no DWG para isto, que é o que a Fase 0 do
        /// plano proíbe.
        /// </summary>
        public static List<MedicaoResultado> DeMateriais(
            IEnumerable<MedFachada> panos, Func<string, bool> artigoConhecido,
            IFormatProvider cultura)
        {
            if (cultura == null) cultura = CultureInfo.CurrentCulture;
            var saida = new List<MedicaoResultado>();
            if (panos == null) return saida;

            foreach (var f in panos)
            {
                if (f == null) continue;

                var m = new MedicaoResultado
                {
                    Handle = f.Handle,
                    Piso = f.Piso,
                    Servico = f.Material,
                    Artigo = f.Artigo,
                    Alcado = f.Alcado,
                    Rotulo = RotuloDoPano(f),
                    Unidade = Unidades.M2,
                    Quantidade = f.AreaLiquida,
                    TipoMedida = "Materiais",
                    Comprimento = f.Comp,
                    UnidadeComprimento = Unidades.Metro,
                    Altura = f.Alt,
                    UnidadeAltura = Unidades.Metro
                };

                Alertas(m, f.Artigo, artigoConhecido);
                if (f.DescontoVaos > f.Area + 1e-9) m.Alertas |= AlertaNo.VaosExcessivos;

                // SÓ O ARTIGO SE EDITA, e é a verdade e não uma limitação da
                // vista: o FacRepo sabe gravar o artigo de um pano, mas não o
                // material nem o piso — esses ficam como foram medidos. Marcar
                // um campo como editável dá-lhe o realce amarelo e deixa
                // escrever; se a escrita não fosse a lado nenhum, seria pior do
                // que um campo bloqueado.
                var pr = m.Propriedades;
                pr.Add(new Propriedade("servico", "Material", f.Material ?? "", true, false));
                pr.Add(new Propriedade("artigo", "Artigo",
                    ResultadosArvore.TextoDoArtigo(f.Artigo), true, true));
                pr.Add(new Propriedade("piso", "Piso", f.Piso ?? "", true, false));
                pr.Add(new Propriedade("comprimento", "Comprimento",
                    Unidades.Texto(Unidades.Metro, f.Comp, cultura), true, false));
                pr.Add(new Propriedade("altura", "Altura",
                    Unidades.Texto(Unidades.Metro, f.Alt, cultura), true, false));
                pr.Add(new Propriedade("areaBruta", "Área bruta",
                    Unidades.Texto(Unidades.M2, f.Area, cultura), false, false));
                pr.Add(new Propriedade("desconto", "Desconto de vãos",
                    f.DescontoVaos > 0
                        ? "−" + Unidades.Texto(Unidades.M2, f.DescontoVaos, cultura)
                        : Unidades.Texto(Unidades.M2, 0.0, cultura), false, false));
                pr.Add(new Propriedade("quantidade", "Quantidade",
                    Unidades.Texto(Unidades.M2, f.AreaLiquida, cultura), true, false));
                if (!string.IsNullOrEmpty(f.Alcado))
                    pr.Add(new Propriedade("alcado", "Alçado / zona", f.Alcado, false, false));
                pr.Add(new Propriedade("handle", "Handle", f.Handle ?? "", false, false));

                for (int i = 0; i < f.Vaos.Count; i++)
                    m.Deducoes.Add(Deducao(f.Vaos[i], i, Unidades.M2,
                        f.Vaos[i].AreaTotal, cultura));

                var marcas = f.Marcas;
                for (int i = 0; i < marcas.Count; i++)
                    m.Titulos.Add(Titulo(marcas[i], f.TextoDaMarca(i), i));

                saida.Add(m);
            }
            return saida;
        }

        private static string RotuloDoPano(MedFachada f)
        {
            string tipo = f.Tipo == TipoFachada.Retangulo ? "Pano" : "Pano (polyline)";
            string handle = (f.Handle ?? "").Trim();
            return handle.Length > 0 ? tipo + " " + handle : tipo;
        }

        // ------------------------------------------------------------------
        // Lineares
        // ------------------------------------------------------------------

        /// <summary>
        /// Uma medição linear: a quantidade É o comprimento, em metros.
        ///
        /// É AQUI QUE ESTÁ A ARMADILHA QUE O BRIEF NOMEIA. O comprimento de uma
        /// parede e o comprimento de um rodapé não vão ao mesmo balde: aquele é
        /// uma dimensão de um artigo medido a m² e vive nas propriedades; este
        /// É a quantidade de um artigo medido a metro. Por isso o comprimento
        /// entra em `Quantidade` — e não como propriedade — só neste adaptador.
        /// </summary>
        public static List<MedicaoResultado> DeLineares(
            IEnumerable<MedItem> lineares, IFormatProvider cultura)
        {
            if (cultura == null) cultura = CultureInfo.CurrentCulture;
            var saida = new List<MedicaoResultado>();
            if (lineares == null) return saida;

            foreach (var l in lineares)
            {
                if (l == null) continue;

                var m = new MedicaoResultado
                {
                    Handle = l.Handle,
                    // Uma medição linear não tem piso no modelo. Fica no grupo
                    // «Sem piso», que é a verdade — e não um piso inventado.
                    Piso = "",
                    Servico = l.Categoria,
                    Artigo = "",
                    Layer = l.Layer,
                    Rotulo = string.IsNullOrEmpty((l.Handle ?? "").Trim())
                        ? "Linear" : "Linear " + l.Handle,
                    Unidade = Unidades.Metro,
                    Quantidade = l.Comprimento,
                    TipoMedida = "Lineares",
                    // O percurso aparece em "Comp." e É a quantidade. A
                    // altura fica em NaN: um rodapé não tem nenhuma, e um
                    // zero ali seria uma dimensão inventada.
                    Comprimento = l.Comprimento,
                    UnidadeComprimento = Unidades.Metro
                };

                var pr = m.Propriedades;
                pr.Add(new Propriedade("servico", "Categoria", l.Categoria ?? "", true, false));
                pr.Add(new Propriedade("quantidade", "Comprimento",
                    Unidades.Texto(Unidades.Metro, l.Comprimento, cultura), true, false));
                pr.Add(new Propriedade("vertices", "Vértices",
                    l.Vertices.ToString(CultureInfo.InvariantCulture), false, false));
                pr.Add(new Propriedade("layer", "Layer", l.Layer ?? "", false, false));
                pr.Add(new Propriedade("handle", "Handle", l.Handle ?? "", false, false));

                saida.Add(m);
            }
            return saida;
        }

        // ------------------------------------------------------------------
        // Contagens
        // ------------------------------------------------------------------

        /// <summary>
        /// Uma contagem: uma unidade cada, somadas em `un.`.
        ///
        /// Cada marca no desenho é UM objecto contado, por isso a quantidade é
        /// sempre 1 e é o grupo que faz a soma. Guardar aqui um total por
        /// categoria seria um segundo sítio a somar o que a árvore já soma —
        /// e dois sítios a somar acabam sempre por discordar.
        /// </summary>
        public static List<MedicaoResultado> DeContagens(
            IEnumerable<MedContagem> contagens, IFormatProvider cultura)
        {
            if (cultura == null) cultura = CultureInfo.CurrentCulture;
            var saida = new List<MedicaoResultado>();
            if (contagens == null) return saida;

            foreach (var c in contagens)
            {
                if (c == null) continue;

                string nome = (c.Nome ?? "").Trim();
                var m = new MedicaoResultado
                {
                    Handle = c.Handle,
                    Piso = c.Piso,
                    Servico = c.Categoria,
                    Artigo = "",
                    Bloco = c.Bloco,
                    Rotulo = nome.Length > 0 ? nome
                        : (string.IsNullOrEmpty((c.Handle ?? "").Trim())
                            ? "Contagem" : "Contagem " + c.Handle),
                    Unidade = Unidades.Unidade,
                    Quantidade = 1,
                    TipoMedida = "Contagens"
                };

                // TUDO DE LEITURA, e é a verdade e não uma limitação da vista:
                // o ContRepo não tem métodos para alterar uma contagem já
                // feita — só as cria e as apaga em bloco. Marcar estes campos
                // como editáveis dava-lhes o realce amarelo, deixava escrever,
                // e a escrita não ia a lado nenhum: um campo que promete
                // gravar e não grava é pior do que um campo bloqueado.
                //
                // Para mudar uma contagem, apaga-se e conta-se outra vez.
                var pr = m.Propriedades;
                pr.Add(new Propriedade("designacao", "Nome", nome, true, false));
                pr.Add(new Propriedade("servico", "Categoria", c.Categoria ?? "", true, false));
                pr.Add(new Propriedade("piso", "Piso", c.Piso ?? "", true, false));
                pr.Add(new Propriedade("quantidade", "Quantidade",
                    Unidades.Texto(Unidades.Unidade, 1, cultura), true, false));
                if (!string.IsNullOrEmpty(c.Bloco))
                    pr.Add(new Propriedade("bloco", "Bloco do desenho", c.Bloco, false, false));
                pr.Add(new Propriedade("handle", "Handle", c.Handle ?? "", false, false));

                saida.Add(m);
            }
            return saida;
        }

        // ------------------------------------------------------------------
        // Partilhado
        // ------------------------------------------------------------------

        private static void Alertas(MedicaoResultado m, string artigo,
                                    Func<string, bool> artigoConhecido)
        {
            if (artigoConhecido == null) return;
            if (string.IsNullOrEmpty(artigo)) m.Alertas |= AlertaNo.PorClassificar;
            else if (!artigoConhecido(artigo)) m.Alertas |= AlertaNo.ArtigoDesconhecido;
        }

        private static DeducaoResultado Deducao(Vao v, int indice, string unidade,
                                                double desconto, IFormatProvider cultura)
        {
            string designacao = (v.Designacao ?? "").Trim();
            string tipo = v.Tipo == TipoVao.Porta ? "Porta" : "Janela";

            var d = new DeducaoResultado
            {
                Indice = indice,
                Designacao = designacao,
                Desconto = desconto,
                Unidade = unidade,
                Rotulo = "Vão · " + (designacao.Length > 0 ? designacao : tipo),
                Propriedades = new List<Propriedade>(),
                TextoPesquisa = ResultadosArvore.Normalizar(designacao + " " + tipo + " vao")
            };

            d.Propriedades.Add(new Propriedade("designacao", "Designação", designacao, true, true));
            d.Propriedades.Add(new Propriedade("tipoVao", "Tipo", tipo, true, true));
            d.Propriedades.Add(new Propriedade("larguraVao", "Largura",
                Unidades.Texto(Unidades.Metro, v.Largura, cultura), true, true));
            d.Propriedades.Add(new Propriedade("alturaVao", "Altura",
                Unidades.Texto(Unidades.Metro, v.Altura, cultura), true, true));
            d.Propriedades.Add(new Propriedade("quantidadeVao", "Quantidade",
                Unidades.Texto(Unidades.Unidade, v.Quantidade, cultura), true, true));
            d.Propriedades.Add(new Propriedade("descontoVao", "Desconto",
                "−" + Unidades.Texto(unidade, desconto, cultura), true, false));
            return d;
        }

        private static TituloResultado Titulo(string nivel, string texto, int indice)
        {
            string nome = nivel == "CAP" ? "Capítulo" : "Artigo";
            string codigo = ChaveArtigo.Codigo(texto ?? "");
            string descricao = ChaveArtigo.Designacao(texto ?? "");

            var t = new TituloResultado
            {
                Indice = indice,
                Nivel = nivel,
                Codigo = codigo,
                Descricao = descricao,
                Propriedades = new List<Propriedade>(),
                Rotulo = "Título · " + nome +
                         (codigo.Length > 0 ? " · " + codigo : "") +
                         (descricao.Length > 0 ? " " + descricao : ""),
                TextoPesquisa = ResultadosArvore.Normalizar(
                    "titulo " + nome + " " + codigo + " " + descricao)
            };

            t.Propriedades.Add(new Propriedade("nivelTitulo", "Nível", nome, true, false));
            t.Propriedades.Add(new Propriedade("codigoTitulo", "Código", codigo, true, true));
            t.Propriedades.Add(new Propriedade("descricaoTitulo", "Descrição", descricao, true, true));
            return t;
        }
    }
}
