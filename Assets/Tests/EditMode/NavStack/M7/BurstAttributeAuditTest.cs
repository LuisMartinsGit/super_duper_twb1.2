// BurstAttributeAuditTest.cs
// task-112 M7 -- reflection-based scan over every type in the
// TheWaningBorder.Systems.Navigation namespace; for each IJob* struct,
// assert it carries [Unity.Burst.BurstCompile]. A regression here
// means someone added a new job without bursting it, which would
// silently slow the sim and risk lockstep desync (DR-15: burst-version
// pinning relies on every job actually being burst-compiled).
//
// Discovery rule:
//   * Walks every loaded assembly looking for types whose namespace
//     starts with "TheWaningBorder.Systems.Navigation".
//   * Skips abstract/interface/enum types.
//   * Selects structs implementing IJob, IJobParallelFor, IJobChunk,
//     or any closed IJobEntity-shaped interface.
//   * Asserts the type carries [BurstCompile].
//
// Failures are collected into a single message so the test report
// lists every offender at once rather than failing on the first.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Burst;
using Unity.Jobs;
using Unity.Entities;

namespace TheWaningBorder.Tests.EditMode.NavStack.M7
{
    public class BurstAttributeAuditTest
    {
        private const string NavNamespace = "TheWaningBorder.Systems.Navigation";

        [Test]
        public void EveryJobInNavNamespace_CarriesBurstCompile()
        {
            var failures = new List<string>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (t.Namespace == null) continue;
                    if (!t.Namespace.StartsWith(NavNamespace, StringComparison.Ordinal)) continue;
                    if (!t.IsValueType) continue;        // structs only
                    if (t.IsAbstract) continue;          // skip interfaces
                    if (t.IsEnum) continue;
                    if (!IsJobType(t)) continue;
                    if (HasBurstCompile(t)) continue;

                    failures.Add(t.FullName);
                }
            }

            if (failures.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("[M7 audit] one or more nav jobs lack [BurstCompile]:");
                foreach (var name in failures) sb.AppendLine("  " + name);
                Assert.Fail(sb.ToString());
            }
        }

        private static bool IsJobType(Type t)
        {
            foreach (var iface in t.GetInterfaces())
            {
                if (iface == typeof(IJob)) return true;
                if (iface == typeof(IJobParallelFor)) return true;
                if (iface == typeof(IJobChunk)) return true;
                // IJobEntity is source-generated; the source generator
                // makes the user struct implement
                // Unity.Entities.IJobEntity which is in Unity.Entities.
                // We detect by interface NAME so the test still works
                // when the closed generic isn't available.
                if (iface.Name == "IJobEntity") return true;
                if (iface.Name == "IJobChunkBeginEnd") return true;
            }
            return false;
        }

        private static bool HasBurstCompile(Type t)
        {
            return t.GetCustomAttribute(typeof(BurstCompileAttribute), inherit: false) != null;
        }
    }
}
