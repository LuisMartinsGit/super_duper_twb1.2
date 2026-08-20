// Loc.Pt.GameUI.cs
// Portuguese (European) for the in-game UI binders: actions panels, spells
// bar, top choice bar, objectives, formations, builder palette, unit roster,
// production queue, building upgrade action, religion panel, plus the
// authored GameUI prefab labels the runtime localizer applies.
// Keys are the ENGLISH source strings exactly as composed at the call sites.
// Location: Assets/Scripts/Core/Localization/Loc.Pt.GameUI.cs

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
            t["Training {0}  {1:F1}s"] = "A treinar {0}  {1:F1}s";
            t["Researching {0}  {1:F1}s"] = "A investigar {0}  {1:F1}s";
            t["Research queue: "] = "Fila de investigação: ";

            // Vault (VaultResourceNames stays English; translated at render).
            t["Supplies"] = "Mantimentos";
            t["Iron"] = "Ferro";
            t["Veilstone"] = "Veilstone";
            t["Veilsteel"] = "Veilsteel";
            t["Glow"] = "Fulgor";
            t["Empty"] = "Vazio";
            t["Interest: {0:F0}%/min (compound)   Stored: {1}"] =
                "Juros: {0:F0}%/min (compostos)   Armazenado: {1}";
            t["LOCKED — {0}:{1:D2} remaining"] = "BLOQUEADO — {0}:{1:D2} restantes";
            t["Resource: {0}  (click to cycle)"] = "Recurso: {0}  (clique para alternar)";
            t["Pick which resource this vault stores. Locked to the stored type once a deposit is made."] =
                "Escolha o recurso que este cofre armazena. Fica fixo ao tipo armazenado assim que for feito um depósito.";
            t["Deposit {0}"] = "Depositar {0}";
            t["Deposits lock the vault for a while; interest compounds per minute."] =
                "Os depósitos bloqueiam o cofre durante algum tempo; os juros compõem a cada minuto.";
            t["Withdraw All ({0})"] = "Levantar Tudo ({0})";
            t["Returns the stored amount (plus accrued interest) to the bank."] =
                "Devolve o valor armazenado (mais os juros acumulados) ao banco.";

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
            t["1. Build a special building (Shrine / Vault / Keep)"] =
                "1. Construa um edifício especial (Santuário / Cofre / Fortaleza)";
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

            // ── BuilderPanelBinder ─────────────────────────────────────────
            t["Build Structure"] = "Construir Estrutura";
            t["Left-click to place, Right/Esc to cancel"] =
                "Clique esquerdo para colocar, Direito/Esc para cancelar";

            // ── UnitRosterPanelBinder ──────────────────────────────────────
            t["In production."] = "Em produção.";
            t["#{0} in queue — right-click to cancel and refund."] =
                "N.º {0} na fila — clique direito para cancelar e reembolsar.";
            t["Click to pin the stats panel to this unit type for the rest of the selection."] =
                "Clique para fixar o painel de estatísticas neste tipo de unidade durante o resto da seleção.";

            // ── ProductionQueueStrip ───────────────────────────────────────
            t["In production"] = "Em produção";
            t["Researching {0}   {1:F1}s"] = "A investigar {0}   {1:F1}s";

            // ── BuildingUpgradeAction ──────────────────────────────────────
            t["Upgrading\n{0}%"] = "A melhorar\n{0}%";
            t["Upgrade in progress"] = "Melhoria em curso";
            t["{0}% complete"] = "{0}% concluído";
            t["Upgrade\nLv {0}"] = "Melhorar\nNv {0}";
            t["Upgrade to Level {0}"] = "Melhorar para o Nível {0}";
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
            t["OBJECTIVES"] = "OBJETIVOS";
            t["Choose a Culture to specialize in"] =
                "Escolha uma Cultura em que se especializar";
            t["{0} units"] = "{0} unidades";
        }
    }
}
