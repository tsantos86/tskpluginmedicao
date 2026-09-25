using System.Collections.Generic;
using Xunit;

namespace TSKTakeOff.Tests
{
    public class ExcelLayoutTests
    {
        [Fact]
        public void Normaliza_todas_as_linhas_incluindo_as_vazias()
        {
            var porTipo = new Dictionary<TipoLinha, List<int>>
            {
                { TipoLinha.Capitulo, new List<int> { 15, 22 } },
                { TipoLinha.Medicao, new List<int> { 18, 23 } },
                { TipoLinha.Deducao, new List<int> { 19, 20 } },
                // Era esta linha que conservava 123,6 pontos do modelo real.
                { TipoLinha.Vazia, new List<int> { 21 } }
            };

            Assert.Equal(
                new[] { 15, 18, 19, 20, 21, 22, 23 },
                ExcelLayout.LinhasParaNormalizar(porTipo));
        }

        [Fact]
        public void Ignora_linhas_invalidas_e_nao_repete_alturas()
        {
            var porTipo = new Dictionary<TipoLinha, List<int>>
            {
                { TipoLinha.Medicao, new List<int> { 23, 0, 23 } },
                { TipoLinha.Vazia, new List<int> { -1, 21 } }
            };

            Assert.Equal(new[] { 21, 23 }, ExcelLayout.LinhasParaNormalizar(porTipo));
        }
    }
}
