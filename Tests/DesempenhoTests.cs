using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Xunit;
using Xunit.Abstractions;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Medições de desempenho da lógica pura da paleta de resultados (sem
    /// AutoCAD nem WinForms) — árvore de resultados, folha de medição e
    /// exportador FIEBDC — com 100, 1.000 e 5.000 medições sintéticas.
    ///
    /// Objetivo: dar números reais a <c>Docs/ANALISE_DESEMPENHO.md</c>, não
    /// travar a suíte. Por isso os limites de tempo são muito folgados — só
    /// apanham uma regressão absurda (ex.: algo que passou de O(n) a O(n²) e
    /// deixou de terminar em segundos), nunca uma máquina de CI mais lenta.
    ///
    /// Correr sozinho: dotnet test Tests/TSKTakeOff.Tests.csproj --filter Categoria=Desempenho
    /// </summary>
    [Trait("Categoria", "Desempenho")]
    public class DesempenhoTests
    {
        private readonly ITestOutputHelper _saida;
        private static readonly CultureInfo Pt = new CultureInfo("pt-PT");

        public DesempenhoTests(ITestOutputHelper saida)
        {
            _saida = saida;
        }

        /// <summary>
        /// Gera <paramref name="n"/> paredes sintéticas espalhadas por 5 pisos,
        /// 4 serviços e 20 artigos, cerca de 1 em cada 3 com um vão e 1 em
        /// cada 10 com um título — uma mistura plausível do que um desenho
        /// grande tem, não um caso melhor-possível artificialmente uniforme.
        /// </summary>
        private static List<Parede> ParedesSinteticas(int n)
        {
            var paredes = new List<Parede>(n);
            for (int i = 0; i < n; i++)
            {
                var p = new Parede
                {
                    Handle = "H" + i.ToString(CultureInfo.InvariantCulture),
                    Piso = "PISO " + (i % 5),
                    Servico = "SERVICO " + (i % 4),
                    Artigo = (i % 17 == 0) ? "" : // por classificar, de vez em quando
                        (10 + i % 20) + ".1\u001fArtigo " + (i % 20),
                    Alcado = "ALCADO " + (i % 3),
                    Bloco = "BLOCO " + (i % 6),
                    Comprimento = 3.0 + (i % 7) * 0.5,
                    Altura = 2.6 + (i % 3) * 0.1,
                    Espessura = 0.15,
                    Ordem = i + 1
                };

                if (i % 3 == 0)
                    p.Vaos.Add(new Vao
                    {
                        Designacao = "V" + i.ToString(CultureInfo.InvariantCulture),
                        Largura = 0.9,
                        Altura = 2.1,
                        Quantidade = 1
                    });

                if (i % 10 == 0)
                    p.AlternarMarca("ART");

                paredes.Add(p);
            }
            return paredes;
        }

        private static void Medir(ITestOutputHelper saida, string operacao, int n, Action accao)
        {
            // Uma passagem de aquecimento (JIT) fora da medição, para não
            // culpar a operação pelo custo de a compilar a primeira vez.
            accao();

            var cron = Stopwatch.StartNew();
            accao();
            cron.Stop();

            saida.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0} | n={1,5} | {2,8} ms", operacao, n, cron.ElapsedMilliseconds));

            // Limite muito folgado (não um alvo de desempenho): só apanha uma
            // regressão de ordem de grandeza (ex.: O(n) a virar O(n²)).
            Assert.True(cron.ElapsedMilliseconds < 15000,
                string.Format(CultureInfo.InvariantCulture,
                    "{0} com n={1} demorou {2} ms — muito acima do esperado para lógica pura",
                    operacao, n, cron.ElapsedMilliseconds));
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Construcao_da_arvore_de_resultados(int n)
        {
            var paredes = ParedesSinteticas(n);

            Medir(_saida, "ResultadosArvore.DeAlvenaria (projectar+construir)", n, () =>
            {
                ResultadosArvore.DeAlvenaria(paredes, RegraDesconto.DescontarTudo, null, Pt);
            });
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Projeccao_da_vista_sem_filtros(int n)
        {
            var paredes = ParedesSinteticas(n);
            var raiz = ResultadosArvore.DeAlvenaria(paredes, RegraDesconto.DescontarTudo, null, Pt);
            var estado = new EstadoVista();

            Medir(_saida, "ResultadosArvore.Projetar (sem filtros, tudo expandido)", n, () =>
            {
                ResultadosArvore.Projetar(raiz, estado);
            });
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Projeccao_da_vista_com_pesquisa_e_filtro(int n)
        {
            var paredes = ParedesSinteticas(n);
            var raiz = ResultadosArvore.DeAlvenaria(paredes, RegraDesconto.DescontarTudo, null, Pt);
            var estado = new EstadoVista
            {
                Pesquisa = "artigo 3",
                Filtro = new FiltroResultados { Pavimentos = { "PISO 1", "PISO 2" } }
            };

            Medir(_saida, "ResultadosArvore.Projetar (com pesquisa + filtro)", n, () =>
            {
                ResultadosArvore.Projetar(raiz, estado);
            });
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Construcao_da_folha_de_medicao(int n)
        {
            var paredes = ParedesSinteticas(n);

            Medir(_saida, "FolhaMedicao.Construir (só alvenaria)", n, () =>
            {
                FolhaMedicao.Construir(paredes, null, null, RegraDesconto.DescontarTudo);
            });
        }

        [Theory]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(5000)]
        public void Exportacao_fiebdc(int n)
        {
            var linhas = new List<FiebdcExporter.Linha>(n);
            for (int i = 0; i < n; i++)
            {
                linhas.Add(new FiebdcExporter.Linha(
                    "PISO " + (i % 5),
                    (10 + i % 20) + "." + (i % 3),
                    "Artigo " + (i % 20),
                    "m2",
                    1.5 + (i % 7) * 0.25));
            }

            Medir(_saida, "FiebdcExporter.Gerar", n, () =>
            {
                FiebdcExporter.Gerar(linhas, "Obra sintética", new DateTime(2026, 9, 27));
            });
        }
    }
}
