// ObjectivesRegion — binds the OBJECTIVES panel to live ECS data.
// Mirrors the per-culture victory tracker logic in
// Assets/Scripts/UI/HUD/VictoryProgressHUD.cs but renders into the jade
// objectives panel of GameplayHUD.uxml instead of a top-center IMGUI box.
//
// Live row:  "Cleanse / Convert / Destroy nodes  [pips...]  N/Total"
//   Label switches based on the local player's culture (FactionProgress).
//   Count is the number of nodes whose CrystalNodeState matches the culture's
//   win condition. Pips visualise progress against `ObjectivePipCount` slots —
//   total nodes can exceed pip count, but the visualisation caps at 6 to keep
//   the row width predictable.
//
// Stub row: "Defeat players  0/3"  — placeholder until an inter-player defeat
// objective is actually a game mechanic.

using Unity.Collections;
using Unity.Entities;
using UnityEngine.UIElements;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.Regions
{
    public sealed class ObjectivesRegion
    {
        private const int ObjectivePipCount = 6;

        private readonly Label _nodesLabel;
        private readonly Label _nodesCount;
        private readonly VisualElement _nodesPips;

        private EntityWorld _world;
        private EntityManager _em;
        private EntityQuery _nodeTagQuery;
        private EntityQuery _nodeStateQuery;
        private EntityQuery _factionProgressQuery;
        private bool _queriesReady;

        // Pip elements — created once in the constructor up to ObjectivePipCount.
        private readonly VisualElement[] _pips = new VisualElement[ObjectivePipCount];

        public ObjectivesRegion(VisualElement root)
        {
            _nodesLabel = root.Q<Label>("obj-nodes-label");
            _nodesCount = root.Q<Label>("obj-nodes-count");
            _nodesPips  = root.Q<VisualElement>("obj-nodes-pips");

            if (_nodesPips != null)
            {
                for (int i = 0; i < ObjectivePipCount; i++)
                {
                    var pip = new VisualElement();
                    pip.AddToClassList("tw-pip");
                    pip.pickingMode = PickingMode.Ignore;
                    _nodesPips.Add(pip);
                    _pips[i] = pip;
                }
            }
        }

        public void Refresh()
        {
            if (!EnsureQueries()) return;

            int total = _nodeTagQuery.CalculateEntityCount();
            byte culture = LocalPlayerCulture();
            int claimed = CountClaimedForCulture(culture);

            UpdateLabel(culture);
            UpdateCount(claimed, total);
            UpdatePips(claimed, total);
        }

        private bool EnsureQueries()
        {
            if (_queriesReady && _world != null && _world.IsCreated) return true;
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated) return false;

            _em = _world.EntityManager;
            _nodeTagQuery   = _em.CreateEntityQuery(ComponentType.ReadOnly<CrystalMainNodeTag>());
            _nodeStateQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<CrystalMainNodeTag>(),
                ComponentType.ReadOnly<CrystalNodeState>());
            _factionProgressQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<HallTag>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionProgress>());
            _queriesReady = true;
            return true;
        }

        // Mirrors VictoryProgressHUD.LocalPlayerCulture — look up the local
        // player's Hall and read its FactionProgress.Culture.
        private byte LocalPlayerCulture()
        {
            using var tags = _factionProgressQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var prog = _factionProgressQuery.ToComponentDataArray<FactionProgress>(Allocator.Temp);
            var local = TheWaningBorder.UI.Regions.HudGameSettings.LocalPlayerFaction;
            for (int i = 0; i < tags.Length; i++)
                if (tags[i].Value == local) return prog[i].Culture;
            return Cultures.None;
        }

        private int CountClaimedForCulture(byte culture)
        {
            if (culture == Cultures.None) return 0;
            using var arr = _nodeStateQuery.ToComponentDataArray<CrystalNodeState>(Allocator.Temp);
            int count = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                switch (arr[i].State)
                {
                    case NodeState.Cleansed:
                        if (culture == Cultures.Alanthor && arr[i].OwnerCulture == Cultures.Alanthor) count++;
                        break;
                    case NodeState.Converted:
                        if (culture == Cultures.Runai && arr[i].OwnerCulture == Cultures.Runai) count++;
                        break;
                    case NodeState.Destroyed:
                        // Feraldis credits all destroyed nodes — matches VictoryProgressHUD.
                        if (culture == Cultures.Feraldis) count++;
                        break;
                }
            }
            return count;
        }

        private void UpdateLabel(byte culture)
        {
            if (_nodesLabel == null) return;
            _nodesLabel.text = culture switch
            {
                Cultures.Alanthor => "Cleanse nodes",
                Cultures.Runai    => "Convert nodes",
                Cultures.Feraldis => "Destroy nodes",
                _                 => "Crystal nodes",  // pre-age-up
            };
        }

        private void UpdateCount(int claimed, int total)
        {
            if (_nodesCount != null) _nodesCount.text = claimed + "/" + total;
        }

        private void UpdatePips(int claimed, int total)
        {
            // Pips visualise progress against ObjectivePipCount slots. If the
            // match has more nodes than pip slots, the right-most pip "fills"
            // when claimed is within the last 1/Nth of the total.
            int filled = total <= 0 ? 0
                : System.Math.Min(ObjectivePipCount,
                    (claimed * ObjectivePipCount + total - 1) / total);

            for (int i = 0; i < ObjectivePipCount; i++)
            {
                if (_pips[i] == null) continue;
                if (i < filled) _pips[i].AddToClassList("tw-pip-on");
                else            _pips[i].RemoveFromClassList("tw-pip-on");
            }
        }
    }

    // Internal helper so this file doesn't take a `using` of `TheWaningBorder.UI`
    // proper — keeps the Regions folder a leaf dependency of the controller layer.
    internal static class HudGameSettings
    {
        public static Faction LocalPlayerFaction => GameSettings.LocalPlayerFaction;
    }
}
