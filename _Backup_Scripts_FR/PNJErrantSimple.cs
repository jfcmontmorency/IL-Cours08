using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Le PNJ choisit un point au hasard sur le NavMesh, s'y rend, attend un peu, puis recommence.
/// Il se tourne d'abord vers sa direction, puis avance : pas de « glissade » en virage.
/// Animation : le script informe l'Animator (Bool) ; l'Animator Controller décide quoi jouer.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class PNJErrantSimple : MonoBehaviour
{
    [Header("Errance")]
    [SerializeField] private float rayonErrance = 10f;
    [SerializeField] private Vector2 pauseMinMax = new Vector2(1f, 3f);

    [Header("Déplacement")]
    [Tooltip("Vitesse du clip de marche (m/s). À ajuster jusqu'à ce que les pieds ne glissent plus.")]
    [SerializeField] private float vitesseMarche = 1.3f;
    [Tooltip("Temps (en secondes) pour faire un demi-tour complet")]
    [SerializeField] private float dureeDemiTour = 0.75f;
    [Tooltip("Au-delà de cet angle, le PNJ pivote sur place avant d'avancer")]
    [SerializeField] private float angleMaxPourAvancer = 45f;

    [Header("Animation (optionnel)")]
    [SerializeField] private Animator animator;
    [Tooltip("Nom exact du paramètre Bool dans l'Animator Controller")]
    [SerializeField] private string parametreMarche = "walking";

    // Vitesse (m/s) sous laquelle le PNJ est considéré immobile
    private const float SeuilMarche = 0.1f;

    private NavMeshAgent agent;
    private Vector3 centreErrance;      // point fixe autour duquel le PNJ erre
    private float heureDepart = -1f;
    private int idMarche;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;   // la rotation est gérée par ce script
        agent.speed = vitesseMarche;
        idMarche = Animator.StringToHash(parametreMarche);
        if (animator != null) animator.applyRootMotion = false;
    }

    private void Start()
    {
        centreErrance = transform.position;

        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"{name} n'est pas sur le NavMesh : vérifier le Bake et la position du personnage.", this);
            enabled = false;
            return;
        }
        ChoisirDestination();
    }

    private void Update()
    {
        Orienter();

        // L'Animator reflète toujours l'état réel du déplacement
        if (animator != null)
            animator.SetBool(idMarche, agent.velocity.magnitude > SeuilMarche);

        bool arrive = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;
        if (!arrive) return;

        if (heureDepart < 0f)
            heureDepart = Time.time + Random.Range(pauseMinMax.x, pauseMinMax.y);
        else if (Time.time >= heureDepart)
        {
            ChoisirDestination();
            heureDepart = -1f;
        }
    }

    /// <summary>
    /// Tourne le PNJ vers le prochain coin du chemin.
    /// Tant que l'écart d'angle est grand, l'agent est freiné : il pivote presque sur place.
    /// </summary>
    private void Orienter()
    {
        if (!agent.hasPath) { agent.speed = vitesseMarche; return; }

        Vector3 direction = agent.steeringTarget - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion cible = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, cible, (180f / dureeDemiTour) * Time.deltaTime);

        // 1 quand le PNJ fait face à sa direction, 0 quand l'écart dépasse angleMaxPourAvancer
        float angle = Quaternion.Angle(transform.rotation, cible);
        float facteur = Mathf.Clamp01(1f - angle / angleMaxPourAvancer);
        agent.speed = vitesseMarche * facteur;
    }

    private void ChoisirDestination()
    {
        // Point au hasard dans un disque horizontal autour du centre fixe
        Vector2 hasard = Random.insideUnitCircle * rayonErrance;
        Vector3 point = centreErrance + new Vector3(hasard.x, 0f, hasard.y);
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, rayonErrance, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    // Affiche la zone d'errance et la destination dans la vue Scene (sélectionner le PNJ)
    private void OnDrawGizmosSelected()
    {
        // Zone d'errance : visible même hors Play.
        // En Play, elle reste au point de départ du PNJ.
        Vector3 centre = Application.isPlaying ? centreErrance : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(centre, rayonErrance);

        // Destination courante (en Play seulement)
        if (agent == null || !agent.hasPath) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position, agent.destination);
        Gizmos.DrawSphere(agent.destination, 0.2f);
    }
}
