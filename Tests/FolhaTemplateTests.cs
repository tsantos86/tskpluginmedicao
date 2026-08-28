using System.Collections.Generic;
using System.Linq;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Modelo clássico (art/descrição): o que o utilizador escreve na folha tem
    /// de voltar ao DWG. Para isso, a linha de artigo tem de saber de que
    /// medição veio — é o HandleOrigem que o ExcelLiveSync usa antes de limpar.
    /// </summary>
    public class FolhaTemplateTests
    {
        private const string Sep = "\u001f";

        [Fact]
        public void Modelo_classico_transporta_o_handle_na_linha_de_artigo()
        {
            // O ponto 4.3 do relatório: no modelo clássico, o texto que se
            // escrevia na linha de artigo perdia-se na reescrita seguinte. A
            // linha tem de carregar o handle da medição que a gerou, senão não
            // há forma de devolver o texto ao desenho.
            var p = new Parede
            {
                Handle = "H123",
                Servico = "ALVENARIA",
                Artigo = "1.1.1" + Sep + "Alvenaria de tijolo",
                Piso = "PISO 0",
                Alcado = "",
                Bloco = "",
                Comprimento = 4.13,
                Altura = 2.8,
                Espessura = 0.15
            };

            var folhas = FolhaTemplate.Construir(
                new List<Parede> { p },
                new List<MedFachada>(),
                new List<MedItem>(),
                new List<MedContagem>(),
                RegraDesconto.DescontarTudo);

            var alvenarias = folhas.Single(f => f.Nome == FolhaTemplate.FolhaAlvenarias);
            var artigo = alvenarias.Linhas.Single(l => l.Tipo == TipoLinhaTpl.Artigo);

            Assert.Equal("H123", artigo.HandleOrigem);
            Assert.Equal("1.1.1", artigo.Item);
            Assert.Equal("Alvenaria de tijolo", artigo.Descricao);
        }

        [Fact]
        public void Folhas_sem_artigo_nao_inventam_handle()
        {
            // CONTAGENS não tem linhas de artigo de medição: não deve carregar
            // handles que não existem.
            var folhas = FolhaTemplate.Construir(
                new List<Parede>(),
                new List<MedFachada>(),
                new List<MedItem>(),
                new List<MedContagem>
                {
                    new MedContagem { Nome = "P.01", Piso = "PISO 0", Categoria = "PORTAS" }
                },
                RegraDesconto.DescontarTudo);

            var contagens = folhas.Single(f => f.Nome == "CONTAGENS");
            Assert.All(contagens.Linhas, l => Assert.True(
                l.Tipo != TipoLinhaTpl.Artigo || string.IsNullOrEmpty(l.HandleOrigem),
                "linhas de artigo sem medição de origem não podem carregar handle"));
        }
    }
}
