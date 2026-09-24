using System;
using System.Text.RegularExpressions;

namespace TSKTakeOff
{
    /// <summary>
    /// Regras puras do detector automático de vãos. Ficam separadas do AutoCAD
    /// para poderem ser cobertas pelos testes sem carregar acmgd/acdbmgd.
    /// </summary>
    internal static class FiltroDeteccaoVaos
    {
        /// <summary>
        /// Diz apenas se o nome pertence ao prefixo configurado pelo plugin.
        /// Não deve ser usado isoladamente para esconder entidades: projetos
        /// externos também podem ter layers MED_*.
        /// </summary>
        internal static bool EhLayerGeradaPeloPlugin(string layer, string prefixo)
        {
            if (string.IsNullOrWhiteSpace(layer) || string.IsNullOrWhiteSpace(prefixo))
                return false;

            return layer.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Reconhece o texto-resumo criado pelo próprio TSK.
        ///
        /// A layer, sozinha, não prova que uma entidade é nossa: muitos projetos
        /// de arquitetura também usam nomes MED_* para cotas e etiquetas de
        /// portas/janelas. Filtrar toda a layer fazia desaparecer ocorrências
        /// reais como VE.02. O resumo do TSK tem uma assinatura estável —
        /// "comprimento × altura = área m²" — e é apenas essa etiqueta que não
        /// deve voltar a entrar no detector de vãos.
        /// </summary>
        internal static bool EhEtiquetaGeradaPeloPlugin(string layer, string prefixo,
            string texto)
        {
            if (!EhLayerGeradaPeloPlugin(layer, prefixo) ||
                string.IsNullOrWhiteSpace(texto))
                return false;

            string t = texto.ToUpperInvariant()
                .Replace("\\P", " ")
                .Replace(" ", "");

            bool temProduto = Regex.IsMatch(t, @"\d(?:[.,]\d+)?[X×]\d");
            bool temArea = t.Contains("M²") || t.Contains("M2");
            return temProduto && t.Contains("=") && temArea;
        }

        /// <summary>
        /// Diz se duas leituras são a mesma abertura. É normal um bloco trazer
        /// a cota tanto no atributo como na definição (ou num XREF), e nesses
        /// casos a mesma porta aparecia duas vezes na confirmação.
        /// </summary>
        internal static bool SaoLeiturasDuplicadas(string designacaoA,
            double larguraA, double alturaA, string designacaoB,
            double larguraB, double alturaB, double distancia)
        {
            return string.Equals(designacaoA ?? "", designacaoB ?? "",
                       StringComparison.OrdinalIgnoreCase) &&
                   Math.Abs(larguraA - larguraB) <= 0.01 &&
                   Math.Abs(alturaA - alturaB) <= 0.01 &&
                   distancia <= 0.15;
        }
    }
}
