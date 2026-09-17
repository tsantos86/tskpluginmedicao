using System.Collections.Generic;
using System.Globalization;
using TSKTakeOff;
using Xunit;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Pesquisa e filtros: o que se vê, e o que NÃO acontece por se ver menos.
    ///
    /// Um filtro é uma lente, não uma operação. Não toca no desenho, não toca
    /// na folha e não muda a próxima medição. O que ele pode estragar é mais
    /// subtil: deixar uma acção apontada a uma linha que já não está à vista —
    /// e aí o "Remover" apaga uma medição que quem carregou no botão não estava
    /// sequer a ver.
    /// </summary>
    public class ResultadosFiltroTests
    {
        private static readonly CultureInfo Pt = new CultureInfo("pt-PT");

        private static NoResultado Arvore()
        {
            return ResultadosArvoreTests.Arvore(ResultadosArvoreTests.DoisPisos());
        }

        private static List<string> Rotulos(VistaResultados v)
        {
            var l = new List<string>();
            foreach (var n in v.Nos) l.Add(n.No.Rotulo);
            return l;
        }

        private static bool Mostra(VistaResultados v, string rotulo)
        {
            return Rotulos(v).Contains(rotulo);
        }

        // -----------------------------------------------------------------
        // Sem filtro
        // -----------------------------------------------------------------

        [Fact]
        public void Sem_filtro_ve_se_a_arvore_toda()
        {
            var vista = ResultadosArvore.Projetar(Arvore(), new EstadoVista());

            Assert.False(vista.AFiltrar);
            Assert.False(vista.Vazia);
            Assert.True(Mostra(vista, "Pavimentos"));
            Assert.True(Mostra(vista, "PISO 0"));
            Assert.True(Mostra(vista, "PISO 1"));
            Assert.True(Mostra(vista, "Parede 2A0"));
            Assert.True(Mostra(vista, "Vão · P01"));
        }

        [Fact]
        public void Um_estado_nulo_nao_rebenta()
        {
            Assert.False(ResultadosArvore.Projetar(Arvore(), null).Vazia);
        }

        [Fact]
        public void Uma_arvore_nula_da_uma_vista_vazia()
        {
            Assert.True(ResultadosArvore.Projetar(null, new EstadoVista()).Vazia);
        }

        // -----------------------------------------------------------------
        // Recolher
        // -----------------------------------------------------------------

        [Fact]
        public void Recolher_esconde_os_filhos_e_mais_nada()
        {
            var raiz = Arvore();
            var estado = new EstadoVista();
            estado.Recolher(raiz.Filhos[0].Id);          // PISO 0

            var vista = ResultadosArvore.Projetar(raiz, estado);

            Assert.True(Mostra(vista, "PISO 0"));
            Assert.False(Mostra(vista, "Parede 2A0"));
            Assert.True(Mostra(vista, "PISO 1"));        // o irmão fica
        }

        [Fact]
        public void Recolher_nao_e_filtrar_a_contagem_nao_muda()
        {
            var raiz = Arvore();
            var estado = new EstadoVista();
            estado.Recolher(raiz.Filhos[0].Id);

            var vista = ResultadosArvore.Projetar(raiz, estado);

            Assert.Equal(3, vista.MedicoesVisiveis);
            Assert.Equal(3, vista.MedicoesTotais);
            Assert.False(vista.AFiltrar);
        }

        [Fact]
        public void Alternar_abre_e_fecha()
        {
            var estado = new EstadoVista();
            Assert.True(estado.Expandido("x"));
            estado.Alternar("x");
            Assert.False(estado.Expandido("x"));
            estado.Alternar("x");
            Assert.True(estado.Expandido("x"));
        }

        // -----------------------------------------------------------------
        // Pesquisa
        // -----------------------------------------------------------------

        [Fact]
        public void Pesquisar_mostra_o_resultado_e_os_antepassados_dele()
        {
            var estado = new EstadoVista { Pesquisa = "2A2" };
            var vista = ResultadosArvore.Projetar(Arvore(), estado);

            Assert.True(Mostra(vista, "Parede 2A2"));
            Assert.True(Mostra(vista, "PISO 1"));        // o caminho até lá
            Assert.True(Mostra(vista, "Pavimentos"));
            Assert.False(Mostra(vista, "Parede 2A0"));   // e mais nada
            Assert.False(Mostra(vista, "PISO 0"));
        }

        [Fact]
        public void Pesquisar_um_grupo_mostra_o_que_ele_tem_por_baixo()
        {
            var vista = ResultadosArvore.Projetar(
                Arvore(), new EstadoVista { Pesquisa = "PISO 1" });

            Assert.True(Mostra(vista, "PISO 1"));
            Assert.True(Mostra(vista, "Parede 2A2"));
        }

        [Fact]
        public void A_pesquisa_ignora_acentos_e_maiusculas()
        {
            var p = ResultadosArvoreTests.Parede("A", "PISO 0", "ALVENARIA", "1.1", 2.0, 2.0);
            p.Alcado = "Alçado Tardoz";
            var raiz = ResultadosArvoreTests.Arvore(new List<Parede> { p });

            foreach (var termo in new[] { "alcado", "ALÇADO", "tardoz", "TaRdOz" })
            {
                var vista = ResultadosArvore.Projetar(raiz, new EstadoVista { Pesquisa = termo });
                Assert.True(Mostra(vista, "Parede A"), "falhou com: " + termo);
            }
        }

        [Fact]
        public void Varios_termos_sao_um_E_nao_um_OU()
        {
            var raiz = Arvore();

            Assert.True(Mostra(ResultadosArvore.Projetar(
                raiz, new EstadoVista { Pesquisa = "parede 2A1" }), "Parede 2A1"));

            // "2A1" existe e "tardoz" não: o conjunto é vazio.
            Assert.True(ResultadosArvore.Projetar(
                raiz, new EstadoVista { Pesquisa = "2A1 tardoz" }).Vazia);
        }

        [Fact]
        public void Procura_se_pelo_handle_pela_nota_e_pela_designacao_do_vao()
        {
            var raiz = Arvore();

            Assert.True(Mostra(ResultadosArvore.Projetar(
                raiz, new EstadoVista { Pesquisa = "2a0" }), "Parede 2A0"));

            // O vão "P01" traz a parede consigo — é o antepassado dele.
            var porVao = ResultadosArvore.Projetar(raiz, new EstadoVista { Pesquisa = "P01" });
            Assert.True(Mostra(porVao, "Vão · P01"));
            Assert.True(Mostra(porVao, "Parede 2A0"));
        }

        [Fact]
        public void Procura_se_pelo_servico_e_pelo_artigo()
        {
            var raiz = Arvore();

            Assert.False(ResultadosArvore.Projetar(
                raiz, new EstadoVista { Pesquisa = "alvenaria" }).Vazia);
            Assert.False(ResultadosArvore.Projetar(
                raiz, new EstadoVista { Pesquisa = "11.2.1" }).Vazia);
            Assert.False(ResultadosArvore.Projetar(
                raiz, new EstadoVista { Pesquisa = "interior" }).Vazia);
        }

        [Fact]
        public void Sem_correspondencia_a_vista_fica_vazia()
        {
            var vista = ResultadosArvore.Projetar(
                Arvore(), new EstadoVista { Pesquisa = "não existe nada assim" });

            Assert.True(vista.Vazia);
            Assert.Equal(0, vista.MedicoesVisiveis);
            Assert.Equal(3, vista.MedicoesTotais);      // o total continua a ser o de tudo
        }

        [Fact]
        public void A_pesquisa_ignora_as_recolhas_que_esconderiam_o_resultado()
        {
            var raiz = Arvore();
            var estado = new EstadoVista();
            estado.Recolher(raiz.Filhos[1].Id);          // fecha o PISO 1
            estado.Pesquisa = "2A2";                     // e procura lá dentro

            var vista = ResultadosArvore.Projetar(raiz, estado);

            Assert.True(Mostra(vista, "Parede 2A2"));
            // A recolha não se perdeu: só foi ignorada enquanto durar a procura.
            Assert.Contains(raiz.Filhos[1].Id, estado.Recolhidos);
        }

        [Fact]
        public void Limpar_repoe_a_vista_sem_desfazer_as_recolhas()
        {
            var raiz = Arvore();
            var estado = new EstadoVista();
            estado.Recolher(raiz.Filhos[0].Id);
            var antes = Rotulos(ResultadosArvore.Projetar(raiz, estado));

            estado.Pesquisa = "2A2";
            estado.Filtro.Pavimentos.Add("PISO 1");
            ResultadosArvore.Projetar(raiz, estado);

            estado.LimparVista();
            var depois = Rotulos(ResultadosArvore.Projetar(raiz, estado));

            Assert.Equal(antes, depois);
        }

        // -----------------------------------------------------------------
        // Filtros
        // -----------------------------------------------------------------

        [Fact]
        public void Filtrar_por_pavimento()
        {
            var estado = new EstadoVista();
            estado.Filtro.Pavimentos.Add("PISO 1");

            var vista = ResultadosArvore.Projetar(Arvore(), estado);

            Assert.True(Mostra(vista, "PISO 1"));
            Assert.False(Mostra(vista, "PISO 0"));
            Assert.Equal(1, vista.MedicoesVisiveis);
            Assert.Equal(3, vista.MedicoesTotais);
        }

        [Fact]
        public void Filtrar_por_artigo_usa_o_codigo()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Artigo = "12.1.3Rodapé";

            var estado = new EstadoVista();
            estado.Filtro.Artigos.Add("12.1.3");

            var vista = ResultadosArvore.Projetar(ResultadosArvoreTests.Arvore(paredes), estado);

            Assert.Equal(1, vista.MedicoesVisiveis);
            Assert.True(Mostra(vista, "Parede 2A1"));
        }

        [Fact]
        public void Filtrar_por_artigo_apanha_o_que_esta_por_classificar()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Artigo = "";

            var estado = new EstadoVista();
            estado.Filtro.Artigos.Add(ResultadosArvore.PorClassificar);

            var vista = ResultadosArvore.Projetar(ResultadosArvoreTests.Arvore(paredes), estado);

            Assert.Equal(1, vista.MedicoesVisiveis);
            Assert.True(Mostra(vista, "Parede 2A1"));
        }

        [Fact]
        public void Filtrar_por_servico()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[2].Servico = "REBOCO";

            var estado = new EstadoVista();
            estado.Filtro.Servicos.Add("REBOCO");

            var vista = ResultadosArvore.Projetar(ResultadosArvoreTests.Arvore(paredes), estado);

            Assert.Equal(1, vista.MedicoesVisiveis);
            Assert.True(Mostra(vista, "Parede 2A2"));
        }

        [Fact]
        public void Filtrar_por_unidade_separa_paredes_de_camadas()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[2].AreaVezesAltura = true;            // passa a faturar m³

            var estado = new EstadoVista();
            estado.Filtro.UnidadesFiltradas.Add("m3");

            var vista = ResultadosArvore.Projetar(ResultadosArvoreTests.Arvore(paredes), estado);

            Assert.Equal(1, vista.MedicoesVisiveis);
            Assert.True(Mostra(vista, "Camada 2A2"));
        }

        [Fact]
        public void Filtrar_por_estado_Com_vaos()
        {
            var estado = new EstadoVista();
            estado.Filtro.Estados = EstadoResultado.ComVaos;

            var vista = ResultadosArvore.Projetar(Arvore(), estado);

            Assert.Equal(1, vista.MedicoesVisiveis);     // só a 2A0 tem vão
            Assert.True(Mostra(vista, "Parede 2A0"));
        }

        [Fact]
        public void Filtrar_por_estado_Por_classificar()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Artigo = "";
            var raiz = ResultadosArvore.DeAlvenaria(
                paredes, RegraDesconto.DescontarTudo, a => true, Pt);

            var estado = new EstadoVista();
            estado.Filtro.Estados = EstadoResultado.PorClassificar;

            var vista = ResultadosArvore.Projetar(raiz, estado);

            Assert.Equal(1, vista.MedicoesVisiveis);
            Assert.True(Mostra(vista, "Parede 2A1"));
        }

        [Fact]
        public void Filtrar_por_estado_Com_alerta()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Vaos.Add(new Vao { Largura = 9.0, Altura = 9.0, Quantidade = 1 });

            var estado = new EstadoVista();
            estado.Filtro.Estados = EstadoResultado.ComAlerta;

            var vista = ResultadosArvore.Projetar(ResultadosArvoreTests.Arvore(paredes), estado);

            Assert.Equal(1, vista.MedicoesVisiveis);
            Assert.True(Mostra(vista, "Parede 2A1"));
        }

        [Fact]
        public void Varios_estados_sao_um_OU()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Artigo = "";                                   // por classificar
            var raiz = ResultadosArvore.DeAlvenaria(
                paredes, RegraDesconto.DescontarTudo, a => true, Pt);

            var estado = new EstadoVista();
            estado.Filtro.Estados = EstadoResultado.PorClassificar | EstadoResultado.ComVaos;

            var vista = ResultadosArvore.Projetar(raiz, estado);

            Assert.Equal(2, vista.MedicoesVisiveis);                  // 2A0 (vão) e 2A1
        }

        [Fact]
        public void Grupos_diferentes_sao_um_E()
        {
            // PISO 1 E com vãos: a 2A2 é do PISO 1 mas não tem vãos.
            var estado = new EstadoVista();
            estado.Filtro.Pavimentos.Add("PISO 1");
            estado.Filtro.Estados = EstadoResultado.ComVaos;

            Assert.True(ResultadosArvore.Projetar(Arvore(), estado).Vazia);
        }

        [Fact]
        public void Pesquisa_e_filtro_combinam_se_e_o_filtro_manda()
        {
            // A 2A0 corresponde à pesquisa, mas o filtro do PISO 1 tirou-a.
            var estado = new EstadoVista { Pesquisa = "2A0" };
            estado.Filtro.Pavimentos.Add("PISO 1");

            Assert.True(ResultadosArvore.Projetar(Arvore(), estado).Vazia);
        }

        [Fact]
        public void Um_grupo_que_fica_sem_filhos_desaparece_com_eles()
        {
            var estado = new EstadoVista();
            estado.Filtro.Pavimentos.Add("PISO 1");

            var vista = ResultadosArvore.Projetar(Arvore(), estado);

            // Nada de um "PISO 0" vazio a dizer 0,00 m² só para ocupar a lista.
            Assert.False(Mostra(vista, "PISO 0"));
        }

        [Fact]
        public void Filtrar_nao_muda_a_arvore_de_origem()
        {
            var raiz = Arvore();
            var antes = raiz.Quantidades.Texto(Pt);
            int filhosAntes = raiz.Filhos.Count;

            var estado = new EstadoVista { Pesquisa = "2A2" };
            estado.Filtro.Pavimentos.Add("PISO 1");
            ResultadosArvore.Projetar(raiz, estado);

            Assert.Equal(antes, raiz.Quantidades.Texto(Pt));
            Assert.Equal(filhosAntes, raiz.Filhos.Count);
            // E o "Limpar tudo" continua a ver os três handles da fonte.
            Assert.Equal(3, ResultadosArvore.Handles(raiz).Count);
        }

        [Fact]
        public void Uma_seleccao_escondida_pelo_filtro_deixa_de_estar_na_vista()
        {
            // É este null que a paleta usa para limpar a selecção e desactivar
            // as acções. Sem ele, o "Remover" ficava apontado a uma linha que
            // já ninguém está a ver.
            var raiz = Arvore();
            var med = ResultadosArvoreTests.Procurar(raiz, "M:2A0");

            var estado = new EstadoVista { Seleccionado = med.Id };
            estado.Filtro.Pavimentos.Add("PISO 1");

            var vista = ResultadosArvore.Projetar(raiz, estado);

            Assert.Null(vista.Procurar(med.Id));
            Assert.Equal(-1, vista.Indice(med.Id));
        }

        [Fact]
        public void Uma_seleccao_dentro_de_um_grupo_recolhido_tambem_sai_da_vista()
        {
            var raiz = Arvore();
            var estado = new EstadoVista();
            estado.Recolher(raiz.Filhos[0].Id);

            var vista = ResultadosArvore.Projetar(raiz, estado);
            Assert.Null(vista.Procurar("M:2A0"));
        }

        // -----------------------------------------------------------------
        // Badge, chips e rascunho
        // -----------------------------------------------------------------

        [Fact]
        public void O_badge_conta_grupos_de_filtro_nao_valores()
        {
            var f = new FiltroResultados();
            Assert.True(f.Vazio);
            Assert.Equal(0, f.Contagem);

            f.Pavimentos.Add("PISO 0");
            f.Pavimentos.Add("PISO 1");
            Assert.Equal(1, f.Contagem);          // três pisos continuam a ser um filtro

            f.Estados = EstadoResultado.ComVaos;
            Assert.Equal(2, f.Contagem);
            Assert.False(f.Vazio);
        }

        [Fact]
        public void Os_chips_dizem_o_valor_quando_e_um_so()
        {
            var f = new FiltroResultados();
            f.Pavimentos.Add("PISO 0");
            f.Estados = EstadoResultado.ComVaos;

            var chips = f.Chips();
            Assert.Equal(2, chips.Count);
            Assert.Equal("Pavimento: PISO 0", chips[0]);
            Assert.Equal("Estado: Com vãos", chips[1]);
        }

        [Fact]
        public void O_rascunho_e_uma_copia_e_so_o_Aplicar_o_promove()
        {
            var aplicado = new FiltroResultados();
            aplicado.Pavimentos.Add("PISO 0");

            var rascunho = aplicado.Clonar();
            rascunho.Pavimentos.Add("PISO 1");
            rascunho.Estados = EstadoResultado.ComVaos;

            Assert.Single(aplicado.Pavimentos);
            Assert.Equal(EstadoResultado.Nenhum, aplicado.Estados);
            Assert.Equal(2, rascunho.Pavimentos.Count);
        }

        [Fact]
        public void Limpar_o_filtro_esvazia_todos_os_grupos()
        {
            var f = new FiltroResultados();
            f.Pavimentos.Add("PISO 0");
            f.Servicos.Add("ALVENARIA");
            f.Artigos.Add("1.1");
            f.UnidadesFiltradas.Add("m2");
            f.Estados = EstadoResultado.ComAlerta;

            f.Limpar();
            Assert.True(f.Vazio);
        }

        // -----------------------------------------------------------------
        // Os valores que os filtros oferecem
        // -----------------------------------------------------------------

        [Fact]
        public void Os_pavimentos_oferecidos_sao_os_que_existem_no_desenho()
        {
            // Um filtro que ofereça um piso que não existe só produz vistas
            // vazias, e quem o escolhe fica a pensar que perdeu medições.
            Assert.Equal(new List<string> { "PISO 0", "PISO 1" },
                         ResultadosArvore.Pavimentos(Arvore()));
        }

        [Fact]
        public void A_ordem_dos_valores_e_a_da_arvore_nao_alfabetica()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[2].Servico = "ALVENARIA";
            paredes[0].Servico = "REBOCO";

            var servicos = ResultadosArvore.Servicos(ResultadosArvoreTests.Arvore(paredes));
            Assert.Equal("REBOCO", servicos[0]);      // por nome viria a seguir
        }

        [Fact]
        public void Os_artigos_oferecidos_sao_codigos_e_incluem_Por_classificar()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Artigo = "";

            var artigos = ResultadosArvore.Artigos(ResultadosArvoreTests.Arvore(paredes));

            Assert.Contains("11.2.1", artigos);
            Assert.Contains(ResultadosArvore.PorClassificar, artigos);
            Assert.Equal(2, artigos.Count);
        }

        [Fact]
        public void So_se_oferecem_as_unidades_que_existem()
        {
            Assert.Equal(new List<string> { "m2" },
                         ResultadosArvore.UnidadesPresentes(Arvore()));

            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[2].AreaVezesAltura = true;
            Assert.Equal(new List<string> { "m2", "m3" },
                ResultadosArvore.UnidadesPresentes(ResultadosArvoreTests.Arvore(paredes)));
        }

        [Fact]
        public void So_se_oferecem_os_estados_que_existem()
        {
            // A fixture tem um vão e nada por classificar nem com alerta.
            Assert.Equal(EstadoResultado.ComVaos,
                         ResultadosArvore.EstadosPresentes(Arvore()));
        }

        [Fact]
        public void Por_classificar_so_e_um_estado_oferecido_havendo_mapa()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            paredes[1].Artigo = "";

            // Sem mapa não há alerta nenhum, logo o estado não se oferece.
            Assert.False((ResultadosArvore.EstadosPresentes(
                ResultadosArvoreTests.Arvore(paredes))
                & EstadoResultado.PorClassificar) != 0);

            // Com mapa, oferece-se.
            var comMapa = ResultadosArvore.DeAlvenaria(
                paredes, RegraDesconto.DescontarTudo, a => true, Pt);
            Assert.True((ResultadosArvore.EstadosPresentes(comMapa)
                & EstadoResultado.PorClassificar) != 0);
        }

        [Fact]
        public void Uma_arvore_vazia_nao_oferece_valores_nenhuns()
        {
            var raiz = ResultadosArvoreTests.Arvore(new List<Parede>());
            Assert.Empty(ResultadosArvore.Pavimentos(raiz));
            Assert.Empty(ResultadosArvore.Servicos(raiz));
            Assert.Empty(ResultadosArvore.Artigos(raiz));
            Assert.Empty(ResultadosArvore.UnidadesPresentes(raiz));
            Assert.Equal(EstadoResultado.Nenhum, ResultadosArvore.EstadosPresentes(raiz));
        }

        [Fact]
        public void Uma_arvore_nula_nao_rebenta_a_recolher_valores()
        {
            Assert.Empty(ResultadosArvore.Pavimentos(null));
            Assert.Empty(ResultadosArvore.UnidadesPresentes(null));
            Assert.Equal(EstadoResultado.Nenhum, ResultadosArvore.EstadosPresentes(null));
        }

        // -----------------------------------------------------------------
        // As cinco combinações, determinísticas
        // -----------------------------------------------------------------

        [Fact]
        public void Os_cinco_filtros_ao_mesmo_tempo_sao_determinísticos()
        {
            var paredes = ResultadosArvoreTests.DoisPisos();
            var raiz = ResultadosArvore.DeAlvenaria(
                paredes, RegraDesconto.DescontarTudo, a => true, Pt);

            var estado = new EstadoVista();
            estado.Filtro.Pavimentos.Add("PISO 0");
            estado.Filtro.Servicos.Add("ALVENARIA");
            estado.Filtro.Artigos.Add("11.2.1");
            estado.Filtro.UnidadesFiltradas.Add("m2");
            estado.Filtro.Estados = EstadoResultado.ComVaos;

            // Só a 2A0 satisfaz os cinco.
            var a = ResultadosArvore.Projetar(raiz, estado);
            Assert.Equal(1, a.MedicoesVisiveis);
            Assert.True(Mostra(a, "Parede 2A0"));

            // E repetir dá exactamente o mesmo.
            var b = ResultadosArvore.Projetar(raiz, estado);
            Assert.Equal(Rotulos(a), Rotulos(b));
        }

        [Fact]
        public void Tirar_um_filtro_de_cinco_alarga_a_vista()
        {
            var raiz = Arvore();
            var estado = new EstadoVista();
            estado.Filtro.Pavimentos.Add("PISO 0");
            estado.Filtro.Estados = EstadoResultado.ComVaos;
            Assert.Equal(1, ResultadosArvore.Projetar(raiz, estado).MedicoesVisiveis);

            estado.Filtro.Estados = EstadoResultado.Nenhum;
            Assert.Equal(2, ResultadosArvore.Projetar(raiz, estado).MedicoesVisiveis);
        }
    }
}
