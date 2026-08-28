using System;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Área e desconto de um vão. É a conta que decide quantos metros
    /// quadrados vão à factura, por isso é a primeira a ter rede.
    /// </summary>
    public class VaoTests
    {
        private const double E = 1e-9;   // tolerância: são contas em double

        [Fact]
        public void Area_e_largura_vezes_altura()
        {
            var v = new Vao { Largura = 0.90, Altura = 2.10 };
            Assert.Equal(1.89, v.Area, 9);
        }

        [Fact]
        public void AreaTotal_multiplica_pela_quantidade()
        {
            var v = new Vao { Largura = 0.90, Altura = 2.10, Quantidade = 3 };
            Assert.Equal(5.67, v.AreaTotal, 9);
        }

        // ---- Regras de desconto -----------------------------------------

        [Fact]
        public void DescontarTudo_desconta_a_area_toda_vezes_a_quantidade()
        {
            var v = new Vao { Largura = 1.00, Altura = 2.00, Quantidade = 2 };
            Assert.Equal(4.00, v.Desconto(RegraDesconto.DescontarTudo), 9);
        }

        [Fact]
        public void NaoDescontar_nunca_desconta_nada()
        {
            var v = new Vao { Largura = 3.00, Altura = 2.50, Quantidade = 4 };
            Assert.Equal(0.0, v.Desconto(RegraDesconto.NaoDescontar), 9);
        }

        [Fact]
        public void Sinapi_ignora_o_vao_pequeno()
        {
            // 0,80 × 2,10 = 1,68 m² — abaixo dos 2 m², não desconta nada
            var v = new Vao { Largura = 0.80, Altura = 2.10 };
            Assert.Equal(0.0, v.Desconto(RegraDesconto.Sinapi2m2), 9);
        }

        [Fact]
        public void Sinapi_desconta_so_o_excedente_acima_de_2m2()
        {
            // 1,20 × 2,10 = 2,52 m² — desconta 0,52
            var v = new Vao { Largura = 1.20, Altura = 2.10 };
            Assert.Equal(0.52, v.Desconto(RegraDesconto.Sinapi2m2), 9);
        }

        [Fact]
        public void Sinapi_no_limite_exacto_de_2m2_nao_desconta()
        {
            var v = new Vao { Largura = 1.00, Altura = 2.00 };
            Assert.Equal(0.0, v.Desconto(RegraDesconto.Sinapi2m2), 9);
        }

        [Fact]
        public void Sinapi_aplica_o_excedente_a_cada_unidade_e_nao_ao_conjunto()
        {
            // Três vãos de 2,52 m²: 0,52 cada, 1,56 no total. Somar as áreas
            // primeiro e subtrair 2 m² uma só vez daria 5,56 — errado, e é o
            // engano fácil de cometer ao reescrever isto.
            var v = new Vao { Largura = 1.20, Altura = 2.10, Quantidade = 3 };
            Assert.Equal(1.56, v.Desconto(RegraDesconto.Sinapi2m2), 9);
        }

        // ---- Pré-aro ------------------------------------------------------

        [Fact]
        public void PreAro_desligado_da_sempre_zero()
        {
            var v = new Vao { Largura = 0.90, Altura = 2.10, PreAro = false, Espessura = 0.15 };
            Assert.Equal(0.0, v.PreAroMetros, 9);
            Assert.Equal(0.0, v.PreAroArea, 9);
            Assert.Equal(0, v.PreAroUnidades);
        }

        [Fact]
        public void PreAro_de_porta_e_dois_lados_mais_a_verga()
        {
            // A porta assenta no chão: não leva aro em baixo. 2×2,10 + 0,90
            var v = new Vao
            {
                Largura = 0.90, Altura = 2.10,
                PreAro = true, Tipo = TipoVao.Porta
            };
            Assert.Equal(5.10, v.PreAroMetros, 9);
        }

        [Fact]
        public void PreAro_de_janela_e_o_perimetro_todo()
        {
            // A janela leva aro a toda a volta: 2×(1,20 + 1,10)
            var v = new Vao
            {
                Largura = 1.20, Altura = 1.10,
                PreAro = true, Tipo = TipoVao.Janela
            };
            Assert.Equal(4.60, v.PreAroMetros, 9);
        }

        [Fact]
        public void PreAro_multiplica_pela_quantidade()
        {
            var v = new Vao
            {
                Largura = 0.90, Altura = 2.10, Quantidade = 4,
                PreAro = true, Tipo = TipoVao.Porta
            };
            Assert.Equal(20.40, v.PreAroMetros, 9);
            Assert.Equal(4, v.PreAroUnidades);
        }

        [Fact]
        public void PreAroArea_e_o_desenvolvimento_vezes_a_espessura()
        {
            var v = new Vao
            {
                Largura = 0.90, Altura = 2.10,
                PreAro = true, Tipo = TipoVao.Porta, Espessura = 0.15
            };
            Assert.Equal(5.10 * 0.15, v.PreAroArea, 9);
        }
    }
}
