// HeadlessLastSeenTest.cs
// Tests docs/Design/Last_Seen_Buildings.md against the real fog and view pipeline,
// on the Basic tutorial scene with a hostile building placed by hand.
//
//   "The Waning Border.exe" -twbLastSeenTest
//
// Exit 0 = every check passed, 3 = one failed. Each check logs [LastSeenTest].

using System;
using System.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;
using TheWaningBorder.Core;
using TheWaningBorder.Entities;
using TheWaningBorder.Rendering;
using TheWaningBorder.Systems.Sect;
using TheWaningBorder.Tutorial;
using TheWaningBorder.UI.Data;
using TheWaningBorder.UI.Menus;
using TheWaningBorder.World.FogOfWar;

namespace TheWaningBorder.Bootstrap
{
    public sealed class HeadlessLastSeenTest : MonoBehaviour
    {
        private const float RevealRadius = 22f;
        private const float RevealSeconds = 3f;
        private const float Settle = 2f;

        private int _failures;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (!CommandLine.Has(Environment.GetCommandLineArgs(), "-twbLastSeenTest")) return;
            var go = new GameObject("HeadlessLastSeenTest");
            DontDestroyOnLoad(go);
            go.AddComponent<HeadlessLastSeenTest>().StartCoroutine(go.GetComponent<HeadlessLastSeenTest>().Run());
        }

        private void Check(string what, bool ok)
        {
            Debug.Log($"[LastSeenTest] {(ok ? "PASS" : "FAIL")} {what}");
            if (!ok) _failures++;
        }

        private static bool ViewShows(Entity e)
            => EntityViewManager.Instance.TryGetView(e, out var go) && go != null && go.activeSelf;

        private IEnumerator Run()
        {
            SceneManager.LoadScene(TutorialMenuItem.Prepare());
            while (!MatchLifecycle.MapPopulated || FogOfWarManager.Instance == null) yield return null;
            yield return new WaitForSecondsRealtime(3f);

            var em = Unity.Entities.World.DefaultGameObjectInjectionWorld.EntityManager;
            var me = GameSettings.LocalPlayerFaction;
            var cfg = TutorialMissionSystemConfig.I;
            var spot = TutorialMissionSystem.Ground(cfg.campSpot + new Vector2(30f, 0f));
            var beside = TutorialMissionSystem.Ground(cfg.campSpot + new Vector2(42f, 0f));

            // Never seen.
            var first = BuildingFactory.Create(em, "Hut", spot, Faction.Red);
            yield return new WaitForSecondsRealtime(Settle);
            Check("a hostile building on unexplored ground is hidden", !ViewShows(first));
            Check("...and unknown", !LastSeenBuildings.IsKnown(first));

            // Seen.
            SectActivePowerHelper.SpawnReveal(em, me, spot, RevealRadius, RevealSeconds);
            yield return new WaitForSecondsRealtime(Settle);
            Check("direct vision shows it", ViewShows(first));
            Check("...live, not frozen", !LastSeenBuildings.IsFrozen(first));

            // Out of vision again.
            yield return new WaitForSecondsRealtime(RevealSeconds + Settle);
            Check("the ground is explored but out of vision",
                FogOfWarManager.Instance.IsRevealed(me, spot) && !FogOfWarManager.Instance.IsVisible(me, spot));
            Check("out of vision it still shows", ViewShows(first));
            Check("...frozen", LastSeenBuildings.IsFrozen(first));
            Check("...with no live health in the panel",
                !EntityInfoExtractor.GetDisplayInfo(first, em).CurrentHealth.HasValue);

            // An upgrade out of vision.
            string nameBefore = EntityInfoExtractor.GetSelectionDisplayName(first, em);
            // An Age 0 Hut has no level yet, so one is added: the name reads the
            // live level unless the memory stops it.
            if (!em.HasComponent<BuildingUpgradeState>(first))
                em.AddComponentData(first, new BuildingUpgradeState());
            var up = em.GetComponentData<BuildingUpgradeState>(first);
            up.Level += 2;
            em.SetComponentData(first, up);
            yield return new WaitForSecondsRealtime(Settle);
            Check($"an upgrade out of vision does not change its name ({nameBefore})",
                EntityInfoExtractor.GetSelectionDisplayName(first, em) == nameBefore);

            // A new building on explored ground: the reported bug.
            var second = BuildingFactory.Create(em, "Hut", beside, Faction.Red);
            yield return new WaitForSecondsRealtime(Settle);
            Check("a new building on explored ground stays hidden", !ViewShows(second));

            // Destroyed out of vision.
            var health = em.GetComponentData<Health>(first);
            health.Value = 0;
            em.SetComponentData(first, health);
            float wait = Time.realtimeSinceStartup + 15f;
            while (em.Exists(first) && Time.realtimeSinceStartup < wait) yield return null;
            yield return new WaitForSecondsRealtime(Settle);
            Check("the destroyed building's entity is gone", !em.Exists(first));
            Check("...and its model stays as a ghost", LastSeenBuildings.Ghosts.Count == 1
                && LastSeenBuildings.Ghosts[0].View != null && LastSeenBuildings.Ghosts[0].View.activeSelf);

            // Vision again.
            SectActivePowerHelper.SpawnReveal(em, me, (spot + beside) * 0.5f, RevealRadius + 12f, RevealSeconds);
            yield return new WaitForSecondsRealtime(Settle);
            Check("regained vision clears the ghost", LastSeenBuildings.Ghosts.Count == 0);
            Check("...and shows the new building", ViewShows(second));
            Check("...live", !LastSeenBuildings.IsFrozen(second));

            Debug.Log($"[LastSeenTest] DONE failures={_failures}");
            Application.Quit(_failures == 0 ? 0 : 3);
        }
    }
}
