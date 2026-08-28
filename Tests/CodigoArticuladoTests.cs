using System;
using System.Collections.Generic;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// A ordem por que a medição se entrega.
    ///
    /// Quem confere a folha segue-a de cima a baixo contra o mapa que enviou.
    /// Se a ordem estiver errada, a folha continua a parecer bem feita — não
    /// há nada no aspecto dela que denuncie um 3.1.1 antes de um 1.1.1 — e o
    /// erro só aparece na conferência, linha a linha, do outro lado.
    /// </summary>
    public class CodigoArticuladoTests
    {
        private static void Antes(string primeiro, string segundo)
        {
            Assert.True(CodigoArticulado.Comparar(primeiro, segundo) < 0,
                primeiro + " devia vir antes de " + segundo);
            // A comparação inversa tem de concordar, senão a ordenação depende
            // da ordem por que os pares calham a ser comparados.
            Assert.True(CodigoArticulado.Comparar(segundo, primeiro) > 0,
                segundo + " devia vir depois de " + primeiro);
        }

        [Fact]
        public void Capitulos_diferentes_seguem_o_numero()
        {
            // O caso que deu origem a isto: a folha do capítulo 3 vinha antes
            // da do capítulo 1 no livro do cliente, e o 3.1.1 saía à frente do
            // 1.1.1 na medição entregue.
            Antes("1.1.1", "3.1.1");
            Antes("1.1.4", "3.1.1");
        }

        [Fact]
        public void Dentro_do_capitulo_segue_a_numeracao()
        {
            Antes("1.1.1", "1.1.2");
            Antes("1.1.2", "1.1.3");
            Antes("1.1.9", "1.2.1");
        }

        [Fact]
        public void Dez_vem_depois_de_nove_e_nao_antes()
        {
            // Comparado como texto, "10" < "9". É o erro clássico, e é
            // invisível: a lista sai ordenada, só que pela ordem errada.
            Antes("9", "10");
            Antes("1.9", "1.10");
            Antes("1.9.9", "1.10.1");
            Antes("2.2", "2.10");
        }

        [Fact]
        public void O_capitulo_abre_o_que_lhe_pertence()
        {
            Antes("1", "1.1");
            Antes("1.1", "1.1.1");
            Antes("3.1", "3.1.1");
        }

        [Fact]
        public void Sufixo_de_letra_desempata_depois_do_numero()
        {
            // "10.1.1.306A" veio de um mapa de obra a sério.
            Antes("10.1.1.306", "10.1.1.306A");
            Antes("10.1.1.306A", "10.1.1.306B");
            Antes("10.1.1.9A", "10.1.1.306A");
        }

        [Fact]
        public void Codigo_sem_numeracao_vai_para_o_fim()
        {
            // "Sem Ref" aparece duas vezes no mapa do Lumare. Não tem lugar na
            // hierarquia, e inventar-lhe um punha-o no meio do articulado.
            Antes("99.9", "Sem Ref");
            Antes("1.1.1", "Sem Ref");
            Assert.Equal(0, CodigoArticulado.Comparar("Sem Ref", "Sem Ref"));
        }

        [Fact]
        public void Ponto_final_e_espacos_nao_mudam_o_lugar()
        {
            // "8.3.3." com ponto no fim existe nos ficheiros reais.
            Assert.Equal(0, CodigoArticulado.Comparar("8.3.3.", "8.3.3"));
            Assert.Equal(0, CodigoArticulado.Comparar(" 1.1.1 ", "1.1.1"));
        }

        [Fact]
        public void Vazio_e_nulo_nao_rebentam()
        {
            Assert.Equal(0, CodigoArticulado.Comparar(null, ""));
            Assert.Equal(0, CodigoArticulado.Comparar("", ""));
            // Sem código não há lugar na numeração: fica com os "Sem Ref".
            Antes("1.1.1", "");
        }

        [Fact]
        public void Uma_lista_inteira_sai_pela_ordem_do_articulado()
        {
            // A prova que interessa: o mapa do cliente, baralhado como vem do
            // livro, tem de sair como se lê.
            var baralhado = new List<string>
            {
                "3.1.1", "1.1.10", "Sem Ref", "1.1.2", "1", "10.1",
                "1.1.1", "2", "1.1", "9.1"
            };

            baralhado.Sort(CodigoArticulado.Comparar);

            Assert.Equal(new[]
            {
                "1", "1.1", "1.1.1", "1.1.2", "1.1.10",
                "2", "3.1.1", "9.1", "10.1", "Sem Ref"
            }, baralhado);
        }
    }
}
