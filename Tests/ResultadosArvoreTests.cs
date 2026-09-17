using System.Collections.Generic;
using System.Globalization;
using TSKTakeOff;
using Xunit;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// A hierarquia Pavimentos > Piso > Serviço > Artigo > Medição > Vão/Título
    /// e a identidade dos nós.
    ///
    /// O que aqui se defende é aquilo que a grelha antiga não conseguia
    /// prometer: que o alvo de um botão continua a ser a mesma medição depois
    /// de a lista ser reconstruída. Ela guardava o índice da linha, e a lista é
    /// refeita a CADA medição — a selecção escorregava para outra parede sem
    /// ninguém lhe tocar, e o "Remover" apagava a errada.
    /// </summary>
    public class ResultadosArvoreTests
    {
        private static readonly CultureInfo Pt = new CultureInfo("pt-PT");

        internal static Parede Parede(string handle, string piso, string servico,
                                      string artigo, double comp, double altura)
        {
            return new Parede
            {
                Handle = handle,
                Piso = piso,
                Servico = servico,
                Artigo = artigo,
                Comprimento = comp,
                Altura = altura,
                Espessura = 0.15
            };
        }

        /// <summary>Fixture com dois pisos, como o plano exige.</summary>
        internal static List<Parede> DoisPisos()
        {
            var a = Parede("2A0", "PISO 0", "ALVENARIA", "11.2.1\u001fParede interior", 5.20, 2.80);
            a.Vaos.Add(new Vao { Designacao = "P01", Largura = 0.90, Altura = 2.10, Quantidade = 1 });

            var b = Parede("2A1", "PISO 0", "ALVENARIA", "11.2.1\u001fParede interior", 3.45, 2.80);
            var c = Parede("2A2", "PISO 1", "ALVENARIA", "11.2.1\u001fParede interior", 4.00, 2.80);
            return new List<Parede> { a, b, c };
        }

        internal static NoResultado Arvore(IEnumerable<Parede> paredes)
        {
            return ResultadosArvore.DeAlvenaria(
                paredes, RegraDesconto.DescontarTudo, null, Pt);
        }

        internal static NoResultado Procurar(NoResultado no, string id)
        {
            if (no.Id == id) return no;
            foreach (var f in no.Filhos)
            {
                var r = Procurar(f, id);
                if (r != null) return r;
            }
            return null;
        }

        private static NoResultado PorRotulo(NoResultado no, TipoNo tipo, string rotulo)
        {
            if (no.Tipo == tipo && no.Rotulo == rotulo) return no;
            foreach (var f in no.Filhos)
            {
                var r = PorRotulo(f, tipo, rotulo);
                if (r != null) return r;
            }
            return null;
        }

        // -----------------------------------------------------------------
        // Hierarquia
        // -----------------------------------------------------------------

        [Fact]
        public void A_raiz_e_Pavimentos_e_tem_os_dois_pisos()
        {
            var raiz = Arvore(DoisPisos());

            Assert.Equal(TipoNo.Raiz, raiz.Tipo);
            Assert.Equal("Pavimentos", raiz.Rotulo);
            Assert.Equal(2, raiz.Filhos.Count);
            Assert.Equal("PISO 0", raiz.Filhos[0].Rotulo);
            Assert.Equal("PISO 1", raiz.Filhos[1].Rotulo);
        }

        [Fact]
        public void A_ordem_dos_niveis_e_a_aprovada()
        {
            var raiz = Arvore(DoisPisos());

            var piso = raiz.Filhos[0];
            var servico = piso.Filhos[0];
            var artigo = servico.Filhos[0];
            var medicao = artigo.Filhos[0];
            var vao = medicao.Filhos[0];

            Assert.Equal(TipoNo.Piso, piso.Tipo);
            Assert.Equal(TipoNo.Tipo, servico.Tipo);
            Assert.Equal(TipoNo.Artigo, artigo.Tipo);
            Assert.Equal(TipoNo.Medicao, medicao.Tipo);
            Assert.Equal(TipoNo.Vao, vao.Tipo);

            // A indentação da grelha sai daqui.
            Assert.Equal(0, raiz.Profundidade);
            Assert.Equal(5, vao.Profundidade);
        }

        [Fact]
        public void Piso_vazio_da_o_grupo_Sem_piso()
        {
            var raiz = Arvore(new List<Parede> { Parede("A", "", "ALVENARIA", "1.1", 2.0, 2.0) });
            Assert.Equal(ResultadosArvore.SemPiso, raiz.Filhos[0].Rotulo);
        }

        [Fact]
        public void Sem_artigo_da_o_grupo_Por_classificar()
        {
            var raiz = Arvore(new List<Parede> { Parede("A", "PISO 0", "ALVENARIA", "", 2.0, 2.0) });
            var artigo = raiz.Filhos[0].Filhos[0].Filhos[0];

            Assert.Equal(TipoNo.Artigo, artigo.Tipo);
            Assert.Equal(ResultadosArvore.PorClassificar, artigo.Rotulo);
        }

        [Fact]
        public void Os_grupos_residuais_ficam_no_fim_dos_irmaos()
        {
            // Medida primeiro, mas o que falta classificar lê-se no fim.
            var paredes = new List<Parede>
            {
                Parede("A", "PISO 0", "ALVENARIA", "", 2.0, 2.0),
                Parede("B", "PISO 0", "ALVENARIA", "11.2.1\u001fParede", 3.0, 2.0),
            };

            var servico = Arvore(paredes).Filhos[0].Filhos[0];
            Assert.Equal(2, servico.Filhos.Count);
            Assert.Equal(ResultadosArvore.PorClassificar, servico.Filhos[1].Rotulo);
        }

        [Fact]
        public void Medicoes_do_mesmo_artigo_caem_no_mesmo_grupo()
        {
            var artigo = Arvore(DoisPisos()).Filhos[0].Filhos[0].Filhos[0];
            Assert.Equal(2, artigo.Filhos.Count);   // 2A0 e 2A1
        }

        // -----------------------------------------------------------------
        // Identidade
        // -----------------------------------------------------------------

        [Fact]
        public void Os_nos_de_grupo_nao_tem_handle()
        {
            var raiz = Arvore(DoisPisos());

            Assert.Null(raiz.Handle);
            Assert.Null(raiz.Filhos[0].Handle);
            Assert.Null(raiz.Filhos[0].Filhos[0].Handle);
            Assert.Null(raiz.Filhos[0].Filhos[0].Filhos[0].Handle);
        }

        [Fact]
        public void O_Id_de_uma_medicao_e_so_o_handle()
        {
            var med = PorRotulo(Arvore(DoisPisos()), TipoNo.Medicao, "Parede 2A0");
            Assert.Equal("M:2A0", med.Id);
            Assert.Equal("2A0", med.Handle);
        }

        [Fact]
        public void Reclassificar_uma_medicao_nao_lhe_muda_o_Id()
        {
            // É por isto que o Id não traz o caminho: quem estava a editar uma
            // parede e lhe muda o artigo não pode perder a selecção por causa
            // disso.
            var antes = Arvore(DoisPisos());
            var idAntes = PorRotulo(antes, TipoNo.Medicao, "Parede 2A0").Id;

            var paredes = DoisPisos();
            paredes[0].Artigo = "14.1.1\u001fOutro artigo";
            paredes[0].Piso = "PISO 3";
            var depois = Arvore(paredes);

            Assert.Equal(idAntes, PorRotulo(depois, TipoNo.Medicao, "Parede 2A0").Id);
        }

        [Fact]
        public void Os_Ids_sao_estaveis_ao_reconstruir_a_arvore()
        {
            var a = Ids(Arvore(DoisPisos()));
            var b = Ids(Arvore(DoisPisos()));
            Assert.Equal(a, b);
        }

        [Fact]
        public void Os_Ids_sobrevivem_a_recolher_e_filtrar()
        {
            var raiz = Arvore(DoisPisos());
            var estado = new EstadoVista();
            estado.Recolher(raiz.Filhos[0].Id);
            estado.Pesquisa = "piso 1";
            ResultadosArvore.Projetar(raiz, estado);

            estado.LimparVista();
            var reconstruida = Arvore(DoisPisos());
            var vista = ResultadosArvore.Projetar(reconstruida, estado);

            // O piso que ficou recolhido continua a ser o mesmo nó, e continua
            // recolhido depois de a árvore ser refeita.
            var piso0 = vista.Procurar(reconstruida.Filhos[0].Id);
            Assert.NotNull(piso0);
            Assert.False(piso0.Expandido);
        }

        private static List<string> Ids(NoResultado no)
        {
            var l = new List<string> { no.Id };
            foreach (var f in no.Filhos) l.AddRange(Ids(f));
            return l;
        }

        // -----------------------------------------------------------------
        // Vãos e títulos
        // -----------------------------------------------------------------

        [Fact]
        public void O_vao_aparece_por_baixo_da_medicao_como_deducao_negativa()
        {
            var med = PorRotulo(Arvore(DoisPisos()), TipoNo.Medicao, "Parede 2A0");
            var vao = med.Filhos[0];

            Assert.Equal(TipoNo.Vao, vao.Tipo);
            Assert.Equal("Vão · P01", vao.Rotulo);
            Assert.Equal(-1.89, vao.Quantidades.De("m2"), 3);   // 0,90 × 2,10
        }

        [Fact]
        public void O_vao_herda_o_handle_da_parede_e_o_indice()
        {
            var med = PorRotulo(Arvore(DoisPisos()), TipoNo.Medicao, "Parede 2A0");
            var vao = med.Filhos[0];

            Assert.Equal("2A0", vao.Handle);
            Assert.Equal(0, vao.Indice);
            Assert.Equal("M:2A0/V:0", vao.Id);
        }

        [Fact]
        public void O_titulo_e_um_no_identificavel_sem_total_nenhum()
        {
            var p = Parede("A", "PISO 0", "ALVENARIA", "1.1", 2.0, 2.0);
            p.AlternarMarca("CAP");
            p.DefinirTextoDaMarca(0, "7\u001fALVENARIAS");

            var med = PorRotulo(Arvore(new List<Parede> { p }), TipoNo.Medicao, "Parede A");
            var titulo = med.Filhos[0];

            Assert.Equal(TipoNo.Titulo, titulo.Tipo);
            Assert.Equal("M:A/T:0", titulo.Id);
            Assert.Contains("Capítulo", titulo.Rotulo);
            // Sem total falso: um título é texto que sai na folha.
            Assert.True(titulo.Quantidades.Vazio);
            Assert.False(titulo.ContaParaTotal);
        }

        // -----------------------------------------------------------------
        // Medir aqui
        // -----------------------------------------------------------------

        [Fact]
        public void Medir_aqui_so_existe_nos_nos_de_artigo()
        {
            var raiz = Arvore(DoisPisos());
            var piso = raiz.Filhos[0];
            var servico = piso.Filhos[0];
            var artigo = servico.Filhos[0];
            var med = artigo.Filhos[0];

            Assert.False(raiz.PermiteMedirAqui);
            Assert.False(piso.PermiteMedirAqui);
            Assert.False(servico.PermiteMedirAqui);
            Assert.True(artigo.PermiteMedirAqui);
            Assert.False(med.PermiteMedirAqui);
            Assert.False(med.Filhos[0].PermiteMedirAqui);
        }

        [Fact]
        public void O_no_de_artigo_leva_consigo_piso_servico_e_artigo()
        {
            // É isto que o "Medir aqui" copia para o Config, e nada mais.
            var artigo = Arvore(DoisPisos()).Filhos[1].Filhos[0].Filhos[0];

            Assert.Equal("PISO 1", artigo.Piso);
            Assert.Equal("ALVENARIA", artigo.Servico);
            Assert.Equal("11.2.1\u001fParede interior", artigo.Artigo);
        }

        // -----------------------------------------------------------------
        // Alertas
        // -----------------------------------------------------------------

        [Fact]
        public void Sem_mapa_nao_ter_artigo_nao_e_alerta()
        {
            var raiz = ResultadosArvore.DeAlvenaria(
                new List<Parede> { Parede("A", "PISO 0", "ALV", "", 2.0, 2.0) },
                RegraDesconto.DescontarTudo, null, Pt);

            Assert.Equal(AlertaNo.Nenhum, PorRotulo(raiz, TipoNo.Medicao, "Parede A").Alertas);
        }

        [Fact]
        public void Com_mapa_o_que_nao_tem_artigo_fica_por_classificar()
        {
            var raiz = ResultadosArvore.DeAlvenaria(
                new List<Parede> { Parede("A", "PISO 0", "ALV", "", 2.0, 2.0) },
                RegraDesconto.DescontarTudo, a => true, Pt);

            var med = PorRotulo(raiz, TipoNo.Medicao, "Parede A");
            Assert.True((med.Alertas & AlertaNo.PorClassificar) != 0);
        }

        [Fact]
        public void Um_artigo_que_o_mapa_nao_conhece_e_um_alerta_diferente()
        {
            var raiz = ResultadosArvore.DeAlvenaria(
                new List<Parede> { Parede("A", "PISO 0", "ALV", "9.9.9\u001fFantasma", 2.0, 2.0) },
                RegraDesconto.DescontarTudo, a => false, Pt);

            var med = PorRotulo(raiz, TipoNo.Medicao, "Parede A");
            Assert.True((med.Alertas & AlertaNo.ArtigoDesconhecido) != 0);
            Assert.True((med.Alertas & AlertaNo.PorClassificar) == 0);
        }

        [Fact]
        public void Vaos_a_descontar_mais_do_que_a_parede_tem_e_alerta()
        {
            var p = Parede("A", "PISO 0", "ALV", "1.1", 2.0, 2.0);   // 4 m²
            p.Vaos.Add(new Vao { Largura = 3.0, Altura = 2.0, Quantidade = 1 });  // 6 m²

            var med = PorRotulo(Arvore(new List<Parede> { p }), TipoNo.Medicao, "Parede A");
            Assert.True((med.Alertas & AlertaNo.VaosExcessivos) != 0);
        }

        [Fact]
        public void Os_alertas_sobem_para_o_grupo()
        {
            // Um piso recolhido tem de conseguir dizer que tem qualquer coisa
            // por classificar lá dentro sem obrigar a abri-lo.
            var raiz = ResultadosArvore.DeAlvenaria(
                new List<Parede> { Parede("A", "PISO 0", "ALV", "", 2.0, 2.0) },
                RegraDesconto.DescontarTudo, a => true, Pt);

            Assert.True((raiz.Filhos[0].Alertas & AlertaNo.PorClassificar) != 0);
            Assert.True((raiz.Alertas & AlertaNo.PorClassificar) != 0);
        }

        // -----------------------------------------------------------------
        // Rótulos e propriedades
        // -----------------------------------------------------------------

        [Fact]
        public void A_nota_escrita_a_mao_ganha_ao_nome_generico()
        {
            var p = Parede("A", "PISO 0", "ALV", "1.1", 2.0, 2.0);
            p.Nota = "Empena norte";

            Assert.NotNull(PorRotulo(Arvore(new List<Parede> { p }), TipoNo.Medicao, "Empena norte"));
        }

        [Fact]
        public void As_dimensoes_vivem_nas_propriedades_nao_na_arvore()
        {
            var med = PorRotulo(Arvore(DoisPisos()), TipoNo.Medicao, "Parede 2A0");

            // A coluna da árvore só tem quantidade, e a quantidade é uma só.
            Assert.Single(med.Quantidades.Lista());

            Assert.Contains(med.Propriedades, x => x.Campo == "comprimento");
            Assert.Contains(med.Propriedades, x => x.Campo == "altura");
            Assert.Contains(med.Propriedades, x => x.Campo == "espessura");
        }

        [Fact]
        public void O_comprimento_e_de_leitura_e_o_artigo_e_editavel()
        {
            var med = PorRotulo(Arvore(DoisPisos()), TipoNo.Medicao, "Parede 2A0");

            Assert.False(med.Propriedades.Find(x => x.Campo == "comprimento").Editavel);
            Assert.True(med.Propriedades.Find(x => x.Campo == "artigo").Editavel);
            Assert.False(med.Propriedades.Find(x => x.Campo == "quantidade").Editavel);
        }

        [Fact]
        public void Um_grupo_diz_quantas_medicoes_tem()
        {
            var piso0 = Arvore(DoisPisos()).Filhos[0];
            Assert.Equal("2", piso0.Propriedades.Find(x => x.Campo == "medicoes").Valor);
        }

        // -----------------------------------------------------------------
        // Handles da fonte (para o "Limpar tudo")
        // -----------------------------------------------------------------

        [Fact]
        public void Handles_da_fonte_sao_distintos_e_pela_ordem_da_arvore()
        {
            var handles = ResultadosArvore.Handles(Arvore(DoisPisos()));
            Assert.Equal(new List<string> { "2A0", "2A1", "2A2" }, handles);
        }

        [Fact]
        public void Uma_lista_vazia_da_uma_arvore_so_com_a_raiz()
        {
            var raiz = Arvore(new List<Parede>());
            Assert.Empty(raiz.Filhos);
            Assert.True(raiz.Quantidades.Vazio);
        }

        [Fact]
        public void Uma_lista_nula_nao_rebenta()
        {
            Assert.NotNull(ResultadosArvore.DeAlvenaria(null, RegraDesconto.DescontarTudo));
        }
    }
}
