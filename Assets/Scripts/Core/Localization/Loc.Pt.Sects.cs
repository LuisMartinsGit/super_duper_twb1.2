// Loc.Pt.Sects.cs
// Portuguese for the sect text corpus: SectInfo (lore, passives, actives,
// buildings, units, technology, short names), the canon active Name/Description
// specs authored in SectLeverEffects.Alanthor.cs (wrapped once at the SectInfo
// display boundary — the data layer stays English), and the SectRadii reach
// labels that ride along in the composed tooltip.

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddSects(Dictionary<string, string> t)
        {
            // ── Sect short names (SectInfo.ShortName — display only; the
            //    "Sect_*" ids never pass through here) ─────────────────────
            t["Antiquity"]   = "Antiguidade";
            t["Renewal"]     = "Renovação";
            t["Fortitude"]   = "Fortitude";
            t["Reclamation"] = "Recuperação";
            t["Silence"]     = "Silêncio";
            t["Justice"]     = "Justiça";
            t["Veneration"]  = "Veneração";
            t["Witness"]     = "Testemunha";
            t["War"]         = "Guerra";
            t["Ash"]         = "Cinzas";
            t["Ruin"]        = "Ruína";
            t["Wrath"]       = "Ira";

            // ── Lore ────────────────────────────────────────────────────────
            t["Keepers of every name that fell to the Border; the dead tally the living's enemies."] =
                "Guardiões de cada nome que tombou perante a Fronteira; os mortos contam os inimigos dos vivos.";
            t["Healers in stone — they teach walls to forget the blows they took."] =
                "Curandeiros na pedra — ensinam as muralhas a esquecer os golpes que sofreram.";
            t["Their hymns weigh more than mortar. Where they pray, the keep does not break."] =
                "Os seus hinos pesam mais do que a argamassa. Onde rezam, a fortaleza não cede.";
            t["They wrest tools, mines, and dying soldiers back from the Veilstone's grasp."] =
                "Arrancam ferramentas, minas e soldados moribundos das garras da Veilstone.";
            t["A doctrine of stillness — to stand is to be unkillable."] =
                "Uma doutrina de quietude — quem permanece imóvel não pode ser morto.";
            t["Every wound your soldiers take is debt; their gods collect at sword-point."] =
                "Cada ferida dos teus soldados é uma dívida; os seus deuses cobram à ponta da espada.";
            t["Each kill is a hymn. Each hymn makes the next blow strike truer."] =
                "Cada morte é um hino. Cada hino faz o golpe seguinte acertar mais certeiro.";
            t["They see what others miss — the road ahead, the trap, the spy."] =
                "Veem o que aos outros escapa — o caminho adiante, a armadilha, o espião.";
            t["The forge-faith. Their barracks burn brighter and empty faster."] =
                "A fé da forja. Os seus quartéis ardem com mais brilho e esvaziam-se mais depressa.";
            t["They embrace dying. Where their dead fall, the earth keeps burning."] =
                "Abraçam o morrer. Onde os seus mortos tombam, a terra continua a arder.";
            t["They love what is broken. Walls fall easier when they sing."] =
                "Amam o que está quebrado. As muralhas caem com mais facilidade quando eles cantam.";
            t["The wounded fight harder under their banner — bleeding makes them dangerous."] =
                "Os feridos lutam com mais afinco sob o seu estandarte — sangrar torna-os perigosos.";
            t["An unknown sect."] = "Uma seita desconhecida.";

            // ── Passives (name — description, one key each) ────────────────
            t["Tally of the Lost — your units gain +dmg for each unit-type they have killed in this match."] =
                "Contagem dos Perdidos — as tuas unidades ganham +dano por cada tipo de unidade que já mataram nesta partida.";
            t["Hands That Mend — your buildings auto-repair when out of combat."] =
                "Mãos Que Remendam — os teus edifícios reparam-se automaticamente fora de combate.";
            t["Veiled Stone — your walls and towers gain bonus HP."] =
                "Pedra Velada — as tuas muralhas e torres ganham HP adicional.";
            t["Border-Hardened — your units take less damage from Border sources."] =
                "Temperados pela Fronteira — as tuas unidades sofrem menos dano de fontes da Fronteira.";
            t["Steadfast Vigil — your units gain armor while holding position."] =
                "Vigília Inabalável — as tuas unidades ganham armadura enquanto mantêm a posição.";
            t["Marked for Sentence — any unit that kills one of yours takes bonus damage from your army."] =
                "Marcado para Sentença — qualquer unidade que mate uma das tuas sofre dano adicional do teu exército.";
            t["Fervor — your unit kills grant a stacking damage and attack-rate buff."] =
                "Fervor — as mortes às mãos das tuas unidades concedem um bónus acumulável de dano e cadência de ataque.";
            t["All-Seeing — your Scout units gain extended vision."] =
                "Omnividente — as tuas unidades Batedoras ganham visão alargada.";
            t["Forged in Battle — your military units cost less and train faster."] =
                "Forjados em Batalha — as tuas unidades militares custam menos e treinam mais depressa.";
            t["Pyre's Promise — your units leave a burning patch on death."] =
                "Promessa da Pira — as tuas unidades deixam uma mancha em chamas ao morrer.";
            t["Profane Hands — your units deal bonus damage vs buildings and refund cost when one falls to them."] =
                "Mãos Profanas — as tuas unidades causam dano adicional a edifícios e devolvem o custo quando um cai perante elas.";
            t["Spite of the Forsaken — your wounded units deal more damage the lower their HP."] =
                "Despeito dos Abandonados — as tuas unidades feridas causam mais dano quanto mais baixo estiver o seu HP.";

            // ── Legacy tier-1 active templates (SectInfo) ──────────────────
            t["Recall the Codex — enemy attack & ability cooldowns in a {0}m circle stop recovering for {1}s (Lv III also inflates their current cooldowns +50%)."] =
                "Evocar o Códice — as recargas de ataque e habilidade inimigas num círculo de {0}m param de recuperar durante {1}s (no Nv III as recargas atuais também aumentam +50%).";
            t["Heal Circle — restores {0} HP to all allied units within {1}m."] =
                "Círculo de Cura — restaura {0} HP a todas as unidades aliadas num raio de {1}m.";
            t["Bulwark — allied units in {0}m gain +{1} armor for {2}s."] =
                "Baluarte — as unidades aliadas num raio de {0}m ganham +{1} de armadura durante {2}s.";
            t["Reclaim Vigour — heals allied units in a {0}m radius for {1} HP."] =
                "Reaver o Vigor — cura as unidades aliadas num raio de {0}m em {1} HP.";
            t["Whisper-Wind — allied units in {0}m gain x{1} move-speed for {2}s."] =
                "Vento-Sussurro — as unidades aliadas num raio de {0}m ganham x{1} de velocidade durante {2}s.";
            t["Eye of the Law — reveals fog of war in a {0}m circle for {1}s."] =
                "Olho da Lei — revela o nevoeiro de guerra num círculo de {0}m durante {1}s.";
            t["Litany — allied units in {0}m gain x{1} damage for {2}s."] =
                "Litania — as unidades aliadas num raio de {0}m ganham x{1} de dano durante {2}s.";
            t["All-Seeing Gaze — reveals fog of war in a {0}m radius for {1}s."] =
                "Olhar Omnividente — revela o nevoeiro de guerra num raio de {0}m durante {1}s.";
            t["War March — allied units in {0}m gain x{1} move speed for {2}s."] =
                "Marcha de Guerra — as unidades aliadas num raio de {0}m ganham x{1} de velocidade durante {2}s.";
            t["Burning Ground — covers a {0}m circle in flame, dealing {1} dmg/s for {2}s."] =
                "Chão Ardente — cobre um círculo de {0}m em chamas, causando {1} de dano/s durante {2}s.";
            t["Profane Strike — burst {0} damage to everything in a {1}m circle."] =
                "Golpe Profano — {0} de dano instantâneo a tudo num círculo de {1}m.";
            t["Spawn Pyre — drops a burning pillar at the target, scorching {0}m for {1}s."] =
                "Invocar Pira — larga um pilar em chamas no alvo, queimando {0}m durante {1}s.";

            // ── Legacy tier-2/3 active templates (SectInfo) ────────────────
            t["Sentence — burst {0} divine damage in a {1}m circle."] =
                "Sentença — {0} de dano divino instantâneo num círculo de {1}m.";
            t["Final Sentence — massive {0} divine damage in a {1}m circle. The ultimate verdict."] =
                "Sentença Final — {0} de dano divino massivo num círculo de {1}m. O veredicto derradeiro.";
            t["Mason's Blessing — allied units in {0}m gain +{1} armor for {2}s."] =
                "Bênção do Pedreiro — as unidades aliadas num raio de {0}m ganham +{1} de armadura durante {2}s.";
            t["Reckoning of the Rebuilt — {0} crushing damage to enemies in a {1}m circle."] =
                "Juízo dos Reconstruídos — {0} de dano esmagador aos inimigos num círculo de {1}m.";
            t["Bloodfury — allied units in {0}m gain x{1} damage for {2}s."] =
                "Fúria de Sangue — as unidades aliadas num raio de {0}m ganham x{1} de dano durante {2}s.";
            t["Annihilation — {0} devastating damage to everything hostile in a {1}m circle."] =
                "Aniquilação — {0} de dano devastador a tudo o que for hostil num círculo de {1}m.";

            // ── Composed tooltip suffixes ──────────────────────────────────
            t["Cooldown: {0}s."] = "Recarga: {0}s.";
            t["Reach: {0}. Cooldown: {1}s."] = "Alcance: {0}. Recarga: {1}s.";

            // ── SectRadii reach labels ─────────────────────────────────────
            t["Single Target"] = "Alvo Único";
            t["Small (8m)"]    = "Pequeno (8m)";
            t["Medium (15m)"]  = "Médio (15m)";
            t["Large (25m)"]   = "Grande (25m)";

            // ── Active names — legacy table + canon slot names ─────────────
            t["Eye of the Law"]           = "Olho da Lei";
            t["Sentence"]                 = "Sentença";
            t["Final Sentence"]           = "Sentença Final";
            t["Heal Circle"]              = "Círculo de Cura";
            t["Mason's Blessing"]         = "Bênção do Pedreiro";
            t["Reckoning of the Rebuilt"] = "Juízo dos Reconstruídos";
            t["War March"]                = "Marcha de Guerra";
            t["Bloodfury"]                = "Fúria de Sangue";
            t["Annihilation"]             = "Aniquilação";
            t["Recall the Codex"]         = "Evocar o Códice";
            t["Deepen the Codex"]         = "Aprofundar o Códice";
            t["Seal the Codex"]           = "Selar o Códice";
            t["Bulwark"]                  = "Baluarte";
            t["Stoneveil"]                = "Véu de Pedra";
            t["Unbroken Oath"]            = "Juramento Inquebrado";
            t["Reclaim Vigour"]           = "Reaver o Vigor";
            t["Harvest the Veil"]         = "Ceifar o Véu";
            t["Greater Harvest"]          = "Ceifa Maior";
            t["Whisper-Wind"]             = "Vento-Sussurro";
            t["Hush"]                     = "Emudecer";
            t["Entomb"]                   = "Sepultar";
            t["Litany"]                   = "Litania";
            t["Crystal Communion"]        = "Comunhão do Cristal";
            t["Greater Communion"]        = "Comunhão Maior";
            t["All-Seeing Gaze"]          = "Olhar Omnividente";
            t["Foresight"]                = "Presciência";
            t["Unblinking Eye"]           = "Olho Que Não Pestaneja";
            t["Burning Ground"]           = "Chão Ardente";
            t["Pyre"]                     = "Pira";
            t["Ashfall"]                  = "Chuva de Cinzas";
            t["Profane Strike"]           = "Golpe Profano";
            t["Unmake"]                   = "Desfazer";
            t["Undoing"]                  = "Perdição";
            t["Spawn Pyre"]               = "Invocar Pira";
            t["Wrathfire"]                = "Fogo da Ira";
            t["Final Hour"]               = "Hora Final";
            t["Active Power"]             = "Poder Ativo";
            t["Locked"]                   = "Bloqueado";

            // Canon names authored in SectLeverEffects.Alanthor.cs (Stoneveil,
            // Bulwark and Harvest the Veil already covered above).
            t["Scour the Registry"] = "Vasculhar o Registo";
            t["Heavy Bureaucracy"]  = "Burocracia Pesada";
            t["Sew Disorder"]       = "Semear a Desordem";
            t["Hands of Plenty"]    = "Mãos da Abundância";
            t["Raise Anew"]         = "Erguer de Novo";
            t["Second Wind"]        = "Segundo Fôlego";
            t["Immovable"]          = "Inamovível";
            t["Cleanse"]            = "Purificar";
            t["Veil-Touched"]       = "Tocados pelo Véu";

            // ── Canon active descriptions (SectLeverEffects.Alanthor.cs) ───
            // Antiquity — Scour the Registry / Heavy Bureaucracy / Sew Disorder
            t["Reveal a medium area for 15s."] = "Revela uma área média durante 15s.";
            t["Reveal a large area for 15s."]  = "Revela uma área grande durante 15s.";
            t["Reveal a large area for 35s."]  = "Revela uma área grande durante 35s.";
            t["One building stops training, research and resource output for 30s."] =
                "Um edifício deixa de treinar, investigar e produzir recursos durante 30s.";
            t["All buildings in a small area stop for 30s."] =
                "Todos os edifícios numa área pequena param durante 30s.";
            t["All buildings in a large area stop for 30s."] =
                "Todos os edifícios numa área grande param durante 30s.";
            t["Units in a small area turn hostile to all other units for 8s."] =
                "As unidades numa área pequena tornam-se hostis a todas as outras unidades durante 8s.";
            t["Units in a medium area turn hostile for 20s."] =
                "As unidades numa área média tornam-se hostis durante 20s.";
            t["Units in a large area turn hostile until killed."] =
                "As unidades numa área grande tornam-se hostis até serem mortas.";

            // Renewal — Hands of Plenty / Raise Anew / Second Wind
            t["Restore 30% HP to units and buildings in a small area."] =
                "Restaura 30% de HP a unidades e edifícios numa área pequena.";
            t["Restore 50% HP in a medium area."] =
                "Restaura 50% de HP numa área média.";
            t["Restore 80% HP in a medium area, and healing continues for 10s."] =
                "Restaura 80% de HP numa área média, e a cura continua durante 10s.";
            t["Raise one free Lv 1 Watch Tower. It crumbles after 30s."] =
                "Ergue uma Torre de Vigia Nv 1 gratuita. Desmorona-se após 30s.";
            t["Raise Lv 2 Watch Towers across a small area. They crumble after 60s."] =
                "Ergue Torres de Vigia Nv 2 por uma área pequena. Desmoronam-se após 60s.";
            t["Raise a permanent Lv 3 Watch Tower. It stays until destroyed."] =
                "Ergue uma Torre de Vigia Nv 3 permanente. Permanece até ser destruída.";
            t["Units in a small area cannot drop below 1 HP for 6s."] =
                "As unidades numa área pequena não podem descer abaixo de 1 HP durante 6s.";
            t["Units in a small area cannot drop below 1 HP for 12s."] =
                "As unidades numa área pequena não podem descer abaixo de 1 HP durante 12s.";
            t["Medium area, 12s; survivors heal 25% when it ends."] =
                "Área média, 12s; os sobreviventes curam 25% quando termina.";

            // Fortitude — Stoneveil / Bulwark / Immovable
            t["Veil a small area for 8s: invisible, untargetable, faster, but unable to act."] =
                "Vela uma área pequena durante 8s: invisíveis, impossíveis de alvejar, mais rápidas, mas incapazes de agir.";
            t["Veil a small area for 15s."] = "Vela uma área pequena durante 15s.";
            t["Veil a medium area for 15s; on expiry they gain +25% damage for 10s."] =
                "Vela uma área média durante 15s; ao terminar, ganham +25% de dano durante 10s.";
            t["One building gains +100% HP for 30s."] =
                "Um edifício ganha +100% de HP durante 30s.";
            t["Buildings in a small area gain +100% HP for 30s."] =
                "Os edifícios numa área pequena ganham +100% de HP durante 30s.";
            t["Buildings in a medium area gain +100% HP for 30s and reflect 20% of melee damage."] =
                "Os edifícios numa área média ganham +100% de HP durante 30s e refletem 20% do dano corpo a corpo.";
            t["Units in a small area gain +5 armor for 10s."] =
                "As unidades numa área pequena ganham +5 de armadura durante 10s.";
            t["Units in a medium area gain +8 armor for 15s."] =
                "As unidades numa área média ganham +8 de armadura durante 15s.";
            t["Units in a large area become invulnerable for 20s."] =
                "As unidades numa área grande ficam invulneráveis durante 20s.";

            // Reclamation — Harvest the Veil / Cleanse / Veil-Touched
            t["Target a resource node: 50 Supplies every 5s for 30s (300 total)."] =
                "Visa um nó de recursos: 50 Mantimentos a cada 5s durante 30s (300 no total).";
            t["Target a resource node: 75 Supplies + 20 Iron every 5s for 30s."] =
                "Visa um nó de recursos: 75 Mantimentos + 20 Ferro a cada 5s durante 30s.";
            t["Target a resource node: 150 Supplies + 60 Iron + 35 Veilstone + 5 Veilsteel every 5s for 30s."] =
                "Visa um nó de recursos: 150 Mantimentos + 60 Ferro + 35 Veilstone + 5 Veilsteel a cada 5s durante 30s.";
            t["Pump heavy player influence into a small area for 20s."] =
                "Injeta influência pesada do jogador numa área pequena durante 20s.";
            t["Pump heavy player influence into a medium area for 40s."] =
                "Injeta influência pesada do jogador numa área média durante 40s.";
            t["Pump heavy player influence into a large area for 40s; allies inside regenerate."] =
                "Injeta influência pesada do jogador numa área grande durante 40s; os aliados no interior regeneram.";
            t["Units in a small area take no curse damage for 15s."] =
                "As unidades numa área pequena não sofrem dano da maldição durante 15s.";
            t["Units in a medium area take no curse damage for 30s."] =
                "As unidades numa área média não sofrem dano da maldição durante 30s.";
            t["Large area, 30s, and they move 20% faster on cursed ground."] =
                "Área grande, 30s, e movem-se 20% mais depressa em terreno amaldiçoado.";

            // ── Unique buildings (canon four) ──────────────────────────────
            t["Reliquary — a vaulted archive. Every one standing shortens your sect-power cooldowns a little. Limit 5. Trains the Lorekeeper, researches Royal Index."] =
                "Relicário — um arquivo abobadado. Cada um de pé encurta ligeiramente as recargas dos teus poderes de seita. Limite 5. Treina o Guardião do Saber, investiga o Índice Real.";
            t["Mending Hall — an open-sided infirmary. Damaged units that walk inside heal over time. Limit 5. Trains the Scar Guard, researches Field Hospital."] =
                "Salão da Cura — uma enfermaria de lados abertos. As unidades danificadas que lá entram curam-se ao longo do tempo. Limite 5. Treina o Guarda das Cicatrizes, investiga o Hospital de Campanha.";
            t["Stonehold — a squat windowless blockhouse with the highest HP of any non-Hall structure; it blocks pathing like a wall. Limit 5. Trains the Stone Warden, researches Deep Foundations."] =
                "Bastião de Pedra — um fortim atarracado e sem janelas, com o HP mais alto de qualquer estrutura além do Hall; bloqueia a passagem como uma muralha. Limite 5. Treina o Guardião da Pedra, investiga as Fundações Profundas.";
            t["Veilworks — a smelter for cursed matter, and the only building that may be raised ON cursed ground. Limit 5. Trains the Golem Autark, researches Warden's Ledger."] =
                "Forja do Véu — uma fundição de matéria amaldiçoada, e o único edifício que pode ser erguido SOBRE terreno amaldiçoado. Limite 5. Treina o Golem Autarca, investiga o Livro-Razão do Guardião.";

            // ── Chapel aura composition (legacy eight) ─────────────────────
            t["Chapel of {0} — projects an aura within {1}m: "] =
                "Capela de {0} — projeta uma aura num raio de {1}m: ";
            t["+{0}% damage"]  = "+{0}% de dano";
            t["+{0} armor"]    = "+{0} de armadura";
            t["+{0}% speed"]   = "+{0}% de velocidade";
            t["{0}% reflect"]  = "{0}% de reflexão";
            t["{0} HP/s regen"] = "{0} HP/s de regeneração";
            t["a quiet sanctifying presence"] = "uma presença santificante serena";
            t[" to allied units."] = " às unidades aliadas.";

            // ── Unit lever composition ─────────────────────────────────────
            // Whole subject phrases: Portuguese articles must agree in gender
            // with the noun, which a "Your {0} gain" template cannot do.
            t["Your all units gain "]       = "Todas as tuas unidades ganham ";
            t["Your melee units gain "]     = "As tuas unidades corpo a corpo ganham ";
            t["Your ranged units gain "]    = "As tuas unidades à distância ganham ";
            t["Your siege units gain "]     = "As tuas unidades de cerco ganham ";
            t["Your miners / workers gain "] = "Os teus mineiros / trabalhadores ganham ";
            t["Your scouts gain "]          = "Os teus batedores ganham ";
            t["Your select units gain "]    = "Certas unidades tuas ganham ";
            t["+{0}% HP"]         = "+{0}% de HP";
            t["a minor blessing"] = "uma bênção menor";

            // ── Technology (upgrade-path) text ─────────────────────────────
            t["At the chapel you can spend RP + resources to upgrade Passive (P), Building aura (B), Unit bonus (U), and Active power (A) — each I → II → III, scaling effects to 1.5× and 2.0× of the listed Lv I numbers.  {0}"] =
                "Na capela podes gastar RP + recursos para melhorar o Passivo (P), a aura do Edifício (B), o bónus de Unidade (U) e o Poder ativo (A) — cada um I → II → III, escalando os efeitos para 1,5× e 2,0× dos números de Nv I indicados.  {0}";
            t["More names in the tally — stronger relics, sharper memory."] =
                "Mais nomes na contagem — relíquias mais fortes, memória mais afiada.";
            t["Deeper communion — walls knit faster, men return from death."] =
                "Comunhão mais profunda — as muralhas cicatrizam mais depressa, os homens regressam da morte.";
            t["Heavier hymns — stone grows thicker, melee endure longer."] =
                "Hinos mais pesados — a pedra torna-se mais espessa, o corpo a corpo resiste mais tempo.";
            t["Wider claim — your workers and tools shrug off Border-rot."] =
                "Domínio mais vasto — os teus trabalhadores e ferramentas resistem à podridão da Fronteira.";
            t["Longer vigil — archers strike harder while still."] =
                "Vigília mais longa — os arqueiros atacam com mais força enquanto imóveis.";
            t["Harsher verdict — sentence falls heavier, faster."] =
                "Veredicto mais severo — a sentença cai mais pesada, mais depressa.";
            t["Higher fervor — kills bless your army with deeper rage."] =
                "Fervor mais alto — as mortes abençoam o teu exército com uma raiva mais profunda.";
            t["Farther sight — scouts see the whole map's edges."] =
                "Visão mais distante — os batedores veem até às orlas do mapa.";
            t["Hotter forges — barracks pay less and pour out faster."] =
                "Forjas mais quentes — os quartéis pagam menos e produzem mais depressa.";
            t["Brighter pyres — corpses burn longer and wider."] =
                "Piras mais brilhantes — os cadáveres ardem mais tempo e mais longe.";
            t["Sharper hands — siege strips faction walls to dust."] =
                "Mãos mais afiadas — o cerco reduz as muralhas das fações a pó.";
            t["Deeper spite — the bleeding wound deals the killing blow."] =
                "Despeito mais fundo — a ferida que sangra desfere o golpe mortal.";
            t["Deeper devotion improves every lever this sect grants."] =
                "Uma devoção mais profunda melhora todas as alavancas que esta seita concede.";
        }
    }
}
