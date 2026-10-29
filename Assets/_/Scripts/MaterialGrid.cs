using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Génère une grille de sphères montrant la variation de deux propriétés
/// de matériau : une sur l'axe X, l'autre sur l'axe Y.
/// Compatible URP (défaut Unity 6), Built-in et HDRP (Lit).
/// La grille se régénère dans l'éditeur dès qu'un paramètre change.
/// </summary>
[ExecuteAlways]
public class MaterialGrid : MonoBehaviour
{
    public enum Property { Metallic, Smoothness, Hue, Saturation, Brightness, Emission }

    [Header("Base material (optional, otherwise the active pipeline's Lit)")]
    public Material baseMaterial;

    [Header("X axis")]
    public Property propertyX = Property.Metallic;
    [Min(1)] public int columns = 6;
    public Vector2 rangeX = new Vector2(0f, 1f);

    [Header("Y axis")]
    public Property propertyY = Property.Smoothness;
    [Min(1)] public int rows = 6;
    public Vector2 rangeY = new Vector2(0f, 1f);

    [Header("Default values (non-varied properties)")]
    public Color baseColor = new Color(0.85f, 0.25f, 0.2f);
    [Range(0f, 1f)] public float defaultMetallic = 0f;
    [Range(0f, 1f)] public float defaultSmoothness = 0.5f;
    [ColorUsage(false, true)] public Color emissionColor = Color.white;

    [Header("Layout")]
    [Min(0.1f)] public float spacing = 1.3f;
    [Min(0.05f)] public float diameter = 1f;
    public bool showLabels = true;
    public float labelSize = 0.12f;

    const string ContainerName = "_GeneratedGrid";
    Transform container;
    readonly List<Material> createdMaterials = new List<Material>();
    bool needsRegenerate;

    // ---------- Cycle de vie ----------

    void OnEnable()   => needsRegenerate = true;
    void OnValidate() => needsRegenerate = true;
    void OnDisable()  => Clear();

    void Update()
    {
        if (!needsRegenerate) return;
        needsRegenerate = false;
        Generate();
    }

    [ContextMenu("Regenerate grid")]
    public void Generate()
    {
        Clear();

        container = new GameObject(ContainerName).transform;
        container.gameObject.hideFlags = HideFlags.DontSave;
        container.SetParent(transform, false);

        Shader shader = baseMaterial ? baseMaterial.shader : DefaultShader();
        if (shader == null)
        {
            Debug.LogWarning("MaterialGrid : aucun shader Lit trouvé.");
            return;
        }

        Vector3 origin = new Vector3(-(columns - 1) * spacing * 0.5f,
                                     -(rows - 1) * spacing * 0.5f, 0f);

        for (int y = 0; y < rows; y++)
        {
            float valY = Mathf.Lerp(rangeY.x, rangeY.y, Ratio(y, rows));

            for (int x = 0; x < columns; x++)
            {
                float valX = Mathf.Lerp(rangeX.x, rangeX.y, Ratio(x, columns));

                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Sphere_{x}_{y}";
                sphere.hideFlags = HideFlags.DontSave;
                SafeDestroy(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(container, false);
                sphere.transform.localPosition = origin + new Vector3(x * spacing, y * spacing, 0f);
                sphere.transform.localScale = Vector3.one * diameter;

                Material mat = baseMaterial ? new Material(baseMaterial) : new Material(shader);
                mat.hideFlags = HideFlags.DontSave;
                mat.name = $"Mat_{propertyX}{valX:0.00}_{propertyY}{valY:0.00}";
                ApplyProperties(mat, valX, valY);
                createdMaterials.Add(mat);

                sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        if (showLabels) CreateLabels(origin);
    }

    // ---------- Matériaux ----------

    void ApplyProperties(Material mat, float valX, float valY)
    {
        float metallic = defaultMetallic;
        float smoothness = defaultSmoothness;
        float emission = 0f;
        Color.RGBToHSV(baseColor, out float h, out float s, out float v);

        void Apply(Property p, float val)
        {
            switch (p)
            {
                case Property.Metallic:   metallic = val; break;
                case Property.Smoothness: smoothness = val; break;
                case Property.Hue:        h = Mathf.Repeat(val, 1f); break;
                case Property.Saturation: s = Mathf.Clamp01(val); break;
                case Property.Brightness: v = Mathf.Max(0f, val); break;
                case Property.Emission:   emission = Mathf.Max(0f, val); break;
            }
        }

        Apply(propertyX, valX);
        Apply(propertyY, valY);

        Color color = Color.HSVToRGB(h, s, v);
        color.a = baseColor.a;

        // Couleur : URP/HDRP = _BaseColor, Built-in = _Color
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);

        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

        // Smoothness : URP/HDRP = _Smoothness, Built-in = _Glossiness
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        if (emission > 0f)
        {
            Color e = emissionColor * emission;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", e);
                mat.EnableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (mat.HasProperty("_EmissiveColor")) // HDRP
                mat.SetColor("_EmissiveColor", e);
        }
    }

    static Shader DefaultShader()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return Shader.Find("Standard");

        string type = rp.GetType().Name;
        if (type.Contains("HDRender")) return Shader.Find("HDRP/Lit");
        return Shader.Find("Universal Render Pipeline/Lit");
    }

    // ---------- Étiquettes ----------

    void CreateLabels(Vector3 origin)
    {
        float margin = spacing * 0.85f;

        // Valeurs de l'axe X (sous la grille)
        for (int x = 0; x < columns; x++)
        {
            float val = Mathf.Lerp(rangeX.x, rangeX.y, Ratio(x, columns));
            CreateText(val.ToString("0.##"),
                origin + new Vector3(x * spacing, -margin, 0f), TextAnchor.UpperCenter);
        }

        // Valeurs de l'axe Y (à gauche)
        for (int y = 0; y < rows; y++)
        {
            float val = Mathf.Lerp(rangeY.x, rangeY.y, Ratio(y, rows));
            CreateText(val.ToString("0.##"),
                origin + new Vector3(-margin, y * spacing, 0f), TextAnchor.MiddleRight);
        }

        // Titres des axes
        float width = (columns - 1) * spacing;
        float height = (rows - 1) * spacing;
        CreateText($"{propertyX}  →",
            origin + new Vector3(width * 0.5f, -margin * 1.8f, 0f), TextAnchor.UpperCenter, 1.4f);
        CreateText($"{propertyY}  ↑",
            origin + new Vector3(-margin * 1.8f, height * 0.5f, 0f), TextAnchor.MiddleRight, 1.4f);
    }

    void CreateText(string text, Vector3 position, TextAnchor anchor, float scale = 1f)
    {
        var go = new GameObject("Label_" + text) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(container, false);
        go.transform.localPosition = position;

        var tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.anchor = anchor;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = labelSize * scale / 6f;
        tm.color = Color.white;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font)
        {
            tm.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }

    // ---------- Utilitaires ----------

    static float Ratio(int i, int total) => total > 1 ? i / (float)(total - 1) : 0.5f;

    void Clear()
    {
        foreach (var m in createdMaterials) SafeDestroy(m);
        createdMaterials.Clear();

        // Inclut une éventuelle grille orpheline laissée par un rechargement de scripts
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name == ContainerName) SafeDestroy(child.gameObject);
        }
        container = null;
    }

    static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
