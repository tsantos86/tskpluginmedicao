using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// Escolher um artigo do mapa de quantidades para medições já feitas.
    ///
    /// Existe porque classificar era a única operação da paleta que obrigava a
    /// ir a outro sítio primeiro: escolher o artigo no topo do painel, voltar à
    /// grelha, seleccionar, e só então carregar num botão. Quem estava a olhar
    /// para uma linha laranja a dizer que lhe faltava o artigo não tinha ali
    /// nada que dissesse por onde se resolvia.
    ///
    /// Agora é o contrário: a caixa vem ter com a medição. A célula «classificar»
    /// abre-a para aquela linha, e o botão Reclassificar abre-a para a selecção
    /// toda — a mesma caixa nos dois casos, para não haver duas maneiras de
    /// fazer a mesma coisa com resultados diferentes.
    /// </summary>
    public class ArtigoDialog : Form
    {
        private readonly TextBox _procura;
        private readonly ListBox _lista;
        private readonly Label _conta;

        /// <summary>O artigo escolhido, ou null se a caixa foi cancelada.</summary>
        public MapaQuantidades.No Escolhido { get; private set; }

        /// <param name="quantas">Quantas medições vão mudar de artigo.</param>
        /// <param name="actual">Artigo a deixar pré-seleccionado, ou "".</param>
        public ArtigoDialog(int quantas, string actual)
        {
            Text = "TSK TakeOff — classificar medições";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(820, 520);
            MinimizeBox = false;
            MaximizeBox = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            // Num portátil a 150% a caixa vinha com o conteúdo maior do que ela
            // própria — é o mesmo cuidado que a caixa de importação do mapa tem.
            MinimumSize = new Size(560, 380);
            AutoScaleMode = AutoScaleMode.Dpi;

            var topo = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(10, 8, 10, 0),
                Text = quantas == 1
                    ? "Escolha o artigo do mapa a que esta medição pertence."
                    : quantas + " medições seleccionadas. Escolha o artigo do mapa " +
                      "a que passam a pertencer.\nA geometria não se toca — muda " +
                      "só onde saem na folha."
            };

            var rotProcura = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Padding = new Padding(10, 0, 10, 0),
                Text = "Procurar (parte do código, parte da designação, ou os dois):"
            };

            _procura = new TextBox { Dock = DockStyle.Top, Margin = new Padding(10) };
            _procura.TextChanged += (s, e) => Encher();

            _conta = new Label
            {
                Dock = DockStyle.Bottom,
                Height = 20,
                Padding = new Padding(10, 0, 10, 0),
                ForeColor = Color.FromArgb(90, 90, 90)
            };

            _lista = new ListBox
            {
                Dock = DockStyle.Fill,
                IntegralHeight = false,
                // Monoespaçada: os códigos alinham-se e a árvore lê-se de
                // relance, como na pré-visualização da importação.
                Font = new Font(FontFamily.GenericMonospace, 8.75f)
            };

            var ok = new Button
            {
                Text = "Classificar",
                DialogResult = DialogResult.OK,
                Size = new Size(130, 34)
            };
            var cancelar = new Button
            {
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                Size = new Size(120, 34),
                Margin = new Padding(8, 0, 0, 0)
            };

            // Duplo clique na lista vale por Classificar: é o gesto que toda a
            // gente tenta primeiro numa lista de escolha.
            _lista.DoubleClick += (s, e) =>
            {
                if (_lista.SelectedItem == null) return;
                DialogResult = DialogResult.OK;
                Close();
            };

            // Fluxo da direita para a esquerda, como nas outras caixas: com
            // coordenadas fixas os botões saltavam do sítio ao redimensionar.
            var baixo = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(10, 10, 12, 10)
            };
            baixo.Controls.Add(cancelar);
            baixo.Controls.Add(ok);

            Controls.Add(_lista);
            Controls.Add(_conta);
            Controls.Add(baixo);
            Controls.Add(_procura);
            Controls.Add(rotProcura);
            Controls.Add(topo);

            AcceptButton = ok;
            CancelButton = cancelar;

            Encher();
            PreSeleccionar(actual);

            // O cursor começa na procura: escreve-se logo o código, sem clicar.
            Shown += (s, e) => { try { _procura.Focus(); } catch { } };
            FormClosing += (s, e) =>
            {
                if (DialogResult == DialogResult.OK)
                    Escolhido = _lista.SelectedItem as MapaQuantidades.No;
            };
        }

        /// <summary>
        /// Enche a lista com os artigos que casam com o que está escrito.
        ///
        /// Só artigos: um capítulo existe para dar contexto no mapa, mas não se
        /// mede num capítulo, e oferecê-lo aqui era oferecer um destino que
        /// depois dava um total sem sentido na folha.
        /// </summary>
        private void Encher()
        {
            List<MapaQuantidades.No> achados;
            try { achados = MapaQuantidades.Filtrar(_procura.Text, true); }
            catch { achados = new List<MapaQuantidades.No>(); }

            // Guardar a escolha: refiltrar a cada tecla não pode fazer perder o
            // artigo que já estava marcado se ele continuar na lista.
            var antes = _lista.SelectedItem as MapaQuantidades.No;

            _lista.BeginUpdate();
            try
            {
                _lista.Items.Clear();
                foreach (var n in achados) _lista.Items.Add(n);

                if (achados.Count == 0)
                    _conta.Text = "Nenhum artigo com «" + (_procura.Text ?? "").Trim() + "».";
                else
                    _conta.Text = achados.Count + " artigo(s). Duplo clique escolhe.";

                if (antes != null)
                {
                    int i = _lista.Items.IndexOf(antes);
                    if (i >= 0) _lista.SelectedIndex = i;
                }
                if (_lista.SelectedIndex < 0 && _lista.Items.Count > 0)
                    _lista.SelectedIndex = 0;
            }
            finally { _lista.EndUpdate(); }
        }

        private void PreSeleccionar(string chave)
        {
            if (string.IsNullOrEmpty(chave)) return;
            for (int i = 0; i < _lista.Items.Count; i++)
            {
                var n = _lista.Items[i] as MapaQuantidades.No;
                if (n != null && n.Chave == chave) { _lista.SelectedIndex = i; return; }
            }
        }
    }
}
