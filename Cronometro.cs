using System.Collections.Generic;
using System.Diagnostics;

namespace TSKTakeOff
{
    /// <summary>
    /// Mede quanto demora cada fase de uma actualização e diz onde se foi o
    /// tempo.
    ///
    /// Existe porque optimizar sem medir é adivinhar, e adivinhar sai caro:
    /// já se corrigiram várias coisas que eram mesmo lentas sem que nenhuma
    /// delas fosse A lenta. Só é impresso quando o total passa do limiar, para
    /// não encher a linha de comando durante o trabalho normal.
    /// </summary>
    public sealed class Cronometro
    {
        /// <summary>Acima disto (ms) a actualização é considerada lenta e é reportada.</summary>
        public static int LimiarMs = 250;

        /// <summary>Imprime sempre, mesmo abaixo do limiar. Ligado por TSKTEMPO.</summary>
        public static bool Sempre = false;

        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly Stopwatch _fase = Stopwatch.StartNew();
        private readonly List<KeyValuePair<string, long>> _fases =
            new List<KeyValuePair<string, long>>();
        private readonly string _titulo;

        public Cronometro(string titulo) { _titulo = titulo; }

        /// <summary>Fecha a fase corrente com este nome e começa a seguinte.</summary>
        public void Marcar(string fase)
        {
            _fases.Add(new KeyValuePair<string, long>(fase, _fase.ElapsedMilliseconds));
            _fase.Restart();
        }

        /// <summary>Detalhe extra dentro de uma fase (contagens, por exemplo).</summary>
        public void Nota(string texto)
        {
            _fases.Add(new KeyValuePair<string, long>("  " + texto, -1));
        }

        /// <summary>Fecha e reporta, se valer a pena.</summary>
        public void Fim()
        {
            long total = _total.ElapsedMilliseconds;
            if (!Sempre && total < LimiarMs) return;

            var sb = new System.Text.StringBuilder();
            sb.Append(_titulo + ": " + total + " ms");
            foreach (var f in _fases)
            {
                if (f.Value < 0) { sb.Append("\n   " + f.Key); continue; }
                if (f.Value == 0) continue;          // ruído
                sb.Append("\n   " + f.Key.PadRight(22) + f.Value + " ms");
            }
            PaletteHost.Log(sb.ToString());
        }
    }
}
