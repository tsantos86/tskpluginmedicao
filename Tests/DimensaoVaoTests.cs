using System;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Leitura das cotas escritas no desenho. Cada gabinete cota à sua
    /// maneira, e enganar-se na unidade é o erro caro: uma porta lida em
    /// metros quando estava em milímetros mete 800 m² numa parede.
    /// </summary>
    public class DimensaoVaoTests
    {
        private static (double l, double a, string u) Ler(string texto)
        {
            var m = DimensaoVao.RxDim.Match(texto);
            Assert.True(m.Success, "não encontrou cotas em: " + texto);
            Assert.True(DimensaoVao.Interpretar(
                m.Groups["a"].Value, m.Groups["b"].Value,
                out double l, out double a, out string u),
                "não conseguiu interpretar: " + texto);
            return (l, a, u);
        }

        // ---- Unidades ----------------------------------------------------

        [Fact]
        public void Milimetros_sem_separador_decimal()
        {
            var r = Ler("VE.02 1190x2350");
            Assert.Equal(1.19, r.l, 3);
            Assert.Equal(2.35, r.a, 3);
            Assert.Equal("mm", r.u);
        }

        [Fact]
        public void Centimetros_quando_milimetros_dariam_um_vao_absurdo()
        {
            // 200x160 em mm dava 0,20 × 0,16 — pequeno de mais para ser vão.
            var r = Ler("PC.04 200X160");
            Assert.Equal(2.00, r.l, 3);
            Assert.Equal(1.60, r.a, 3);
            Assert.Equal("cm", r.u);
        }

        [Fact]
        public void Metros_quando_ha_virgula()
        {
            var r = Ler("AL205 2,67x2,62 m");
            Assert.Equal(2.67, r.l, 3);
            Assert.Equal(2.62, r.a, 3);
            Assert.Equal("m", r.u);
        }

        [Fact]
        public void Metros_quando_ha_ponto()
        {
            var r = Ler("0.90 x 2.10");
            Assert.Equal(0.90, r.l, 3);
            Assert.Equal(2.10, r.a, 3);
            Assert.Equal("m", r.u);
        }

        [Fact]
        public void Milimetros_da_porta_corrente()
        {
            var r = Ler("P1 800x2100");
            Assert.Equal(0.80, r.l, 3);
            Assert.Equal(2.10, r.a, 3);
            Assert.Equal("mm", r.u);
        }

        [Fact]
        public void Aceita_o_x_maiusculo_o_minusculo_e_o_sinal_de_vezes()
        {
            foreach (var sep in new[] { "x", "X", "×" })
            {
                var r = Ler("V1 900" + sep + "2100");
                Assert.Equal(0.90, r.l, 3);
                Assert.Equal(2.10, r.a, 3);
            }
        }

        [Fact]
        public void Espacos_a_volta_do_separador_nao_incomodam()
        {
            var r = Ler("J2 1200  x  1100");
            Assert.Equal(1.20, r.l, 3);
            Assert.Equal(1.10, r.a, 3);
        }

        // ---- O que tem de ser recusado -----------------------------------

        [Fact]
        public void Recusa_o_que_nao_cabe_num_vao_em_nenhuma_unidade()
        {
            // 12x14: em m é grande de mais para a altura, em cm e mm é
            // pequeno de mais. Não há leitura plausível — melhor não inventar.
            Assert.False(DimensaoVao.Interpretar("12", "14",
                out _, out _, out _));
        }

        [Fact]
        public void Recusa_dimensoes_absurdas()
        {
            Assert.False(DimensaoVao.Interpretar("9999", "9999", out _, out _, out _));
        }

        [Fact]
        public void Recusa_zero_e_negativos()
        {
            Assert.False(DimensaoVao.Interpretar("0", "2100", out _, out _, out _));
            Assert.False(DimensaoVao.Interpretar("800", "0", out _, out _, out _));
        }

        [Fact]
        public void Recusa_texto_que_nao_e_numero()
        {
            Assert.False(DimensaoVao.Interpretar("largura", "altura", out _, out _, out _));
            Assert.False(DimensaoVao.Interpretar(null, "2100", out _, out _, out _));
        }

        [Fact]
        public void Uma_escala_1_50_nao_e_lida_como_vao()
        {
            // "ESC 1:50" não tem "x", mas há quem escreva "1x50". Em nenhuma
            // unidade isso dá um vão plausível.
            Assert.False(DimensaoVao.Interpretar("1", "50", out _, out _, out _));
        }

        // ---- Designação ---------------------------------------------------

        [Theory]
        [InlineData("VE.02 1190x2350", "VE.02")]
        [InlineData("PC.04 200X160", "PC.04")]
        [InlineData("AL205 2,67x2,62 m", "AL205")]
        [InlineData("J2 900x1100", "J2")]
        public void Le_a_designacao_do_rotulo(string texto, string esperado)
        {
            Assert.Equal(esperado, DimensaoVao.Designacao(texto));
        }

        [Fact]
        public void Sem_designacao_devolve_null_em_vez_de_inventar()
        {
            Assert.Null(DimensaoVao.Designacao("900x2100"));
            Assert.Null(DimensaoVao.Designacao(""));
            Assert.Null(DimensaoVao.Designacao(null));
        }

        // ---- Números ------------------------------------------------------

        [Theory]
        [InlineData("2,67", 2.67)]
        [InlineData("2.67", 2.67)]
        [InlineData("1190", 1190.0)]
        [InlineData("lixo", 0.0)]
        [InlineData(null, 0.0)]
        public void ParseNum_aceita_virgula_e_ponto(string entrada, double esperado)
        {
            Assert.Equal(esperado, DimensaoVao.ParseNum(entrada), 6);
        }

        [Fact]
        public void Encontra_duas_cotas_na_mesma_linha()
        {
            // Rótulo com tosco e aro: "1190x2350 (1200x2400)". As duas são
            // encontradas; é o detector que decide qual fica marcada.
            var ms = DimensaoVao.RxDim.Matches("VE.02 1190x2350 (1200x2400)");
            Assert.Equal(2, ms.Count);
        }
    }
}
