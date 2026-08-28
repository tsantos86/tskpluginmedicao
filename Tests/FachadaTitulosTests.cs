using System.Linq;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Títulos CAP/ART da aba Materiais (fix 5.5): a MedFachada tem de suportar
    /// os dois níveis em simultâneo, como a Parede da Alvenaria já fazia.
    /// </summary>
    public class FachadaTitulosTests
    {
        [Fact]
        public void Fachada_suporta_capitulo_e_artigo_em_simultaneo()
        {
            var f = new MedFachada();

            f.AlternarMarca("ART");
            f.AlternarMarca("CAP");

            // Os dois coexistem, com o capítulo primeiro.
            Assert.Equal(new[] { "CAP", "ART" }, f.Marcas.ToArray());
        }

        [Fact]
        public void Tirar_um_nivel_nao_apaga_o_outro()
        {
            var f = new MedFachada();
            f.AlternarMarca("ART");
            f.AlternarMarca("CAP");

            f.AlternarMarca("CAP");

            Assert.Equal(new[] { "ART" }, f.Marcas.ToArray());
        }

        [Fact]
        public void O_texto_fica_na_posicao_certa_com_dois_titulos()
        {
            var f = new MedFachada();
            f.AlternarMarca("ART");
            f.AlternarMarca("CAP");

            f.DefinirTextoDaMarca(1, "1.1.1\u001fAlvenaria de tijolo");

            Assert.Equal("", f.TextoDaMarca(0));   // o capítulo continua vazio
            Assert.Equal("1.1.1\u001fAlvenaria de tijolo", f.TextoDaMarca(1));
        }
    }
}
