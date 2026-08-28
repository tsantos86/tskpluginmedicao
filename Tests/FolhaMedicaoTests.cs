using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// A folha de medição: o que sai, por que ordem, e onde ficam as somas.
    ///
    /// É o ficheiro que vai para o cliente. Os erros aqui não rebentam nada —
    /// uma ordem trocada, um bloco em falta ou um sub-total na coluna errada
    /// saem numa folha com ar de estar bem feita, e só aparecem na conferência
    /// do outro lado. É por isso que estão sob teste.
    /// </summary>
    public class FolhaMedicaoTests : IDisposable
    {
        // O mapa de quantidades fingido: código -> posição no articulado.
        private readonly Dictionary<string, int> _mapa =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public FolhaMedicaoTests()
        {
            FolhaMedicao.OrdemDoArtigo = chave =>
            {
                int o;
                return _mapa.TryGetValue(chave ?? "", out o) ? o : int.MaxValue;
            };
            FolhaMedicao.Contagens = new List<MedContagem>();
        }

        public void Dispose()
        {
            // Estático partilhado entre testes: deixá-lo posto contaminava o
            // teste seguinte conforme a ordem por que corressem.
            FolhaMedicao.OrdemDoArtigo = null;
            FolhaMedicao.ArtigoCanonico = null;
            FolhaMedicao.Contagens = new List<MedContagem>();
        }

        private const string Sep = "";

        private Parede Med(string servico, string codigo, string piso,
                           double comp, double altura = 2.80, int ordemNoMapa = 0)
        {
            string chave = codigo == null ? "" : codigo + Sep + "descrição de " + codigo;
            if (codigo != null && ordemNoMapa > 0) _mapa[chave] = ordemNoMapa;

            return new Parede
            {
                Handle = servico + "/" + piso + "/" + comp.ToString("F3"),
                Servico = servico,
                Artigo = chave,
                Piso = piso ?? "",
                Alcado = "",
                Bloco = "",
                Comprimento = comp,
                Altura = altura,
                Espessura = 0.15
            };
        }

        private static List<LinhaFolha> Construir(params Parede[] paredes)
        {
            return FolhaMedicao.Construir(paredes.ToList(), new List<MedFachada>(),
                new List<MedItem>(), new List<MedContagem>(),
                RegraDesconto.DescontarTudo, 2);
        }

        private static List<string> Servicos(List<LinhaFolha> l)
        {
            return l.Where(x => x.Tipo == TipoLinha.Capitulo)
                    .Select(x => x.Designacao).ToList();
        }

        // ==================================================================
        // Nada se perde
        // ==================================================================

        [Fact]
        public void Todas_as_medicoes_saem_na_folha()
        {
            // O caso relatado: um serviço aparecia na paleta e não chegava à
            // folha. Se alguma vez voltar a acontecer, é aqui que se apanha.
            var l = Construir(
                Med("ALVENARIA", "3.1.1", "PISO 0", 188.23),
                Med("ALVENARIA", "3.1.1", "PISO 0", 18.30),
                Med("PAR.01", "1.1.1", "", 4.13),
                Med("PAR.01", "1.1.1", "PISO 3", 4.88));

            int medicoes = l.Count(x => x.Tipo == TipoLinha.Medicao);
            Assert.Equal(4, medicoes);

            var comps = l.Where(x => x.Tipo == TipoLinha.Medicao)
                         .Select(x => x.Comp).ToList();
            Assert.Contains(188.23, comps);
            Assert.Contains(18.30, comps);
            Assert.Contains(4.13, comps);
            Assert.Contains(4.88, comps);
        }

        [Fact]
        public void Cada_servico_sai_uma_vez_e_so_uma()
        {
            var l = Construir(
                Med("ALVENARIA", "3.1.1", "PISO 0", 188.23),
                Med("ALVENARIA", "3.1.1", "PISO 0", 18.30),
                Med("PAR.01", "1.1.1", "PISO 3", 4.13));

            var servicos = Servicos(l);
            Assert.Equal(2, servicos.Count);
            Assert.Single(servicos, s => s == "ALVENARIA");
            Assert.Single(servicos, s => s == "PAR.01");
        }

        // ==================================================================
        // A ordem é a do articulado
        // ==================================================================

        [Fact]
        public void Os_artigos_saem_pela_ordem_do_articulado()
        {
            // O 3.1.1 vem DEPOIS do 1.1.1, mesmo tendo sido medido primeiro.
            var l = Construir(
                Med("ALVENARIA", "3.1.1", "PISO 0", 188.23, ordemNoMapa: 300),
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.02", "1.1.2", "PISO 3", 2.49, ordemNoMapa: 200));

            Assert.Equal(new[] { "PAR.01", "PAR.02", "ALVENARIA" }, Servicos(l));
        }

        [Fact]
        public void Medicao_sem_artigo_sai_no_fim()
        {
            var l = Construir(
                Med("SEM_ARTIGO", null, "PISO 0", 9.99),
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100));

            Assert.Equal(new[] { "PAR.01", "SEM_ARTIGO" }, Servicos(l));
        }

        // ==================================================================
        // Espaçamento
        // ==================================================================

        [Fact]
        public void Cada_artigo_novo_leva_uma_linha_em_branco_por_cima()
        {
            var l = Construir(
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.02", "1.1.2", "PISO 3", 2.49, ordemNoMapa: 200));

            for (int i = 1; i < l.Count; i++)
            {
                if (l[i].Tipo != TipoLinha.TituloArtigo) continue;
                Assert.True(l[i - 1].Tipo == TipoLinha.Vazia,
                    "o artigo " + l[i].Item + " devia ter linha em branco por cima");
            }
        }

        [Fact]
        public void Dois_servicos_do_mesmo_artigo_nao_se_separam()
        {
            // O RV.06 e o REV.06 são ambos do artigo 2.6. É UM bloco: um
            // cabeçalho de artigo, e os dois serviços por baixo sem nada a
            // parti-los ao meio.
            var l = Construir(
                Med("RV.06", "2.6", "", 0.664, ordemNoMapa: 100),
                Med("REV.06", "2.6", "", 2.147, ordemNoMapa: 100));

            Assert.Single(l, x => x.Tipo == TipoLinha.TituloArtigo);

            var caps = l.Where(x => x.Tipo == TipoLinha.Capitulo).ToList();
            Assert.Equal(2, caps.Count);

            // O segundo serviço não leva linha em branco por cima: pertence ao
            // mesmo artigo que o primeiro.
            int i2 = l.IndexOf(caps[1]);
            Assert.NotEqual(TipoLinha.Vazia, l[i2 - 1].Tipo);
        }

        [Fact]
        public void A_chave_canonica_junta_o_que_e_o_mesmo_artigo()
        {
            // Duas medições do mesmo artigo com a chave guardada de formas
            // diferentes — uma com a designação inteira, outra cortada. Sem a
            // canónica davam dois blocos, ambos a dizer "2.6".
            string longa = "2.6" + Sep + new string('x', 300);
            string curta = "2.6" + Sep + new string('x', 255);

            FolhaMedicao.ArtigoCanonico = a => a == longa ? curta : a;
            _mapa[curta] = 100;

            var a1 = Med("RV.06", null, "", 0.664);
            a1.Artigo = longa;
            var a2 = Med("REV.06", null, "", 2.147);
            a2.Artigo = curta;

            var l = Construir(a1, a2);

            Assert.Single(l, x => x.Tipo == TipoLinha.TituloArtigo);
        }

        [Fact]
        public void A_folha_nao_abre_com_uma_linha_morta()
        {
            var l = Construir(Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100));
            Assert.NotEqual(TipoLinha.Vazia, l[0].Tipo);
        }

        [Fact]
        public void Nao_ha_linha_em_branco_entre_o_piso_e_as_suas_medicoes()
        {
            var l = Construir(
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.01", "1.1.1", "PISO 4", 5.20, ordemNoMapa: 100));

            for (int i = 0; i < l.Count - 1; i++)
                if (l[i].Tipo == TipoLinha.Piso)
                    Assert.NotEqual(TipoLinha.Vazia, l[i + 1].Tipo);
        }

        [Fact]
        public void Nao_ha_duas_linhas_em_branco_seguidas()
        {
            var l = Construir(
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.01", "1.1.1", "PISO 4", 5.20, ordemNoMapa: 100),
                Med("PAR.02", "1.1.2", "PISO 3", 2.49, ordemNoMapa: 200));

            for (int i = 0; i < l.Count - 1; i++)
                Assert.False(l[i].Tipo == TipoLinha.Vazia &&
                             l[i + 1].Tipo == TipoLinha.Vazia,
                             "duas linhas em branco seguidas na posição " + i);
        }

        // ==================================================================
        // Medições lineares e numeração
        // ==================================================================

        [Fact]
        public void Lineares_saem_na_folha()
        {
            // O bug 3.1 do relatório: as lineares (MEDIR) só entravam na folha
            // pelo exportador simples; na folha ao vivo e no modelo sumiam.
            var lineares = new List<MedItem>
            {
                new MedItem { Categoria = "RODAPÉ", Comprimento = 12.5 },
                new MedItem { Categoria = "RODAPÉ", Comprimento = 3.2 }
            };
            var l = FolhaMedicao.Construir(new List<Parede>(), new List<MedFachada>(),
                lineares, new List<MedContagem>(), RegraDesconto.DescontarTudo, 2);

            var cap = l.Single(x => x.Tipo == TipoLinha.Capitulo);
            Assert.Equal("RODAPÉ", cap.Designacao);
            Assert.Equal("m", cap.Un);
            Assert.Equal(2, l.Count(x => x.Tipo == TipoLinha.Medicao));
            Assert.Equal(12.5, l.First(x => x.Tipo == TipoLinha.Medicao).Comp);
        }

        [Fact]
        public void Linhas_em_branco_nao_consomem_numero()
        {
            // O bug 5.2 do relatório: a linha em branco ficava com "A004" e a
            // medição seguinte saltava para "A005", sem razão nenhuma.
            var p1 = Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100);
            p1.Separador = true;
            var l = Construir(
                p1,
                Med("PAR.01", "1.1.1", "PISO 3", 3.44, ordemNoMapa: 100));

            var vazias = l.Where(x => x.Tipo == TipoLinha.Vazia).ToList();
            Assert.True(vazias.Count > 0,
                "uma parede com separador tem de produzir linha em branco");
            Assert.All(vazias, v => Assert.True(string.IsNullOrEmpty(v.Item),
                "uma linha em branco não pode consumir número"));

            // O piso leva A001; a linha em branco fica sem número; as duas
            // medições vêm logo a seguir, A002 e A003, sem buracos. Antes da
            // correcção a linha em branco roubava um número e as medições
            // saltavam para A003/A004.
            var medicoes = l.Where(x => x.Tipo == TipoLinha.Medicao).ToList();
            Assert.Equal(2, medicoes.Count);
            Assert.Equal("A002", medicoes[0].Item);
            Assert.Equal("A003", medicoes[1].Item);
        }

        // ==================================================================
        // As somas
        // ==================================================================

        [Fact]
        public void O_total_do_servico_soma_as_suas_medicoes()
        {
            var l = Construir(
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.01", "1.1.1", "PISO 3", 3.44, ordemNoMapa: 100));

            var cap = l.Single(x => x.Tipo == TipoLinha.Capitulo);
            Assert.False(string.IsNullOrEmpty(cap.FormulaTotais));
            Assert.StartsWith("SUM(H", cap.FormulaTotais);
        }

        [Fact]
        public void O_total_de_um_servico_nao_apanha_o_seguinte()
        {
            var l = Construir(
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.02", "1.1.2", "PISO 3", 2.49, ordemNoMapa: 200));

            var caps = l.Where(x => x.Tipo == TipoLinha.Capitulo).ToList();
            int iSegundo = l.IndexOf(caps[1]);

            // A fórmula do primeiro tem de acabar antes da linha do segundo.
            int fim = UltimaLinhaDaSoma(caps[0].FormulaTotais);
            Assert.True(fim < 2 + iSegundo,
                "o total de " + caps[0].Designacao + " apanha o serviço seguinte");
        }

        // ------------------------------------------------------------------
        // O sub-total tem sempre de aparecer
        // ------------------------------------------------------------------

        [Fact]
        public void Servico_sem_pisos_leva_o_sub_total_na_sua_linha()
        {
            // O caso do RV.06: medido de ponta a ponta, sem piso nenhum. A
            // coluna Sub total saía vazia de alto a baixo.
            var l = Construir(
                Med("RV.06", "6.1.1", "", 0.664, ordemNoMapa: 100),
                Med("RV.06", "6.1.1", "", 1.445, ordemNoMapa: 100));

            var cap = l.Single(x => x.Tipo == TipoLinha.Capitulo);
            Assert.False(string.IsNullOrEmpty(cap.FormulaSubTotal),
                "sem pisos, o sub-total tem de ficar na linha do serviço");
            Assert.StartsWith("SUM(H", cap.FormulaSubTotal);
        }

        [Fact]
        public void Servico_com_pisos_deixa_o_sub_total_nos_pisos()
        {
            var l = Construir(
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.01", "1.1.1", "PISO 4", 5.20, ordemNoMapa: 100));

            var cap = l.Single(x => x.Tipo == TipoLinha.Capitulo);
            Assert.True(string.IsNullOrEmpty(cap.FormulaSubTotal),
                "com pisos, o serviço não repete o sub-total — senão não se " +
                "sabe qual dos dois manda");

            var pisos = l.Where(x => x.Tipo == TipoLinha.Piso).ToList();
            Assert.Equal(2, pisos.Count);
            Assert.All(pisos, p => Assert.StartsWith("SUM(H", p.FormulaSubTotal));
        }

        [Fact]
        public void Nenhum_servico_fica_sem_sub_total()
        {
            // A garantia, misturando os dois casos na mesma folha.
            var l = Construir(
                Med("RV.06", "6.1.1", "", 0.664, ordemNoMapa: 300),
                Med("PAR.01", "1.1.1", "PISO 3", 4.13, ordemNoMapa: 100),
                Med("PAR.02", "1.1.2", "", 2.49, ordemNoMapa: 200));

            var caps = l.Where(x => x.Tipo == TipoLinha.Capitulo).ToList();
            Assert.Equal(3, caps.Count);

            foreach (var cap in caps)
            {
                int i = l.IndexOf(cap);
                bool temSubTotal = !string.IsNullOrEmpty(cap.FormulaSubTotal);

                // ou nele, ou numa divisão sua antes do serviço seguinte
                for (int j = i + 1; j < l.Count && !temSubTotal; j++)
                {
                    if (l[j].Tipo == TipoLinha.Capitulo) break;
                    if (!string.IsNullOrEmpty(l[j].FormulaSubTotal)) temSubTotal = true;
                }
                Assert.True(temSubTotal,
                    "o serviço " + cap.Designacao + " ficou sem sub-total nenhum");
            }
        }

        [Fact]
        public void O_sub_total_do_servico_sem_pisos_nao_apanha_o_seguinte()
        {
            var l = Construir(
                Med("RV.06", "6.1.1", "", 0.664, ordemNoMapa: 100),
                Med("RV.07", "6.1.2", "", 1.445, ordemNoMapa: 200));

            var caps = l.Where(x => x.Tipo == TipoLinha.Capitulo).ToList();
            int iSegundo = l.IndexOf(caps[1]);

            int fim = UltimaLinhaDaSoma(caps[0].FormulaSubTotal);
            Assert.True(fim < 2 + iSegundo,
                "o sub-total de " + caps[0].Designacao + " apanha o serviço seguinte");
        }

        // ==================================================================
        // O Bloco é uma etiqueta, não um nível
        // ==================================================================

        [Fact]
        public void Bloco_nao_faz_linha_de_cabecalho()
        {
            // Era o que fazia: entrava na chave do alçado e cada bloco saía
            // como um título só seu. Com o campo a servir para "WC1" e
            // "quarto 2", isso enchia a folha de títulos com uma medição cada.
            var a = Med("ALVENARIA", "3.1.1", "", 168.332);
            a.Bloco = "WC1";
            var b = Med("ALVENARIA", "3.1.1", "", 101.984);
            b.Bloco = "QUARTO 2";

            var l = Construir(a, b);

            Assert.DoesNotContain(l, x => x.Tipo == TipoLinha.Alcado);
            Assert.Equal(2, l.Count(x => x.Tipo == TipoLinha.Medicao));
            Assert.Single(Servicos(l));
        }

        [Fact]
        public void Bloco_arranca_a_designacao_da_medicao()
        {
            var a = Med("ALVENARIA", "3.1.1", "", 168.332);
            a.Nota = FolhaMedicao.NotaInicial(null, "WC1");

            var l = Construir(a);
            var med = l.Single(x => x.Tipo == TipoLinha.Medicao);

            Assert.Equal("WC1", med.Designacao);
        }

        [Fact]
        public void Medicao_antiga_com_bloco_e_sem_nota_nao_fica_sem_designacao()
        {
            // As que já estão nos DWG: bloco gravado, nota por escrever. Como
            // o bloco deixou de fazer cabeçalho, sem isto passavam a sair com
            // a coluna Designação em branco — perdiam a identificação que
            // tinham antes da mudança.
            var a = Med("ALVENARIA", "3.1.1", "", 168.332);
            a.Bloco = "CCC";
            a.Nota = "";

            var l = Construir(a);
            Assert.Equal("CCC", l.Single(x => x.Tipo == TipoLinha.Medicao).Designacao);
        }

        [Fact]
        public void Nota_escrita_no_excel_manda_sobre_o_bloco()
        {
            // E não acumula: a nota lida de volta é reescrita tal e qual, sem
            // o bloco colado outra vez ao fim.
            var a = Med("ALVENARIA", "3.1.1", "", 168.332);
            a.Bloco = "CCC";
            a.Nota = "em Pavimento - CCC";

            var l = Construir(a);
            Assert.Equal("em Pavimento - CCC",
                         l.Single(x => x.Tipo == TipoLinha.Medicao).Designacao);
        }

        [Fact]
        public void NotaInicial_nao_esmaga_o_que_ja_foi_escrito()
        {
            // A nota escrita à mão no Excel manda. Se o bloco a substituísse,
            // cada nova sincronização apagava o que a pessoa lá tinha posto.
            Assert.Equal("em Pavimento", FolhaMedicao.NotaInicial("em Pavimento", "CCC"));
            Assert.Equal("CCC", FolhaMedicao.NotaInicial("", "CCC"));
            Assert.Equal("CCC", FolhaMedicao.NotaInicial(null, " CCC "));
            Assert.Equal("", FolhaMedicao.NotaInicial(null, null));
        }

        /// <summary>Lê o "24" de "SUM(H18:H24)".</summary>
        private static int UltimaLinhaDaSoma(string formula)
        {
            int dp = formula.IndexOf(':');
            string fim = formula.Substring(dp + 2).TrimEnd(')');
            return int.Parse(fim);
        }
    }
}
