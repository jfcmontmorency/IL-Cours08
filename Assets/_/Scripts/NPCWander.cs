using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Le PNJ choisit un point au hasard sur le NavMesh, s'y rend, attend un peu, puis recommence.
/// Il se tourne d'abord vers sa direction, puis avance : pas de « glissade » en virage.
/// Animation : le script informe l'Animator (Bool) ; l'Animator Controller décide quoi jouer.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class NPCWander : MonoBehaviour
{
    [Header("Wander")]
    [Tooltip("Distance minimale entre le PNJ et son prochain point : évite les tout petits déplacements")]
    [SerializeField] private float minDistance = 1f;
    [Tooltip("Distance maximale entre le PNJ et son prochain point")]
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private Vector2 pauseMinMax = new Vector2(1f, 3f);

    [Header("Movement")]
    [Tooltip("Vitesse du clip de marche (m/s). À ajuster jusqu'à ce que les pieds ne glissent plus.")]
    [SerializeField] private float walkSpeed = 1.3f;
    [Tooltip("Temps (en secondes) pour faire un demi-tour complet")]
    [SerializeField] private float halfTurnDuration = 0.75f;
    [Tooltip("Au-delà de cet angle, le PNJ pivote sur place avant d'avancer")]
    [SerializeField] private float maxAngleToMove = 45f;

    [Header("Animation (optional)")]
    [SerializeField] private Animator animator;
    [Tooltip("Nom exact du paramètre Bool dans l'Animator Controller")]
    [SerializeField] private string walkParameter = "walking";

    // Vitesse (m/s) sous laquelle le PNJ est considéré immobile
    private const float WalkThreshold = 0.1f;
    // Nombre d'essais pour trouver un point assez loin avant de se contenter du plus éloigné
    private const int MaxAttempts = 10;

    private NavMeshAgent agent;
    private float departureTime = -1f;
    private int walkId;
    private bool hasWalkParameter;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;   // la rotation est gérée par ce script
        agent.speed = walkSpeed;
        walkId = Animator.StringToHash(walkParameter);
        if (animator != null) animator.applyRootMotion = false;
    }

    private void Start()
    {
        // Paramètre de l'Animator : vérifié une seule fois, au démarrage
        if (animator != null)
            hasWalkParameter = CheckParameter(walkParameter, AnimatorControllerParameterType.Bool, "l'animation de marche ne jouera pas");

        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"{name} n'est pas sur le NavMesh : vérifier le Bake et la position du personnage.", this);
            enabled = false;
            return;
        }
        PickDestination();
    }

    private void Update()
    {
        Orient();

        // L'Animator reflète toujours l'état réel du déplacement
        if (hasWalkParameter)
            animator.SetBool(walkId, agent.velocity.magnitude > WalkThreshold);

        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
        if (!arrived) return;

        if (departureTime < 0f)
            departureTime = Time.time + Random.Range(pauseMinMax.x, pauseMinMax.y);
        else if (Time.time >= departureTime)
        {
            PickDestination();
            departureTime = -1f;
        }
    }

    /// <summary>
    /// Tourne le PNJ vers le prochain coin du chemin.
    /// Tant que l'écart d'angle est grand, l'agent est freiné : il pivote presque sur place.
    /// </summary>
    private void Orient()
    {
        if (!agent.hasPath) { agent.speed = walkSpeed; return; }

        Vector3 direction = agent.steeringTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, target, (180f / halfTurnDuration) * Time.deltaTime);

        // 1 quand le PNJ fait face à sa direction, 0 quand l'écart dépasse maxAngleToMove
        float angle = Quaternion.Angle(transform.rotation, target);
        float factor = Mathf.Clamp01(1f - angle / maxAngleToMove);
        agent.speed = walkSpeed * factor;
    }

    /// <summary>
    /// Tire un point au hasard autour du PNJ, entre minDistance et maxDistance.
    /// Si aucun essai ne convient (bord du NavMesh, obstacles), le point le plus éloigné est retenu.
    /// </summary>
    private void PickDestination()
    {
        bool found = false;
        Vector3 best = Vector3.zero;
        float bestDistance = -1f;

        for (int i = 0; i < MaxAttempts; i++)
        {
            // Direction au hasard à plat, puis distance au hasard entre min et max
            Vector2 direction = Random.insideUnitCircle.normalized;
            float range = Random.Range(minDistance, maxDistance);
            Vector3 point = transform.position + new Vector3(direction.x, 0f, direction.y) * range;
            if (!NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, agent.areaMask))
                continue;

            // SamplePosition peut rapprocher le point : distance revérifiée, à plat
            Vector3 offset = hit.position - transform.position;
            offset.y = 0f;
            float distance = offset.magnitude;

            if (distance >= minDistance)
            {
                agent.SetDestination(hit.position);
                return;
            }
            if (distance > bestDistance)
            {
                best = hit.position;
                bestDistance = distance;
                found = true;
            }
        }

        if (found) agent.SetDestination(best);
    }

    /// <summary>
    /// Vérifie qu'un paramètre existe dans l'Animator Controller, avec le bon type.
    /// Sinon, affiche un seul avertissement clair (au lieu de celui de Unity à chaque image).
    /// </summary>
    private bool CheckParameter(string parameterName, AnimatorControllerParameterType type, string consequence)
    {
        if (string.IsNullOrEmpty(parameterName)) return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == parameterName && parameter.type == type) return true;

        string controller = animator.runtimeAnimatorController != null
            ? animator.runtimeAnimatorController.name
            : "(aucun Animator Controller)";
        Debug.LogWarning($"{name} : pas de {type} \"{parameterName}\" dans {controller} : {consequence}.", this);
        return false;
    }

    // Affiche les distances min et max au sol, et la destination, dans la vue Scene (sélectionner le PNJ)
    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        // Le prochain point est tiré entre les deux cercles
        DrawRing(minDistance, new Color(1f, 0.5f, 0f), "Min");
        DrawRing(maxDistance, Color.cyan, "Max");
#endif

        // Destination courante (en Play seulement)
        if (agent == null || !agent.hasPath) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, agent.destination);
        Gizmos.DrawSphere(agent.destination, 0.2f);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Dessine un cercle à plat au sol, avec son nom et son rayon (vue Scene seulement).
    /// </summary>
    private void DrawRing(float radius, Color color, string label)
    {
        if (radius <= 0f) return;
        Vector3 center = transform.position + Vector3.up * 0.05f;   // juste au-dessus du sol
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius, 2f);
        GUIStyle style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
        style.normal.textColor = color;
        UnityEditor.Handles.Label(center + Vector3.right * radius, $" {label} {radius:0.#} m", style);
    }
#endif
}
