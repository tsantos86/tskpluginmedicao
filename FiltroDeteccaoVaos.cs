using System;

namespace TSKTakeOff
{
    /// <summary>
    /// Regras puras do detector automático de vãos. Ficam separadas do AutoCAD
    /// para poderem ser cobertas pelos testes sem carregar acmgd/acdbmgd.
    /// </summary>
    internal static class FiltroDeteccaoVaos
    {
        /// <summary>
        /// As etiquetas criadas pelo próprio plugin vivem nas layers MED_* e
        /// contêm textos como "5,00 × 2,80 = 14,00 m²". Esse texto parece uma
        /// dimensão de vão; ignorar estas layers impede que uma medição antiga
        /// seja proposta como porta ou janela da medição nova.
        /// </summary>
        internal static bool EhLayerGeradaPeloPlugin(string layer, string prefixo)
        {
            if (string.IsNullOrWhiteSpace(layer) || string.IsNullOrWhiteSpace(prefixo))
                return false;

            return layer.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase);
        }
    }
}
