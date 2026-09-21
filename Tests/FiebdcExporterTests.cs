using System;
using System.Collections.Generic;
using System.Linq;
using TSKTakeOff;
using Xunit;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Verifica a ESTRUTURA do .bc3 gerado — separadores, marcadores de
    /// registo, agregação por artigo. Não é (e não pode ser, sem o
    /// Arquimedes/CYPECAD à mão) uma confirmação de que o ficheiro importa
    /// tal como se espera — ver a nota em FiebdcExporter.cs.
    /// </summary>
    public class FiebdcExporterTests
    {
        private static readonly DateTime Data = new DateTime(2026, 9, 21);

        [Fact]
        public void Comeca_sempre_pelo_registo_de_cabecalho()
        {
            string bc3 = FiebdcExporter.Gerar(
                new List<FiebdcExporter.Linha>(), "Obra Teste", Data);
            Assert.StartsWith("~V|", bc3);
        }

        [Fact]
        public void Sem_linhas_ainda_escreve_o_conceito_raiz()
        {
            string bc3 = FiebdcExporter.Gerar(
                new List<FiebdcExporter.Linha>(), "Obra Teste", Data);
            Assert.Contains("~C|" + FiebdcExporter.CodigoRaiz + "|", bc3);
            // Sem artigos, não há decomposição nenhuma a escrever.
            Assert.DoesNotContain("~D|", bc3);
        }

        [Fact]
        public void Um_artigo_gera_conceito_decomposicao_e_medicao()
        {
            var linhas = new List<FiebdcExporter.Linha>
            {
                new FiebdcExporter.Linha("PISO 0", "1.1.1", "Parede em alvenaria", "m2", 12.5),
            };
            string bc3 = FiebdcExporter.Gerar(linhas, "Obra Teste", Data);

            Assert.Contains("~C|1.1.1|m2|Parede em alvenaria|", bc3);
            Assert.Contains("~D|" + FiebdcExporter.CodigoRaiz + "|1.1.1\\1\\1\\100\\|", bc3);
            Assert.Contains("~M|1.1.1||12.5|", bc3);
        }

        [Fact]
        public void O_mesmo_artigo_em_varios_pisos_soma_no_total_e_so_aparece_uma_vez()
        {
            var linhas = new List<FiebdcExporter.Linha>
            {
                new FiebdcExporter.Linha("PISO 0", "1.1.1", "Parede em alvenaria", "m2", 10),
                new FiebdcExporter.Linha("PISO 1", "1.1.1", "Parede em alvenaria", "m2", 5.25),
            };
            string bc3 = FiebdcExporter.Gerar(linhas, "Obra Teste", Data);

            // Um artigo == um ~C e um ~M, mesmo repetido em dois pisos.
            Assert.Equal(1, ContarOcorrencias(bc3, "~C|1.1.1|"));
            Assert.Equal(1, ContarOcorrencias(bc3, "~M|1.1.1|"));
            Assert.Contains("~M|1.1.1||15.25|", bc3);
            // O detalhe por piso fica registado em texto, não perdido.
            Assert.Contains("~T|1.1.1|PISO 0: 10 m2 · PISO 1: 5.25 m2|", bc3);
        }

        [Fact]
        public void Cada_artigo_unico_entra_na_decomposicao_da_raiz_uma_vez_so()
        {
            var linhas = new List<FiebdcExporter.Linha>
            {
                new FiebdcExporter.Linha("PISO 0", "1.1.1", "Parede", "m2", 10),
                new FiebdcExporter.Linha("PISO 1", "1.1.1", "Parede", "m2", 5),
                new FiebdcExporter.Linha("PISO 0", "2.1.1", "Rodapé", "m", 3),
            };
            string bc3 = FiebdcExporter.Gerar(linhas, "Obra Teste", Data);

            var linhaD = bc3.Split(new[] { "\r\n" }, StringSplitOptions.None)
                .First(l => l.StartsWith("~D|"));
            Assert.Equal(1, ContarOcorrencias(linhaD, "1.1.1\\"));
            Assert.Contains("2.1.1\\1\\1\\100\\", linhaD);
        }

        [Fact]
        public void Linhas_sem_codigo_sao_ignoradas()
        {
            var linhas = new List<FiebdcExporter.Linha>
            {
                new FiebdcExporter.Linha("PISO 0", "", "Sem código", "m2", 10),
                new FiebdcExporter.Linha("PISO 0", "1.1.1", "Com código", "m2", 5),
            };
            string bc3 = FiebdcExporter.Gerar(linhas, "Obra Teste", Data);

            Assert.DoesNotContain("Sem código", bc3);
            Assert.Contains("1.1.1", bc3);
        }

        [Fact]
        public void Separadores_do_formato_dentro_de_um_texto_sao_neutralizados()
        {
            var linhas = new List<FiebdcExporter.Linha>
            {
                new FiebdcExporter.Linha("PISO 0", "1.1.1",
                    "Parede | com ~ separadores \\ do formato", "m2", 1),
            };
            string bc3 = FiebdcExporter.Gerar(linhas, "Obra Teste", Data);

            var linhaC = bc3.Split(new[] { "\r\n" }, StringSplitOptions.None)
                .First(l => l.StartsWith("~C|1.1.1|"));
            // ~C|CODIGO|UNIDADE|RESUMO|PRECO|DATA|TIPO| — 6 campos, 7 pipes.
            Assert.Equal(7, linhaC.Count(c => c == '|'));
            // E nenhum separador do próprio formato sobreviveu dentro do texto.
            Assert.DoesNotContain("separadores \\", linhaC);
        }

        [Fact]
        public void Numeros_usam_ponto_decimal_nunca_virgula()
        {
            var linhas = new List<FiebdcExporter.Linha>
            {
                new FiebdcExporter.Linha("PISO 0", "1.1.1", "Parede", "m2", 12.5),
            };
            string bc3 = FiebdcExporter.Gerar(linhas, "Obra Teste", Data);
            Assert.Contains("12.5", bc3);
            Assert.DoesNotContain("12,5", bc3);
        }

        [Fact]
        public void Ficheiro_usa_latin1_nao_utf8()
        {
            Assert.Equal("iso-8859-1", FiebdcExporter.CodificacaoFicheiro.WebName);
        }

        private static int ContarOcorrencias(string texto, string sub)
        {
            int n = 0, i = 0;
            while ((i = texto.IndexOf(sub, i, StringComparison.Ordinal)) >= 0)
            { n++; i += sub.Length; }
            return n;
        }
    }
}
