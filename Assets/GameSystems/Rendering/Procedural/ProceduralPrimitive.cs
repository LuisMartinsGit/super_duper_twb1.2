// ProceduralPrimitive.cs
// The primitive builder that used to be copy-pasted into every *Visual.cs.
//
// Twenty visual files each declared their own local `Make` lambda, in four
// near-identical variants (10 units shared one byte-for-byte, 7 buildings
// shared another). Each also ran its own `Shader.Find`, which is the risky
// half: Shader.Find returns null in a player build when nothing references
// the shader, and the copies fed that null straight into `new Material(null)`.
// That check now happens once, here.
//
// NOTE: this deliberately creates a material PER OBJECT, unlike
// ProceduralMaterialHelper, which shares one material and pushes colour
// through a MaterialPropertyBlock. That helper cannot be used here: every
// caller keeps its Material references and re-tints them later (per-unit
// faction colour), and a shared material would recolour every unit at once.

using UnityEngine;

public static class ProceduralPrimitive
{
    private static Shader _lit;
    private static bool _warned;

    /// <summary>URP Lit, falling back to Standard. Cached; warns once if the
    /// shader was stripped from the build instead of handing back null.</summary>
    public static Shader LitShader
    {
        get
        {
            if (_lit == null)
            {
                _lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (_lit == null && !_warned)
                {
                    _warned = true;
                    Debug.LogError("[ProceduralPrimitive] Neither URP/Lit nor Standard " +
                                   "resolved -- the shader was stripped from this build. " +
                                   "Procedural visuals will render untextured.");
                }
            }
            return _lit;
        }
    }

    /// <summary>
    /// Build one primitive: parent it, place it, give it its own material, drop
    /// the collider Unity attaches by default.
    /// </summary>
    /// <param name="glow">Enable _EMISSION and drive _EmissionColor from colour.</param>
    /// <param name="shader">Overrides <see cref="LitShader"/> when non-null.</param>
    public static GameObject Make(
        PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Vector3 localScale, Quaternion localRot,
        Color color, float metallic, float smoothness,
        bool glow = false, Shader shader = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = localRot;
        go.transform.localScale = localScale;

        var r = go.GetComponent<Renderer>();
        var sh = shader != null ? shader : LitShader;
        if (r != null && sh != null)
        {
            var m = new Material(sh);
            m.color = color;
            if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (glow && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 1.6f);
            }
            r.material = m;
        }

        var c = go.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
        return go;
    }

    /// <summary>
    /// Re-tint an already-built material toward a faction colour. Callers hold
    /// their own Material instances precisely so this is safe per object.
    /// </summary>
    public static void Tint(Material m, Color c, float whiten, bool emissive)
    {
        if (m == null) return;
        var baseCol = Color.Lerp(c, Color.white, whiten);
        m.SetColor("_BaseColor", baseCol);
        if (m.HasProperty("_Color")) m.SetColor("_Color", baseCol);
        if (emissive && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c * 0.35f);
        }
    }
}
