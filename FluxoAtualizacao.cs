namespace TSKTakeOff
{
    /// <summary>
    /// Decide se é preciso reler o DWG. A apresentação na paleta e a escrita no
    /// Excel são destinos independentes: o Excel ao vivo não pode depender de a
    /// paleta já ter sido criada.
    /// </summary>
    internal static class FluxoAtualizacao
    {
        internal static bool DeveLerDesenho(bool painelDisponivel, bool excelConectado)
        {
            return painelDisponivel || excelConectado;
        }
    }
}
