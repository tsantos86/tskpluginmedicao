using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TSKTakeOff
{
    /// <summary>
    /// As definições dos tipos de medida que não têm campos próprios no
    /// painel único: material e altura do piso dos panos, nome, categoria e
    /// raio das contagens.
    ///
    /// EXISTE PORQUE O PAINEL ÚNICO TIROU AS QUATRO ABAS.
    ///
    /// Cada aba antiga tinha a sua secção de CONFIGURAÇÃO, e essa secção
    /// desapareceu com ela. Os comandos TSKRET, TSKPOLF e TSKCONTAR não
    /// mudaram — continuam a ler <see cref="FachadaConfig"/> e
    /// <see cref="ContagemConfig"/> antes de desenhar — mas deixou de haver
    /// onde escrever esses campos antes de disparar o comando.
    ///
    /// Não entraram na CONFIGURAÇÃO principal porque só servem um tipo de
    /// medida de cada vez: metade dos campos ficaria sempre cinzenta,
    /// consoante o que se estivesse a medir. Um diálogo à parte, aberto
    /// quando se precisa dele, é o que evita isso sem inventar uma
    /// configuração dinâmica que troca de campos por baixo do rato.
    ///
    /// Grava directamente nas classes estáticas — não há "OK" que devolva um
    /// objecto para o chamador escrever; os comandos já leem essas classes,
    /// e é aí que este diálogo também escreve.
    /// </summary>
    public class DefinicoesTipoDialog : Form
    {
        private ComboBox _cmbMaterial;
        private NumericUpDown _numAlturaPiso;
        private ComboBox _cmbNomeContagem;
        private TextBox _txtCategoriaContagem;
        private NumericUpDown _numRaioContagem;
        private CheckBox _chkTextoContagem;

        public DefinicoesTipoDialog()
        {
            Text = "Definições por tipo de medida";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 310);
            BackColor = PaletteTheme.Fundo;
            ForeColor = PaletteTheme.Tinta;
            Font = PaletteTheme.Normal;

            var raiz = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(0)
            };
            raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            raiz.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            raiz.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            raiz.Controls.Add(SeccaoMateriais(), 0, 0);
            raiz.Controls.Add(SeccaoContagens(), 0, 1);
            raiz.Controls.Add(Botoes(), 0, 2);

            Controls.Add(raiz);
            PaletteTheme.AplicarTema(this);
        }

        // ------------------------------------------------------------------
        // Materiais — o que o TSKRET / TSKPOLF leem antes de desenhar
        // ------------------------------------------------------------------
        private Panel SeccaoMateriais()
        {
            var cabecalho = TituloDeSeccao("MATERIAIS");

            var grelha = PalettePanelShell.GrelhaDeCampos();
            grelha.Padding = new Padding(PaletteTheme.Margem, 8, PaletteTheme.Margem, 8);

            FachadaConfig.Carregar();
            if (FachadaConfig.Materiais.Count == 0)
                FachadaConfig.Materiais.AddRange(new[]
                    { "ETICS", "REBOCO", "PINTURA", "IMPERMEABILIZACAO" });

            _cmbMaterial = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown,
                AccessibleName = "Material"
            };
            _cmbMaterial.Items.AddRange(FachadaConfig.Materiais.ToArray());
            _cmbMaterial.Text = FachadaConfig.Material;

            _numAlturaPiso = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                DecimalPlaces = 2,
                Increment = 0.05M,
                Minimum = 0.1M,
                Maximum = 30M,
                Value = (decimal)FachadaConfig.AlturaPiso,
                AccessibleName = "Altura do piso"
            };

            PalettePanelShell.Campo(grelha, "Material:", _cmbMaterial);
            PalettePanelShell.Campo(grelha, "Altura piso:", _numAlturaPiso);

            var painel = new Panel {
                Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            painel.Controls.Add(grelha);
            painel.Controls.Add(cabecalho);
            return painel;
        }

        // ------------------------------------------------------------------
        // Contagens — o que o TSKCONTAR lê antes de marcar
        // ------------------------------------------------------------------
        private Panel SeccaoContagens()
        {
            var cabecalho = TituloDeSeccao("CONTAGENS");

            var grelha = PalettePanelShell.GrelhaDeCampos();
            grelha.Padding = new Padding(PaletteTheme.Margem, 8, PaletteTheme.Margem, 8);

            ContagemConfig.Carregar();
            if (ContagemConfig.Nomes.Count == 0)
                ContagemConfig.Nomes.AddRange(new[]
                    { "P.01", "J.01", "TOMADA", "LUMINARIA", "LOUÇA" });

            _cmbNomeContagem = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDown,
                AccessibleName = "Nome da contagem"
            };
            _cmbNomeContagem.Items.AddRange(ContagemConfig.Nomes.ToArray());
            _cmbNomeContagem.Text = ContagemConfig.Nome;

            _txtCategoriaContagem = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = ContagemConfig.Categoria,
                AccessibleName = "Categoria da contagem"
            };

            _numRaioContagem = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                DecimalPlaces = 2,
                Increment = 0.05M,
                Minimum = 0.01M,
                Maximum = 100M,
                Value = (decimal)ContagemConfig.Raio,
                AccessibleName = "Raio do círculo"
            };

            _chkTextoContagem = new CheckBox
            {
                Dock = DockStyle.Fill,
                Text = "Escrever o nome ao lado do círculo",
                Checked = ContagemConfig.ComTexto,
                AutoSize = true,
                ForeColor = PaletteTheme.Tinta,
                AccessibleName = "Escrever nome ao lado do círculo"
            };

            PalettePanelShell.Campo(grelha, "Nome:", _cmbNomeContagem);
            PalettePanelShell.Campo(grelha, "Categoria:", _txtCategoriaContagem);
            PalettePanelShell.Campo(grelha, "Raio (m):", _numRaioContagem);
            PalettePanelShell.Campo(grelha, "", _chkTextoContagem);

            var painel = new Panel {
                Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            painel.Controls.Add(grelha);
            painel.Controls.Add(cabecalho);
            return painel;
        }

        private static Panel TituloDeSeccao(string texto)
        {
            return new PalettePanelShell.Titulo(texto, "");
        }

        /// <summary>
        /// Os botões, numa faixa própria ancorada ao fundo.
        ///
        /// Um FlowLayoutPanel da direita para a esquerda, e não posições
        /// calculadas à mão: nesta fase da construção o painel ainda não
        /// passou pelo layout e a sua largura/altura valem zero — colocar os
        /// botões a partir daí punha-os fora de vista, sem erro nenhum a
        /// avisar.
        /// </summary>
        private Panel Botoes()
        {
            var painel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 42,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Padding(PaletteTheme.Margem, 7, PaletteTheme.Margem, 7),
                BackColor = PaletteTheme.FundoSeccao
            };

            var btnOk = new Button
            {
                Text = "Guardar",
                DialogResult = DialogResult.OK,
                Width = 84,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaletteTheme.Palido,
                ForeColor = PaletteTheme.Acento,
                UseVisualStyleBackColor = false
            };
            var btnCancel = new Button
            {
                Text = "Cancelar",
                DialogResult = DialogResult.Cancel,
                Width = 84,
                Height = 26,
                FlatStyle = FlatStyle.Flat,
                BackColor = PaletteTheme.Fundo,
                ForeColor = PaletteTheme.Tinta,
                UseVisualStyleBackColor = false
            };
            // A ordem de inserção num FlowLayoutPanel RightToLeft conta: o
            // primeiro fica mais à direita. "Guardar" entra primeiro para
            // ficar no canto — o botão de fecho do teclado (Enter) no sítio
            // onde o olhar chega primeiro.
            painel.Controls.Add(btnOk);
            painel.Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;

            btnOk.Click += (s, e) => Guardar();
            return painel;
        }

        /// <summary>
        /// Grava tudo nas classes estáticas que os comandos já leem. Não há
        /// validação além da que os próprios NumericUpDown já impõem — os
        /// mesmos limites que os campos da CONFIGURAÇÃO principal usam.
        /// </summary>
        private void Guardar()
        {
            FachadaConfig.Material = string.IsNullOrWhiteSpace(_cmbMaterial.Text)
                ? FachadaConfig.Material
                : _cmbMaterial.Text.Trim().ToUpperInvariant().Replace(" ", "_");
            if (!string.IsNullOrEmpty(FachadaConfig.Material) &&
                !FachadaConfig.Materiais.Contains(FachadaConfig.Material))
                FachadaConfig.Materiais.Add(FachadaConfig.Material);
            FachadaConfig.AlturaPiso = (double)_numAlturaPiso.Value;
            FachadaConfig.Guardar();

            ContagemConfig.Nome = string.IsNullOrWhiteSpace(_cmbNomeContagem.Text)
                ? ContagemConfig.Nome : _cmbNomeContagem.Text.Trim();
            if (!string.IsNullOrEmpty(ContagemConfig.Nome) &&
                !ContagemConfig.Nomes.Contains(ContagemConfig.Nome))
                ContagemConfig.Nomes.Add(ContagemConfig.Nome);
            ContagemConfig.Categoria = (_txtCategoriaContagem.Text ?? "").Trim();
            ContagemConfig.Raio = (double)_numRaioContagem.Value;
            ContagemConfig.ComTexto = _chkTextoContagem.Checked;
            ContagemConfig.Guardar();
        }
    }
}
