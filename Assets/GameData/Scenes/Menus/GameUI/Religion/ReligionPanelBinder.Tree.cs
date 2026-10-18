// ReligionPanelBinder.Tree.cs
// The RELIGION TREE (docs/Design/Religion_Menu.md §6): a full-screen
// flowchart opened with T or the menu's Tree button.
//
//   Temple (the gate) → three culture columns (set passive in the header) →
//   four sect cards each: Adopt → A / B / ★ (B and ★ both hang from adoption)
//   → level II → III, and below it the sect building, unit, research and hero.
//
// Node states: owned (cyan), can buy (gold), short of RP (red), not adopted
// (dim). Hovering a node shows its detail card (the shared tooltip) and lights
// up the pieces it pairs with. Clicking a buyable node asks once, in the bar
// at the bottom of the screen.

using System.Collections.Generic;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using TheWaningBorder.Core;
using TheWaningBorder.Core.Commands;
using TheWaningBorder.Core.Localization;
using TheWaningBorder.Data;
using TheWaningBorder.Economy;
using TheWaningBorder.Systems.Sect;

namespace TheWaningBorder.UI.Ingame
{
    public sealed partial class ReligionPanelBinder
    {
        private const float TreeW = 3840f, TreeH = 2160f;
        private const float ColW = 1200f, ColGap = 40f, CardH = 430f, NodeH = 92f;

        private bool _treeOpen;
        private RectTransform _tree;
        private TMP_Text _treeRp, _treeSlots, _treeBar;
        private Button _treeBarOk;
        private System.Action _treeAction;
        private readonly TMP_Text[] _treeSetText = new TMP_Text[3];
        private TMP_Text _treeConcord;
        private readonly List<TreeNode> _nodes = new List<TreeNode>();
        private string _hoverKey;

        private sealed class TreeNode
        {
            public string SectId;
            public string Kind;      // adopt, power, level, building, unit, research, hero
            public int Arg;          // slot or level
            public string Key;       // display name, for "pairs with"
            public Button Button;
            public Image Image;
            public TMP_Text Text;
            public Outline Outline;
        }

        private static readonly Dictionary<string, string[]> Partners = new Dictionary<string, string[]>
        {
            { "Artificer",          new[] { "Archivist of Hours", "Collector" } },
            { "Archivist of Hours", new[] { "Artificer" } },
            { "Collector",          new[] { "Artificer", "Guildmaster" } },
            { "Guildmaster",        new[] { "Collector" } },
            { "Scar Guard",         new[] { "Surgeon", "Lifeline" } },
            { "Surgeon",            new[] { "Scar Guard" } },
            { "Lifeline",           new[] { "Scar Guard", "Surgeon" } },
            { "Sallyguard",         new[] { "Warden" } },
            { "Warden",             new[] { "Sallyguard", "Ward Discharge" } },
            { "Ward Discharge",     new[] { "Warden" } },
        };

        private void BuildTree()
        {
            var canvas = GetComponentInParent<Canvas>();
            var parent = canvas != null ? canvas.transform : _root.parent;
            _tree = GameUIKit.Rect(parent, "ReligionTree");
            _tree.anchorMin = _tree.anchorMax = new Vector2(0.5f, 0.5f);
            _tree.pivot = new Vector2(0.5f, 0.5f);
            _tree.sizeDelta = new Vector2(TreeW, TreeH);
            var bg = GameUIKit.Image(_tree, "Bg", new Color(0.04f, 0.07f, 0.13f, 0.97f), raycast: true);
            GameUIKit.Stretch(bg.rectTransform);

            var title = GameUIKit.Text(_tree, "Title", Loc.T("Religion"), 68f, GameUIKit.Gold);
            Place(title.rectTransform, 96f, 44f, 500f, 100f);
            _treeRp = GameUIKit.Text(_tree, "RP", "", 52f, GoldBadge);
            Place(_treeRp.rectTransform, 520f, 54f, 300f, 80f);
            _treeSlots = GameUIKit.Text(_tree, "Slots", "", 32f, GameUIKit.TextDim);
            Place(_treeSlots.rectTransform, 840f, 70f, 900f, 60f);
            var legend = GameUIKit.Text(_tree, "Legend",
                "<color=#5ED6E6>■</color> " + Loc.T("owned") + "    <color=#F5C451>■</color> " + Loc.T("can buy")
                + "    <color=#FF9E8C>■</color> " + Loc.T("short of RP") + "    <color=#56627C>■</color> " + Loc.T("not adopted")
                + "    <color=#DDE3EE>□</color> " + Loc.T("pairs with"), 28f, GameUIKit.TextDim, TextAlignmentOptions.Right);
            Place(legend.rectTransform, 1800f, 74f, 1820f, 50f);
            var close = MakeButton(_tree, "Close", out _, () => SetTreeOpen(false));
            Place((RectTransform)close.transform, TreeW - 96f - 88f, 48f, 88f, 88f);
            Label(close.transform, "Text", "✕", 44f, GameUIKit.TextMain, TextAlignmentOptions.Center);
            UITooltip.Bind(close.gameObject, Loc.T("Close (T or Esc)"));

            // The Temple and the trunk to the three columns.
            var temple = GameUIKit.Image(_tree, "Temple", CyanFill);
            Place(temple.rectTransform, TreeW * 0.5f - 400f, 180f, 800f, 100f);
            var tt = Label(temple.transform, "Text", Loc.T("Temple of Ridan") + "  <size=70%><color=#A8A38F>"
                + Loc.T("gates adopting, sect buildings and casting") + "</color></size>", 40f, Cyan, TextAlignmentOptions.Center);
            var trunk = GameUIKit.Image(_tree, "Trunk", Cyan);
            // Only the edition's clusters get a column (Alpha_Build.md §4).
            int cols = 0;
            for (int c = 0; c < 3; c++) if (SectConfig.ClusterInEdition((SectCluster)c)) cols++;
            float left = (TreeW - cols * ColW - (cols - 1) * ColGap) * 0.5f;
            Place(trunk.rectTransform, left + ColW * 0.5f, 312f, math.max(4f, (cols - 1) * (ColW + ColGap)), 4f);
            int col = 0;
            Place(GameUIKit.Image(_tree, "TrunkDown", Cyan).rectTransform, TreeW * 0.5f - 2f, 280f, 4f, 34f);

            for (int c = 0; c < 3; c++)
            {
                if (!SectConfig.ClusterInEdition((SectCluster)c)) continue;
                float x = left + col++ * (ColW + ColGap);
                Place(GameUIKit.Image(_tree, "Drop" + c, Cyan).rectTransform, x + ColW * 0.5f - 2f, 312f, 4f, 30f);
                var head = GameUIKit.Text(_tree, "Culture" + c, ClusterName(c), 46f, ClusterColor[c]);
                Place(head.rectTransform, x + 16f, 350f, 500f, 70f);
                _treeSetText[c] = GameUIKit.Text(_tree, "Set" + c, "", 30f, GameUIKit.TextMain, TextAlignmentOptions.Right);
                Place(_treeSetText[c].rectTransform, x + 500f, 362f, ColW - 516f, 50f);
                Place(GameUIKit.Image(_tree, "Rule" + c, ClusterColor[c]).rectTransform, x, 424f, ColW, 4f);

                int n = 0;
                for (int i = 0; i < SectConfig.SectCount; i++)
                {
                    string sectId = SectConfig.IdAt(i);
                    if ((int)SectConfig.ClusterOf(sectId) != c) continue;
                    BuildCard(sectId, x, 444f + n * (CardH + 16f));
                    n++;
                }
            }

            _treeConcord = GameUIKit.Text(_tree, "Concord", "", 32f, GameUIKit.TextDim, TextAlignmentOptions.Center);
            Place(_treeConcord.rectTransform, TreeW * 0.5f - 700f, TreeH - 92f, 1400f, 60f);
            _treeConcord.gameObject.SetActive(SetPassives.InEdition(SectSet.Concord));

            // The confirm bar.
            var bar = GameUIKit.Image(_tree, "Bar", new Color(0.07f, 0.11f, 0.19f, 0.98f), raycast: true);
            Place(bar.rectTransform, TreeW * 0.5f - 900f, TreeH - 220f, 1800f, 110f);
            _treeBar = Label(bar.transform, "Text", "", 32f, GameUIKit.TextMain, TextAlignmentOptions.Left);
            _treeBar.rectTransform.offsetMin = new Vector2(30f, 0f); _treeBar.rectTransform.offsetMax = new Vector2(-260f, 0f);
            _treeBarOk = MakeButton(bar.transform, "OK", out var okbg, () => { var a = _treeAction; HideTreeBar(); a?.Invoke(); });
            okbg.color = GoldBadge;
            var okr = (RectTransform)_treeBarOk.transform;
            okr.anchorMin = okr.anchorMax = new Vector2(1f, 0.5f); okr.pivot = new Vector2(1f, 0.5f);
            okr.anchoredPosition = new Vector2(-130f, 0f); okr.sizeDelta = new Vector2(100f, 90f);
            Label(_treeBarOk.transform, "Text", "✓", 44f, new Color(0.1f, 0.08f, 0.02f), TextAlignmentOptions.Center);
            var no = MakeButton(bar.transform, "Cancel", out var nobg, HideTreeBar);
            nobg.color = GameUIKit.Danger;   // every Cancel is red
            var nor = (RectTransform)no.transform;
            nor.anchorMin = nor.anchorMax = new Vector2(1f, 0.5f); nor.pivot = new Vector2(1f, 0.5f);
            nor.anchoredPosition = new Vector2(-16f, 0f); nor.sizeDelta = new Vector2(100f, 90f);
            Label(no.transform, "Text", "✕", 40f, GameUIKit.TextMain, TextAlignmentOptions.Center);
            bar.gameObject.SetActive(false);

            _tree.gameObject.SetActive(false);
        }

        private void BuildCard(string sectId, float x, float y)
        {
            var card = GameUIKit.Image(_tree, "Card_" + sectId, new Color(0.07f, 0.11f, 0.19f, 0.9f));
            Place(card.rectTransform, x, y, ColW, CardH);
            var name = GameUIKit.Text(card.transform, "Name", SectInfo.ShortName(sectId), 38f, GameUIKit.TextMain);
            Place(name.rectTransform, 24f, 16f, 500f, 56f);
            UITooltip.Bind(card.gameObject, () => AdoptTooltip(sectId));

            // Chain: Adopt → A / B / ★ → II → III, over a trunk line.
            float cy = 96f;
            Place(GameUIKit.Image(card.transform, "Trunk", new Color(0.37f, 0.84f, 0.90f, 0.5f)).rectTransform,
                40f, cy + NodeH * 0.5f - 2f, ColW - 80f, 4f);
            float cx = 24f;
            AddNode(card.transform, sectId, "adopt", 0, Loc.T("Adopt"), cx, cy, 170f); cx += 184f;
            for (int s = 1; s <= 3; s++)
            {
                var spec = SectLeverEffects.ActiveOf(sectId, s, 1);
                string pname = spec.Name ?? "[" + (s == 1 ? "A" : s == 2 ? "B" : "★") + "]";
                AddNode(card.transform, sectId, "power", s, (s == 1 ? "A · " : s == 2 ? "B · " : "★ · ") + pname, cx, cy, 250f, spec.Name);
                cx += 264f;
            }
            AddNode(card.transform, sectId, "level", 2, "II", cx, cy, 90f); cx += 104f;
            AddNode(card.transform, sectId, "level", 3, "III", cx, cy, 90f);

            // The sect building, unit, research and hero.
            float by = 96f + NodeH + 48f;
            float bx = 24f;
            string building = SectConfig.BuildingIdFor(sectId);
            string unit = SectConfig.UnitIdFor(sectId);
            string hero = SectConfig.HeroIdFor(sectId);
            string research = ResearchIdFor(sectId);
            string bName = building != null && TechCatalog.TryGetBuilding(building, out var bd) ? bd.name : "[" + Loc.T("building") + "]";
            string uName = unit != null && TechCatalog.TryGetUnit(unit, out var ud) ? ud.name : "[" + Loc.T("unit") + "]";
            string hName = hero != null && TechCatalog.TryGetUnit(hero, out var hd) ? hd.name : "[" + Loc.T("hero") + "]";
            string rName = research != null ? ResearchName(research) : "[" + Loc.T("research") + "]";
            AddNode(card.transform, sectId, "building", 0, bName, bx, by, 270f); bx += 284f;
            AddNode(card.transform, sectId, "unit", 0, uName, bx, by, 270f, uName); bx += 284f;
            AddNode(card.transform, sectId, "research", 0, rName, bx, by, 270f); bx += 284f;
            AddNode(card.transform, sectId, "hero", 0, hName, bx, by, 290f, hName);
        }

        private void AddNode(Transform parent, string sectId, string kind, int arg, string text,
            float x, float y, float w, string key = null)
        {
            var node = new TreeNode { SectId = sectId, Kind = kind, Arg = arg, Key = key };
            node.Button = MakeButton(parent, "Node_" + kind + arg, out node.Image, () => ClickNode(node));
            Place((RectTransform)node.Button.transform, x, y, w, NodeH);
            node.Text = Label(node.Button.transform, "Text", text, 26f, GameUIKit.TextMain, TextAlignmentOptions.Center);
            node.Text.textWrappingMode = TextWrappingModes.Normal;
            node.Outline = node.Button.gameObject.AddComponent<Outline>();
            node.Outline.effectColor = new Color(0.87f, 0.89f, 0.93f, 0.9f);
            node.Outline.effectDistance = new Vector2(5f, -5f);
            node.Outline.enabled = false;
            var relay = UITooltip.Relay(node.Button.gameObject);
            relay.OnEnter += () => { _hoverKey = node.Key; HighlightPartners(); };
            relay.OnExit += () => { if (_hoverKey == node.Key) { _hoverKey = null; HighlightPartners(); } };
            UITooltip.Bind(node.Button.gameObject, () => NodeTooltip(node));
            _nodes.Add(node);
        }

        private void HighlightPartners()
        {
            string[] partners = null;
            if (_hoverKey != null) Partners.TryGetValue(_hoverKey, out partners);
            foreach (var n in _nodes)
            {
                bool lit = partners != null && n.Key != null && System.Array.IndexOf(partners, n.Key) >= 0;
                n.Outline.enabled = lit;
            }
        }

        private void SetTreeOpen(bool open)
        {
            _treeOpen = open;
            if (_tree == null) return;
            _tree.gameObject.SetActive(open);
            if (open) { _tree.SetAsLastSibling(); ClosePopups(); RefreshTree(); }
            else HideTreeBar();
        }

        private void HideTreeBar()
        {
            _treeAction = null;
            if (_treeBar != null) _treeBar.transform.parent.gameObject.SetActive(false);
        }

        private void RefreshTree()
        {
            if (!_treeOpen || _tree == null || !TryWorld()) return;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape)) { SetTreeOpen(false); return; }
            _treeRp.text = _rpBalance + " " + Loc.T("RP");
            var counts = _hasState ? SetPassives.ClusterCounts(_state) : default;
            int used = counts.x + counts.y + counts.z;
            _treeSlots.text = string.Format(Loc.T("{0} of {1} sects · choices are final"), used, SectConfig.MaxAdoptedSects);
            var active = SetPassives.SetOf(counts);
            for (int c = 0; c < 3; c++)
            {
                if (_treeSetText[c] == null) continue;   // not in this edition
                var set = c == 0 ? SectSet.Alanthor : c == 1 ? SectSet.Runai : SectSet.Feraldis;
                int have = c == 0 ? counts.x : c == 1 ? counts.y : counts.z;
                string name = Loc.T(SetPassives.Name(set));
                _treeSetText[c].text = active == set ? "<color=#F5C451>" + name + " · " + Loc.T("active") + "</color>"
                    : SetPassives.StillReachable(counts, set) ? name + " · " + have + "/4"
                    : "<color=#6E7A93><s>" + name + " · " + have + "/4</s></color>";
            }
            _treeConcord.text = (active == SectSet.Concord ? "<color=#F5C451>" : SetPassives.StillReachable(counts, SectSet.Concord) ? "" : "<color=#6E7A93>")
                + Loc.T("Concord") + " · " + Loc.T("two sects from each culture") + " · " + counts.x + "·" + counts.y + "·" + counts.z;

            foreach (var n in _nodes) RefreshNode(n);
        }

        private void RefreshNode(TreeNode n)
        {
            int idx = SectConfig.IndexOf(n.SectId);
            var sect = _hasState ? _state.Get(idx) : default;
            bool adopted = sect.IsAdopted;
            Color bg = DimCell, fg = new Color(0.55f, 0.6f, 0.7f);
            switch (n.Kind)
            {
                case "adopt":
                    if (adopted) { bg = CyanFill; fg = Cyan; n.Text.text = Loc.T("Adopted"); }
                    else
                    {
                        var r = SectAdoption.CanAdopt(_em, _faction, n.SectId, out int cost);
                        n.Text.text = Loc.T("Adopt") + " · " + cost;
                        if (r == SectAdoptionResult.Ok) { bg = BuyCell; fg = GoldBadge; }
                        else if (r == SectAdoptionResult.NotEnoughRP) { bg = BuyCell; fg = RedBadge; }
                    }
                    break;
                case "power":
                    if (!adopted) break;
                    if (sect.IsSlotUnlocked(n.Arg)) { bg = CyanFill; fg = Cyan; }
                    else
                    {
                        bool can = ReligionPurchases.CanBuy(_em, _faction, ReligionPurchases.UnlockKindFor(n.Arg), n.SectId, out _);
                        bg = BuyCell; fg = can ? GoldBadge : RedBadge;
                    }
                    break;
                case "level":
                    if (!adopted) break;
                    int level = sect.PowerLevel < 1 ? 1 : sect.PowerLevel;
                    if (level >= n.Arg) { bg = CyanFill; fg = Cyan; }
                    else if (level == n.Arg - 1)
                    {
                        bool can = ReligionPurchases.CanBuy(_em, _faction, ReligionPurchaseKind.ChapelLevel, n.SectId, out _);
                        bg = BuyCell; fg = can ? GoldBadge : RedBadge;
                    }
                    break;
                case "building":
                    if (!adopted || SectConfig.BuildingIdFor(n.SectId) == null) break;
                    var st = BuildingState(n.SectId, out _, out _);
                    if (st == SiteState.Standing) { bg = CyanFill; fg = Cyan; }
                    else if (st == SiteState.Destroyed) { bg = Danger; fg = RedBadge; }
                    else { bg = BuyCell; fg = _templeStanding ? GoldBadge : RedBadge; }
                    break;
                case "unit":
                    string unit = SectConfig.UnitIdFor(n.SectId);
                    if (adopted && unit != null && SectHeroes.CountLiveOrQueued(_em, _faction, unit, 1) > 0) { bg = CyanFill; fg = Cyan; }
                    else if (adopted) fg = GameUIKit.TextMain;
                    break;
                case "research":
                    string res = ResearchIdFor(n.SectId);
                    if (adopted && res != null && FactionResearchState.Instance != null
                        && FactionResearchState.Instance.HasResearched(_faction, res)) { bg = CyanFill; fg = Cyan; }
                    else if (adopted) fg = GameUIKit.TextMain;
                    break;
                case "hero":
                    string hero = SectConfig.HeroIdFor(n.SectId);
                    if (adopted && hero != null && FindHero(hero, out int hl) != Unity.Entities.Entity.Null)
                    {
                        bg = CyanFill; fg = Cyan;
                    }
                    else if (adopted) fg = GameUIKit.TextMain;
                    break;
            }
            n.Image.color = bg;
            n.Text.color = fg;
        }

        private string NodeTooltip(TreeNode n)
        {
            switch (n.Kind)
            {
                case "adopt": return AdoptTooltip(n.SectId);
                case "power":
                {
                    var spec = SectLeverEffects.ActiveOf(n.SectId, n.Arg, TryWorld() ? SetPassives.EffectivePowerLevel(_em, _faction, n.SectId) : 1);
                    if (spec.Name == null) return Loc.T("Not authored yet for this sect.");
                    string pairs = PairsLine(spec.Name);
                    return "<b>" + Loc.T(spec.Name) + "</b>\n" + (spec.Description != null ? Loc.T(spec.Description) : "")
                        + "\n<color=#A8A38F>" + Loc.T("Reach") + ": " + SectRadii.Label(spec.Reach) + "   " + Loc.T("Cooldown") + ": "
                        + Mathf.RoundToInt(spec.Cooldown) + "s</color>" + pairs;
                }
                case "level": return string.Format(Loc.T("Chapel level {0}: every unlocked power of the sect is cast at this level."), n.Arg == 2 ? "II" : "III");
                case "building": return SectInfo.BuildingDescription(n.SectId);
                case "unit": return (SectInfo.SectUnitText(n.SectId) ?? Loc.T("Not authored yet for this sect.")) + PairsLine(n.Key);
                case "research": return SectInfo.ResearchText(n.SectId) ?? SectInfo.TechnologyDescription(n.SectId);
                case "hero": return (SectInfo.HeroText(n.SectId) ?? Loc.T("Not authored yet for this sect.")) + PairsLine(n.Key);
            }
            return "";
        }

        private static string PairsLine(string key)
        {
            if (key == null || !Partners.TryGetValue(key, out var p)) return "";
            return "\n<color=#DDE3EE>" + Loc.T("Pairs with") + " " + string.Join(", ", p) + "</color>";
        }

        private void ClickNode(TreeNode n)
        {
            if (!TryWorld()) return;
            switch (n.Kind)
            {
                case "adopt":
                    if (SectAdoption.CanAdopt(_em, _faction, n.SectId, out int cost) != SectAdoptionResult.Ok) return;
                    AskTree(string.Format("<b>{0}</b> <color=#F5C451>· {1} RP</color>   <color=#A8A38F>{2}</color>",
                        Loc.T("Adopt") + " " + SectInfo.ShortName(n.SectId), cost, Loc.T("Final, cannot be undone")), () =>
                        {
                            if (TryWorld() && TryGetTemple(out var temple)
                                && SectAdoption.ValidateAdoption(_em, _faction, n.SectId) == SectAdoptionResult.Ok)
                                CommandRouter.IssueSectAdoption(_em, temple, n.SectId);
                        });
                    break;
                case "power":
                    if (SectActivePowerHelper.IsSlotUnlocked(_em, _faction, n.SectId, n.Arg)) return;
                    AskPurchase(ReligionPurchases.UnlockKindFor(n.Arg), n.SectId, n.Text.text);
                    break;
                case "level":
                    if (SectQuery.PowerLevelOf(_em, _faction, n.SectId) != n.Arg - 1) return;
                    AskPurchase(ReligionPurchaseKind.ChapelLevel, n.SectId, Loc.T("Chapel level") + " " + (n.Arg == 2 ? "II" : "III"));
                    break;
                case "building":
                    if (!SectQuery.IsAdopted(_em, _faction, n.SectId)) return;
                    var st = BuildingState(n.SectId, out var b, out _);
                    if (st == SiteState.Standing || st == SiteState.Building) { SetTreeOpen(false); Select(b); }
                    else if (_templeStanding && SectConfig.BuildingIdFor(n.SectId) is string id)
                    { SetTreeOpen(false); WorkerCommandPanel.TriggerBuildingPlacement(id); }
                    break;
            }
        }

        private void AskPurchase(ReligionPurchaseKind kind, string sectId, string what)
        {
            if (!ReligionPurchases.CanBuy(_em, _faction, kind, sectId, out int rp))
            {
                PlayerNotificationSystem.NotifyError(Loc.T("Not enough Religion Points"));
                return;
            }
            AskTree(string.Format("<b>{0}</b> <color=#F5C451>· {1} RP</color>   <color=#A8A38F>{2}</color>",
                what, rp, Loc.T("Final, cannot be undone")), () =>
                {
                    if (TryWorld()) CommandRouter.IssueReligionPurchase(_em, _faction, kind, sectId);
                });
        }

        private void AskTree(string text, System.Action action)
        {
            _treeBar.text = text;
            _treeAction = action;
            _treeBar.transform.parent.gameObject.SetActive(true);
        }
    }
}
