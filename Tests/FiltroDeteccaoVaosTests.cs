using Xunit;

namespace TSKTakeOff.Tests
{
    public class FiltroDeteccaoVaosTests
    {
        [Theory]
        [InlineData("MED_ALVENARIA", true)]
        [InlineData("med_parede", true)]
        [InlineData("MED_", true)]
        [InlineData("PORTAS", false)]
        [InlineData("MEDIDAS", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Reconhece_apenas_layers_geradas_pelo_plugin(string layer, bool esperado)
        {
            Assert.Equal(esperado,
                FiltroDeteccaoVaos.EhLayerGeradaPeloPlugin(layer, "MED_"));
        }

        [Fact]
        public void Prefixo_vazio_nunca_esconde_uma_layer()
        {
            Assert.False(FiltroDeteccaoVaos.EhLayerGeradaPeloPlugin("PORTAS", ""));
        }
    }
}
