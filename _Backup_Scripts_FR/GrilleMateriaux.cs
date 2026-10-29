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
public class GrilleMateriaux : MonoBehaviour
{
    public enum Propriete { Metallic, Smoothness, Teinte, Saturation, Luminosite, Emission }

    [Header("Matériau de base (optionnel — sinon Lit du pipeline actif)")]
    public Material materiauBase;

    [Header("Axe X")]
    public Propriete proprieteX = Propriete.Metallic;
    [Min(1)] public int colonnes = 6;
    public Vector2 plageX = new Vector2(0f, 1f);

    [Header("Axe Y")]
    public Propriete proprieteY = Propriete.Smoothness;
    [Min(1)] public int rangees = 6;
    public Vector2 plageY = new Vector2(0f, 1f);

    [Header("Valeurs par défaut (propriétés non variées)")]
    public Color couleurBase = new Color(0.85f, 0.25f, 0.2f);
    [Range(0f, 1f)] public float metallicDefaut = 0f;
    [Range(0f, 1f)] public float smoothnessDefaut = 0.5f;
    [ColorUsage(false, true)] public Color couleurEmission = Color.white;

    [Header("Disposition")]
    [Min(0.1f)] public float espacement = 1.3f;
    [Min(0.05f)] public float diametre = 1f;
    public bool afficherEtiquettes = true;
    public float tailleEtiquettes = 0.12f;

    const string NomConteneur = "_GrilleGeneree";
    Transform conteneur;
    readonly List<Material> materiauxCrees = new List<Material>();
    bool aRegenerer;

    // ---------- Cycle de vie ----------

    void OnEnable()   => aRegenerer = true;
    void OnValidate() => aRegenerer = true;
    void OnDisable()  => Nettoyer();

    void Update()
    {
        if (!aRegenerer) return;
        aRegenerer = false;
        Generer();
    }

    [ContextMenu("Régénérer la grille")]
    public void Generer()
    {
        Nettoyer();

        conteneur = new GameObject(NomConteneur).transform;
        conteneur.gameObject.hideFlags = HideFlags.DontSave;
        conteneur.SetParent(transform, false);

        Shader shader = materiauBase ? materiauBase.shader : ShaderParDefaut();
        if (shader == null)
        {
            Debug.LogWarning("GrilleMateriaux : aucun shader Lit trouvé.");
            return;
        }

        Vector3 origine = new Vector3(-(colonnes - 1) * espacement * 0.5f,
                                      -(rangees - 1) * espacement * 0.5f, 0f);

        for (int y = 0; y < rangees; y++)
        {
            float valY = Mathf.Lerp(plageY.x, plageY.y, Ratio(y, rangees));

            for (int x = 0; x < colonnes; x++)
            {
                float valX = Mathf.Lerp(plageX.x, plageX.y, Ratio(x, colonnes));

                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.name = $"Sphere_{x}_{y}";
                sphere.hideFlags = HideFlags.DontSave;
                DetruireObjet(sphere.GetComponent<Collider>());
                sphere.transform.SetParent(conteneur, false);
                sphere.transform.localPosition = origine + new Vector3(x * espacement, y * espacement, 0f);
                sphere.transform.localScale = Vector3.one * diametre;

                Material mat = materiauBase ? new Material(materiauBase) : new Material(shader);
                mat.hideFlags = HideFlags.DontSave;
                mat.name = $"Mat_{proprieteX}{valX:0.00}_{proprieteY}{valY:0.00}";
                AppliquerProprietes(mat, valX, valY);
                materiauxCrees.Add(mat);

                sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        if (afficherEtiquettes) CreerEtiquettes(origine);
    }

    // ---------- Matériaux ----------

    void AppliquerProprietes(Material mat, float valX, float valY)
    {
        float metallic = metallicDefaut;
        float smoothness = smoothnessDefaut;
        float emission = 0f;
        Color.RGBToHSV(couleurBase, out float h, out float s, out float v);

        void Appliquer(Propriete p, float val)
        {
            switch (p)
            {
                case Propriete.Metallic:   metallic = val; break;
                case Propriete.Smoothness: smoothness = val; break;
                case Propriete.Teinte:     h = Mathf.Repeat(val, 1f); break;
                case Propriete.Saturation: s = Mathf.Clamp01(val); break;
                case Propriete.Luminosite: v = Mathf.Max(0f, val); break;
                case Propriete.Emission:   emission = Mathf.Max(0f, val); break;
            }
        }

        Appliquer(proprieteX, valX);
        Appliquer(proprieteY, valY);

        Color couleur = Color.HSVToRGB(h, s, v);
        couleur.a = couleurBase.a;

        // Couleur : URP/HDRP = _BaseColor, Built-in = _Color
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", couleur);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", couleur);

        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);

        // Smoothness : URP/HDRP = _Smoothness, Built-in = _Glossiness
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        if (emission > 0f)
        {
            Color e = couleurEmission * emission;
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

    static Shader ShaderParDefaut()
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp == null) return Shader.Find("Standard");

        string type = rp.GetType().Name;
        if (type.Contains("HDRender")) return Shader.Find("HDRP/Lit");
        return Shader.Find("Universal Render Pipeline/Lit");
    }

    // ---------- Étiquettes ----------

    void CreerEtiquettes(Vector3 origine)
    {
        float marge = espacement * 0.85f;

        // Valeurs de l'axe X (sous la grille)
        for (int x = 0; x < colonnes; x++)
        {
            float val = Mathf.Lerp(plageX.x, plageX.y, Ratio(x, colonnes));
            CreerTexte(val.ToString("0.##"),
                origine + new Vector3(x * espacement, -marge, 0f), TextAnchor.UpperCenter);
        }

        // Valeurs de l'axe Y (à gauche)
        for (int y = 0; y < rangees; y++)
        {
            float val = Mathf.Lerp(plageY.x, plageY.y, Ratio(y, rangees));
            CreerTexte(val.ToString("0.##"),
                origine + new Vector3(-marge, y * espacement, 0f), TextAnchor.MiddleRight);
        }

        // Titres des axes
        float largeur = (colonnes - 1) * espacement;
        float hauteur = (rangees - 1) * espacement;
        CreerTexte($"{proprieteX}  →",
            origine + new Vector3(largeur * 0.5f, -marge * 1.8f, 0f), TextAnchor.UpperCenter, 1.4f);
        CreerTexte($"{proprieteY}  ↑",
            origine + new Vector3(-marge * 1.8f, hauteur * 0.5f, 0f), TextAnchor.MiddleRight, 1.4f);
    }

    void CreerTexte(string texte, Vector3 position, TextAnchor ancre, float echelle = 1f)
    {
        var go = new GameObject("Etiquette_" + texte) { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(conteneur, false);
        go.transform.localPosition = position;

        var tm = go.AddComponent<TextMesh>();
        tm.text = texte;
        tm.anchor = ancre;
        tm.alignment = TextAlignment.Center;
        tm.fontSize = 64;
        tm.characterSize = tailleEtiquettes * echelle / 6f;
        tm.color = Color.white;

        Font police = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (police)
        {
            tm.font = police;
            go.GetComponent<MeshRenderer>().sharedMaterial = police.material;
        }
    }

    // ---------- Utilitaires ----------

    static float Ratio(int i, int total) => total > 1 ? i / (float)(total - 1) : 0.5f;

    void Nettoyer()
    {
        foreach (var m in materiauxCrees) DetruireObjet(m);
        materiauxCrees.Clear();

        // Inclut une éventuelle grille orpheline laissée par un rechargement de scripts
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform enfant = transform.GetChild(i);
            if (enfant.name == NomConteneur) DetruireObjet(enfant.gameObject);
        }
        conteneur = null;
    }

    static void DetruireObjet(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
