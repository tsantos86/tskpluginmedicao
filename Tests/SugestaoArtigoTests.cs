using System.Collections.Generic;
using TSKTakeOff;
using Xunit;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// A ordenação de candidatos a artigo por relevância ao contexto — não é
    /// IA, é pontuação por sobreposição de palavras (ver SugestaoArtigo.cs).
    /// </summary>
    public class SugestaoArtigoTests
    {
        private static SugestaoArtigo.Candidato Art(string codigo, string designacao)
        {
            return new SugestaoArtigo.Candidato(codigo, designacao);
        }

        [Fact]
        public void Sem_contexto_a_pontuacao_e_zero()
        {
            Assert.Equal(0, SugestaoArtigo.Pontuacao(null, Art("1.1.1", "Parede em alvenaria")));
            Assert.Equal(0, SugestaoArtigo.Pontuacao("", Art("1.1.1", "Parede em alvenaria")));
            Assert.Equal(0, SugestaoArtigo.Pontuacao("   ", Art("1.1.1", "Parede em alvenaria")));
        }

        [Fact]
        public void Palavra_do_contexto_que_aparece_na_designacao_pontua()
        {
            var artigo = Art("1.1.1", "Parede em alvenaria de tijolo");
            int pontos = SugestaoArtigo.Pontuacao("ALVENARIA", artigo);
            Assert.Equal("ALVENARIA".Length, pontos);
        }

        [Fact]
        public void Palavras_curtas_nao_contam()
        {
            // "de", "em" têm menos de 3 letras — não podem inflacionar a
            // pontuação de artigos que não têm nada a ver com o contexto.
            var artigo = Art("2.1.1", "Rodapé cerâmico");
            int pontos = SugestaoArtigo.Pontuacao("de em um", artigo);
            Assert.Equal(0, pontos);
        }

        [Fact]
        public void Varias_palavras_batidas_somam()
        {
            var artigo = Art("1.1.1", "Parede em alvenaria de tijolo furado");
            int pontos = SugestaoArtigo.Pontuacao("ALVENARIA TIJOLO", artigo);
            Assert.Equal("ALVENARIA".Length + "TIJOLO".Length, pontos);
        }

        [Fact]
        public void O_codigo_tambem_conta_para_a_pontuacao()
        {
            var artigo = Art("ALVENARIA.01", "Parede simples");
            int pontos = SugestaoArtigo.Pontuacao("ALVENARIA", artigo);
            Assert.True(pontos > 0);
        }

        [Fact]
        public void Ordenar_poe_a_maior_pontuacao_primeiro()
        {
            var lista = new List<SugestaoArtigo.Candidato>
            {
                Art("3.1.1", "Rodapé cerâmico"),
                Art("1.1.1", "Parede em alvenaria de tijolo"),
                Art("2.1.1", "Porta interior"),
            };

            var ordenada = SugestaoArtigo.Ordenar("ALVENARIA", lista, c => c);

            Assert.Equal("1.1.1", ordenada[0].Codigo);
        }

        [Fact]
        public void Empates_mantem_a_ordem_de_entrada_estavel()
        {
            // Sem nenhuma palavra do contexto a bater em nenhum candidato,
            // todos ficam com pontuação 0 — e a ordem do articulado (a ordem
            // de entrada) tem de sobreviver, não uma ordem qualquer do sort.
            var lista = new List<SugestaoArtigo.Candidato>
            {
                Art("3.1.1", "Rodapé cerâmico"),
                Art("1.1.1", "Parede em alvenaria"),
                Art("2.1.1", "Porta interior"),
            };

            var ordenada = SugestaoArtigo.Ordenar("NADA-A-VER-COM-NENHUM", lista, c => c);

            Assert.Equal("3.1.1", ordenada[0].Codigo);
            Assert.Equal("1.1.1", ordenada[1].Codigo);
            Assert.Equal("2.1.1", ordenada[2].Codigo);
        }

        [Fact]
        public void Sem_contexto_Ordenar_devolve_a_lista_tal_como_veio()
        {
            var lista = new List<SugestaoArtigo.Candidato>
            {
                Art("3.1.1", "Rodapé cerâmico"),
                Art("1.1.1", "Parede em alvenaria"),
            };

            var ordenada = SugestaoArtigo.Ordenar(null, lista, c => c);

            Assert.Equal("3.1.1", ordenada[0].Codigo);
            Assert.Equal("1.1.1", ordenada[1].Codigo);
        }

        [Fact]
        public void Ordenar_com_lista_nula_devolve_lista_vazia()
        {
            var ordenada = SugestaoArtigo.Ordenar("ALVENARIA",
                (List<SugestaoArtigo.Candidato>)null, c => c);
            Assert.Empty(ordenada);
        }
    }
}
