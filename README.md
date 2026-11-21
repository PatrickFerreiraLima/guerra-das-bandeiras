# Brasil: Guerra das Bandeiras — protótipo

Jogo de estratégia em tempo real feito em **C# / .NET 8 + MonoGame**, seguindo o caderno de conceito
`Brasil Guerra das Bandeiras Demo.docx`.

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
| `dotnet run -- --shot img.png game 600` | Teste automático: IA x IA por 600 s simulados, salva captura + relatório `.txt` |
| `dotnet run -- --shot img.png battle 5` | Captura de uma batalha montada na Bandeira do Sul |
| `dotnet run -- --test resultado.txt` | Roda os 40 testes automáticos (regras, partidas, mouse/teclado simulados, sons, seleção, introdução, tela final, referência de balanceamento) |
| `dotnet run -- --balance saida 30` | Diagnóstico de balanceamento: 180 partidas IA x IA (3 confrontos x 2 posições x 30 sementes), gera `saida.csv` e `saida.md` |
| `dotnet run -- --snapshot Assets/Testes/balanco_referencia.txt` | Regrava a referência de balanceamento (só quando mudar os números de propósito) |

Resolução de teste: defina `GB_SIZE=1366x768` antes de rodar.

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

Todos de [Kenney.nl](https://kenney.nl), licença **CC0** (domínio público, uso comercial livre):
Medieval RTS, Kenney Fonts, Interface Sounds, Impact Sounds, RPG Audio e Music Jingles (licenças em `Assets/Audio/LICENSE-kenney-*.txt`).
Ambiência (vento, pássaros) gerada por código em `Core/Ambience.cs`, sem arquivos externos.
Fonte de texto: Nunito (SIL Open Font License, `Assets/Fonts/LICENSE-Nunito-OFL.txt`).
As bandeiras estaduais são desenhadas por código, estilizadas (`Core/FlagArt.cs`).

Relatórios de balanceamento: `docs/balanceamento/`.
Medieval RTS, Kenney Fonts, Interface Sounds, Impact Sounds, RPG Audio.
