using System.Collections.Generic;
using System.Globalization;
using TSKTakeOff;
using Xunit;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Os totais da árvore.
    ///
    /// A regra é uma só e vale em todos os níveis: um grupo nunca tem um total
    /// escrito à mão — soma os filhos —, e nunca soma unidades diferentes. É a
    /// mesma que o <see cref="TotaisMedicao"/> já defende no rodapé, agora
    /// dentro da hierarquia, onde há mais sítios para se enganar: um vão que
    /// desconta duas vezes, um título com um total inventado, um comprimento de
    /// parede a cair no balde dos metros de um rodapé.
    /// </summary>
    public class ResultadosTotaisTests
    {
        private static readonly CultureInfo Pt = new CultureInfo("pt-PT");

        private static MedicaoResultado Medicao(
            string handle, string piso, string servico, string artigo,
            string unidade, double quantidade)
        {
            return new MedicaoResultado
            {
                Handle = handle,
                Piso = piso,
                Servico = servico,
                Artigo = artigo,
                Rotulo = handle,
                Unidade = unidade,
                Quantidade = quantidade
            };
        }

        // -----------------------------------------------------------------
        // Somar os filhos
        // -----------------------------------------------------------------

        [Fact]
        public void O_total_do_topo_e_a_soma_dos_pisos()
        {
            // 5,20×2,80 − 0,90×2,10 = 12,67 ; 3,45×2,80 = 9,66 ; 4,00×2,80 = 11,20
            var raiz = ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos());

            double piso0 = raiz.Filhos[0].Quantidades.De("m2");
            double piso1 = raiz.Filhos[1].Quantidades.De("m2");

            Assert.Equal(22.33, piso0, 2);
            Assert.Equal(11.20, piso1, 2);
            Assert.Equal(piso0 + piso1, raiz.Quantidades.De("m2"), 3);
        }

        [Fact]
        public void Cada_nivel_soma_o_de_baixo()
        {
            var raiz = ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos());
            var piso = raiz.Filhos[0];
            var servico = piso.Filhos[0];
            var artigo = servico.Filhos[0];

            double soma = 0.0;
            foreach (var m in artigo.Filhos) soma += m.Quantidades.De("m2");

            Assert.Equal(soma, artigo.Quantidades.De("m2"), 3);
            Assert.Equal(artigo.Quantidades.De("m2"), servico.Quantidades.De("m2"), 3);
            Assert.Equal(servico.Quantidades.De("m2"), piso.Quantidades.De("m2"), 3);
        }

        // -----------------------------------------------------------------
        // Unidades que não se somam
        // -----------------------------------------------------------------

        [Fact]
        public void Paredes_e_camadas_ficam_em_baldes_diferentes()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[2].AreaVezesAltura = true;     // o PISO 1 passa a faturar m³

            var raiz = ResultadosArvoreTests.Arvore(paredes);

            Assert.Equal(2, raiz.Quantidades.Count);
            Assert.Equal(22.33, raiz.Quantidades.De("m2"), 2);
            Assert.Equal(11.20, raiz.Quantidades.De("m3"), 2);
        }

        [Fact]
        public void Um_grupo_com_unidades_diferentes_mostra_as_lado_a_lado()
        {
            var raiz = ResultadosArvore.Construir(new List<MedicaoResultado>
            {
                Medicao("A", "PISO 0", "ARQ", "11.2.1", "m2", 62.93),
                Medicao("B", "PISO 0", "REV", "12.1.3", "m",  36.90),
                Medicao("C", "PISO 0", "CAR", "14.2.1", "un",  2.0),
            }, Pt);

            Assert.Equal("62,93 m² · 36,90 m · 2 un.", raiz.Quantidades.Texto(Pt));
        }

        [Fact]
        public void As_unidades_saem_pela_ordem_de_chegada_nao_por_nome()
        {
            // Quem mede alvenaria vê m² à frente. Por nome, o m³ vinha sempre
            // primeiro, que é o caso menos comum.
            var raiz = ResultadosArvore.Construir(new List<MedicaoResultado>
            {
                Medicao("A", "PISO 0", "ARQ", "1.1", "m2", 10.0),
                Medicao("B", "PISO 0", "ARQ", "1.2", "m3", 2.0),
            }, Pt);

            var lista = raiz.Quantidades.Lista();
            Assert.Equal("m2", lista[0].Key);
            Assert.Equal("m3", lista[1].Key);
        }

        [Fact]
        public void O_comprimento_de_uma_parede_nao_entra_no_total_em_metros()
        {
            // O caso que engana: o comprimento de uma parede e o comprimento de
            // um rodapé não vão ao mesmo balde. Um é DIMENSÃO de um artigo
            // medido a m², o outro É a quantidade de um artigo medido a metro.
            var parede = ResultadosArvoreTests.Parede(
                "P", "PISO 0", "ALVENARIA", "11.2.1", 30.0, 2.80);

            var medicoes = ResultadosArvore.DeParedes(
                new List<Parede> { parede }, RegraDesconto.DescontarTudo, null, Pt);
            medicoes.Add(Medicao("R", "PISO 0", "REVESTIMENTOS", "12.1.3", "m", 29.15));

            var raiz = ResultadosArvore.Construir(medicoes, Pt);

            // Os 30 m de comprimento da parede NÃO estão aqui.
            Assert.Equal(29.15, raiz.Quantidades.De("m"), 2);
            Assert.Equal(84.00, raiz.Quantidades.De("m2"), 2);

            // Estão onde são o que são: uma dimensão, nas propriedades.
            var med = ResultadosArvoreTests.Procurar(raiz, "M:P");
            Assert.Equal("30,00 m", med.Propriedades.Find(x => x.Campo == "comprimento").Valor);
            Assert.Single(med.Quantidades.Lista());
            Assert.Equal("m2", med.Quantidades.Lista()[0].Key);
        }

        // -----------------------------------------------------------------
        // Vãos e títulos
        // -----------------------------------------------------------------

        [Fact]
        public void O_vao_nao_desconta_uma_segunda_vez()
        {
            // A área líquida da parede JÁ tem o vão descontado. Se o nó do vão
            // entrasse na soma do grupo, descontava-o outra vez.
            var p = ResultadosArvoreTests.Parede("A", "PISO 0", "ALV", "1.1", 5.0, 2.0);
            p.Vaos.Add(new Vao { Largura = 1.0, Altura = 2.0, Quantidade = 1 });

            var raiz = ResultadosArvoreTests.Arvore(new List<Parede> { p });

            Assert.Equal(8.0, raiz.Quantidades.De("m2"), 3);      // 10 − 2, e não 6
        }

        [Fact]
        public void O_vao_mostra_se_negativo_mas_nao_conta_para_o_total()
        {
            var p = ResultadosArvoreTests.Parede("A", "PISO 0", "ALV", "1.1", 5.0, 2.0);
            p.Vaos.Add(new Vao { Designacao = "P01", Largura = 1.0, Altura = 2.0, Quantidade = 1 });

            var med = ResultadosArvoreTests.Procurar(
                ResultadosArvoreTests.Arvore(new List<Parede> { p }), "M:A");
            var vao = med.Filhos[0];

            Assert.Equal(-2.0, vao.Quantidades.De("m2"), 3);
            Assert.False(vao.ContaParaTotal);
            Assert.Equal(8.0, med.Quantidades.De("m2"), 3);
        }

        [Fact]
        public void Um_titulo_nao_tem_total_nenhum()
        {
            var p = ResultadosArvoreTests.Parede("A", "PISO 0", "ALV", "1.1", 5.0, 2.0);
            p.AlternarMarca("ART");

            var raiz = ResultadosArvoreTests.Arvore(new List<Parede> { p });

            Assert.Equal(10.0, raiz.Quantidades.De("m2"), 3);
            var titulo = ResultadosArvoreTests.Procurar(raiz, "M:A/T:0");
            Assert.True(titulo.Quantidades.Vazio);
        }

        [Fact]
        public void A_regra_SINAPI_muda_o_desconto_e_o_total()
        {
            var p = ResultadosArvoreTests.Parede("A", "PISO 0", "ALV", "1.1", 5.0, 2.0);
            p.Vaos.Add(new Vao { Largura = 1.0, Altura = 2.0, Quantidade = 1 });   // 2 m²

            // SINAPI: vãos até 2 m² não descontam.
            var raiz = ResultadosArvore.DeAlvenaria(
                new List<Parede> { p }, RegraDesconto.Sinapi2m2, null, Pt);

            Assert.Equal(10.0, raiz.Quantidades.De("m2"), 3);
            Assert.Equal(0.0, ResultadosArvoreTests.Procurar(raiz, "M:A/V:0")
                .Quantidades.De("m2"), 3);
        }

        // -----------------------------------------------------------------
        // Totais visíveis
        // -----------------------------------------------------------------

        [Fact]
        public void Filtrar_da_o_total_do_que_esta_a_vista_e_o_total_de_tudo()
        {
            var estado = new EstadoVista();
            estado.Filtro.Pavimentos.Add("PISO 1");

            var vista = ResultadosArvore.Projetar(
                ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos()), estado);

            Assert.Equal(11.20, vista.TotaisVisiveis.De("m2"), 2);
            Assert.Equal(33.53, vista.TotaisGlobais.De("m2"), 2);
        }

        [Fact]
        public void O_total_de_um_grupo_filtrado_bate_com_as_linhas_que_ele_mostra()
        {
            // Senão a soma das linhas não bate com o cabeçalho delas, e quem
            // confere fica sem saber qual dos dois números acreditar.
            var paredes = ResultadosArvoreTests.DoisPisos();
            var estado = new EstadoVista { Pesquisa = "2A1" };

            var vista = ResultadosArvore.Projetar(
                ResultadosArvoreTests.Arvore(paredes), estado);

            var piso0 = vista.Procurar(vista.Nos[0].No.Filhos[0].Id);
            Assert.Equal(9.66, piso0.Quantidades.De("m2"), 2);      // só a 2A1
            Assert.Equal(9.66, vista.TotaisVisiveis.De("m2"), 2);
        }

        [Fact]
        public void Recolher_nao_muda_o_total_do_grupo()
        {
            // Fechar um piso para ver melhor não pode dar a impressão de a
            // medição ter encolhido.
            var raiz = ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos());
            var estado = new EstadoVista();
            estado.Recolher(raiz.Filhos[0].Id);

            var vista = ResultadosArvore.Projetar(raiz, estado);

            Assert.Equal(22.33, vista.Procurar(raiz.Filhos[0].Id).Quantidades.De("m2"), 2);
            Assert.Equal(33.53, vista.TotaisVisiveis.De("m2"), 2);
        }

        // -----------------------------------------------------------------
        // "n visíveis de N"
        // -----------------------------------------------------------------

        [Fact]
        public void Sem_filtro_o_resumo_e_a_contagem_total()
        {
            var vista = ResultadosArvore.Projetar(
                ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos()),
                new EstadoVista());

            Assert.Equal("3 medições", vista.Resumo(Pt));
        }

        [Fact]
        public void Com_filtro_o_resumo_diz_quantas_ficaram_a_vista()
        {
            var estado = new EstadoVista();
            estado.Filtro.Pavimentos.Add("PISO 0");

            var vista = ResultadosArvore.Projetar(
                ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos()), estado);

            Assert.Equal("2 visíveis de 3", vista.Resumo(Pt));
        }

        [Fact]
        public void Uma_so_medicao_le_se_no_singular()
        {
            var raiz = ResultadosArvoreTests.Arvore(new List<Parede>
            {
                ResultadosArvoreTests.Parede("A", "PISO 0", "ALV", "1.1", 2.0, 2.0)
            });

            Assert.Equal("1 medição", ResultadosArvore.Projetar(raiz, new EstadoVista()).Resumo(Pt));
        }

        [Fact]
        public void A_contagem_e_calculada_e_nao_texto_fixo()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes.Add(ResultadosArvoreTests.Parede("2A3", "PISO 1", "ALVENARIA", "1.1", 1.0, 1.0));

            var vista = ResultadosArvore.Projetar(
                ResultadosArvoreTests.Arvore(paredes), new EstadoVista());

            Assert.Equal(4, vista.MedicoesTotais);
            Assert.Equal("4 medições", vista.Resumo(Pt));
        }

        // -----------------------------------------------------------------
        // Escrita das unidades
        // -----------------------------------------------------------------

        [Fact]
        public void As_unidades_escrevem_se_como_quem_mede_as_le()
        {
            Assert.Equal("m²", Unidades.Escrita("m2"));
            Assert.Equal("m³", Unidades.Escrita("m3"));
            Assert.Equal("m", Unidades.Escrita("m"));
            Assert.Equal("un.", Unidades.Escrita("un"));
        }

        [Fact]
        public void As_unidades_contam_se_inteiras()
        {
            // Dar uma casa decimal a quem contou duas portas é inventar-lhe
            // precisão que a medição não tem.
            Assert.Equal("2 un.", Unidades.Texto("un", 2.0, Pt));
            Assert.Equal("12,67 m²", Unidades.Texto("m2", 12.67, Pt));
        }

        [Fact]
        public void Um_acumulador_vazio_nao_escreve_nada()
        {
            var q = new Quantidades();
            Assert.True(q.Vazio);
            Assert.Equal("", q.Texto(Pt));
            Assert.Equal(0.0, q.De("m2"));
            Assert.False(q.Tem("m2"));
        }

        [Fact]
        public void Somar_um_acumulador_a_outro_preserva_a_ordem()
        {
            var a = new Quantidades();
            a.Somar("m2", 1.0);

            var b = new Quantidades();
            b.Somar("m3", 2.0);
            b.Somar("m2", 3.0);

            a.Somar(b);

            var lista = a.Lista();
            Assert.Equal("m2", lista[0].Key);
            Assert.Equal(4.0, lista[0].Value, 3);
            Assert.Equal("m3", lista[1].Key);
        }

        // -----------------------------------------------------------------
        // Os factores das colunas «Comp.» e «Altura»
        // -----------------------------------------------------------------

        [Fact]
        public void Numa_medicao_os_factores_sao_os_da_geometria()
        {
            var med = ResultadosArvoreTests.Procurar(
                ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos()), "M:2A0");

            Assert.Equal(5.20, med.Comprimento, 2);
            Assert.Equal("m", med.UnidadeComprimento);
            Assert.Equal(2.80, med.Altura, 2);
            Assert.Equal("5,20 m", med.TextoComprimento(Pt));
            Assert.Equal("2,80 m", med.TextoAltura(Pt));
        }

        [Fact]
        public void Numa_camada_o_primeiro_factor_e_uma_AREA_em_planta()
        {
            // É o par de factores que explica por que o resultado é m³ e não
            // m². Debaixo de um cabeçalho "Comp." em metros, os 48,50 seriam
            // uma mentira — por isso a unidade viaja na célula.
            var c = ResultadosArvoreTests.Parede("C", "PISO 0", "BET", "7.4", 1.0, 0.08);
            c.Largura = 48.50;
            c.AreaVezesAltura = true;

            var med = ResultadosArvoreTests.Procurar(
                ResultadosArvoreTests.Arvore(new List<Parede> { c }), "M:C");

            Assert.Equal(48.50, med.Comprimento, 2);
            Assert.Equal("m2", med.UnidadeComprimento);
            Assert.Equal("48,50 m²", med.TextoComprimento(Pt));
            Assert.Equal("0,08 m", med.TextoAltura(Pt));
            Assert.Equal("m3", med.Quantidades.Lista()[0].Key);
        }

        [Fact]
        public void Um_linear_nao_tem_altura_e_mostra_um_traco()
        {
            // Não é zero: um rodapé não tem altura nenhuma. Um "0,00 m" ali
            // seria uma dimensão inventada — e foi assim que a contagem de
            // duas portas foi parar debaixo de «Comp.».
            var raiz = ResultadosArvore.Construir(ResultadosAdaptadores.DeLineares(
                new List<MedItem> { new MedItem { Handle = "L1", Comprimento = 29.15 } }, Pt), Pt);

            var med = ResultadosArvoreTests.Procurar(raiz, "M:L1");
            Assert.Equal("29,15 m", med.TextoComprimento(Pt));
            Assert.Equal("—", med.TextoAltura(Pt));
        }

        [Fact]
        public void Uma_contagem_nao_tem_factores_nenhuns()
        {
            var raiz = ResultadosArvore.Construir(ResultadosAdaptadores.DeContagens(
                new List<MedContagem> { new MedContagem { Handle = "C1", Nome = "P.01" } }, Pt), Pt);

            var med = ResultadosArvoreTests.Procurar(raiz, "M:C1");
            Assert.Equal("—", med.TextoComprimento(Pt));
            Assert.Equal("—", med.TextoAltura(Pt));
        }

        [Fact]
        public void Num_grupo_o_comprimento_soma_se()
        {
            // 5,20 + 3,45 = 8,65 — o desenvolvimento daquelas paredes, que é
            // uma coisa que existe.
            var artigo = ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos())
                         .Filhos[0].Filhos[0].Filhos[0];

            Assert.Equal(8.65, artigo.Comprimento, 2);
        }

        [Fact]
        public void Num_grupo_a_altura_NAO_se_soma()
        {
            // Três paredes de 2,80 m não fazem uma de 8,40. A altura só
            // aparece porque é a mesma nas duas; somá-la dava a contagem
            // disfarçada de medida.
            var artigo = ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos())
                         .Filhos[0].Filhos[0].Filhos[0];

            Assert.Equal(2.80, artigo.Altura, 2);
        }

        [Fact]
        public void Alturas_diferentes_no_mesmo_grupo_dao_um_traco()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Altura = 3.20;               // a 2A1 passa a ser mais alta

            var artigo = ResultadosArvoreTests.Arvore(paredes).Filhos[0].Filhos[0].Filhos[0];

            Assert.True(double.IsNaN(artigo.Altura));
            Assert.Equal("—", artigo.TextoAltura(Pt));
        }

        [Fact]
        public void Comprimentos_de_unidades_diferentes_nao_se_somam()
        {
            // Os metros de uma parede e os metros quadrados em planta de uma
            // camada não vão ao mesmo balde — é o mesmo erro das quantidades,
            // um nível abaixo.
            var parede = ResultadosArvoreTests.Parede("P", "PISO 0", "ALV", "1.1", 5.0, 2.0);
            var camada = ResultadosArvoreTests.Parede("C", "PISO 0", "ALV", "1.1", 1.0, 0.08);
            camada.Largura = 48.50;
            camada.AreaVezesAltura = true;

            var piso = ResultadosArvoreTests.Arvore(
                new List<Parede> { parede, camada }).Filhos[0];

            // O piso junta dois tipos com unidades de factor diferentes.
            Assert.True(double.IsNaN(piso.Comprimento));
            Assert.Equal("—", piso.TextoComprimento(Pt));
        }

        [Fact]
        public void Clonar_um_acumulador_da_uma_copia_independente()
        {
            var a = new Quantidades();
            a.Somar("m2", 1.0);

            var c = a.Clonar();
            c.Somar("m2", 5.0);

            Assert.Equal(1.0, a.De("m2"), 3);
            Assert.Equal(6.0, c.De("m2"), 3);
        }
    }
}
