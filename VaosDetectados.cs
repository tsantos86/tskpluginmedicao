using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace TSKTakeOff
{
    /// <summary>
    /// Confirmação dos vãos de uma parede, logo a seguir a medi-la.
    ///
    /// A largura vem do desenho e é exacta — é a folga entre os troços da
    /// hachura, ou o que o utilizador indicar. O que a planta não diz é a
    /// altura, o nome e quantas portas iguais há ali. Por isso pergunta-se,
    /// em vez de inventar: uma altura errada por omissão espalha-se pela obra
    /// toda sem ninguém dar por ela.
    ///
    /// A quantidade está aqui de propósito. Duas portas iguais na mesma parede
    /// são UMA linha com quantidade 2 na folha da casa — não duas linhas de 1.
    /// </summary>
    public static class VaosDetectados
    {
        /// <summary>
        /// Mostra os vãos para confirmação. Devolve a lista final, ou null se
        /// o utilizador cancelar (e aí a medição fica sem vãos nenhuns).
        /// </summary>
        public static List<Vao> Confirmar(IList<double> larguras, double alturaParede)
        {
            if (larguras == null || larguras.Count == 0) return new List<Vao>();

            using (var frm = new Form())
            using (var grelha = new DataGridView())
            using (var ok = new Button())
            using (var nenhum = new Button())
            using (var rot = new Label())
            {
                frm.Text = "TSK TakeOff — vãos desta parede";
                frm.StartPosition = FormStartPosition.CenterScreen;
                frm.FormBorderStyle = FormBorderStyle.FixedDialog;
                frm.MinimizeBox = false;
                frm.MaximizeBox = false;
                frm.ClientSize = new Size(600, 320);

                rot.Text = larguras.Count + " interrupção(ões) encontrada(s) na parede.\n" +
                           "A largura vem do desenho. Diga o que é cada uma: uma PORTA " +
                           "tem parede por cima e desconta só a sua altura; um PILAR " +
                           "não tem, e desconta a altura toda. Duas iguais são uma " +
                           "linha com quantidade 2.";
                rot.Dock = DockStyle.Top;
                rot.Height = 74;
                rot.Padding = new Padding(10, 8, 10, 0);

                grelha.Dock = DockStyle.Fill;
                grelha.AllowUserToAddRows = false;
                grelha.AllowUserToDeleteRows = true;
                grelha.RowHeadersVisible = false;
                grelha.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                grelha.BackgroundColor = Color.White;

                Coluna(grelha, "larg", "Largura (m)");
                ColunaTipo(grelha);
                Coluna(grelha, "alt", "Altura (m)");
                Coluna(grelha, "qt", "Qt");
                Coluna(grelha, "nome", "Nome (PI04, VE01…)");

                foreach (double l in larguras)
                    grelha.Rows.Add(N3(l), "Porta", N2(Config.AlturaVaoPadrao), "1", "");

                // Escolher "Pilar" põe a altura da parede: um pilar não tem
                // verga por cima, a alvenaria não existe ali em altura nenhuma.
                grelha.CurrentCellDirtyStateChanged += (s2, e2) =>
                {
                    if (grelha.IsCurrentCellDirty)
                        grelha.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
                grelha.CellValueChanged += (s2, e2) =>
                {
                    if (e2.RowIndex < 0) return;
                    if (grelha.Columns[e2.ColumnIndex].Name != "tipo") return;

                    var linha = grelha.Rows[e2.RowIndex];
                    string t = (linha.Cells["tipo"].Value ?? "").ToString();
                    if (t == "Pilar" && alturaParede > 0)
                        linha.Cells["alt"].Value = N2(alturaParede);
                    else if (t != "Pilar")
                        linha.Cells["alt"].Value = N2(Config.AlturaVaoPadrao);
                };

                var baixo = new Panel { Dock = DockStyle.Bottom, Height = 46 };
                ok.Text = "Aceitar";
                ok.DialogResult = DialogResult.OK;
                ok.SetBounds(310, 9, 90, 28);
                nenhum.Text = "Sem vãos";
                nenhum.DialogResult = DialogResult.Cancel;
                nenhum.SetBounds(408, 9, 90, 28);
                baixo.Controls.Add(ok);
                baixo.Controls.Add(nenhum);

                frm.Controls.Add(grelha);
                frm.Controls.Add(baixo);
                frm.Controls.Add(rot);
                frm.AcceptButton = ok;

                if (AcadApp.ShowModalDialog(frm) != DialogResult.OK)
                    return new List<Vao>();

                var vaos = new List<Vao>();
                foreach (DataGridViewRow linha in grelha.Rows)
                {
                    if (linha.IsNewRow) continue;

                    double larg = Ler(linha, "larg");
                    double alt = Ler(linha, "alt");
                    if (larg <= 0 || alt <= 0) continue;

                    int qt = (int)Math.Round(Ler(linha, "qt"));
                    if (qt < 1) qt = 1;

                    string nome = (linha.Cells["nome"].Value ?? "").ToString().Trim();
                    string tipo = (linha.Cells["tipo"].Value ?? "Porta").ToString();

                    // Um pilar é um vão cuja altura é a da parede. Descontado
                    // assim, dá exactamente o mesmo que excluí-lo do
                    // comprimento — e fica visível na folha, que é o que
                    // permite conferir.
                    if (tipo == "Pilar" && alturaParede > 0) alt = alturaParede;

                    // Um vão mais alto do que a parede é engano de quem escreve,
                    // e passaria despercebido como uma área negativa na folha.
                    if (alturaParede > 0 && alt > alturaParede) alt = alturaParede;

                    vaos.Add(new Vao
                    {
                        Largura = larg,
                        Altura = alt,
                        Quantidade = qt,
                        Tipo = (tipo == "Janela" || EhJanela(nome))
                            ? TipoVao.Janela : TipoVao.Porta,
                        Designacao = string.IsNullOrEmpty(nome) && tipo == "Pilar"
                            ? "pilar" : nome,
                        Espessura = Config.Espessura
                    });
                }
                return vaos;
            }
        }

        private static bool EhJanela(string nome)
        {
            string n = (nome ?? "").ToUpperInvariant();
            return n.StartsWith("V") || n.Contains("JANELA");
        }

        /// <summary>Coluna de escolha: porta, janela ou pilar.</summary>
        private static void ColunaTipo(DataGridView g)
        {
            var col = new DataGridViewComboBoxColumn
            {
                Name = "tipo",
                HeaderText = "O que é",
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            col.Items.AddRange("Porta", "Janela", "Pilar");
            g.Columns.Add(col);
        }

        private static void Coluna(DataGridView g, string nome, string titulo)
        {
            g.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = nome,
                HeaderText = titulo,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private static double Ler(DataGridViewRow linha, string coluna)
        {
            string t = (linha.Cells[coluna].Value ?? "").ToString().Replace(',', '.');
            double v;
            return double.TryParse(t, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static string N2(double v) { return v.ToString("N2"); }
        private static string N3(double v) { return v.ToString("N3"); }
    }
}
