// Loc.Pt.Notifications.cs
// Portuguese for player notifications / toasts (PlayerNotificationSystem).

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddNotifications(Dictionary<string, string> t)
        {
            // ---- Building placement (BuildCommandPannel) ----
            t["Invalid placement"] = "Colocação inválida";
            t["Maximum 10 Trading Posts"] = "Máximo de 10 Postos Comerciais";
            t["Only one Temple of Ridan per faction"] = "Apenas um Templo de Ridan por facção";
            t["Limit reached: {0} per faction"] = "Limite atingido: {0} por facção";
            t["Already have a choice building"] = "Já tens um edifício de escolha";
            t["{0} - Lvl {1}"] = "{0} - Nív {1}";
            t["Must build inside your influence"] = "Tens de construir dentro da tua influência";
            t["You can only build in your own territory"] = "Só podes construir no teu próprio território";
            t["Gatherer's Huts must be built on a free supply node"] = "As Cabanas do Recoletor têm de ser construídas num nó de mantimentos livre";
            t["This territory already has a Hall"] = "Este território já tem um Salão";
            t["Cannot claim ground another player holds"] = "Não podes reclamar terreno que outro jogador detém";
            t["War Totems must be planted on blood"] = "Os Totens de Guerra têm de ser erguidos sobre sangue";
            t["Mines must be built on a free iron deposit"] = "As minas têm de ser construídas sobre um depósito de ferro livre";
            t["Veilstone Mines must be built on a free veilstone outcropping"] = "As Minas de Veilstone têm de ser construídas sobre um afloramento de veilstone livre";
            t["This building must stand on a free resource node"] = "Este edifício tem de assentar sobre um nó de recurso livre";
            t["Sawyers must be built against a forest"] = "As Serrações têm de ser construídas junto a uma floresta";
            t["Not enough resources"] = "Recursos insuficientes";
            t["Source hub no longer exists"] = "O bastião de origem já não existe";
            t["Those hubs are already connected"] = "Esses bastiões já estão ligados";
            // Placement refusal reasons (TerritoryOwnership.PlacementRefusalText)
            t["That building cannot be placed"] = "Esse edifício não pode ser colocado";
            t["The ground here is unsuitable"] = "O terreno aqui não é adequado";
            t["Cannot build on a resource node — only its own extractor may stand there"] = "Não é possível construir sobre um recurso — só o seu próprio extrator pode lá estar";
            t["Alanthor do not mine veilstone — raise a Trading Outpost on it"] = "Os Alanthor não mineram veilstone — ergue um Entreposto Comercial sobre ele";
            t["Nothing — build on it"] = "Nada — constrói aqui";
            t["/min"] = "/min";
            t["This veilstone outcrop is cursed or mined out"] = "Este afloramento de veilstone está amaldiçoado ou esgotado";
            t["Trading Outposts must stand on an uncursed veilstone outcrop"] = "Os Entrepostos Comerciais têm de ficar sobre um afloramento de veilstone não amaldiçoado";
            t["Trading Outposts must be built on a free, uncursed veilstone outcrop"] = "Os Entrepostos Comerciais têm de ser construídos sobre um afloramento de veilstone livre e não amaldiçoado";
            t["Mines must be built on a free iron or veilstone node"] = "As Minas têm de ser construídas num recurso livre de ferro ou veilstone";
            t["Veilstone Mines must be built on a free, uncursed veilstone outcropping"] = "As Minas de Veilstone têm de ser construídas num afloramento livre e não amaldiçoado";
            t["Something is already built here"] = "Já existe algo construído aqui";
            t["Cannot build on cursed ground"] = "Não podes construir em terreno amaldiçoado";
            t["The curse holds this territory"] = "A maldição domina este território";
            t["A Hall must border a territory you hold"] = "Um Salão tem de fazer fronteira com um território que detenhas";
            t["Worker too far from the Hall site"] = "Trabalhador demasiado longe do local do Salão";
            t["Select a worker to place a Hall"] = "Seleciona um trabalhador para colocar um Salão";
            t["The worker must stand inside the territory the Hall will claim"] = "O trabalhador tem de estar dentro do território que o Salão vai reclamar";
            t["You have the most of that building you may hold"] = "Já tens o máximo permitido desse edifício";

            // ---- Command routing (CommandRouter) ----
            t["The well is sealed — an Iconoclast must crack it open first"] =
                "O poço está selado — um Iconoclasta tem de o abrir primeiro";
            t["The well resists all arms — only Feraldis may break it"] =
                "O poço resiste a todas as armas — apenas os Feraldis o podem quebrar";
            t["Requires Lv {0} {1}"] = "Requer {1} de Nv {0}";
            t["King Lexor already serves your realm"] = "O Rei Lexor já serve o teu reino";
            t["Your court already employs a Ledger"] = "A tua corte já emprega um Escrivão";
            t["Production queue full"] = "Fila de produção cheia";
            // The Wall Rule refusal (WorldClickInput) — docs/Design/Combat_Pacing.md
            t["Only siege can damage walls"] = "Apenas máquinas de cerco podem danificar muralhas";
            // Directed building fire refused by every selected building (WorldClickInput)
            t["Cannot fire on that target"] = "Não é possível disparar sobre esse alvo";

            // ---- Wall drawing (BuildCommandPannel wall-draw release) ----
            t["Wall is too short to close into a loop"] = "A muralha é demasiado curta para fechar em anel";
            t["Wall bends too sharply"] = "A muralha curva demasiado";
            t["Wall runs back over itself"] = "A muralha cruza-se a si própria";
            t["Wall crosses ground you cannot build on"] = "A muralha atravessa terreno onde não podes construir";

            // ---- Shardroot (ShardrootSystem / ShardrootCarrySystem / TempleExplodeSystem) ----
            t["The SHARDROOT has been unearthed!"] = "O SHARDROOT foi desenterrado!";
            t["A SHARDROOT sleeps beneath one of the wells. The first to work that well claims it."] =
                "Um SHARDROOT dorme sob um dos poços. O primeiro a trabalhar esse poço reclama-o.";
            t["{0} has awakened the SHARDBOUND HERO!"] = "{0} despertou o HERÓI SHARDBOUND!";
            t["{0} carries the SHARDROOT!"] = "{0} transporta o SHARDROOT!";
            t["{0} has ENSHRINED the Shardroot — their powers surge!"] =
                "{0} CONSAGROU o Shardroot — os seus poderes aumentam!";
            t["The SHARDROOT has fallen — claim it!"] = "O SHARDROOT caiu — reclama-o!";
            t["The Temple falls — the SHARDROOT lies in the crater!"] =
                "O Templo cai — o SHARDROOT jaz na cratera!";

            // ---- Curse / wells (Border systems) ----
            t["The rite collapses — the well erupts!"] = "O ritual colapsa — o poço entra em erupção!";
            t["Backlash — wave {0} of {1}!"] = "Retaliação — vaga {0} de {1}!";
            t["A well stirs — {0} has disturbed it!"] = "Um poço agita-se — {0} perturbou-o!";
            t["Blood pool contaminating — {0} curse unit(s) will rise at ({1:0},{2:0}) in {3}s!"] =
                "Poça de sangue em contaminação — {0} unidade(s) da maldição vão erguer-se em ({1:0},{2:0}) dentro de {3}s!";
            t["A veilstone node is corrupting — the curse rises in {0}s!"] =
                "Um nódulo de veilstone está a corromper-se — a maldição ergue-se dentro de {0}s!";
            t["A Corruptor is defiling a well!"] = "Um Corruptor está a profanar um poço!";
            t["A well lies open — {0}s to break it!"] = "Um poço está exposto — {0}s para o quebrar!";
            t["The well seals itself — the corruption failed."] = "O poço sela-se — a corrupção falhou.";
            t["{0} holds all but ONE well — stop them!"] =
                "{0} controla todos os poços menos UM — trava-os!";

            // ---- Feraldis (WarTotemAuraSystem) ----
            t["A War Totem crumbles — its blood is spent."] =
                "Um Totem de Guerra desmorona-se — o seu sangue esgotou-se.";
        }
    }
}
