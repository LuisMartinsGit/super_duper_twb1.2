// Loc.Pt.Tutorial.cs
// Portuguese for the tutorial coach (TutorialDirector): chapter names, the
// twenty step titles and bodies, grant labels, the eyebrow/progress line,
// buttons and every tutorial notification.
//
// Body keys are the FULL concatenated English strings exactly as
// TutorialDirector builds them — the segment breaks below mirror the
// source file only for readability. Rich-text (<b>/<i>) and \n breaks are
// preserved verbatim in the values.

using System.Collections.Generic;

namespace TheWaningBorder.Core.Localization
{
    public static partial class Loc
    {
        private static void AddTutorial(Dictionary<string, string> t)
        {
            // ── Chapters ───────────────────────────────────────────────────
            t["1. Controls"] = "1. Controlos";
            t["2. Workers & resources"] = "2. Trabalhadores e recursos";
            t["3. Combat"] = "3. Combate";
            t["4. Culture"] = "4. Cultura";
            t["5. Religion"] = "5. Religião";
            t["6. The curse"] = "6. A maldição";
            t["7. The wells"] = "7. Os poços";

            // ── Eyebrow / progress line ────────────────────────────────────
            t["{0}   ·   {1} / {2} DONE   ·   ANY ORDER"] =
                "{0}   ·   {1} / {2} FEITOS   ·   QUALQUER ORDEM";
            t["   —   DONE"] = "   —   FEITO";

            // ── Buttons ────────────────────────────────────────────────────
            t["Skip this step"] = "Saltar este passo";
            t["Tick this one off and suggest the next. Steps can be done in any order — "
              + "skipping still pays out its resource package, so jumping ahead never "
              + "leaves you short."] =
                "Marca este passo como feito e sugere o seguinte. Os passos podem ser "
              + "feitos por qualquer ordem — saltar continua a pagar o seu pacote de "
              + "recursos, por isso avançar nunca te deixa sem meios.";
            t["End tutorial"] = "Terminar tutorial";
            t["Dismiss the coach. The match carries on as a normal skirmish."] =
                "Dispensa o instrutor. A partida continua como uma escaramuça normal.";

            // ── Notifications ──────────────────────────────────────────────
            t["Tutorial: {0} — done"] = "Tutorial: {0} — feito";
            t["Tutorial: {0} — done (ahead of the coach)"] =
                "Tutorial: {0} — feito (adiantado ao instrutor)";
            t["Tutorial: granted {0}."] = "Tutorial: recebeste {0}.";
            t["Tutorial complete — the match continues as a normal skirmish."] =
                "Tutorial concluído — a partida continua como uma escaramuça normal.";
            t["Tutorial: no Hall found — skip this step to continue."] =
                "Tutorial: nenhum Salão encontrado — salta este passo para continuar.";
            t["Tutorial: the curse is not active on this map — skip this step."] =
                "Tutorial: a maldição não está ativa neste mapa — salta este passo.";
            t["A ritual has failed — the curse is waking east of your Hall!"] =
                "Um ritual falhou — a maldição está a despertar a leste do teu Salão!";
            t["Tutorial: this upgrade will carry the Temple to level {0}."] =
                "Tutorial: esta melhoria vai levar o Templo ao nível {0}.";

            // ── Grant labels ───────────────────────────────────────────────
            t["a building fund"] = "um fundo de construção";
            t["a survey fund"] = "um fundo de prospeção";
            t["an army budget"] = "um orçamento para o exército";
            t["enough for a special building"] = "o suficiente para um edifício especial";
            t["the age-up cost"] = "o custo do avanço de Idade";
            t["Temple materials"] = "materiais para o Templo";
            t["Temple upgrade stone"] = "pedra para a melhoria do Templo";
            t["chapel materials"] = "materiais para a capela";
            t["a Scholar's stipend"] = "a bolsa de um Erudito";
            t["a campaign chest"] = "uma arca de campanha";

            // ── 1. Controls ────────────────────────────────────────────────
            t["Look around"] = "Olha à tua volta";
            t["Push the mouse to any <b>screen edge</b> to pan, or use the "
              + "<b>arrow keys</b>. Hold the <b>middle mouse button</b> to drag the "
              + "view, or click the minimap to jump.\n"
              + "Find your <b>Hall</b> — the big building your warband starts around."] =
                "Empurra o rato contra qualquer <b>borda do ecrã</b> para deslocar a "
              + "vista, ou usa as <b>setas do teclado</b>. Mantém premido o <b>botão "
              + "do meio do rato</b> para arrastar a vista, ou clica no minimapa para "
              + "saltar.\n"
              + "Encontra o teu <b>Salão</b> — o grande edifício em redor do qual o "
              + "teu bando de guerra começa.";

            t["Zoom"] = "Zoom";
            t["<b>Scroll wheel</b> zooms in and out.\n"
              + "Pull back to read a fight, push in to see what a building is doing."] =
                "A <b>roda do rato</b> aproxima e afasta a vista.\n"
              + "Afasta para leres uma batalha, aproxima para veres o que um edifício "
              + "está a fazer.";

            // ── 2. Workers and resources ───────────────────────────────────
            t["Select and move"] = "Seleciona e move";
            t["<b>Left-click</b> a single Worker to select it. <b>Right-click</b> "
              + "the ground to send it there.\n"
              + "Its stats appear bottom-left; what it can do appears beside them."] =
                "<b>Clica com o botão esquerdo</b> num único Trabalhador para o "
              + "selecionares. <b>Clica com o botão direito</b> no chão para o "
              + "enviares para lá.\n"
              + "As suas estatísticas aparecem em baixo à esquerda; o que ele pode "
              + "fazer aparece ao lado.";

            t["Mine veilstone"] = "Extrai veilstone";
            t["Right-click a <b>veilstone outcropping</b> with a worker selected.\n"
              + "Mined resources go straight to your bank — no hauling, no drop-off "
              + "building.\n<b>Veilstone is the one resource the curse controls.</b> "
              + "The patch by your base is what the world had spare; everything after "
              + "it has to be taken."] =
                "Clica com o botão direito num <b>afloramento de veilstone</b> com um "
              + "trabalhador selecionado.\n"
              + "Os recursos extraídos vão diretamente para o teu banco — sem "
              + "transporte, sem edifício de depósito.\n<b>O veilstone é o único "
              + "recurso que a maldição controla.</b> A jazida junto à tua base é o "
              + "que o mundo tinha de sobra; tudo o que vem depois tem de ser tomado.";

            t["Box-select and build"] = "Seleciona em caixa e constrói";
            t["<b>Drag a box</b> over two or more Workers, then pick <b>Hut</b> from "
              + "the actions panel and left-click the ground.\n"
              + "Huts raise your population cap. Hold <b>Shift</b> while placing to "
              + "keep going."] =
                "<b>Arrasta uma caixa</b> sobre dois ou mais Trabalhadores, depois "
              + "escolhe <b>Cabana</b> no painel de ações e clica com o botão "
              + "esquerdo no chão.\n"
              + "As Cabanas aumentam o teu limite de população. Mantém o <b>Shift</b> "
              + "premido enquanto colocas para continuares.";

            t["Train more workers"] = "Treina mais trabalhadores";
            t["Select your <b>Hall</b> and click <b>Worker</b> in the actions panel.\n"
              + "The queue strip above the panel shows what is in production — "
              + "<b>right-click a queued chip</b> to cancel it and get the cost back."] =
                "Seleciona o teu <b>Salão</b> e clica em <b>Trabalhador</b> no painel "
              + "de ações.\n"
              + "A faixa de fila acima do painel mostra o que está em produção — "
              + "<b>clica com o botão direito num item em fila</b> para o cancelar e "
              + "recuperares o custo.";

            t["Split your economy"] = "Divide a tua economia";
            t["Put <b>three workers on veilstone and three on iron</b>.\n"
              + "They feed different things: iron buys soldiers and buildings, "
              + "veilstone buys everything the Temple and the sects need."] =
                "Põe <b>três trabalhadores no veilstone e três no ferro</b>.\n"
              + "Alimentam coisas diferentes: o ferro paga soldados e edifícios, o "
              + "veilstone paga tudo aquilo de que o Templo e as seitas precisam.";

            t["Place a good Gatherer's Hut"] = "Coloca uma boa Cabana do Recoletor";
            t["A Gatherer's Hut earns from the open ground inside its circle. While "
              + "placing it, the preview shows a <b>yield percentage</b> — blocked "
              + "ground, other huts' circles and the map edge all eat into it.\n"
              + "Find a spot reading <b>90% or better</b>."] =
                "Uma Cabana do Recoletor rende a partir do terreno aberto dentro do "
              + "seu círculo. Enquanto a colocas, a pré-visualização mostra uma "
              + "<b>percentagem de rendimento</b> — terreno bloqueado, os círculos de "
              + "outras cabanas e a borda do mapa reduzem-na.\n"
              + "Encontra um sítio que marque <b>90% ou melhor</b>.";

            // ── 3. Combat ──────────────────────────────────────────────────
            t["Raise a Barracks"] = "Ergue um Quartel";
            t["Build a <b>Barracks</b>, then train <b>three Spearmen</b>.\n"
              + "Units counter each other — select one and read the <b>Bonus vs</b> "
              + "line in its stats."] =
                "Constrói um <b>Quartel</b> e depois treina <b>três Lanceiros</b>.\n"
              + "As unidades contrariam-se umas às outras — seleciona uma e lê a "
              + "linha <b>Bónus vs</b> nas suas estatísticas.";

            t["Take the fight out"] = "Leva a luta ao inimigo";
            t["<b>Box-select</b> your soldiers and <b>right-click an enemy</b> to "
              + "attack.\nPress <b>A</b> then click the ground to attack-move — they "
              + "will engage anything they meet on the way."] =
                "<b>Seleciona em caixa</b> os teus soldados e <b>clica com o botão "
              + "direito num inimigo</b> para atacar.\nPrime <b>A</b> e depois clica "
              + "no chão para um ataque em marcha — eles atacarão tudo o que "
              + "encontrarem pelo caminho.";

            // ── 4. Culture ─────────────────────────────────────────────────
            t["Choose your special building"] = "Escolhe o teu edifício especial";
            t["Pick one of <b>Shrine of Ahridan</b>, <b>Vault of Almiérra</b> or "
              + "<b>Fiendstone Keep</b> from the top of the screen and place it. "
              + "Hover each for what it does.\n"
              + "This choice is final for the match, and it is what unlocks your "
              + "culture."] =
                "Escolhe entre <b>Santuário de Ahridan</b>, <b>Cofre de Almiérra</b> "
              + "ou <b>Torreão de Fiendstone</b> no topo do ecrã e coloca-o. Passa o "
              + "rato sobre cada um para veres o que faz.\n"
              + "Esta escolha é definitiva para a partida, e é ela que desbloqueia a "
              + "tua cultura.";

            t["Age up"] = "Avança de Idade";
            t["When it finishes, <b>SELECT CULTURE</b> appears at the top. Click it "
              + "and commit.\nThat ends Age 0 and opens your culture's units, "
              + "buildings and upgrades — and your <b>verb</b>, which is how the "
              + "match is won."] =
                "Quando terminar, <b>SELECIONAR CULTURA</b> aparece no topo. Clica e "
              + "confirma.\nIsso encerra a Idade 0 e abre as unidades, os edifícios e "
              + "as melhorias da tua cultura — e o teu <b>verbo</b>, que é como se "
              + "vence a partida.";

            t["Raise the Temple of Ridan"] = "Ergue o Templo de Ridan";
            t["Place the <b>Temple of Ridan</b>.\n"
              + "It holds your chapel slots and every sect you will ever adopt."] =
                "Coloca o <b>Templo de Ridan</b>.\n"
              + "Guarda os teus lugares de capela e todas as seitas que alguma vez "
              + "vieres a adotar.";

            t["Upgrade the Temple"] = "Melhora o Templo";
            t["Select the Temple and start its upgrade.\n"
              + "Each level raises your <b>era</b>, which pays <b>Religion Points</b> "
              + "and advances every sect you have adopted.\n"
              + "<i>Tutorial shortcut: this one upgrade carries it to level 4 — the "
              + "top — so the next chapters have everything they need.</i>"] =
                "Seleciona o Templo e inicia a sua melhoria.\n"
              + "Cada nível sobe a tua <b>era</b>, o que paga <b>Pontos de "
              + "Religião</b> e avança todas as seitas que adotaste.\n"
              + "<i>Atalho do tutorial: esta única melhoria leva-o ao nível 4 — o "
              + "topo — para que os próximos capítulos tenham tudo o que precisam.</i>";

            // ── 5. Religion ────────────────────────────────────────────────
            t["Adopt a sect"] = "Adotar uma seita";
            t["The <b>religion panel</b> on the right shows your chapel slots and "
              + "your <b>Religion Points</b>.\n"
              + "RP is not income. You get a fixed amount per era — <b>6, then 8, "
              + "then 10</b> — plus <b>1</b> for a Shrine, and anything unspent "
              + "carries to the next era at <b>two to one</b>. There is no way to "
              + "farm more.\nSo the sects you choose <i>are</i> your build. Click a "
              + "slot, read the roster on hover, and commit."] =
                "O <b>painel de religião</b> à direita mostra os teus lugares de "
              + "capela e os teus <b>Pontos de Religião</b>.\n"
              + "RP não é rendimento. Recebes uma quantia fixa por era — <b>6, depois "
              + "8, depois 10</b> — mais <b>1</b> por um Santuário, e o que ficar por "
              + "gastar transita para a era seguinte a <b>dois por um</b>. Não há "
              + "forma de acumular mais.\nPor isso, as seitas que escolhes <i>são</i> "
              + "a tua estratégia. Clica num lugar, lê o elenco ao passar o rato e "
              + "confirma.";

            t["Cast a sect power"] = "Lança um poder de seita";
            t["Your sect's slot now carries four cells: <b>P</b> is its always-on "
              + "passive, <b>1 2 3</b> are its actives, unlocked by Temple level.\n"
              + "Hover each for what it does, then click a lit one and pick a target "
              + "on the map."] =
                "O lugar da tua seita mostra agora quatro células: <b>P</b> é o seu "
              + "passivo sempre ativo, <b>1 2 3</b> são os seus ativos, desbloqueados "
              + "pelo nível do Templo.\n"
              + "Passa o rato sobre cada um para veres o que faz, depois clica num "
              + "que esteja aceso e escolhe um alvo no mapa.";

            // ── 6. The curse ───────────────────────────────────────────────
            t["The curse wakes"] = "A maldição desperta";
            t["<b>A ritual has failed somewhere on the map.</b> A channeler began "
              + "their rite and died before finishing it, and the curse has awakened "
              + "as a consequence.\n"
              + "A veilstone node near you is <b>corrupting</b>. In a few seconds a "
              + "<b>Curse Node</b> rises there and hazes the whole patch. Watch the "
              + "purple spread.\n"
              + "This is also what happens when a patch runs dry: <b>the last node of "
              + "any patch always corrupts.</b> Your home patch is safe — your Hall "
              + "projects a suppression ring, and the curse can never wake inside "
              + "your influence. It is the patches you have to leave home for that "
              + "bite."] =
                "<b>Um ritual falhou algures no mapa.</b> Um canalizador começou o "
              + "seu rito e morreu antes de o terminar, e a maldição despertou em "
              + "consequência.\n"
              + "Um nó de veilstone perto de ti está a <b>corromper-se</b>. Dentro de "
              + "alguns segundos, um <b>Nó da Maldição</b> ergue-se ali e envolve a "
              + "jazida inteira em bruma. Observa o roxo a alastrar.\n"
              + "É também isto que acontece quando uma jazida se esgota: <b>o último "
              + "nó de qualquer jazida corrompe-se sempre.</b> A tua jazida de origem "
              + "está segura — o teu Salão projeta um anel de supressão, e a maldição "
              + "nunca pode despertar dentro da tua influência. São as jazidas que te "
              + "obrigam a sair de casa que mordem.";

            t["Break the Curse Node"] = "Quebra o Nó da Maldição";
            t["Bring your army. It has <b>1800 HP</b> and is built to resist a "
              + "starting force — this is a real commitment.\n"
              + "Kill it and the pocket <b>shatters</b>: the ground clears and it pays "
              + "out <b>five veilstone nodes</b>. You get the patch back and a bonus.\n"
              + "Leave it and it keeps feeding — the haze taxes anyone mining there, "
              + "and crusted ground costs you: a few seconds' grace, then damage that "
              + "scales with depth, plus slower movement and worse stats.\n"
              + "The other way is to <b>starve</b> it. Push influence over it — a "
              + "tower, or an upgraded building, since every level widens a "
              + "building's influence — and it dies on its own."] =
                "Traz o teu exército. Tem <b>1800 PV</b> e foi feito para resistir a "
              + "uma força inicial — isto é um compromisso a sério.\n"
              + "Mata-o e o foco <b>estilhaça-se</b>: o terreno limpa-se e ele paga "
              + "<b>cinco nós de veilstone</b>. Recuperas a jazida e ainda ganhas um "
              + "bónus.\n"
              + "Se o deixares, continua a alimentar-se — a bruma taxa quem ali "
              + "extrair, e o chão encrostado custa-te caro: alguns segundos de "
              + "tolerância, depois dano que aumenta com a profundidade, além de "
              + "movimento mais lento e piores estatísticas.\n"
              + "A outra forma é <b>esfomeá-lo</b>. Empurra influência sobre ele — "
              + "uma torre, ou um edifício melhorado, já que cada nível alarga a "
              + "influência de um edifício — e ele morre sozinho.";

            // ── 7. The wells ───────────────────────────────────────────────
            t["Train a Holy Scholar"] = "Treina um Erudito Sagrado";
            t["The giant veilstone formations are the <b>wells</b> — selecting one "
              + "reads <i>Veilstone Hive</i>. They are the largest income on the map "
              + "and the only way the match is won.\n"
              + "Every well is <b>dormant</b> until a player reaches for it. That is "
              + "why the map was quiet.\n"
              + "Claiming one needs a ritualist. Alanthor's is the <b>Holy Scholar</b>, "
              + "trained at the <b>Temple of Ridan at level 3 or higher</b> — yours is "
              + "at 4. It has 90 HP and no answer to anything: a key, not a soldier."] =
                "As formações gigantes de veilstone são os <b>poços</b> — ao "
              + "selecionares uma, lê-se <i>Colmeia de Veilstone</i>. São o maior "
              + "rendimento do mapa e a única forma de vencer a partida.\n"
              + "Cada poço está <b>adormecido</b> até um jogador o tentar alcançar. É "
              + "por isso que o mapa estava calmo.\n"
              + "Reclamar um exige um ritualista. O de Alanthor é o <b>Erudito "
              + "Sagrado</b>, treinado no <b>Templo de Ridan de nível 3 ou "
              + "superior</b> — o teu está no 4. Tem 90 PV e não tem resposta para "
              + "nada: uma chave, não um soldado.";

            t["Purify a well"] = "Purifica um poço";
            t["Send the Scholar to a well <b>with your army around it</b> and begin "
              + "the rite.\nTwo things happen the instant the channel starts, and "
              + "neither can be undone:\n"
              + "<b>The well wakes, permanently.</b> It begins feeding the curse and "
              + "never sleeps again — and every player is told who woke it. Waking one "
              + "on a rival's doorstep costs them ground whether you finish or not.\n"
              + "<b>You are committed.</b> Break the channel — Scholar killed, dragged "
              + "off, interrupted — and the well answers with the <b>Backlash</b>: "
              + "five escalating waves of crystal creatures that keep coming whether "
              + "you stay or run. That is the failed ritual you were told about.\n"
              + "Each culture has one verb: Alanthor <b>purifies</b>, Runai "
              + "<b>pacifies</b>, Feraldis <b>destroys</b>. Hold every well in your "
              + "verb-state at once and you win."] =
                "Envia o Erudito para um poço <b>com o teu exército à volta dele</b> "
              + "e começa o rito.\nDuas coisas acontecem no instante em que a "
              + "canalização começa, e nenhuma pode ser desfeita:\n"
              + "<b>O poço acorda, permanentemente.</b> Começa a alimentar a maldição "
              + "e nunca mais adormece — e todos os jogadores ficam a saber quem o "
              + "acordou. Acordar um à porta de um rival custa-lhe terreno, quer "
              + "termines quer não.\n"
              + "<b>Ficas comprometido.</b> Quebra a canalização — Erudito morto, "
              + "arrastado para longe, interrompido — e o poço responde com o "
              + "<b>Contragolpe</b>: cinco vagas crescentes de criaturas de cristal "
              + "que continuam a chegar, quer fiques quer fujas. Esse é o ritual "
              + "falhado de que te falaram.\n"
              + "Cada cultura tem um verbo: Alanthor <b>purifica</b>, Runai "
              + "<b>pacifica</b>, Feraldis <b>destrói</b>. Mantém todos os poços no "
              + "estado do teu verbo ao mesmo tempo e vences.";
        }
    }
}
