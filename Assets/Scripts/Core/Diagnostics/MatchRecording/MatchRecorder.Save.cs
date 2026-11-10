// MatchRecorder.Save.cs
// The match record inside a saved game. docs/Design/Replays_And_Saves.md §3
//
// The record lives in memory and starts over whenever a match boots — which a
// load is. Without this the Game Review of a loaded match showed only what was
// played since the load. The record is written as a snapshot section
// ("matchrecord") and adopted by the recorder of the loaded match, so the
// timeline runs from the start of the match.
//
// ENTITIES. A restored world hands out new entity indices, so the tracks of
// units and buildings still alive at the save are re-keyed through their
// NetworkedEntity ids, exactly as SnapshotCodec re-keys entity fields. A live
// track whose entity has no id (or is gone) simply ends at the next sample.
//
// WHEN IT IS ADOPTED. The snapshot is restored by the bootstrap; the recorder
// may not have seen the new match epoch yet (it is a MonoBehaviour on the
// runtime managers object). The bytes wait in a static and Step adopts them
// right after its own new-match reset.
//
// Old saves have no such section: their review starts at the load, as before.

using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using TheWaningBorder.Core.Maps;
using TheWaningBorder.Core.Multiplayer;
using EntityWorld = Unity.Entities.World;

namespace TheWaningBorder.Core.Diagnostics.MatchRecording
{
    public sealed partial class MatchRecorder
    {
        const string SectionKey = "matchrecord";
        const int SectionVersion = 1;
        const int NoId = int.MinValue;

        /// <summary>A saved record waiting for the recorder of the loaded match.</summary>
        static byte[] _pendingRestore;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RegisterSnapshotSection()
        {
            _pendingRestore = null;
            TheWaningBorder.Core.Save.SnapshotState.RegisterSection(SectionKey, SaveSection, LoadSection);
        }

        static byte[] SaveSection()
        {
            var me = Instance;
            if (me == null || me._rec == null || !me._started) return null;
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                me.WriteRecord(w, world.EntityManager);
            return ms.ToArray();
        }

        static void LoadSection(byte[] data) => _pendingRestore = data;

        // ── write ────────────────────────────────────────────────────────

        void WriteRecord(BinaryWriter w, EntityManager em)
        {
            var r = _rec;
            w.Write(SectionVersion);

            // header
            w.Write(r.MapScene ?? "");
            w.Write(r.MapName ?? "");
            w.Write(r.WorldMin.x); w.Write(r.WorldMin.y);
            w.Write(r.WorldMax.x); w.Write(r.WorldMax.y);
            w.Write(MatchRecord.Factions);
            for (int f = 0; f < MatchRecord.Factions; f++)
            {
                var c = r.FactionColor[f];
                w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
                w.Write(r.FactionPresent[f]);
            }

            // outcome
            w.Write(r.Duration);
            w.Write(r.Ended);
            w.Write(r.Winner ?? "");

            // map
            w.Write(r.RegionList.Count);
            foreach (var x in r.RegionList) { w.Write(x.Name ?? ""); w.Write(x.X); w.Write(x.Z); }
            w.Write(r.NodeList.Count);
            foreach (var x in r.NodeList) { w.Write(x.Kind ?? ""); w.Write(x.X); w.Write(x.Z); }
            w.Write(r.OwnerEventList.Count);
            foreach (var x in r.OwnerEventList) { w.Write(x.T); w.Write(x.Territory); w.Write(x.Owner); }

            w.Write(r.CurseW); w.Write(r.CurseH);
            w.Write(r.CurseCell); w.Write(r.CurseX0); w.Write(r.CurseZ0);
            w.Write(r.CurseFrameList.Count);
            foreach (var x in r.CurseFrameList)
            {
                w.Write(x.T);
                w.Write(x.Bits != null ? x.Bits.Length : 0);
                if (x.Bits != null) w.Write(x.Bits);
            }

            w.Write(_partitionDone);
            w.Write(r.TerritoryW); w.Write(r.TerritoryH);
            w.Write(r.TerritoryCell); w.Write(r.TerritoryX0); w.Write(r.TerritoryZ0);
            int cells = r.TerritoryCells != null ? r.TerritoryCells.Length : -1;
            w.Write(cells);
            for (int i = 0; i < cells; i++) w.Write(r.TerritoryCells[i]);

            // units
            var unitId = new int[r.UnitList.Count];
            for (int i = 0; i < unitId.Length; i++) unitId[i] = NoId;
            foreach (var kv in _unitIndex)
                if (kv.Value >= 0 && kv.Value < unitId.Length) unitId[kv.Value] = IdOf(em, kv.Key);
            var liveUnit = new HashSet<int>(_liveUnits);

            w.Write(r.UnitList.Count);
            for (int i = 0; i < r.UnitList.Count; i++)
            {
                var u = r.UnitList[i];
                w.Write(u.Faction); w.Write(u.Class); w.Write(u.Kind);
                w.Write(u.TypeId ?? "");
                w.Write(u.Born); w.Write(u.Died);
                w.Write(u.Samples.Count);
                foreach (var s in u.Samples) WriteSample(w, s);
                w.Write(u.HasPending);
                WriteSample(w, u.Pending);
                bool live = liveUnit.Contains(i);
                w.Write(live);
                w.Write(live ? unitId[i] : NoId);
            }
            w.Write(r.UnitSampleTotal);
            w.Write(r.PositionPeriod);

            // buildings
            var liveBuilding = new HashSet<int>(_liveBuildings);
            w.Write(r.BuildingList.Count);
            for (int i = 0; i < r.BuildingList.Count; i++)
            {
                var b = r.BuildingList[i];
                w.Write(b.Faction);
                w.Write(b.TypeId ?? "");
                w.Write(b.X); w.Write(b.Z); w.Write(b.W); w.Write(b.H); w.Write(b.Yaw);
                w.Write(b.Born); w.Write(b.Completed); w.Write(b.Died);
                w.Write(b.Levels.Count);
                foreach (var (t, level) in b.Levels) { w.Write(t); w.Write(level); }
                bool live = liveBuilding.Contains(i);
                w.Write(live);
                w.Write(live && i < _buildingEntity.Count ? IdOf(em, _buildingEntity[i]) : NoId);
            }

            w.Write(r.DeathList.Count);
            foreach (var d in r.DeathList) { w.Write(d.T); w.Write(d.Faction); w.Write(d.X); w.Write(d.Z); }

            // series
            w.Write(r.SeriesTimeList.Count);
            foreach (float t in r.SeriesTimeList) w.Write(t);
            w.Write(r.SeriesValues.Length);
            foreach (var list in r.SeriesValues)
            {
                w.Write(list.Count);
                foreach (float v in list) w.Write(v);
            }

            // the recorder's own clocks (match time) and change trackers
            w.Write(_lastT); w.Write(_nextPos); w.Write(_nextSeries);
            w.Write(_ownLast.Length);
            foreach (int o in _ownLast) w.Write(o);
            w.Write(_thinWarned);
        }

        static void WriteSample(BinaryWriter w, UnitSample s)
        {
            w.Write(s.T); w.Write(s.X); w.Write(s.Z); w.Write(s.State);
        }

        static UnitSample ReadSample(BinaryReader r)
            => new UnitSample { T = r.ReadSingle(), X = r.ReadInt32(), Z = r.ReadInt32(), State = r.ReadByte() };

        static int IdOf(EntityManager em, Entity e)
            => e != Entity.Null && em.Exists(e) && em.HasComponent<NetworkedEntity>(e)
                ? em.GetComponentData<NetworkedEntity>(e).NetworkId : NoId;

        // ── read ─────────────────────────────────────────────────────────

        /// <summary>Replace the fresh record with the saved one. On any
        /// failure the fresh record stays: the review starts at the load.</summary>
        void AdoptSavedRecord()
        {
            byte[] data = _pendingRestore;
            _pendingRestore = null;
            var world = EntityWorld.DefaultGameObjectInjectionWorld;
            if (data == null || world == null || !world.IsCreated) return;
            try
            {
                using var r = new BinaryReader(new MemoryStream(data), System.Text.Encoding.UTF8);
                ReadRecord(r, world.EntityManager);
                Debug.Log($"[MatchRecorder] Saved record adopted: {_rec.Duration:0}s, " +
                          $"{_rec.UnitList.Count} unit track(s), {_rec.BuildingList.Count} building(s), " +
                          $"{_rec.SeriesCount} series sample(s).");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[MatchRecorder] The saved match record could not be read; " +
                                 "the review will start at the load. " + e.Message);
                BeginRecord();
            }
        }

        void ReadRecord(BinaryReader r, EntityManager em)
        {
            int version = r.ReadInt32();
            if (version != SectionVersion)
                throw new InvalidDataException("record version " + version);

            BeginRecord();                 // every tracker back to its fresh state
            var rec = _rec;

            // header
            rec.MapScene = r.ReadString();
            rec.MapName = r.ReadString();
            rec.WorldMin = new Vector2(r.ReadSingle(), r.ReadSingle());
            rec.WorldMax = new Vector2(r.ReadSingle(), r.ReadSingle());
            int factions = r.ReadInt32();
            if (factions != MatchRecord.Factions) throw new InvalidDataException("faction count " + factions);
            for (int f = 0; f < factions; f++)
            {
                rec.FactionColor[f] = new Color(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                rec.FactionPresent[f] = r.ReadBoolean();
            }
            // Who is looking is a fact of THIS session, not of the save.
            rec.LocalFaction = (int)GameSettings.LocalPlayerFaction;
            rec.Observer = GameSettings.IsObserver;
            MapInfo info = null;
            try { info = string.IsNullOrEmpty(rec.MapScene) ? null : MapInfoIndex.For(rec.MapScene); }
            catch (Exception) { info = null; }
            rec.MapThumbnail = info != null ? info.Thumbnail : null;

            // outcome
            rec.Duration = r.ReadSingle();
            rec.Ended = r.ReadBoolean();
            rec.Winner = r.ReadString();

            // map
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                rec.RegionList.Add(new RegionRec { Name = r.ReadString(), X = r.ReadSingle(), Z = r.ReadSingle() });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                rec.NodeList.Add(new NodeRec { Kind = r.ReadString(), X = r.ReadSingle(), Z = r.ReadSingle() });
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                rec.OwnerEventList.Add(new OwnerEvent { T = r.ReadSingle(), Territory = r.ReadInt32(), Owner = r.ReadInt32() });

            rec.CurseW = r.ReadInt32(); rec.CurseH = r.ReadInt32();
            rec.CurseCell = r.ReadSingle(); rec.CurseX0 = r.ReadSingle(); rec.CurseZ0 = r.ReadSingle();
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                float t = r.ReadSingle();
                int len = r.ReadInt32();
                rec.CurseFrameList.Add(new CurseFrame { T = t, Bits = r.ReadBytes(len) });
            }
            // The last frame kept is what the next sample is compared with.
            _curseLast = rec.CurseFrameList.Count > 0
                ? rec.CurseFrameList[rec.CurseFrameList.Count - 1].Bits : null;

            _partitionDone = r.ReadBoolean();
            rec.TerritoryW = r.ReadInt32(); rec.TerritoryH = r.ReadInt32();
            rec.TerritoryCell = r.ReadSingle(); rec.TerritoryX0 = r.ReadSingle(); rec.TerritoryZ0 = r.ReadSingle();
            int cells = r.ReadInt32();
            if (cells >= 0)
            {
                var arr = new int[cells];
                for (int i = 0; i < cells; i++) arr[i] = r.ReadInt32();
                rec.TerritoryCells = arr;
            }

            var byId = EntitiesById(em);

            // units
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var u = new UnitTrack
                {
                    Faction = r.ReadInt32(), Class = r.ReadInt32(), Kind = r.ReadByte(),
                    TypeId = r.ReadString(),
                    Born = r.ReadSingle(), Died = r.ReadSingle(),
                };
                int samples = r.ReadInt32();
                u.Samples.Capacity = Math.Max(u.Samples.Capacity, samples);
                for (int k = 0; k < samples; k++) u.Samples.Add(ReadSample(r));
                u.HasPending = r.ReadBoolean();
                u.Pending = ReadSample(r);
                bool live = r.ReadBoolean();
                int id = r.ReadInt32();
                rec.UnitList.Add(u);
                _unitStamp.Add(0);
                if (!live) continue;
                _liveUnits.Add(i);
                if (id != NoId && byId.TryGetValue(id, out var e)) _unitIndex[e] = i;
            }
            rec.UnitSampleTotal = r.ReadInt32();
            rec.PositionPeriod = r.ReadSingle();

            // buildings
            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                var b = new BuildingRec
                {
                    Faction = r.ReadInt32(),
                    TypeId = r.ReadString(),
                    X = r.ReadSingle(), Z = r.ReadSingle(), W = r.ReadSingle(), H = r.ReadSingle(),
                    Yaw = r.ReadSingle(),
                    Born = r.ReadSingle(), Completed = r.ReadSingle(), Died = r.ReadSingle(),
                };
                int levels = r.ReadInt32();
                for (int k = 0; k < levels; k++) b.Levels.Add((r.ReadSingle(), r.ReadInt32()));
                bool live = r.ReadBoolean();
                int id = r.ReadInt32();
                rec.BuildingList.Add(b);
                _buildingStamp.Add(0);
                Entity e = Entity.Null;
                if (live)
                {
                    _liveBuildings.Add(i);
                    if (id != NoId && byId.TryGetValue(id, out e)) _buildingIndex[e] = i;
                    else e = Entity.Null;
                }
                _buildingEntity.Add(e);
            }

            n = r.ReadInt32();
            for (int i = 0; i < n; i++)
                rec.DeathList.Add(new DeathRec { T = r.ReadSingle(), Faction = r.ReadInt32(), X = r.ReadSingle(), Z = r.ReadSingle() });

            // series
            n = r.ReadInt32();
            for (int i = 0; i < n; i++) rec.SeriesTimeList.Add(r.ReadSingle());
            int lists = r.ReadInt32();
            if (lists != rec.SeriesValues.Length) throw new InvalidDataException("series count " + lists);
            for (int s = 0; s < lists; s++)
            {
                int c = r.ReadInt32();
                var list = rec.SeriesValues[s];
                for (int i = 0; i < c; i++) list.Add(r.ReadSingle());
            }

            _lastT = r.ReadSingle(); _nextPos = r.ReadSingle(); _nextSeries = r.ReadSingle();
            n = r.ReadInt32();
            _ownLast = new int[n];
            for (int i = 0; i < n; i++) _ownLast[i] = r.ReadInt32();
            _thinWarned = r.ReadBoolean();

            // The header is in the record; ownership and the curse are re-read
            // against the restored trackers, so only real changes are added.
            _started = true;
            _ownVersion = int.MinValue;
            _curseVersion = int.MinValue;
        }

        /// <summary>NetworkId → entity for every networked entity of the
        /// restored world. One-shot per load, disposed here.</summary>
        static Dictionary<int, Entity> EntitiesById(EntityManager em)
        {
            var map = new Dictionary<int, Entity>();
            var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<NetworkedEntity>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });
            using (var es = q.ToEntityArray(Allocator.Temp))
            using (var ids = q.ToComponentDataArray<NetworkedEntity>(Allocator.Temp))
                for (int i = 0; i < es.Length; i++) map[ids[i].NetworkId] = es[i];
            q.Dispose();
            return map;
        }
    }
}
