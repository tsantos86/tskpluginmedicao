# Análise — o piso como objecto, e a árvore da paleta

> Nota de estudo, 2026-08-21. **Nada disto foi implementado.** Ficou escrito
> para se decidir com calma, porque a parte que interessa é uma decisão de
> produto e não de código.
>
> Ponto de partida: a árvore do AltoQi Eberick (`EDIFICAÇÃO → Pavimentos →
> L1…L12 → Lajes / Pilares / Vigas / Escadas`) e a pergunta «podemos chegar a
> este nível, separando projecto, piso e as medições dentro?».

---

## 1. A lição do Eberick não é a árvore

O que faz aquela árvore funcionar não é o widget: é que ali **`Pavimento` é um
objecto**, com cota, pé-direito, ordem e a noção de pavimento-tipo que se
repete. A árvore é a vista disso.

Copiar a árvore sem copiar o objecto dá uma casca por cima do mesmo problema.

E há uma diferença de natureza que não se apaga: **o Eberick é dono do modelo**
— os desenhos são saída dele. O TSK TakeOff mede o desenho **dos outros**. Isso
não impede a árvore, mas decide o que ela pode afirmar.

---

## 2. O que está no código hoje

| O quê | Onde | Estado |
|---|---|---|
| Piso de uma medição | [Models.cs:99](Models.cs#L99) | `string`, sem mais nada |
| Piso por omissão | [Models.cs:389](Models.cs#L389) | `static string Piso = "PISO 0"` — global |
| Altura da medição | [Models.cs:449](Models.cs#L449) | `static double Altura = 2.80` — global do painel |
| Ordem dos pisos na folha | [FolhaMedicao.cs:326](FolhaMedicao.cs#L326) | ordem de desenho, desempate alfabético |
| Agrupamento artigo→serviço→alçado→piso | [FolhaMedicao.cs:310](FolhaMedicao.cs#L310) | já existe, e é o que a folha usa |

Não existe classe `Obra` nem `Projecto`. As medições vivem em XData dentro de
cada DWG. Um piso **não é nada**: é uma etiqueta escrita num campo.

---

## 3. As três consequências disso

### 3.1 A altura é global e erra em silêncio

A altura vem do painel, não do piso. Mede-se o PISO 0 a 2.80, sobe-se para um
piso de pé-direito 3.20, esquece-se de mudar o campo — e as medições saem a
2.80. **Nada apanha isto.** Numa torre é o erro que só aparece na conferência,
e é o tipo de erro que faz perder a confiança na ferramenta toda.

### 3.2 Os pisos não ordenam por cota

Ordena-se pela ordem em que foram desenhados, com desempate alfabético. Ou
seja `PISO 10` fica junto do `PISO 1`, não depois do `PISO 9`. Com dois pisos
ninguém repara; com doze, a folha sai baralhada.

### 3.3 Não há pavimento-tipo

É aqui que está o dinheiro. Numa torre, metade dos pisos são iguais. Hoje
medem-se todos.

---

## 4. Proposta: o piso como objecto

```
Piso { Nome, Ordem, Cota, PeDireito, RepeteVezes }
```

A medição continua a guardar o **nome** do piso como ligação. Nada muda no DWG
nem nas medições já feitas — é o ponto que torna isto incremental em vez de uma
migração.

Ganhos, por ordem de valor:

1. **A altura passa a vir do piso.** O campo global deixa de poder estar errado
   sem ninguém ver. Resolve 3.1.
2. **A ordem passa a ser a cota.** A folha sai de baixo para cima, como uma
   medição se lê. Resolve 3.2.
3. **A árvore torna-se natural**, porque passa a haver uma lista de pisos para
   mostrar. Deixa de ser uma funcionalidade e passa a ser uma consequência.
4. **O pavimento-tipo aparece quase de graça.** Resolve 3.3.

---

## 5. O aviso sério sobre o pavimento-tipo

**Ele multiplica quantidades.** Num programa de medição, um número que foi
multiplicado por cinco sem se ver onde é veneno: quem confere perde a confiança
e volta ao Excel — que é exactamente o que o produto existe para evitar.

Se for feito, tem de sair **auditável na folha**. Duas hipóteses a decidir
antes de escrever a primeira linha:

- uma coluna com o factor (`×5`) na linha do piso;
- ou as linhas repetidas explicitamente, uma por repetição.

Isto é decisão de produto, não de implementação.

---

## 6. O que NÃO copiar do Eberick

- **Prumada e continuidade vertical** — não querem dizer nada em medição.
- **Modelo 3D e cálculo** — ele precisa disso porque dimensiona estruturas.
  O TSK mede o que já está desenhado.

---

## 7. A árvore da paleta

Achievable e de risco baixo, **dentro de um desenho**. A hierarquia já existe
nos dados e o `OrdenarComoFolha` já a constrói.

Desenho proposto: **árvore à esquerda, a grelha actual à direita**, filtrada
pelo nó seleccionado. A grelha não serve só para ver — nela editam-se altura,
largura, espessura, piso e artigo, e selecciona-se em bloco para Reclassificar.
Uma árvore é má a editar. Assim ganha-se a navegação sem perder o que funciona.

**Ganho escondido:** a parte mais frágil da paleta hoje é o `_linhasVao` /
`_indiceVao` / `_linhasTitulo` ([Palette.cs:1350](Palette.cs#L1350)) —
dicionários que mapeiam *índice de linha* para «isto é um vão da medição nº 3».
É uma árvore simulada com números. Numa árvore a sério, cada nó é o objecto, e
esse mapeamento desaparece: **menos código, não mais.**

---

## 8. O nível «Projecto»

É outra conversa, e a única parte que mexe na arquitectura. Obriga a haver algo
que atravessa desenhos — um ficheiro de obra, ou uma convenção de pasta.

**A cautela:** a virtude actual, que o manual até promete, é que *cada medição
fica guardada dentro do próprio DWG; envia-se o DWG a um colega e ele vê as
medições*. Um ficheiro de projecto cria um **segundo sítio onde a verdade
vive**, e é daí que nascem os bugs de sincronização: alguém renomeia um DWG,
duas pessoas medem pisos diferentes ao mesmo tempo, o ficheiro de obra fica
numa pasta e os desenhos noutra.

Isto muda o que o produto **é**: de «uma ferramenta que mede um desenho» para
«uma ferramenta que guarda uma obra». Provavelmente é a decisão que define se
compete com o Eberick ou com uma folha de Excel bem feita.

---

## 9. Ordem sugerida

| # | Passo | Risco | Vale por si? |
|---|---|---|---|
| 1 | `Piso` como objecto (nome, ordem, cota, pé-direito) | baixo | sim — mata o erro silencioso da altura |
| 2 | Árvore à esquerda da grelha, dentro do desenho | baixo | sim — navegar 300 medições |
| 3 | Vãos e títulos como filhos a sério | baixo | sim — retira dívida técnica |
| 4 | Pavimento-tipo | médio | sim — mas só depois de decidido como se audita |
| 5 | Nível de projecto (multi-DWG) | **alto** | muda o produto |

---

## 10. Decisões que ficam à espera

1. **Pavimento-tipo: como aparece na folha?** Coluna com factor, ou linhas
   repetidas? Sem isto decidido, não começar.
2. **Onde vive o projecto**, se houver nível 5. Ficheiro próprio? Pasta? E quem
   ganha quando ele e o DWG discordam?
3. **Quando abrir esta frente.** Enquanto os pilotos ainda apanham hachuras que
   não aparecem e lentidão por medir, é confiança a sangrar — e funcionalidade
   nova não a repõe. O passo 1 e o 2 são os que dão demonstração forte com
   risco baixo.
