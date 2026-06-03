// AssemblyInfo.cs
// task-nav-stack-flowfields-112 M7 follow-up: expose runtime internals to the
// test assemblies so EditMode tests can construct and execute job structs
// that are marked `internal` for normal callers.
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TheWaningBorder.Tests.EditMode")]
[assembly: InternalsVisibleTo("TheWaningBorder.Tests.PlayMode")]
