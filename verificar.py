#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Verificador estático do TSK TakeOff.

Não substitui o compilador, mas apanha os erros que mais custam tempo:
chaves desequilibradas, membros inexistentes nas nossas próprias classes,
comandos AutoCAD duplicados, comandos chamados pela UI que não existem,
e ambiguidades típicas entre a API do AutoCAD e o Windows Forms.

Uso:  python verificar.py
"""
import glob
import io
import os
import re
import sys
from collections import defaultdict

PASTA = os.path.dirname(os.path.abspath(__file__))

# Tipos que existem nos dois mundos e dão CS0104 quando ambos os namespaces
# são importados no mesmo ficheiro. Cada entrada: tipo -> (nsA, nsB)
AMBIGUOS = {
    # Autodesk.AutoCAD.Windows  vs  System.Windows.Forms
    "ColorDialog": ("Autodesk.AutoCAD.Windows", "System.Windows.Forms"),
    "OpenFileDialog": ("Autodesk.AutoCAD.Windows", "System.Windows.Forms"),
    "SaveFileDialog": ("Autodesk.AutoCAD.Windows", "System.Windows.Forms"),
    "Palette": ("Autodesk.AutoCAD.Windows", "System.Windows.Forms"),
    "ToolTip": ("Autodesk.AutoCAD.Windows", "System.Windows.Forms"),
    "StatusBar": ("Autodesk.AutoCAD.Windows", "System.Windows.Forms"),
    # Autodesk.AutoCAD.ApplicationServices  vs  System.Windows.Forms
    "Application": ("Autodesk.AutoCAD.ApplicationServices", "System.Windows.Forms"),
    # Autodesk.AutoCAD.DatabaseServices  vs  System.Windows.Forms
    "FlowDirection": ("Autodesk.AutoCAD.DatabaseServices", "System.Windows.Forms"),
    "View": ("Autodesk.AutoCAD.DatabaseServices", "System.Windows.Forms"),
    "Group": ("Autodesk.AutoCAD.DatabaseServices", "System.Windows.Forms"),
    "Line": ("Autodesk.AutoCAD.DatabaseServices", "System.Windows.Shapes"),
    # cores
    "Color": ("Autodesk.AutoCAD.Colors", "System.Drawing"),
    "Transparency": ("Autodesk.AutoCAD.Colors", "System.Windows.Forms"),
    # WPF vs WinForms (Ribbon.cs)
    "Orientation": ("System.Windows.Controls", "System.Windows.Forms"),
    "Cursor": ("System.Windows.Input", "System.Windows.Forms"),
    "MessageBox": ("System.Windows", "System.Windows.Forms"),
    "Point": ("System.Windows", "System.Drawing"),
    "Size": ("System.Windows", "System.Drawing"),
    "Image": ("System.Windows.Controls", "System.Drawing"),
    "Brush": ("System.Windows.Media", "System.Drawing"),
    "Pen": ("System.Windows.Media", "System.Drawing"),
    "FontStyle": ("System.Windows", "System.Drawing"),
}

# Tipos que só existem se o namespace estiver importado (ou houver alias).
# Quando um ficheiro usa aliases em vez do "using System.Windows.Forms;",
# é fácil esquecer um — foi o que aconteceu com MessageBoxDefaultButton.
TIPOS_POR_NAMESPACE = {
    "System.Windows.Forms": [
        "MessageBox", "MessageBoxButtons", "MessageBoxIcon",
        "MessageBoxDefaultButton", "DialogResult", "Form", "TextBox",
        "ComboBox", "Button", "Label", "DataGridView", "ToolStrip",
        "NumericUpDown", "CheckBox", "ColorDialog", "OpenFileDialog",
        "SaveFileDialog", "TableLayoutPanel", "FlowLayoutPanel",
        "UserControl", "Padding", "DockStyle", "ToolStripButton",
    ],
    "System.Drawing": [
        "Bitmap", "Graphics", "SolidBrush", "Pen", "Font", "FontStyle",
        "Point", "PointF", "Rectangle", "SystemColors", "ContentAlignment",
    ],
}

erros = []
avisos = []


def ficheiros():
    return sorted(f for f in os.listdir(PASTA) if f.endswith(".cs"))


def ler(nome):
    return io.open(os.path.join(PASTA, nome), encoding="utf-8-sig").read()


def sem_texto(src):
    """Remove comentários e literais para não gerar falsos positivos.

    Percorre o ficheiro UMA vez, em vez de aplicar regexes em cadeia.

    A versão a regex tirava as strings ANTES dos comentários, e por isso uma
    aspa solta dentro de um comentário abria uma "string" que ia comer código
    a sério até à aspa seguinte. Foi o que aconteceu no Commands.cs: o
    comentário do NomeLayer diz que o AutoCAD recusa < > / \\ " : ; ? * |, e
    aquela aspa engoliu a chaveta que abre o corpo do método. Resultado: 35
    erros inventados — '}' a mais, o Util a aparecer sem membros nenhuns, e o
    verificador a ficar sem serventia, que é o pior dos desfechos, porque um
    verificador que grita a torto e a direito deixa de ser lido.

    Num tokenizador a ordem deixa de existir: dentro de um comentário uma
    aspa é texto, dentro de uma string um // é texto, e cada um só acaba onde
    a sua própria regra manda.

    As mudanças de linha são TODAS preservadas — inclusive as de dentro de
    comentários de bloco e de strings verbatim, que a versão anterior deitava
    fora. É delas que saem os números de linha dos erros.
    """
    out = []
    i, n = 0, len(src)

    while i < n:
        c = src[i]
        prox = src[i + 1] if i + 1 < n else ""

        # comentário de linha: fica tudo, menos o \n que o fecha
        if c == "/" and prox == "/":
            while i < n and src[i] != "\n":
                i += 1
            continue

        # comentário de bloco
        if c == "/" and prox == "*":
            i += 2
            while i + 1 < n and not (src[i] == "*" and src[i + 1] == "/"):
                if src[i] == "\n":
                    out.append("\n")
                i += 1
            i += 2
            continue

        # string verbatim: @"..." e $@"...". Aqui a barra invertida não escapa
        # nada e a aspa escreve-se duplicada.
        if (c == "@" and prox == '"') or (
                c == "$" and prox == "@" and src[i + 2:i + 3] == '"'):
            i += 2 if c == "@" else 3
            out.append('""')
            while i < n:
                if src[i] == '"':
                    if src[i + 1:i + 2] == '"':
                        i += 2
                        continue
                    i += 1
                    break
                if src[i] == "\n":
                    out.append("\n")
                i += 1
            continue

        # string normal, interpolada incluída ($"...{x}...")
        if c == '"':
            i += 1
            out.append('""')
            while i < n:
                if src[i] == "\\":
                    i += 2
                    continue
                if src[i] == '"':
                    i += 1
                    break
                # Uma string normal não atravessa linhas. Se aparecer um \n é
                # porque a aspa era outra coisa qualquer: parar aqui em vez de
                # engolir o resto do ficheiro.
                if src[i] == "\n":
                    break
                i += 1
            continue

        # literal de carácter: 'x', '\n', '￿' — no máximo 8 caracteres
        # com as plicas. Passar disso é sinal de que a plica não abria um
        # literal nenhum, e então vale como carácter solto.
        if c == "'":
            j = i + 1
            while j < n:
                if src[j] == "\\":
                    j += 2
                    continue
                if src[j] == "'":
                    j += 1
                    break
                if src[j] == "\n":
                    break
                j += 1
            if j - i <= 8:
                out.append("' '")
                i = j
                continue

        out.append(c)
        i += 1

    return "".join(out)


# ------------------------------------------------- ShowModalDialog só com Form
# AcadApp.ShowModalDialog(Form) — CommonDialog (OpenFileDialog, SaveFileDialog,
# ColorDialog, FolderBrowserDialog) não herda de Form e não compila aqui. É um
# erro fácil de fazer porque a chamada parece igual e só rebenta no compilador.
COMMON_DIALOGS = ("OpenFileDialog", "SaveFileDialog", "ColorDialog",
                  "FolderBrowserDialog", "FontDialog", "PrintDialog")


def check_modaldialog(nome, src):
    for m in re.finditer(r'ShowModalDialog\(\s*(\w+)\s*\)', src):
        var = m.group(1)
        # de que tipo é esta variável, olhando para o "new X(" mais próximo acima
        antes = src[:m.start()]
        decl = re.findall(r'\b' + re.escape(var) + r'\s*=\s*new\s+([\w\.]+)', antes)
        if not decl:
            continue
        tipo = decl[-1].split('.')[-1]
        if tipo in COMMON_DIALOGS:
            erros.append("%s: ShowModalDialog(%s) — %s é CommonDialog, não Form. "
                         "Usar %s.ShowDialog()." % (nome, var, tipo, var))


# ------------------------------------------------- escopo (que classe é esta)
TIPO_DECL = re.compile(r'\b(?:class|struct|interface|enum)\s+(\w+)')


def escopos(s):
    """[(inicio, fim, classe)] seguindo as chavetas.

    Uma classe aninhada abre e fecha dentro da de fora, por isso a pilha
    guarda a profundidade a que cada uma abriu — é assim que se sabe qual
    fecha em cada '}'.
    """
    out, pilha, prof, pendente, i = [], [], 0, None, 0
    while i < len(s):
        m = TIPO_DECL.match(s, i)
        if m:
            pendente = m.group(1)
            i = m.end()
            continue
        ch = s[i]
        if ch == '{':
            prof += 1
            if pendente:
                pilha.append([pendente, prof, i])
                pendente = None
        elif ch == '}':
            if pilha and pilha[-1][1] == prof:
                nome, _, ini = pilha.pop()
                out.append((ini, i, nome))
            prof -= 1
        i += 1
    for nome, _, ini in pilha:
        out.append((ini, len(s), nome))
    return out


def classe_em(esc, pos):
    """A classe mais interior que contem esta posicao."""
    melhor, tam = None, 10 ** 9
    for ini, fim, nome in esc:
        if ini <= pos <= fim and (fim - ini) < tam:
            melhor, tam = nome, fim - ini
    return melhor


def check_escopo(nomes, fontes):
    """Campos duplicados na mesma classe e estáticos chamados sem qualificar.

    Foi preciso seguir as chavetas para isto ser fiável: sem escopo, dois
    campos com o mesmo nome em classes diferentes pareciam um erro, e um
    método de uma classe aninhada era atribuído à classe de fora.
    """
    esc = {f: escopos(fontes[f]) for f in nomes}

    CAMPO = re.compile(r'\b(?:public|private|internal|protected)\s+'
                       r'(?:static\s+|readonly\s+|const\s+|volatile\s+)*'
                       r'[\w<>\[\],\.\?]+\s+(\w+)\s*(?:=[^=]|;)')
    for f in nomes:
        vistos = {}
        for m in CAMPO.finditer(fontes[f]):
            k = (classe_em(esc[f], m.start()), m.group(1))
            vistos.setdefault(k, []).append(fontes[f][:m.start()].count("\n") + 1)
        for (cls, nome), linhas in vistos.items():
            if len(linhas) > 1:
                erros.append("%s: %s.%s declarado %dx (linhas %s)"
                             % (f, cls, nome, len(linhas), linhas))

    # A visibilidade vai no grupo 1 porque faz falta duas vezes: os privados
    # TÊM de entrar aqui — é assim que um N2() privado do VaosDialog conta
    # como segunda declaração do nome e cala o aviso — mas nunca podem ser
    # APONTADOS como o alvo a qualificar, que é o passo a seguir.
    METODO = re.compile(r'\b(public|internal|private|protected)\s+'
                        r'(?:static\s+)(?:[\w<>\[\],\.\?]+\s+)(\w+)\s*\(')
    declarado_em = {}
    for f in nomes:
        for m in METODO.finditer(fontes[f]):
            declarado_em.setdefault(m.group(2), set()).add(
                (f, classe_em(esc[f], m.start()), m.group(1)))

    MEMBRO = re.compile(r'\b(?:public|internal|private|protected)\s+'
                        r'(?!static\b)[\w<>\[\],\.\?\s]+?(\w+)\s*(?:\(|\{|=|;)')
    ambiguos = set()
    for f in nomes:
        for m in MEMBRO.finditer(fontes[f]):
            ambiguos.add(m.group(1))

    CHAMADA = re.compile(r'(?<![\.\w])(\w+)\s*\(')
    CHAVES = set("if while for foreach switch catch using lock return new typeof "
                 "sizeof nameof base this get set value do else try finally throw "
                 "yield fixed checked unchecked".split())
    for f in nomes:
        s = fontes[f]
        for m in CHAMADA.finditer(s):
            nome = m.group(1)
            if nome in CHAVES or nome not in declarado_em: continue
            # "new Polyline()" constrói um tipo, não chama método nenhum. O
            # lookbehind do CHAMADA só trava um ponto ou uma letra colados ao
            # nome, e entre o new e o tipo há um espaço.
            if re.search(r"\bnew\s+$", s[:m.start()]): continue
            if len(declarado_em[nome]) != 1 or nome in ambiguos: continue
            df, dcls, dvis = next(iter(declarado_em[nome]))
            # Um "private static" de outra classe é invisível daqui: mandar
            # qualificá-lo dá um conselho que não compila. Foi o que começou
            # a acontecer quando o IconFactory ganhou um Polyline() privado
            # para desenhar os ícones — todos os "new Polyline()" do AutoCAD
            # ficaram acusados.
            if dvis == "private": continue
            aqui = classe_em(esc[f], m.start())
            if aqui is None or aqui == dcls: continue
            if re.search(r"using\s+static\s+[\w\.]*\b" + re.escape(dcls) + r"\s*;", s):
                continue
            erros.append("%s:%d: %s() chamado em %s mas e estatico de %s (%s) — "
                         "falta qualificar com %s."
                         % (f, s[:m.start()].count("\n") + 1, nome, aqui, dcls, df, dcls))


# ---------------------------------------------------------------- chaves
def check_chaves(nome, limpo):
    saldo = 0
    for ch in limpo:
        if ch == "{":
            saldo += 1
        elif ch == "}":
            saldo -= 1
            if saldo < 0:
                erros.append("%s: '}' a mais" % nome)
                return
    if saldo != 0:
        erros.append("%s: %d chave(s) '{' por fechar" % (nome, saldo))

    for abre, fecha, nomep in (("(", ")", "parênteses"), ("[", "]", "parênteses retos")):
        if limpo.count(abre) != limpo.count(fecha):
            erros.append("%s: %s desequilibrados (%d vs %d)"
                         % (nome, nomep, limpo.count(abre), limpo.count(fecha)))


# ----------------------------------------------------- membros das classes
DECL_TIPO = re.compile(
    r"\b(?:public|internal|private|protected)\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*"
    r"(?:(class|struct|enum))\s+(\w+)")

# Nome do membro: o último identificador antes de ( { = ; ou =>
MEMBRO = re.compile(
    r"\b(?:public|internal|protected|private)\s+"
    r"(?:static\s+|readonly\s+|const\s+|virtual\s+|override\s+|async\s+|new\s+|extern\s+)*"
    r"(?:[\w<>\[\]\.\?,\s]+?\s+)?"
    r"(\w+)\s*(?:\(|\{|=>|=[^=]|;)")


def corpo_do_tipo(src, pos_decl):
    """Devolve (inicio, fim) do corpo entre chaves a partir da declaração."""
    i = src.find("{", pos_decl)
    if i < 0:
        return None
    profundidade = 0
    for j in range(i, len(src)):
        if src[j] == "{":
            profundidade += 1
        elif src[j] == "}":
            profundidade -= 1
            if profundidade == 0:
                return (i + 1, j)
    return None


def recolher_tipos(fontes):
    """{Tipo: set(membros)} respeitando tipos aninhados."""
    tipos = defaultdict(set)

    for nome, limpo in fontes.items():
        for m in DECL_TIPO.finditer(limpo):
            especie, tipo = m.group(1), m.group(2)
            faixa = corpo_do_tipo(limpo, m.end())
            if not faixa:
                continue
            ini, fim = faixa
            corpo = limpo[ini:fim]

            if especie == "enum":
                for val in re.findall(r"(\w+)\s*(?:=\s*[^,}]+)?\s*(?:,|$)", corpo):
                    tipos[tipo].add(val)
                continue

            # Remove os corpos dos tipos aninhados para não roubar membros
            interior = corpo
            for mn in DECL_TIPO.finditer(corpo):
                f2 = corpo_do_tipo(corpo, mn.end())
                if f2:
                    interior = interior.replace(corpo[f2[0]:f2[1]], " ")

            # Só o primeiro nível: apaga corpos de métodos
            for mm in MEMBRO.finditer(interior):
                tipos[tipo].add(mm.group(1))

            # Propriedades auto e campos com inicializador genérico
            for mm in re.finditer(
                    r"\b(?:public|internal|protected)\s+[\w<>\[\]\.\?,\s]+?\s+(\w+)\s*(?:\{\s*get|=)",
                    interior):
                tipos[tipo].add(mm.group(1))

            # Declarações em série: public const int A = 1, B = 2, C = 3;
            for decl in re.finditer(
                    r"\b(?:public|internal|protected)\s+(?:static\s+|readonly\s+|const\s+)*"
                    r"[\w<>\[\]\.\?]+\s+([^;{}()]+);", interior):
                corpo_decl = decl.group(1)
                if "," not in corpo_decl:
                    continue
                for parte in corpo_decl.split(","):
                    nome = parte.strip().split("=")[0].strip()
                    if re.fullmatch(r"\w+", nome):
                        tipos[tipo].add(nome)

    return tipos


def check_membros(fontes, tipos):
    nossos = set(tipos) - {"Commands"}   # Commands tem muitos aliases
    for nome, limpo in fontes.items():
        for m in re.finditer(r"\b([A-Z]\w+)\.(\w+)\b", limpo):
            tipo, membro = m.group(1), m.group(2)
            if tipo not in nossos:
                continue
            if membro in tipos[tipo]:
                continue
            linha = limpo[:m.start()].count("\n") + 1
            erros.append("%s:%d  %s.%s não existe em %s"
                         % (nome, linha, tipo, membro, tipo))


def check_membros_de_variaveis(fontes, tipos):
    """
    Membros acedidos por uma VARIAVEL local, nao pelo nome do tipo.

    O check_membros so ve "Tipo.membro" — acesso estatico. Nunca via
    "b.Alcados", com b a ser um local. E foi precisamente assim que passou
    despercebido um campo que eu apaguei sem querer ao editar uma classe:
    o verificador deu tudo verde e o erro so apareceu no compilador, com
    a compilacao ja a meio.

    Como funciona: procura "var x = new Tipo" e "Tipo x = new Tipo" no
    ficheiro, e depois verifica cada "x.membro" contra os membros
    conhecidos desse tipo.

    Cautela deliberada — so acusa quando:
      * o tipo e NOSSO (dos que o recolher_tipos conhece);
      * o nome da variavel tem UM so tipo em todo o ficheiro. Se o mesmo
        nome for reutilizado com tipos diferentes, cala-se. Um verificador
        que grita a torto e a direito deixa de ser lido, e isso e pior do
        que nao verificar nada.
    """
    nossos = set(tipos) - {"Commands"}

    # O tipo pode vir qualificado: "new System.Windows.Forms.ColorDialog()".
    # Aceitar so o nome simples fazia essas declaracoes passarem despercebidas,
    # e entao um "dlg" que e VaoDialog num metodo e ColorDialog noutro parecia
    # ter um tipo unico. Deu tres falsos positivos a primeira vez que corri
    # isto — o proprio verificador precisou de ser verificado.
    DECL = re.compile(
        r'\b(?:var|[\w<>\[\],\.]+)\s+(\w+)\s*=\s*new\s+([A-Z][\w\.]*)\s*[\({]')

    # O DECL so ve o "= new Tipo". Um foreach declara a variavel sem new
    # nenhum, e o tipo dela vem da lista — que o verificador nao sabe ler.
    FOREACH = re.compile(
        r'\bforeach\s*\(\s*(?:var|[\w<>\[\],\.\?]+)\s+(\w+)\s+in\b')

    for nome, limpo in fontes.items():
        # variavel -> conjunto de tipos que lhe foram atribuidos
        tipos_de = defaultdict(set)
        for m in DECL.finditer(limpo):
            var, tipo = m.group(1), m.group(2).split(".")[-1]
            tipos_de[var].add(tipo)

        # Marca as do foreach com um tipo que nao e nosso. Assim, um nome
        # curto reaproveitado — o "p" que e Parede num metodo e Point3d no
        # foreach do metodo seguinte — deixa de ter tipo unico e cala-se,
        # como manda a cautela desta funcao. Era daqui que vinham os quatro
        # falsos positivos antigos: p.DistanceTo, p.X, p.Y e p.Z.
        for m in FOREACH.finditer(limpo):
            tipos_de[m.group(1)].add("?foreach")

        # so as que tem um tipo unico E que e nosso
        fiaveis = {v: list(t)[0] for v, t in tipos_de.items()
                   if len(t) == 1 and list(t)[0] in nossos}
        if not fiaveis:
            continue

        for m in re.finditer(r"\b([a-z_]\w*)\.(\w+)\b", limpo):
            var, membro = m.group(1), m.group(2)
            tipo = fiaveis.get(var)
            if tipo is None:
                continue
            if membro in tipos[tipo]:
                continue
            # Métodos herdados de object e de listas não estão no nosso mapa.
            if membro in ("ToString", "Equals", "GetHashCode", "GetType",
                          "Count", "Add", "Clear", "Contains", "Remove"):
                continue

            # O verificador não resolve a hierarquia completa do WinForms.
            # A exceção é limitada às classes conhecidas do projeto, para não
            # esconder um Close/Dispose/Focus inválido num modelo de domínio.
            winforms = {
                "LicencaDialog", "PrivacidadeDialog", "VaosDialog",
                "VaoDialog", "ContagemControl", "MedPanelControl",
                "FachadaControl"
            }
            if tipo in winforms and membro in (
                    "ShowDialog", "Show", "Close", "Dispose", "Focus",
                    "Activate", "Refresh", "Invalidate"):
                continue
            linha = limpo[:m.start()].count("\n") + 1
            erros.append("%s:%d  %s.%s nao existe em %s (%s e %s)"
                         % (nome, linha, var, membro, tipo, var, tipo))


# -------------------------------------------------------------- comandos
def check_comandos(fontes):
    declarados = {}
    for nome, limpo in fontes.items():
        for m in re.finditer(r'CommandMethod\(\s*""', limpo):
            pass  # literais foram limpos; usar o original abaixo
    for nome in fontes:
        bruto = ler(nome)
        # Aceita [CommandMethod("X")] e [CommandMethod("X", CommandFlags...)]
        for m in re.finditer(r'\[CommandMethod\(\s*"([^"]+)"\s*[,)]', bruto):
            cmd = m.group(1).upper()
            if cmd in declarados:
                erros.append('Comando duplicado "%s" (%s e %s)'
                             % (cmd, declarados[cmd], nome))
            declarados[cmd] = nome

    chamados = set()
    for nome in fontes:
        bruto = ler(nome)
        for m in re.finditer(r'RunCommand\("([A-Z]+)\s*"\)', bruto):
            chamados.add(m.group(1).upper())
        for m in re.finditer(r'(?:CommandParameter|BotaoGrande\([^,]+,[^,]+),\s*"([A-Z]+)\s*"', bruto):
            if m.group(1):
                chamados.add(m.group(1).upper())
        for m in re.finditer(r'"([A-Z]{3,})\s"', bruto):
            t = m.group(1).upper()
            if t.startswith("TSK") or t.startswith("MED"):
                chamados.add(t)

    for cmd in sorted(chamados):
        if cmd not in declarados:
            erros.append('Comando "%s" é chamado pela UI mas não está declarado' % cmd)
    return declarados


# ------------------------------------------------------------ ambiguidade
def check_tipos_importados(fontes):
    """
    Tipo usado sem o namespace importado e sem alias -> CS0103
    ("O nome X não existe no contexto atual").
    """
    for nome in fontes:
        bruto = ler(nome)
        usings = set(re.findall(r"^\s*using\s+([\w\.]+);", bruto, re.M))
        aliases = set(re.findall(r"^\s*using\s+(\w+)\s*=", bruto, re.M))
        limpo = fontes[nome]

        for ns, tipos in TIPOS_POR_NAMESPACE.items():
            if ns in usings:
                continue                      # namespace inteiro importado: ok
            # Só faz sentido avisar se o ficheiro já usa aliases desse namespace
            usa_aliases_do_ns = any(
                re.search(r"^\s*using\s+\w+\s*=\s*" + re.escape(ns) + r"\.", bruto, re.M)
                for _ in [0])
            if not usa_aliases_do_ns:
                continue

            for tipo in tipos:
                if tipo in aliases:
                    continue
                m = re.search(r"(?<![\w\.])" + tipo + r"(?![\w])", limpo)
                if m:
                    linha = limpo[:m.start()].count("\n") + 1
                    erros.append(
                        "%s:%d  CS0103: '%s' usado sem alias nem 'using %s;'"
                        % (nome, linha, tipo, ns))


def check_ambiguidade(fontes):
    """
    CS0104: tipo usado sem qualificação quando os dois namespaces que o
    definem estão importados. Qualquer ocorrência não qualificada é erro.
    """
    for nome in fontes:
        bruto = ler(nome)
        usings = set(re.findall(r"^\s*using\s+([\w\.]+);", bruto, re.M))
        # 'using X = A.B.C;' resolve a ambiguidade desse nome
        aliases = set(re.findall(r"^\s*using\s+(\w+)\s*=", bruto, re.M))
        limpo = fontes[nome]

        for tipo, (ns1, ns2) in AMBIGUOS.items():
            if ns1 not in usings or ns2 not in usings:
                continue
            if tipo in aliases:
                continue   # já foi desambiguado por alias

            for m in re.finditer(r"(?<![\w\.])" + tipo + r"(?![\w])", limpo):
                linha = limpo[:m.start()].count("\n") + 1
                erros.append(
                    "%s:%d  CS0104: '%s' é ambíguo (%s vs %s) — escreva o nome completo"
                    % (nome, linha, tipo, ns1, ns2))


# ------------------------------------------------------------------ main
def check_bytes_controlo(nome):
    """
    Bytes de controlo em cru dentro do ficheiro.

    O separador que usamos entre codigo e descricao e o 0x1F, e escreve-lo
    em cru num literal C# funciona — ate alguem copiar o ficheiro, ou uma
    ferramenta o normalizar, e o byte desaparecer sem deixar rasto. Ja
    aconteceu, e o sintoma foi um separador perdido que so apareceu na folha
    de medicao.

    Pior ainda: um 0x00 num literal compila e passa despercebido para
    sempre. Todos os separadores tem de ser escritos em escape ("\\u001f").
    """
    try:
        with open(nome, "rb") as f:
            b = f.read()
    except IOError:
        return

    maus = {}
    linha = 1
    for x in b:
        v = x if isinstance(x, int) else ord(x)
        if v == 10:
            linha += 1
            continue
        if v < 9 or v in (11, 12) or (14 <= v <= 31):
            maus.setdefault(v, []).append(linha)

    for v, linhas in sorted(maus.items()):
        erros.append(
            "%s:%d: byte de controlo 0x%02X em cru (%dx). "
            "Escreva-o em escape, p.ex. \"\\u%04x\" — em cru desaparece "
            "numa copia sem deixar rasto."
            % (nome, linhas[0], v, len(linhas), v))


def check_xml():
    """
    Os ficheiros de build sao XML valido?

    Um comentario XML NAO pode conter dois tracos seguidos. Escrevi um
    desenho com "+--" dentro de um comentario do Version.props e o MSBuild
    recusou o ficheiro inteiro com "o arquivo era invalido" — sem dizer
    onde nem porque. O projeto deixou de compilar por causa de um
    comentario.

    Custa milissegundos verificar aqui e poupa uma tarde a procurar.
    """
    import xml.dom.minidom

    alvos = (glob.glob("*.csproj") + glob.glob("*.props") +
             glob.glob("Deploy/TSKTakeOff.bundle/*.xml"))

    for f in alvos:
        try:
            xml.dom.minidom.parse(f)
        except Exception as e:
            texto = str(e)
            pista = ""
            # A causa mais provavel, e a que a mensagem do MSBuild esconde.
            try:
                with io.open(f, encoding="utf-8", errors="replace") as fh:
                    conteudo = fh.read()
                maus = [c for c in re.findall(r"<!--(.*?)-->", conteudo, re.S)
                        if "--" in c]
                if maus:
                    pista = ("  PROVAVEL CAUSA: %d comentario(s) com dois "
                             "tracos seguidos. Um comentario XML nao os "
                             "aceita." % len(maus))
            except IOError:
                pass
            erros.append("%s: XML invalido — %s%s" % (f, texto, pista))


def check_powershell():
    """
    Scripts .ps1 acentuados sem BOM.

    O powershell.exe do Windows (5.1) le um .ps1 SEM BOM como ANSI, nao
    como UTF-8. Um script gravado em UTF-8 com acentos chega mangado, e ja
    fez o sincronizar-versao.ps1 sair com codigo 1 no meio de uma
    compilacao — com uma mensagem que nao diz nada sobre acentos.

    Duas saidas validas: gravar com BOM, ou nao usar acentos. A segunda e
    mais robusta, porque sobrevive a qualquer editor que normalize o
    ficheiro, e um script de build nao precisa de ser bonito.

    Verifica-se tambem o 'exit 0': um script chamado pelo build nunca deve
    poder fazer falhar a compilacao por sua conta.
    """
    for f in glob.glob("Deploy/**/*.ps1", recursive=True) + glob.glob("*.ps1"):
        try:
            with open(f, "rb") as fh:
                b = fh.read()
        except IOError:
            continue

        tem_bom = b[:3] == b"\xef\xbb\xbf"
        acentos = sum(1 for x in bytearray(b) if x > 127)

        if acentos and not tem_bom:
            erros.append(
                "%s: %d bytes acentuados e SEM BOM. O powershell.exe (5.1) "
                "le-o como ANSI e os acentos chegam mangados. Grave com BOM "
                "UTF-8, ou escreva o script so em ASCII." % (f, acentos))

        # Os scripts chamados pelo build nao devem poder falhar a compilacao.
        if "sincronizar" in f.lower() and not b.rstrip().endswith(b"exit 0"):
            avisos.append(
                "%s: nao termina com 'exit 0'. Um script de build que sai "
                "com codigo != 0 faz a compilacao avisar ou falhar." % f)


def check_versoes():
    """
    A versao esta escrita em tres sitios e tem de dizer o mesmo nos tres.

    A fonte e o Version.props; o build propaga para o .iss e para o
    PackageContents.xml. Mas o alvo que propaga corre PowerShell, e o
    PowerShell pode estar bloqueado por politica de execucao — nesse caso
    a propagacao falha em silencio (de proposito: nao vale a pena impedir
    alguem de compilar por causa disto).

    Entao a rede de seguranca e aqui. Entregar um instalador a dizer uma
    versao e uma DLL a dizer outra transforma qualquer pedido de suporte
    num exercicio de adivinhacao.
    """
    import io

    def ler_txt(p):
        try:
            with io.open(p, encoding="utf-8", errors="replace") as f:
                return f.read()
        except IOError:
            return None

    props = ler_txt("Version.props")
    if props is None:
        return  # projeto sem versionamento automatico; nada a verificar

    m_base = re.search(r"<VersaoBase>([^<]+)</VersaoBase>", props)
    m_canal = re.search(r"<VersaoCanal>([^<]*)</VersaoCanal>", props)
    if not m_base:
        erros.append("Version.props: nao encontrei <VersaoBase>")
        return

    base = m_base.group(1).strip()
    canal = (m_canal.group(1).strip() if m_canal else "")
    completa = base + ("-" + canal if canal else "")

    # A versao distribuida leva o build do dia atras ("1.0.0-rc1.9719"), e
    # esse numero muda todos os dias. Nao da para o verificador saber qual
    # e — nem deve: o que ele tem de garantir e que os tres sitios dizem a
    # MESMA coisa e que essa coisa comeca no que o Version.props decide.
    #
    # A forma exacta fica de fora de proposito. Se um dia a versao passar a
    # ser "1.0.0-rc1.9719.4", isto continua a servir; o que nao pode passar
    # e um instalador de uma base e um bundle de outra.
    alvos = [
        ("Deploy/Installer/TSKTakeOff.iss",
         r'#define\s+AppVersao\s+"([^"]*)"', "AppVersao do instalador",
         completa),
        ("Deploy/Installer/TSKTakeOff.iss",
         r'#define\s+AppVersaoNum\s+"([^"]*)"', "AppVersaoNum do instalador",
         base),
        ("Deploy/TSKTakeOff.bundle/PackageContents.xml",
         r'AppVersion\s*=\s*"([^"]*)"', "AppVersion do bundle",
         base),
    ]

    builds = {}
    for caminho, padrao, desc, prefixo in alvos:
        txt = ler_txt(caminho)
        if txt is None:
            continue
        m = re.search(padrao, txt)
        if not m:
            erros.append("%s: nao encontrei o %s" % (caminho, desc))
            continue

        valor = m.group(1).strip()
        if not valor.startswith(prefixo + "."):
            erros.append(
                "%s: %s diz %r, que nao comeca por %r. "
                "Compile uma vez (o build sincroniza), ou corra "
                "Deploy/sincronizar-versao.ps1."
                % (caminho, desc, valor, prefixo + "."))
            continue

        builds[desc] = valor[len(prefixo) + 1:]

    # Os tres tem de vir da MESMA compilacao. Divergirem quer dizer que a
    # sincronizacao correu para uns e nao para outros — que e precisamente
    # o caso que este verificador existe para apanhar, porque o
    # sincronizar-versao.ps1 nunca falha a compilacao.
    if len(set(builds.values())) > 1:
        erros.append(
            "os numeros de build nao concordam entre si: %s. "
            "Compile outra vez para os sincronizar."
            % ", ".join("%s=%s" % (k, v) for k, v in sorted(builds.items())))

    # A numerica nao pode levar o canal: o VersionInfoVersion do Inno e o
    # AppVersion do bundle so aceitam x.x.x.x, e um "-rc1" la dentro faz o
    # ISCC recusar o instalador inteiro.
    for desc in ("AppVersaoNum do instalador", "AppVersion do bundle"):
        v = builds.get(desc)
        if v is not None and not re.match(r"^\d+$", v):
            erros.append(
                "%s: a parte de build e %r e devia ser so digitos. "
                "Estes campos nao aceitam sufixos como '-rc1'." % (desc, v))

    # A folha vai buscar a ordem do articulado a um ponto de ligacao, para
    # poder ser testada sem AutoCAD. Se a ligacao se perder, ela ordena como
    # se nao houvesse mapa importado — e NAO SE QUEIXA, porque nao ter mapa e
    # um estado legitimo. A medicao sai fora da ordem do mapa do cliente e so
    # se da por isso na conferencia, do outro lado.
    if glob.glob("FolhaMedicao.cs"):
        ligada = False
        for f in glob.glob("*.cs"):
            txt = ler_txt(f) or ""
            if re.search(r"FolhaMedicao\.OrdemDoArtigo\s*=\s*MapaQuantidades\.OrdemDe",
                         txt):
                ligada = True
                break
        if not ligada:
            erros.append(
                "ninguem liga a FolhaMedicao.OrdemDoArtigo ao "
                "MapaQuantidades.OrdemDe. Sem essa linha (esta no "
                "PluginInit.Initialize) a folha ignora o mapa de quantidades "
                "em silencio e a medicao sai fora da ordem do articulado.")

        # A mesma rede para a chave canonica. Sem ela, duas medicoes do mesmo
        # artigo com a chave guardada de formas diferentes dao DOIS blocos na
        # folha, com o cabecalho do artigo repetido.
        canonica = False
        for f in glob.glob("*.cs"):
            txt = ler_txt(f) or ""
            if re.search(r"FolhaMedicao\.ArtigoCanonico\s*=\s*"
                         r"MapaQuantidades\.ChaveCanonica", txt):
                canonica = True
                break
        if not canonica:
            erros.append(
                "ninguem liga a FolhaMedicao.ArtigoCanonico ao "
                "MapaQuantidades.ChaveCanonica. Sem essa linha o mesmo artigo "
                "pode sair em dois blocos na folha, com o cabecalho repetido.")

    # Ninguem deve voltar a escrever a versao a mao no csproj.
    for proj in glob.glob("*.csproj"):
        txt = ler_txt(proj) or ""
        if re.search(r"<Version>\s*\d", txt):
            erros.append(
                "%s: tem <Version> escrito a mao. A versao vem do "
                "Version.props — dois sitios a decidir e como isto "
                "comecou a divergir." % proj)


def main():
    nomes = ficheiros()
    if not nomes:
        print("Sem ficheiros .cs")
        return 1

    fontes = {n: sem_texto(ler(n)) for n in nomes}

    for n in nomes:
        check_chaves(n, fontes[n])
        check_modaldialog(n, fontes[n])
        check_bytes_controlo(n)

    check_escopo(nomes, fontes)

    tipos = recolher_tipos(fontes)
    check_membros(fontes, tipos)
    check_membros_de_variaveis(fontes, tipos)
    cmds = check_comandos(fontes)
    check_ambiguidade(fontes)
    check_xml()
    check_powershell()
    check_versoes()
    check_tipos_importados(fontes)

    print("Ficheiros analisados : %d" % len(nomes))
    print("Tipos encontrados    : %d" % len(tipos))
    print("Comandos AutoCAD     : %d (%s)"
          % (len(cmds), ", ".join(sorted(cmds))))
    print()

    if avisos:
        print("AVISOS (%d):" % len(avisos))
        for a in avisos:
            print("  ~ " + a)
        print()

    if erros:
        print("ERROS (%d):" % len(erros))
        for e in erros:
            print("  x " + e)
        return 1

    print("Sem erros estruturais detectados.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
