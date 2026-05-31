# Media & Marketing Plan — Shardroot / The Waning Border

> Concrete pre-announce plan for a **bootstrap-budget, solo-dev, pre-
> announce-stage RTS** in 2026. Designed around `shardroot.com` and
> `info@shardroot.com` as the central hub. Read this top-to-bottom once,
> then come back to specific sections as you execute.

**Status today (May 2026):** Pre-announce. No public devlog, no Steam
page, no social presence. Studio name (Shardroot) and domain registered.
Game is playable with placeholder art.

**Scope decision (2026-05-26):** First public release is a **free Steam
demo featuring only the Alanthor culture** (Runai + Feraldis deferred to
full release). See [05_Demo_Scope.md](05_Demo_Scope.md) for the demo's
content boundaries. **This is a marketing gift, not a constraint** — the
two-cultures-later structure becomes a year-long content engine. Each
culture reveal becomes its own marketing chapter (see [§ The three-
chapter culture-reveal arc](#the-three-chapter-culture-reveal-arc)
below).

**Target wishlist range at launch:** 10k+ is the viable minimum for an
indie RTS; 50k+ marks the publisher-interest threshold (Fractured
Alliance benchmark). With the three-chapter arc giving us three reveals
to drive wishlists (instead of one announcement burst), the path to 10k+
is materially easier. *(Sources: see end of doc.)*

---

## The three-chapter culture-reveal arc

The Alanthor-first demo turns the marketing campaign from a single
reveal-then-launch arc into a **three-chapter story** with built-in
re-engagement moments. Each chapter has its own audience peak.

| Chapter | Trigger event | Wishlist push duration | Devlog topics |
|---------|---------------|------------------------|---------------|
| **1. Alanthor demo** | Steam page + demo go live together | ~6-8 weeks | Walls, sects, Crystal Curse, Glow, "what's coming" tease |
| **2. Runai reveal** | When Runai is playable (full game beta or paid early access) | ~4-6 weeks | Caravan transit-spike, trader-warrior networks, "no walls" identity, Acolyte's Conversion ritual |
| **3. Feraldis reveal** | Full game launch lead-up | ~6-8 weeks | Pillage / damage-as-income, Houses spawning raiders, Veilsteel Frenzy, Iconoclast |

**Why this works:**
- Three separate "first impressions" instead of one — every chapter
  re-acquires lapsed audience
- Each chapter has a **legitimate news hook** (a new culture is real
  playable content, not a "soon™" video)
- Press and streamers get **three coverage windows** per game, not one
- Wishlist velocity compounds: chapter 2 lifts chapter 1's numbers
  retroactively as people who weren't ready in chapter 1 come back
- The constraint *is* the differentiator — "an asymmetric RTS that
  ships its cultures one at a time, each rebalancing the meta" is a
  marketing story in itself

**Discipline rules for the arc:**
- Never describe locked cultures as "missing" or "limited." They are
  "coming chapters."
- Every Alanthor screenshot includes the **"Coming next: Runai"**
  watermark in a corner (small, ~5% screen real estate)
- The Runai reveal moment should be a **separate trailer** with its own
  YouTube upload — not a patch note. Re-press, re-stream, re-post on
  Bluesky as if it's a new game.
- Hold back at least 30% of each culture's mechanics from public reveal
  until that culture's chapter. Don't burn the Veilsteel Frenzy story
  in chapter 1; save it for chapter 3.

---

## Guiding principles

1. **Don't break stealth until the Steam page works.** Wishlists need a
   destination. Posting "look at my game" with nowhere to wishlist burns
   audience momentum that's hard to rebuild. Every public step assumes
   the wishlist button is one click away.
2. **One asset, many uses.** Bootstrap means every screenshot, gif, and
   sketch ships to ~5 places. Plan asset creation around what each one
   feeds: Steam page, devlog post, Bluesky thread, YouTube Short, press
   pitch.
3. **The mechanic IS the marketing.** Three-way movement asymmetry is
   the strongest hook. Every public asset should make the
   wall-vs-raid-vs-lane triangle legible in 5 seconds. Don't lead with
   generic "asymmetric RTS" — show the triangle.
4. **Devlog cadence beats devlog frequency.** Monthly milestones with a
   reason to post (a feature, a culture reveal, a fight, a tech, a
   number) beat weekly noise. Press and algorithm both reward rhythm.

---

## Phase 0 — Foundation (do first, ~2-3 weeks)

Goal: have a place to send people. Nothing public yet, but the
infrastructure to go public exists.

### `shardroot.com` minimum viable site

A **single landing page** with:
- Studio name, one-sentence game pitch, hero image (placeholder OK)
- Email capture (newsletter / "get notified" — Buttondown or MailerLite
  free tier; both have GDPR-clean templates)
- Discord invite (create the server even if empty — see below)
- `info@shardroot.com` for press / collaboration
- Social links (Bluesky, YouTube, Twitter/X — even if dormant)

**Tech recommendation:** static site (HTML/CSS or Astro/11ty) hosted on
Netlify or Cloudflare Pages, both free. Avoid WordPress for a
single-pager — overkill and slower. If you want a one-day solution,
Carrd ($19/year) gives you a one-page site with email capture out of
the box.

### Discord server setup

Even before anyone joins, create:
- `#announcements` (announcement channel, read-only)
- `#general` (chat)
- `#devlog` (long-form updates auto-posted from the website / RSS)
- `#feedback` (bug reports, suggestions)
- `#art-and-screenshots` (you'll dump WIP here as you build)
- `#crystal-curse-lore` (worldbuilding teasers when you're ready)

Pin the game pitch in `#announcements`. Set up a simple "welcome"
auto-message via Carl-bot or MEE6 (free tiers). Discord becomes the
**retention layer** — once people opt in here, you can re-engage them
forever, unlike algorithmic platforms.

### Email list (Buttondown recommended)

Newsletter beats every other channel for conversion-to-wishlist when the
Steam page lands. Buttondown free tier covers up to 100 subscribers,
$9/mo after. **Set this up before any public post.** A "join the mailing
list" CTA at the top of every devlog is the single highest-ROI move you
can make for free.

### Social handles to register *now* (even if not posting yet)

- Bluesky: `@shardroot.bsky.social` (or custom: `@shardroot.com` via
  domain handle — free, ~5 min setup via DNS TXT record)
- Twitter/X: `@shardroot` (use even if you barely post — for press
  contact and discovery search)
- YouTube: `@shardroot` channel
- TikTok: `@shardroot` (claim, post sparingly)
- itch.io: `shardroot` (you may release a free demo here later)
- Instagram: `@shardroot.games` (concept art only, low priority)

Register everything in one afternoon. **Domain-handle Bluesky** (the
`@shardroot.com` form) reads as serious and you don't want to be stuck
with a generic handle later.

### Press kit folder (build empty, fill as you go)

`shardroot.com/press` — a single page with:
- Studio name + bio (2 paragraphs)
- Game name, genre, platform (PC / Steam), release window ("TBA 2027" or
  whatever your honest answer is)
- One-paragraph pitch + USP bullets
- Logo (PNG + SVG, transparent backgrounds)
- 5-10 screenshots
- 1-3 trailers
- Press contact: `info@shardroot.com`
- "Made with" credits
- Steam page link (when it exists)

Empty today, full by Phase 2. **Use [presskit()](https://dopresskit.com)
or [Indie Press Kit](https://indiepresskit.dev)** — both free, both
deliver a journalist-ready page in an hour.

---

## Phase 1 — Stealth devlogging (months 1-3)

Goal: build a backlog of devlog content **before going public**. Three
posts ready in the can means month 1 of public launch already has
content scheduled.

### Pick three devlog beats to develop in stealth

These should each take ~2-4 weeks of game-dev work to "have something to
show," and each should be visual:

1. **The triangle reveal** — a side-by-side gif of an Alanthor wall
   compartment, a Runai caravan in transit, and a Feraldis raid party.
   30 seconds, three thumbnails. *This is your single most important
   marketing asset until you have key art.*
2. **The Crystal Curse spreading** — a timelapse of cursed ground
   creeping across a map. Players will want to see the PvE layer.
3. **One culture deep-dive** — pick the most polished faction (probably
   Alanthor given the wall system just landed) and do a 5-minute video
   on its identity, walls, and play loop.

### Capture pipeline (set up once, use forever)

- **OBS Studio** — free, lossless capture at game native res
- **DaVinci Resolve** — free pro NLE; better than Premiere for indies
- **FFmpeg** — for batch gif/mp4 conversion in scripts
- **ShareX** — free Windows screenshot/gif tool; one-keystroke share
- **Topaz Video AI** *(optional, paid)* — upscaling old captures

Set up a **screenshot folder** (`F:/Marketing/Captures/YYYY-MM-DD/`)
that you'll dump everything into. Tag with feature name in the
filename. Future-you will thank present-you.

### Three rules for stealth devlog content

1. **No "coming soon" without a date.** Vague teasers train your future
   audience to ignore you.
2. **Every visual has the studio watermark** (small, bottom-right
   `shardroot.com`) — when one inevitably leaks or gets reposted, the
   credit comes with it.
3. **Build the Steam page assets in parallel.** See Phase 2.

---

## Phase 2 — Steam page + reveal + demo (month 4-6)

**This is the most important phase.** The Steam page is the wishlist
funnel, and the demo is the conversion mechanism. Both go live together.
Until they exist, everything else is throat-clearing.

**Critical:** the **demo and the Steam page launch on the same day**.
Steam policy requires the demo to be attached to a base-game page; you
cannot ship a demo independently. The Alanthor demo is *the* reveal
artifact — playable content from the first marketing day. This is rare
and powerful — most indie RTS reveals show video; you'll show a
download. Lean into it.

### Steam page requirements checklist

Steam now requires (per Valve's 2026 guidelines):
- Capsule art (header, small, main capsule, vertical) — see art brief
- 5+ screenshots at native res
- 1 trailer (60-90 seconds, no narration required, gameplay first 5s)
- Description (short + long form, 4-5 paragraphs)
- Genre + tag selection (do this carefully — RTS, Base Building,
  Strategy, Real Time Tactics, Asymmetric, Singleplayer, Indie)
- Coming Soon release date or "TBA"
- Required Steamworks setup (Steam Direct $100 one-time, then free)

**Time to wishlist live: ~5 business days for Valve review** once
submitted. Plan accordingly.

### Capsule art is the single most-viewed asset

Steam's main capsule art appears everywhere — search, friend
recommendations, store front. **Invest here first** if you're going to
pay for any single asset. Industry rate: $250-1500 depending on artist
tier. This goes in the art hiring brief as priority #1.

If genuinely $0 budget: brief a student or trade for credit, but accept
the cost is iteration. Plan to commission a paid replacement before
launch.

### Steam demo specifics

- Demo is a **separate Steam product** in Steamworks, linked to the
  main game page. ~5 business days of Valve review for the demo too.
- Demo build size: keep under 2GB if possible. Exclude unused
  Runai/Feraldis assets at build level (per
  [05_Demo_Scope.md § Strip from build](05_Demo_Scope.md#strip-from-build)).
- **Demo entitlement design:** simplest version — free, no time limit,
  no feature gates beyond the locked cultures (already locked via UI).
  Steam's free-demo plumbing handles the rest.
- Steam page DEMO button appears prominently once the demo product is
  approved.
- Wishlist tracking: each free demo download adds a +1 to the demo's
  follower count; the base game's wishlist count rises separately when
  players click WISHLIST in the locked-culture screen (see
  [06_Locked_Culture_UI_Spec.md](06_Locked_Culture_UI_Spec.md)).

### Reveal day choreography

Pick a day you'll commit to. ~2 weeks out:
1. Press kit complete and live at `shardroot.com/press`
2. Discord invite working, server populated with the team (you,
   collaborators, friends) — empty Discord is worse than no Discord
3. Newsletter live with a real welcome email
4. Bluesky / Twitter pre-loaded with reveal thread drafts (5-7 posts each)
5. YouTube channel with the reveal trailer **scheduled** (private, set
   to go public at reveal time)
6. Reddit accounts warmed up (post-history on `r/RealTimeStrategy`,
   `r/gamedev`, etc. — empty accounts get auto-spam-filtered)
7. Personal contacts in indie press warned 48h ahead (see press list
   section)

Reveal day itself:
- 9:00 — Steam page goes live (you toggle it) **— with the demo button**
- 9:05 — YouTube trailer goes public, embed on `shardroot.com`. Trailer
  ends on "Play the demo today" CTA
- 9:10 — Bluesky + Twitter reveal threads post; link to demo in post 1
  ("Play it now"), wishlist in post 2
- 9:30 — Reddit posts on `r/RealTimeStrategy`, `r/IndieGaming`,
  `r/IndieDev`, `r/Unity3D` (NOT r/gamedev — that's no-self-promotion).
  Lead with "we shipped a demo" — playable content is the strongest
  Reddit hook
- 11:00 — Discord opens to public (server invite goes everywhere). Set
  up a `#demo-feedback` channel before opening — early players will
  immediately want somewhere to post
- Throughout the day — reply to every comment, every tag, every quote-
  reply. Algorithms reward engagement-on-engagement.
- End of day — newsletter blast to the list ("we just revealed + the
  demo is live")
- **Day +1 to +7**: monitor the demo's Steam reviews if/when they
  appear. Reply publicly to each one within 48h. Even bad reviews get
  thoughtful, non-defensive responses — visible developer engagement
  converts other readers into wishlist-clicks.

**Reveal day rule:** plan to do nothing else for 12 hours. Cancel meetings.

---

## Phase 3 — Sustaining cadence (months 7+, ongoing until launch)

Goal: monthly content beat that compounds wishlists.

### Monthly devlog rhythm (recommended)

| Week | What | Where |
|---|---|---|
| Week 1 | Long-form devlog post (1500-3000 words + 5 visuals) | `shardroot.com/devlog` + newsletter blast |
| Week 2 | Short-form video cut from the devlog (60-90s) | YouTube Shorts + TikTok + Bluesky video |
| Week 3 | Behind-the-scenes / dev-process post (concept art, code peek, design rationale) | Bluesky + Twitter thread |
| Week 4 | Community spotlight or Q&A / Discord-driven post | Discord first, then social cross-post |

**Why this cadence works:** one big creative push per month (the devlog)
seeds three weeks of derivative content. You're not creating content
weekly — you're *reusing* monthly content weekly.

### Platform mix (2026-tuned)

| Platform | Role | Effort | Notes |
|---|---|---|---|
| **`shardroot.com/devlog`** | Owned hub. RSS-feedable. | High per post, low maintenance | Linkable from everywhere, search-indexed, never deplatforms you. |
| **Email newsletter** | Highest-converting channel. | Low (cross-post devlog) | A 1k-subscriber list converts to wishlists at 20-40%. |
| **Discord** | Retention + feedback. | Moderate (daily presence) | Where your most engaged 100 fans live. |
| **YouTube** | Devlog video + Shorts. Algorithmic stability is best in 2026. | High (videos take time) | Long-form devlog + the cut-Short = double dip. |
| **Bluesky** | Dev-to-dev conversation, press discovery. | Low | Domain handle reads serious. Skeets > tweets in 2026 for indie dev community. |
| **Twitter/X** | Press contact, hashtag visibility (#gamedev, #indiedev, #RTS, #screenshotsaturday). | Low-moderate | Still where press lives. Conversion is meh, but discovery is real. |
| **TikTok** | Burst content, NOT main channel. | Variable | Organic reach collapsed in 2025-26. Post if a video happens to be portrait-format-friendly; don't structure your strategy around it. |
| **Reddit** | Periodic posts on milestone moments. | Low | r/RealTimeStrategy is small but dedicated. Don't spam. |
| **YouTube Shorts** | The 2026 indie wishlist driver. | Moderate | Vertical cuts of devlog footage. Often outperforms TikTok. |
| **Instagram** | Concept art, screenshots. | Low | Optional. Skip if time-constrained. |

### Hashtags worth using (consistently)

- `#gamedev` (dev-facing audience, low conversion but high reach)
- `#indiedev` (same)
- `#indiegame` (player-facing)
- `#RTS` (genre-fans search this)
- `#screenshotsaturday` (Saturdays, near-mandatory for indie devs)
- `#wishlistwednesday` (Wednesdays, on Steam-page-live days)
- `#madewithunity` (Unity boosts, occasional retweets from their account)
- `#crystalcurse` (start using this early — your unique IP tag)

### What to post about (a year of devlog topics, in rough order)

1. **The triangle** — three factions, one map, three relationships to
   movement. Lead with this; it's the hook.
2. **Alanthor walls** — the BFME2 hub-and-segment system is rare in
   modern RTS. Show the auto-segment formation, the hub-death cascade.
3. **Runai caravans in transit** — the transit-spike is a unique
   economy mechanic; gif-friendly.
4. **Feraldis raid pressure** — Houses spawning raiders, Pillage
   economy. Show the autonomous violence loop.
5. **The Crystal Curse spreading** — PvE layer; foreboding screenshots.
6. **Glow as one-shot finite resource** — explain the late-game
   bottleneck; players will theorycraft.
7. **Petriarchy sect picks** — once balance settles, reveal sects
   one-per-week. Twelve weeks of content for free.
8. **Religious unit tier** — Scholar, Acolyte, Iconoclast. Three videos.
9. **DOTS/ECS scaling** — "look how many units" videos historically
   convert RTS fans well. 1000+ units on screen is a Bluesky-worthy
   screenshot.
10. **A real fight** — record a full 1v1 match against the AI. Cut
    highlights into a video.
11. **Modding / map editor reveal** — if any of this exists.
12. **Demo announcement** — if you do a Next Fest.

### Steam Next Fest

Steam Next Fest is the single biggest free wishlist-driver. Two windows
per year (typically February and October). **Aim to participate once,
4-6 weeks before launch**, not earlier — the demo has to be in genuinely
good shape, because Next Fest visibility brings critique as well as
wishlists.

Preparation checklist: a polished 30-60 minute demo, livestream slots
booked across the Next Fest week (Valve increasingly weights games that
livestream during the event), social pre-promotion 3 weeks out, Discord
on-call during the week.

---

## Phase 4 — Pre-launch (3 months out from launch)

- Press list outreach (see template below)
- Streamer outreach — focus on micro-influencers (1k-10k concurrent
  viewers) over megastars. Conversion is dramatically better, key codes
  are cheap.
- Cross-promo with adjacent indie RTSes (Battle Aces dev community,
  Stormgate dev community, Fractured Alliance team) — gamedev is
  collaborative
- "Final approach" newsletter cadence — weekly emails in the last month
- Launch trailer (different from reveal trailer — show *more*)
- Day-1 patch ready in CI

## Phase 5 — Launch + post-launch

Out of scope for this plan. Re-evaluate.

---

## The press / streamer outreach template

Copy-paste-ready. Personalize the *italicized* fields per recipient.

```
Subject: The Waning Border — a three-way asymmetric RTS with a playable Alanthor demo

Hi *[NAME]*,

I'm Luis from Shardroot. I'm building The Waning Border — a real-time
strategy game where three cultures share one starting age, then diverge
into three opposite relationships with movement on the map: Alanthor
denies it with hub-and-segment walls, Runai embodies it with caravan
trade-lanes that auto-spawn patrolling trader-warriors, and Feraldis
preys on it through raids and a "damage = income" pillage economy.

We're doing something unusual on the release side: shipping one
culture at a time. The Alanthor demo is live now on Steam — full
defensive-wall culture, six of twelve religious sects, and the Crystal
Curse PvE layer. Runai and Feraldis arrive as chapters two and three.

I noticed *[specific thing about their channel/site, e.g., "your recent
Tempest Rising coverage" / "your asymmetric RTS retrospective"]* and
thought you might find the staggered-reveal approach interesting —
especially the BFME2-style wall system, which is rare in modern RTS.

Demo (free): [Steam page link]
Press kit: shardroot.com/press
Discord: [link]

Happy to send a key, jump on a call, or share a short gameplay clip —
whichever fits your workflow.

Thanks for the work you do for the genre.
— Luis Martins
   info@shardroot.com
```

**Send rules:**
- One personalization sentence is non-negotiable. Mass-blasted templates
  get filtered.
- Send between Tuesday and Thursday, between 9-11 AM in recipient's
  timezone.
- Follow up ONCE, 7-10 days later. Then stop.
- Press list builds slowly — start with 5-10 contacts, learn, expand.

### Press / streamer starter list for indie RTS (research these)

- **RockPaperShotgun** — `hello@rockpapershotgun.com` (RTS coverage)
- **PC Gamer** — `pcg-tips@futurenet.com`
- **IndieGameMag**, **GamingOnLinux**, **Indie DB**
- **Tortuga Power**, **Beaglerush**, **Lowko** *(via Twitter DM —
  StarCraft / RTS streamers)*
- **Many A True Nerd** *(longer-tail strategy game coverage)*
- **Quill18** *(strategy-focused YouTube)*
- **JorRaptor**, **Skill Up** *(general indie game coverage)*

Each of these needs to be verified-and-active when you actually pitch —
the indie game press landscape churns. The list above is a starter.

---

## Concrete next 30 days

If you're starting from zero today, do these in order:

| Day | Action | Time |
|---|---|---|
| 1-2 | Register all social handles in one sitting | 2h |
| 3-4 | Spin up `shardroot.com` landing page (Carrd or static HTML) | 4h |
| 5 | Set up Buttondown newsletter; create welcome email | 1h |
| 6-7 | Set up Discord; populate channels; invite 3-5 friends to seed | 2h |
| 8-10 | Capture pipeline setup (OBS, Resolve, ShareX, capture folder) | 3h |
| 11-14 | Capture footage of the three faction identities in current build | 6h |
| 15-18 | Cut a 30-second "triangle reveal" gif/video — save for later | 4h |
| 19-21 | Start the art hiring search (see [03_Art_Hiring_Brief.md](03_Art_Hiring_Brief.md) and [04_Where_To_Find_Artists.md](04_Where_To_Find_Artists.md)) | 6h |
| 22-25 | Write devlog post #1 (do not publish) | 6h |
| 26-30 | Write devlog post #2 in stealth; rest | 6h |

By day 30, you have: a landing page, a newsletter, a Discord, all your
social handles, two devlog posts in the can, a reveal gif, and an
active art search. **You have not posted publicly yet.** That's intentional.

---

## What to *not* do

- Don't make a Kickstarter without 10k+ wishlists and a $50k+ marketing
  budget. The crowdfunding moment for indie RTS passed in ~2018; modern
  Kickstarters fail more often than succeed.
- Don't pay for TikTok ads. ROI is bad for RTS specifically.
- Don't enter every Steam festival. Pick one Next Fest, hit it hard.
- Don't keep iterating on the Steam description forever. Ship v1,
  improve quarterly.
- Don't compare yourself to Stormgate or Tempest Rising's wishlist
  numbers. Their teams are 30+ devs with publisher backing. Your
  comparable is a solo or small-team indie RTS hitting 10-25k.
- Don't reveal Petriarchy sects all at once — they're 12 free weeks of
  content.
- Don't agonize over the studio logo before the Steam page is live. A
  serviceable logo today beats a perfect logo six months from now.

---

## Sources / further reading

- [Roadmap to an Effective Indie Game Marketing Strategy in 2026](https://www.game-developers.org/roadmap-to-an-effective-indie-game-marketing-strategy-in-2026)
- [Indie Game Marketing: The Complete 2026 Guide for Developers — Fungies.io](https://fungies.io/indie-game-marketing/)
- [Fractured Alliance: 50,000 wishlists case study](https://www.gamespress.com/en-US/Indie-RTS-Fractured-Alliance-Surpasses-50000-Steam-Wishlists-as-Publis)
- [Steam Marketing Guide for Indie Devs — Indieformer](https://www.indieformer.com/steam-marketing-guide)
- [Steam Next Fest Marketing — Big Games Machine](https://www.biggamesmachine.com/steam-next-fest-marketing-strategies/)
- [Indie Game Distribution & UA Painpoints 2025-2026 — Metricus](https://metricusapp.com/blog/indie-game-distribution-user-acquisition-painpoints-2025-2026/)

---

*Last updated: 2026-05-26. Owner: Luis Martins, Shardroot.*
