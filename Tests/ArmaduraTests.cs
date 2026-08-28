using Xunit;
using TSKTakeOff;

namespace TSKTakeOff.Tests
{
    /// <summary>
    /// Leitura das descrições de armadura.
    ///
    /// O caso que dá razão a este ficheiro é o "C=350": lido como 350 metros
    /// em vez de 3,50, o aço de uma laje passa de duas toneladas para duzentas
    /// — e o número sai no mapa de quantidades com ar de estar bem. É por isso
    /// que os limites de plausibilidade estão testados um a um.
    /// </summary>
    public class ArmaduraTests
    {
        private static Armadura.Leitura Ler(string texto)
        {
            Armadura.Leitura l;
            Armadura.Falha f;
            Assert.True(Armadura.Ler(texto, out l, out f),
                        "não devia falhar: «" + texto + "» → " + f);
            return l;
        }

        private static Armadura.Falha Recusa(string texto)
        {
            Armadura.Leitura l;
            Armadura.Falha f;
            Assert.False(Armadura.Ler(texto, out l, out f),
                         "devia falhar: «" + texto + "»");
            Assert.Null(l);
            return f;
        }

        // ==================================================================
        // Formatos que os desenhos trazem
        // ==================================================================

        [Theory]
        // Formato A — Ø com C= decimal. O "c/20" é o AFASTAMENTO entre varões
        // e não pode ser confundido com o comprimento.
        [InlineData("15 Ø 16 c/20 C=3.50", 15, 16, 3.50)]
        [InlineData("15Ø16 C=3.50m", 15, 16, 3.50)]
        // Formato B — a letra substitui o Ø, e o comprimento vem em cm.
        [InlineData("15 T 16 C=350", 15, 16, 3.50)]
        [InlineData("15N16 L=350", 15, 16, 3.50)]
        // Formato C — "x" na quantidade e comprimento atrás de traço, em cm.
        [InlineData("12xØ12 - 400", 12, 12, 4.00)]
        public void Le_os_formatos_do_desenho(string texto, int qtd, int diam, double comp)
        {
            var l = Ler(texto);
            Assert.Equal(qtd, l.Quantidade);
            Assert.Equal(diam, l.DiametroMm);
            Assert.Equal(comp, l.ComprimentoM, 3);
        }

        [Theory]
        [InlineData("20 Ø 10 C=350cm", 3.50)]
        [InlineData("20 Ø 10 C=3500mm", 3.50)]
        [InlineData("20 Ø 10 C=3,50", 3.50)]      // vírgula decimal
        [InlineData("20 Ø 10 C=3.50 m", 3.50)]
        public void Unidade_escrita_manda_sempre(string texto, double esperado)
        {
            Assert.Equal(esperado, Ler(texto).ComprimentoM, 3);
        }

        [Fact]
        public void O_afastamento_nao_e_lido_como_comprimento()
        {
            // Se o c/20 passasse por comprimento, dava 0,20 m — dezassete
            // vezes menos aço do que o desenho manda.
            var l = Ler("15 Ø 16 c/20 C=3.50");
            Assert.Equal(3.50, l.ComprimentoM, 3);
        }

        [Fact]
        public void Diametro_de_tres_digitos_nao_e_truncado()
        {
            // "Ø160" não pode virar Ø16 — sairia uma medição com o diâmetro
            // errado e ninguém daria por ela. O (?!\d) da cabeça recusa a
            // leitura inteira, e a falha é "não encontrei diâmetro" e não
            // "diâmetro sem peso": o que lá está não chega a ser um diâmetro.
            Assert.Equal(Armadura.Falha.SemDiametro, Recusa("10 Ø160 C=3.50"));
        }

        // ==================================================================
        // Peso
        // ==================================================================

        [Theory]
        [InlineData("15 Ø 16 C=3.50", 82.845)]    // 15 × 3,50 × 1,578
        [InlineData("10 Ø 8 C=2.00", 7.900)]      // 10 × 2,00 × 0,395
        [InlineData("4 Ø 32 C=6.00", 151.512)]    // 4 × 6,00 × 6,313
        public void Peso_e_quantidade_vezes_comprimento_vezes_nominal(string texto, double kg)
        {
            Assert.Equal(kg, Armadura.PesoKg(Ler(texto)), 3);
        }

        [Fact]
        public void Metros_lineares_sao_quantidade_vezes_comprimento()
        {
            Assert.Equal(52.5, Ler("15 Ø 16 C=3.50").MetrosLineares, 3);
        }

        [Fact]
        public void Todos_os_diametros_da_tabela_sao_lidos()
        {
            foreach (var d in Armadura.PesoPorMetro.Keys)
            {
                var l = Ler("10 Ø" + d + " C=2.00");
                Assert.Equal(d, l.DiametroMm);
                Assert.True(Armadura.PesoKg(l) > 0);
            }
        }

        // ==================================================================
        // O que tem de ser recusado
        // ==================================================================

        [Fact] public void Texto_vazio()
            => Assert.Equal(Armadura.Falha.TextoVazio, Recusa(""));

        [Fact] public void Texto_em_branco()
            => Assert.Equal(Armadura.Falha.TextoVazio, Recusa("   "));

        [Fact] public void Sem_quantidade()
            => Assert.Equal(Armadura.Falha.SemQuantidade, Recusa("Ø C="));

        [Fact] public void Sem_diametro()
            => Assert.Equal(Armadura.Falha.SemDiametro, Recusa("15 varões C=3.50"));

        [Fact] public void Sem_comprimento()
            => Assert.Equal(Armadura.Falha.SemComprimento, Recusa("15 Ø 16 c/20"));

        [Fact] public void Diametro_fora_da_tabela()
            => Assert.Equal(Armadura.Falha.DiametroNaoSuportado, Recusa("15 Ø 14 C=3.50"));

        [Fact] public void Comprimento_absurdo_e_recusado()
            => Assert.Equal(Armadura.Falha.ComprimentoForaDeLimites, Recusa("15 Ø 16 C=350m"));

        [Fact] public void Comprimento_curto_de_mais_e_recusado()
            => Assert.Equal(Armadura.Falha.ComprimentoForaDeLimites, Recusa("15 Ø 16 C=5mm"));

        [Fact] public void Texto_sem_numeros_nenhuns()
            => Assert.Equal(Armadura.Falha.SemQuantidade, Recusa("ARMADURA SUPERIOR"));

        [Fact]
        public void Nada_de_util_nao_deixa_leitura_meia_feita()
        {
            Armadura.Leitura l;
            Armadura.Falha f;
            Assert.False(Armadura.Ler("PILAR P3", out l, out f));
            Assert.Null(l);
            Assert.NotEqual(Armadura.Falha.Nenhuma, f);
        }

        [Fact]
        public void Cada_falha_tem_explicacao_para_a_linha_de_comandos()
        {
            foreach (Armadura.Falha f in System.Enum.GetValues(typeof(Armadura.Falha)))
            {
                if (f == Armadura.Falha.Nenhuma) continue;
                Assert.False(string.IsNullOrWhiteSpace(Armadura.Explicar(f)),
                             "sem explicação: " + f);
            }
        }

        // ==================================================================
        // A regra da unidade implícita — é aqui que mora o erro caro
        // ==================================================================

        [Theory]
        [InlineData("10 Ø 12 C=350", 3.50)]    // sem decimal → cm
        [InlineData("10 Ø 12 C=3.50", 3.50)]   // com decimal → m
        [InlineData("10 Ø 12 C=1200", 12.00)]  // varão comercial inteiro, em cm
        public void Sem_unidade_o_decimal_decide(string texto, double esperado)
        {
            Assert.Equal(esperado, Ler(texto).ComprimentoM, 3);
        }

        [Fact]
        public void Trezentos_e_cinquenta_nunca_e_trezentos_e_cinquenta_metros()
        {
            Assert.Equal(3.50, Ler("15 T 16 C=350").ComprimentoM, 3);
        }

        // ==================================================================
        // MALHA DISTRIBUÍDA — o que os desenhos reais trazem
        //
        // Textos retirados das plantas do Casquilho Poente (EST-00-03-030 e
        // -031, Piso 1 ao Piso 7, armaduras inferiores e superiores). Não têm
        // quantidade nenhuma: têm diâmetro e afastamento, e a quantidade sai
        // da área que a malha cobre.
        // ==================================================================

        private static Armadura.Malha Malha(string texto)
        {
            Armadura.Malha m;
            Armadura.Falha f;
            Assert.True(Armadura.LerMalha(texto, out m, out f),
                        "não devia falhar: «" + texto + "» → " + f);
            return m;
        }

        [Theory]
        [InlineData("Ø8//0.125", 8, 0.125)]
        [InlineData("Ø10//0.125 m", 10, 0.125)]
        [InlineData("Ø12//0.125 m", 12, 0.125)]
        [InlineData("Ø16//0.125 m", 16, 0.125)]
        public void Le_a_malha_das_plantas_de_laje(string texto, int diam, double esp)
        {
            var m = Malha(texto);
            Assert.Equal(diam, m.DiametroMm);
            Assert.Equal(esp, m.AfastamentoM, 4);
        }

        [Theory]
        [InlineData("Ø8//0.125", 3.160)]     // 8 m/m² × 0,395
        [InlineData("Ø10//0.125", 4.936)]    // 8 m/m² × 0,617
        [InlineData("Ø12//0.125", 7.104)]    // 8 m/m² × 0,888
        [InlineData("Ø16//0.125", 12.624)]   // 8 m/m² × 1,578
        public void Consumo_por_m2_e_o_inverso_do_afastamento_vezes_o_nominal(
            string texto, double kgm2)
        {
            Assert.Equal(kgm2, Malha(texto).KgPorM2, 3);
        }

        [Fact]
        public void Oito_varoes_por_metro_a_doze_e_meio_centimetros()
        {
            Assert.Equal(8.0, Malha("Ø12//0.125").MetrosPorM2, 4);
        }

        [Fact]
        public void Peso_de_uma_zona_e_a_area_vezes_o_consumo()
        {
            // Uma laje de 100 m² com Ø12//0.125 numa direcção.
            Assert.Equal(710.4, Malha("Ø12//0.125 m").PesoKg(100), 1);
        }

        [Fact]
        public void As_quatro_malhas_de_uma_laje_somam()
        {
            // INF1 + INF2 + SUP1 + SUP2 de 100 m², como nos desenhos.
            double total =
                Malha("Ø10//0.125 m").PesoKg(100) + Malha("Ø10//0.125 m").PesoKg(100) +
                Malha("Ø12//0.125 m").PesoKg(100) + Malha("Ø12//0.125 m").PesoKg(100);
            Assert.Equal(2408.0, total, 1);
        }

        [Theory]
        [InlineData("Ø12//12.5cm", 0.125)]
        [InlineData("Ø12//125mm", 0.125)]
        [InlineData("Ø12//0,125", 0.125)]    // vírgula decimal
        [InlineData("Ø12 c/ 0.125", 0.125)]  // afastamento à americana
        [InlineData("Ø12//12", 0.12)]        // sem decimal → cm
        public void Afastamento_normaliza_para_metros(string texto, double esp)
        {
            Assert.Equal(esp, Malha(texto).AfastamentoM, 4);
        }

        [Fact]
        public void Malha_sem_afastamento_e_recusada()
        {
            Armadura.Malha m; Armadura.Falha f;
            Assert.False(Armadura.LerMalha("Ø12", out m, out f));
            Assert.Equal(Armadura.Falha.SemAfastamento, f);
        }

        [Fact]
        public void Afastamento_absurdo_e_recusado()
        {
            // "//125" em metros seria 125 m entre varões.
            Armadura.Malha m; Armadura.Falha f;
            Assert.False(Armadura.LerMalha("Ø12//125m", out m, out f));
            Assert.Equal(Armadura.Falha.AfastamentoForaDeLimites, f);
        }

        [Fact]
        public void Malha_com_diametro_fora_da_tabela_e_recusada()
        {
            Armadura.Malha m; Armadura.Falha f;
            Assert.False(Armadura.LerMalha("Ø14//0.125", out m, out f));
            Assert.Equal(Armadura.Falha.DiametroNaoSuportado, f);
        }

        [Fact]
        public void Texto_corrido_da_nota_do_desenho_nao_engana()
        {
            // Frase real da nota das plantas. Tem um Ø8//0.125 lá dentro e é
            // isso que se quer — o resto da frase não pode estorvar.
            var m = Malha("deverá ser disposta uma malha de armadura mínima de Ø8//0.125");
            Assert.Equal(8, m.DiametroMm);
            Assert.Equal(0.125, m.AfastamentoM, 4);
        }
    }
}
