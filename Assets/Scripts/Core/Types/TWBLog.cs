// TWBLog.cs
// Verbose dev logging that compiles out of normal builds.
//
// Use TWBLog.Log(...) for "[Tag]"-prefixed development/diagnostic logs. The
// calls (and their argument evaluation, e.g. string interpolation) are stripped
// by the compiler unless the TWB_VERBOSE scripting-define symbol is set
// (Project Settings > Player > Scripting Define Symbols), so they cost nothing
// in normal play. Keep Debug.LogWarning / Debug.LogError for real problems —
// those are NOT gated.
//
// Declared in the global namespace (like the ECS components) so it's callable
// everywhere without an extra using.

using System.Diagnostics;

public static class TWBLog
{
    [Conditional("TWB_VERBOSE")]
    public static void Log(object message) => UnityEngine.Debug.Log(message);

    [Conditional("TWB_VERBOSE")]
    public static void Log(object message, UnityEngine.Object context) => UnityEngine.Debug.Log(message, context);
}
