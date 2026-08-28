using System.Collections.Generic;
using TSKTakeOff;
using Xunit;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// A regra que a barra da paleta quebrava: NUNCA somar unidades diferentes.
    ///
    /// A paleta somava a área líquida de todas as medições e escrevia "m²" ao
    /// lado. Só que uma camada — área em planta × espessura, o TSKAREA — fatura
    /// m³ e entrava na mesma conta. O número que saía não era m² nem m³, e
    /// tinha ar de estar bem, que é o pior dos casos: ninguém desconfia de um
    /// total.
    /// </summary>
    public class TotaisMedicaoTests
    {
        private static Parede Parede(double comp, double alt)
        {
            return new Parede { Comprimento = comp, Altura = alt };
        }

        /// <summary>Camada: a área em planta entra na Largura e a espessura na
        /// Altura — a conta é a mesma, o significado dos factores é que muda.</summary>
        private static Parede Camada(double area, double espessura)
        {
            return new Parede
            {
                Comprimento = 1.0,
                Largura = area,
                Altura = espessura,
                AreaVezesAltura = true,
            };
        }

        [Fact]
        public void Parede_fatura_m2()
        {
            Assert.Equal("m2", Parede(5.20, 2.80).Unidade);
        }

        [Fact]
        public void Camada_fatura_m3()
        {
            Assert.Equal("m3", Camada(30.0, 0.08).Unidade);
        }

        [Fact]
        public void So_paredes_da_um_total_em_m2()
        {
            var t = TotaisMedicao.PorUnidade(
                new List<Parede> { Parede(5.0, 2.0), Parede(3.0, 2.0) },
                RegraDesconto.DescontarTudo);

            Assert.Single(t);
            Assert.Equal("m2", t[0].Key);
            Assert.Equal(16.0, t[0].Value, 3);   // 10 + 6
        }

        [Fact]
        public void Paredes_e_camadas_NAO_se_somam()
        {
            var t = TotaisMedicao.PorUnidade(
                new List<Parede> { Parede(5.0, 2.0), Camada(30.0, 0.08) },
                RegraDesconto.DescontarTudo);

            Assert.Equal(2, t.Count);

            Assert.Equal("m2", t[0].Key);
            Assert.Equal(10.0, t[0].Value, 3);

            Assert.Equal("m3", t[1].Key);
            Assert.Equal(2.4, t[1].Value, 3);    // 30 × 0,08

            // O que a paleta escrevia antes: 12,40 rotulado "m²".
            Assert.NotEqual(12.4, t[0].Value, 3);
        }

        [Fact]
        public void A_ordem_e_a_de_chegada_nao_alfabetica()
        {
            // Quem mede alvenaria vê m² primeiro. Por nome, o m3 vinha à frente.
            var t = TotaisMedicao.PorUnidade(
                new List<Parede> { Parede(2.0, 2.0), Camada(10.0, 0.1), Parede(1.0, 1.0) },
                RegraDesconto.DescontarTudo);

            Assert.Equal(2, t.Count);
            Assert.Equal("m2", t[0].Key);
            Assert.Equal(5.0, t[0].Value, 3);    // 4 + 1, as duas paredes juntas
            Assert.Equal("m3", t[1].Key);
        }

        [Fact]
        public void Lista_vazia_ou_nula_nao_rebenta()
        {
            Assert.Empty(TotaisMedicao.PorUnidade(new List<Parede>(), RegraDesconto.DescontarTudo));
            Assert.Empty(TotaisMedicao.PorUnidade(null, RegraDesconto.DescontarTudo));
        }

        [Fact]
        public void Os_vaos_descontam_antes_de_entrar_no_total()
        {
            var p = Parede(5.0, 2.0);                       // 10 m²
            p.Vaos.Add(new Vao { Largura = 1.0, Altura = 2.0, Quantidade = 1 });

            var t = TotaisMedicao.PorUnidade(
                new List<Parede> { p }, RegraDesconto.DescontarTudo);

            Assert.Single(t);
            Assert.Equal(8.0, t[0].Value, 3);               // 10 − 2
        }
    }
}
