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
            t["4. Culture"] = "4. Cultura";
            t["5. Territory"] = "5. Território";
            t["6. The curse"] = "6. A maldição";
            t["7. Religion"] = "7. Religião";
            t["8. Heroes and victory"] = "8. Heróis e vitória";

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
            t["the landmark's cost"] = "o custo do marco";
            t["a war chest"] = "um cofre de guerra";
            t["a siege budget"] = "um orçamento de cerco";
            t["Temple materials"] = "materiais para o Templo";
            t["sect materials and a Religion Point"] = "materiais da seita e um Ponto de Religião";
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
            t["<b>Drag a box</b> over two or more Workers, then pick <b>Hut</b> from the actions panel and left-click the ground.\nYour order first appears as a <b>white plan</b> only you can see; it rises when a Worker arrives. Huts raise your population cap, more as you upgrade them. Hold <b>Shift</b> while placing to keep going."] =
                "<b>Arrasta uma caixa</b> sobre dois ou mais Trabalhadores, escolhe <b>Cabana</b> no painel de ações e faz clique esquerdo no chão.\nA tua ordem aparece primeiro como um <b>plano branco</b> que só tu vês; ergue-se quando chega um Trabalhador. As cabanas aumentam o teu limite de população, mais à medida que as melhoras. Mantém <b>Shift</b> ao colocar para continuar.";

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
            t["Raise a <b>Gatherer's Hut</b> on a <b>supply node</b> and a <b>Mine</b> on an iron or veilstone node.\nThere is <b>one Mine button</b>: the node under your cursor decides which mine rises. Each node takes exactly one extractor, <b>nothing else can be built on a node</b>, and every node eventually runs dry."] =
                "Ergue uma <b>Cabana do Recoletor</b> num <b>nó de mantimentos</b> e uma <b>Mina</b> num nó de ferro ou Veilstone.\nHá <b>um só botão de Mina</b>: o nó debaixo do cursor decide que mina se ergue. Cada nó aceita exatamente um extrator, <b>mais nada pode ser construído num nó</b>, e todos os nós acabam por se esgotar.";

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
            t["<b>Ground belongs to whoever stands on it.</b> After age-up, march soldiers into a territory bordering yours and keep them there: its meter fills, faster the more population stands on it, and at <b>100</b> the territory is yours. A bar over the territory itself shows the progress.\nWorkers and scouts do not claim. When two hostile armies share a territory, <b>the stronger side pushes the meter</b> by the difference."] =
                "<b>O terreno pertence a quem está nele.</b> Depois de avançares de Era, marcha soldados para um território que faça fronteira com o teu e mantém-nos lá: o seu medidor enche, mais depressa quanto mais população lá estiver, e aos <b>100</b> o território é teu. Uma barra por cima do próprio território mostra o progresso.\nTrabalhadores e batedores não reivindicam. Quando dois exércitos hostis partilham um território, <b>o lado mais forte empurra o medidor</b> pela diferença.";

            t["Hold it, or lose it"] = "Segura-o, ou perde-o";
            t["Ground you leave <b>empty and unbuilt</b> decays back to nobody.\n<b>Any building holds</b> a territory against decay — but an enemy army standing there still drains it.\nAn <b>extractor on a node</b>, or a <b>Fortress</b>, <b>locks</b> it: the meter cannot be drained at all while one stands. To take locked ground, raze every lock first.\nLose a territory and <b>every building you had in it collapses</b>. Ground cut off from your Fortress slowly wears down."] =
                "Terreno que deixas <b>vazio e sem construções</b> decai de volta para ninguém.\n<b>Qualquer edifício segura</b> um território contra o decaimento — mas um exército inimigo lá dentro continua a drená-lo.\nUm <b>extrator num nó</b>, ou uma <b>Fortaleza</b>, <b>tranca-o</b>: o medidor não pode ser drenado enquanto um estiver de pé. Para tomar terreno trancado, arrasa primeiro todas as trancas.\nPerde um território e <b>todos os edifícios que lá tinhas desabam</b>. O terreno isolado da tua Fortaleza desgasta-se devagar.";

            // ── 5. Culture ─────────────────────────────────────────────────
            t["Build your landmark"] = "Constrói o teu marco";
            t["The age-up <b>is</b> a building. Pick the <b>Vault of Almiérra</b> from the top of the screen and place it on ground you own.\nIt builds itself slowly, and every worker you send speeds it up. <b>Only one per match</b> — and if it is destroyed before it finishes, the progress and everything you paid are lost."] =
                "O avanço de Era <b>é</b> um edifício. Escolhe o <b>Cofre de Almiérra</b> no topo do ecrã e coloca-o em terreno teu.\nConstrói-se sozinho, devagar, e cada trabalhador que envias acelera-o. <b>Só um por partida</b> — e se for destruído antes de terminar, o progresso e tudo o que pagaste perdem-se.";

            t["Age up"] = "Avança de Era";
            t["When the Vault finishes, you <b>are</b> Alanthor: Age 0 ends and your culture's units, buildings and upgrades open. Only now can your army claim new ground.\nProtect the site until then. The <b>Advancing</b> pill at the top shows how far it has come."] =
                "Quando o Cofre termina, <b>és</b> Alanthor: a Era 0 acaba e abrem-se as unidades, edifícios e melhorias da tua cultura. Só agora o teu exército pode reivindicar novo terreno.\nProtege o local até lá. A etiqueta <b>A avançar</b> no topo mostra quanto já avançou.";

            // ── 6. The curse ───────────────────────────────────────────────
            t["The curse"] = "A maldição";
            t["The curse claims ground the same way you do — by standing on it. It raises <b>curse nodes</b> on resource nodes, and each one <b>locks</b> its territory and fields a garrison that grows over time.\nThe purple around a node is <b>cursed ground</b>: it slows and burns anything that stands in it, and wears down buildings. A <b>Litharch</b> can clear it.\nThe minimap is pinging the nearest node now."] =
                "A maldição reivindica terreno como tu — estando nele. Ergue <b>Nós da Maldição</b> sobre nós de recursos, e cada um <b>tranca</b> o seu território e mantém uma guarnição que cresce com o tempo.\nO roxo à volta de um nó é <b>terreno amaldiçoado</b>: abranda e queima tudo o que lá estiver, e desgasta os edifícios. Um <b>Litharch</b> pode limpá-lo.\nO minimapa está a assinalar agora o nó mais próximo.";

            t["Kill curse creatures"] = "Mata criaturas da maldição";
            t["Curse creatures are the <b>surest source of religion</b>. Every kill pays points — the <b>last hit</b> is paid, whatever landed it — and points turn into <b>Religion Points</b>. The first ones come cheap.\nThe ring beside your resources shows how close the next one is."] =
                "As criaturas da maldição são a <b>fonte mais segura de religião</b>. Cada morte paga pontos — é pago o <b>último golpe</b>, venha de onde vier — e os pontos tornam-se <b>Pontos de Religião</b>. Os primeiros saem baratos.\nO anel ao lado dos teus recursos mostra quão perto está o próximo.";

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
            t["Place the <b>Temple of Ridan</b>. It costs only resources.\nIt trains the <b>Litharch</b>, adds to every curse kill and trickles religion on its own. Its <b>Tithe</b> — click your Religion Points — buys an RP for resources, dearer each time. It can also trade RP for resources and sell you the <b>Petriarchy's Army</b>."] =
                "Coloca o <b>Templo de Ridan</b>. Custa só recursos.\nTreina o <b>Litharch</b>, soma a cada morte da maldição e gera religião por si, devagar. O seu <b>Dízimo</b> — clica nos teus Pontos de Religião — compra um PR com recursos, mais caro de cada vez. Também troca PR por recursos e vende-te o <b>Exército da Patriarquia</b>.";

            t["Adopt a sect"] = "Adota uma seita";
            t["The <b>religion panel</b> on the right (<b>R</b>) shows your six chapel slots, and <b>T</b> opens the full tree. A chapel adopts a sect for RP, cheaper for a sect of your own culture, and the choice is final.\nIt unlocks the sect's first power and lets you raise its building."] =
                "O <b>painel de religião</b> à direita (<b>R</b>) mostra os teus seis lugares de capela, e <b>T</b> abre a árvore completa. Uma capela adota uma seita com PR, mais barato para uma seita da tua cultura, e a escolha é definitiva.\nDesbloqueia o primeiro poder da seita e permite erguer o seu edifício.";

            t["Cast a sect power"] = "Lança um poder de seita";
            t["Each sect has a row of cells: <b>A</b>, <b>B</b> and a wildcard power. Click a lit one, or press <b>F1-F6</b>, and pick a target in sight.\nRP unlocks the other powers, and the chapel's level — also bought with RP — is the level of every power it has."] =
                "Cada seita tem uma fila de células: os poderes <b>A</b>, <b>B</b> e um trunfo. Clica numa acesa, ou carrega em <b>F1-F6</b>, e escolhe um alvo à vista.\nOs PR desbloqueiam os outros poderes, e o nível da capela — também comprado com PR — é o nível de todos os seus poderes.";

            t["Recruit a sect hero"] = "Recruta um herói de seita";
            t["The sect's building also recruits its <b>hero</b>, for 1 RP the first time plus its price. Heroes are the strongest units in the game, and they gain levels by fighting.\nTheir abilities <b>cast themselves</b> within their sight. If a hero falls, train it again: it returns at <b>the same level</b>, for more resources and time, and never for RP."] =
                "O edifício da seita também recruta o seu <b>herói</b>, por 1 PR da primeira vez mais o seu preço. Os heróis são as unidades mais fortes do jogo, e sobem de nível a combater.\nAs suas habilidades <b>lançam-se sozinhas</b> dentro da sua visão. Se um herói cair, treina-o de novo: regressa ao <b>mesmo nível</b>, por mais recursos e tempo, e nunca por PR.";

            // ── New steps (2026-11-07) ─────────────────────────────────────
            t["See first, and grow stronger"] = "Vê primeiro, e fica mais forte";
            t["You only know what you can see: use your <b>Scout</b> to find what is out there, because powers can only be aimed at ground in sight.\nArmies grow three ways: <b>building levels</b>, which count for every building of that type, <b>research</b> and <b>unit ranks</b>."] =
                "Só sabes o que consegues ver: usa o teu <b>Batedor</b> para descobrir o que há lá fora, porque os poderes só se apontam a terreno à vista.\nOs exércitos crescem de três formas: <b>níveis de edifício</b>, que contam para todos os edifícios desse tipo, <b>pesquisa</b> e <b>patentes</b>.";
            t["Walls and gates"] = "Muralhas e portões";
            t["Alanthor holds ground with stone. Every culture starts with a timber <b>Palisade</b>; yours becomes a <b>Stone Wall</b> you can level up.\nDraw walls, convert a piece into a <b>gate</b> or a <b>tower</b>, and put soldiers on top. <b>Only siege can break a wall.</b>"] =
                "Alanthor segura terreno com pedra. Todas as culturas começam com uma <b>Paliçada</b> de madeira; a tua torna-se uma <b>Muralha de Pedra</b> que podes subir de nível.\nDesenha muralhas, converte uma secção num <b>portão</b> ou numa <b>torre</b>, e põe soldados lá em cima. <b>Só o cerco parte uma muralha.</b>";
            t["Raise the sect's building"] = "Ergue o edifício da seita";
            t["Workers build the sect's own <b>building</b>. While it stands it gives the sect's passive effect, and it sells the sect's research.\nIt trains the <b>sect unit</b>: far stronger than a normal soldier, but each one costs a <b>Religion Point</b> on top of its resources."] =
                "Os trabalhadores erguem o <b>edifício</b> da seita. Enquanto estiver de pé dá o efeito passivo da seita, e vende a sua pesquisa.\nTreina a <b>unidade de seita</b>: muito mais forte do que um soldado normal, mas cada uma custa um <b>Ponto de Religião</b> além dos recursos.";
            t["Take a monument"] = "Toma um monumento";
            t["Monuments stand in the curse's first territories. Any unit standing in the circle takes one, even a Worker, and it <b>trickles Religion Points</b> while you hold it.\nPay to <b>explore its ruins</b> for a hidden buff that is yours for good. One monument also knows where the <b>Shardroot</b> lies hidden."] =
                "Os monumentos estão nos primeiros territórios da maldição. Qualquer unidade no círculo toma um, até um Trabalhador, e ele <b>gera Pontos de Religião</b> enquanto o seguras.\nPaga para <b>explorar as ruínas</b> e ganhar um bónus oculto, teu para sempre. Um monumento também sabe onde está escondida a <b>Shardroot</b>.";
            t["Level 10"] = "Nível 10";
            t["A hero's <b>level 10 ability</b> is the one you cast yourself. It has a very long cooldown, it reaches beyond the hero's sight, and it can <b>end the game</b>.\nEveryone can see a hero's level on its bar, so everyone knows when one is close."] =
                "A <b>habilidade de nível 10</b> de um herói é a que lanças tu. Tem uma recarga muito longa, chega além da visão do herói, e pode <b>decidir a partida</b>.\nTodos veem o nível de um herói na sua barra, por isso todos sabem quando está perto.";
            t["The Shardroot"] = "A Shardroot";
            t["<b>Only a hero can take the Shardroot.</b> Carrying it makes the hero <b>Ascended</b>: very hard to kill, and hunted by the curse and every rival. If it dies anyway, <b>it explodes</b>.\nWith the bearer selected you can <b>Drop</b> it safely, or stow it in your Temple for the Temple victory."] =
                "<b>Só um herói pode levar a Shardroot.</b> Carregá-la torna o herói <b>Ascendido</b>: muito difícil de matar, e caçado pela maldição e por todos os rivais. Se morrer na mesma, <b>explode</b>.\nCom o portador selecionado podes <b>Largá-la</b> em segurança, ou guardá-la no teu Templo para a vitória do Templo.";
            t["Three ways to win"] = "Três formas de vencer";
            t["<b>Be the last player standing.</b> A faction is out with no Fortress, military building or Worker left, or after too long with no territory.\n<b>Keep the Shardroot in your Temple</b> through its countdown, or <b>hold every Sanctum</b> through theirs. Everyone can see those timers.\nOpen the <b>Field Guide</b> from the pause menu to read any card again."] =
                "<b>Sê o último jogador de pé.</b> Uma fação está fora sem Fortaleza, edifício militar nem Trabalhador, ou após demasiado tempo sem território.\n<b>Mantém a Shardroot no teu Templo</b> durante a contagem, ou <b>segura todos os Sanctums</b> durante a sua. Todos veem esses temporizadores.\nAbre o <b>Guia de Campo</b> no menu de pausa para reler qualquer cartão.";
            t["Field Guide"] = "Guia de Campo";
            t["Read the infographic cards: every feature of the game and the tech tree."] = "Lê os cartões informativos: todas as funcionalidades do jogo e a árvore tecnológica.";
            t["Previous"] = "Anterior";
            t["Next"] = "Seguinte";
            t["Close"] = "Fechar";
            t["Stop showing cards"] = "Não mostrar mais cartões";
        }
    }
}
