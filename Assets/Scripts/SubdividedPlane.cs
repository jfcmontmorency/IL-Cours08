using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Génère un plan très subdivisé (beaucoup de sommets) pour que le shader
/// « Custom/Lit Deplacement » ait assez de géométrie à déplacer.
/// Ajouter sur un GameObject vide : MeshFilter et MeshRenderer sont ajoutés automatiquement.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SubdividedPlane : MonoBehaviour
{
    [Min(0.1f)] public float size = 4f;
    [Range(1, 500)] public int subdivisions = 200;
    [Tooltip("Répétition de la texture sur le plan")]
    [Min(0.01f)] public float uvTiling = 1f;

    Mesh mesh;

    void OnEnable()   => Generate();
    void OnValidate() => Generate();

    void OnDisable()
    {
        if (mesh == null) return;
        if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
        mesh = null;
    }

    void Generate()
    {
        if (mesh == null)
        {
            mesh = new Mesh { name = "SubdividedPlane", hideFlags = HideFlags.DontSave };
            mesh.indexFormat = IndexFormat.UInt32;
        }

        int n = subdivisions + 1;
        var vertices  = new Vector3[n * n];
        var uvs       = new Vector2[n * n];
        var normals   = new Vector3[n * n];
        var triangles = new int[subdivisions * subdivisions * 6];

        for (int z = 0; z < n; z++)
        for (int x = 0; x < n; x++)
        {
            int i = z * n + x;
            float u = x / (float)subdivisions;
            float v = z / (float)subdivisions;
            vertices[i] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
            uvs[i]      = new Vector2(u, v) * uvTiling;
            normals[i]  = Vector3.up;
        }

        int t = 0;
        for (int z = 0; z < subdivisions; z++)
        for (int x = 0; x < subdivisions; x++)
        {
            int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
            triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
            triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
        }

        mesh.Clear();
        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.normals   = normals;
        mesh.triangles = triangles;
        mesh.RecalculateTangents();   // nécessaire pour la normal map
        mesh.RecalculateBounds();
        // Marge pour que le déplacement ne fasse pas disparaître le plan (culling)
        var bounds = mesh.bounds; bounds.Expand(new Vector3(0f, size * 0.5f, 0f)); mesh.bounds = bounds;

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}
