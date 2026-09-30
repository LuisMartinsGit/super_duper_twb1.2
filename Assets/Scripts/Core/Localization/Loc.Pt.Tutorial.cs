// Loc.Pt.Tutorial.cs
// Portuguese for the tutorial coach (TutorialDirector): chapter names, the
// step titles and bodies, grant labels, the eyebrow/progress line, buttons
// and every tutorial notification. Rewritten 2026-09-29 with the coach.
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
            t["2. Economy"] = "2. Economia";
            t["3. Army"] = "3. Exército";
            t["4. Territory"] = "4. Território";
            t["5. Culture"] = "5. Cultura";
            t["6. The curse"] = "6. A maldição";
            t["7. Religion"] = "7. Religião";
            t["8. Victory"] = "8. Vitória";

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
            t["Tutorial: the curse holds no node right now — it will raise one soon."] =
                "Tutorial: a maldição não tem nenhum nó agora — vai erguer um em breve.";

            // ── Grant labels ───────────────────────────────────────────────
            t["a building fund"] = "um fundo de construção";
            t["a survey fund"] = "um fundo de prospeção";
            t["an army budget"] = "um orçamento para o exército";
            t["a campaign purse"] = "uma bolsa de campanha";
            t["the landmark's cost"] = "o custo do monumento";
            t["a war chest"] = "um cofre de guerra";
            t["a siege budget"] = "um orçamento de cerco";
            t["Temple materials and a Religion Point"] =
                "materiais para o Templo e um Ponto de Religião";
            t["chapel materials and 3 Religion Points"] =
                "materiais para a capela e 3 Pontos de Religião";
            t["a hero's stipend and a Religion Point"] =
                "a bolsa de um herói e um Ponto de Religião";

            // ── 1. Controls ────────────────────────────────────────────────
            t["Look around"] = "Olha à tua volta";
            t["Push the mouse to any <b>screen edge</b> to pan, or use the "
              + "<b>arrow keys</b>. Hold the <b>middle mouse button</b> to drag the "
              + "view, or click the minimap to jump.\n"
              + "Find your <b>Fortress</b> — the capital your warband starts around. "
              + "It locks the ground it stands on, and that ground is your first "
              + "<b>territory</b>."] =
                "Leva o rato a qualquer <b>borda do ecrã</b> para deslocar a vista, ou "
              + "usa as <b>setas</b>. Mantém o <b>botão do meio</b> premido para "
              + "arrastar a vista, ou clica no minimapa para saltar.\n"
              + "Encontra a tua <b>Fortaleza</b> — a capital à volta da qual o teu "
              + "bando começa. Tranca o terreno onde está, e esse terreno é o teu "
              + "primeiro <b>território</b>.";

            t["Zoom"] = "Zoom";
            t["<b>Scroll wheel</b> zooms in and out.\n"
              + "Pull back to read a fight, push in to see what a building is doing."] =
                "A <b>roda do rato</b> aproxima e afasta.\n"
              + "Afasta para ler uma batalha, aproxima para ver o que um edifício "
              + "está a fazer.";

            // ── 2. Economy ─────────────────────────────────────────────────
            t["Select and move"] = "Selecionar e mover";
            t["<b>Left-click</b> a single Worker to select it. <b>Right-click</b> "
              + "the ground to send it there.\n"
              + "Its stats appear bottom-left; what it can do appears beside them."] =
                "Faz <b>clique esquerdo</b> num Trabalhador para o selecionar. Faz "
              + "<b>clique direito</b> no chão para o enviar para lá.\n"
              + "Os seus atributos aparecem em baixo à esquerda; o que pode fazer "
              + "aparece ao lado.";

            t["Box-select and build"] = "Seleção em caixa e construção";
            t["<b>Drag a box</b> over two or more Workers, then pick <b>Hut</b> from "
              + "the actions panel and left-click the ground.\n"
              + "Huts raise your population cap: <b>3</b>, and <b>5 / 8 / 10</b> as "
              + "you upgrade them. Your Fortress gives a flat <b>10</b>. Hold "
              + "<b>Shift</b> while placing to keep going."] =
                "<b>Arrasta uma caixa</b> sobre dois ou mais Trabalhadores, escolhe "
              + "<b>Cabana</b> no painel de ações e faz clique esquerdo no chão.\n"
              + "As cabanas aumentam o teu limite de população: <b>3</b>, e "
              + "<b>5 / 8 / 10</b> à medida que as melhoras. A tua Fortaleza dá "
              + "<b>10</b> fixos. Mantém <b>Shift</b> ao colocar para continuar.";

            t["Train more workers"] = "Treina mais trabalhadores";
            t["Select your <b>Fortress</b> and click <b>Worker</b> in the actions "
              + "panel.\n"
              + "The queue strip above the panel shows what is in production — "
              + "<b>right-click a queued chip</b> to cancel it and get the cost back."] =
                "Seleciona a tua <b>Fortaleza</b> e clica em <b>Trabalhador</b> no "
              + "painel de ações.\n"
              + "A faixa de fila acima do painel mostra o que está em produção — "
              + "<b>clique direito num item da fila</b> cancela-o e devolve o custo.";

            t["Your ground is already paying"] = "O teu terreno já está a pagar";
            t["<b>Nobody gathers anything.</b> Income comes from the ground you "
              + "hold: every territory pays you per minute, and every resource "
              + "<b>node</b> standing in it pays more.\n"
              + "Every home territory holds a <b>veilstone outcropping</b>, so "
              + "veilstone is arriving in your bank right now. Watch the counter.\n"
              + "An <b>extractor</b> built on a node multiplies what it pays — and, "
              + "as the next chapters show, it is also what makes the ground truly "
              + "yours."] =
                "<b>Ninguém recolhe nada.</b> O rendimento vem do terreno que "
              + "controlas: cada território paga-te por minuto, e cada <b>nó</b> de "
              + "recursos dentro dele paga mais.\n"
              + "Todos os territórios iniciais têm um <b>afloramento de Veilstone</b>, "
              + "por isso está a chegar Veilstone ao teu tesouro neste momento. Olha "
              + "para o contador.\n"
              + "Um <b>extrator</b> construído num nó multiplica o que ele paga — e, "
              + "como os próximos capítulos mostram, é também o que torna o terreno "
              + "verdadeiramente teu.";

            t["Build on your nodes"] = "Constrói nos teus nós";
            t["Raise a <b>Gatherer's Hut</b> on a <b>supply node</b> and a <b>Mine</b> "
              + "on an ore node — iron, veilstone or veilsteel.\n"
              + "There is <b>one Mine button</b>: the node under your cursor decides "
              + "which mine rises. Each node takes exactly one extractor, and "
              + "<b>nothing else can be built on a node</b>."] =
                "Ergue uma <b>Cabana do Recoletor</b> num <b>nó de mantimentos</b> e "
              + "uma <b>Mina</b> num nó de minério — ferro, Veilstone ou Veilsteel.\n"
              + "Há <b>um só botão de Mina</b>: o nó debaixo do cursor decide que "
              + "mina se ergue. Cada nó aceita exatamente um extrator, e "
              + "<b>mais nada pode ser construído num nó</b>.";

            // ── 3. Army ────────────────────────────────────────────────────
            t["Raise a Barracks"] = "Ergue um Quartel";
            t["Build a <b>Barracks</b>, then train <b>three Spearmen</b>.\n"
              + "Units counter each other — select one and read the <b>Bonus vs</b> "
              + "line in its stats."] =
                "Constrói um <b>Quartel</b> e depois treina <b>três Lanceiros</b>.\n"
              + "As unidades contrariam-se umas às outras — seleciona uma e lê a "
              + "linha <b>Bónus contra</b> nos seus atributos.";

            t["Chain your orders"] = "Encadeia as tuas ordens";
            t["Hold <b>Shift</b> and <b>right-click</b> several points: the orders "
              + "queue up and run in turn — up to <b>15</b>. The route is drawn on "
              + "the ground.\n"
              + "Soldiers start <b>Aggressive</b>: they chase anything in sight. "
              + "<b>D</b> makes them Defensive, <b>H</b> holds them in place, <b>G</b> "
              + "turns them loose again. A queued move is obeyed over the stance — "
              + "if you want them to fight on the way, press <b>A</b> first."] =
                "Mantém <b>Shift</b> e faz <b>clique direito</b> em vários pontos: as "
              + "ordens entram em fila e são cumpridas por ordem — até <b>15</b>. A "
              + "rota é desenhada no chão.\n"
              + "Os soldados começam <b>Agressivos</b>: perseguem tudo o que veem. "
              + "<b>D</b> torna-os Defensivos, <b>H</b> mantém-nos no lugar, <b>G</b> "
              + "solta-os de novo. Um movimento em fila é obedecido acima da "
              + "postura — se os queres a lutar pelo caminho, carrega primeiro em "
              + "<b>A</b>.";

            t["Take the fight out"] = "Leva a luta lá para fora";
            t["<b>Box-select</b> your soldiers and <b>right-click an enemy</b> to "
              + "attack.\nPress <b>A</b> then click the ground to attack-move — they "
              + "will engage anything they meet on the way."] =
                "<b>Seleciona em caixa</b> os teus soldados e faz <b>clique direito "
              + "num inimigo</b> para atacar.\nCarrega em <b>A</b> e clica no chão "
              + "para avançar a atacar — vão enfrentar tudo o que encontrarem pelo "
              + "caminho.";

            // ── 4. Territory ───────────────────────────────────────────────
            t["Claim ground with your army"] = "Reivindica terreno com o teu exército";
            t["<b>Ground belongs to whoever stands on it.</b> March soldiers into a "
              + "neighbouring territory and keep them there: its meter fills, "
              + "faster the more population stands on it, and at <b>100</b> the "
              + "territory is yours. A bar over the territory itself shows the progress.\n"
              + "Workers and scouts do not claim. Two hostile armies in one territory "
              + "<b>freeze</b> it — you take ground by clearing it."] =
                "<b>O terreno pertence a quem está nele.</b> Marcha soldados para um "
              + "território vizinho e mantém-nos lá: o seu medidor enche, mais "
              + "depressa quanto mais população lá estiver, e aos <b>100</b> o "
              + "território é teu. Uma barra por cima do próprio território mostra o progresso.\n"
              + "Trabalhadores e batedores não reivindicam. Dois exércitos hostis no "
              + "mesmo território <b>congelam-no</b> — conquista-se terreno limpando-o.";

            t["Hold it, or lose it"] = "Segura-o, ou perde-o";
            t["Ground you leave <b>empty and unbuilt</b> decays back to nobody.\n"
              + "<b>Any building holds</b> a territory against decay — but an enemy "
              + "army standing there still drains it.\n"
              + "An <b>extractor on a node</b>, or a <b>Fortress</b>, <b>locks</b> it: "
              + "the meter cannot be drained at all while one stands. To take locked "
              + "ground, raze every lock first.\n"
              + "Lose a territory and <b>every building you had in it collapses</b>. "
              + "A second Fortress is the most expensive thing you can build — and "
              + "the surest lock there is."] =
                "Terreno que deixas <b>vazio e sem construções</b> decai de volta para "
              + "ninguém.\n"
              + "<b>Qualquer edifício segura</b> um território contra o decaimento — "
              + "mas um exército inimigo lá dentro continua a drená-lo.\n"
              + "Um <b>extrator num nó</b>, ou uma <b>Fortaleza</b>, <b>tranca-o</b>: "
              + "o medidor não pode ser drenado enquanto um estiver de pé. Para "
              + "tomar terreno trancado, arrasa primeiro todas as trancas.\n"
              + "Perde um território e <b>todos os edifícios que lá tinhas "
              + "desabam</b>. Uma segunda Fortaleza é a coisa mais cara que podes "
              + "construir — e a tranca mais segura que existe.";

            // ── 5. Culture ─────────────────────────────────────────────────
            t["Build your landmark"] = "Constrói o teu monumento";
            t["The age-up <b>is</b> a building. Pick the <b>Vault of Almiérra</b> from "
              + "the top of the screen and place it on ground you own.\n"
              + "It costs <b>600 supplies, 300 iron and 200 veilstone</b>, builds "
              + "itself in 90 seconds, and every worker you send speeds it up. "
              + "<b>Only one per match</b> — and if it is destroyed before it "
              + "finishes, the progress and everything you paid are lost."] =
                "O avanço de Era <b>é</b> um edifício. Escolhe o <b>Cofre de "
              + "Almiérra</b> no topo do ecrã e coloca-o em terreno teu.\n"
              + "Custa <b>600 mantimentos, 300 ferro e 200 Veilstone</b>, "
              + "constrói-se sozinho em 90 segundos, e cada trabalhador que envias "
              + "acelera-o. <b>Só um por partida</b> — e se for destruído antes de "
              + "terminar, o progresso e tudo o que pagaste perdem-se.";

            t["Age up"] = "Avança de Era";
            t["When the Vault finishes, you <b>are</b> Alanthor: Age 0 ends and your "
              + "culture's units, buildings and upgrades open.\n"
              + "Protect the site until then. The <b>Advancing</b> pill at the top "
              + "shows how far it has come."] =
                "Quando o Cofre termina, <b>és</b> Alanthor: a Era 0 acaba e abrem-se "
              + "as unidades, edifícios e melhorias da tua cultura.\n"
              + "Protege o local até lá. A etiqueta <b>A avançar</b> no topo mostra "
              + "quanto já avançou.";

            // ── 6. The curse ───────────────────────────────────────────────
            t["The curse"] = "A maldição";
            t["The curse claims ground the same way you do — by standing on it, "
              + "at <b>double weight</b>. It raises <b>curse nodes</b> on resource "
              + "nodes, and each one <b>locks</b> its territory and fields a "
              + "garrison that grows over time.\n"
              + "The purple around a node is <b>cursed ground</b>: it slows and "
              + "burns anything that stands in it.\n"
              + "The minimap is pinging the nearest node now."] =
                "A maldição reivindica terreno como tu — estando nele, com <b>peso "
              + "a dobrar</b>. Ergue <b>Nós da Maldição</b> sobre nós de "
              + "recursos, e cada um <b>tranca</b> o seu território e mantém uma "
              + "guarnição que cresce com o tempo.\n"
              + "O roxo à volta de um nó é <b>terreno amaldiçoado</b>: abranda e "
              + "queima tudo o que lá estiver.\n"
              + "O minimapa está a assinalar agora o nó mais próximo.";

            t["Kill curse creatures"] = "Mata criaturas da maldição";
            t["Curse creatures are the <b>only source of religion</b>. Every kill "
              + "pays points — the <b>last hit</b> is paid, whatever landed it — and "
              + "points turn into <b>Religion Points</b>. The first ones come cheap.\n"
              + "The ring beside your resources shows how close the next one is."] =
                "As criaturas da maldição são a <b>única fonte de religião</b>. Cada "
              + "morte paga pontos — é pago o <b>último golpe</b>, venha de onde "
              + "vier — e os pontos tornam-se <b>Pontos de Religião</b>. Os "
              + "primeiros saem baratos.\n"
              + "O anel ao lado dos teus recursos mostra quão perto está o próximo.";

            t["Destroy a curse node"] = "Destrói um Nó da Maldição";
            t["Bring a real army — the garrison defends it. Raze the node and "
              + "you are paid <b>a full Religion Point</b>, and the territory "
              + "<b>unlocks</b>: stand on it and it is yours to claim.\n"
              + "The curse is never gone for good. With no node left, it raises a new "
              + "one somewhere after a few minutes."] =
                "Traz um exército a sério — a guarnição defende-o. Arrasa o nó e "
              + "recebes <b>um Ponto de Religião inteiro</b>, e o território "
              + "<b>destranca-se</b>: fica nele e é teu para reivindicar.\n"
              + "A maldição nunca desaparece de vez. Sem nenhum nó, ergue um novo "
              + "algures ao fim de alguns minutos.";

            // ── 7. Religion ────────────────────────────────────────────────
            t["Raise the Temple of Ridan"] = "Ergue o Templo de Ridan";
            t["Place the <b>Temple of Ridan</b>: <b>1 Religion Point</b> plus "
              + "200 supplies and 100 iron.\n"
              + "It trains the <b>Litharch</b>, adds <b>+50 %</b> to every curse kill "
              + "and trickles religion on its own. Its <b>Tithe</b> — click your "
              + "Religion Points — buys an RP for resources, dearer each time."] =
                "Coloca o <b>Templo de Ridan</b>: <b>1 Ponto de Religião</b> mais "
              + "200 mantimentos e 100 ferro.\n"
              + "Treina o <b>Litharch</b>, acrescenta <b>+50 %</b> a cada morte da "
              + "maldição e gera religião por si, devagar. O seu <b>Dízimo</b> — "
              + "clica nos teus Pontos de Religião — compra um PR com recursos, "
              + "mais caro de cada vez.";

            t["Adopt a sect"] = "Adota uma seita";
            t["The <b>religion panel</b> on the right shows your chapel slots. A "
              + "chapel adopts a sect: <b>2 RP</b> for a sect of your own culture, "
              + "<b>3 RP</b> for any other — and before you age up, every sect is "
              + "3.\nIt grants the sect's passive, its research and its first power."] =
                "O <b>painel de religião</b> à direita mostra as tuas capelas. Uma "
              + "capela adota uma seita: <b>2 PR</b> para uma seita da tua cultura, "
              + "<b>3 PR</b> para qualquer outra — e antes de avançares de Era, "
              + "todas custam 3.\nDá o passivo da seita, a sua pesquisa e o seu "
              + "primeiro poder.";

            t["Cast a sect power"] = "Lança um poder de seita";
            t["Your sect's slot carries its cells: <b>P</b> is its always-on "
              + "passive, the numbered cells are its powers.\n"
              + "Click a lit one and pick a target on the map. The <b>+</b> cells "
              + "unlock the other powers, and the chapel's level — bought with RP — "
              + "is the level of every power it has."] =
                "A capela da tua seita mostra as suas células: <b>P</b> é o passivo "
              + "sempre ativo, as células numeradas são os seus poderes.\n"
              + "Clica numa acesa e escolhe um alvo no mapa. As células <b>+</b> "
              + "desbloqueiam os outros poderes, e o nível da capela — comprado com "
              + "PR — é o nível de todos os seus poderes.";

            t["Recruit a sect hero"] = "Recruta um herói de seita";
            t["Every sect has <b>one hero</b>, recruited at its chapel for "
              + "<b>1 RP</b> plus its price. Heroes gain levels by fighting.\n"
              + "If yours falls, recruit it again — a revival costs resources, never "
              + "Religion Points."] =
                "Cada seita tem <b>um herói</b>, recrutado na sua capela por "
              + "<b>1 PR</b> mais o seu preço. Os heróis ganham níveis a combater.\n"
              + "Se o teu cair, recruta-o de novo — uma revivificação custa "
              + "recursos, nunca Pontos de Religião.";

            // ── 8. Victory ─────────────────────────────────────────────────
            t["The Shardroot, and the last one standing"] =
                "A Shardroot, e o último de pé";
            t["Sooner or later a curse creature rides out carrying the "
              + "<b>Shardroot</b>. Kill it and the artifact is yours to carry and "
              + "store — and from that moment <b>the whole curse hunts you</b>, "
              + "bigger and faster, and ignores everyone else.\n"
              + "There is one way to win: <b>be the last player standing</b>. A "
              + "faction is out when it has no Fortress, no military building and "
              + "no Worker left."] =
                "Mais cedo ou mais tarde uma criatura da maldição sai a carregar a "
              + "<b>Shardroot</b>. Mata-a e o artefacto é teu para levar e "
              + "guardar — e a partir desse momento <b>toda a maldição te caça</b>, "
              + "maior e mais rápida, e ignora todos os outros.\n"
              + "Só há uma forma de vencer: <b>ser o último jogador de pé</b>. Uma "
              + "fação está fora quando não lhe resta Fortaleza, edifício militar "
              + "nem Trabalhador.";
        }
    }
}
