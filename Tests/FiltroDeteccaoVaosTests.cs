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
        public void Reconhece_layers_com_o_prefixo_configurado(string layer, bool esperado)
        {
            Assert.Equal(esperado,
                FiltroDeteccaoVaos.EhLayerGeradaPeloPlugin(layer, "MED_"));
        }

        [Fact]
        public void Prefixo_vazio_nunca_esconde_uma_layer()
        {
            Assert.False(FiltroDeteccaoVaos.EhLayerGeradaPeloPlugin("PORTAS", ""));
        }

        [Theory]
        [InlineData("MED_ALVENARIA", "ALVENARIA\\P4,58 × 2,80 = 12,82 m²", true)]
        [InlineData("MED_ALVENARIA", "ALVENARIA 4.58 x 2.80 = 12.82 m2", true)]
        [InlineData("MED_VAOS", "VE.02", false)]
        [InlineData("MED_COTAS", "VE.02 2.00x2.10", false)]
        [InlineData("PORTAS", "4.58 × 2.80 = 12.82 m²", false)]
        public void So_a_etiqueta_resumo_do_tsk_e_excluida(string layer,
            string texto, bool esperado)
        {
            Assert.Equal(esperado,
                FiltroDeteccaoVaos.EhEtiquetaGeradaPeloPlugin(
                    layer, "MED_", texto));
        }

        [Fact]
        public void Mesma_cota_no_atributo_e_no_bloco_conta_uma_vez()
        {
            Assert.True(FiltroDeteccaoVaos.SaoLeiturasDuplicadas(
                "VE.04.02", 2.00, 2.10, "ve.04.02", 2.001, 2.10, 0.08));
        }

        [Fact]
        public void Vaos_iguais_em_posicoes_diferentes_nao_se_juntam()
        {
            Assert.False(FiltroDeteccaoVaos.SaoLeiturasDuplicadas(
                "P01", 0.90, 2.10, "P01", 0.90, 2.10, 0.16));
        }
    }
}
