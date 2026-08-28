# TSK TakeOff

## Plugin de medições para AutoCAD

**Desenvolvido pela TSK Digital**

A TSK Digital apresenta o **TSK TakeOff**, um plugin criado para tornar o processo de medição de obras mais rápido, organizado e confiável, diretamente no AutoCAD.

Com o plugin, o profissional mede os elementos da construção no próprio desenho, visualiza as medições de forma clara e envia os resultados para o Excel, já organizados em uma folha de medição.

> **TSK Digital — medir, conferir e entregar.**

---

## O que o TSK TakeOff faz?

O TSK TakeOff transforma a geometria do desenho em informações de medição organizadas.

Ao realizar uma medição, o plugin pode:

- calcular comprimentos, áreas e volumes;
- identificar e registrar automaticamente cada medição;
- criar hachuras e organizar os elementos em layers próprias;
- aplicar cores por piso ou categoria para facilitar a conferência;
- detectar portas e janelas e descontar os vãos conforme a regra escolhida;
- guardar as medições no próprio arquivo DWG;
- atualizar uma planilha Excel em tempo real;
- exportar uma folha de medição em formato `.xlsx`;
- organizar os resultados de acordo com o mapa de quantidades do cliente.

O objetivo é reduzir o trabalho manual, evitar transcrições repetitivas e facilitar a conferência entre o desenho, as medições e a planilha final.

---

## Principais funcionalidades

### 1. Medição de paredes de alvenaria

O plugin permite medir paredes de diferentes formas:

- **Parede por retângulo:** dois cliques nos cantos da parede. O comprimento e a espessura são obtidos da geometria desenhada.
- **Parede por polyline:** ideal para paredes com percursos irregulares ou com vários segmentos.
- **Medição a partir de elementos já desenhados:** permite selecionar hachuras ou polylines existentes no projeto, sem precisar redesenhar a geometria.

A altura, a espessura, o serviço, o piso e o alçado podem ser configurados antes da medição.

### 2. Medição de áreas e volumes

Para serviços como betonilhas, enchimentos, impermeabilizações e outras camadas, o plugin calcula:

> **Área × altura ou espessura = volume**

A área pode ser desenhada no momento da medição ou selecionada diretamente a partir de hachuras e contornos fechados já existentes no projeto.

Quando a medição é feita sobre elementos existentes, o desenho original é preservado. O plugin trabalha com cópias destinadas ao controle das medições.

### 3. Fachadas, ETICS e revestimentos

O TSK TakeOff também permite medir materiais e serviços de fachada:

- medição por retângulo em alçado;
- medição por polyline em planta multiplicada pela altura do piso;
- organização por material, alçado e piso;
- visualização por cores para facilitar a identificação dos elementos medidos.

Essa funcionalidade pode ser utilizada para fachadas, ETICS, revestimentos e outros serviços medidos em metros quadrados.

### 4. Medições lineares

Para serviços medidos em metros lineares, o plugin permite medir percursos como:

- rodapés;
- tubulações;
- remates;
- perfis;
- outros elementos lineares do projeto.

### 5. Contagem de blocos

O plugin permite selecionar blocos existentes no desenho e contabilizá-los por categoria e piso.

Pode ser utilizado, por exemplo, para contar:

- portas;
- janelas;
- tomadas;
- luminárias;
- louças sanitárias;
- equipamentos;
- outros componentes representados por blocos.

Cada elemento contado pode ser marcado no desenho, facilitando a conferência visual e evitando que itens sejam contados duas vezes.

### 6. Detecção e desconto de vãos

Ao medir uma parede, o TSK TakeOff pode procurar portas e janelas representadas por textos, atributos ou blocos no desenho.

Os vãos encontrados são apresentados para conferência. O usuário pode confirmar, corrigir ou remover qualquer item antes da geração dos resultados.

O plugin permite escolher a regra de desconto:

- **Descontar todos os vãos**;
- **Regra SINAPI:** vãos até 2 m² não são descontados e, acima desse limite, é descontado apenas o excedente;
- **Não descontar vãos:** mantém a área bruta da parede.

Também é possível adicionar vãos manualmente quando eles não estiverem identificados automaticamente.

### 7. Mapa de quantidades do cliente

O TSK TakeOff pode importar o articulado ou mapa de quantidades enviado pelo cliente.

A importação traz a estrutura dos capítulos, subcapítulos e artigos, mas não importa quantidades ou fórmulas. Os valores da folha continuam sendo calculados a partir das medições realizadas no desenho.

Depois da importação, o usuário pode escolher o artigo correspondente diretamente no painel. Assim, as medições já ficam associadas ao código e à descrição corretos.

Também é possível:

- reclassificar medições já realizadas;
- alterar o artigo de várias medições ao mesmo tempo;
- identificar medições ainda não classificadas;
- gerar a folha na ordem do articulado do cliente.

Essa função reduz erros de digitação e evita que o mesmo artigo seja escrito de formas diferentes ao longo da obra.

### 8. Excel ao vivo

Com o recurso **Excel ao Vivo**, a folha de medição é atualizada enquanto o trabalho é realizado no AutoCAD.

Isso permite acompanhar imediatamente:

- as medições lançadas;
- os descontos de vãos;
- os subtotais por piso ou alçado;
- os totais por serviço;
- as áreas, quantidades e volumes calculados.

O Excel pode trabalhar em uma folha simples ou em um modelo fornecido pela empresa.

### 9. Exportação para Excel

Ao final do trabalho, o plugin pode gerar um arquivo `.xlsx` com:

- capítulos e artigos;
- descrição dos serviços;
- unidades de medida;
- quantidades;
- dimensões;
- parciais;
- subtotais;
- totais;
- descontos de portas e janelas.

Quando a empresa possui um modelo próprio, o TSK TakeOff trabalha sobre uma cópia do arquivo, preservando o modelo original, suas fórmulas e suas macros.

---

## Como funciona o fluxo de trabalho?

### 1. Abrir o AutoCAD

Após a instalação, o plugin disponibiliza o separador **TSK TakeOff** na ribbon do AutoCAD e um painel lateral de medições.

### 2. Configurar a medição

Antes de medir, o usuário informa os dados necessários, como:

- serviço ou material;
- piso;
- alçado ou zona;
- etiqueta ou bloco;
- altura;
- espessura;
- regra de desconto dos vãos;
- artigo do mapa de quantidades, quando aplicável.

### 3. Medir no desenho

O usuário escolhe o tipo de medição e seleciona ou desenha a geometria correspondente no AutoCAD.

### 4. Conferir os resultados

As medições ficam identificadas no desenho e aparecem na grade do painel. Portas, janelas e outros descontos podem ser conferidos antes da consolidação.

### 5. Acompanhar no Excel

O usuário pode ligar o Excel ao vivo para acompanhar a folha durante a execução ou deixar para exportar o arquivo ao final.

### 6. Exportar e entregar

Ao terminar, o plugin gera a folha de medição em Excel, organizada conforme o padrão definido para a empresa ou conforme o mapa de quantidades do cliente.

---

## Onde as medições ficam armazenadas?

As medições são gravadas no próprio arquivo **DWG**, junto ao desenho.

Isso significa que:

- não é necessário manter uma base de dados separada para cada desenho;
- ao salvar o DWG, as medições são salvas junto com ele;
- o arquivo pode ser reaberto posteriormente com as informações preservadas;
- outro profissional com o plugin pode consultar as medições do desenho.

As layers, hachuras, identificações e cores também ajudam a visualizar o que já foi medido e a conferir o trabalho diretamente na planta.

---

## Benefícios para a empresa

### Mais produtividade

A medição e o lançamento na folha passam a fazer parte do mesmo fluxo, reduzindo tarefas repetitivas e a necessidade de alternar entre diferentes ferramentas.

### Menos erros de transcrição

Os valores calculados no desenho são enviados para a folha, reduzindo a digitação manual de comprimentos, áreas, quantidades e descontos.

### Melhor conferência

As medições ficam visíveis no DWG, organizadas por layers, hachuras, cores, pisos e serviços.

### Padronização

O resultado pode seguir o modelo de Excel já utilizado pela empresa, mantendo a estrutura, os campos e a forma de apresentação adotada internamente.

### Rastreabilidade

Cada medição fica relacionada à geometria do desenho e armazenada no próprio DWG, facilitando revisões e verificações futuras.

### Flexibilidade

O plugin pode ser utilizado em diferentes tipos de serviço: alvenaria, fachadas, ETICS, revestimentos, camadas, medições lineares e contagens.

---

## Compatibilidade e requisitos

- AutoCAD 2021 a 2026;
- Windows;
- arquivo de projeto em formato DWG;
- desenho configurado na unidade utilizada pela empresa, preferencialmente em metros;
- Microsoft Excel instalado somente para utilizar o recurso **Excel ao Vivo**;
- a exportação para `.xlsx` pode ser realizada sem manter o Excel aberto.

O plugin é executado dentro do AutoCAD e utiliza a geometria do desenho como base para os cálculos.

---

## Pontos importantes

O TSK TakeOff automatiza cálculos, organização e transferência de dados, mas a conferência técnica continua sendo uma etapa importante do processo.

A qualidade dos resultados depende de:

- precisão e organização do desenho;
- unidade correta do projeto;
- configuração adequada da altura e espessura;
- escolha correta do serviço ou artigo;
- validação dos vãos detectados;
- conferência final da folha de medição.

O plugin foi desenvolvido para apoiar o profissional responsável pela medição, tornando o processo mais rápido e controlado, sem substituir a sua análise técnica.

---

## Sobre a TSK Digital

A **TSK Digital** desenvolve soluções digitais para melhorar processos técnicos, reduzir tarefas manuais e conectar o desenho, a medição e a documentação de obra.

O TSK TakeOff foi criado com foco nas necessidades reais de profissionais de arquitetura, engenharia, preparação de obra e construção que trabalham diariamente com AutoCAD e Excel.

Nosso objetivo é oferecer uma ferramenta prática, integrada ao fluxo de trabalho existente e adaptável ao padrão de cada empresa.

---

## Licenciamento e suporte

As condições de licenciamento, implantação, treinamento e suporte são definidas pela **TSK Digital** de acordo com a necessidade de cada cliente.

Para conhecer o TSK TakeOff ou solicitar uma demonstração, entre em contato com a TSK Digital.

**TSK Digital**  
**TSK TakeOff — medir, conferir e entregar.**
