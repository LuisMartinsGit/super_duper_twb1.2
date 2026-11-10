// Loc.Pt.GameUI.cs
// Portuguese (European) for the in-game UI binders: actions panels, spells
// bar, top choice bar, objectives, formations, worker palette, unit roster,
// production queue, building upgrade action, religion panel, plus the
// authored GameUI prefab labels the runtime localizer applies.
// Keys are the ENGLISH source strings exactly as composed at the call sites.

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddGameUI(Dictionary<string, string> t)
        {
            // ── Shared ─────────────────────────────────────────────────────
            // "Cost: " is the tooltip splice marker: composer writes
            // "\n" + Loc.T("Cost: ") and the actions panels re-split on the
            // same expression.
            t["Cost: "] = "Custo: ";
            t["Not enough resources"] = "Recursos insuficientes";
            t["<color=#C08040>Not enough resources.</color>"] =
                "<color=#C08040>Recursos insuficientes.</color>";
            t["Training queue full"] = "Fila de treino cheia";
            t["Population cap reached"] = "Limite de população atingido";
            t["Production queue full"] = "Fila de produção cheia";
            t["Queued — right-click to cancel and refund"] =
                "Em fila — clique direito para cancelar e reembolsar";
            t["Research"] = "Investigação";

            // ── ActionsPanelBinder ─────────────────────────────────────────
            t["Actions"] = "Ações";
            t["Train Units"] = "Treinar Unidades";
            t["Temple of Ridan"] = "Templo de Ridan";
            t["Age-Up Choice"] = "Escolha de Avanço de Era";
            t["Converting..."] = "A converter...";
            t["Upgrade Wall"] = "Melhorar Muralha";
            t["Bazaar Wagon"] = "Carroça do Bazar";
            t["Left-click to place hub, Right/Esc to cancel"] =
                "Clique esquerdo para colocar o núcleo, Direito/Esc para cancelar";
            t["Extend Wall"] = "Estender Muralha";
            t["Vault of Almiérra"] = "Cofre de Almiérra";
            t["Maximum 5 Reliquaries"] = "Máximo de 5 Relicários";
            t["Upgrading building  {0}%"] = "A melhorar edifício  {0}%";
            t["Temple Level {0} (Maximum) — all eras unlocked"] =
                "Templo Nível {0} (Máximo) — todas as eras desbloqueadas";
            t["Upgrading to Level {0}  {1}%"] = "A melhorar para o Nível {0}  {1}%";
            t["Advance to Era 2 first (culture choice)"] =
                "Avance primeiro para a Era 2 (escolha de cultura)";
            t["Upgrade to Level {0} (Era {1}) — {2}s"] =
                "Melhorar para o Nível {0} (Era {1}) — {2}s";
            t["Temple upgrade"] = "Melhoria do Templo";
            t["Grants +{0} Religion Points"] = "Concede +{0} Pontos de Religião";
            t["Temple upgrade started ({0}s)"] = "Melhoria do Templo iniciada ({0}s)";
            // The bar names whatever the head item is — a tech or a level-up.
            t["{0}  {1:F1}s"] = "{0}  {1:F1}s";
            t["Queued: "] = "Em fila: ";

            // Vault (VaultResourceNames stays English; translated at render).
            t["Supplies"] = "Mantimentos";
            t["Iron"] = "Ferro";
            t["Veilstone"] = "Veilstone";
            t["Glow"] = "Fulgor";

            // Resource bar income tooltip + territory overlay trade lines.
            t["Producing {0}/min"] = "A produzir {0}/min";
            t["Selling {0}/min"] = "A vender {0}/min";
            t["Territory: {0}"] = "Território: {0}";
            t["Buildings: {0}"] = "Edifícios: {0}";
            t["Trading Outposts: {0}"] = "Postos Comerciais: {0}";
            t["Trading Outposts"] = "Postos Comerciais";
            t["selling"] = "a vender";
            t["bought"] = "comprado";
            t["Vault withdrawals, caravans and loot are not counted."] =
                "Levantamentos do Cofre, caravanas e saque não são contados.";
            t["Empty"] = "Vazio";
            t["{0} +{1:F0}%"] = "{0} +{1:F0}%";
            t["Income bonus: {0}"] = "Bónus de rendimento: {0}";
            t["Income bonus: none yet — each filled step of a bar pays"] = "Bónus de rendimento: nenhum ainda — cada passo cheio de uma barra rende";
            t["{0}: locked"] = "{0}: bloqueada";
            t["{0}: empty"] = "{0}: vazia";
            t["{0}: {1} {2}/{3} ({4}/{5})"] = "{0}: {1} {2}/{3} ({4}/{5})";
            t["Pay with: {0}  (click to cycle)"] = "Pagar com: {0}  (clique para alternar)";
            t["Any resource fills a bar, one for one. A withdrawal gives back exactly what you paid in."] =
                "Qualquer recurso enche uma barra, um por um. Um levantamento devolve exatamente o que pagou.";
            t["Next bar boosts: {0}  (click to cycle)"] = "A próxima barra aumenta: {0}  (clique para alternar)";
            t["The resource a bar raises when full, chosen as it starts filling. Iron and veilstone open with the Vault's Accounts research."] =
                "O recurso que uma barra aumenta quando cheia, escolhido quando começa a encher. O ferro e o veilstone abrem com a investigação de Contas do Cofre.";
            t["Every open bar is full"] = "Todas as barras abertas estão cheias";
            t["Deposit {0} {1}"] = "Depositar {0} {1}";
            t["Fills the open bars in order ({0} / {1} / {2} / {3}), each in {4} steps. Every filled step adds {5:0.#}% to the income of the resource its bar boosts."] =
                "Enche as barras abertas por ordem ({0} / {1} / {2} / {3}), cada uma em {4} passos. Cada passo cheio acrescenta {5:0.#}% ao rendimento do recurso que a sua barra aumenta.";
            t["Withdraw All ({0})"] = "Levantar Tudo ({0})";
            t["Gives back everything paid in. Every bar empties: the bonus and the progress are lost."] =
                "Devolve tudo o que foi pago. Todas as barras se esvaziam: o bónus e o progresso perdem-se.";

            // ── ActionsPanelPrefabBinder ───────────────────────────────────
            // "Choreographed Volleys" is kept English deliberately — the
            // ability name is a title owned by the abilities table.
            t["Choreographed Volleys is recharging"] =
                "Choreographed Volleys está a recarregar";
            t["No planted siege engine ready"] =
                "Nenhuma máquina de cerco montada está pronta";
            t["A wing is already under construction"] = "Já existe uma ala em construção";

            // ── SpellsPanelBinder ──────────────────────────────────────────
            t["ABILITY"] = "HABILIDADE";
            t["Affects the caster."] = "Afeta o lançador.";
            t["Targets one unit."] = "Visa uma unidade.";
            t["Cast on one enemy unit."] = "Lança-se sobre uma unidade inimiga.";
            t["Cast on one of your units."] = "Lança-se sobre uma das tuas unidades.";
            t["Cast on one friendly unit."] = "Lança-se sobre uma unidade aliada.";
            t["Cast on one unit."] = "Lança-se sobre uma unidade.";
            t["Cast on one enemy building."] = "Lança-se sobre um edifício inimigo.";
            t["Cast on one of your buildings."] = "Lança-se sobre um dos teus edifícios.";
            t["Cast on one friendly building."] = "Lança-se sobre um edifício aliado.";
            t["Cast on one building."] = "Lança-se sobre um edifício.";
            t["Cast on one enemy unit or building."] = "Lança-se sobre uma unidade ou edifício inimigo.";

            // ── EntityTargeting (single-target spell pick) ─────────────────
            t["Pick an enemy unit"] = "Escolhe uma unidade inimiga";
            t["Pick one of your units"] = "Escolhe uma das tuas unidades";
            t["Pick a friendly unit"] = "Escolhe uma unidade aliada";
            t["Pick a unit"] = "Escolhe uma unidade";
            t["Pick an enemy building"] = "Escolhe um edifício inimigo";
            t["Pick one of your buildings"] = "Escolhe um dos teus edifícios";
            t["Pick a friendly building"] = "Escolhe um edifício aliado";
            t["Pick a building"] = "Escolhe um edifício";
            t["Pick an enemy unit or building"] = "Escolhe uma unidade ou edifício inimigo";
            t["Pick a target"] = "Escolhe um alvo";
            t["That target is dead"] = "Esse alvo está morto";
            t["That target is gone"] = "Esse alvo já não existe";
            t["That target cannot be seen"] = "Esse alvo não está visível";
            t["Walls cannot be targeted"] = "Não se pode visar muralhas";
            t["Curse structures cannot be targeted"] = "Não se pode visar estruturas da maldição";
            t["Not on your capital"] = "Não na tua capital";
            t["That building is not finished"] = "Esse edifício não está concluído";
            t["That target is already affected"] = "Esse alvo já está afetado";
            t["That building trains nothing"] = "Esse edifício não treina nada";
            t["Out of range"] = "Fora de alcance";
            t["The caster is gone"] = "O lançador já não existe";
            t["Targets an area."] = "Visa uma área.";
            t["Continuous aura around the caster."] = "Aura contínua em torno do lançador.";
            t["Affects the whole faction."] = "Afeta toda a fação.";
            t["Allies of your culture."] = "Aliados da sua cultura.";
            t["All allies."] = "Todos os aliados.";
            t["Allied cavalry."] = "Cavalaria aliada.";
            t["Enemies."] = "Inimigos.";
            t["Allied economy buildings."] = "Edifícios económicos aliados.";
            t["% damage dealt"] = "% de dano infligido";
            t["% armour"] = "% de armadura";
            t[" armour"] = " de armadura";
            t["% damage taken"] = "% de dano recebido";
            t["% move speed"] = "% de velocidade de movimento";
            t["costs {0:0.#}% of max HP over the duration"] =
                "custa {0:0.#}% da vida máxima ao longo da duração";
            t["cannot drop below {0:0.#} HP"] = "não pode descer abaixo de {0:0.#} PV";
            t[" charge damage"] = " de dano de carga";
            t["reveals fog of war"] = "revela o nevoeiro de guerra";
            t["% resource yield"] = "% de rendimento de recursos";
            t["blocks further automation"] = "impede mais automação";
            t["sight grows while standing still"] = "a visão aumenta enquanto estiver parado";
            t["% damage on the next charge"] = "% de dano na próxima carga";
            t["cannot attack while it lasts"] = "não pode atacar enquanto durar";
            t["deploys a temporary field hospital"] =
                "instala um hospital de campanha temporário";
            t["Radius"] = "Raio";
            t["Range"] = "Alcance";
            t["Lasts"] = "Dura";
            t["Always on"] = "Sempre ativo";
            t["Cooldown"] = "Recarga";
            t["Passive: {0}"] = "Passiva: {0}";
            t["passive"] = "passiva";
            t["active"] = "ativa";
            t["auto"] = "auto";
            t["<i>This unit casts it by itself — you cannot trigger it.</i>"] =
                "<i>Esta unidade lança-a sozinha — não a pode ativar.</i>";
            t["<color=#C08040>Recharging — {0}s.</color>"] =
                "<color=#C08040>A recarregar — {0}s.</color>";
            t["<color=#7FB069>Ready.</color>"] = "<color=#7FB069>Pronta.</color>";
            // Spell overhaul (docs/Design/Spells.md): damage line, channel
            // line, the three effect kinds that had no text, cast bar label.
            t["Allied ranged units."] = "Unidades de ataque à distância aliadas.";
            t["deals {0:0} {1} damage"] = "inflige {0:0} de dano {1}";
            t["Channel {0:0.#}s -- a new order, a stun or death interrupts it."] =
                "Canalização de {0:0.#}s -- uma nova ordem, um atordoamento ou a morte interrompem-na.";
            t["% fire rate"] = "% de cadência de tiro";
            t["calls in a temporary army that grows with the hero's level"] =
                "convoca um exército temporário que cresce com o nível do herói";
            t["hurls every enemy in the radius into the air; enemy buildings take heavy damage"] =
                "projeta pelo ar todos os inimigos no raio; os edifícios inimigos sofrem dano pesado";
            // The Guildmaster's 2026-11-10 kit (Sects.md § Sect of Prosperity).
            t["every enemy unit in the circle dies and its owner is refunded its cost; heroes are immune; the circle grows {0:0.#} m a hero level"] =
                "todas as unidades inimigas no círculo morrem e o dono é reembolsado do seu custo; os heróis são imunes; o círculo cresce {0:0.#} m por nível do herói";
            t["the extractor under the hero pays +{0:0.#}% until it takes damage"] =
                "o extrator sob o herói rende +{0:0.#}% até sofrer dano";
            t["everything that dies near the hero, his own side included, pays you {0:0}% of its cost"] =
                "tudo o que morre perto do herói, incluindo o seu próprio lado, paga-te {0:0}% do seu custo";
            t["{0} calls a WINDFALL — every death near the Guildmaster pays!"] =
                "{0} invoca uma MARÉ DE SORTE — cada morte perto do Mestre da Guilda paga!";
            t["Guildmaster — L1 Bounty: enemy units in a small aimed circle die and their owners are refunded; the circle grows with his level. L3 Stake: the extractor under him pays 5% more until it takes damage. L6 Liquidate: demolish one of your buildings for half of everything put into it. L10 Windfall: for 40s everything that dies near him, his own side included, pays you 10% of its cost."] =
                "Mestre da Guilda — N1 Recompensa: as unidades inimigas num pequeno círculo apontado morrem e os donos são reembolsados; o círculo cresce com o nível dele. N3 Participação: o extrator sob ele rende mais 5% até sofrer dano. N6 Liquidar: demole um dos teus edifícios por metade de tudo o que lá investiste. N10 Maré de Sorte: durante 40s, tudo o que morre perto dele, incluindo o seu próprio lado, paga-te 10% do seu custo.";
            t["casting {0:0.0}s"] = "a lançar {0:0.0}s";
            t["melee"] = "corpo a corpo";
            t["ranged"] = "à distância";
            t["siege"] = "de cerco";
            t["true"] = "verdadeiro";
            t["magic"] = "mágico";
            // Religion panel: power level and damage of the current spec.
            t["Level {0}"] = "Nível {0}";
            t["Damage: {0:0} {1}"] = "Dano: {0:0} {1}";

            // ── TopChoiceBar ───────────────────────────────────────────────
            t["Select Culture"] = "Selecionar Cultura";
            t["SELECT CULTURE"] = "SELECIONAR CULTURA";
            t["NOT AVAILABLE YET"] = "AINDA NÃO DISPONÍVEL";
            t["Choose one"] = "Escolha um";
            t["Cancel (Esc)"] = "Cancelar (Esc)";
            t["Choose your culture"] = "Escolha a sua cultura";
            t["Advances the faction to Era 2 and unlocks its Age 1 buildings, units and upgrades."] =
                "Avança a fação para a Era 2 e desbloqueia os seus edifícios, unidades e melhorias da Idade 1.";
            t["<color=#C08040>Finish your special building first.</color>"] =
                "<color=#C08040>Termine primeiro o seu edifício especial.</color>";
            t["<i>One special building per faction — this choice is final.</i>"] =
                "<i>Um edifício especial por fação — esta escolha é definitiva.</i>";
            t["Choose your culture — this advances your faction to Era 2.   Cost: "] =
                "Escolha a sua cultura — isto avança a sua fação para a Era 2.   Custo: ";
            t["Not enough resources to advance.   Cost: "] =
                "Recursos insuficientes para avançar.   Custo: ";
            t["Advancing {0}%"] = "A avançar {0}%";
            t["{0} is coming soon"] = "{0} estará disponível em breve";
            t["Not enough resources to advance"] = "Recursos insuficientes para avançar";

            // ── CultureConfig (Names[]/Descriptions[] stay English) ────────
            // Culture names are proper nouns and fall back unchanged; listed
            // here so coverage is explicit. "None" is deliberately absent —
            // it never renders as a culture name and a generic "None" entry
            // could mis-gender other domains.
            t["Runai"] = "Runai";
            t["Alanthor"] = "Alanthor";
            t["Feraldis"] = "Feraldis";
            t["Nomadic traders and explorers.\nBonus: Trade routes, mobile outposts."] =
                "Comerciantes e exploradores nómadas.\nBónus: Rotas comerciais, postos avançados móveis.";
            t["Industrial forgemasters.\nBonus: Superior metal processing, fortifications."] =
                "Mestres-ferreiros industriais.\nBónus: Processamento de metal superior, fortificações.";
            t["Fierce warband culture.\nBonus: Hunting bonuses, aggressive units."] =
                "Cultura de hostes ferozes.\nBónus: Bónus de caça, unidades agressivas.";

            // ── ObjectivesPanelBinder ──────────────────────────────────────
            t["<s>1. Build a special building</s>"] =
                "<s>1. Construa um edifício especial</s>";
            t["1. Build a special building - under construction"] =
                "1. Construa um edifício especial - em construção";
            t["1. Build a special building (Vault / Keep)"] =
                "1. Construa um edifício especial (Cofre / Fortaleza)";
            t["<s>2. Select a culture and age up</s>"] =
                "<s>2. Selecione uma cultura e avance de era</s>";
            t["2. Advancing to Era 2 - {0}%"] = "2. A avançar para a Era 2 - {0}%";
            t["2. Select a culture and age up"] = "2. Selecione uma cultura e avance de era";
            t["3A. Build the Temple, ascend the ages,\n     then cleanse the curse nodes"] =
                "3A. Construa o Templo, ascenda pelas eras,\n     depois purgue os nós da maldição";
            t["Pacify"] = "Pacificar";
            t["Destroy"] = "Destruir";
            t["Purify"] = "Purificar";
            t["3A. Build the Temple of Ridan,\n     then {0} the curse nodes"] =
                "3A. Construa o Templo de Ridan,\n     depois {0} os nós da maldição";
            t["3A. Temple of Ridan - under construction"] =
                "3A. Templo de Ridan - em construção";
            t["<s>3A. Build the Temple of Ridan</s>"] =
                "<s>3A. Construa o Templo de Ridan</s>";
            t["     Upgrade the Temple (Lv {0}, upgrading {1}%)"] =
                "     Melhore o Templo (Nv {0}, a melhorar {1}%)";
            t["     Upgrade the Temple to age up (Lv {0} of {1})"] =
                "     Melhore o Templo para avançar de era (Nv {0} de {1})";
            t["<s>     Upgrade the Temple to age up</s>"] =
                "<s>     Melhore o Templo para avançar de era</s>";
            t["     {0} the curse nodes ({1}/{2}) - hold {3:0.0}s"] =
                "     {0} os nós da maldição ({1}/{2}) - mantenha {3:0.0}s";
            t["<s>     {0} the curse nodes ({1}/{2})</s>"] =
                "<s>     {0} os nós da maldição ({1}/{2})</s>";
            t["     {0} the curse nodes ({1}/{2})"] =
                "     {0} os nós da maldição ({1}/{2})";
            t["<s>3B. Destroy all other players ({0}/{1})</s>"] =
                "<s>3B. Destrua todos os outros jogadores ({0}/{1})</s>";
            t["3B. Destroy all other players ({0}/{1})"] =
                "3B. Destrua todos os outros jogadores ({0}/{1})";

            // ── FormationsPanelBinder (Labels[]/Tips[] stay English) ───────
            t["FORMATION  (X to cycle)"] = "FORMAÇÃO  (X para alternar)";
            t["Box"] = "Quadrado";
            t["Line"] = "Linha";
            t["Wedge"] = "Cunha";
            t["Stagger"] = "Escalonada";
            t["<b>Box</b>\nCompact rectangle. The all-round default — good for moving a mixed group without exposing a flank."] =
                "<b>Quadrado</b>\nRetângulo compacto. A opção padrão — boa para mover um grupo misto sem expor um flanco.";
            t["<b>Line</b>\nWide, shallow rank. Maximises how many units can shoot or engage at once; fragile if hit from the side."] =
                "<b>Linha</b>\nFileira larga e pouco profunda. Maximiza quantas unidades podem disparar ou combater ao mesmo tempo; frágil se atingida de lado.";
            t["<b>Wedge</b>\nArrowhead. Concentrates the leading edge for a charge that punches through a line."] =
                "<b>Cunha</b>\nPonta de seta. Concentra a frente para uma carga que rompe uma linha.";
            t["<b>Stagger</b>\nOffset rows. Spreads the group out so area damage and siege hit fewer units at a time."] =
                "<b>Escalonada</b>\nFilas desfasadas. Dispersa o grupo para que o dano de área e o cerco atinjam menos unidades de cada vez.";

            // ── WorkerPanelBinder ─────────────────────────────────────────
            t["Build Structure"] = "Construir Estrutura";
            t["Left-click to place, Right/Esc to cancel"] =
                "Clique esquerdo para colocar, Direito/Esc para cancelar";

            // ── UnitRosterPanelBinder ──────────────────────────────────────
            t["In production."] = "Em produção.";
            t["#{0} in queue — right-click to cancel and refund."] =
                "N.º {0} na fila — clique direito para cancelar e reembolsar.";
            t["Click to pin the stats panel to this unit type for the rest of the selection."] =
                "Clique para fixar o painel de estatísticas neste tipo de unidade durante o resto da seleção.";

            // ── BuildingUpgradeAction ──────────────────────────────────────
            t["Upgrading\n{0}%"] = "A melhorar\n{0}%";
            t["Upgrade in progress"] = "Melhoria em curso";
            t["Upgrade\nqueued"] = "Melhoria\nem fila";
            t["Upgrade queued"] = "Melhoria em fila";
            t["It starts when this building finishes what it is working on. "
              + "Right-click its slot in the queue to cancel and refund."] =
                "Começa quando este edifício terminar o que está a fazer. Clique direito "
              + "no seu lugar na fila para cancelar e reembolsar.";
            t["{0}% complete"] = "{0}% concluído";
            t["Upgrade\nLv {0}"] = "Melhorar\nNv {0}";
            t["Upgrade to Level {0}"] = "Melhorar para o Nível {0}";
            // Faction-wide building levels (2026-10-19).
            t["Upgrade to Level {0} (every building of this type)"] =
                "Melhorar para o Nível {0} (todos os edifícios deste tipo)";
            t["Upgrading at another building of this type"] = "A melhorar noutro edifício deste tipo";
            t["A level-up raises every building of this type you own, so only one runs at a time. "
              + "This one rises with it."] =
                "Uma melhoria eleva todos os edifícios deste tipo que possui, por isso só corre uma "
              + "de cada vez. Este sobe com ela.";
            t["Applies to all {0} buildings of this type you own, and every one you build later."] =
                "Aplica-se aos {0} edifícios deste tipo que possui e a todos os que construir depois.";
            t["Another building of this type is already upgrading"] =
                "Outro edifício deste tipo já está a ser melhorado";
            // Decision 42 (2026-11-09): one crystal, Trading Outposts beside any node, stop extracting.
            t["Economy per territory"] = "Economia por território";
            t["Per minute"] = "Por minuto";
            t["No territory yet."] = "Ainda sem território.";
            t["of which Trading Outposts"] = "dos quais Entrepostos Comerciais";
            t["+{0} more"] = "+{0} mais";
            t["Every iron and veilstone slot your Mines and Veilstone Mines work pays +20%."] = "Cada nó de ferro e veilstone trabalhado pelas tuas Minas e Minas de Veilstone rende +20%.";
            t["Every iron and veilstone slot your Mines and Veilstone Mines work pays +40% (replaces Deep Shafts)."] = "Cada nó de ferro e veilstone trabalhado pelas tuas Minas e Minas de Veilstone rende +40% (substitui Poços Profundos).";
            t["Trade Charters"] = "Cartas de Comércio";
            t["Shard Tools"] = "Ferramentas de Estilhaço";
            t["Shard Pylons"] = "Pilares de Estilhaço";
            t["Shard Chains"] = "Correntes de Estilhaço";
            t["Shard Axeheads"] = "Cabeças de Machado de Estilhaço";
            t["Shard Blades"] = "Lâminas de Estilhaço";
            t["Shard Shot"] = "Projéteis de Estilhaço";
            t["Shard Weapons"] = "Armas de Estilhaço";
            t["Shard-Tipped Javelins"] = "Dardos com Ponta de Estilhaço";
            t["Shard Lances"] = "Lanças de Estilhaço";
            t["Runai Foundry"] = "Fundição Runai";
            t["Smelt"] = "Fundir";
            t["Trade for Veilstone"] = "Trocar por Veilstone";
            t["Spend {0} religion points for {1} veilstone. You have {2}."] = "Gasta {0} pontos de religião por {1} veilstone. Tens {2}.";
            t["Idle — its node is spent, cursed, or not in your ground."] = "Inativo — o seu nó está esgotado, amaldiçoado ou fora do teu território.";
            t["per minute, never drawn from its node"] = "por minuto, sem nunca gastar o nó";
            t["Holds its node: the node stops at its last unit and this post keeps paying."] = "Segura o seu nó: o nó pára na última unidade e este posto continua a render.";
            t["{0} in {1} is worked out — its Trading Outposts keep it, the extractor collapsed"] = "{0} em {1} está esgotado — os Entrepostos Comerciais mantêm-no, o extrator colapsou";
            t["Post {0} of {1} beside its node"] = "Posto {0} de {1} junto ao seu nó";
            t["Falls with its node when the node runs dry — stop its extractor in time."] = "Cai com o seu nó quando este se esgota — para o extrator a tempo.";
            t["Research Trade Charters at the Fortress (level 3) first."] = "Investiga primeiro as Cartas de Comércio na Fortaleza (nível 3).";
            t["Stands on a free side of a supply, iron or veilstone node, up to {0} per node. Trickles that node's resource without drawing on it, for as long as the node lives."] = "Fica num lado livre de um nó de mantimentos, ferro ou veilstone, até {0} por nó. Rende o recurso desse nó sem o gastar, enquanto o nó existir.";
            t["Each further post beside the same node costs {0} to build; a new node starts at base price."] = "Cada posto adicional junto ao mesmo nó custa {0} a construir; um nó novo começa no preço base.";
            t["The Runai do not mine veilstone — raise a Sanctuary on it"] = "Os Runai não mineram veilstone — ergue um Santuário sobre ele";
            t["This node is cursed or mined out"] = "Este nó está amaldiçoado ou esgotado";
            t["Trading Outposts must stand beside a living resource node"] = "Os Entrepostos Comerciais têm de ficar ao lado de um nó de recursos ativo";
            t["Trading Outposts must stand on a free side of a living resource node"] = "Os Entrepostos Comerciais têm de ficar num lado livre de um nó de recursos ativo";
            t["Research Trade Charters at the Fortress first"] = "Investiga primeiro as Cartas de Comércio na Fortaleza";
            t["Alanthor. Unlocks the Trading Outpost: up to four beside any supply, iron or veilstone node, each trickling that node's resource without ever drawing on it, for as long as the node lives."] = "Alanthor. Desbloqueia o Entreposto Comercial: até quatro junto a qualquer nó de mantimentos, ferro ou veilstone, cada um a render o recurso desse nó sem nunca o gastar, enquanto o nó existir.";
            t["Weapon tier 4: melee units deal a further +75% damage."] = "Armas de nível 4: as unidades corpo a corpo causam mais +75% de dano.";
            t["Arrow tier 4: ranged units deal a further +75% damage."] = "Flechas de nível 4: as unidades de ataque à distância causam mais +75% de dano.";
            t["Weapon tier 4: cavalry deal a further +75% damage."] = "Armas de nível 4: a cavalaria causa mais +75% de dano.";
            t["Munition tier 4: siege engines deal a further +75% damage."] = "Munições de nível 4: as máquinas de cerco causam mais +75% de dano.";
            t["Unlocks the shard weapon tier at every Feraldis trainer."] = "Desbloqueia o nível de armas de estilhaço em todos os centros de treino Feraldis.";
            t["Forging & weapons"] = "Forja e armas";
            // Trading Outpost side slots, ramp and levels (2026-10-04).
            t["Trades x{0} — spends and earns that much more per minute."] =
                "Comercia x{0} — gasta e ganha tanto mais por minuto.";
            t["Raises this building's stats and unlocks its next tier of units and research."] =
                "Aumenta as estatísticas deste edifício e desbloqueia o próximo escalão de unidades e investigação.";
            t["Not enough resources to upgrade"] = "Recursos insuficientes para melhorar";
            t["Choose a culture before upgrading buildings"] =
                "Escolha uma cultura antes de melhorar edifícios";
            t["Building is already at max level"] = "O edifício já está no nível máximo";

            // ── ReligionPanelBinder ────────────────────────────────────────
            t["Religion Points: "] = "Pontos de Religião: ";
            t["Temple Lv {0} - upgrading {1}%"] = "Templo Nv {0} - a melhorar {1}%";
            t["Temple Lv {0} - power tier {1}"] = "Templo Nv {0} - escalão de poder {1}";
            t["No slot"] = "Sem espaço";
            t["Adopt a sect"] = "Adotar uma seita";
            t["{0} chapel - {1}%"] = "Capela de {0} - {1}%";
            t["{0}  Tier {1}"] = "{0}  Escalão {1}";
            t["Cast: "] = "Lançar: ";
            t["Ready in {0}s"] = "Pronta em {0}s";
            t["Active {0}/3"] = "Ativa {0}/3";
            t["Locked — raise the sect's Active lever to Lv {0}."] =
                "Bloqueada — suba a alavanca de Ativas da seita para o Nv {0}.";
            t["<color=#7FB069>Ready — click, then pick a target on the map.</color>"] =
                "<color=#7FB069>Pronta — clique e depois escolha um alvo no mapa.</color>";
            t["<i>Glow allocated: cooldowns halved.</i>"] =
                "<i>Fulgor atribuído: recargas reduzidas para metade.</i>";
            t["<i>Right-click the slot to allocate Glow (halves cooldowns).</i>"] =
                "<i>Clique direito no espaço para atribuir Fulgor (reduz as recargas para metade).</i>";
            t["<b>Empty chapel slot</b>"] = "<b>Espaço de capela vazio</b>";
            t["Click to open the sect roster. Adopting a sect spends Religion Points plus the chapel's materials and is permanent for the match."] =
                "Clique para abrir a lista de seitas. Adotar uma seita gasta Pontos de Religião mais os materiais da capela e é permanente durante a partida.";
            t["<b>{0} chapel</b>"] = "<b>Capela de {0}</b>";
            t["Under construction. Its powers come online when the chapel finishes."] =
                "Em construção. Os seus poderes ficam disponíveis quando a capela estiver concluída.";
            t["<i>Left-click: cast the highest ready active. Right-click: toggle Glow allocation (halves cooldowns).</i>"] =
                "<i>Clique esquerdo: lança a ativa pronta mais alta. Clique direito: alterna a atribuição de Fulgor (reduz as recargas para metade).</i>";
            t["<b>No slot</b>"] = "<b>Sem espaço</b>";
            t["Upgrade the Temple of Ridan to open more chapel slots."] =
                "Melhore o Templo de Ridan para abrir mais espaços de capela.";
            t["<i>Coming soon.</i>"] = "<i>Disponível em breve.</i>";
            t["Passive"] = "Passiva";
            t["Active {0}"] = "Ativa {0}";
            t["Unit"] = "Unidade";
            t["<b>{0} — passive</b>"] = "<b>{0} — passiva</b>";
            t["<color=#7FB069>Active — always on, no cooldown.</color>"] =
                "<color=#7FB069>Ativa — sempre ligada, sem recarga.</color>";
            t["<color=#C08040>Dormant — your Temple is down.</color>"] =
                "<color=#C08040>Adormecida — o seu Templo está destruído.</color>";
            t["{0} - coming soon"] = "{0} - disponível em breve";
            t["{0} - {1} RP"] = "{0} - {1} PR";
            t["{0} - need materials"] = "{0} - faltam materiais";
            t["{0} - adopted"] = "{0} - adotada";
            t["{0} - need {1} RP (have {2})"] = "{0} - precisa de {1} PR (tem {2})";
            t["{0} - no free slot"] = "{0} - sem espaço livre";
            t["{0} - unavailable"] = "{0} - indisponível";
            t["Not enough Religion Points"] = "Pontos de Religião insuficientes";
            t["All chapel slots are in use"] = "Todos os espaços de capela estão ocupados";
            t["Sect already adopted"] = "Seita já adotada";
            t["This sect is coming soon"] = "Esta seita estará disponível em breve";
            t["Cannot adopt this sect"] = "Não é possível adotar esta seita";
            t["No Glow stored in the Temple"] = "Não há Fulgor armazenado no Templo";

            // ── Authored GameUI prefab labels (runtime localizer) ──────────
            t["RELIGION"] = "RELIGIÃO";
            t["CHOOSE A SECT"] = "ESCOLHA UMA SEITA";
            t["Empty slot"] = "Espaço vazio";
            t["Sect"] = "Seita";
            t["Cancel"] = "Cancelar";
            t["Idle"] = "Inativos";
            t["Idle builders: {0}"] = "Construtores inativos: {0}";
            t["Show income per minute"] = "Mostrar rendimento por minuto";
            t["Hide income per minute"] = "Ocultar rendimento por minuto";
            t["OBJECTIVES"] = "OBJETIVOS";
            t["Choose a Culture to specialize in"] =
                "Escolha uma Cultura em que se especializar";
            t["{0} units"] = "{0} unidades";
        }
    }
}
