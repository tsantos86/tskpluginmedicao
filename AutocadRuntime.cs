using System;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Dados simples do AutoCAD capturados no contexto do comando/thread principal.
    ///
    /// A API AutoCAD/ObjectARX não é thread-safe. Este objeto permite que
    /// trabalhos de rede, licença e telemetria em background usem apenas
    /// strings já capturadas, sem consultar AcadApp nessa thread.
    /// </summary>
    internal static class AutocadRuntime
    {
        private static readonly object Sync = new object();
        private static string _versao = "?";
        private static string _produto = "AutoCAD";

        public static string VersaoAutoCAD
        {
            get { lock (Sync) return _versao; }
        }

        public static string Produto
        {
            get { lock (Sync) return _produto; }
        }

        /// <summary>
        /// Deve ser chamado a partir de Initialize ou de um comando AutoCAD.
        /// Não deve ser chamado pela thread de rede/telemetria.
        /// </summary>
        public static void Capturar()
        {
            string versao = "?";
            string produto = "AutoCAD";
            try { versao = AcadApp.GetSystemVariable("ACADVER") as string ?? "?"; }
            catch { }
            try { produto = AcadApp.GetSystemVariable("PRODUCT") as string ?? "AutoCAD"; }
            catch { }

            lock (Sync)
            {
                _versao = versao;
                _produto = produto;
            }
        }

        /// <summary>
        /// Atualiza o snapshot quando o AutoCAD ainda não tinha contexto
        /// disponível durante o Initialize. Só chamar no thread principal.
        /// </summary>
        public static void CapturarSeNecessario()
        {
            bool precisa;
            lock (Sync) precisa = _versao == "?";
            if (precisa) Capturar();
        }
    }
}
