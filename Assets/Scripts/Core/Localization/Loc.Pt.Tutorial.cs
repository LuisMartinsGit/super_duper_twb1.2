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
            t["sect materials and 3 Religion Points"] =
                "materiais da seita e 3 Pontos de Religião";
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
              + "<b>D</b> makes them Standard (they only return fire), <b>H</b> makes "
              + "them Defensive (they hold in place), <b>G</b> "
              + "turns them loose again. A queued move is obeyed over the stance — "
              + "if you want them to fight on the way, press <b>A</b> first."] =
                "Mantém <b>Shift</b> e faz <b>clique direito</b> em vários pontos: as "
              + "ordens entram em fila e são cumpridas por ordem — até <b>15</b>. A "
              + "rota é desenhada no chão.\n"
              + "Os soldados começam <b>Agressivos</b>: perseguem tudo o que veem. "
              + "<b>D</b> põe-nos em Padrão (só respondem ao fogo), <b>H</b> põe-nos "
              + "em Defensivo (mantêm-se no lugar), <b>G</b> "
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
            t["Ground you claim <b>stays yours</b> when your soldiers leave: only an enemy army standing on it can take it.\nThe one exception is the curse: while it holds <b>two or more nodes</b> in a territory, that ground decays back to nobody.\nAn <b>extractor on a node</b>, or a <b>Fortress</b>, <b>locks</b> it: the meter cannot be drained at all while one stands. To take locked ground, raze every lock first.\nLose a territory and <b>every building you had in it collapses</b>."] =
                "O terreno que reivindicas <b>continua teu</b> quando os teus soldados saem: só um exército inimigo lá dentro o pode tomar.\nA única exceção é a maldição: enquanto tiver <b>dois ou mais nós</b> num território, esse terreno decai de volta para ninguém.\nUm <b>extrator num nó</b>, ou uma <b>Fortaleza</b>, <b>tranca-o</b>: o medidor não pode ser drenado enquanto um estiver de pé. Para tomar terreno trancado, arrasa primeiro todas as trancas.\nPerde um território e <b>todos os edifícios que lá tinhas desabam</b>.";

            // ── 5. Culture ─────────────────────────────────────────────────
            t["Build your landmark"] = "Constrói o teu marco";
            t["The age-up <b>is</b> a building. Pick the <b>Vault of Almiérra</b> from the top of the screen and place it on ground you own.\nYour Workers build it, like any other building - send them to the site. <b>Only one per match</b> — and if it is destroyed before it finishes, the progress and everything you paid are lost."] =
                "O avanço de Era <b>é</b> um edifício. Escolhe o <b>Cofre de Almiérra</b> no topo do ecrã e coloca-o em terreno teu.\nOs teus Trabalhadores constroem-no, como qualquer outro edifício - envia-os para o local. <b>Só um por partida</b> — e se for destruído antes de terminar, o progresso e tudo o que pagaste perdem-se.";

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
            t["The <b>religion panel</b> on the right (<b>R</b>) shows the sects. Adopting one costs RP, cheaper for a sect of your own culture, and the choice is final.\nIt unlocks the sect's first power and lets you raise its building."] =
                "O <b>painel de religião</b> à direita (<b>R</b>) mostra as seitas. Adotar uma custa PR, mais barato para uma seita da tua cultura, e a escolha é definitiva.\nDesbloqueia o primeiro poder da seita e permite erguer o seu edifício.";

            t["Cast a sect power"] = "Lança um poder de seita";
            t["Each sect has a row of cells: <b>A</b>, <b>B</b> and a wildcard power. Click a lit one, or press <b>F1-F6</b>, and pick a target in sight.\nRP unlocks the other powers, and the sect's level — also bought with RP — is the level of every power it has."] =
                "Cada seita tem uma fila de células: os poderes <b>A</b>, <b>B</b> e um trunfo. Clica numa acesa, ou carrega em <b>F1-F6</b>, e escolhe um alvo à vista.\nOs PR desbloqueiam os outros poderes, e o nível da seita — também comprado com PR — é o nível de todos os seus poderes.";

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

            // ── The tutorial missions (docs/Design/Tutorial_Missions.md) ──
            t["1: The Basics"] = "1: O Essencial";
            t["2: Alanthor"] = "2: Alanthor";

            // ── The Tutorials screen (TutorialsMenu.unity, TutorialsPanel) ──
            t["TUTORIALS"] = "TUTORIAIS";
            t["MISSIONS"] = "MISSÕES";
            t["MISSION NAME"] = "NOME DA MISSÃO";
            t["START MISSION"] = "INICIAR MISSÃO";
            t["Pick a mission to read its briefing."] = "Escolhe uma missão para leres o seu briefing.";
            t["Age 0, on a small map with two territories: yours and the curse's.\n\n"
              + "1. Move the camera and give orders\n"
              + "2. Rescue the lost workers\n"
              + "3. Build the Shelter and settle the camp\n"
              + "4. Huts, Gatherer's Huts, mines and the Barracks\n"
              + "5. Train Scouts and Spearmen\n"
              + "6. Survive the curse waves and destroy the node"] =
                "Era 0, num mapa pequeno com dois territórios: o teu e o da maldição.\n\n"
              + "1. Mover a câmara e dar ordens\n"
              + "2. Resgatar os trabalhadores perdidos\n"
              + "3. Construir o Abrigo e fundar o acampamento\n"
              + "4. Cabanas, Cabanas do Recoletor, minas e o Quartel\n"
              + "5. Treinar Batedores e Lanceiros\n"
              + "6. Sobreviver às vagas da maldição e destruir o nó";
            t["Your town is built. Age up to Alanthor and win the match.\n\n"
              + "1. Build the Vault of Almiérra and age up\n"
              + "2. The Alanthor economy: Guilds and Mines\n"
              + "3. The Alanthor army\n"
              + "4. Walls, towers and claiming territory\n"
              + "5. Religion Points, the Temple and the sects\n"
              + "6. The cursed Sanctum, the monument and the Shardroot\n"
              + "7. Three ways to win: conquest, enshrinement, or every Sanctum"] =
                "A tua cidade está construída. Avança para Alanthor e vence a partida.\n\n"
              + "1. Construir o Cofre de Almiérra e avançar de Era\n"
              + "2. A economia de Alanthor: Guildas e Minas\n"
              + "3. O exército de Alanthor\n"
              + "4. Muralhas, torres e conquista de território\n"
              + "5. Pontos de Religião, o Templo e as seitas\n"
              + "6. O Santuário amaldiçoado, o monumento e a Shardroot\n"
              + "7. Três formas de vencer: conquista, consagração ou todos os Santuários";
            t["You are Alanthor now. Your buildings have new forms."] = "Agora são Alanthor. Os teus edifícios têm novas formas.";
            t["A rival kingdom holds the east."] = "Um reino rival domina o leste.";
            t["The node is gone. A monument stands in the Sanctum."] = "O nó caiu. Há um monumento no Santuário.";
            t["The rival kingdom is now your enemy. Choose how to win."] = "O reino rival é agora teu inimigo. Escolhe como vencer.";
            t["Your capital has fallen."] = "A tua capital caiu.";
            t["1 - Build the Vault of Almiérra ({0}/{1})"] = "1 - Constrói o Cofre de Almiérra ({0}/{1})";
            t["2a - Level up your Guilds ({0}/{1})"] = "2a - Sobe as tuas Guildas de nível ({0}/{1})";
            t["Your Gatherer's Huts are now <b>Guilds</b>: select one and level it up — every Guild levels with it and pays more. Then level up a <b>Mine</b> the same way. Every node runs dry in the end: keep raising extractors on fresh nodes. The Litharch is now <b>King Lexor</b>, your hero."] =
                "As tuas Cabanas do Recoletor são agora <b>Guildas</b>: seleciona uma e sobe-a de nível — todas as Guildas sobem com ela e rendem mais. Depois sobe uma <b>Mina</b> de nível da mesma forma. Todos os nós acabam por se esgotar: continua a erguer extratores em nós novos. O Litharch é agora o <b>Rei Lexor</b>, o teu herói.";
            t["2b - Level up your Mines ({0}/{1})"] = "2b - Sobe as tuas Minas de nível ({0}/{1})";
            t["3 - Build an Archery Range ({0}/{1})"] = "3 - Constrói uma Campo de Tiro com Arco ({0}/{1})";
            t["4a - Build a Battle Tower ({0}/{1})"] = "4a - Constrói uma Torre de Batalha ({0}/{1})";
            t["4b - Claim a territory ({0}/{1})"] = "4b - Conquista um território ({0}/{1})";
            t["5 - Earn Religion Points ({0}/{1})"] = "5 - Ganha Pontos de Religião ({0}/{1})";
            t["6a - Build the Temple ({0}/{1})"] = "6a - Constrói o Templo ({0}/{1})";
            t["6b - Adopt a sect ({0}/{1})"] = "6b - Adota uma seita ({0}/{1})";
            t["7 - Destroy the curse node in the Sanctum ({0}/{1})"] = "7 - Destrói o nó da maldição no Santuário ({0}/{1})";
            t["8a - Claim the monument ({0}/{1})"] = "8a - Conquista o monumento ({0}/{1})";
            t["8b - Explore its ruins ({0}/{1})"] = "8b - Explora as suas ruínas ({0}/{1})";
            t["9 - Win: destroy the rival, enshrine the Shardroot, or hold every Sanctum"] = "9 - Vence: destrói o rival, consagra a Shardroot, ou mantém todos os Santuários";
            t["The camp has grown into a town, and its Litharch is now its king. Use the special building menu at the top of the screen to place the <b>Vault of Almiérra</b>. When it stands, your people become Alanthor."] = "O acampamento cresceu e é agora uma vila, e o seu Litharch é agora o seu rei. Usa o menu de edifícios especiais no topo do ecrã para colocar o <b>Cofre de Almiérra</b>. Quando estiver de pé, o teu povo torna-se Alanthor.";
            t["The Alanthor economy"] = "A economia de Alanthor";
            t["The Alanthor army"] = "O exército de Alanthor";
            t["The Barracks is now the <b>Garrison</b>. You can also build the <b>Archery Range</b>, the <b>Royal Stable</b> and the <b>Siege Yard</b>. Levelling up one building levels every building of that type."] = "O Quartel é agora a <b>Guarnição</b>. Também podes construir a <b>Campo de Tiro com Arco</b>, o <b>Estrebaria Real</b> e o <b>Estaleiro de Cerco</b>. Subir o nível de um edifício sobe o de todos os edifícios desse tipo.";
            t["Defend and expand"] = "Defende e expande";
            t["The waves keep coming. <b>Stone Walls</b> close the way and <b>Battle Towers</b> shoot whatever comes near. To take new ground, stand soldiers in a territory that borders yours until its meter fills, then build there to hold it."] = "As vagas continuam a chegar. As <b>Muralhas de Pedra</b> fecham o caminho e as <b>Torres de Batalha</b> disparam sobre o que se aproxima. Para conquistar terreno, mantém soldados num território vizinho até o medidor encher e depois constrói lá para o manter.";
            t["A rival kingdom"] = "Um reino rival";
            t["Another kingdom holds the east. It will not march on you. When two hostile armies share a territory, the stronger one pushes the meter. For now it stays out of reach."] = "Outro reino domina o leste. Não marchará contra ti. Quando dois exércitos hostis partilham um território, o mais forte empurra o medidor. Por agora, fica fora de alcance.";
            t["Religion Points"] = "Pontos de Religião";
            t["Every curse creature you kill earns religion points, and they turn into Religion Points. They are the key to victory. Keep your army at the walls and let the waves come."] = "Cada criatura da maldição que matas rende pontos, que se transformam em Pontos de Religião. São a chave da vitória. Mantém o exército junto às muralhas e deixa as vagas chegar.";
            t["Build the <b>Temple</b>, then press <b>R</b> to open the religion menu and adopt a sect of the House of Vaelith. A sect gives you powers and its own unit. Press <b>T</b> for the full tree."] = "Constrói o <b>Templo</b>, depois carrega em <b>R</b> para abrir o menu de religião e adota uma seita da Casa de Vaelith. Uma seita dá-te poderes e uma unidade própria. Carrega em <b>T</b> para ver a árvore completa.";
            t["The cursed Sanctum"] = "O Santuário amaldiçoado";
            t["The curse node stands in a Sanctum, marked on the minimap. Select your army, press <b>A</b> and right-click the node. A Sanctum you hold pays Religion Points and shortens your powers' cooldowns."] = "O nó da maldição está num Santuário, assinalado no minimapa. Seleciona o exército, carrega em <b>A</b> e clica com o botão direito no nó. Um Santuário teu paga Pontos de Religião e encurta o tempo de recarga dos teus poderes.";
            t["The monument"] = "O monumento";
            t["A monument stands in this territory. Keep any unit inside its circle to claim it, then select it and pay to <b>Explore</b> its ruins. They grant a buff, and these ones show where the Shardroot lies."] = "Há um monumento neste território. Mantém uma unidade dentro do círculo para o conquistar, depois seleciona-o e paga para <b>Explorar</b> as ruínas. Dão um bónus, e estas mostram onde está a Shardroot.";
            t["The rival is now your enemy. <b>Destroy</b> it. Or send a hero to take the Shardroot, bring it to your Temple and <b>enshrine</b> it for {0}. Or hold <b>every Sanctum</b> for {0}. Choose your road."] = "O rival é agora teu inimigo. <b>Destrói-o</b>. Ou envia um herói buscar a Shardroot, leva-a ao Templo e <b>consagra-a</b> durante {0}. Ou mantém <b>todos os Santuários</b> durante {0}. Escolhe o teu caminho.";
            t["{0} minutes"] = "{0} minutos";
            t["You found lost workers. They join you."] = "Encontraste trabalhadores perdidos. Juntam-se a ti.";
            t["You found lost Spearmen. They join you."] = "Encontraste Lanceiros perdidos. Juntam-se a ti.";
            t["Everyone is found. Look for a place to camp."] = "Estão todos reunidos. Procura um lugar para acampar.";
            t["The camp has a Shelter. Now build it up."] = "O acampamento tem um Abrigo. Agora faz com que cresça.";
            t["Soldiers are lost out there too. Find them, and train more."] = "Também há soldados perdidos lá fora. Encontra-os e treina mais.";
            t["This clearing will do. The camp's stores are yours."] = "Esta clareira serve. As provisões do acampamento são tuas.";
            t["Crystallings! The curse has a node to the east. It must fall."] = "Crystallings! A maldição tem um nó a leste. Tem de cair.";
            t["5 - Survive the curse long enough to vanquish the node"] = "5 - Sobrevive à maldição o tempo suficiente para vencer o nó";
            t["The curse sends a wave."] = "A maldição envia uma vaga.";
            t["Mission complete"] = "Missão concluída";
            t["The node is gone and the camp stands."] = "O nó caiu e o acampamento resiste.";
            t["Mission failed"] = "Missão falhada";
            t["The curse destroyed your Shelter."] = "A maldição destruiu o teu Abrigo.";
            t["1 - Rescue all the workers ({0}/{1})"] = "1 - Salva todos os trabalhadores ({0}/{1})";
            t["2 - Build the Shelter ({0}/{1})"] = "2 - Constrói o Abrigo ({0}/{1})";
            t["3a - Build {1} Huts ({0}/{1})"] = "3a - Constrói {1} Cabanas ({0}/{1})";
            t["3b - Build {1} Gatherer's Huts ({0}/{1})"] = "3b - Constrói {1} Cabanas do Recoletor ({0}/{1})";
            t["3c - Build {0} Veilstone Mine and {1} Iron Mines ({2}/{0}, {3}/{1})"] = "3c - Constrói {0} Mina de Veilstone e {1} Minas de Ferro ({2}/{0}, {3}/{1})";
            t["3d - Build a Barracks ({0}/{1})"] = "3d - Constrói um Quartel ({0}/{1})";
            t["4a - Train Scouts ({0}/{1}) and find the lost Spearmen ({2}/{3})"] = "4a - Treina Batedores ({0}/{1}) e encontra os Lanceiros perdidos ({2}/{3})";
            t["4b - Train {1} Spearmen ({0}/{1})"] = "4b - Treina {1} Lanceiros ({0}/{1})";
            t["5 - Survive the curse long enough to vanquish the node (next wave {0})"] = "5 - Sobrevive à maldição o tempo suficiente para vencer o nó (próxima vaga {0})";
            t["A curse node stands in the east, and it sends a wave every {0} minutes. Keep soldiers near the camp, and train more than you lose. When your army is strong enough, select it, press <b>A</b> and right-click the node. The mission ends when the node falls."] = "Há um nó da maldição a leste, e envia uma vaga a cada {0} minutos. Mantém soldados perto do acampamento e treina mais do que perdes. Quando o teu exército for forte o suficiente, seleciona-o, carrega em <b>A</b> e clica com o botão direito no nó. A missão termina quando o nó cair.";
            t["Muster"] = "Reunir";
            t["Select the <b>Shelter</b> and train Scouts, then send them down the side paths off the clearing to find the lost Spearmen. Select the <b>Barracks</b> to train more. Research there too: <b>Stone Weapons</b> makes every soldier hit harder."] = "Seleciona o <b>Abrigo</b> e treina Batedores, depois envia-os pelos caminhos laterais da clareira para encontrar os Lanceiros perdidos. Seleciona o <b>Quartel</b> para treinar mais. Investiga lá também: <b>Armas de Pedra</b> faz cada soldado bater com mais força.";
            t["After the calamity"] = "Depois da calamidade";
            t["Your Litharch is searching the wilderness for anything that survived. Look around first: use the <b>arrow keys</b>, hold the <b>middle mouse button</b> and drag, or push the cursor to the edge of the screen. The <b>mouse wheel</b> zooms."] = "O teu Litharch procura na floresta tudo o que sobreviveu. Olha primeiro em redor: usa as <b>setas</b>, mantém o <b>botão do meio do rato</b> e arrasta, ou leva o cursor à borda do ecrã. A <b>roda do rato</b> faz zoom.";
            t["Select your Litharch"] = "Seleciona o teu Litharch";
            t["<b>Left-click</b> the Litharch to select them."] = "<b>Clica com o botão esquerdo</b> no Litharch para o selecionar.";
            t["Give an order"] = "Dá uma ordem";
            t["<b>Right-click</b> the ground to walk there. The only way on is the path north through the trees."] = "<b>Clica com o botão direito</b> no chão para caminhar até lá. O único caminho é o trilho para norte, por entre as árvores.";
            t["Hold <b>Shift</b> while you right-click several points along the path. The Litharch walks to each one in turn."] = "Mantém <b>Shift</b> enquanto clicas com o botão direito em vários pontos do trilho. O Litharch caminha até cada um, por ordem.";
            t["Find the workers"] = "Encontra os trabalhadores";
            t["People are lost in these woods. Bring the Litharch close and they will join you."] = "Há pessoas perdidas nesta floresta. Aproxima o Litharch e elas juntam-se a ti.";
            t["A place to camp"] = "Um lugar para acampar";
            t["Everyone is together. Drag a box to select them all, then lead them to the clearing marked on the minimap."] = "Estão todos juntos. Arrasta uma caixa para os selecionar a todos e leva-os até à clareira assinalada no minimapa.";
            t["Build the Shelter"] = "Constrói o Abrigo";
            t["These are your <b>resources</b>, bottom left. This ground is yours now. Select a Worker, choose the <b>Shelter</b> from the build menu and click a spot in the clearing. The more Workers you send, the faster it rises."] = "Estes são os teus <b>recursos</b>, em baixo à esquerda. Este terreno agora é teu. Seleciona um Trabalhador, escolhe o <b>Abrigo</b> no menu de construção e clica num lugar da clareira. Quantos mais Trabalhadores enviares, mais depressa se ergue.";
            t["Build up the camp"] = "Faz crescer o acampamento";
            t["Nobody gathers by hand: a building on a resource node pays you by itself. <b>Gatherer's Huts</b> go on supply nodes. The <b>Mine</b> button goes on iron, and on the crystal outcrop it raises a Veilstone Mine. <b>Huts</b> house your people, and the <b>Barracks</b> trains soldiers."] = "Ninguém recolhe à mão: um edifício num nó de recursos paga-te por si só. As <b>Cabanas do Recoletor</b> vão nos nós de provisões. O botão <b>Mina</b> serve para o ferro e, no afloramento de cristal, ergue uma Mina de Veilstone. As <b>Cabanas</b> alojam a tua gente, e o <b>Quartel</b> treina soldados.";
        }
    }
}
