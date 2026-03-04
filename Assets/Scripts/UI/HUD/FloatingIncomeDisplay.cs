// FloatingIncomeDisplay.cs
// Shows floating "+N" text above income-generating buildings when they tick
// BFME2-style income visualization
// Location: Assets/Scripts/UI/HUD/FloatingIncomeDisplay.cs

using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Transforms;
using Unity.Collections;
using TheWaningBorder.Economy;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.UI.HUD
{
    /// <summary>
    /// Spawns floating "+N" text above income-generating buildings when they tick.
    /// Detects ticks by watching the SuppliesIncome.Elapsed timer reset.
    /// </summary>
    public class FloatingIncomeDisplay : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private float floatHeight = 1.5f;
        [SerializeField] private float floatDuration = 1.5f;
        [SerializeField] private int poolSize = 20;

        // Object pool for floating text
        private readonly List<FloatingText> _pool = new();
        private readonly List<FloatingText> _active = new();

        // Track per-entity elapsed to detect tick resets
        private readonly Dictionary<Entity, float> _prevElapsed = new();

        // ECS refs
        private EntityWorld _world;
        private EntityManager _em;
        private EntityQuery _incomeQuery;

        private struct FloatingText
        {
            public GameObject GO;
            public TextMesh Mesh;
            public float Elapsed;
            public Vector3 StartPos;
        }

        void Start()
        {
            _world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (_world == null || !_world.IsCreated) return;
            _em = _world.EntityManager;

            _incomeQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<SuppliesIncome>(),
                ComponentType.ReadOnly<FactionTag>(),
                ComponentType.ReadOnly<LocalTransform>());

            // Pre-populate pool
            for (int i = 0; i < poolSize; i++)
            {
                var go = CreateTextObject();
                go.SetActive(false);
                _pool.Add(new FloatingText { GO = go, Mesh = go.GetComponent<TextMesh>() });
            }
        }

        void Update()
        {
            if (_world == null || !_world.IsCreated) return;

            // Update active floating texts
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var ft = _active[i];
                ft.Elapsed += Time.deltaTime;

                if (ft.Elapsed >= floatDuration)
                {
                    ft.GO.SetActive(false);
                    _pool.Add(ft);
                    _active.RemoveAt(i);
                    continue;
                }

                float t = ft.Elapsed / floatDuration;
                float y = Mathf.Lerp(0, floatHeight, t);
                ft.GO.transform.position = ft.StartPos + Vector3.up * y;

                // Fade out alpha
                var color = ft.Mesh.color;
                color.a = 1f - t;
                ft.Mesh.color = color;

                // Billboard: face camera
                if (Camera.main != null)
                    ft.GO.transform.rotation = Camera.main.transform.rotation;

                _active[i] = ft;
            }

            // Check each building for tick events
            DetectAndShowTicks();
        }

        private void DetectAndShowTicks()
        {
            var localFaction = GameSettings.LocalPlayerFaction;

            using var entities = _incomeQuery.ToEntityArray(Allocator.Temp);
            using var factions = _incomeQuery.ToComponentDataArray<FactionTag>(Allocator.Temp);
            using var transforms = _incomeQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
            using var incomes = _incomeQuery.ToComponentDataArray<SuppliesIncome>(Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                if (factions[i].Value != localFaction) continue;
                if (_em.HasComponent<UnderConstruction>(entities[i])) continue;

                var income = incomes[i];
                if (income.PerTick <= 0f || income.Interval <= 0f) continue;

                float elapsed = income.Elapsed;
                var entity = entities[i];

                if (_prevElapsed.TryGetValue(entity, out float prev))
                {
                    // Detect timer reset (elapsed wrapped around to less than prev)
                    if (elapsed < prev - 0.1f)
                    {
                        int amount = (int)income.PerTick;
                        if (amount > 0)
                        {
                            Vector3 pos = (Vector3)transforms[i].Position + Vector3.up * 2f;
                            SpawnText($"+{amount}", pos, new Color(1f, 0.85f, 0.4f));
                        }
                    }
                }

                _prevElapsed[entity] = elapsed;
            }
        }

        private void SpawnText(string text, Vector3 position, Color color)
        {
            FloatingText ft;
            if (_pool.Count > 0)
            {
                ft = _pool[_pool.Count - 1];
                _pool.RemoveAt(_pool.Count - 1);
            }
            else
            {
                var go = CreateTextObject();
                ft = new FloatingText { GO = go, Mesh = go.GetComponent<TextMesh>() };
            }

            ft.GO.SetActive(true);
            ft.GO.transform.position = position;
            ft.Mesh.text = text;
            ft.Mesh.color = color;
            ft.Elapsed = 0f;
            ft.StartPos = position;

            _active.Add(ft);
        }

        private GameObject CreateTextObject()
        {
            var go = new GameObject("FloatingIncome");
            var tm = go.AddComponent<TextMesh>();
            tm.fontSize = 32;
            tm.characterSize = 0.15f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = new Color(1f, 0.85f, 0.4f, 1f);
            return go;
        }
    }
}
