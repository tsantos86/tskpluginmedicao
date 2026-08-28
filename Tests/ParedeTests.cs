using System;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Área bruta, área líquida e volume de uma parede — incluindo o caso que
    /// mais confusão dá na obra: a parede que o desenho traz partida em troços
    /// por causa dos vãos.
    /// </summary>
    public class ParedeTests
    {
        private static Parede Simples(double comp, double alt, double esp = 0.15)
        {
            return new Parede { Comprimento = comp, Altura = alt, Espessura = esp };
        }

        [Fact]
        public void AreaBruta_e_comprimento_vezes_altura()
        {
            var p = Simples(5.00, 2.80);
            Assert.Equal(14.00, p.AreaBruta, 9);
        }

        [Fact]
        public void AreaBruta_com_largura_preenchida_entra_no_produto()
        {
            // Largura = 0 significa "não usar", e não "multiplicar por zero":
            // sem esta distinção uma parede sem largura daria área nula.
            var p = Simples(5.00, 2.80);
            p.Largura = 2.0;
            Assert.Equal(28.00, p.AreaBruta, 9);
        }

        [Fact]
        public void AreaVezesAltura_da_o_volume_da_camada()
        {
            // Uma betonilha de enchimento: 86,392 m² de pavimento × 0,12 m de
            // espessura = 10,367 m³. O Comprimento guarda a ÁREA e a Altura a
            // espessura — a conta é a de sempre, o que muda é o significado.
            var p = new Parede
            {
                Comprimento = 86.392,
                Altura = 0.12,
                Espessura = 0,
                AreaVezesAltura = true
            };

            Assert.Equal(10.36704, p.AreaBruta, 9);
            // Espessura a zero: sem isto a coluna Volume mostrava
            // área × altura × espessura, que não é grandeza nenhuma.
            Assert.Equal(0.0, p.Volume(RegraDesconto.DescontarTudo), 9);
        }

        [Fact]
        public void AreaLiquida_desconta_os_vaos()
        {
            var p = Simples(5.00, 2.80);
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10 });   // 1,89
            Assert.Equal(14.00 - 1.89, p.AreaLiquida(RegraDesconto.DescontarTudo), 9);
        }

        [Fact]
        public void AreaLiquida_nunca_desce_abaixo_de_zero()
        {
            // Vãos maiores do que a parede: erro de introdução, e o resultado
            // tem de ser 0 e não um número negativo a somar à folha.
            var p = Simples(1.00, 2.00);
            p.Vaos.Add(new Vao { Largura = 3.00, Altura = 2.50 });
            Assert.Equal(0.0, p.AreaLiquida(RegraDesconto.DescontarTudo), 9);
        }

        [Fact]
        public void AreaLiquida_com_varios_vaos_soma_os_descontos()
        {
            var p = Simples(10.00, 2.80);                            // 28,00
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10 });   // 1,89
            p.Vaos.Add(new Vao { Largura = 1.20, Altura = 1.10, Quantidade = 2 }); // 2,64
            Assert.Equal(28.00 - 1.89 - 2.64, p.AreaLiquida(RegraDesconto.DescontarTudo), 9);
        }

        [Fact]
        public void Volume_e_area_liquida_vezes_espessura()
        {
            var p = Simples(5.00, 2.80, 0.20);
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10 });
            double esperado = (14.00 - 1.89) * 0.20;
            Assert.Equal(esperado, p.Volume(RegraDesconto.DescontarTudo), 9);
        }

        [Fact]
        public void NaoDescontar_deixa_a_area_liquida_igual_a_bruta()
        {
            var p = Simples(5.00, 2.80);
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10 });
            Assert.Equal(p.AreaBruta, p.AreaLiquida(RegraDesconto.NaoDescontar), 9);
        }

        // ---- Geometria interrompida pelos vãos ---------------------------

        [Fact]
        public void GeometriaSemVaos_repoe_a_largura_dos_vaos_no_comprimento()
        {
            // A polyline mede só os troços cheios (4,10 m). Com a marca ligada,
            // a largura da porta volta ao comprimento e a parede volta a ter
            // os 5,00 m que tem na realidade.
            var p = Simples(4.10, 2.80);
            p.GeometriaSemVaos = true;
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10 });

            Assert.Equal(5.00, p.ComprimentoTotal, 9);
            Assert.Equal(14.00, p.AreaBruta, 9);
        }

        [Fact]
        public void Sem_a_marca_a_largura_dos_vaos_nao_e_reposta()
        {
            var p = Simples(4.10, 2.80);
            p.GeometriaSemVaos = false;
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10 });

            Assert.Equal(4.10, p.ComprimentoTotal, 9);
        }

        [Fact]
        public void ComprimentoExtra_soma_se_sempre()
        {
            var p = Simples(4.00, 2.80);
            p.ComprimentoExtra = 1.50;
            Assert.Equal(5.50, p.ComprimentoTotal, 9);
        }

        [Fact]
        public void LarguraDosVaos_conta_a_quantidade()
        {
            var p = Simples(10.00, 2.80);
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10, Quantidade = 2 });
            p.Vaos.Add(new Vao { Largura = 1.20, Altura = 1.10 });
            Assert.Equal(3.00, p.LarguraDosVaos, 9);
        }

        // ---- Pré-aro somado à parede -------------------------------------

        [Fact]
        public void PreAro_da_parede_soma_o_de_todos_os_vaos()
        {
            var p = Simples(10.00, 2.80, 0.15);
            p.Vaos.Add(new Vao
            {
                Largura = 0.90, Altura = 2.10, PreAro = true,
                Tipo = TipoVao.Porta, Espessura = 0.15
            });                                                  // 5,10 ml
            p.Vaos.Add(new Vao
            {
                Largura = 1.20, Altura = 1.10, PreAro = true, Quantidade = 2,
                Tipo = TipoVao.Janela, Espessura = 0.15
            });                                                  // 9,20 ml

            Assert.Equal(3, p.PreAroUn);
            Assert.Equal(14.30, p.PreAroMl, 9);
            Assert.Equal(14.30 * 0.15, p.PreAroM2, 9);
        }
    }
}
