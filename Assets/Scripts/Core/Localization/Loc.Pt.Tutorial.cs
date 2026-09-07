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
            t["2. Territory & economy"] = "2. Território e economia";
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
            t["A ritual has failed — the curse is waking east of your Fortress!"] =
                "Um ritual falhou — a maldição está a despertar a leste da tua Fortaleza!";
            t["Tutorial: this upgrade will carry the Temple to level {0}."] =
                "Tutorial: esta melhoria vai levar o Templo ao nível {0}.";

            // ── Grant labels ───────────────────────────────────────────────
            t["a building fund"] = "um fundo de construção";
            t["a survey fund"] = "um fundo de prospeção";
            t["a claim pot"] = "um fundo de reivindicação";
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
              + "Find your <b>Fortress</b> — the capital your warband starts around. "
              + "It holds the ground it stands on, and that ground is your first "
              + "<b>territory</b>."] =
                "Empurra o rato contra qualquer <b>borda do ecrã</b> para deslocar a "
              + "vista, ou usa as <b>setas do teclado</b>. Mantém premido o <b>botão "
              + "do meio do rato</b> para arrastar a vista, ou clica no minimapa para "
              + "saltar.\n"
              + "Encontra a tua <b>Fortaleza</b> — a capital em redor da qual o teu "
              + "bando de guerra começa. Ela detém o terreno onde assenta, e esse "
              + "terreno é o teu primeiro <b>território</b>.";

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

            t["Your ground is already paying"] = "O teu terreno já está a pagar";
            t["<b>Nobody gathers anything.</b> Income comes from the ground you "
              + "hold: every territory pays you per minute, and every resource "
              + "<b>node</b> standing in it pays more — with nothing built on it "
              + "and nobody working it.\n"
              + "Your home territory holds a <b>veilstone outcropping</b>, so that "
              + "veilstone is arriving in your bank right now, just for holding the "
              + "ground. Watch the counter.\n"
              + "<b>An extractor multiplies a node, it does not unlock one.</b> In "
              + "this age the <b>Gatherer's Hut</b> is the only one you can raise — "
              + "the rest arrive with your culture. So for now the way to earn more "
              + "is to hold more ground."] =
                "<b>Ninguém recolhe nada.</b> O rendimento vem do terreno que "
              + "possuis: cada território paga-te por minuto, e cada <b>nó</b> de "
              + "recurso nele paga mais — sem nada construído em cima e sem "
              + "ninguém a trabalhá-lo.\n"
              + "O teu território de origem tem um <b>afloramento de veilstone</b>, "
              + "por isso esse veilstone está a entrar no teu banco neste momento, "
              + "só por deteres o terreno. Repara no contador.\n"
              + "<b>Um extrator multiplica um nó, não o desbloqueia.</b> Nesta "
              + "idade a <b>Cabana do Recoletor</b> é a única que podes erguer — as "
              + "restantes chegam com a tua cultura. Por isso, para já, a maneira de "
              + "ganhar mais é deter mais terreno.";

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
            t["Select your <b>Fortress</b> and click <b>Worker</b> in the actions "
              + "panel.\n"
              + "The queue strip above the panel shows what is in production — "
              + "<b>right-click a queued chip</b> to cancel it and get the cost back."] =
                "Seleciona a tua <b>Fortaleza</b> e clica em <b>Trabalhador</b> no "
              + "painel de ações.\n"
              + "A faixa de fila acima do painel mostra o que está em produção — "
              + "<b>clica com o botão direito num item em fila</b> para o cancelar e "
              + "recuperares o custo.";

            t["Claim a second territory"] = "Reivindica um segundo território";
            t["You may only build inside ground you already hold — with one "
              + "exception, and it is the whole game: the <b>Hall</b>.\n"
              + "A Hall is the only building you can raise on unclaimed ground, and "
              + "raising it <b>claims that territory</b>. Pick Hall, place it in a "
              + "neighbouring region, and the ground becomes yours.\n"
              + "It costs <b>450 supplies and 450 iron</b> — the largest purchase in "
              + "the game, because it is the only one that makes your economy bigger. "
              + "<b>One Hall per territory</b>, and a claim <b>dies with its Hall</b>: "
              + "kill the building, the ground goes back to unclaimed."] =
                "Só podes construir dentro de terreno que já possuis — com uma "
              + "exceção, e é ela o jogo inteiro: o <b>Salão</b>.\n"
              + "O Salão é o único edifício que podes erguer em terreno não "
              + "reivindicado, e erguê-lo <b>reivindica esse território</b>. Escolhe "
              + "Salão, coloca-o numa região vizinha, e o terreno passa a ser teu.\n"
              + "Custa <b>450 mantimentos e 450 ferro</b> — a maior compra do jogo, "
              + "porque é a única que torna a tua economia maior. <b>Um Salão por "
              + "território</b>, e uma reivindicação <b>morre com o seu Salão</b>: "
              + "destrói o edifício e o terreno volta a não estar reivindicado.";

            t["Work your supply nodes"] = "Trabalha os teus nós de mantimentos";
            t["A <b>Gatherer's Hut</b> must stand <b>on a supply node</b>, and each "
              + "node takes one. So a territory's supply-node count IS its hut cap — "
              + "an ordinary territory has <b>two</b>, and a home like yours has "
              + "<b>four</b>.\n"
              + "Build <b>three</b>. Each one roughly triples what its node pays."] =
                "Uma <b>Cabana do Recoletor</b> tem de assentar <b>sobre um nó de "
              + "mantimentos</b>, e cada nó aceita uma. Por isso o número de nós de "
              + "mantimentos de um território É o seu limite de cabanas — um "
              + "território comum tem <b>dois</b>, e uma origem como a tua tem "
              + "<b>quatro</b>.\n"
              + "Constrói <b>três</b>. Cada uma triplica aproximadamente o que o seu "
              + "nó paga.";

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
            t["Pick one of <b>Shrine of Ridan</b>, <b>Vault of Almiérra</b> or "
              + "<b>Fiendstone Keep</b> from the top of the screen and place it. "
              + "Hover each for what it does.\n"
              + "This choice is final for the match, and it is what unlocks your "
              + "culture."] =
                "Escolhe entre <b>Santuário de Ridan</b>, <b>Cofre de Almiérra</b> "
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
              + "This is also what <b>holding ground</b> costs. Keep a veilstone "
              + "territory that is not your home for <b>two minutes</b> and its "
              + "pocket wakes. Your home is exempt — your Fortress projects a "
              + "suppression ring, and the curse can never wake inside your "
              + "influence. It is the ground you had to leave home for that bites."] =
                "<b>Um ritual falhou algures no mapa.</b> Um canalizador começou o "
              + "seu rito e morreu antes de o terminar, e a maldição despertou em "
              + "consequência.\n"
              + "Um nó de veilstone perto de ti está a <b>corromper-se</b>. Dentro de "
              + "alguns segundos, um <b>Nó da Maldição</b> ergue-se ali e envolve a "
              + "jazida inteira em bruma. Observa o roxo a alastrar.\n"
              + "É também isto que custa <b>deter terreno</b>. Mantém um território "
              + "de veilstone que não seja o teu de origem durante <b>dois "
              + "minutos</b> e a sua bolsa desperta. A tua origem está isenta — a tua "
              + "Fortaleza projeta um anel de supressão, e a maldição nunca pode "
              + "despertar dentro da tua influência. É o terreno que te obrigou a "
              + "sair de casa que morde.";

            t["Break the Curse Node"] = "Quebra o Nó da Maldição";
            t["Bring your army. It has <b>1800 HP</b> and is built to resist a "
              + "starting force — this is a real commitment.\n"
              + "Kill it and the pocket <b>shatters</b>: the ground clears and it pays "
              + "out <b>five veilstone nodes</b>. You get the patch back and a bonus.\n"
              + "Leave it and it keeps feeding, and the crust it lays down denies you "
              + "the ground: a few seconds' grace, then damage that scales with "
              + "depth, plus slower movement and worse stats.\n"
              + "The other way is to <b>starve</b> it. A pocket cannot live on ground "
              + "somebody holds — <b>claim the territory</b> and it dies on its own. "
              + "That is the same rule twice: taking ground is how you grow, and it "
              + "is also how you clean."] =
                "Traz o teu exército. Tem <b>1800 PV</b> e foi feito para resistir a "
              + "uma força inicial — isto é um compromisso a sério.\n"
              + "Mata-o e a bolsa <b>estilhaça-se</b>: o terreno limpa-se e paga "
              + "<b>cinco nós de veilstone</b>. Recuperas a jazida e ainda levas um "
              + "bónus.\n"
              + "Deixa-o e continua a alimentar-se, e a crosta que assenta nega-te o "
              + "terreno: alguns segundos de tolerância, depois dano que aumenta com "
              + "a profundidade, mais lentidão e piores atributos.\n"
              + "A outra maneira é <b>fazê-lo passar fome</b>. Uma bolsa não "
              + "sobrevive em terreno que alguém detém — <b>reivindica o "
              + "território</b> e ela morre sozinha. É a mesma regra duas vezes: "
              + "tomar terreno é como cresces, e é também como limpas.";

            // ── 7. The wells ───────────────────────────────────────────────
            t["The curse takes ground"] = "A maldição toma terreno";
            t["The pocket you just broke was the curse being <b>provoked</b>. It is "
              + "also a <b>territorial power</b>, and it expands the way you do.\n"
              + "It holds every well territory from the first minute. Every couple of "
              + "minutes it takes <b>one more</b> — always a territory that is next to "
              + "ground it already holds, <b>carries veilstone</b>, and has <b>no "
              + "Hall</b>. Look at the territory map: what it can take next is "
              + "readable, exactly like your own expansion.\n"
              + "Each territory it takes gets a <b>curse anchor</b>. Kill the anchor "
              + "and the ground reverts at once — anchors die, wells do not. And "
              + "every cursed veilstone territory <b>sends waves at you</b>, so "
              + "ground you leave hall-less becomes a front line.\n"
              + "<b>A Hall is a wall.</b> Claiming veilstone ground is how you stop "
              + "the map being eaten — expansion is defence."] =
                "A bolsa que acabaste de quebrar era a maldição <b>provocada</b>. Ela "
              + "é também uma <b>potência territorial</b>, e expande-se como tu.\n"
              + "Detém todos os territórios com poço desde o primeiro minuto. A cada "
              + "poucos minutos toma <b>mais um</b> — sempre um território vizinho de "
              + "terreno que já detém, que <b>tenha veilstone</b> e <b>nenhum "
              + "Salão</b>. Olha para o mapa de territórios: o que ela pode tomar a "
              + "seguir é legível, tal como a tua própria expansão.\n"
              + "Cada território que toma recebe uma <b>âncora da maldição</b>. Mata "
              + "a âncora e o terreno reverte de imediato — as âncoras morrem, os "
              + "poços não. E cada território de veilstone amaldiçoado <b>envia "
              + "vagas contra ti</b>, por isso o terreno que deixas sem Salão "
              + "torna-se uma frente de batalha.\n"
              + "<b>Um Salão é uma muralha.</b> Reivindicar terreno de veilstone é "
              + "como impedes que o mapa seja devorado — expandir é defender.";

            t["Train a Holy Scholar"] = "Treina um Erudito Sagrado";
            t["The giant veilstone formations are the <b>wells</b> — selecting one "
              + "reads <i>Veilstone Hive</i>. They are the largest income on the map "
              + "and the only way the match is won.\n"
              + "Every well is <b>dormant</b> until a player reaches for it. The curse "
              + "has held the ground around them since the first minute — but the "
              + "wells themselves are asleep, and a sleeping well does not fight "
              + "you.\n"
              + "Claiming one needs a ritualist. Alanthor's is the <b>Holy Scholar</b>, "
              + "trained at the <b>Temple of Ridan at level 3 or higher</b> — yours is "
              + "at 4. It has 90 HP and no answer to anything: a key, not a soldier."] =
                "As formações gigantes de veilstone são os <b>poços</b> — ao "
              + "selecionares um lê-se <i>Colmeia de Veilstone</i>. São o maior "
              + "rendimento do mapa e a única forma de vencer a partida.\n"
              + "Cada poço está <b>adormecido</b> até que um jogador lhe estenda a "
              + "mão. A maldição detém o terreno à sua volta desde o primeiro minuto "
              + "— mas os poços em si dormem, e um poço adormecido não te ataca.\n"
              + "Reivindicar um exige um ritualista. O de Alanthor é o <b>Erudito "
              + "Sagrado</b>, treinado no <b>Templo de Ridan ao nível 3 ou "
              + "superior</b> — o teu está no 4. Tem 90 PV e não tem resposta para "
              + "nada: é uma chave, não um soldado.";

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
