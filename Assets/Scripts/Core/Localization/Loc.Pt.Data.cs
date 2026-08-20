// Loc.Pt.Data.cs
// Portuguese (European) translations for DATA-DRIVEN text: strings authored
// in TechTree.json, the Building/Unit ScriptableObject assets, the loading
// tips, map infos and scenario infos. The data files stay English; display
// code looks the English string up here at render time.
// KEYS ARE BYTE-EXACT copies of the English source strings. Use the indexer,
// never t.Add() (the same string can appear in several domains).
// Location: Assets/Scripts/Core/Localization/Loc.Pt.Data.cs

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddData(Dictionary<string, string> t)
        {
            // ============================================================
            // TechTree.json — building / unit / tech names
            // ============================================================
            t["Hall"] = "Salão";
            t["Hut"] = "Cabana";
            t["Gatherer's Hut"] = "Cabana do Recoletor";
            t["Barracks"] = "Quartel";
            t["Archery Range"] = "Campo de Tiro com Arco";
            t["Shrine of Ridan"] = "Santuário de Ridan";
            t["Vault of Almiérra"] = "Cofre de Almiérra";
            t["Worker"] = "Trabalhador";
            t["Crossbowman"] = "Besteiro";
            t["Longbowman"] = "Arqueiro de Arco Longo";
            t["Advance to Era II"] = "Avançar para a Era II";
            t["Iron Surveying I"] = "Prospeção de Ferro I";
            t["Iron Survey II"] = "Prospeção de Ferro II";
            t["Iron Survey III"] = "Prospeção de Ferro III";
            t["Veilstone Survey I"] = "Prospeção de Veilstone I";
            t["Veilstone Survey II"] = "Prospeção de Veilstone II";
            t["Veilsteel Survey"] = "Prospeção de Veilsteel";
            t["Raiding I"] = "Pilhagem I";
            t["Raiding II"] = "Pilhagem II";
            t["Raiding III"] = "Pilhagem III";
            t["Iron Plunder"] = "Saque de Ferro";
            t["Veilstone Plunder"] = "Saque de Veilstone";
            t["Veilsteel Plunder"] = "Saque de Veilsteel";
            t["Iron reinforcements"] = "Reforços de ferro";
            t["Veilstone walls"] = "Muralhas de Veilstone";
            t["Veilsteel Pylons"] = "Pilares de Veilsteel";
            t["Retaliatory measures"] = "Medidas de retaliação";
            t["Stone Tools"] = "Ferramentas de Pedra";
            t["Iron Tools"] = "Ferramentas de Ferro";
            t["Veilstone Tools"] = "Ferramentas de Veilstone";
            t["Veilsteel Tools"] = "Ferramentas de Veilsteel";
            t["Mason Guild"] = "Guilda dos Pedreiros";
            t["Scouting Celestarii"] = "Exploração Celestarii";
            t["Armed Scouts"] = "Batedores Armados";
            t["Conscription"] = "Conscrição";
            t["Stone Weapons"] = "Armas de Pedra";
            t["Stone-Tipped Arrows"] = "Flechas com Ponta de Pedra";
            t["Fletching"] = "Empenamento";
            t["Coffers"] = "Cofres";
            t["Merchant Charters"] = "Cartas Mercantis";
            t["Sovereign Bonds"] = "Obrigações Soberanas";
            t["Iron Subsidies"] = "Subsídios de Ferro";
            t["Veilstone Monetization"] = "Monetização de Veilstone";
            t["Veilsteel Bonds"] = "Obrigações de Veilsteel";
            t["Heightened Masses"] = "Missas Elevadas";
            t["Warrior Priests"] = "Sacerdotes Guerreiros";
            t["Pious Masses"] = "Missas Piedosas";
            t["Fervored Masses"] = "Missas Fervorosas";
            t["Ballista Emplacement"] = "Posição de Balista";
            t["Trebuchet Emplacement"] = "Posição de Trabuco";
            t["Additional Towers"] = "Torres Adicionais";
            t["Reinforced Walls"] = "Muralhas Reforçadas";
            t["Choreographed Volleys"] = "Salvas Coreografadas";
            t["Royal Index"] = "Índice Real";
            t["Mason's Charter"] = "Carta dos Pedreiros";
            t["Deep Foundations"] = "Fundações Profundas";
            t["Warden's Ledger"] = "Registo do Guardião";
            t["Thessara's Bazaar"] = "Bazar de Thessara";
            t["Runai Outpost"] = "Posto Avançado Runai";
            t["Runai Trade Hub"] = "Entreposto Comercial Runai";
            t["Runai Vault"] = "Cofre Runai";
            t["Runai Veilsteel Foundry"] = "Fundição de Veilsteel Runai";
            t["Runai Siege Workshop"] = "Oficina de Cerco Runai";
            t["Catapult"] = "Catapulta";
            t["Fiendstone Keep"] = "Fortaleza de Fiendstone";
            t["Hunting Lodge"] = "Cabana de Caça";
            t["Logging Station"] = "Posto Madeireiro";
            t["Fiend Foundry"] = "Fundição Fiend";
            t["Totem Tower"] = "Torre-Totem";
            t["Longhouse"] = "Casa Comunal";
            t["Siege Yard"] = "Estaleiro de Cerco";
            t["King's Court"] = "Corte do Rei";
            t["Alanthor Wall"] = "Muralha Alanthor";
            t["Wall Tower"] = "Torre de Muralha";
            t["Wall Gate"] = "Portão de Muralha";
            t["Watch Tower"] = "Torre de Vigia";
            t["Forge"] = "Forja";
            t["Royal Stable"] = "Estrebaria Real";
            t["Stone Ledgers"] = "Registos de Pedra";
            t["Mason's Guild"] = "Guilda dos Pedreiros";
            t["Iron Weapons"] = "Armas de Ferro";
            t["Veilstone Weapons"] = "Armas de Veilstone";
            t["Shard-infused Weapons"] = "Armas Imbuídas de Fragmentos";
            t["Seasoned Infantry"] = "Infantaria Experiente";
            t["Veteran Infantry"] = "Infantaria Veterana";
            t["Elite Infantry"] = "Infantaria de Elite";
            t["Charge"] = "Carga";
            t["Shield Wall"] = "Muralha de Escudos";
            t["Iron-Tipped Arrows"] = "Flechas com Ponta de Ferro";
            t["Veilstone-Tipped Arrows"] = "Flechas com Ponta de Veilstone";
            t["Shard-Tipped Arrows"] = "Flechas com Ponta de Fragmento";
            t["Seasoned Archers"] = "Arqueiros Experientes";
            t["Veteran Archers"] = "Arqueiros Veteranos";
            t["Elite Archers"] = "Arqueiros de Elite";
            t["Arrow Volley"] = "Salva de Flechas";
            t["Arrow Shower"] = "Chuva de Flechas";
            t["Deploy Stakes"] = "Colocar Estacas";
            t["Seasoned Cavalry"] = "Cavalaria Experiente";
            t["Veteran Cavalry"] = "Cavalaria Veterana";
            t["Elite Cavalry"] = "Cavalaria de Elite";
            t["War Horn"] = "Corno de Guerra";
            t["Full Gallop"] = "A Todo o Galope";
            t["Reinforced Bolts"] = "Virotes Reforçados";
            t["Iron-Shod Ram"] = "Aríete Ferrado";
            t["Counterweight Tuning"] = "Afinação de Contrapesos";
            t["Seasoned Crews"] = "Equipas Experientes";
            t["Veteran Crews"] = "Equipas Veteranas";
            t["Elite Crews"] = "Equipas de Elite";
            t["Ranging Shot"] = "Tiro de Ajuste";
            t["Siege Screens"] = "Anteparos de Cerco";
            t["Iron Plate"] = "Placa de Ferro";
            t["Veilstone Plate"] = "Placa de Veilstone";
            t["Shard Plate"] = "Placa de Fragmentos";
            t["Iron Brigandine"] = "Brigantina de Ferro";
            t["Veilstone Brigandine"] = "Brigantina de Veilstone";
            t["Shard Brigandine"] = "Brigantina de Fragmentos";
            t["Iron Barding"] = "Barda de Ferro";
            t["Veilstone Barding"] = "Barda de Veilstone";
            t["Shard Barding"] = "Barda de Fragmentos";
            t["Iron Plating"] = "Blindagem de Ferro";
            t["Veilstone Plating"] = "Blindagem de Veilstone";
            t["Shard Plating"] = "Blindagem de Fragmentos";
            t["Field Hospital"] = "Hospital de Campanha";

            // ============================================================
            // TechTree.json — roles (shared with the building SO assets
            // where the strings are identical)
            // ============================================================
            t["HQ / Research Era"] = "QG / Investigação de Era";
            t["Provides population"] = "Fornece população";
            t["Area resource trickle (no stacking)"] = "Fluxo de recursos em área (não acumula)";
            t["Train infantry — basics at L1, advanced at L2"] = "Treina infantaria — básica no N1, avançada no N2";
            t["Train ranged units — Archer at L1, Crossbowman at L2, Longbowman at L3"] = "Treina unidades de ataque à distância — Arqueiro no N1, Besteiro no N2, Arqueiro de Arco Longo no N3";
            t["Trains Litharchs (healers) and Alanthor Scholars (ritualists)"] = "Treina Litharchs (curandeiros) e Eruditos Alanthor (ritualistas)";
            t["Banking (deposit/interest)"] = "Banca (depósito/juros)";
            t["Economy"] = "Economia";
            t["Defense"] = "Defesa";
            t["Utility"] = "Utilidade";
            t["Military Offense"] = "Ofensiva Militar";
            t["Military Training"] = "Treino Militar";
            t["Banking"] = "Banca";
            t["Religion"] = "Religião";
            t["Fortification"] = "Fortificação";
            t["Military"] = "Militar";
            t["Support"] = "Apoio";
            t["Mobile trade HQ / Tariffs / Trains light military"] = "QG comercial móvel / Tarifas / Treina tropas ligeiras";
            t["Trade route node / vision"] = "Nó de rota comercial / visão";
            t["Trade bonus / Caravan spawn"] = "Bónus de comércio / criação de Caravanas";
            t["Banking (tariff synergy)"] = "Banca (sinergia com tarifas)";
            t["Produce Veilsteel"] = "Produz Veilsteel";
            t["Train siege engines"] = "Treina máquinas de cerco";
            t["Fast training, garrison, tower add-ons"] = "Treino rápido, guarnição, torres adicionais";
            t["Upgraded hut; bonus near wildlife"] = "Cabana melhorada; bónus perto de fauna";
            t["Upgraded hut; bonus near trees"] = "Cabana melhorada; bónus perto de árvores";
            t["Veilsteel forging & weapons"] = "Forja de Veilsteel e armas";
            t["Defensive tower (arrow fire, detects bloody ground to empower)"] = "Torre defensiva (dispara flechas; deteta chão ensanguentado para se fortalecer)";
            t["Batch-trains melee (5 or 10) with small discount"] = "Treina corpo a corpo em lotes (5 ou 10) com pequeno desconto";
            t["Generates Supplies, global building techs"] = "Gera Mantimentos, tecnologias globais de edifícios";
            t["Compartment boundaries"] = "Limites de compartimentos";
            t["Wall instance upgraded to ranged tower"] = "Troço de muralha melhorado para torre de tiro";
            t["Wall instance upgraded to gate - auto-opens for friendlies"] = "Troço de muralha melhorado para portão - abre automaticamente para aliados";
            t["Defensive tower with long range"] = "Torre defensiva de longo alcance";
            t["Passively generates Veilsteel (limit 1)"] = "Gera Veilsteel passivamente (limite 1)";
            t["Heavy-cavalry trainer (Cataphract)."] = "Treina cavalaria pesada (Catafracto).";

            // ============================================================
            // TechTree.json — tech descriptions
            // ============================================================
            t["Alanthor Guild. Faction-wide: every Gatherer's Hut also generates Iron (+12/min, doubled inside your influence border)."] = "Guilda Alanthor. Para toda a fação: cada Cabana do Recoletor gera também Ferro (+12/min, a dobrar dentro da tua fronteira de influência).";
            t["Alanthor Guild. Raises Gatherer's Hut Iron output to +24/min."] = "Guilda Alanthor. Aumenta a produção de Ferro da Cabana do Recoletor para +24/min.";
            t["Alanthor Guild. Raises Gatherer's Hut Iron output to +42/min."] = "Guilda Alanthor. Aumenta a produção de Ferro da Cabana do Recoletor para +42/min.";
            t["Alanthor Guild. Faction-wide: Gatherer's Huts also generate Veilstone (+6/min)."] = "Guilda Alanthor. Para toda a fação: as Cabanas do Recoletor geram também Veilstone (+6/min).";
            t["Alanthor Guild. Raises Gatherer's Hut Veilstone output to +18/min."] = "Guilda Alanthor. Aumenta a produção de Veilstone da Cabana do Recoletor para +18/min.";
            t["Alanthor Guild. Faction-wide: Gatherer's Huts also generate a slow trickle of Veilsteel (+6/min)."] = "Guilda Alanthor. Para toda a fação: as Cabanas do Recoletor geram também um fluxo lento de Veilsteel (+6/min).";
            t["Feraldis Raider Camp. Plunderers carry off 60% more from the players they raid."] = "Acampamento de Salteadores Feraldis. Os Saqueadores levam mais 60% dos jogadores que pilham.";
            t["Feraldis Raider Camp. Raises the Plunderer take to 2.4x."] = "Acampamento de Salteadores Feraldis. Aumenta o saque do Saqueador para 2,4x.";
            t["Feraldis Raider Camp. Raises the Plunderer take to 3.4x."] = "Acampamento de Salteadores Feraldis. Aumenta o saque do Saqueador para 3,4x.";
            t["Feraldis Raider Camp. Plunderers also strip Iron from the players they raid."] = "Acampamento de Salteadores Feraldis. Os Saqueadores roubam também Ferro aos jogadores que pilham.";
            t["Feraldis Raider Camp. Plunderers also strip Veilstone from the players they raid."] = "Acampamento de Salteadores Feraldis. Os Saqueadores roubam também Veilstone aos jogadores que pilham.";
            t["Feraldis Raider Camp. Plunderers strip a slow trickle of Veilsteel from the players they raid."] = "Acampamento de Salteadores Feraldis. Os Saqueadores roubam um fluxo lento de Veilsteel aos jogadores que pilham.";
            t["Alanthor Guild. Gatherer's Huts auto-repair after 10s without taking damage (5 HP/s out of combat). (behaviour-by-id)"] = "Guilda Alanthor. As Cabanas do Recoletor reparam-se automaticamente após 10 s sem sofrer dano (5 HP/s fora de combate). (behaviour-by-id)";
            t["Alanthor Guild. Below 50% HP a Gatherer's Hut casts a Slow burst on nearby enemies (-50% speed for 7.5s, 90s cooldown); also speeds up auto-repair (10 HP/s). (behaviour-by-id)"] = "Guilda Alanthor. Abaixo de 50% de HP, a Cabana do Recoletor lança uma onda de Lentidão sobre os inimigos próximos (-50% de velocidade durante 7,5 s, recarga de 90 s); também acelera a autorreparação (10 HP/s). (behaviour-by-id)";
            t["Alanthor Guild. Upgrades the low-HP defensive cast from Slow to Stop (-100% speed for 10s, 90s cooldown); further speeds up auto-repair (20 HP/s). (behaviour-by-id)"] = "Guilda Alanthor. Melhora o lançamento defensivo com HP baixo de Lentidão para Paragem (-100% de velocidade durante 10 s, recarga de 90 s); acelera ainda mais a autorreparação (20 HP/s). (behaviour-by-id)";
            t["Alanthor. Houses fight back: gain an auto-fire arrow attack (12 dmg, range 14). (behaviour-by-id)"] = "Alanthor. As casas defendem-se: ganham um ataque automático de flechas (12 de dano, alcance 14). (behaviour-by-id)";
            t["Upgrades worker tools to improve gathering speed."] = "Melhora as ferramentas dos trabalhadores para aumentar a velocidade de recolha.";
            t["Tool tier 2 — Alanthor. Improves gathering speed."] = "Ferramentas de nível 2 — Alanthor. Aumenta a velocidade de recolha.";
            t["Tool tier 3 — Alanthor. Improves gathering speed."] = "Ferramentas de nível 3 — Alanthor. Aumenta a velocidade de recolha.";
            t["Tool tier 4 — Alanthor. Improves gathering speed."] = "Ferramentas de nível 4 — Alanthor. Aumenta a velocidade de recolha.";
            t["Alanthor. +30% HP on all your buildings (behaviour-by-id)."] = "Alanthor. +30% de HP em todos os teus edifícios (behaviour-by-id).";
            t["Unlocks the Use Celestar reveal ability on Scouts and restores full Scout vision: settled max LOS and sight-ramp speed return to 100 percent (behaviour-by-id)."] = "Desbloqueia a capacidade de revelação Usar Celestar nos Batedores e restaura a visão total do Batedor: o alcance máximo de visão estabilizado e a velocidade de expansão da visão voltam a 100 por cento (behaviour-by-id).";
            t["Arms your Scouts with blades: Scouts gain their melee attack. Until researched Scouts are vision-only (behaviour-by-id)."] = "Arma os teus Batedores com lâminas: os Batedores ganham o seu ataque corpo a corpo. Até ser investigado, os Batedores servem apenas para visão (behaviour-by-id).";
            t["Standing levies train 20 percent faster at the Barracks."] = "As tropas de leva permanentes treinam 20 por cento mais depressa no Quartel.";
            t["Knapped stone heads harden the line: melee units gain +2 damage. (Interim faction-wide bump until per-battalion upgrades ship.)"] = "Pontas de pedra lascada endurecem a linha: as unidades corpo a corpo ganham +2 de dano. (Aumento provisório para toda a fação até chegarem as melhorias por batalhão.)";
            t["Weighted stone tips: ranged units gain +2 damage. (Interim faction-wide bump until per-battalion upgrades ship.)"] = "Pontas de pedra pesadas: as unidades de ataque à distância ganham +2 de dano. (Aumento provisório para toda a fação até chegarem as melhorias por batalhão.)";
            t["Improved arrow flight: +15 percent attack range for Archers."] = "Voo de flecha melhorado: +15 por cento de alcance de ataque para os Arqueiros.";
            t["Safe storage: Vault interest rises to 50 percent per minute."] = "Armazenamento seguro: os juros do Cofre sobem para 50 por cento por minuto.";
            t["Active credit: Vault interest rises to 75 percent per minute."] = "Crédito ativo: os juros do Cofre sobem para 75 por cento por minuto.";
            t["High-stakes investment: Vault interest rises to 100 percent per minute."] = "Investimento de alto risco: os juros do Cofre sobem para 100 por cento por minuto.";
            t["Unlocks Iron banking: Iron can be deposited in the Vault."] = "Desbloqueia a banca de Ferro: o Ferro pode ser depositado no Cofre.";
            t["Unlocks Veilstone banking: Veilstone can be deposited in the Vault."] = "Desbloqueia a banca de Veilstone: o Veilstone pode ser depositado no Cofre.";
            t["Unlocks Veilsteel banking: Veilsteel can be deposited in the Vault."] = "Desbloqueia a banca de Veilsteel: o Veilsteel pode ser depositado no Cofre.";
            t["Shrine healing aura rises from 1 to 3 percent of Max HP per second."] = "A aura de cura do Santuário sobe de 1 para 3 por cento do HP máximo por segundo.";
            t["Litharchs take up arms: gain a melee attack (6 damage every 1.5 seconds)."] = "Os Litharchs pegam em armas: ganham um ataque corpo a corpo (6 de dano a cada 1,5 segundos).";
            t["Shrine healing aura rises from 3 to 6 percent of Max HP per second."] = "A aura de cura do Santuário sobe de 3 para 6 por cento do HP máximo por segundo.";
            t["Shrine healing aura rises from 6 to 15 percent of Max HP per second."] = "A aura de cura do Santuário sobe de 6 para 15 por cento do HP máximo por segundo.";
            t["The Keep's auto-fire gains an extra single-target ballista bolt (18 siege damage) each volley."] = "O disparo automático da Fortaleza ganha um virote de balista adicional de alvo único (18 de dano de cerco) por salva.";
            t["The Keep's auto-fire gains an arcing trebuchet shot (36 siege damage, splash) each volley."] = "O disparo automático da Fortaleza ganha um tiro de trabuco em arco (36 de dano de cerco, com área) por salva.";
            t["The Keep's auto-fire strikes two additional targets per volley."] = "O disparo automático da Fortaleza atinge dois alvos adicionais por salva.";
            t["The Keep's hull is reinforced: +20 percent Max HP."] = "A estrutura da Fortaleza é reforçada: +20 por cento de HP máximo.";
            t["Active skill: doubles the fire rate of all Archers for 5 s. 40 s cooldown. Faction-wide, triggered from the Archery Range panel."] = "Competência ativa: duplica a cadência de tiro de todos os Arqueiros durante 5 s. Recarga de 40 s. Para toda a fação, ativada a partir do painel do Campo de Tiro com Arco.";
            t["Sect of Antiquity. All technologies and building upgrades take 30% less time and 10% fewer resources."] = "Seita da Antiguidade. Todas as tecnologias e melhorias de edifícios demoram menos 30% de tempo e custam menos 10% de recursos.";
            t["Sect of Renewal. All buildings gain +20% HP and construct 25% faster."] = "Seita da Renovação. Todos os edifícios ganham +20% de HP e são construídos 25% mais depressa.";
            t["Sect of Fortitude. Defensive structures cost 20% less and build 30% faster."] = "Seita da Fortitude. As estruturas defensivas custam menos 20% e são construídas 30% mais depressa.";
            t["Sect of Reclamation. Veilstone yields +25%, and every cursed node is harvestable regardless of tier."] = "Seita da Recuperação. O rendimento de Veilstone sobe +25% e todos os nós amaldiçoados podem ser explorados independentemente do nível.";
            t["Weapon tier 2: melee units deal +15% damage."] = "Armas de nível 2: as unidades corpo a corpo causam +15% de dano.";
            t["Weapon tier 3: melee units deal a further +30% damage."] = "Armas de nível 3: as unidades corpo a corpo causam mais +30% de dano.";
            t["Weapon tier 4: melee units deal a further +75% damage. Needs Veilsteel."] = "Armas de nível 4: as unidades corpo a corpo causam mais +75% de dano. Requer Veilsteel.";
            t["+30 HP, +5% speed, +1 damage, +1 defense to all Garrison line infantry."] = "+30 de HP, +5% de velocidade, +1 de dano, +1 de defesa para toda a infantaria de linha da Guarnição.";
            t["+30 HP, +10% speed, +1 damage, +1 defense to all Garrison line infantry."] = "+30 de HP, +10% de velocidade, +1 de dano, +1 de defesa para toda a infantaria de linha da Guarnição.";
            t["+30 HP, +15% speed, +2 damage, +2 defense to all Garrison line infantry."] = "+30 de HP, +15% de velocidade, +2 de dano, +2 de defesa para toda a infantaria de linha da Guarnição.";
            t["Unlocks the Charge passive for Garrison line infantry: the first strike deals +30% damage; recharges after 2 s out of combat."] = "Desbloqueia o passivo Carga para a infantaria de linha da Guarnição: o primeiro golpe causa +30% de dano; recarrega após 2 s fora de combate.";
            t["Unlocks the Shield Wall passive for Garrison line infantry: +30% defense against the first incoming attack while stationary; recharges after 3 s."] = "Desbloqueia o passivo Muralha de Escudos para a infantaria de linha da Guarnição: +30% de defesa contra o primeiro ataque recebido enquanto parada; recarrega após 3 s.";
            t["Arrow tier 2: ranged units deal +15% damage."] = "Flechas de nível 2: as unidades de ataque à distância causam +15% de dano.";
            t["Arrow tier 3: ranged units deal a further +30% damage."] = "Flechas de nível 3: as unidades de ataque à distância causam mais +30% de dano.";
            t["Arrow tier 4: ranged units deal a further +75% damage. Needs Veilsteel."] = "Flechas de nível 4: as unidades de ataque à distância causam mais +75% de dano. Requer Veilsteel.";
            t["+20 HP, +3 line of sight, +2 attack range to all Practice Range archers."] = "+20 de HP, +3 de linha de visão, +2 de alcance de ataque para todos os arqueiros do Campo de Treino.";
            t["+20 HP, +1 damage, +3 line of sight, +2 attack range to all Practice Range archers."] = "+20 de HP, +1 de dano, +3 de linha de visão, +2 de alcance de ataque para todos os arqueiros do Campo de Treino.";
            t["+50 HP, +2 damage, +5 line of sight, +3 attack range to all Practice Range archers."] = "+50 de HP, +2 de dano, +5 de linha de visão, +3 de alcance de ataque para todos os arqueiros do Campo de Treino.";
            t["-30% attack cooldown for all ranged units."] = "-30% de tempo de recarga de ataque para todas as unidades de ataque à distância.";
            t["Ranged attack cooldown drops to -50% total (stacks multiplicatively with Arrow Volley)."] = "O tempo de recarga do ataque à distância desce para -50% no total (acumula multiplicativamente com Salva de Flechas).";
            t["Unlocks the Deploy Stakes passive for Practice Range archers: +50% defense against the first cavalry charge once stationary for 3 s; recharges on move."] = "Desbloqueia o passivo Colocar Estacas para os arqueiros do Campo de Treino: +50% de defesa contra a primeira carga de cavalaria após 3 s parado; recarrega ao mover-se.";
            t["+30 HP, +5% speed, +1 damage, +1 defense to Outriders and Cataphracts."] = "+30 de HP, +5% de velocidade, +1 de dano, +1 de defesa para Ginetes e Catafractos.";
            t["+30 HP, +10% speed, +1 damage, +1 defense to Outriders and Cataphracts."] = "+30 de HP, +10% de velocidade, +1 de dano, +1 de defesa para Ginetes e Catafractos.";
            t["+30 HP, +15% speed, +2 damage, +2 defense to Outriders and Cataphracts."] = "+30 de HP, +15% de velocidade, +2 de dano, +2 de defesa para Ginetes e Catafractos.";
            t["Unlocks the War Horn active for Alanthor cavalry: allied cavalry within 20 m gain +50% damage on their next charge. 20 s window, 60 s cooldown."] = "Desbloqueia o ativo Corno de Guerra para a cavalaria Alanthor: a cavalaria aliada num raio de 20 m ganha +50% de dano na próxima carga. Janela de 20 s, recarga de 60 s.";
            t["Unlocks the Full Gallop active for Alanthor cavalry: allied cavalry within 20 m gain +40% move speed for 8 s but cannot attack during the burst. 75 s cooldown."] = "Desbloqueia o ativo A Todo o Galope para a cavalaria Alanthor: a cavalaria aliada num raio de 20 m ganha +40% de velocidade de movimento durante 8 s, mas não pode atacar durante o efeito. Recarga de 75 s.";
            t["Ballista bolts punch harder: +10 damage."] = "Os virotes da balista atingem com mais força: +10 de dano.";
            t["The ram is iron-shod: +100 HP so it survives the approach."] = "O aríete é ferrado: +100 de HP para sobreviver à aproximação.";
            t["Retuned counterweights: Trebuchet range 38 to 44."] = "Contrapesos afinados: alcance do Trabuco de 38 para 44.";
            t["+40 HP, +2 damage, +1 attack range to every Alanthor siege engine."] = "+40 de HP, +2 de dano, +1 de alcance de ataque para todas as máquinas de cerco Alanthor.";
            t["+40 HP, +3 damage, +2 attack range to every Alanthor siege engine."] = "+40 de HP, +3 de dano, +2 de alcance de ataque para todas as máquinas de cerco Alanthor.";
            t["+60 HP, +5 damage, +3 attack range to every Alanthor siege engine."] = "+60 de HP, +5 de dano, +3 de alcance de ataque para todas as máquinas de cerco Alanthor.";
            t["Unlocks the Ranging Shot active for Alanthor siege engines: after standing still for 3 s the next shot deals +100% damage. 45 s cooldown."] = "Desbloqueia o ativo Tiro de Ajuste para as máquinas de cerco Alanthor: após 3 s parada, o próximo tiro causa +100% de dano. Recarga de 45 s.";
            t["Unlocks the Siege Screens passive for Alanthor siege engines: +50% ranged defense while stationary; lost the moment the engine moves."] = "Desbloqueia o passivo Anteparos de Cerco para as máquinas de cerco Alanthor: +50% de defesa à distância enquanto parada; perde-se assim que a máquina se move.";
            t["Infantry armour tier 1: +1 defense to all melee units."] = "Armadura de infantaria de nível 1: +1 de defesa para todas as unidades corpo a corpo.";
            t["Infantry armour tier 2: +2 defense to all melee units."] = "Armadura de infantaria de nível 2: +2 de defesa para todas as unidades corpo a corpo.";
            t["Infantry armour tier 3: +3 defense to all melee units."] = "Armadura de infantaria de nível 3: +3 de defesa para todas as unidades corpo a corpo.";
            t["Ranged armour tier 1: +1 defense to all ranged units."] = "Armadura de atirador de nível 1: +1 de defesa para todas as unidades de ataque à distância.";
            t["Ranged armour tier 2: +2 defense to all ranged units."] = "Armadura de atirador de nível 2: +2 de defesa para todas as unidades de ataque à distância.";
            t["Ranged armour tier 3: +3 defense to all ranged units."] = "Armadura de atirador de nível 3: +3 de defesa para todas as unidades de ataque à distância.";
            t["Cavalry armour tier 1: +1 defense to all cavalry."] = "Armadura de cavalaria de nível 1: +1 de defesa para toda a cavalaria.";
            t["Cavalry armour tier 2: +2 defense to all cavalry."] = "Armadura de cavalaria de nível 2: +2 de defesa para toda a cavalaria.";
            t["Cavalry armour tier 3: +3 defense to all cavalry."] = "Armadura de cavalaria de nível 3: +3 de defesa para toda a cavalaria.";
            t["Siege armour tier 1: +1 defense to all siege engines."] = "Armadura de cerco de nível 1: +1 de defesa para todas as máquinas de cerco.";
            t["Siege armour tier 2: +2 defense to all siege engines."] = "Armadura de cerco de nível 2: +2 de defesa para todas as máquinas de cerco.";
            t["Siege armour tier 3: +3 defense to all siege engines."] = "Armadura de cerco de nível 3: +3 de defesa para todas as máquinas de cerco.";
            t["Unlocks the Deploy Field Hospital ability for Litharchs: a temporary building that heals nearby allied units, then destroys itself after 2 minutes. 300 s cooldown."] = "Desbloqueia a capacidade Montar Hospital de Campanha para os Litharchs: um edifício temporário que cura unidades aliadas próximas e se destrói passados 2 minutos. Recarga de 300 s.";

            // ============================================================
            // TechTree.json — effects, description, note
            // ============================================================
            t["Unlocks cultural choice (Runai, Feraldis, Alanthor). Starts 2-minute auto-despawn on all Gatherer's Huts (except Feraldis)."] = "Desbloqueia a escolha de cultura (Runai, Feraldis, Alanthor). Inicia o desaparecimento automático em 2 minutos de todas as Cabanas do Recoletor (exceto Feraldis).";
            t["+15% Supplies from trade routes; +25% if route length > 60u."] = "+15% de Mantimentos das rotas comerciais; +25% se o comprimento da rota for > 60u.";
            t["Reduce pack/unpack time by 40%; Bazaar gains +200 HP while packed."] = "Reduz o tempo de empacotar/desempacotar em 40%; o Bazar ganha +200 de HP enquanto empacotado.";
            t["Trade Hubs spawn 2 uncontrollable escorts with each Caravan. Each active Caravan grants +2 population housing."] = "Os Entrepostos Comerciais criam 2 escoltas incontroláveis com cada Caravana. Cada Caravana ativa concede +2 de alojamento de população.";
            t["Veilstone-Border waves aggro Runai units less often (-20% per tier, stacks to -60% at L3). Runai walks the border lands without provoking them."] = "As vagas da Fronteira de Veilstone atacam unidades Runai com menos frequência (-20% por nível, acumulando até -60% no N3). Os Runai percorrem as terras da fronteira sem as provocar.";
            t["Killing non-military units grants +15 Supplies and +1 Iron per kill to the attacker's owner."] = "Matar unidades não militares concede +15 de Mantimentos e +1 de Ferro por morte ao dono do atacante.";
            t["Military units carry Veilsteel shavings from kills (max 5); each grants +2% attack (stacks to +10%)."] = "As unidades militares transportam limalhas de Veilsteel das mortes (máx. 5); cada uma concede +2% de ataque (acumula até +10%).";
            t["Each closed wall compartment yields +8 Supplies per 10u² area per minute."] = "Cada compartimento de muralha fechado rende +8 de Mantimentos por 10u² de área por minuto.";
            t["+15% building HP, +20% repair rate."] = "+15% de HP dos edifícios, +20% de velocidade de reparação.";
            t["Grants 1 Sect Point and provides a single chapel slot."] = "Concede 1 Ponto de Seita e fornece uma única vaga de capela.";
            t["Choose exactly one culture. Existing buildings re-skin to chosen culture."] = "Escolhe exatamente uma cultura. Os edifícios existentes mudam de aparência para a cultura escolhida.";

            // ============================================================
            // Building SO assets — displayNames not present in TechTree.json
            // ============================================================
            t["War Totem"] = "Totem de Guerra";
            t["Mine"] = "Mina";
            t["Pasture"] = "Pastagem";
            t["Smelter"] = "Forno de Fundição";

            // ============================================================
            // Building SO assets — roles not present in TechTree.json
            // ============================================================
            t["HQ / Research Era / Research Economy / Worker upgrades / resource drop-off / Supply generation / Objective"] = "QG / Investigação de Era / Investigação de Economia / Melhorias de Trabalhadores / entrega de recursos / geração de Mantimentos / Objetivo";
            t["Trains Litharchs (healers)"] = "Treina Litharchs (curandeiros)";
            t["Fortified training + supply (Age 0 choice)"] = "Treino fortificado + mantimentos (escolha da Era 0)";
            t["Feraldis territory — plant on a blood pool; drinks blood into Fervor and claims ground"] = "Território Feraldis — planta-se numa poça de sangue; bebe o sangue convertendo-o em Fervor e reclama terreno";
            t["Workerless ore extraction - build next to an iron or veilstone patch"] = "Extração de minério sem trabalhadores - constrói junto a um filão de ferro ou veilstone";
            t["Feraldis cavalry - Raider at L1, War Chariot at L2"] = "Cavalaria Feraldis - Salteador no N1, Carro de Guerra no N2";
            t["Passive veilsteel generation; hosts the armour research ladders"] = "Geração passiva de veilsteel; acolhe as linhas de investigação de armaduras";

            // ============================================================
            // Building SO assets — descriptions
            // ============================================================
            t["The starting town center. Trains Workers and Scouts, banks gathered resources, and researches the core economy techs and the advance to Era II. Renamed to its cultured form (Town Hall / Trader's Hall / War Hall) at age-up."] = "O centro da povoação inicial. Treina Trabalhadores e Batedores, guarda os recursos recolhidos e investiga as tecnologias económicas centrais e o avanço para a Era II. Renomeado para a sua forma cultural (Salão da Vila / Salão dos Mercadores / Salão da Guerra) ao subir de era.";
            t["Population housing for Age 0 (the 'House'). Each one raises the population cap. Its post-age-up behavior splits per culture."] = "Alojamento de população da Era 0 (a 'Casa'). Cada uma aumenta o limite de população. O seu comportamento após a subida de era varia por cultura.";
            t["Early Age 0 supply generator. Emits a +Supplies aura over a small radius. At age-up it transforms into the culture's signature economy structure (wall anchor / trade wagon / Hunting or Logging station)."] = "Gerador de mantimentos do início da Era 0. Emite uma aura de +Mantimentos num pequeno raio. Ao subir de era, transforma-se na estrutura económica emblemática da cultura (âncora de muralha / carroça de comércio / posto de Caça ou Madeireiro).";
            t["Primary melee training building. Produces Spearmen and researches melee upgrades. Becomes the culture's melee structure (Garrison / Route Guard / Longhouse) at age-up."] = "Principal edifício de treino corpo a corpo. Produz Lanceiros e investiga melhorias corpo a corpo. Torna-se a estrutura corpo a corpo da cultura (Guarnição / Guarda de Rota / Casa Comunal) ao subir de era.";
            t["Ranged training building. Its level unlocks the archer ladder (Archer at L1, Crossbowman at L2, Longbowman at L3) and it researches the volley / fletching techs."] = "Edifício de treino de unidades de ataque à distância. O seu nível desbloqueia a progressão de arqueiros (Arqueiro no N1, Besteiro no N2, Arqueiro de Arco Longo no N3) e investiga as tecnologias de salva / empenamento.";
            t["Early religious building and one of the three Age 0 choice buildings. Trains Litharch healers, slowly heals friendly units in radius, and grants Religion Points on build (Runai +30% heal, Feraldis -30%)."] = "Edifício religioso inicial e um dos três edifícios de escolha da Era 0. Treina Litharchs curandeiros, cura lentamente as unidades amigas no seu raio e concede Pontos de Religião ao ser construído (Runai +30% de cura, Feraldis -30%).";
            t["Resource bank and one of the three Age 0 choice buildings that unlock the advance to Era II. Deposited resources earn compounding interest each minute; banking-grade techs raise the rate (Alanthor +30%, Runai -30%)."] = "Banco de recursos e um dos três edifícios de escolha da Era 0 que desbloqueiam o avanço para a Era II. Os recursos depositados rendem juros compostos a cada minuto; as tecnologias de banca aumentam a taxa (Alanthor +30%, Runai -30%).";
            t["Fortified keep and one of the three Age 0 choice buildings. Trains non-religious, non-siege military faster than normal, generates modest Supplies, and fires arrow volleys at attackers (Feraldis +50% HP, Alanthor -50%)."] = "Fortaleza fortificada e um dos três edifícios de escolha da Era 0. Treina militares não religiosos e não de cerco mais depressa do que o normal, gera Mantimentos modestos e dispara salvas de flechas contra os atacantes (Feraldis +50% de HP, Alanthor -50%).";
            t["Works every iron and veilstone node in range with no workers at all. Slower than mining by hand, but it never depletes the ore."] = "Explora todos os nós de ferro e veilstone ao alcance sem nenhum trabalhador. Mais lenta do que minerar à mão, mas nunca esgota o minério.";
            t["Feraldis cavalry house. There is no Royal Stable analogue for Feraldis; all cavalry trains here."] = "Casa da cavalaria Feraldis. Não existe equivalente da Estrebaria Real para os Feraldis; toda a cavalaria treina aqui.";

            // ============================================================
            // Unit SO assets — displayNames
            // (Veilstinger, Godsplinter, Crystalling, Litharch and
            // Feraldis_Berserker are kept as coined/proper names.)
            // ============================================================
            t["Spearman"] = "Lanceiro";
            t["Archer"] = "Arqueiro";
            t["Scout"] = "Batedor";
            t["Swordsman"] = "Espadachim";
            t["Battering Ram"] = "Aríete";
            t["Ballista"] = "Balista";
            t["Trebuchet"] = "Trabuco";
            t["King Lexor"] = "Rei Lexor";
            t["Holy Scholar"] = "Erudito Sagrado";
            t["Nobleman"] = "Nobre";
            t["Outrider"] = "Ginete";
            t["Ledger"] = "Escrivão";
            t["Alanthor_Sentinel"] = "Alanthor_Sentinela";
            t["Alanthor_Crossbowman"] = "Alanthor_Besteiro";
            t["Alanthor_Cataphract"] = "Alanthor_Catafracto";
            t["Runai_Spearman"] = "Runai_Lanceiro";
            t["Runai_Skirmisher"] = "Runai_Escaramuçador";
            t["Runai_Raider"] = "Runai_Salteador";
            t["Runai_Acolyte"] = "Runai_Acólito";
            t["Runai_Caravan"] = "Runai_Caravana";
            t["Raider"] = "Salteador";
            t["Plunderer"] = "Saqueador";
            t["Bloodletter"] = "Sangrador";
            t["Suicidal"] = "Suicida";
            t["Firethrower"] = "Lançador de Fogo";
            t["Axe Thrower"] = "Lançador de Machados";
            t["War Chariot"] = "Carro de Guerra";
            t["Feraldis_WarboarRider"] = "Feraldis_CavaleiroDeJavali";
            t["Feraldis_SiegeRam"] = "Feraldis_AríeteDeCerco";
            t["Feraldis_Iconoclast"] = "Feraldis_Iconoclasta";

            // ============================================================
            // LoadingTips.json — all 24 tips
            // ============================================================
            t["Build Gatherer's Huts near grass — each cell of empty ground inside the circle adds to your supply trickle."] = "Constrói Cabanas do Recoletor perto de erva — cada célula de terreno vazio dentro do círculo aumenta o teu fluxo de mantimentos.";
            t["Older Gatherer's Huts get priority — new huts placed inside an older circle earn nothing from the overlap."] = "As Cabanas do Recoletor mais antigas têm prioridade — cabanas novas colocadas dentro de um círculo mais antigo não ganham nada da sobreposição.";
            t["Miners auto-find new iron deposits when one runs out, but only within their Line of Sight."] = "Os mineiros encontram automaticamente novos depósitos de ferro quando um se esgota, mas apenas dentro da sua Linha de Visão.";
            t["Right-click your Hall with miners selected to force them to dump their cargo immediately."] = "Clica com o botão direito no teu Salão com mineiros selecionados para os obrigar a descarregar a carga imediatamente.";
            t["Builders chain to nearby unfinished structures after completing one — keep them grouped to save micro."] = "Os construtores encadeiam para estruturas inacabadas próximas depois de concluírem uma — mantém-nos agrupados para poupar micro.";
            t["Shift+click while placing a building keeps the cursor in placement mode for the next copy."] = "Shift+clique ao colocar um edifício mantém o cursor em modo de colocação para a cópia seguinte.";
            t["Battalions count as one unit for selection and orders — the leader is the dummy you don't see."] = "Os batalhões contam como uma única unidade para seleção e ordens — o líder é o fantoche que não vês.";
            t["The Border spreads from main nodes outward. Purify a node before its glow reaches your base."] = "A Fronteira alastra dos nós principais para fora. Purifica um nó antes que o seu fulgor chegue à tua base.";
            t["Religion Points come from Shrines and Chapels. A Temple unlocks up to six chapel slots."] = "Os Pontos de Religião vêm de Santuários e Capelas. Um Templo desbloqueia até seis vagas de capela.";
            t["Glow drops when units die. Pick it up fast — it's the only resource that doesn't have a safe stockpile."] = "O Fulgor cai quando as unidades morrem. Apanha-o depressa — é o único recurso que não tem reserva segura.";
            t["Archery Range can be upgraded for tougher armor and faster training, same cost curve as the Barracks."] = "O Campo de Tiro com Arco pode ser melhorado para armadura mais resistente e treino mais rápido, com a mesma curva de custos do Quartel.";
            t["The Alanthor culture uses Walls as their gatherer income. Build them to enclose population."] = "A cultura Alanthor usa as Muralhas como rendimento de recoleção. Constrói-as para cercar população.";
            t["Runaii caravans travel along trade lanes that you can re-route by placing new outposts."] = "As caravanas Runaii percorrem rotas comerciais que podes redirecionar colocando novos postos avançados.";
            t["Feraldis trains units in batches of five — line up the iron before queueing."] = "Os Feraldis treinam unidades em lotes de cinco — garante o ferro antes de pôr na fila.";
            t["Hold Position (H) keeps a unit still even while enemies provoke it. Useful for kiting screens."] = "Manter Posição (H) mantém uma unidade parada mesmo quando os inimigos a provocam. Útil para cortinas de proteção contra kiting.";
            t["Cliffs are impassable. Use them as walls against melee armies if a chokepoint isn't an option."] = "As falésias são intransponíveis. Usa-as como muralhas contra exércitos corpo a corpo se um estrangulamento não for opção.";
            t["Mountains are off-limits to pathing entirely — they make natural map borders, not obstacles to climb."] = "As montanhas estão totalmente vedadas ao movimento — formam fronteiras naturais do mapa, não obstáculos a escalar.";
            t["Place a Hut on a cell with smoothed ground — uneven terrain refuses building placement."] = "Coloca uma Cabana numa célula de terreno aplanado — o terreno irregular recusa a colocação de edifícios.";
            t["Your Hall's population provider is 20. Each Hut adds 10. Don't outgrow your food supply."] = "O fornecimento de população do teu Salão é 20. Cada Cabana acrescenta 10. Não cresças além dos teus mantimentos.";
            t["Equipment Tier is faction-wide; Unit Rank is per-unit veterancy. Both stack on top of each other."] = "O Nível de Equipamento é de toda a fação; a Patente de Unidade é veterania por unidade. Ambos se acumulam.";
            t["The minimap shows impassable terrain darker — plan attacks around the visible flat corridors."] = "O minimapa mostra o terreno intransponível mais escuro — planeia os ataques em torno dos corredores planos visíveis.";
            t["Iron is mined by Miners; Veilstone is mined by anyone but yields more to dedicated workers."] = "O Ferro é minerado por Mineiros; o Veilstone pode ser minerado por qualquer um, mas rende mais a trabalhadores dedicados.";
            t["Veilsteel is the Hall's tier-3 sink. Don't spend it casually — it gates your highest-end research."] = "O Veilsteel é o sumidouro de nível 3 do Salão. Não o gastes à toa — condiciona a tua investigação de topo.";
            t["Forests block sight and movement. Scouts pierce sight through trees but still pay the move cost."] = "As florestas bloqueiam visão e movimento. Os Batedores veem através das árvores, mas continuam a pagar o custo de movimento.";

            // ============================================================
            // Map infos — display names, size tags, descriptions
            // (Yiel Lymwérra is a proper name; Sundered Crown has an
            // empty description.)
            // ============================================================
            t["Test Map"] = "Mapa de Teste";
            t["Twin Spans"] = "Pontes Gémeas";
            t["Hollow Table"] = "Meseta Oca";
            t["Sundered Crown"] = "Coroa Partida";
            t["OPEN"] = "ABERTO";
            t["SMALL"] = "PEQUENO";
            t["SMALL / OPEN"] = "PEQUENO / ABERTO";
            t["LARGE / 3v3"] = "GRANDE / 3v3";
            t["The old proving grounds of the border marches. Open ground with iron and veilstone scattered wide — expand quickly or be out-mustered."] = "Os antigos campos de prova das marcas da fronteira. Terreno aberto com ferro e veilstone dispersos por toda a parte — expande depressa ou serás ultrapassado em forças.";
            t["Development test map — a small two-warband proving field for exercising game systems."] = "Mapa de teste de desenvolvimento — um pequeno campo de prova para dois bandos de guerra, para exercitar os sistemas do jogo.";
            t["Three warbands to a shore, split by a river nothing fords. Two stone spans are the only way over, and a well stands at each of the four bridgeheads — cross through the blight, or break the well first. Home ground is rich and safe; the river is not."] = "Três bandos de guerra em cada margem, separados por um rio que nada consegue vadear. Duas pontes de pedra são a única travessia, e um poço ergue-se em cada uma das quatro cabeças de ponte — atravessa pela praga ou destrói primeiro o poço. O terreno de casa é rico e seguro; o rio não.";
            t["A duel across 128 m of open ground. Home holds barely enough to open with; the north and south wings hold everything else, and sit exactly as far from you as from your enemy. One well stands on the mesa in the middle — the map's only objective."] = "Um duelo ao longo de 128 m de terreno aberto. A base mal tem o suficiente para começar; as alas norte e sul guardam tudo o resto e ficam exatamente à mesma distância de ti e do teu inimigo. Um poço ergue-se na meseta ao centro — o único objetivo do mapa.";

            // ============================================================
            // Scenario infos — display names and descriptions
            // ============================================================
            t["Scenario A"] = "Cenário A";
            t["Scenario B"] = "Cenário B";
            t["Scenario C"] = "Cenário C";
            t["Scenario A — placeholder.\n\nReplace this description and the thumbnail (ScenarioA.jpg), and build out ScenarioA.unity. Fields live in ScenarioA.asset."] = "Cenário A — provisório.\n\nSubstitui esta descrição e a miniatura (ScenarioA.jpg) e constrói o ScenarioA.unity. Os campos estão em ScenarioA.asset.";
            t["Scenario B — placeholder.\n\nReplace this description and the thumbnail (ScenarioB.jpg), and build out ScenarioB.unity. Fields live in ScenarioB.asset."] = "Cenário B — provisório.\n\nSubstitui esta descrição e a miniatura (ScenarioB.jpg) e constrói o ScenarioB.unity. Os campos estão em ScenarioB.asset.";
            t["Scenario C — placeholder.\n\nReplace this description and the thumbnail (ScenarioC.jpg), and build out ScenarioC.unity. Fields live in ScenarioC.asset."] = "Cenário C — provisório.\n\nSubstitui esta descrição e a miniatura (ScenarioC.jpg) e constrói o ScenarioC.unity. Os campos estão em ScenarioC.asset.";
        }
    }
}
