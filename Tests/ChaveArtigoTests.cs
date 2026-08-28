using System;
using System.Collections.Generic;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// A forma normalizada de uma chave de artigo.
    ///
    /// Ela passou a ser a chave do índice que o MapaQuantidades.Procurar usa,
    /// em vez de percorrer o articulado inteiro a cada consulta. Isso só é
    /// legítimo enquanto "duas chaves normalizam igual" quiser dizer
    /// exactamente o mesmo que "Compativel diz que sim". Se as duas regras
    /// divergirem, uma medição deixa de encontrar o seu artigo, sai órfã no
    /// fim da folha, e não há nada no ecrã que o explique.
    ///
    /// É esse contrato que estes testes prendem.
    /// </summary>
    public class ChaveArtigoTests
    {
        // O separador de producao, nao uma copia: se ele mudar, os testes
        // mudam com ele em vez de passarem a testar outra coisa.
        const string SEP = ChaveArtigo.Sep;

        private static string Chave(string codigo, string designacao)
        {
            return codigo + SEP + designacao;
        }

        // ---- o contrato: normalizar == comparar ---------------------------

        [Fact]
        public void Normalizar_igual_quer_dizer_compativel()
        {
            var chaves = new List<string>
            {
                Chave("2.6", "Alvenaria de tijolo 15"),
                Chave("2.6", "Alvenaria de tijolo 20"),
                Chave("2.7", "Alvenaria de tijolo 15"),
                Chave("2.6", "alvenaria de TIJOLO 15"),      // so muda a caixa
                Chave("Sem Ref", "Trabalhos diversos"),
                Chave("10.1.1.306A", "Reboco"),
                "sem separador nenhum",
                Chave("3.1", new string('x', 300)),           // passa dos 255
                Chave("3.1", new string('x', 260)),           // idem, corta igual
                Chave("", ""),
            };

            foreach (var a in chaves)
                foreach (var b in chaves)
                {
                    bool compat = ChaveArtigo.Compativel(a, b);
                    bool mesmaForma = string.Equals(
                        ChaveArtigo.Normalizada(a), ChaveArtigo.Normalizada(b),
                        StringComparison.OrdinalIgnoreCase);

                    Assert.True(compat == mesmaForma,
                        "divergiram para:\n  a = " + a.Replace(SEP, "<SEP>") +
                        "\n  b = " + b.Replace(SEP, "<SEP>") +
                        "\n  Compativel=" + compat + "  mesmaForma=" + mesmaForma);
                }
        }

        [Fact]
        public void Duas_designacoes_que_so_diferem_depois_dos_255_normalizam_igual()
        {
            // A XData nao guarda alem dos 255: as duas sao indistinguiveis, e
            // e por isso que o mapa as trata como ambiguas em vez de escolher.
            string a = Chave("3.1", new string('a', 300));
            string b = Chave("3.1", new string('a', 260));

            Assert.True(ChaveArtigo.Compativel(a, b));
            Assert.Equal(ChaveArtigo.Normalizada(a), ChaveArtigo.Normalizada(b));
        }

        [Fact]
        public void Codigo_diferente_nunca_normaliza_igual()
        {
            string a = Chave("7.4", "Betao de limpeza");
            string b = Chave("7.5", "Betao de limpeza");

            Assert.False(ChaveArtigo.Compativel(a, b));
            Assert.NotEqual(ChaveArtigo.Normalizada(a), ChaveArtigo.Normalizada(b));
        }

        [Fact]
        public void Designacao_diferente_nunca_normaliza_igual()
        {
            // O codigo repete-se nos mapas reais; e a designacao que separa.
            string a = Chave("7.4", "Betao de limpeza");
            string b = Chave("7.4", "Betao armado");

            Assert.False(ChaveArtigo.Compativel(a, b));
            Assert.NotEqual(ChaveArtigo.Normalizada(a), ChaveArtigo.Normalizada(b));
        }

        // ---- a fronteira do separador ------------------------------------

        [Fact]
        public void A_juncao_nao_desloca_a_fronteira()
        {
            // A armadilha de juntar duas partes numa chave so: "AB"+"C" dar o
            // mesmo que "A"+"BC". Aqui nao pode acontecer, porque o Codigo()
            // devolve o que esta ANTES do separador e nunca o contem.
            string a = Chave("2.6", "1 Alvenaria");
            string b = Chave("2.61", " Alvenaria");

            Assert.False(ChaveArtigo.Compativel(a, b));
            Assert.NotEqual(ChaveArtigo.Normalizada(a), ChaveArtigo.Normalizada(b));
        }

        [Fact]
        public void Chave_sem_separador_vale_como_codigo_inteiro()
        {
            // Desenhos antigos guardavam so o codigo.
            Assert.Equal("2.6", ChaveArtigo.Codigo("2.6"));
            Assert.True(ChaveArtigo.Compativel("2.6", "2.6"));
            Assert.Equal(ChaveArtigo.Normalizada("2.6"), ChaveArtigo.Normalizada("2.6"));
        }

        // ---- nulos --------------------------------------------------------

        [Fact]
        public void Nulo_nunca_e_compativel_com_nada()
        {
            Assert.False(ChaveArtigo.Compativel(null, Chave("2.6", "x")));
            Assert.False(ChaveArtigo.Compativel(Chave("2.6", "x"), null));
            // Mesmo com os dois a null: sem chave nao ha artigo.
            Assert.False(ChaveArtigo.Compativel(null, null));
        }

        [Fact]
        public void Normalizada_de_nulo_e_nula()
        {
            Assert.Null(ChaveArtigo.Normalizada(null));
        }
    }
}
