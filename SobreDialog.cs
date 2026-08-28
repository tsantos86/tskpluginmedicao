using System;
using System.Drawing;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Janela "Sobre": quem fez o plugin, o que ele faz e que versão está a
    /// correr. Aberta pelo botão "TSK DIGITAL" no fim da ribbon (painel Mapas) e pelo comando TSKSOBRE.
    /// </summary>
    public class SobreDialog : Form
    {
        public SobreDialog()
        {
            Text = "TSK TakeOff — Sobre";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 440);

            // Marca — azul claro, a cor de marca da TSK DIGITAL (ver IconFactory.Sobre).
            var marca = new Label
            {
                Text = "TSK DIGITAL",
                Font = new Font(Font.FontFamily, 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(62, 134, 206),
                Location = new Point(20, 16),
                AutoSize = true
            };

            var produto = new Label
            {
                Text = "TSK TakeOff — medições de obra no AutoCAD",
                Font = new Font(Font.FontFamily, 10.5f, FontStyle.Bold),
                Location = new Point(20, 56),
                AutoSize = true
            };

            var texto = new TextBox
            {
                Text = "O TSK TakeOff torna a medição no AutoCAD mais rápida, " +
                       "organizada e sem erros.\r\n\r\n" +
                       "Selecione, meça e deixe o resto com o TSK TakeOff: cada área e " +
                       "elemento medido é identificado e organizado automaticamente em " +
                       "hatches e layers, ficando registado no próprio desenho — para " +
                       "visualizar, conferir e controlar tudo num relance.\r\n\r\n" +
                       "Os resultados seguem depois para o Excel, já no formato da casa, " +
                       "com o mapa de quantidades, os vãos descontados e os totais por " +
                       "serviço, prontos a entregar.",
                Multiline = true,
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = SystemColors.Window,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(20, 84),
                Size = new Size(520, 180),
                TabStop = false
            };

            var versao = new Label
            {
                Text = "Versão " + Versao.Curta,
                Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
                Location = new Point(20, 280),
                AutoSize = true
            };

            var build = new Label
            {
                Text = "Build: " + Versao.Informativa,
                Location = new Point(20, 302),
                AutoSize = true
            };

            var licenca = new Label
            {
                Text = "Licença: " + Licenca.Resumo(),
                Location = new Point(20, 324),
                AutoSize = true
            };
            AplicarCorLicenca(licenca);

            var rodape = new Label
            {
                Text = "© TSK DIGITAL — suporte: " + Licenca.EmailContacto,
                Location = new Point(20, 362),
                AutoSize = true
            };

            var btnOk = new Button
            {
                Text = "OK",
                Location = new Point(470, 398),
                Size = new Size(70, 28)
            };
            btnOk.Click += (s, e) => Close();
            AcceptButton = btnOk;

            Controls.Add(marca);
            Controls.Add(produto);
            Controls.Add(texto);
            Controls.Add(versao);
            Controls.Add(build);
            Controls.Add(licenca);
            Controls.Add(rodape);
            Controls.Add(btnOk);
        }

        /// <summary>
        /// Colore o estado da licença sem alterar o texto nem a validade.
        /// Avaliação: mais de 5 dias verde; 4–5 amarelo; 0–3 vermelho/negrito.
        /// Licença expirada ou ausente também merece o alerta máximo.
        /// </summary>
        private static void AplicarCorLicenca(Label label)
        {
            if (label == null) return;

            bool avaliacao = Licenca.EmAvaliacao;
            int dias = Licenca.DiasRestantes;

            if (!Licenca.Valida || dias <= 3)
            {
                label.ForeColor = Color.FromArgb(190, 35, 35);
                label.Font = new Font(label.Font, FontStyle.Bold);
            }
            else if (avaliacao && dias <= 5)
            {
                label.ForeColor = Color.FromArgb(190, 125, 0);
                label.Font = new Font(label.Font, FontStyle.Regular);
            }
            else
            {
                label.ForeColor = Color.FromArgb(0, 128, 72);
                label.Font = new Font(label.Font, FontStyle.Regular);
            }
        }

        /// <summary>Mostra a janela como modal do AutoCAD (com dono, nunca atrás).</summary>
        public static void Mostrar()
        {
            try
            {
                using (var dlg = new SobreDialog())
                    AcadApp.ShowModalDialog(dlg);
            }
            catch { /* mostrar o Sobre nunca pode rebentar */ }
        }
    }
}
