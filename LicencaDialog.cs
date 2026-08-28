using System;
using System.Drawing;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// Janela de activação: a pessoa cola o código uma vez e fica autorizada.
    /// Mostra também o estado actual, para saber quantos dias faltam.
    /// </summary>
    public class LicencaDialog : Form
    {
        private readonly TextBox _txtCodigo;
        private readonly TextBox _txtEmail;
        private readonly Label _lblEstado;
        private readonly Button _btnActivar;
        private readonly Button _btnTrial;

        private const string Exemplo = "TSK-XXXX-XXXX-XXXX";

        public LicencaDialog()
        {
            Text = "TSK TakeOff — licença";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(460, 330);

            var titulo = new Label
            {
                Text = "Introduza o código de licença",
                Font = new Font(Font.FontFamily, 11f, FontStyle.Bold),
                Location = new Point(16, 16),
                Size = new Size(420, 24)
            };

            var ajuda = new Label
            {
                Text = "O código autoriza este posto durante o período contratado. " +
                       "Só é preciso introduzi-lo uma vez.",
                Location = new Point(16, 42),
                Size = new Size(424, 34),
                ForeColor = SystemColors.GrayText
            };

            var lblEmail = new Label
            {
                Text = "E-mail de contacto (necessário para iniciar o trial):",
                Location = new Point(16, 84),
                Size = new Size(424, 20)
            };

            _txtEmail = new TextBox
            {
                Location = new Point(16, 106),
                Size = new Size(424, 24),
                Text = Licenca.Email ?? ""
            };

            var lblCodigo = new Label
            {
                Text = "Código de licença (se já recebeu um):",
                Location = new Point(16, 140),
                Size = new Size(424, 20)
            };

            _txtCodigo = new TextBox
            {
                Location = new Point(16, 162),
                Size = new Size(300, 28),
                Font = new Font("Consolas", 12f),
                CharacterCasing = CharacterCasing.Upper,
                Text = ""
            };
            // .NET Framework 4.8 não tem PlaceholderText: usamos texto-fantasma manual
            _txtCodigo.ForeColor = SystemColors.GrayText;
            _txtCodigo.Text = Exemplo;
            _txtCodigo.GotFocus += (s, e) =>
            {
                if (_txtCodigo.Text == Exemplo)
                {
                    _txtCodigo.Text = "";
                    _txtCodigo.ForeColor = SystemColors.WindowText;
                }
            };
            _txtCodigo.LostFocus += (s, e) =>
            {
                if (_txtCodigo.Text.Trim().Length == 0)
                {
                    _txtCodigo.Text = Exemplo;
                    _txtCodigo.ForeColor = SystemColors.GrayText;
                }
            };

            _btnActivar = new Button
            {
                Text = "Activar",
                Location = new Point(326, 161),
                Size = new Size(114, 30)
            };
            _btnActivar.Click += (s, e) => Activar();

            _lblEstado = new Label
            {
                Location = new Point(16, 200),
                Size = new Size(424, 54),
                ForeColor = SystemColors.ControlText
            };

            var lblPosto = new Label
            {
                Text = "Posto: " + Licenca.Maquina,
                Location = new Point(16, 258),
                Size = new Size(424, 20),
                ForeColor = SystemColors.GrayText,
                Font = new Font(Font.FontFamily, 8f)
            };

            var btnFechar = new Button
            {
                Text = "Fechar",
                DialogResult = DialogResult.Cancel,
                Location = new Point(352, 292),
                Size = new Size(88, 28)
            };

            // Avaliação gratuita: só aparece a quem ainda não tem nada.
            // Quem já é cliente não precisa de ver este botão.
            _btnTrial = new Button
            {
                Text = "Experimentar 15 dias",
                Location = new Point(16, 284),
                Size = new Size(220, 38),
                Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(255, 165, 0),
                ForeColor = Color.Black,
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Visible = !Licenca.Valida
            };
            _btnTrial.FlatAppearance.BorderColor = Color.FromArgb(210, 125, 0);
            _btnTrial.FlatAppearance.BorderSize = 1;
            _btnTrial.Click += (s, e) => Experimentar();

            AcceptButton = _btnActivar;
            CancelButton = btnFechar;

            Controls.Add(titulo);
            Controls.Add(ajuda);
            Controls.Add(lblEmail);
            Controls.Add(_txtEmail);
            Controls.Add(lblCodigo);
            Controls.Add(_txtCodigo);
            Controls.Add(_btnActivar);
            Controls.Add(_lblEstado);
            Controls.Add(lblPosto);
            Controls.Add(_btnTrial);
            Controls.Add(btnFechar);

            MostrarEstado();
        }

        /// <summary>Pede a avaliação gratuita de 15 dias para este posto.</summary>
        private void Experimentar()
        {
            _btnTrial.Enabled = false;
            _lblEstado.ForeColor = SystemColors.GrayText;
            _lblEstado.Text = "A contactar o servidor…";
            Application.DoEvents();

            string erro = Licenca.PedirTrial(_txtEmail.Text);

            _btnTrial.Enabled = true;

            if (erro == null)
            {
                MostrarEstado();
                _btnTrial.Visible = false;
                PaletteHost.Log("Avaliação iniciada: " + Licenca.Resumo());
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _lblEstado.ForeColor = Color.FromArgb(170, 40, 40);
                _lblEstado.Text = erro;
            }
        }

        private void MostrarEstado()
        {
            if (_btnTrial != null) _btnTrial.Visible = !Licenca.Valida;

            if (Licenca.Valida)
            {
                int dias = Licenca.DiasRestantes;
                _lblEstado.ForeColor = dias <= 7
                    ? Color.FromArgb(180, 100, 0)
                    : Color.FromArgb(0, 120, 60);
                _lblEstado.Text = "Licença activa — " + Licenca.Resumo();
                if (dias <= 7)
                    _lblEstado.Text += "\nRenove antes de expirar para não interromper o trabalho.";
            }
            else
            {
                _lblEstado.ForeColor = Color.FromArgb(170, 40, 40);
                string motivo = Licenca.UltimoMotivo;
                _lblEstado.Text = string.IsNullOrEmpty(motivo)
                    ? "Este posto ainda não está licenciado."
                    : "Sem licença válida — " + motivo;
                if (Licenca.ExpiraEm != DateTime.MinValue)
                    _lblEstado.Text += "\nPara continuar, contacte " + Licenca.EmailContacto + ".";
            }
        }

        private void Activar()
        {
            string codigo = (_txtCodigo.Text ?? "").Trim();
            if (codigo == Exemplo) codigo = "";
            if (codigo.Length == 0)
            {
                _lblEstado.ForeColor = Color.FromArgb(170, 40, 40);
                _lblEstado.Text = "Escreva o código primeiro.";
                return;
            }

            _btnActivar.Enabled = false;
            _lblEstado.ForeColor = SystemColors.GrayText;
            _lblEstado.Text = "A contactar o servidor…";
            Application.DoEvents();

            string erro = Licenca.Activar(codigo);

            _btnActivar.Enabled = true;

            if (erro == null)
            {
                MostrarEstado();
                _txtCodigo.Text = Exemplo;
                _txtCodigo.ForeColor = SystemColors.GrayText;
                PaletteHost.Log("Licença activada: " + Licenca.Resumo());
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _lblEstado.ForeColor = Color.FromArgb(170, 40, 40);
                _lblEstado.Text = erro;
            }
        }
    }
}
