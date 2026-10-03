// PackVfxTable.cs
// Which purchased effect goes where — the assignments of
// docs/Design/Vfx_Assignments.md. Each copy lives in the folder of what owns
// it (CLAUDE.md co-location rule): all-unit effects in Age0/Units/Vfx/,
// all-building effects in Shared/Buildings/Vfx/, an ability's effect as the
// <Ability>_Effect prefab beside its AbilityDefSO (what AbilityDefSO.vfxPrefab
// points at). Sources are the vendor paths; PackVfxImport does the copying.

namespace TheWaningBorder.EditorTools
{
    public static class PackVfxTable
    {
        const string Lana = "Assets/Tools/Lana Studio/Casual RPG VFX/Prefabs/";
        const string Hovl = "Assets/MISC/Hovl Studio/Magic effects pack/Prefabs/";
        const string T = "Assets/GameData/TechTree/";
        const string Units = T + "Age0/Units/Vfx/";
        const string Buildings = T + "Shared/Buildings/Vfx/";
        const string Lexor = T + "Civs/Alanthor/Units/KingLexor/Abilities/";
        const string Renewal = T + "Sects/Renewal/Abilities/";
        const string Archery = T + "Civs/Alanthor/Buildings/ArcheryRange/Vfx/";

        public static readonly (string source, string dest)[] Entries =
        {
            // ── Units (§1) — UnitHealVfx, UnitCombatVfx ──
            (Hovl + "Character auras/Healing.prefab", Units + "Unit_Heal.prefab"),
            (Hovl + "Hits and explosions/Holy hit.prefab", Units + "Unit_Hit.prefab"),
            (Hovl + "Hits and explosions/Stones hit.prefab", Units + "Unit_ChargeHit.prefab"),
            (Lana + "Fog/Fog_speedFast.prefab", Units + "Unit_SpeedUp.prefab"),
            (Lana + "Fog/Fog_speedSlow.prefab", Units + "Unit_SlowDown.prefab"),
            (Lana + "Fog/Fog_electric.prefab", Units + "Unit_AttackUp.prefab"),

            // ── Arrow-tip tiers (Archery Range research) — ArrowTrailTiers,
            //    UnitCombatVfx. Stone / Iron are trails only, no prefab. ──
            (Lana + "Range_attack/Projectiles_dark_magic.prefab", Archery + "ArrowTip_Veilstone.prefab"),
            (Lana + "Range_attack/Projectiles_electric.prefab", Archery + "ArrowTip_Veilsteel.prefab"),
            (Lana + "Range_attack/Hit_dark_magic.prefab", Archery + "ArrowHit_Veilstone.prefab"),
            (Lana + "Range_attack/Hit_electric.prefab", Archery + "ArrowHit_Veilsteel.prefab"),

            // ── Buildings (§6) — BuildingEffectSystem ──
            (Hovl + "Smoke effects/Dust loop.prefab", Buildings + "Building_ConstructionDust.prefab"),   // continuous while built
            (Hovl + "Smoke effects/Smoke loop.prefab", Buildings + "Building_DamageSmoke.prefab"),

            // ── The Ledger's buff on the automated building (30 s) ──
            (Lana + "Fog/Fog_speedFast.prefab", T + "Civs/Alanthor/Units/Ledger/Abilities/AutomateFacility/AutomateFacility_Effect.prefab"),

            // ── King Lexor (Veilshift Withdrawal has none of its own: its
            //    slow shows through the unit slow effect) ──
            (Lana + "Orbs/Orbs_gold.prefab", Lexor + "KingsCall/KingsCall_Effect.prefab"),
            (Lana + "Shields/Shield_gold.prefab", Lexor + "LiquidCourage/LiquidCourage_Effect.prefab"),
            (Lana + "Fog/Fog_herts.prefab", Lexor + "LifeCling/LifeCling_Effect.prefab"),
            (Lana + "States/Level_up.prefab", Lexor + "HonourThyPledge/HonourThyPledge_Effect.prefab"),
            (Lana + "Top_down_attack/top_down_lightning_circle_blue.prefab", Lexor + "ShardboundFury/ShardboundFury_Effect.prefab"),

            // ── Sect of Renewal ──
            (Lana + "Regeneration/Regeneration_health_area.prefab", Renewal + "HandsOfPlenty/HandsOfPlenty_Landing.prefab"),
            (Lana + "Regeneration/Regeneration_health_loop.prefab", Renewal + "HandsOfPlenty/HandsOfPlenty_RegenTail.prefab"),
            (Lana + "Burst/Burst_rings.prefab", Renewal + "SecondWind/SecondWind_Landing.prefab"),
            (Lana + "Shields/Shield_wind.prefab", Renewal + "SecondWind/SecondWind_Ward.prefab"),
            (Lana + "Burst/Poof_leaves.prefab", Renewal + "RaiseAnew/RaiseAnew_Landing.prefab"),
            (Lana + "Regeneration/Regeneration_health_area.prefab", Renewal + "DeployFieldHospital/DeployFieldHospital_Effect.prefab"),
            (Lana + "Regeneration/Regeneration_health_area_loop.prefab", Renewal + "DeployFieldHospital/FieldHospital_Aura.prefab"),

            // ── The Shardroot ──
            (Lana + "Loot/Loot_iddle.prefab", T + "Border/Shardroot_Idle.prefab"),
            (Lana + "Loot/Loot_drop.prefab", T + "Border/Shardroot_Drop.prefab"),
            (Lana + "Loot/Loot_pick_up.prefab", T + "Border/Shardroot_PickUp.prefab"),
            (Lana + "Loot/Loot_flicker.prefab", T + "Border/Shardroot_Carried.prefab"),
        };
    }
}
