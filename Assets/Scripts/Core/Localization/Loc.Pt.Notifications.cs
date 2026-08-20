// Loc.Pt.Notifications.cs
// Portuguese for player notifications / toasts (PlayerNotificationSystem).
// Location: Assets/Scripts/Core/Localization/Loc.Pt.Notifications.cs

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
            t["Maximum 6 Halls per faction"] = "Máximo de 6 Salões por facção";
            t["Only one Temple of Ridan per faction"] = "Apenas um Templo de Ridan por facção";
            t["Already have a choice building"] = "Já tens um edifício de escolha";
            t["Must build inside your influence"] = "Tens de construir dentro da tua influência";
            t["War Totems must be planted on blood"] = "Os Totens de Guerra têm de ser erguidos sobre sangue";
            t["Mines must be built next to iron or veilstone"] = "As minas têm de ser construídas junto a ferro ou veilstone";
            t["Not enough resources"] = "Recursos insuficientes";
            t["Source hub no longer exists"] = "O bastião de origem já não existe";
            t["Those hubs are already connected"] = "Esses bastiões já estão ligados";

            // ---- Command routing (CommandRouter) ----
            t["The well resists all arms — only Feraldis may break it"] =
                "O poço resiste a todas as armas — apenas os Feraldis o podem quebrar";
            t["Requires Lv {0} {1}"] = "Requer {1} de Nv {0}";
            t["King Lexor already serves your realm"] = "O Rei Lexor já serve o teu reino";
            t["Your court already employs a Ledger"] = "A tua corte já emprega um Escrivão";
            t["Production queue full"] = "Fila de produção cheia";

            // ---- Shardroot (ShardrootSystem / GlowFlowSystem / TempleExplodeSystem) ----
            t["The SHARDROOT has been unearthed!"] = "O SHARDROOT foi desenterrado!";
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

            // ---- Temple of Ridan (TempleUpgradeSystem) ----
            t["Era {0} reached! +{1} Religion Points — adopted sects advanced to Lv {2}"] =
                "Era {0} alcançada! +{1} Pontos de Religião — seitas adotadas avançaram para Nv {2}";
        }
    }
}
