using System;

namespace TSKTakeOff
{
    /// <summary>
    /// A chave de um artigo — código + designação — na forma em que viaja
    /// dentro da XData.
    ///
    /// O limite de 255 caracteres aplica-se ao texto inteiro, não apenas à
    /// designação. Portanto, para uma chave como
    /// <c>2.2 + separador + designação</c>, só cabem 251 caracteres da
    /// designação. O mapa guarda a designação no seu próprio campo e pode
    /// conservar 255; a chave que dele resulta fica, por isso, quatro
    /// caracteres maior do que a chave gravada na entidade.
    ///
    /// A comparação tem de reproduzir o corte da XData nos dois lados. Não se
    /// pode aceitar simplesmente qualquer prefixo: com o mesmo código,
    /// "Parede" e "Parede dupla" são artigos diferentes quando nenhum deles
    /// foi cortado.
    /// </summary>
    public static class ChaveArtigo
    {
        /// <summary>
        /// Separador entre código e designação. O mesmo 0x1F de sempre,
        /// escrito em escape porque em cru é invisível no editor.
        /// </summary>
        public const string Sep = "\u001f";

        /// <summary>Limite total de um texto de XData.</summary>
        public const int LimiteXData = 255;

        /// <summary>
        /// A parte do código. Numa chave sem separador — coisa que só
        /// aparece em desenhos muito antigos — vale a chave toda.
        /// </summary>
        public static string Codigo(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return "";
            int i = chave.IndexOf(Sep, StringComparison.Ordinal);
            return i < 0 ? chave : chave.Substring(0, i);
        }

        /// <summary>A parte da designação, ou vazio se não houver separador.</summary>
        public static string Designacao(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return "";
            int i = chave.IndexOf(Sep, StringComparison.Ordinal);
            return i < 0 ? "" : chave.Substring(i + Sep.Length);
        }

        /// <summary>
        /// Devolve a chave como ela pode existir dentro de um único texto de
        /// XData: no máximo 255 caracteres no total.
        /// </summary>
        public static string Truncar(string chave)
        {
            if (chave == null) return null;
            return chave.Length <= LimiteXData
                ? chave
                : chave.Substring(0, LimiteXData);
        }

        /// <summary>
        /// Podem estas duas chaves ser o mesmo artigo depois de a XData as
        /// guardar?
        ///
        /// O código continua a ter de ser exactamente igual. Depois, ambas as
        /// chaves são reduzidas ao mesmo limite total de 255 caracteres e só
        /// então comparadas. Assim uma chave da entidade com 251 caracteres
        /// de designação coincide com a chave do mapa cuja designação tem 255,
        /// sem que uma designação curta seja tratada como prefixo de outra.
        ///
        /// Se dois artigos com o mesmo código forem iguais nos primeiros 255
        /// caracteres, a XData não conserva informação para os distinguir.
        /// Nesse caso esta função só pode dizer que as formas armazenadas são
        /// compatíveis; a decisão de não escolher arbitrariamente entre dois
        /// artigos continua a pertencer ao chamador.
        /// </summary>
        public static bool Compativel(string guardada, string doMapa)
        {
            if (guardada == null || doMapa == null) return false;
            return string.Equals(Normalizada(guardada), Normalizada(doMapa),
                StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// A chave reduzida à forma que o <see cref="Compativel"/> compara:
        /// código, separador, e a chave truncada ao limite da XData.
        ///
        /// Existe para o mapa poder INDEXAR em vez de percorrer. O
        /// MapaQuantidades.Procurar comparava a chave de cada medição contra
        /// todos os nós do articulado, e a grelha da paleta chama-o uma vez
        /// por linha: com duzentas medições e um articulado de dois mil
        /// artigos são quatrocentas mil comparações por actualização, cada uma
        /// a cortar duas cadeias. Com esta forma como chave de dicionário,
        /// passa a ser uma consulta.
        ///
        /// TEM DE VIVER AO LADO DO Compativel, e é por isso que o Compativel
        /// passou a ser escrito à custa dela: se as duas regras divergirem, o
        /// índice deixa de encontrar o que a comparação encontraria, e o
        /// sintoma seria medições a ficarem órfãs sem explicação.
        ///
        /// O código nunca contém o separador — o <see cref="Codigo"/> devolve
        /// o que está ANTES dele — por isso a junção não é ambígua: o primeiro
        /// separador da chave composta marca sempre a mesma fronteira.
        /// </summary>
        public static string Normalizada(string chave)
        {
            if (chave == null) return null;
            return Codigo(chave) + Sep + Truncar(chave);
        }
    }
}
