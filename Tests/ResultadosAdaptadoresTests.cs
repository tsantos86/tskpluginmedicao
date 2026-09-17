using System.Collections.Generic;
using System.Globalization;
using TSKTakeOff;
using Xunit;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Os adaptadores das abas Materiais, Lineares e Contagens.
    ///
    /// O que aqui se defende é a UNIDADE de cada medição. Um pano fatura m², um
    /// rodapé fatura metro e uma porta fatura unidade — e o comprimento de um
    /// pano NÃO é a mesma coisa que o comprimento de um rodapé, mesmo sendo os
    /// dois metros. Um é dimensão e vive nas propriedades; o outro é a
    /// quantidade. Se os dois caírem no mesmo balde, o total soma coisas que se
    /// não somam e sai com ar de estar bem, que é o pior dos casos.
    /// </summary>
    public class ResultadosAdaptadoresTests
    {
        private static readonly CultureInfo Pt = new CultureInfo("pt-PT");

        // -----------------------------------------------------------------
        // Materiais
        // -----------------------------------------------------------------

        private static MedFachada Pano(string handle, string piso, string material,
                                       double comp, double alt)
        {
            return new MedFachada
            {
                Handle = handle,
                Piso = piso,
                Material = material,
                Comp = comp,
                Alt = alt,
                Area = comp * alt
            };
        }

        [Fact]
        public void Um_pano_fatura_m2_e_o_comprimento_dele_e_uma_dimensao()
        {
            var m = ResultadosAdaptadores.DeMateriais(
                new List<MedFachada> { Pano("F1", "PISO 0", "ETICS", 10.0, 2.5) }, null, Pt);

            Assert.Single(m);
            Assert.Equal("m2", m[0].Unidade);
            Assert.Equal(25.0, m[0].Quantidade, 3);

            // Os 10 m de comprimento estão nas propriedades, não na quantidade.
            Assert.Equal("10,00 m", m[0].Propriedades.Find(p => p.Campo == "comprimento").Valor);
        }

        [Fact]
        public void O_segundo_nivel_e_o_TIPO_DE_MEDIDA_nao_o_material()
        {
            // Mede-se arquitetura e mais nada: não há especialidades para
            // dividir. O que varia é COMO se mede, e é isso que determina a
            // unidade. O material continua a identificar o pano — mas nas
            // propriedades, que é onde sempre pertenceu.
            var raiz = ResultadosArvore.Construir(ResultadosAdaptadores.DeMateriais(
                new List<MedFachada> { Pano("F1", "PISO 0", "ETICS", 10.0, 2.5) }, null, Pt), Pt);

            var nivel = raiz.Filhos[0].Filhos[0];
            Assert.Equal(TipoNo.Tipo, nivel.Tipo);
            Assert.Equal("Materiais", nivel.TipoMedida);

            var med = ResultadosArvoreTests.Procurar(raiz, "M:F1");
            Assert.Equal("ETICS", med.Propriedades.Find(p => p.Campo == "servico").Valor);
        }

        [Fact]
        public void O_rotulo_do_tipo_traz_a_unidade_em_que_ele_mede()
        {
            // Ler "Alvenaria" sem saber em que se mede obriga a descer à
            // primeira linha para descobrir. O tipo É o que fixa a unidade,
            // por isso di-la no próprio nome.
            var raiz = ResultadosArvore.Construir(ResultadosAdaptadores.DeContagens(
                new List<MedContagem> { new MedContagem { Handle="C1", Nome="P.01", Piso="PISO 0" } },
                Pt), Pt);

            Assert.Equal("Contagens · un.", raiz.Filhos[0].Filhos[0].Rotulo);
        }

        [Fact]
        public void Uma_parede_e_uma_camada_sao_tipos_diferentes()
        {
            // Comprimento × altura dá m²; área em planta × espessura dá m³.
            // No mesmo grupo, o rótulo teria de mentir sobre uma das duas.
            var parede = ResultadosArvoreTests.Parede("P", "PISO 0", "ALV", "1.1", 5.0, 2.0);
            var camada = ResultadosArvoreTests.Parede("C", "PISO 0", "BET", "7.4", 1.0, 0.08);
            camada.Largura = 48.50;
            camada.AreaVezesAltura = true;

            var raiz = ResultadosArvore.DeAlvenaria(
                new List<Parede> { parede, camada }, RegraDesconto.DescontarTudo, null, Pt);

            var tipos = ResultadosArvore.TiposDeMedida(raiz);
            Assert.Equal(new List<string> { "Alvenaria", "Camadas" }, tipos);

            var piso = raiz.Filhos[0];
            Assert.Equal(2, piso.Filhos.Count);
            Assert.Equal("Alvenaria · m²", piso.Filhos[0].Rotulo);
            Assert.Equal("Camadas · m³", piso.Filhos[1].Rotulo);

            // E o piso soma-os lado a lado, nunca entre si.
            Assert.Equal(10.00, piso.Quantidades.De("m2"), 2);
            Assert.Equal(3.88, piso.Quantidades.De("m3"), 2);
        }

        [Fact]
        public void Os_vaos_de_um_pano_descontam_e_nao_contam_duas_vezes()
        {
            var f = Pano("F1", "PISO 0", "ETICS", 10.0, 2.0);          // 20 m²
            f.Vaos.Add(new Vao { Designacao = "J1", Largura = 2.0, Altura = 1.0, Quantidade = 1 });

            var raiz = ResultadosArvore.Construir(
                ResultadosAdaptadores.DeMateriais(new List<MedFachada> { f }, null, Pt), Pt);

            Assert.Equal(18.0, raiz.Quantidades.De("m2"), 3);          // 20 − 2
            var vao = ResultadosArvoreTests.Procurar(raiz, "M:F1/V:0");
            Assert.Equal(-2.0, vao.Quantidades.De("m2"), 3);
            Assert.False(vao.ContaParaTotal);
        }

        [Fact]
        public void Um_pano_com_vaos_maiores_do_que_ele_e_alerta()
        {
            var f = Pano("F1", "PISO 0", "ETICS", 2.0, 2.0);           // 4 m²
            f.Vaos.Add(new Vao { Largura = 3.0, Altura = 2.0, Quantidade = 1 });   // 6 m²

            var m = ResultadosAdaptadores.DeMateriais(new List<MedFachada> { f }, null, Pt);
            Assert.True((m[0].Alertas & AlertaNo.VaosExcessivos) != 0);
        }

        // -----------------------------------------------------------------
        // Lineares
        // -----------------------------------------------------------------

        [Fact]
        public void Um_linear_fatura_metro_e_o_comprimento_E_a_quantidade()
        {
            var m = ResultadosAdaptadores.DeLineares(new List<MedItem>
            {
                new MedItem { Handle = "L1", Categoria = "RODAPE", Comprimento = 29.15 }
            }, Pt);

            Assert.Single(m);
            Assert.Equal("m", m[0].Unidade);
            Assert.Equal(29.15, m[0].Quantidade, 3);
        }

        [Fact]
        public void O_comprimento_de_um_pano_e_o_de_um_linear_nao_se_somam()
        {
            // O caso que o brief nomeia. Os dois são metros; um é dimensão de
            // um artigo medido a m², o outro é a quantidade de um medido a
            // metro. Nunca vão ao mesmo balde.
            var medicoes = ResultadosAdaptadores.DeMateriais(
                new List<MedFachada> { Pano("F1", "PISO 0", "ETICS", 30.0, 2.0) }, null, Pt);
            medicoes.AddRange(ResultadosAdaptadores.DeLineares(new List<MedItem>
            {
                new MedItem { Handle = "L1", Categoria = "RODAPE", Comprimento = 29.15 }
            }, Pt));

            var raiz = ResultadosArvore.Construir(medicoes, Pt);

            Assert.Equal(60.0, raiz.Quantidades.De("m2"), 2);   // 30 × 2
            Assert.Equal(29.15, raiz.Quantidades.De("m"), 2);   // só o rodapé
            Assert.Equal(2, raiz.Quantidades.Count);
        }

        [Fact]
        public void Um_linear_sem_piso_cai_no_grupo_Sem_piso()
        {
            var raiz = ResultadosArvore.Construir(ResultadosAdaptadores.DeLineares(
                new List<MedItem> { new MedItem { Handle = "L1", Comprimento = 5.0 } }, Pt), Pt);

            Assert.Equal(ResultadosArvore.SemPiso, raiz.Filhos[0].Rotulo);
        }

        // -----------------------------------------------------------------
        // Contagens
        // -----------------------------------------------------------------

        [Fact]
        public void Cada_contagem_vale_uma_unidade_e_o_grupo_e_que_soma()
        {
            var raiz = ResultadosArvore.Construir(ResultadosAdaptadores.DeContagens(
                new List<MedContagem>
                {
                    new MedContagem { Handle = "C1", Nome = "P.01", Piso = "PISO 0", Categoria = "CARPINTARIAS" },
                    new MedContagem { Handle = "C2", Nome = "P.02", Piso = "PISO 0", Categoria = "CARPINTARIAS" },
                }, Pt), Pt);

            Assert.Equal(2.0, raiz.Quantidades.De("un"), 3);
            Assert.Equal("2 un.", raiz.Quantidades.Texto(Pt));
        }

        [Fact]
        public void O_nome_da_contagem_e_o_rotulo_dela()
        {
            var m = ResultadosAdaptadores.DeContagens(new List<MedContagem>
            {
                new MedContagem { Handle = "C1", Nome = "VE.10" }
            }, Pt);

            Assert.Equal("VE.10", m[0].Rotulo);
        }

        [Fact]
        public void Uma_contagem_sem_nome_ainda_se_identifica_pelo_handle()
        {
            var m = ResultadosAdaptadores.DeContagens(new List<MedContagem>
            {
                new MedContagem { Handle = "C9", Nome = "" }
            }, Pt);

            Assert.Equal("Contagem C9", m[0].Rotulo);
        }

        // -----------------------------------------------------------------
        // As três juntas
        // -----------------------------------------------------------------

        [Fact]
        public void As_tres_unidades_aparecem_lado_a_lado_e_nunca_somadas()
        {
            var medicoes = ResultadosAdaptadores.DeMateriais(
                new List<MedFachada> { Pano("F1", "PISO 0", "ETICS", 10.0, 2.0) }, null, Pt);
            medicoes.AddRange(ResultadosAdaptadores.DeLineares(new List<MedItem>
            {
                new MedItem { Handle = "L1", Categoria = "RODAPE", Comprimento = 36.90 }
            }, Pt));
            medicoes.AddRange(ResultadosAdaptadores.DeContagens(new List<MedContagem>
            {
                new MedContagem { Handle = "C1", Nome = "P.01" },
                new MedContagem { Handle = "C2", Nome = "P.02" },
            }, Pt));

            var raiz = ResultadosArvore.Construir(medicoes, Pt);
            Assert.Equal("20,00 m² · 36,90 m · 2 un.", raiz.Quantidades.Texto(Pt));
        }

        [Fact]
        public void Listas_nulas_ou_vazias_nao_rebentam()
        {
            Assert.Empty(ResultadosAdaptadores.DeMateriais(null, null, Pt));
            Assert.Empty(ResultadosAdaptadores.DeLineares(null, Pt));
            Assert.Empty(ResultadosAdaptadores.DeContagens(null, Pt));

            Assert.Empty(ResultadosAdaptadores.DeMateriais(new List<MedFachada>(), null, Pt));
            Assert.Empty(ResultadosAdaptadores.DeLineares(new List<MedItem>(), Pt));
            Assert.Empty(ResultadosAdaptadores.DeContagens(new List<MedContagem>(), Pt));
        }

        // -----------------------------------------------------------------
        // Editável quer dizer que ALGUÉM o grava
        // -----------------------------------------------------------------

        /// <summary>
        /// Um campo marcado como editável ganha realce amarelo e deixa
        /// escrever. Se não houver método no repositório que o grave, a
        /// escrita não vai a lado nenhum — e um campo que promete gravar e não
        /// grava é pior do que um campo bloqueado, porque quem o usa fica
        /// convencido de que alterou.
        ///
        /// Estes testes fixam o que cada repositório sabe mesmo fazer.
        /// </summary>
        [Fact]
        public void Num_pano_so_o_artigo_se_edita()
        {
            // O FacRepo tem DefinirArtigo. Não tem DefinirMaterial nem
            // DefinirPiso — por isso esses são de leitura.
            var m = ResultadosAdaptadores.DeMateriais(
                new List<MedFachada> { Pano("F1", "PISO 0", "ETICS", 10.0, 2.5) }, null, Pt);

            Assert.True(m[0].Propriedades.Find(p => p.Campo == "artigo").Editavel);
            Assert.False(m[0].Propriedades.Find(p => p.Campo == "servico").Editavel);
            Assert.False(m[0].Propriedades.Find(p => p.Campo == "piso").Editavel);
        }

        [Fact]
        public void Numa_contagem_nao_se_edita_nada()
        {
            // O ContRepo cria e apaga contagens, mas não altera uma já feita.
            var m = ResultadosAdaptadores.DeContagens(new List<MedContagem>
            {
                new MedContagem { Handle = "C1", Nome = "P.01", Categoria = "CARP", Piso = "PISO 0" }
            }, Pt);

            foreach (var p in m[0].Propriedades)
                Assert.False(p.Editavel, "não devia ser editável: " + p.Campo);
        }

        [Fact]
        public void Num_linear_nao_se_edita_nada()
        {
            var m = ResultadosAdaptadores.DeLineares(new List<MedItem>
            {
                new MedItem { Handle = "L1", Categoria = "RODAPE", Comprimento = 5.0 }
            }, Pt);

            foreach (var p in m[0].Propriedades)
                Assert.False(p.Editavel, "não devia ser editável: " + p.Campo);
        }

        [Fact]
        public void As_dimensoes_e_os_calculos_sao_sempre_de_leitura()
        {
            var m = ResultadosAdaptadores.DeMateriais(
                new List<MedFachada> { Pano("F1", "PISO 0", "ETICS", 10.0, 2.5) }, null, Pt);

            foreach (var campo in new[] { "comprimento", "altura", "areaBruta",
                                          "desconto", "quantidade" })
            {
                var p = m[0].Propriedades.Find(x => x.Campo == campo);
                Assert.NotNull(p);
                Assert.False(p.Editavel, campo + " vem da geometria ou é uma conta");
            }
        }

        [Fact]
        public void Os_Ids_das_outras_abas_sao_o_handle_como_na_alvenaria()
        {
            var raiz = ResultadosArvore.Construir(ResultadosAdaptadores.DeContagens(
                new List<MedContagem> { new MedContagem { Handle = "C1", Nome = "P.01" } }, Pt), Pt);

            Assert.NotNull(ResultadosArvoreTests.Procurar(raiz, "M:C1"));
        }
    }
}
