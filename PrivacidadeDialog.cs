using System;
using System.Drawing;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Pergunta, uma vez, se pode enviar o registo de arranque.
    ///
    /// Aparece quando a pessoa abre o painel pela primeira vez, e não durante
    /// o arranque do AutoCAD: uma janela a saltar por cima de um arranque já
    /// lento é fechada no reflexo, sem ninguém a ler, e uma resposta dessas
    /// não é consentimento nenhum.
    ///
    /// Não há botão por omissão nem X que valha por "sim". Fechar a janela
    /// pela cruz deixa a pergunta por responder, e por responder significa
    /// não enviar — ver <see cref="Telemetria.RegistarSessao"/>.
    /// </summary>
    public class PrivacidadeDialog : Form
    {
        /// <summary>Resposta dada. <c>null</c> se fechou sem responder.</summary>
        public bool? Aceitou { get; private set; }

        public PrivacidadeDialog()
        {
            Text = "TSK TakeOff — dados de utilização";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(520, 400);

            var titulo = new Label
            {
                Text = "Podemos saber que o plugin arrancou?",
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                Location = new Point(16, 16),
                Size = new Size(488, 24)
            };

            var texto = new TextBox
            {
                Text = Telemetria.TextoExplicativo(),
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Location = new Point(16, 50),
                Size = new Size(488, 268),
                TabStop = false
            };

            var btnAceitar = new Button
            {
                Text = "Aceito enviar",
                Location = new Point(232, 336),
                Size = new Size(130, 30)
            };
            btnAceitar.Click += (s, e) => Responder(true);

            var btnRecusar = new Button
            {
                Text = "Não enviar",
                Location = new Point(374, 336),
                Size = new Size(130, 30)
            };
            btnRecusar.Click += (s, e) => Responder(false);

            // Escape = fechar sem responder, e não "recusar": só se regista uma
            // decisão que a pessoa tenha tomado de facto.
            CancelButton = null;

            Controls.Add(titulo);
            Controls.Add(texto);
            Controls.Add(btnAceitar);
            Controls.Add(btnRecusar);
        }

        private void Responder(bool aceita)
        {
            Aceitou = aceita;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>
        /// Mostra a pergunta se ainda não tiver sido feita, e arranca a
        /// telemetria caso a resposta seja sim. Seguro de chamar sempre que o
        /// painel abre: a partir da primeira resposta não faz nada.
        /// </summary>
        internal static void PerguntarSeNecessario()
        {
            try
            {
                if (Telemetria.Consentimento != Telemetria.Escolha.PorPerguntar)
                    return;

                using (var dlg = new PrivacidadeDialog())
                {
                    // Com dono, como todos os outros diálogos do plugin. Sem
                    // dono, a janela podia abrir atrás do AutoCAD em ecrã
                    // inteiro e ser impossível de encontrar — a pergunta ficava
                    // "por responder" e o utilizador achava que o plugin não
                    // funcionava.
                    AcadApp.ShowModalDialog(dlg);
                    if (!dlg.Aceitou.HasValue) return;      // fechou sem responder

                    Telemetria.GuardarEscolha(dlg.Aceitou.Value);
                    if (dlg.Aceitou.Value) Telemetria.RegistarSessao();
                }
            }
            catch { /* uma pergunta que falha não pode impedir o painel de abrir */ }
        }
    }
}
