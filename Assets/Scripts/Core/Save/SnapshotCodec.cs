// SnapshotCodec.cs
// Reflection-driven writer/reader for simulation state that lives OUTSIDE the
// ECS world: system fields (timers, carries, RNG streams, curse brain…) and a
// short list of statics. docs/Design/Replays_And_Saves.md §3
//
// What it can carry: primitives, enums, strings, Entity (re-keyed through
// NetworkedEntity ids, because a restored world hands out new indices), plain
// structs of those (Unity.Mathematics vectors, Random, SimCadence.Periodic…),
// arrays, List / HashSet / Dictionary / Queue of those, and plain classes of
// our own. Anything else — EntityQuery, lookups, native containers, Unity
// objects, delegates — is skipped: such fields are handles or caches the
// owner rebuilds, never match state.
//
// Two translations on the way back in:
//   * Entity  — NetworkId written, the new world's entity with that id read.
//   * epoch   — an int field whose name contains "epoch" and whose saved value
//               was the saved match epoch is rewritten to the NEW epoch, so the
//               owner's "new match → reset" latch does not wipe what was just
//               restored on the first tick.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Entities;

namespace TheWaningBorder.Core.Save
{
    internal sealed class SnapshotCodec
    {
        public Dictionary<Entity, int> EntityToId;
        public Dictionary<int, Entity> IdToEntity;

        public int SavedSimEpoch, NewSimEpoch;
        public int SavedMatchEpoch, NewMatchEpoch;

        /// <summary>The owner holds native containers it allocates itself:
        /// keep its epoch latch un-translated, so its own "new match" block
        /// runs and allocates them (its managed state is then re-seeded by it).</summary>
        public bool KeepOwnLifecycle;

        /// <summary>Fields skipped by the lifecycle rule, for the load log.</summary>
        public readonly List<string> Skipped = new List<string>();

        static readonly string[] LifecycleWords =
            { "init", "creat", "alloc", "built", "setup", "primed", "cached", "registered" };

        /// <summary>
        /// A bool that records the OWNER'S OWN setup ("_initialised",
        /// "_queriesCreated"…) must never be restored: the fresh system has not
        /// done that setup, and a restored true would make it skip it — queries
        /// never created, containers never allocated.
        /// </summary>
        static bool IsLifecycleFlag(System.Reflection.FieldInfo f)
        {
            if (f.FieldType != typeof(bool)) return false;
            string n = f.Name.ToLowerInvariant();
            foreach (var w in LifecycleWords) if (n.Contains(w)) return true;
            return false;
        }

        const int NoEntity = int.MinValue;

        // ── what can be carried ──────────────────────────────────────────

        static readonly Dictionary<Type, bool> _supported = new Dictionary<Type, bool>();
        static readonly Dictionary<Type, FieldInfo[]> _fields = new Dictionary<Type, FieldInfo[]>();

        public static bool IsSupported(Type t)
        {
            lock (_supported)
            {
                if (_supported.TryGetValue(t, out bool ok)) return ok;
                _supported[t] = false;          // cycle guard: a type in progress is unsupported
                ok = Compute(t);
                _supported[t] = ok;
                return ok;
            }
        }

        static bool Compute(Type t)
        {
            if (t.IsPointer || t.IsByRef) return false;
            if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)) return true;
            if (t == typeof(Entity)) return true;
            if (typeof(Delegate).IsAssignableFrom(t)) return false;
            if (typeof(UnityEngine.Object).IsAssignableFrom(t)) return false;
            if (t == typeof(IntPtr) || t == typeof(UIntPtr)) return false;

            if (t.IsArray)
            {
                var et = t.GetElementType();
                if (et.IsPrimitive) return true;                  // any rank, copied as raw bytes
                return t.GetArrayRank() == 1 && IsSupported(et);
            }

            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var args = t.GetGenericArguments();
                if (def == typeof(List<>) || def == typeof(HashSet<>) || def == typeof(Queue<>))
                    return IsSupported(args[0]);
                if (def == typeof(Dictionary<,>))
                    return IsSupported(args[0]) && IsSupported(args[1]);
                if (def == typeof(Nullable<>)) return false;
            }

            string ns = t.Namespace ?? "";
            if (t.IsValueType)
            {
                // Engine handles and native containers carry pointers or
                // world-specific indices; they are rebuilt, never restored.
                if (ns.StartsWith("Unity.Entities") || ns.StartsWith("Unity.Collections")
                    || ns.StartsWith("Unity.Jobs") || ns.StartsWith("Unity.Burst")
                    || ns.StartsWith("System")) return false;
                foreach (var f in FieldsOf(t))
                    if (!IsSupported(f.FieldType)) return false;
                return true;
            }

            // Reference types: only plain classes of our own.
            if (t.IsAbstract || t.IsInterface) return false;
            if (!(ns.Length == 0 || ns.StartsWith("TheWaningBorder"))) return false;
            foreach (var f in FieldsOf(t))
                if (!IsSupported(f.FieldType)) return false;
            return true;
        }

        public static FieldInfo[] FieldsOf(Type t)
        {
            lock (_fields)
            {
                if (_fields.TryGetValue(t, out var fs)) return fs;
                var list = new List<FieldInfo>();
                for (var c = t; c != null && c != typeof(object) && c != typeof(ValueType); c = c.BaseType)
                {
                    foreach (var f in c.GetFields(BindingFlags.Instance | BindingFlags.Public
                                                  | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        list.Add(f);
                    if (t.IsValueType) break;
                }
                fs = list.ToArray();
                _fields[t] = fs;
                return fs;
            }
        }

        // ── writing ──────────────────────────────────────────────────────

        /// <summary>Every supported field of <paramref name="target"/> as a
        /// (name → bytes) block, so a reader can match by name and skip.</summary>
        public void WriteFields(BinaryWriter w, IEnumerable<FieldInfo> fields, object target)
        {
            var picked = new List<FieldInfo>();
            foreach (var f in fields)
                if (!f.IsLiteral && IsSupported(f.FieldType)) picked.Add(f);

            w.Write(picked.Count);
            foreach (var f in picked)
            {
                w.Write(f.Name);
                using var ms = new MemoryStream();
                using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                    Write(bw, f.FieldType, f.GetValue(target));
                w.Write((int)ms.Length);
                w.Write(ms.GetBuffer(), 0, (int)ms.Length);
            }
        }

        public void Write(BinaryWriter w, Type t, object v)
        {
            if (!t.IsValueType)
            {
                w.Write(v != null);
                if (v == null) return;
            }

            if (t.IsEnum) { WritePrimitive(w, Enum.GetUnderlyingType(t), Convert.ChangeType(v, Enum.GetUnderlyingType(t))); return; }
            if (t.IsPrimitive || t == typeof(decimal)) { WritePrimitive(w, t, v); return; }
            if (t == typeof(string)) { w.Write((string)v); return; }
            if (t == typeof(Entity))
            {
                var e = (Entity)v;
                w.Write(EntityToId != null && e != Entity.Null && EntityToId.TryGetValue(e, out int id) ? id : NoEntity);
                return;
            }
            if (t.IsArray && t.GetElementType().IsPrimitive)
            {
                var pa = (Array)v;
                w.Write(pa.Rank);
                for (int d = 0; d < pa.Rank; d++) w.Write(pa.GetLength(d));
                int nbytes = Buffer.ByteLength(pa);
                var raw = new byte[nbytes];
                Buffer.BlockCopy(pa, 0, raw, 0, nbytes);
                w.Write(nbytes);
                w.Write(raw);
                return;
            }
            if (t.IsArray)
            {
                var a = (Array)v;
                var et = t.GetElementType();
                w.Write(a.Length);
                for (int i = 0; i < a.Length; i++) Write(w, et, a.GetValue(i));
                return;
            }
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var args = t.GetGenericArguments();
                if (def == typeof(Dictionary<,>))
                {
                    var d = (IDictionary)v;
                    w.Write(d.Count);
                    foreach (DictionaryEntry kv in d) { Write(w, args[0], kv.Key); Write(w, args[1], kv.Value); }
                    return;
                }
                if (def == typeof(List<>) || def == typeof(HashSet<>) || def == typeof(Queue<>))
                {
                    var items = new List<object>();
                    foreach (var o in (IEnumerable)v) items.Add(o);
                    w.Write(items.Count);
                    foreach (var o in items) Write(w, args[0], o);
                    return;
                }
            }
            // struct or plain class: its fields in declaration order
            foreach (var f in FieldsOf(t)) Write(w, f.FieldType, f.GetValue(v));
        }

        static void WritePrimitive(BinaryWriter w, Type t, object v)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: w.Write((bool)v); break;
                case TypeCode.Byte: w.Write((byte)v); break;
                case TypeCode.SByte: w.Write((sbyte)v); break;
                case TypeCode.Int16: w.Write((short)v); break;
                case TypeCode.UInt16: w.Write((ushort)v); break;
                case TypeCode.Int32: w.Write((int)v); break;
                case TypeCode.UInt32: w.Write((uint)v); break;
                case TypeCode.Int64: w.Write((long)v); break;
                case TypeCode.UInt64: w.Write((ulong)v); break;
                case TypeCode.Single: w.Write((float)v); break;
                case TypeCode.Double: w.Write((double)v); break;
                case TypeCode.Char: w.Write((char)v); break;
                case TypeCode.Decimal: w.Write((decimal)v); break;
                default: throw new NotSupportedException(t.FullName);
            }
        }

        // ── reading ──────────────────────────────────────────────────────

        /// <summary>Restore the fields written by <see cref="WriteFields"/> onto
        /// <paramref name="target"/> (a class instance, or a BOXED struct the
        /// caller unboxes afterwards). Unknown names are skipped.</summary>
        public int ReadFields(BinaryReader r, IEnumerable<FieldInfo> fields, object target)
        {
            var byName = new Dictionary<string, FieldInfo>();
            foreach (var f in fields) byName[f.Name] = f;

            int n = r.ReadInt32(), restored = 0;
            for (int i = 0; i < n; i++)
            {
                string name = r.ReadString();
                int len = r.ReadInt32();
                byte[] bytes = r.ReadBytes(len);
                if (!byName.TryGetValue(name, out var f) || f.IsLiteral || !IsSupported(f.FieldType)) continue;
                if (IsLifecycleFlag(f)
                    || (KeepOwnLifecycle && name.IndexOf("epoch", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    Skipped.Add((f.DeclaringType?.Name ?? "?") + "." + name);
                    continue;
                }
                try
                {
                    using var br = new BinaryReader(new MemoryStream(bytes));
                    object current = f.GetValue(target);
                    object value = Read(br, f.FieldType, current, f.Name);
                    // Collections and classes are filled IN PLACE when one
                    // already exists (so a readonly field keeps its instance);
                    // only a new instance or a value needs the field set.
                    if (!ReferenceEquals(value, current) || f.FieldType.IsValueType)
                        f.SetValue(target, value);
                    restored++;
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning($"[Snapshot] Could not restore {f.DeclaringType?.Name}.{name}: {e.Message}");
                }
            }
            return restored;
        }

        public object Read(BinaryReader r, Type t, object existing, string fieldName)
        {
            if (!t.IsValueType && !r.ReadBoolean()) return null;

            if (t.IsEnum) return Enum.ToObject(t, ReadPrimitive(r, Enum.GetUnderlyingType(t)));
            if (t.IsPrimitive || t == typeof(decimal))
            {
                object v = ReadPrimitive(r, t);
                if (t == typeof(int) && fieldName != null
                    && fieldName.IndexOf("epoch", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    int iv = (int)v;
                    if (iv != 0 && iv == SavedSimEpoch) v = NewSimEpoch;
                    else if (iv != 0 && iv == SavedMatchEpoch) v = NewMatchEpoch;
                }
                return v;
            }
            if (t == typeof(string)) return r.ReadString();
            if (t == typeof(Entity))
            {
                int id = r.ReadInt32();
                return id != NoEntity && IdToEntity != null && IdToEntity.TryGetValue(id, out var e) ? e : Entity.Null;
            }
            if (t.IsArray && t.GetElementType().IsPrimitive)
            {
                int rank = r.ReadInt32();
                var dims = new int[rank];
                for (int d = 0; d < rank; d++) dims[d] = r.ReadInt32();
                int nbytes = r.ReadInt32();
                byte[] raw = r.ReadBytes(nbytes);
                var pa = existing as Array;
                bool same = pa != null && pa.Rank == rank;
                for (int d = 0; same && d < rank; d++) same = pa.GetLength(d) == dims[d];
                if (!same) pa = Array.CreateInstance(t.GetElementType(), dims);
                Buffer.BlockCopy(raw, 0, pa, 0, Math.Min(nbytes, Buffer.ByteLength(pa)));
                return pa;
            }
            if (t.IsArray)
            {
                int len = r.ReadInt32();
                var et = t.GetElementType();
                var a = existing as Array;
                if (a == null || a.Length != len) a = Array.CreateInstance(et, len);
                for (int i = 0; i < len; i++) a.SetValue(Read(r, et, a.GetValue(i), null), i);
                return a;
            }
            if (t.IsGenericType)
            {
                var def = t.GetGenericTypeDefinition();
                var args = t.GetGenericArguments();
                if (def == typeof(Dictionary<,>))
                {
                    var d = (existing as IDictionary) ?? (IDictionary)Activator.CreateInstance(t);
                    d.Clear();
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++)
                    {
                        object k = Read(r, args[0], null, null);
                        object v = Read(r, args[1], null, null);
                        if (k == null || (k is Entity ke && ke == Entity.Null)) continue;   // unmappable key
                        d[k] = v;
                    }
                    return d;
                }
                if (def == typeof(List<>))
                {
                    var l = (existing as IList) ?? (IList)Activator.CreateInstance(t);
                    l.Clear();
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) l.Add(Read(r, args[0], null, null));
                    return l;
                }
                if (def == typeof(HashSet<>) || def == typeof(Queue<>))
                {
                    object c = existing ?? Activator.CreateInstance(t);
                    t.GetMethod("Clear").Invoke(c, null);
                    var add = def == typeof(HashSet<>) ? t.GetMethod("Add") : t.GetMethod("Enqueue");
                    int n = r.ReadInt32();
                    for (int i = 0; i < n; i++) add.Invoke(c, new[] { Read(r, args[0], null, null) });
                    return c;
                }
            }

            // struct or plain class
            object target = t.IsValueType
                ? (existing ?? Activator.CreateInstance(t))
                : (existing != null && existing.GetType() == t ? existing
                   : System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t));
            if (t.IsValueType) target = CopyBox(target);
            foreach (var f in FieldsOf(t))
            {
                object cur = f.GetValue(target);
                f.SetValue(target, Read(r, f.FieldType, cur, f.Name));
            }
            return target;
        }

        // A fresh box, so writing into it never aliases the caller's value.
        static object CopyBox(object boxed) => boxed == null ? null
            : typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(boxed, null);

        static object ReadPrimitive(BinaryReader r, Type t)
        {
            switch (Type.GetTypeCode(t))
            {
                case TypeCode.Boolean: return r.ReadBoolean();
                case TypeCode.Byte: return r.ReadByte();
                case TypeCode.SByte: return r.ReadSByte();
                case TypeCode.Int16: return r.ReadInt16();
                case TypeCode.UInt16: return r.ReadUInt16();
                case TypeCode.Int32: return r.ReadInt32();
                case TypeCode.UInt32: return r.ReadUInt32();
                case TypeCode.Int64: return r.ReadInt64();
                case TypeCode.UInt64: return r.ReadUInt64();
                case TypeCode.Single: return r.ReadSingle();
                case TypeCode.Double: return r.ReadDouble();
                case TypeCode.Char: return r.ReadChar();
                case TypeCode.Decimal: return r.ReadDecimal();
                default: throw new NotSupportedException(t.FullName);
            }
        }
    }
}
