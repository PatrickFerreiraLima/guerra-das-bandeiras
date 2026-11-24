# Brasil: Guerra das Bandeiras — protótipo

Jogo de estratégia em tempo real feito em **C# / .NET 8 + MonoGame**, seguindo o caderno
`Brasil Guerra das Bandeiras - Caderno Unificado.docx` (concepção, gameplay e identidade visual num só documento).

## Como rodar

**Jeito mais fácil:** dê dois cliques em `Brasil Guerra das Bandeiras.exe` na raiz do projeto. É um arquivo único, já com o .NET e todos os assets dentro — não precisa instalar nada.

Para gerar esse executável de novo depois de mudar o código:

```bash
cd GuerraDasBandeiras
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o ../publicar
```

Para desenvolver:

Precisa do .NET SDK 8 ou mais novo.

```bash
cd GuerraDasBandeiras
dotnet run
```

Ou abra `GuerraDasBandeiras.sln` no Visual Studio / Rider e aperte F5.

Atalhos de linha de comando úteis no desenvolvimento:

| Comando | O que faz |
|---|---|
| `dotnet run -- --quick` | Pula o menu e abre uma escaramuça |
| `dotnet run -- --sheet folha.png` | Gera uma folha com todos os sprites numerados (para escolher assets) |
| `dotnet run -- --chars personagens.png` | Gera uma folha ampliada com todos os personagens em pixel art (por facção e com o time azul do espelho) |
| `dotnet run -- --shot img.png game 600` | Teste automático: IA x IA por 600 s simulados, salva captura + relatório `.txt` |
| `dotnet run -- --shot img.png battle 5` | Captura de uma batalha montada na Bandeira do Sul |
| `GB_FOCUS=31,19 dotnet run -- --shot img.png scenery 2` | Captura com o mapa revelado, enquadrando o tile informado (bom para revisar cenário) |
| `dotnet run -- --shot host.png net_host 8` e, em outro terminal, `dotnet run -- --shot conv.png net_join 8` | Dois jogos de verdade: um cria a sala, o outro entra pelo 127.0.0.1; cada um salva sua tela após 8 s de partida em rede |
| `dotnet run -- --test resultado.txt` | Roda os 48 testes automáticos (regras, partidas, mouse/teclado simulados, sons, seleção, introdução, tela final, referência de balanceamento e rede 1x1: lockstep, reconexão, chat, código da sala) |
| `dotnet run -- --balance saida 30` | Diagnóstico de balanceamento: 180 partidas IA x IA (3 confrontos x 2 posições x 30 sementes), gera `saida.csv` e `saida.md` |
| `dotnet run -- --snapshot Assets/Testes/balanco_referencia.txt` | Regrava a referência de balanceamento (só quando mudar os números de propósito) |

Resolução de teste: defina `GB_SIZE=1366x768` antes de rodar.

## Jogar 1x1 em rede (sem servidor)

No menu, **Jogar 1x1 em rede**. Cada um digita o nome (ele vira o nome da sala; nomes repetidos podem) e escolhe a facção (pode ser a mesma). Só precisa do `.exe`: nada para instalar.

- **Criar sala**: você vira o anfitrião. A sala aparece sozinha para quem procurar na mesma rede. Para quem está em outra casa, o jogo tenta abrir a porta do roteador sozinho (UPnP) e mostra um **código da sala** (ex.: `SC07-2B80`) com botão **Copiar código**.
- **Entrar numa sala**: clique numa sala da lista (mesma rede) ou digite o **código** (ou o IP) e clique em Conectar.
- **Se o roteador não abrir a porta** (UPnP desligado) a tela avisa: dá para ligar o UPnP no roteador (geralmente em 192.168.1.1), usar Radmin VPN/ZeroTier, ou liberar a porta **TCP 27027** manualmente. Com **CGNAT** (operadora divide o IP) só VPN ou um servidor resolvem — o jogo detecta e avisa.
- **Chat**: Enter abre, Enter manda, Esc fecha.
- **Queda de conexão**: a partida congela e o jogo reconecta sozinho por até 60 s (o convidado liga de novo para o anfitrião); ao voltar, as ordens que faltaram são reenviadas e a partida segue de onde parou.
- Na primeira vez o Windows pergunta se o jogo pode usar a rede: permita em redes privadas.
- Mesmo objetivo do jogo solo. Não há pausa (o menu abre, mas a partida segue). Sair = desistir: o outro vence por abandono (também se a conexão não voltar em 60 s).
- Os dois precisam do **mesmo executável** e do mesmo `balance.json` (o jogo confere ao entrar).

Como funciona: as duas máquinas rodam a mesma simulação em passos fixos (*lockstep*, 30 por segundo) e só trocam as ordens dos jogadores, aplicadas com ~133 ms de atraso nos dois lados ao mesmo tempo. A cada segundo comparam um checksum do estado; se divergirem, a partida é encerrada com aviso. Código em `GuerraDasBandeiras/Net/` e `Gameplay/Commands.cs`.

## Balanceamento sem recompilar

Todos os números de habilidades, comandantes, bandeiras, limites e bônus de facção ficam em
`GuerraDasBandeiras/Assets/balance.json`. Edite, salve e abra o jogo de novo.

## O que já está no jogo (demo do caderno)

- **3 facções** com bônus, ponto fraco e unidade especial:
  Amazonas (Vigia da Mata), Minas Gerais (Guardião das Gerais), Rio Grande do Sul (Lanceiro dos Pampas).
- **3 recursos**: comida (frutas), madeira (árvores), minério (rochas).
- **6 construções**: Centro de Comando, Casa, Coletor, Quartel, Torre, Oficina.
- **5 unidades compartilhadas + 3 especiais**: Trabalhador, Espadachim, Lanceiro, Atirador, Batedor.
- **Vantagens**: lanceiro vence cavalaria; atirador vence infantaria lenta; infantaria alcança atiradores.
- **Terreno**: mata esconde batedores, serra dá visão/alcance extra, rio só se cruza nas partes rasas.
- **Mapa "Encontro das Fronteiras"**: mata a oeste, serra a leste, planície ao sul, rio com duas travessias.
- **Vitória**: dominar as 2 bandeiras por 90 s ou destruir o Centro inimigo.
- **Tutorial rápido** (6 passos, com destaque dourado no elemento de cada etapa).
- **Bandeiras com bônus**: Sul = econômica (+10% comida), Norte = militar (+10% resistência); só soldados capturam.
- **Arquitetura e acessórios por facção**: palafitas e folhagem (AM), pedra e mineração (MG), cercas, feno e moinho (RS).
- **Missão guiada "A primeira bandeira"** com dicas passo a passo e contra-ataque final.
- **Habilidades especiais** (tecla V): Camuflagem (Vigia da Mata), Postura de Pedra (Guardião das Gerais), Investida (Lanceiro dos Pampas).
- **Comandantes** (tecla C), do GDD: Mara das Águas (Véu da Mata), Tomás Ferraz (Fortificar), Helena Dornelles (Ataque Coordenado), cada um com um passivo.
- **Aríete** (cerco leve), reparo de construções, ordens em fila com Shift, câmera WASD.
- **IA** com estados desenvolver → reconhecer → atacar → recuar/defender, que usa habilidades e poderes, e 3 dificuldades.
- Névoa de guerra, minimapa, grupos de controle, filas de produção, ponto de encontro, atacar-mover.
- Escala da interface e volume ajustáveis (Esc), ajuda (F1), tela cheia (F11).
- **Personagens em pixel art por facção** (gerados em `Core/CharArt.cs`): cocar de penas de arara, colete de folhas e urucum (AM); elmo de ferro, túnica com o triângulo de Minas, mineiro de lanterna e tropeiro (MG); chapéu campeiro, poncho, lenço e bombacha (RS). A cor do time aparece no lenço, na faixa ou no cinto.
- **Paisagem brasileira** (só visual, `Gameplay/Scenery.cs`): coqueiros na beira do rio, ipês amarelos, roxos e brancos (alguns com tucano), vitórias-régias, capivaras, revoadas de araras; açaizeiros e bananeiras perto da base do Amazonas, igreja barroca colonial em Minas, araucárias e gado nos pampas.
- **Logo oficial** no menu e como ícone do executável e da janela (`GuerraDasBandeiras/Icon.ico`, gerado a partir de `Guerra das Bandeiras_ Heróis do Brasil.png`).

## Estrutura do código

```
GuerraDasBandeiras/
  Core/        Assets (carrega PNG/OGG/TTF direto do disco), Câmera, Input, desenho e UI
  Gameplay/    Regras (Defs), Mundo, Unidades, Pathfinding A*, Mapa, IA, Missão, Renderizador
  Screens/     Menu, escolha de facção, partida (HUD) e tela final
  Tools/       Ferramentas de teste (folha de sprites, capturas automáticas)
  Assets/      Sprites, fontes e sons
```

Todos os números de balanceamento (custos, vida, dano, velocidade, bônus) ficam em
`Gameplay/Defs.cs` — é o lugar para ajustar depois dos testes.

## Créditos dos assets

Cenário, prédios, sons e fontes de título: [Kenney.nl](https://kenney.nl), licença **CC0** (domínio público, uso comercial livre) —
Medieval RTS, Kenney Fonts, Interface Sounds, Impact Sounds, RPG Audio e Music Jingles (licenças em `Assets/Audio/LICENSE-kenney-*.txt`).
Ambiência (vento, pássaros) gerada por código em `Core/Ambience.cs`, sem arquivos externos.
Fonte de texto: Nunito (SIL Open Font License, `Assets/Fonts/LICENSE-Nunito-OFL.txt`).
As bandeiras estaduais (`Core/FlagArt.cs`), os personagens (`Core/CharArt.cs`) e a paisagem brasileira (`Gameplay/Scenery.cs`) são desenhados por código, estilizados.
Logo: arte "Guerra das Bandeiras: Heróis do Brasil" do projeto (`Assets/Images/logo.png` e ícones `Icon.ico`/`Icon.bmp`).

Relatórios de balanceamento: `docs/balanceamento/`.
