// CrystallingGait.cs
// The Crystalling's body is a rock-and-crystal quadruped with no skeleton and
// no clips: this component walks it. Each paw is planted in the WORLD and
// stays there while the body passes over it, then steps (diagonal pairs, a
// trot) to where the body is going; two-segment legs are solved to the paws
// every frame. Where a paw lands a crystal juts out of the ground and
// dissolves a second later (CrystallingFootprints).
//
// Presentation only: it reads the view's own transform (which the spawn
// system pins to the entity) and, for the attack lunge, the entity's
// AttackCooldown. The prefab is authored by
// Waning Border > Art > Units > Author Crystalling, which fills the
// serialized references below.

using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core;

namespace TheWaningBorder.Rendering
{
    public sealed class CrystallingGait : MonoBehaviour
    {
        // ── Authored on the prefab ──
        public Transform body;
        /// <summary>Front-left, front-right, back-left, back-right.</summary>
        public Transform[] hips;
        public Transform[] uppers;
        public Transform[] lowers;
        /// <summary>Where each paw rests, in root space (y = 0).</summary>
        public Vector3[] pawHome;
        public float upperLength;
        public float lowerLength;
        public Mesh footprintMesh;
        public Material footprintMaterial;

        // ── Gait shape (engine-side presentation constants) ──
        const float Stride = 0.62f;          // metres a paw covers in one step
        const float StepTrigger = 0.30f;     // a paw this far behind its rest point steps
        const float MaxStepTime = 0.16f;
        const float MinStepTime = 0.055f;
        const float StepLift = 0.20f;
        const float IdleSettle = 0.08f;      // standing: a paw further than this re-plants
        const float LungeTime = 0.32f;
        const float LungeReach = 0.34f;
        const float TeleportDistance = 3f;

        readonly Vector3[] _planted = new Vector3[4];
        readonly Vector3[] _from = new Vector3[4];
        readonly Vector3[] _to = new Vector3[4];
        readonly float[] _stepT = new float[4];     // < 0 = planted
        readonly float[] _stepDur = new float[4];
        readonly Vector3[] _paw = new Vector3[4];

        Vector3 _lastPos;
        Vector3 _velocity;
        Vector3 _bodyRest;
        float _bodyBob;
        bool _started;

        EntityReference _link;
        float _lastCdTimer = float.MaxValue;
        float _lunge = -1f;
        int _frame;

        void OnEnable() { _started = false; }

        void Init()
        {
            _started = true;
            _lastPos = transform.position;
            _velocity = Vector3.zero;
            if (body != null) _bodyRest = body.localPosition;
            for (int i = 0; i < 4; i++)
            {
                _planted[i] = Ground(transform.TransformPoint(pawHome[i]));
                _paw[i] = _planted[i];
                _stepT[i] = -1f;
            }
            _frame = GetInstanceID() & 3;
        }

        void LateUpdate()
        {
            if (body == null || hips == null || hips.Length != 4) return;
            if (!_started) Init();

            float dt = Time.deltaTime;
            Vector3 pos = transform.position;
            Vector3 moved = pos - _lastPos;
            _lastPos = pos;

            // A teleport (spawn pin, fog re-show): put the paws under the body.
            if (moved.sqrMagnitude > TeleportDistance * TeleportDistance) { Init(); return; }

            if (dt > 1e-5f)
            {
                Vector3 v = moved / dt; v.y = 0f;
                _velocity = Vector3.Lerp(_velocity, v, Mathf.Clamp01(dt * 14f));
            }
            float speed = _velocity.magnitude;
            Vector3 heading = speed > 0.05f ? _velocity / speed : transform.forward;

            // Off screen: keep the paws under the body, draw nothing new.
            if (!ProceduralRigCulling.Visible(pos, 2.5f))
            {
                for (int i = 0; i < 4; i++) { _planted[i] = transform.TransformPoint(pawHome[i]); _stepT[i] = -1f; }
                return;
            }

            ReadAttack();

            bool pairAStepping = _stepT[0] >= 0f || _stepT[3] >= 0f;
            bool pairBStepping = _stepT[1] >= 0f || _stepT[2] >= 0f;
            float lift = 0f;

            for (int i = 0; i < 4; i++)
            {
                Vector3 home = transform.TransformPoint(pawHome[i]);

                if (_stepT[i] < 0f)
                {
                    Vector3 lag = home - _planted[i]; lag.y = 0f;
                    bool otherPairBusy = (i == 0 || i == 3) ? pairBStepping : pairAStepping;
                    float trigger = speed > 0.3f ? StepTrigger : IdleSettle;
                    if (lag.magnitude > trigger && !otherPairBusy)
                    {
                        _from[i] = _planted[i];
                        float reach = speed > 0.3f ? Stride - StepTrigger : 0f;
                        _stepDur[i] = speed > 0.3f
                            ? Mathf.Clamp(Stride / speed * 0.5f, MinStepTime, MaxStepTime)
                            : MaxStepTime;
                        // Land ahead of the rest point by the reach, plus what
                        // the body covers while the paw is in the air.
                        _to[i] = Ground(home + heading * reach + _velocity * _stepDur[i]);
                        _stepT[i] = 0f;
                        if (i == 0 || i == 3) pairAStepping = true; else pairBStepping = true;
                    }
                    _paw[i] = _planted[i];
                }

                if (_stepT[i] >= 0f)
                {
                    _stepT[i] += dt / _stepDur[i];
                    float t = Mathf.Clamp01(_stepT[i]);
                    float arc = Mathf.Sin(t * Mathf.PI);
                    _paw[i] = Vector3.Lerp(_from[i], _to[i], t) + Vector3.up * (arc * StepLift);
                    lift += arc;
                    if (_stepT[i] >= 1f)
                    {
                        _stepT[i] = -1f;
                        _planted[i] = _to[i];
                        _paw[i] = _to[i];
                        if (speed > 0.3f && footprintMesh != null)
                            CrystallingFootprints.Spawn(footprintMesh, footprintMaterial, _to[i],
                                transform.rotation.eulerAngles.y + i * 67f, transform.lossyScale.x);
                    }
                }
            }

            // Body: a small bounce with the steps, a lean into the run, the lunge.
            _bodyBob = Mathf.Lerp(_bodyBob, lift * 0.035f, Mathf.Clamp01(dt * 18f));
            float lungeK = 0f;
            if (_lunge >= 0f)
            {
                _lunge += dt / LungeTime;
                if (_lunge >= 1f) _lunge = -1f;
                else lungeK = Mathf.Sin(Mathf.Clamp01(_lunge) * Mathf.PI);
            }
            float breathe = speed < 0.3f ? Mathf.Sin(Time.time * 2.3f + GetInstanceID() * 0.37f) * 0.012f : 0f;
            body.localPosition = _bodyRest + new Vector3(0f, _bodyBob + breathe - lungeK * 0.06f, lungeK * LungeReach);
            body.localRotation = Quaternion.Euler(Mathf.Min(speed, 7f) * 1.4f + lungeK * 16f, 0f, 0f);

            for (int i = 0; i < 4; i++) SolveLeg(i);
        }

        void SolveLeg(int i)
        {
            Vector3 hip = hips[i].position;
            Vector3 toPaw = _paw[i] - hip;
            float scale = transform.lossyScale.x;
            float a = upperLength * scale, b = lowerLength * scale;
            float d = Mathf.Clamp(toPaw.magnitude, Mathf.Abs(a - b) + 0.01f, a + b - 0.005f);
            Vector3 dir = toPaw.sqrMagnitude > 1e-8f ? toPaw.normalized : Vector3.down;

            // Knees up and out, like a crouched predator.
            Vector3 side = transform.right * (pawHome[i].x < 0f ? -1f : 1f);
            Vector3 pole = transform.up * 0.75f + side * 0.55f - transform.forward * (pawHome[i].z < 0f ? -0.35f : 0.35f);
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            perp = perp.sqrMagnitude > 1e-8f ? perp.normalized : transform.up;

            float x = (a * a - b * b + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, a * a - x * x));
            Vector3 knee = hip + dir * x + perp * h;
            Vector3 paw = hip + dir * d;

            uppers[i].SetPositionAndRotation(hip, Quaternion.FromToRotation(Vector3.up, knee - hip));
            lowers[i].SetPositionAndRotation(knee, Quaternion.FromToRotation(Vector3.up, paw - knee));
        }

        // The cooldown timer jumping back up is a swing: lunge with it.
        void ReadAttack()
        {
            if ((++_frame & 3) != 0) return;
            if (_link == null) _link = GetComponent<EntityReference>();
            if (_link == null || _link.Entity == Entity.Null) return;
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;
            if (!em.Exists(_link.Entity) || !em.HasComponent<AttackCooldown>(_link.Entity)) return;
            float timer = em.GetComponentData<AttackCooldown>(_link.Entity).Timer;
            if (timer > _lastCdTimer + 0.01f && _lunge < 0f) _lunge = 0f;
            _lastCdTimer = timer;
        }

        static Vector3 Ground(Vector3 p)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                float y = terrain.SampleHeight(p) + terrain.transform.position.y;
                // A unit on a wall deck or a bridge stands above the terrain:
                // keep its paws at its own level there.
                if (Mathf.Abs(y - p.y) < 1.2f) p.y = y;
            }
            return p;
        }
    }
}
