using System.Collections.Generic;

namespace TSKTakeOff
{
    // ======================================================================
    // Os dados de uma medição, sem uma linha de AutoCAD.
    //
    // Estas classes viviam ao pé dos repositórios que as lêem do desenho — o
    // MedFachada no Fachada.cs, o MedContagem no Contagem.cs — e esses
    // ficheiros ligam-se ao acmgd. Só que quem as CONSOME é a FolhaMedicao,
    // que é aritmética e arrumação: nem toca no desenho.
    //
    // Enquanto as classes estiveram naqueles ficheiros, a folha não podia ser
    // posta sob teste, porque arrastava o AutoCAD atrás. E a folha é
    // exactamente onde os erros aparecem — ordem trocada, linhas em branco a
    // mais, um sub-total na coluna errada — e são erros que não rebentam:
    // saem num ficheiro que vai para o cliente com ar de estar bem.
    //
    // Aqui, são dados. Quem os lê do DWG continua no seu sítio.
    // ======================================================================

    public enum TipoFachada { Retangulo = 0, PolylineAltura = 1 }

    /// <summary>
    /// Medição de fachada/ETICS: retângulo em alçado (comp × alt reais do desenho)
    /// ou polyline em planta × altura do piso.
    /// </summary>
    public class MedFachada
    {
        public string Handle { get; set; }
        public string Material { get; set; }   // ex.: ETICS
        public string Piso { get; set; }       // ex.: PISO 0
        /// <summary>Alçado / zona (ex.: "Alçado Tardoz").</summary>
        public string Alcado { get; set; } = "";
        /// <summary>Emite uma linha em branco antes desta medição no Excel.</summary>
        public bool Separador { get; set; }
        /// <summary>Título a emitir DEPOIS desta medição: "CAP", "ART" ou vazio.</summary>
        public string MarcaDepois { get; set; } = "";
        /// <summary>Texto escrito pelo utilizador na linha de título (código  descrição).</summary>
        public string TextoTitulo { get; set; } = "";
        /// <summary>
        /// Artigo do mapa a que este pano pertence, como "código + 0x1F +
        /// descrição". Vazio quando não há mapa importado.
        ///
        /// O mesmo campo que a Parede já tinha. Sem ele, um pano medido com um
        /// artigo escolhido no painel saía no Excel debaixo do material e não
        /// do artigo — a medição estava lá, mas não onde a pessoa a foi
        /// procurar.
        /// </summary>
        public string Artigo { get; set; } = "";
        public TipoFachada Tipo { get; set; }
        public double Comp { get; set; }       // largura (RET) ou comprimento (POL)
        public double Alt { get; set; }        // altura (RET: do próprio retângulo; POL: definida)
        public double Area { get; set; }       // área bruta

        public List<Vao> Vaos { get; } = new List<Vao>();

        /// <summary>
        /// O desconto de vãos segundo a regra escolhida — a mesma que a
        /// <see cref="Parede"/> já respeita, e pela mesma razão: sem o
        /// parâmetro, um pano medido com a regra SINAPI ou "não descontar"
        /// ligada acabava sempre a descontar a área toda, porque nada aqui
        /// perguntava qual era a regra em vigor.
        ///
        /// Não há versão sem argumento — de propósito, para não voltar a
        /// existir um sítio a descontar "tudo" em silêncio. É o mesmo
        /// contrato de <see cref="Parede.DescontoVaos"/>.
        /// </summary>
        public double DescontoVaos(RegraDesconto regra)
        {
            double soma = 0;
            foreach (var v in Vaos) soma += v.Desconto(regra);
            return soma;
        }

        /// <summary>Área líquida segundo a regra escolhida.</summary>
        public double AreaLiquida(RegraDesconto regra)
        {
            return System.Math.Max(0.0, Area - DescontoVaos(regra));
        }

        // ------------------------------------------------------------------
        // Títulos (CAP/ART) — o mesmo contrato da Parede, para a aba Materiais
        // suportar os dois níveis em simultâneo como a Alvenaria já faz.
        // ------------------------------------------------------------------

        /// <summary>Os títulos desta medição, por ordem de saída: capítulo e artigo.</summary>
        public List<string> Marcas
        {
            get
            {
                var r = new List<string>();
                foreach (var m in (MarcaDepois ?? "").Split(';'))
                    if (m == "CAP" || m == "ART") r.Add(m);
                return r;
            }
        }

        /// <summary>O texto do título válido que está nesta posição da lista.</summary>
        public string TextoDaMarca(int indice)
        {
            if (indice < 0) return "";
            var marcas = (MarcaDepois ?? "").Split(';');
            var textos = (TextoTitulo ?? "").Split(';');
            int valido = 0;
            for (int i = 0; i < marcas.Length; i++)
            {
                if (marcas[i] != "CAP" && marcas[i] != "ART") continue;
                if (valido == indice)
                    return i < textos.Length ? textos[i] : "";
                valido++;
            }
            return "";
        }

        /// <summary>
        /// Acrescenta ou remove capítulo/artigo, mantendo essa ordem. Os outros
        /// níveis ficam: um sub-artigo por baixo de um artigo não apaga o artigo.
        /// </summary>
        public void AlternarMarca(string marca)
        {
            if (marca != "CAP" && marca != "ART") return;

            var marcas = new List<string>();
            var textos = new List<string>();
            var antigas = (MarcaDepois ?? "").Split(';');
            var textosAntigos = (TextoTitulo ?? "").Split(';');
            for (int k = 0; k < antigas.Length; k++)
            {
                if (antigas[k] != "CAP" && antigas[k] != "ART") continue;
                marcas.Add(antigas[k]);
                textos.Add(k < textosAntigos.Length ? textosAntigos[k] : "");
            }

            int i = marcas.IndexOf(marca);
            if (i >= 0)
            {
                marcas.RemoveAt(i);
                textos.RemoveAt(i);
            }
            else
            {
                marcas.Add(marca);
                textos.Add("");
                var ordem = new List<string> { "CAP", "ART" };
                var pares = new List<KeyValuePair<string, string>>();
                for (int k = 0; k < marcas.Count; k++)
                    pares.Add(new KeyValuePair<string, string>(marcas[k], textos[k]));
                pares.Sort((a, b) => ordem.IndexOf(a.Key).CompareTo(ordem.IndexOf(b.Key)));
                marcas.Clear(); textos.Clear();
                foreach (var par in pares) { marcas.Add(par.Key); textos.Add(par.Value); }
            }

            MarcaDepois = string.Join(";", marcas.ToArray());
            TextoTitulo = string.Join(";", textos.ToArray());
        }

        /// <summary>Grava o texto do título que está nesta posição.</summary>
        public void DefinirTextoDaMarca(int indice, string texto)
        {
            var marcas = Marcas;
            var textos = new List<string>((TextoTitulo ?? "").Split(';'));
            while (textos.Count < marcas.Count) textos.Add("");
            if (indice < 0 || indice >= marcas.Count) return;

            // O ";" separa títulos e o "\u001f" separa código de descrição:
            // nenhum dos dois pode viajar dentro do texto.
            textos[indice] = (texto ?? "").Replace(";", ",");
            TextoTitulo = string.Join(";", textos.ToArray());
        }

        public string SerializeVaos()
        {
            var partes = new List<string>();
            foreach (var v in Vaos) partes.Add(v.Serialize());
            return string.Join(";", partes);
        }

        public void DeserializeVaos(string s)
        {
            Vaos.Clear();
            if (string.IsNullOrWhiteSpace(s)) return;
            foreach (var parte in s.Split(';'))
            {
                var v = Vao.Deserialize(parte);
                if (v != null) Vaos.Add(v);
            }
        }
    }

    /// <summary>
    /// Uma contagem: um círculo desenhado no centro de um bloco (porta, janela,
    /// louça, equipamento) a dizer "este já foi contado".
    ///
    /// Uma marca por bloco, não uma marca por lote. É o que permite ver no
    /// desenho o que falta, apagar uma que esteja a mais, e ter o total sempre
    /// certo — o total é simplesmente quantos círculos existem com aquele nome.
    /// </summary>
    public class MedContagem
    {
        public string Handle { get; set; }
        /// <summary>Nome dado pelo utilizador: P.01, J.02, VE.10…</summary>
        public string Nome { get; set; } = "";
        public string Piso { get; set; } = "";
        /// <summary>Categoria/artigo a que pertence (ex.: CARPINTARIAS). Opcional.</summary>
        public string Categoria { get; set; } = "";
        /// <summary>Nome do bloco que foi contado — só para diagnóstico.</summary>
        public string Bloco { get; set; } = "";
    }
}
