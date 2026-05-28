// ResourcesRegion — binds the <ui:Label name="*-value"/> elements inside the
// bottom-left resources panel to live ECS data. Mirrors the polling pattern in
// the IMGUI Assets/Scripts/UI/HUD/ResourceHUD.cs so the visible refresh rate
// matches what players are used to (default 0.25s).
//
// Queries (all read-only):
//   FactionTag + FactionResources       → Supplies / Iron / Crystal / Veilsteel / Glow
//   FactionTag + FactionPopulation      → Current / Max
//   FactionTag + FactionReligionPoints  → Balance
//
// In observer mode, the displayed faction follows the player's selection — same
// rule as ResourceHUD.GetDisplayedFaction so the UX is consistent.

using Unity.Collections;
using Unity.Entities;
using UnityEngine.UIElements;
using TheWaningBorder.Economy;
using TheWaningBorder.Input;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.Regions
{
    public sealed class ResourcesRegion
    {
        private readonly Label _population;
        private readonly Label _religion;
        private readonly Label _supplies;
        private readonly Label _iron;
        private readonly Label _crystal;
        private readonly Label _veilsteel;
        private readonly Label _glow;

        private EntityWorld _world;
        private EntityManager _em;
        private EntityQuery _banksQuery;
        private EntityQuery _popQuery;
        private EntityQuery _rpQuery;
        private bool _queriesReady;

        public ResourcesRegion(VisualElement root)
        {
            _population = root.Q<Label>("population-value");
            _religion   = root.Q<Label>("religion-value");
            _supplies   = root.Q<Label>("supplies-value");
            _iron       = root.Q<Label>("iron-value");
            _crystal    = root.Q<Label>("crystal-value");
            _veilsteel  = root.Q<Label>("veilsteel-value");
            _glow       = root.Q<Label>("glow-value");
        }

        public void Refresh()
        {
            if (!EnsureQueries()) return;

            var displayed = GetDisplayedFaction();
            UpdateResources(displayed);
            UpdatePopulation(displayed);
            UpdateReligion(displayed);
        }

        private bool EnsureQueries()
        {
            if (_queriesReady && _world != null && _world.IsCreated) return true;
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated) return false;

            _em = _world.EntityManager;
            _banksQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionResources>());
            _popQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionPopulation>());
            _rpQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<FactionReligionPoints>());
            _queriesReady = true;
            return true;
        }

        private Faction GetDisplayedFaction()
        {
            // Observer mode follows the player's selection — mirrors ResourceHUD.cs.
            if (GameSettings.IsObserver)
            {
                var sel = SelectionSystem.CurrentSelection;
                if (sel != null && sel.Count > 0)
                {
                    for (int i = 0; i < sel.Count; i++)
                    {
                        var e = sel[i];
                        if (_em.Exists(e) && _em.HasComponent<FactionTag>(e))
                            return _em.GetComponentData<FactionTag>(e).Value;
                    }
                }
            }
            return GameSettings.LocalPlayerFaction;
        }

        private void UpdateResources(Faction faction)
        {
            using var tags  = _banksQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var banks = _banksQuery.ToComponentDataArray<FactionResources>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                var r = banks[i];
                if (_supplies  != null) _supplies.text  = r.Supplies.ToString();
                if (_iron      != null) _iron.text      = r.Iron.ToString();
                if (_crystal   != null) _crystal.text   = r.Crystal.ToString();
                if (_veilsteel != null) _veilsteel.text = r.Veilsteel.ToString();
                if (_glow      != null) _glow.text      = r.Glow.ToString();
                return;
            }
        }

        private void UpdatePopulation(Faction faction)
        {
            using var tags = _popQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var pops = _popQuery.ToComponentDataArray<FactionPopulation>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                if (_population != null)
                    _population.text = pops[i].Current + "/" + pops[i].Max;
                return;
            }
        }

        private void UpdateReligion(Faction faction)
        {
            using var tags = _rpQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var rp   = _rpQuery.ToComponentDataArray<FactionReligionPoints>(Allocator.Temp);
            for (int i = 0; i < tags.Length; i++)
            {
                if (tags[i].Value != faction) continue;
                if (_religion != null) _religion.text = rp[i].Balance.ToString();
                return;
            }
        }
    }
}
