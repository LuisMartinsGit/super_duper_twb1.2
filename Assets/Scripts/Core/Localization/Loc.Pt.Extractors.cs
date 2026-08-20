// Loc.Pt.Extractors.cs
// Portuguese for the entity extractors: display names resolved by the
// EntityExtractors name ladder (translated at the render sites — the
// resolvers themselves stay English because the strings double as
// GameUICatalog icon keys), training/research/build action labels, the
// shared BuildTooltip scaffolding, ability names, and Keep wing strings.
// Location: Assets/Scripts/Core/Localization/Loc.Pt.Extractors.cs

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddExtractors(Dictionary<string, string> t)
        {
            // ── Tooltip scaffolding (BuildTooltip + requirement templates) ──
            // "Cost: " is a CONTRACT key: BuildTooltip emits "\n" + Loc.T("Cost: ")
            // and ActionsPanelPrefabBinder splits on the same expression.
            t["Cost: "] = "Custo: ";
            t["Time: {0}s"] = "Tempo: {0}s";
            t["{0}  (Lv {1})"] = "{0}  (Nv {1})";
            t["{0}  (Temple Lv {1})"] = "{0}  (Templo Nv {1})";
            t["Requires Lv {0} {1}"] = "Requer {1} de Nv {0}";
            t["Requires Temple Level {0}"] = "Requer Templo de Nível {0}";
            t["Requires: {0}"] = "Requer: {0}";
            t["Requires: Era {0}"] = "Requer: Era {0}";
            t["Build {0}"] = "Construir {0}";
            t["Recharging: {0}s"] = "A recarregar: {0}s";
            t["{0} Supplies, {1} Iron"] = "{0} Mantimentos, {1} Ferro";
            t["{0} Supplies, {1} Iron, {2} Veilstone"] = "{0} Mantimentos, {1} Ferro, {2} Veilstone";
            t["{0} — the sect's unique unit"] = "{0} — a unidade única da seita";
            t["(Requires Reliquary lever Lv II)"] = "(Requer alavanca do Relicário de Nv II)";
            t["All three wing slots are used"] = "As três alas já estão ocupadas";
            t["A wing is already under construction"] = "Já existe uma ala em construção";

            // ── Entity action buttons ──
            t["Build Wall"] = "Construir Muralha";
            t["Place a connected wall hub. Auto-builds in 30s with no builder."] =
                "Coloca um nó de muralha ligado. Constrói-se sozinho em 30s, sem construtor.";
            t["Unpack"] = "Montar";
            t["Unpack wagon back into Thessara's Bazaar"] =
                "Monta a carroça de volta no Bazar de Thessara";
            t["Pack"] = "Desmontar";
            t["Pack Bazaar into a mobile wagon"] = "Desmonta o Bazar numa carroça móvel";
            t["Convert to Wall Hub"] = "Converter em Nó de Muralha";
            t["Convert to Watch Tower"] = "Converter em Torre de Vigia";
            t["Convert to Gate ({0}x)"] = "Converter em Portão ({0}x)";
            t["Convert to Tower"] = "Converter em Torre";
            t["Replaces the hut with a Wall Hub. Adjacent hubs auto-link into wall segments."] =
                "Substitui a cabana por um Nó de Muralha. Nós adjacentes ligam-se automaticamente em segmentos de muralha.";
            t["Replaces the hut with a stand-alone Alanthor Watch Tower (ranged defense)."] =
                "Substitui a cabana por uma Torre de Vigia Alanthor independente (defesa à distância).";
            t["Short segment — gate will span {0} instances. Groups wider than {0} may not fit."] =
                "Segmento curto — o portão abrangerá {0} troços. Grupos mais largos do que {0} podem não passar.";
            t["3-instance opening. Units can path through."] =
                "Abertura de 3 troços. As unidades podem atravessar.";
            t["Reinforces this wall section into a watchtower (ranged defense)."] =
                "Reforça esta secção de muralha numa torre de vigia (defesa à distância).";

            // ── Armor types + bonus-vs text (stat chips) ──
            t["Light Infantry"] = "Infantaria Ligeira";
            t["Heavy Infantry"] = "Infantaria Pesada";
            t["Structure"] = "Estrutura";
            t["vs"] = "contra";
            t["Infantry"] = "Infantaria";
            t["Cavalry"] = "Cavalaria";
            t["Ranged"] = "À Distância";
            t["Siege"] = "Cerco";
            t["Heavy"] = "Pesados";
            t["Light"] = "Ligeiros";
            t["Building"] = "Edifício";
            t["Worker"] = "Trabalhador";
            t["Religious"] = "Religiosos";
            t["Ship"] = "Navios";

            // ── Building names (GetBuildingName ladder + cultured renames;
            //    translated by the selection header, resolvers stay English) ──
            t["Hall"] = "Salão";
            t["Barracks"] = "Quartel";
            t["Archery Range"] = "Campo de Tiro com Arco";
            t["Gatherer's Hut"] = "Cabana do Recoletor";
            t["Hut"] = "Cabana";
            t["Depot"] = "Depósito";
            t["Workshop"] = "Oficina";
            t["Shrine of Ahridan"] = "Santuário de Ahridan";
            t["Temple of Ridan"] = "Templo de Ridan";
            t["Vault of Almiérra"] = "Cofre de Almiérra";
            t["Fiendstone Keep"] = "Fortaleza de Fiendstone";
            t["Forge"] = "Forja";
            t["The Reliquary"] = "O Relicário";
            t["Reliquary"] = "Relicário";
            t["Wall Hub"] = "Nó de Muralha";
            t["Wall Tower"] = "Torre de Muralha";
            t["Wall Gate"] = "Portão de Muralha";
            t["Wall"] = "Muralha";
            t["Wall Segment"] = "Segmento de Muralha";
            t["Runai Outpost"] = "Posto Avançado Runai";
            t["Trade Hub"] = "Centro de Comércio";
            t["Trading Post"] = "Entreposto Comercial";
            t["Thessara's Bazaar"] = "Bazar de Thessara";
            t["Bazaar Wagon"] = "Carroça do Bazar";
            t["Siege Workshop"] = "Oficina de Cerco";
            t["Watch Tower"] = "Torre de Vigia";
            t["Siege Yard"] = "Estaleiro de Cerco";
            t["Royal Stable"] = "Estrebaria Real";
            t["Hunting Lodge"] = "Cabana de Caça";
            t["Logging Station"] = "Posto Madeireiro";
            t["Longhouse"] = "Casa Comunal";
            t["Totem Tower"] = "Torre-Totem";
            t["Veilstone Hive"] = "Colmeia de Veilstone";
            t["Curse Node"] = "Nó da Maldição";
            t["Warbrand Foundry"] = "Fundição Warbrand";
            t["War Hall"] = "Salão de Guerra";
            t["Garrison"] = "Guarnição";
            t["Thrower Camp"] = "Acampamento de Lançadores";
            t["Practice Range"] = "Campo de Treino";
            t["Raider Camp"] = "Acampamento de Salteadores";
            t["Veilsteel Mine"] = "Mina de Veilsteel";
            t["Iron Deposit"] = "Depósito de Ferro";
            t["Veilstone Node"] = "Nó de Veilstone";

            // Chapel names composed as Prettify(sectId) + " Chapel".
            t["Antiquity Chapel"] = "Capela da Antiguidade";
            t["Renewal Chapel"] = "Capela da Renovação";
            t["Fortitude Chapel"] = "Capela da Fortitude";
            t["Reclamation Chapel"] = "Capela da Recuperação";
            t["Silence Chapel"] = "Capela do Silêncio";
            t["Justice Chapel"] = "Capela da Justiça";
            t["Veneration Chapel"] = "Capela da Veneração";
            t["Witness Chapel"] = "Capela do Testemunho";
            t["War Chapel"] = "Capela da Guerra";
            t["Ash Chapel"] = "Capela das Cinzas";
            t["Ruin Chapel"] = "Capela da Ruína";
            t["Wrath Chapel"] = "Capela da Ira";

            // ── Unit names (GetUnitName ladder + PresentationId table).
            //    Coined proper names (Litharch, Berserker, Crystalling,
            //    Veilstinger, Godsplinter, Ledger, Corruptor) stay as-is. ──
            t["Swordsman"] = "Espadachim";
            t["Archer"] = "Arqueiro";
            t["Scout"] = "Batedor";
            t["Siege Unit"] = "Unidade de Cerco";
            t["Unit"] = "Unidade";
            t["Longbowman"] = "Arqueiro de Arco Longo";
            t["Spearman"] = "Lanceiro";
            t["Skirmisher"] = "Escaramuçador";
            t["Raider"] = "Salteador";
            t["Catapult"] = "Catapulta";
            t["Sentinel"] = "Sentinela";
            t["Crossbowman"] = "Besteiro";
            t["Cataphract"] = "Catafracto";
            t["Ballista"] = "Balista";
            t["Nobleman"] = "Nobre";
            t["Battering Ram"] = "Aríete";
            t["Trebuchet"] = "Trabuco";
            t["Outrider"] = "Ginete";
            t["Hunter"] = "Caçador";
            t["Warboar Rider"] = "Cavaleiro de Javali";
            t["Siege Ram"] = "Aríete de Cerco";
            t["King Lexor"] = "Rei Lexor";
            t["Scholar"] = "Erudito";
            t["Acolyte"] = "Acólito";
            t["Iconoclast"] = "Iconoclasta";
            t["Lorekeeper"] = "Guardião do Saber";
            t["Tinker"] = "Funileiro";
            t["Inquisitor"] = "Inquisidor";
            t["Warbreaker"] = "Quebra-Guerras";
            t["Scar Guard"] = "Guarda da Cicatriz";
            t["Golem Autark"] = "Golem Autarca";
            t["Stone Warden"] = "Guardião de Pedra";
            t["Archivist Adept"] = "Adepto Arquivista";
            t["Flame Warden"] = "Guardião da Chama";
            t["Vault Keeper"] = "Guardião do Cofre";
            t["Glassmark Arcanist"] = "Arcanista Glassmark";
            t["Judicator"] = "Judicador";
            t["Ashblade"] = "Lâmina de Cinzas";
            t["Brandbreaker"] = "Quebra-Marcas";
            t["Chaincaster"] = "Lança-Correntes";
            t["Nullblade"] = "Lâmina Nula";
            t["Magic"] = "Místico";

            // ── Ability names (AbilityCatalog seed — data stays English) ──
            t["King's Call"] = "Chamamento do Rei";
            t["Liquid Courage"] = "Coragem Líquida";
            t["Veilshift Withdrawal"] = "Abstinência do Véu";
            t["Life Cling"] = "Apego à Vida";
            t["Automate Facility"] = "Automatizar Instalação";
            t["Under Automation"] = "Sob Automação";
            t["Use Celestar"] = "Usar Celestar";
            t["Scout Sight"] = "Visão de Batedor";
            t["War Horn"] = "Corno de Guerra";
            t["Full Gallop"] = "A Todo o Galope";
            t["Deploy Field Hospital"] = "Montar Hospital de Campanha";

            // ── Alanthor building-fired actives ──
            t["Choreographed Volleys"] = "Salvas Coreografadas";
            t["All your Archers fire twice as fast for 5 s."] =
                "Todos os teus Arqueiros disparam duas vezes mais depressa durante 5 s.";
            t["Ranging Shot"] = "Tiro de Ajuste";
            t["Planted siege engines load an aimed shot: +100% damage on their next shot."] =
                "As máquinas de cerco montadas carregam um tiro apontado: +100% de dano no próximo tiro.";

            // ── Reliquary abilities ──
            t["Scry"] = "Vidência";
            t["Lockout"] = "Bloqueio";
            t["Vision"] = "Visão";
            t["Scry — reveal a distant area of the map ({0}m for {1}s)."] =
                "Vidência — revela uma área distante do mapa ({0}m durante {1}s).";
            t["Ability Lockout — enemy attack & ability cooldowns in the target circle stop recovering for {0}s."] =
                "Bloqueio de Habilidades — os tempos de recarga de ataques e habilidades inimigas no círculo alvo deixam de recuperar durante {0}s.";
            t["Vision Aura — a wide reveal around the Reliquary ({0}m for {1}s)."] =
                "Aura de Visão — uma revelação ampla em redor do Relicário ({0}m durante {1}s).";

            // ── Long unit / building blurbs (chapel + Temple rosters) ──
            t["Lorekeeper — Antiquity support scholar.\nReveals stealthed enemies nearby "
                + "(Lv II: doubled radius; Lv III: far-sight through fog).\nGarrison the "
                + "Reliquary (stand beside it) to speed up its ability cooldowns."] =
                "Guardião do Saber — erudito de apoio da Antiguidade.\nRevela inimigos furtivos "
                + "por perto (Nv II: raio duplicado; Nv III: visão longínqua através do nevoeiro)."
                + "\nGuarnece o Relicário (posiciona-te ao lado dele) para acelerar os tempos de "
                + "recarga das suas habilidades.";
            t["The Reliquary — Antiquity intel hub (one per faction).\nLv I: Scry (reveal a "
                + "distant area). Lv II: adds Ability Lockout and a Vision aura. Lv III: "
                + "cooldowns -30%, garrison effects doubled.\nGarrison a Lorekeeper beside it "
                + "to recharge abilities faster."] =
                "O Relicário — centro de informações da Antiguidade (um por facção).\nNv I: "
                + "Vidência (revela uma área distante). Nv II: adiciona Bloqueio de Habilidades "
                + "e uma aura de Visão. Nv III: recargas -30%, efeitos de guarnição duplicados."
                + "\nGuarnece um Guardião do Saber ao lado dele para recarregar as habilidades "
                + "mais depressa.";
            t["Holy Scholar — purifies wells (channels the ritual) and walks a wide cleansing "
                + "font that burns away curse and blood."] =
                "Erudito Sagrado — purifica poços (canaliza o ritual) e transporta uma fonte "
                + "purificadora ampla que consome maldição e sangue.";
            t["Corruptor — channels on a well to crack it OPEN, leaving it vulnerable to attack "
                + "for a short window. The curse defends it while it is exposed; break the well "
                + "before it seals. Destroy every well to win."] =
                "Corruptor — canaliza num poço para o ABRIR, deixando-o vulnerável a ataques "
                + "durante uma curta janela. A maldição defende-o enquanto está exposto; destrói "
                + "o poço antes que se sele. Destrói todos os poços para vencer.";
            t["Lorekeeper — Antiquity support scholar. Reveals stealthed enemies nearby; "
                + "garrison the Reliquary to speed its cooldowns."] =
                "Guardião do Saber — erudito de apoio da Antiguidade. Revela inimigos furtivos "
                + "por perto; guarnece o Relicário para acelerar as suas recargas.";
            t["Tinker — Renewal field engineer. Repairs and raises structures; cannot fight or mine."] =
                "Funileiro — engenheiro de campo da Renovação. Repara e ergue estruturas; não "
                + "pode lutar nem minerar.";
            t["Inquisitor — Justice support caster. Periodically cleanses a debuff (such as a "
                + "Codex freeze) from a nearby ally."] =
                "Inquisidor — conjurador de apoio da Justiça. Purga periodicamente um efeito "
                + "negativo (como um congelamento do Códice) de um aliado próximo.";
            t["Warbreaker — War's heavy elite. A slow, hard-hitting frontline bruiser."] =
                "Quebra-Guerras — a elite pesada da Guerra. Um combatente de primeira linha "
                + "lento e demolidor.";

            // ── Fiendstone Keep wings (KeepWingConfig stays English) ──
            t["War Wing"] = "Ala de Guerra";
            t["Civic Wing"] = "Ala Cívica";
            t["Engineers' Wing"] = "Ala dos Engenheiros";
            t["Economic Wing"] = "Ala Económica";
            t["Librarians' Wing"] = "Ala dos Bibliotecários";
            t["Temple Wing"] = "Ala do Templo";
            t["Wing"] = "Ala";
            t["Train Barracks, Archery Range and Stable units at the Keep."] =
                "Treina unidades do Quartel, do Campo de Tiro com Arco e do Estábulo na Fortaleza.";
            t["The Keep generates Supplies and trains Workers."] =
                "A Fortaleza gera Mantimentos e treina Trabalhadores.";
            t["Three ballista emplacements (extra bolts each volley) and +25% Keep HP."] =
                "Três posições de balista (virotes extra por salva) e +25% de PV da Fortaleza.";
            t["Gathers like a Gatherer's Hut with a larger area (Supplies income)."] =
                "Recolhe como uma Cabana do Recoletor, com uma área maior (rendimento de Mantimentos).";
            t["Hall economy techs researchable at the Keep; all research 20% faster."] =
                "As tecnologias económicas do Salão podem ser investigadas na Fortaleza; toda a "
                + "investigação é 20% mais rápida.";
            t["Trains sect units (Litharchs for now); grants +1 Religion Point when built."] =
                "Treina unidades de seita (Litharchs, por agora); concede +1 Ponto de Religião "
                + "quando construída.";
        }
    }
}
