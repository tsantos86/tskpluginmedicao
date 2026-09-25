using Xunit;

namespace TSKTakeOff.Tests
{
    public class FluxoAtualizacaoTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, true, true)]
        public void Le_o_desenho_para_o_painel_ou_para_o_excel(
            bool painel, bool excel, bool esperado)
        {
            Assert.Equal(esperado,
                FluxoAtualizacao.DeveLerDesenho(painel, excel));
        }

        [Fact]
        public void Excel_nao_depende_de_o_painel_ter_sido_aberto()
        {
            Assert.True(FluxoAtualizacao.DeveLerDesenho(
                painelDisponivel: false, excelConectado: true));
        }
    }
}
