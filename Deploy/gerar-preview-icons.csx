// Pré-visualização dos ícones do TSK TakeOff.
// Compila o IconFactory.cs (que não toca no AutoCAD) e grava uma imagem
// com todos os ícones em grelha, para ver o resultado sem abrir o AutoCAD.
// Uso:  dotnet script Deploy/gerar-preview-icons.csx   (ou copie para um projeto)
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;

// Carregar o IconFactory.cs diretamente
string src = File.ReadAllText(@"C:\Users\Thiago Almeida\Documents\Casquilho\PluginMedicoes\IconFactory.cs");
// (dotnet-script compila ficheiros csx; aqui apenas desenhamos a preview)

// Grelha: 7 colunas x 5 linhas = 35 ícones, 64px cada + margem
string[] nomes = {
    "ParedeRet", "ParedePoly", "Retangulo", "Area", "AreaSel", "MedirSel", "Linear",
    "Polf", "Vao", "Contagem", "Qselect", "Excel", "Exportar", "Modelo",
    "Mapa", "Macro MD", "Macro EO", "Macro QT", "Painel", "Licenca", "Sobre",
    "Atualizar", "Capitulo", "Artigo", "Reclassificar", "LinhaBranca", "Limpar", "Remover"
};

int cell = 88, cols = 7, rows = 4, pad = 12;
var bmp = new Bitmap(cols * cell, rows * cell);
using (var g = Graphics.FromImage(bmp))
{
    g.Clear(Color.FromArgb(50, 50, 56)); // fundo escuro, como a ribbon
    g.SmoothingMode = SmoothingMode.AntiAlias;

    var fab = Type.GetType("TSKTakeOff.IconFactory, TSKTakeOff") ??
              Assembly.LoadFrom(@"C:\Users\Thiago Almeida\Documents\Casquilho\PluginMedicoes\bin\Debug\net48\TSKTakeOff.dll")
                       .GetType("TSKTakeOff.IconFactory");

    if (fab == null) { Console.WriteLine("IconFactory não encontrado."); return; }

    for (int i = 0; i < nomes.Length; i++)
    {
        string nome = nomes[i];
        Bitmap icone = null;
        if (nome.StartsWith("Macro "))
            icone = (Bitmap)fab.GetMethod("Macro").Invoke(null, new object[] { nome.Substring(6) });
        else
            icone = (Bitmap)fab.GetMethod(nome).Invoke(null, null);

        int x = (i % cols) * cell + pad, y = (i / cols) * cell + pad;
        g.DrawImage(icone, x, y, 64, 64);
        g.DrawString(nome, new Font("Segoe UI", 8f), Brushes.White, x, y + 66);
        icone.Dispose();
    }
}

string saida = @"C:\Users\Thiago Almeida\Documents\Casquilho\PluginMedicoes\Deploy\preview-icones.png";
bmp.Save(saida, ImageFormat.Png);
Console.WriteLine("Gravado: " + saida);
