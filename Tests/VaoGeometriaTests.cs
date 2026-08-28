using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Vãos lidos da GEOMETRIA — o rectângulo ou a polyline que É o vão, em
    /// vez do texto que o rotula.
    ///
    /// O que aqui se protege é a escolha de qual das duas dimensões é a
    /// altura. Num alçado o desenho tem as duas; numa planta a segunda é a
    /// espessura da parede, e tomá-la por altura dá um desconto seis vezes
    /// menor do que o real — num número com todo o ar de estar certo.
    /// </summary>
    public class VaoGeometriaTests
    {
        const double PAINEL = 2.10;   // Config.AlturaVaoPadrao

        private static (double l, double a) Planta(
            double dx, double dy, double lado1 = 0, double lado2 = 0)
        {
            Assert.True(DimensaoVao.DeGeometria(dx, dy, lado1, lado2, false, PAINEL,
                out double l, out double a), "recusou o que devia aceitar");
            return (l, a);
        }

        private static (double l, double a) Alcado(double dx, double dy)
        {
            Assert.True(DimensaoVao.DeGeometria(dx, dy, 0, 0, true, 0,
                out double l, out double a), "recusou o que devia aceitar");
            return (l, a);
        }

        // ---- Alçado: as duas cotas estão no desenho -----------------------

        [Fact]
        public void Alcado_porta_le_largura_e_altura_do_desenho()
        {
            var r = Alcado(0.90, 2.10);
            Assert.Equal(0.90, r.l, 3);
            Assert.Equal(2.10, r.a, 3);
        }

        [Fact]
        public void Alcado_janela_mais_alta_que_larga_nao_troca_os_lados()
        {
            // A ratoeira do "maior = largura": aqui a altura é que é maior.
            var r = Alcado(1.19, 2.35);
            Assert.Equal(1.19, r.l, 3);
            Assert.Equal(2.35, r.a, 3);
        }

        [Fact]
        public void Alcado_janela_mais_larga_que_alta()
        {
            var r = Alcado(2.40, 1.10);
            Assert.Equal(2.40, r.l, 3);
            Assert.Equal(1.10, r.a, 3);
        }

        // ---- Planta: a altura não está no desenho -------------------------

        [Fact]
        public void Planta_le_a_largura_e_a_altura_vem_do_painel()
        {
            // Vão de 0,90 numa parede de 0,20: a altura NÃO é 0,20.
            var r = Planta(0.90, 0.20, 0.90, 0.20);
            Assert.Equal(0.90, r.l, 3);
            Assert.Equal(PAINEL, r.a, 3);
        }

        [Fact]
        public void Planta_usa_o_lado_e_nao_a_caixa_quando_o_vao_esta_rodado()
        {
            // Numa parede a 45°, a caixa envolvente de um vão de 1,20 mede
            // cerca de 0,99 em cada eixo. Ler a caixa dava uma largura errada;
            // o lado é imune à rotação.
            var r = Planta(0.99, 0.99, 1.20, 0.20);
            Assert.Equal(1.20, r.l, 3);
            Assert.Equal(PAINEL, r.a, 3);
        }

        [Fact]
        public void Planta_sem_rectangulo_cai_na_maior_extensao()
        {
            // Polyline solta ou hachura: não há lados, usa-se a caixa.
            var r = Planta(1.60, 0.20);
            Assert.Equal(1.60, r.l, 3);
            Assert.Equal(PAINEL, r.a, 3);
        }

        [Fact]
        public void Planta_parede_grossa_nao_confunde_espessura_com_largura()
        {
            // Parede de 0,40: o maior lado continua a ser a largura do vão.
            var r = Planta(0.80, 0.40, 0.80, 0.40);
            Assert.Equal(0.80, r.l, 3);
            Assert.Equal(PAINEL, r.a, 3);
        }

        // ---- Recusas: melhor nada do que um número inventado --------------

        [Fact]
        public void Recusa_geometria_pequena_de_mais_para_um_vao()
        {
            // Um quadradinho de 0,10 é um símbolo, não um vão.
            Assert.False(DimensaoVao.DeGeometria(0.10, 0.10, 0.10, 0.10, false, PAINEL,
                out _, out _));
        }

        [Fact]
        public void Recusa_geometria_grande_de_mais()
        {
            // A parede inteira seleccionada por engano.
            Assert.False(DimensaoVao.DeGeometria(25.0, 0.20, 25.0, 0.20, false, PAINEL,
                out _, out _));
        }

        [Fact]
        public void Recusa_alcado_com_altura_implausivel()
        {
            // Em alçado, um rectângulo de 0,20 de alto não é um vão.
            Assert.False(DimensaoVao.DeGeometria(0.90, 0.20, 0, 0, true, 0,
                out _, out _));
        }

        [Fact]
        public void Recusa_entidade_sem_dimensao()
        {
            Assert.False(DimensaoVao.DeGeometria(0, 0, 0, 0, false, PAINEL, out _, out _));
        }

        [Fact]
        public void Planta_sem_altura_de_painel_e_recusada()
        {
            // Sem altura não há vão: mais vale recusar do que gravar um
            // desconto de zero que ninguém vê.
            Assert.False(DimensaoVao.DeGeometria(0.90, 0.20, 0.90, 0.20, false, 0,
                out _, out _));
        }

        // ---- Limites exactos ---------------------------------------------

        [Fact]
        public void Aceita_nos_limites_e_recusa_logo_a_seguir()
        {
            Assert.True(DimensaoVao.DeGeometria(DimensaoVao.LargMin, 0.20,
                DimensaoVao.LargMin, 0.20, false, PAINEL, out _, out _));
            Assert.False(DimensaoVao.DeGeometria(DimensaoVao.LargMin - 0.01, 0.20,
                DimensaoVao.LargMin - 0.01, 0.20, false, PAINEL, out _, out _));

            Assert.True(DimensaoVao.DeGeometria(1.0, DimensaoVao.AltMax,
                0, 0, true, 0, out _, out _));
            Assert.False(DimensaoVao.DeGeometria(1.0, DimensaoVao.AltMax + 0.01,
                0, 0, true, 0, out _, out _));
        }

        [Fact]
        public void Arredonda_ao_milimetro()
        {
            var r = Alcado(0.9004999, 2.1000001);
            Assert.Equal(0.900, r.l, 3);
            Assert.Equal(2.100, r.a, 3);
        }
    }
}
