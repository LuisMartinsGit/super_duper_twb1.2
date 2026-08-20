// HutEvolutionDriver.cs
// Scenario-only timeline driver for ScenarioType.HutEvolution. The hut
// self-constructs with no workers (AutoConstructionSystem owns the build;
// the numbered Lv0 rise plays as normal), then every N seconds the driver
// plays the next step of the full Guild evolution, every switch using the
// building-upgrade DISSOLVE wave:
//
//    1  construction (Lv0)          8  Veilstone Surveying I
//    2  upgrade to Lv1              9  upgrade to Lv3
//    3  Iron Reinforcements        10  Veilsteel Pylons (no look yet)
//    4  Iron Surveying I           11  Iron Surveying III
//    5  upgrade to Lv2             12  Veilstone Surveying II
//    6  Veilstone Walls            13  Veilsteel Surveying
//    7  Iron Surveying II          14  DAMAGE PHASE (see below)
//
// Damage phase: one interval after the last tech, the hut starts draining
// 5% max HP per second (DebugBuildingDamageTarget). The ward researches
// are completed at that moment so the Guild's defensive casts fire live:
// the SLOW AOE pops at 75% HP, the STOP AOE (NovaStorm field) at 50%, and
// the drain carries through to the death collapse. IronReinforcements is
// deliberately NOT completed — its auto-repair would fight the drain.
//
// Tech steps go through BuildingVariantVisual.ShowTechVisual, so the
// replacement chains play out exactly like live research: Veilstone Walls
// dissolves wall_low away, each survey tier dissolves out the tier below.
//
// While running, the RTS camera controller is suspended and the camera
// orbits the hut slowly at HALF the RTS viewing distance (same elevation),
// so every angle of every transition gets captured. Controller restored on
// completion.
// Location: Assets/GameData/TechTree/Buildings/Age 0/Hut/HutEvolutionDriver.cs

using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace TheWaningBorder.Presentation
{
    public class HutEvolutionDriver : MonoBehaviour
    {
        private Entity _hut;
        private byte _culture = Cultures.Alanthor;
        private float _interval = 5f;

        private bool _constructionDone;
        private float _timer;
        private int _step;
        private bool _damageStarted;
        private bool _deathObserved;
        private Vector3 _lastHutPos;

        // (level, null) = branch switch; (0, techId) = tech reveal.
        private static readonly (int level, string techId)[] Steps =
        {
            (1, null),                    //  2 upgrade to Lv1
            (0, "IronReinforcements"),    //  3
            (0, "IronSurveying1"),        //  4
            (2, null),                    //  5 upgrade to Lv2
            (0, "VeilstoneWalls"),        //  6 (dissolves wall_low out)
            (0, "IronSurveying2"),        //  7 (dissolves tier I out)
            (0, "VeilstoneSurvey1"),      //  8
            (3, null),                    //  9 upgrade to Lv3
            (0, "VeilsteelPylons"),       // 10 (no authored look yet)
            (0, "IronSurveying3"),        // 11 (dissolves tier II out)
            (0, "VeilstoneSurvey2"),      // 12 (dissolves tier I out)
            (0, "VeilsteelSurvey"),       // 13
        };

        // Camera orbit: near-horizontal so the SIDES of the building fill
        // the frame (the RTS elevation looks down too steeply for review).
        private const float OrbitDegreesPerSecond = 9f;
        private const float OrbitElevationDegrees = 18f;
        private TheWaningBorder.Input.CameraController _suspendedController;
        private Camera _camera;
        private bool _orbitReady;
        private float _orbitAngle;
        private float _orbitRadiusXZ;
        private float _orbitHeight;
        private float _savedFarClip;

        // A near-horizontal camera sees clear to the horizon — with scenario
        // fog of war off, the whole map's terrain/props render and the frame
        // rate collapses. The subject is one building; nothing beyond this
        // needs to draw during the orbit.
        private const float OrbitFarClip = 150f;

        public void Configure(Entity hut, byte culture, float upgradeInterval)
        {
            _hut = hut;
            _culture = culture;
            _interval = Mathf.Max(0.1f, upgradeInterval);
        }

        void Update()
        {
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            if (_hut == Entity.Null || !em.Exists(_hut))
            {
                // Died (damage phase end): keep orbiting the wreck for a few
                // seconds so the collapse + burn play out on camera.
                if (!_deathObserved)
                {
                    _deathObserved = true;
                    Invoke(nameof(Finish), 4f);
                }
                OrbitAround(_lastHutPos);
                return;
            }

            UpdateOrbit(em);

            if (!_constructionDone)
            {
                // AutoConstructionSystem owns the build timer; the interval
                // clock starts the frame UnderConstruction disappears.
                if (em.HasComponent<UnderConstruction>(_hut)) return;
                _constructionDone = true;
                _timer = 0f;
                return;
            }

            if (_step >= Steps.Length)
            {
                // All evolution steps done: one more interval, then the
                // damage phase runs to the death collapse.
                if (!_damageStarted)
                {
                    _timer += Time.deltaTime;
                    if (_timer >= _interval)
                    {
                        _timer = 0f;
                        StartDamagePhase(em);
                    }
                }
                return;
            }

            _timer += Time.deltaTime;
            if (_timer < _interval) return;
            _timer = 0f;

            var (level, techId) = Steps[_step];
            _step++;

            var view = EntityViewManager.Instance != null
                ? EntityViewManager.Instance.GetView(_hut) : null;
            if (view == null) return;

            var variant = view.GetComponent<BuildingVariantVisual>();
            if (variant == null)
            {
                TWBLog.Log("[HutEvolution] view has no BuildingVariantVisual — " +
                    "is the multi-variant prefab (Lv0 + culture branches) assigned on the SO?");
                return;
            }

            Color accent = em.HasComponent<FactionTag>(_hut)
                ? FactionColors.Get(em.GetComponentData<FactionTag>(_hut).Value)
                : new Color(1f, 0.85f, 0.45f);

            bool changed = level > 0
                ? variant.ShowVariantWithTransition(_culture, level, accent)
                : variant.ShowTechVisual(techId, accent, withTransition: true);

            if (changed)
            {
                BuildingLevelUpEffect.Spawn(view, accent);
            }
            else if (techId != null)
            {
                // Expected for VeilsteelPylons until it gets a model — mark
                // the step with the flourish alone so the beat still reads.
                BuildingLevelUpEffect.Spawn(view, accent);
                TWBLog.Log($"[HutEvolution] no visual node for {techId} in the current branch");
            }

        }

        // Arm the wards and start the 5%/s drain. The cast system reads the
        // research flags live, so completing the two ward techs HERE (and
        // not at scenario start) keeps the evolution timeline untouched.
        private void StartDamagePhase(EntityManager em)
        {
            _damageStarted = true;

            var research = TheWaningBorder.Economy.FactionResearchState.Instance;
            if (research != null && em.HasComponent<FactionTag>(_hut))
            {
                var fac = em.GetComponentData<FactionTag>(_hut).Value;
                research.CompleteResearch(fac, "VeilstoneWalls");   // Slow ward, pops at 75% HP
                research.CompleteResearch(fac, "VeilsteelPylons");  // Stop ward, pops at 50% HP
                // IronReinforcements deliberately NOT completed — auto-repair
                // would fight the drain and stall the showcase.
            }

            if (!em.HasComponent<DebugBuildingDamageTarget>(_hut))
                em.AddComponentData(_hut, new DebugBuildingDamageTarget());

            TWBLog.Log("[HutEvolution] damage phase: 5%/s drain — Slow AOE at 75%, Stop AOE at 50%, collapse at 0");
        }

        // Slow orbit around the hut at HALF the RTS camera's viewing
        // distance, keeping the RTS elevation angle. Set up on the first
        // frame the camera and hut both exist.
        private void UpdateOrbit(EntityManager em)
        {
            if (!em.HasComponent<LocalTransform>(_hut)) return;
            Vector3 hutPos = em.GetComponentData<LocalTransform>(_hut).Position;
            _lastHutPos = hutPos;

            if (!_orbitReady)
            {
                _camera = TheWaningBorder.Input.GameCamera.MainCamera;
                if (_camera == null) return;

                Vector3 toCam = _camera.transform.position - hutPos;
                float rtsDistance = toCam.magnitude;
                if (rtsDistance < 1f) return; // camera not placed yet

                float orbitDistance = rtsDistance * 0.5f;
                float elevation = OrbitElevationDegrees * Mathf.Deg2Rad;
                _orbitRadiusXZ = orbitDistance * Mathf.Cos(elevation);
                _orbitHeight = orbitDistance * Mathf.Sin(elevation);
                _orbitAngle = Mathf.Atan2(toCam.z, toCam.x) * Mathf.Rad2Deg;

                _suspendedController = TheWaningBorder.Input.GameCamera.Controller;
                if (_suspendedController != null) _suspendedController.enabled = false;
                _savedFarClip = _camera.farClipPlane;
                _camera.farClipPlane = Mathf.Min(_savedFarClip, OrbitFarClip);
                _orbitReady = true;
            }

            OrbitAround(hutPos);
        }

        private void OrbitAround(Vector3 center)
        {
            if (!_orbitReady || _camera == null) return;

            _orbitAngle += OrbitDegreesPerSecond * Time.deltaTime;
            float rad = _orbitAngle * Mathf.Deg2Rad;
            var camPos = center + new Vector3(
                Mathf.Cos(rad) * _orbitRadiusXZ, _orbitHeight, Mathf.Sin(rad) * _orbitRadiusXZ);
            _camera.transform.position = camPos;
            _camera.transform.LookAt(center + Vector3.up * 2f);
        }

        private void Finish()
        {
            RestoreCamera();
            Destroy(gameObject);
        }

        void OnDestroy()
        {
            RestoreCamera();
        }

        private void RestoreCamera()
        {
            if (_suspendedController != null) _suspendedController.enabled = true;
            if (_orbitReady && _camera != null && _savedFarClip > 0f)
                _camera.farClipPlane = _savedFarClip;
        }
    }
}
