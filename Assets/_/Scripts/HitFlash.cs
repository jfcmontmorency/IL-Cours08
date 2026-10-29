using System.Collections;
using UnityEngine;

/// <summary>
/// Fait virer le personnage au rouge, puis revenir à sa couleur normale, pour montrer qu'il a été touché.
/// À placer sur la cible (le joueur), puis appeler Flash() — par exemple depuis l'événement
/// On Attack d'un NPCFollower.
/// Les matériaux ne sont pas modifiés : la couleur passe par un MaterialPropertyBlock,
/// qui ne touche que ce personnage (les autres objets qui partagent le matériau ne changent pas).
/// </summary>
public class HitFlash : MonoBehaviour
{
    [Tooltip("Couleur au moment de l'impact")]
    [SerializeField] private Color flashColor = Color.red;
    [Tooltip("Durée du retour à la couleur normale (s)")]
    [SerializeField] private float duration = 0.4f;
    [Tooltip("Attente avant le flash (s) : permet de le synchroniser avec l'impact du coup dans l'animation d'attaque")]
    [SerializeField] private float delay = 0f;

    // Propriété de couleur : URP/HDRP = _BaseColor, Built-in = _Color
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private Renderer[] renderers;
    private MaterialPropertyBlock block;
    private Coroutine running;

    private void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        block = new MaterialPropertyBlock();
    }

    /// <summary>
    /// Lance le flash. Un nouveau coup pendant un flash le relance depuis le rouge.
    /// </summary>
    public void Flash()
    {
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(FlashRoutine());
    }

    private IEnumerator FlashRoutine()
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            // 1 au moment de l'impact, 0 à la fin : du rouge vers la couleur d'origine
            ApplyTint(1f - t / duration);
            yield return null;
        }

        ClearTint();
        running = null;
    }

    private void ApplyTint(float amount)
    {
        foreach (Renderer r in renderers)
        {
            Material[] materials = r.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null) continue;

                int id = material.HasProperty(BaseColorId) ? BaseColorId
                       : material.HasProperty(ColorId) ? ColorId
                       : -1;
                if (id == -1) continue;

                r.GetPropertyBlock(block, i);
                block.SetColor(id, Color.Lerp(material.GetColor(id), flashColor, amount));
                r.SetPropertyBlock(block, i);
            }
        }
    }

    private void ClearTint()
    {
        // Retire les blocs : chaque renderer reprend exactement son matériau d'origine
        foreach (Renderer r in renderers)
            for (int i = 0; i < r.sharedMaterials.Length; i++)
                r.SetPropertyBlock(null, i);
    }

    private void OnDisable() => ClearTint();
}
