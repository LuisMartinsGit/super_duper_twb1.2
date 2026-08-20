// Loc.Pt.Menus.cs
// Portuguese (Portugal) table — MENU SCREENS AND OVERLAYS domain:
// main menu (authored MainMenu.unity labels), skirmish / multiplayer /
// scenarios panels, colour picker, map preview, loading screen, pause menu,
// victory screen, network status overlay, planning-mode overlay.
// Keys are the ENGLISH source strings exactly as authored / written at the
// call sites. Indexer only — duplicate keys across domains are expected.
// Location: Assets/Scripts/Core/Localization/Loc.Pt.Menus.cs

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddMenus(Dictionary<string, string> t)
        {
            t["Scene could not be loaded"] = "Não foi possível carregar a cena";

            // ── MainMenu.unity authored labels (scene localizer) ────────
            t["SINGLE PLAYER"] = "UM JOGADOR";
            t["MULTIPLAYER"] = "MULTIJOGADOR";
            t["SCENARIOS"] = "CENÁRIOS";
            t["SCENARIO"] = "CENÁRIO";
            t["SKIRMISH VS AI"] = "ESCARAMUÇA CONTRA IA";
            t["TRAINING GROUNDS"] = "CAMPO DE TREINO";
            t["BEGIN SKIRMISH"] = "INICIAR ESCARAMUÇA";
            t["CALL THE BANNERS"] = "CONVOCAR OS ESTANDARTES";
            // Skirmish footer: CANCEL is shared with the multiplayer
            // lobby below, START is the skirmish primary action.
            t["START"] = "INICIAR";
            t["START MATCH"] = "INICIAR PARTIDA";
            t["START SCENARIO"] = "INICIAR CENÁRIO";
            t["HOST GAME"] = "CRIAR JOGO";
            t["HOST GAME SETUP"] = "CONFIGURAÇÃO DO JOGO";
            t["JOIN GAME"] = "ENTRAR NUM JOGO";
            t["CREATE LOBBY"] = "CRIAR SALA";
            t["DIRECT CONNECT"] = "LIGAÇÃO DIRETA";
            t["AVAILABLE GAMES"] = "JOGOS DISPONÍVEIS";
            t["LOBBY"] = "SALA";
            t["CONNECTING..."] = "A LIGAR...";
            t["Please wait..."] = "Aguarde, por favor...";
            t["MATCH OPTIONS"] = "OPÇÕES DA PARTIDA";
            t["MAP OPTIONS"] = "OPÇÕES DO MAPA";
            t["STARTING AGE"] = "ERA INICIAL";
            t["STARTING RESOURCES"] = "RECURSOS INICIAIS";
            t["FOG OF WAR"] = "NÉVOA DE GUERRA";
            t["CURSE NODES"] = "NÓS DA MALDIÇÃO";
            t["MAP"] = "MAPA";
            t["MAP NAME"] = "NOME DO MAPA";
            t["GAME NAME"] = "NOME DO JOGO";
            t["YOUR NAME"] = "O SEU NOME";
            t["HOST IP"] = "IP DO ANFITRIÃO";
            t["PORT"] = "PORTA";
            t["PLAYER"] = "JOGADOR";
            t["PLAYERS"] = "JOGADORES";
            // Roster team dropdown (SkirmishPanel.TeamNames).
            t["NO TEAM"] = "SEM EQUIPA";
            t["TEAM 1"] = "EQUIPA 1";
            t["TEAM 2"] = "EQUIPA 2";
            t["TEAM 3"] = "EQUIPA 3";
            t["TEAM 4"] = "EQUIPA 4";
            t["HOST"] = "ANFITRIÃO";
            t["JOIN"] = "ENTRAR";
            t["AI"] = "IA";
            t["ON"] = "LIGADO";
            t["OFF"] = "DESLIGADO";
            // Authored with two spaces after the plus sign; the single-space
            // variant covers any re-authoring.
            t["+  ADD OPPONENT"] = "+  ADICIONAR ADVERSÁRIO";
            t["+ ADD OPPONENT"] = "+ ADICIONAR ADVERSÁRIO";
            t["< MAIN MENU"] = "< MENU PRINCIPAL";
            t["Begin with later units unlocked."] = "Começar com unidades mais avançadas desbloqueadas.";
            t["Capturable veilstone wells."] = "Poços de Veilstone capturáveis.";
            t["Scouts must uncover the map."] = "Os batedores têm de revelar o mapa.";
            t["Opening stockpile for all houses."] = "Reservas iniciais para todas as casas.";
            t["Browse games on the local network"] = "Procurar jogos na rede local";
            t["Open a LAN lobby others can join"] = "Abrir uma sala LAN a que outros se podem juntar";
            t["Enter text..."] = "Introduzir texto...";
            t["Game"] = "Jogo";
            t["Scenario"] = "Cenário";

            // Blue-menu entries (authored Title Case in the Synty prefab /
            // scene overrides; rendered uppercase by the font style).
            t["Skirmish"] = "Escaramuça";
            t["Multiplayer"] = "Multijogador";
            t["Scenarios"] = "Cenários";
            t["Campaign"] = "Campanha";
            t["Load Game"] = "Carregar Jogo";
            t["Load Game\n"] = "Carregar Jogo\n";   // authored with a trailing newline
            t["Settings"] = "Definições";
            // "Quit" / "QUIT": MenuQuitButton.IsQuitLabel matches the VISIBLE
            // label to wire Application.Quit — it also accepts "SAIR" so this
            // translation cannot unhook the button.
            t["Quit"] = "Sair";
            t["SETTINGS"] = "DEFINIÇÕES";
            t["QUIT"] = "SAIR";
            t["CAMPAIGN"] = "CAMPANHA";
            t["LOAD GAME"] = "CARREGAR JOGO";
            t["TUTORIAL"] = "TUTORIAL";

            // ── MultiplayerPanel ────────────────────────────────────────
            t["< BACK"] = "< VOLTAR";
            t["CANCEL LOBBY"] = "CANCELAR SALA";
            t["LEAVE LOBBY"] = "SAIR DA SALA";
            t["CANCEL"] = "CANCELAR";
            t["HOSTING: {0}"] = "A ALOJAR: {0}";   // retired: the lobby title is "MULTIPLAYER - <name>" now
            t["MULTIPLAYER - {0}"] = "MULTIJOGADOR - {0}";
            t["CONNECT"] = "LIGAR";
            t["No free game port in {0}-{1}. Close other game instances."] = "Sem porta livre em {0}-{1}. Fecha outras instâncias do jogo.";
            t["No response from host."] = "Sem resposta do anfitrião.";
            t["Searching for games…"] = "À procura de jogos…";
            t["No host heard yet — check both PCs share a network and the firewall allows the game, or join by IP below."] =
                "Ainda não foi detetado nenhum anfitrião — verifique que os dois PCs partilham a mesma rede e que a firewall permite o jogo, ou entre por IP abaixo.";
            t["waiting for player…"] = "à espera de jogador…";
            t["OPEN"] = "ABERTO";
            t["Player"] = "Jogador";
            t["  (YOU)"] = "  (EU)";
            t["AI · {0}"] = "IA · {0}";
            t["AI ({0})"] = "IA ({0})";
            t["Invalid IP address: {0}"] = "Endereço IP inválido: {0}";
            t["Invalid port: {0}"] = "Porta inválida: {0}";
            t["Port {0} or {1} already in use. Close other game instances or pick another port."] =
                "A porta {0} ou {1} já está em uso. Feche outras instâncias do jogo ou escolha outra porta.";
            t["Port {0} already in use. Close other game instances or restart Unity."] =
                "A porta {0} já está em uso. Feche outras instâncias do jogo ou reinicie o Unity.";
            t["Socket error ({0}): {1}"] = "Erro de socket ({0}): {1}";
            t["Network error: {0}"] = "Erro de rede: {0}";
            t["Failed to start host: {0}"] = "Falha ao iniciar o anfitrião: {0}";
            t["Failed to start client: {0}"] = "Falha ao iniciar o cliente: {0}";
            t["{0} generates its terrain at runtime, which cannot be guaranteed identical on both machines. Pick a map with baked terrain."] =
                "{0} gera o terreno em tempo real, o que não garante um resultado idêntico nas duas máquinas. Escolha um mapa com terreno pré-gerado.";

            // ── Difficulty / strategy / options arrays (arrays stay
            //    English in code; translated at render) ──────────────────
            t["EASY"] = "FÁCIL";
            t["STANDARD"] = "NORMAL";
            t["HARD"] = "DIFÍCIL";
            t["EXPERT"] = "PERITO";
            t["RANDOM"] = "ALEATÓRIO";
            t["ECONOMIST"] = "ECONOMISTA";
            t["BALANCED"] = "EQUILIBRADO";
            t["TECHNOLOGIST"] = "TECNÓLOGO";
            t["AGGRESSOR"] = "AGRESSOR";
            t["TURTLE"] = "TARTARUGA";
            t["DEFENDER"] = "DEFENSOR";
            t["MAX"] = "MÁXIMO";
            t["AGE 0"] = "ERA 0";
            t["AGE 1"] = "ERA 1";
            t["AGE 2"] = "ERA 2";
            t["AGE 3"] = "ERA 3";
            t["AGE 4"] = "ERA 4";
            t["AGE 1 (AL)"] = "ERA 1 (AL)";
            t["AGE 2 (AL)"] = "ERA 2 (AL)";
            t["AGE 3 (AL)"] = "ERA 3 (AL)";
            t["AGE 4 (AL)"] = "ERA 4 (AL)";
            t["AGE 1 (FER)"] = "ERA 1 (FER)";
            t["AGE 2 (FER)"] = "ERA 2 (FER)";
            t["AGE 3 (FER)"] = "ERA 3 (FER)";
            t["AGE 4 (FER)"] = "ERA 4 (FER)";
            t["AGE 1 (RU)"] = "ERA 1 (RU)";
            t["AGE 2 (RU)"] = "ERA 2 (RU)";
            t["AGE 3 (RU)"] = "ERA 3 (RU)";
            t["AGE 4 (RU)"] = "ERA 4 (RU)";

            // ── SkirmishPanel ───────────────────────────────────────────
            t["OBSERVER"] = "OBSERVADOR";
            t["+ ADD PLAYER"] = "+ ADICIONAR JOGADOR";
            t["EMPTY"] = "VAZIO";
            t["Pick a player on the left, then choose a start position."] =
                "Selecione um jogador à esquerda e depois escolha uma posição inicial.";
            t["Need at least 2 AI warbands to observe!"] =
                "São necessários pelo menos 2 exércitos de IA para observar!";
            t["Need at least 1 human player!"] = "É necessário pelo menos 1 jogador humano!";

            // ── ScenariosPanel ──────────────────────────────────────────
            t["NO SCENARIOS"] = "SEM CENÁRIOS";
            t["No scenario definitions found. Rebuild the scenario library via Tools ▸ TWB ▸ Scenarios."] =
                "Não foram encontradas definições de cenário. Reconstrua a biblioteca de cenários em Tools ▸ TWB ▸ Scenarios.";
            t["Code-driven test scenario. Spawns its setup on load; no briefing available."] =
                "Cenário de teste gerado por código. Cria a sua configuração ao carregar; sem briefing disponível.";
            t["Tutorial — The Whole Campaign"] = "Tutorial — A Campanha Completa";
            t["A guided match on the standard map against one relaxed opponent, from "
              + "the opening to the victory condition.\n\n"
              + "1. Camera controls\n"
              + "2. Workers, mining and the Gatherer's Hut\n"
              + "3. Barracks, Spearmen and taking a fight\n"
              + "4. The special building, the age-up and the Temple\n"
              + "5. Religion Points, sects and their powers\n"
              + "6. The curse — why it wakes, what it costs, how to break it\n"
              + "7. The wells — the verb, and how the match is won\n\n"
              + "Each step tops your bank up so the lesson does not wait on the "
              + "economy, and the Temple upgrade carries straight to level 4. "
              + "Steps can be done in ANY ORDER and tick themselves off as you play; "
              + "skip any of them, or dismiss the coach and keep going as a normal "
              + "skirmish."] =
                "Uma partida guiada no mapa padrão contra um adversário tranquilo, da "
              + "abertura até à condição de vitória.\n\n"
              + "1. Controlos da câmara\n"
              + "2. Trabalhadores, mineração e a Cabana do Recoletor\n"
              + "3. O Quartel, os Lanceiros e travar um combate\n"
              + "4. O edifício especial, a subida de era e o Templo\n"
              + "5. Pontos de Religião, as seitas e os seus poderes\n"
              + "6. A maldição — porque desperta, o que custa, como quebrá-la\n"
              + "7. Os poços — o verbo, e como se vence a partida\n\n"
              + "Cada passo reforça o seu banco para que a lição não fique à espera da "
              + "economia, e a melhoria do Templo avança diretamente até ao nível 4. "
              + "Os passos podem ser feitos por QUALQUER ORDEM e assinalam-se sozinhos "
              + "enquanto joga; salte qualquer um deles, ou dispense o treinador e "
              + "continue como uma escaramuça normal.";

            // ScenarioCatalog display labels (catalog data stays English;
            // translated at the ScenariosPanel render site).
            t["Large Melee Battle (6v6)"] = "Grande Batalha Corpo a Corpo (6v6)";
            t["Large Ranged Battle (6v6)"] = "Grande Batalha à Distância (6v6)";
            t["Large Mixed Battle (6v6)"] = "Grande Batalha Mista (6v6)";
            t["Healer Test"] = "Teste de Curandeiro";
            t["Four-Way Cultures (4 armies)"] = "Quatro Culturas (4 exércitos)";
            t["Full Army (Archers + Swords + Siege)"] = "Exército Completo (Arqueiros + Espadas + Cerco)";
            t["Wall Siege (Walls vs Siege)"] = "Cerco a Muralhas (Muralhas vs Cerco)";
            t["Spell Showcase (all spells, flat map)"] = "Demonstração de Feitiços (todos os feitiços, mapa plano)";
            t["Sect Showcase (12 Sect Abilities)"] = "Demonstração de Seitas (12 Habilidades de Seita)";
            t["Building Showcase (every culture)"] = "Demonstração de Edifícios (todas as culturas)";
            t["The Border Combat Test"] = "Teste de Combate da Fronteira";
            t["Patrol Defense (6 Veilstingers vs Wave)"] = "Defesa em Patrulha (6 Veilstingers vs Vaga)";
            t["Alanthor vs Veilstone Horde (6 batt. vs 50)"] = "Alanthor vs Horda de Veilstone (6 batalhões vs 50)";
            t["Phase 1 Nav Test (1 unit flat grid)"] = "Teste de Navegação Fase 1 (1 unidade, grelha plana)";
            t["Phase 2 Nav Test (300 swords flow + steering)"] = "Teste de Navegação Fase 2 (300 espadas, fluxo + direção)";
            t["Phase 3 Nav Test (1 unit 512x512 SW->NE)"] = "Teste de Navegação Fase 3 (1 unidade, 512x512 SO->NE)";
            t["Phase 4 Nav Test (50 swords + wall place/destroy)"] = "Teste de Navegação Fase 4 (50 espadas + construir/destruir muralha)";
            t["Phase 5 Nav Test (wall ring + 10 Blue + 10 Red)"] = "Teste de Navegação Fase 5 (anel de muralha + 10 Azuis + 10 Vermelhos)";
            t["Phase 7 Nav Test (determinism replay 100 units)"] = "Teste de Navegação Fase 7 (repetição determinista, 100 unidades)";
            t["Wall Climb Test (stairs + rampart garrison)"] = "Teste de Escalada de Muralha (escadas + guarnição de adarve)";
            t["Longbowman Showcase (idle/patrol/shoot/spawn)"] = "Demonstração de Arqueiro Longo (parado/patrulha/disparo/criação)";
            t["Longbowman Battle (30v30, 2x 3x5 blocks)"] = "Batalha de Arqueiros Longos (30v30, 2 blocos de 3x5)";
            t["Building Damage Test (Alanthor row, 5%/s)"] = "Teste de Dano a Edifícios (fila Alanthor, 5%/s)";
            t["Building Damage Showcase (all cultures, 5%/s)"] = "Demonstração de Dano a Edifícios (todas as culturas, 5%/s)";
            t["Guild Defense (fully-upgraded Guild vs swarm)"] = "Defesa da Guilda (Guilda totalmente melhorada vs enxame)";
            t["Hut Evolution (5s self-build, 3s upgrades)"] = "Evolução da Cabana (autoconstrução em 5s, melhorias em 3s)";

            // ── ColorPickerPopup ────────────────────────────────────────
            t["PLAYER COLOUR"] = "COR DO JOGADOR";
            t["Blue"] = "Azul";
            t["Red"] = "Vermelho";
            t["Green"] = "Verde";
            t["Yellow"] = "Amarelo";
            t["Purple"] = "Roxo";
            t["Orange"] = "Laranja";
            t["Teal"] = "Verde-azulado";
            t["Silver"] = "Prateado";
            t["Pink"] = "Rosa";
            t["Brown"] = "Castanho";
            t["Black"] = "Preto";
            t["Maroon"] = "Grená";
            t["White"] = "Branco";     // faction name (defeat toast)
            t["Border"] = "Fronteira"; // faction name (defeat toast)

            // ── MapPreviewWidget ────────────────────────────────────────
            t["STARTS"] = "INÍCIOS";
            t["IRON"] = "FERRO";
            t["CURSE"] = "MALDIÇÃO";
            t["OPEN"] = "ABERTO";
            t["SMALL"] = "PEQUENO";
            t["MEDIUM"] = "MÉDIO";
            t["LARGE"] = "GRANDE";
            t["SMALL / OPEN"] = "PEQUENO / ABERTO";
            t["LARGE / 3v3"] = "GRANDE / 3v3";
            t["A hand-authored theatre. Warband starts, resources, and "
              + "border sites are placed by the map's own markers."] =
                "Um teatro criado à mão. As posições iniciais, os recursos e os "
              + "locais da fronteira são colocados pelos marcadores do próprio mapa.";

            // ── LoadingScreen ───────────────────────────────────────────
            t["Loading..."] = "A carregar...";
            t["Ready"] = "Pronto";
            t["Starting"] = "A iniciar";
            t["Loading world…"] = "A carregar o mundo…";

            // ── PauseMenuPanel ──────────────────────────────────────────
            t["PAUSED"] = "EM PAUSA";
            t["Resume"] = "Retomar";
            t["Close this menu and carry on (Esc)."] = "Fechar este menu e continuar (Esc).";
            t["Restart Match"] = "Reiniciar Partida";
            t["Reload this map from the beginning. All progress in the current "
              + "match is lost."] =
                "Recarregar este mapa desde o início. Todo o progresso da partida "
              + "atual é perdido.";
            t["Quit to Main Menu"] = "Sair para o Menu Principal";
            t["Abandon the match and return to the main menu."] =
                "Abandonar a partida e voltar ao menu principal.";
            t["Quit to Desktop"] = "Sair para o Ambiente de Trabalho";
            t["Close the game."] = "Fechar o jogo.";
            t["Restart the match? Current progress is lost."] =
                "Reiniciar a partida? O progresso atual será perdido.";
            t["Quit to the main menu? Current progress is lost."] =
                "Sair para o menu principal? O progresso atual será perdido.";
            t["Quit to desktop?"] = "Sair para o ambiente de trabalho?";
            t["Confirm"] = "Confirmar";
            t["Cancel"] = "Cancelar";

            // ── VictoryPanel / VictoryConditionSystem ───────────────────
            t["VICTORY"] = "VITÓRIA";
            t["DEFEAT"] = "DERROTA";
            t["Return to Main Menu"] = "Voltar ao Menu Principal";
            t["{0} has been DEFEATED"] = "{0} foi DERROTADO";
            t["{0} WINS"] = "{0} VENCE";
            t["Winner: {0}"] = "Vencedor: {0}";
            t["GAME OVER — {0}"] = "FIM DE JOGO — {0}";
            t["{0} WINS (node victory)"] = "{0} VENCE (vitória pelos nós)";
            t["VICTORY — {0} node win"] = "VITÓRIA — vitória pelos nós de {0}";
            t["DEFEAT — {0} node win"] = "DERROTA — vitória pelos nós de {0}";
            t["{0} node victory — winner: {1}"] = "Vitória pelos nós de {0} — vencedor: {1}";

            // ── NetworkStatusOverlay ────────────────────────────────────
            t["{0} Hz  delay {1:0} ms  "] = "{0} Hz  atraso {1:0} ms  ";
            t["The two games have gone out of sync (tick {0}).\n"
              + "The match cannot continue. A report was written to the logs folder "
              + "beside the game — please send it along with the other player's copy."] =
                "Os dois jogos perderam a sincronização (tick {0}).\n"
              + "A partida não pode continuar. Foi escrito um relatório na pasta logs "
              + "junto ao jogo — envie-o, por favor, juntamente com a cópia do outro jogador.";
            t["Lost contact with the other player.\n"
              + "They may have quit or lost their connection."] =
                "Perdeu-se o contacto com o outro jogador.\n"
              + "Pode ter saído ou perdido a ligação.";
            t["Waiting for player {0}…  ({1:0}s)"] = "À espera do jogador {0}…  ({1:0}s)";

            // ── PlanningModeOverlay ─────────────────────────────────────
            t["PLANNING MODE (Z to execute, ESC to cancel)"] =
                "MODO DE PLANEAMENTO (Z para executar, ESC para cancelar)";
            t["{0} command(s) queued"] = "{0} comando(s) em fila";
        }
    }
}
