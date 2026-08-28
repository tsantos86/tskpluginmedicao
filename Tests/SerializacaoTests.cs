using System;
using System.Globalization;
using System.Threading;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Ida e volta da XData. É o que garante que um desenho medido hoje ainda
    /// se lê daqui a dois anos — e que um desenho medido pela versão antiga
    /// continua a abrir na nova.
    /// </summary>
    public class SerializacaoTests
    {
        [Fact]
        public void Vao_sobrevive_a_ida_e_volta()
        {
            var original = new Vao
            {
                Largura = 0.90, Altura = 2.10, Quantidade = 3,
                PreAro = true, Tipo = TipoVao.Janela,
                Espessura = 0.175, Designacao = "VE.02"
            };

            var lido = Vao.Deserialize(original.Serialize());

            Assert.NotNull(lido);
            Assert.Equal(original.Largura, lido.Largura, 9);
            Assert.Equal(original.Altura, lido.Altura, 9);
            Assert.Equal(original.Quantidade, lido.Quantidade);
            Assert.Equal(original.PreAro, lido.PreAro);
            Assert.Equal(original.Tipo, lido.Tipo);
            Assert.Equal(original.Espessura, lido.Espessura, 9);
            Assert.Equal(original.Designacao, lido.Designacao);
        }

        [Fact]
        public void Formato_antigo_de_cinco_campos_continua_a_ler()
        {
            // Desenhos medidos antes de existirem espessura e designação. Se
            // isto partir, o cliente abre uma obra antiga e os vãos somem.
            var v = Vao.Deserialize("0.9|2.1|2|1|P");

            Assert.NotNull(v);
            Assert.Equal(0.9, v.Largura, 9);
            Assert.Equal(2.1, v.Altura, 9);
            Assert.Equal(2, v.Quantidade);
            Assert.True(v.PreAro);
            Assert.Equal(TipoVao.Porta, v.Tipo);
            Assert.Equal(0.0, v.Espessura, 9);
        }

        [Fact]
        public void Menos_de_cinco_campos_e_recusado_em_vez_de_adivinhado()
        {
            Assert.Null(Vao.Deserialize("0.9|2.1|1|0"));
            Assert.Null(Vao.Deserialize(""));
            Assert.Null(Vao.Deserialize("   "));
            Assert.Null(Vao.Deserialize(null));
        }

        [Fact]
        public void Numeros_ilegiveis_ficam_a_zero_sem_rebentar()
        {
            var v = Vao.Deserialize("abc|xyz|nada|0|P");
            Assert.NotNull(v);
            Assert.Equal(0.0, v.Largura, 9);
            Assert.Equal(0.0, v.Altura, 9);
            Assert.Equal(1, v.Quantidade);          // omissão, não zero
        }

        [Fact]
        public void Separadores_na_designacao_nao_partem_o_formato()
        {
            // Se um "|" ou um ";" escapasse para dentro da designação, partia
            // a linha em campos a mais e estragava todos os vãos seguintes.
            var v = new Vao
            {
                Largura = 1.0, Altura = 2.0,
                Designacao = "VE|02;bis"
            };

            var lido = Vao.Deserialize(v.Serialize());
            Assert.NotNull(lido);
            Assert.Equal("VE02bis", lido.Designacao);
            Assert.Equal(1.0, lido.Largura, 9);
            Assert.Equal(2.0, lido.Altura, 9);
        }

        [Fact]
        public void Lista_de_vaos_de_uma_parede_sobrevive_a_ida_e_volta()
        {
            var p = new Parede { Comprimento = 10, Altura = 2.8 };
            p.Vaos.Add(new Vao { Largura = 0.90, Altura = 2.10, Designacao = "PC.01" });
            p.Vaos.Add(new Vao { Largura = 1.20, Altura = 1.10, Quantidade = 2,
                                 Tipo = TipoVao.Janela, Designacao = "VE.03" });

            string s = p.SerializeVaos();

            var outra = new Parede { Comprimento = 10, Altura = 2.8 };
            outra.DeserializeVaos(s);

            Assert.Equal(2, outra.Vaos.Count);
            Assert.Equal("PC.01", outra.Vaos[0].Designacao);
            Assert.Equal("VE.03", outra.Vaos[1].Designacao);
            Assert.Equal(2, outra.Vaos[1].Quantidade);
            Assert.Equal(TipoVao.Janela, outra.Vaos[1].Tipo);
        }

        [Fact]
        public void Ler_vaos_limpa_os_que_la_estavam()
        {
            var p = new Parede();
            p.Vaos.Add(new Vao { Largura = 1, Altura = 1 });
            p.DeserializeVaos("");
            Assert.Empty(p.Vaos);
        }

        [Fact]
        public void Uma_entrada_corrompida_no_meio_nao_leva_as_outras_atras()
        {
            var p = new Parede();
            p.DeserializeVaos("0.9|2.1|1|0|P;lixo;1.2|1.1|1|0|J");
            Assert.Equal(2, p.Vaos.Count);
        }

        [Fact]
        public void Formato_nao_muda_com_a_lingua_do_Windows()
        {
            // Em pt-PT o separador decimal é a vírgula. Se a serialização
            // seguisse a cultura da máquina, um desenho medido em Portugal
            // não abriria noutro sítio — e o "|" já é o separador de campos.
            var antes = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pt-PT");
                string s = new Vao { Largura = 0.9, Altura = 2.1 }.Serialize();
                Assert.StartsWith("0.9|2.1|", s);

                Thread.CurrentThread.CurrentCulture = new CultureInfo("en-US");
                var lido = Vao.Deserialize(s);
                Assert.Equal(0.9, lido.Largura, 9);
            }
            finally { Thread.CurrentThread.CurrentCulture = antes; }
        }
    }
}
