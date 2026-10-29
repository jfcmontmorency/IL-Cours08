using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Le PNJ suit une cible (le joueur) sur le NavMesh.
/// - Deux seuils de distance : il repart au-delà de distanceDepart, s'arrête en deçà de distanceArret.
/// - Le chemin est recalculé à intervalle régulier, pas à chaque image.
/// - Il court quand il est loin (si un paramètre de course est fourni).
/// - Il abandonne quand la cible est trop loin, et reprend quand elle revient.
/// - Option : après un abandon, il erre au hasard autour de l'endroit où il a perdu la cible.
/// - Il se tourne avant d'avancer et, à l'arrêt, se tourne vers la cible.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class PNJSuiveur : MonoBehaviour
{
    [Header("Cible")]
    [Tooltip("Vide = l'objet qui a le tag Player")]
    [SerializeField] private Transform cible;

    [Header("Distances (m)")]
    [Tooltip("Le PNJ se remet à suivre quand la cible est plus loin que cette distance")]
    [SerializeField] private float distanceDepart = 3f;
    [Tooltip("Le PNJ s'arrête quand il est plus près que cette distance")]
    [SerializeField] private float distanceArret = 1.5f;
    [Tooltip("Au-delà de cette distance, le PNJ court")]
    [SerializeField] private float distanceCourse = 6f;
    [Tooltip("Au-delà de cette distance, le PNJ abandonne et reste sur place. 0 = il suit toujours")]
    [SerializeField] private float distanceAbandon = 15f;

    [Header("Errance après un abandon")]
    [Tooltip("Coché = le PNJ erre au hasard quand il a perdu la cible. Nécessite une Distance Abandon plus grande que 0")]
    [SerializeField] private bool errerQuandPerdu = false;
    [Tooltip("Distance maximale du prochain point, autour de l'endroit où la cible a été perdue")]
    [SerializeField] private float rayonErrance = 8f;
    [Tooltip("Durée de la pause à chaque arrivée, entre deux valeurs (en secondes)")]
    [SerializeField] private Vector2 pauseMinMax = new Vector2(1f, 3f);

    [Header("Déplacement")]
    [SerializeField] private float vitesseMarche = 1.3f;
    [SerializeField] private float vitesseCourse = 4f;
    [Tooltip("Temps (en secondes) pour faire un demi-tour complet")]
    [SerializeField] private float dureeDemiTour = 0.75f;
    [Tooltip("Au-delà de cet angle, le PNJ pivote sur place avant d'avancer")]
    [SerializeField] private float angleMaxPourAvancer = 45f;

    [Header("Animation (optionnel)")]
    [SerializeField] private Animator animator;
    [Tooltip("Nom exact du Bool de marche dans l'Animator Controller")]
    [SerializeField] private string parametreMarche = "walking";
    [Tooltip("Nom exact du Bool de course. Vide = le PNJ ne court jamais")]
    [SerializeField] private string parametreCourse = "";
    [Tooltip("Vitesse (m/s) à laquelle le clip de marche colle au sol : Average Velocity Z du clip. 0 = vitesse d'animation non ajustée")]
    [SerializeField] private float vitesseClipMarche = 1.3f;
    [Tooltip("Vitesse (m/s) à laquelle le clip de course colle au sol : Average Velocity Z du clip. 0 = vitesse d'animation non ajustée")]
    [SerializeField] private float vitesseClipCourse = 4f;

    // Temps entre deux calculs de chemin (en secondes)
    private const float IntervalleRecalcul = 0.2f;
    // Vitesse (m/s) sous laquelle le PNJ est considéré immobile
    private const float SeuilMarche = 0.1f;
    // Après un abandon, la cible doit revenir à (distanceAbandon - cette marge) pour que le PNJ reprenne
    private const float MargeReprise = 2f;
    // Limites de la vitesse de lecture de l'animation (1 = vitesse normale du clip)
    private const float VitesseAnimMin = 0.5f;
    private const float VitesseAnimMax = 2.5f;

    private NavMeshAgent agent;
    private bool suit;
    private bool court;
    private bool perdu;
    private Vector3 origineErrance;
    private bool enPause;
    private float finPause;
    private float prochainRecalcul;
    private float vitesseVoulue;
    private bool peutCourir;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.updateRotation = false;   // la rotation est gérée par ce script
        // Un peu plus court que distanceArret : l'agent franchit vraiment le seuil d'arrêt
        agent.stoppingDistance = distanceArret * 0.8f;
        peutCourir = !string.IsNullOrEmpty(parametreCourse);
        if (animator != null) animator.applyRootMotion = false;
    }

    private void Start()
    {
        if (cible == null)
        {
            GameObject joueur = GameObject.FindGameObjectWithTag("Player");
            if (joueur != null) cible = joueur.transform;
        }

        if (cible == null)
        {
            Debug.LogWarning($"{name} n'a pas de cible : glisser le joueur dans le champ Cible, ou lui donner le tag Player.", this);
            enabled = false;
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning($"{name} n'est pas sur le NavMesh : vérifier le Bake et la position du personnage.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        float distance = Vector3.Distance(transform.position, cible.position);

        // Abandon : la cible est trop loin, le PNJ reste sur place jusqu'à ce qu'elle revienne
        if (distanceAbandon > 0f)
        {
            if (!perdu && distance > distanceAbandon)
            {
                perdu = true;
                suit = false;
                court = false;
                agent.ResetPath();
                // L'errance commence par une pause : le PNJ « cherche » avant de repartir
                origineErrance = transform.position;
                enPause = true;
                finPause = Time.time + Random.Range(pauseMinMax.x, pauseMinMax.y);
            }
            else if (perdu && distance < distanceAbandon - MargeReprise)
            {
                perdu = false;
                agent.ResetPath();   // abandonne le point d'errance en cours
            }
        }

        // Hystérésis : deux seuils différents pour partir et pour s'arrêter
        if (!perdu && !suit && distance > distanceDepart)
        {
            suit = true;
            prochainRecalcul = 0f;   // calcul immédiat au départ
        }
        else if (suit && distance <= distanceArret)
        {
            suit = false;
            court = false;
            agent.ResetPath();
        }

        if (suit)
        {
            // Courir quand la cible est loin, revenir à la marche quand elle se rapproche
            if (peutCourir)
            {
                if (!court && distance > distanceCourse) court = true;
                else if (court && distance < distanceDepart) court = false;
            }
            vitesseVoulue = court ? vitesseCourse : vitesseMarche;

            if (Time.time >= prochainRecalcul)
            {
                agent.SetDestination(cible.position);
                prochainRecalcul = Time.time + IntervalleRecalcul;
            }

            OrienterVers(agent.steeringTarget, true);
        }
        else if (perdu && errerQuandPerdu)
        {
            Errer();
        }
        else if (!perdu)
        {
            // À l'arrêt : se tourner vers la cible (pas quand elle est perdue)
            OrienterVers(cible.position, false);
        }

        MettreAJourAnimation();
    }

    /// <summary>
    /// Errance : marcher vers un point au hasard autour de origineErrance, faire une pause, recommencer.
    /// </summary>
    private void Errer()
    {
        vitesseVoulue = vitesseMarche;
        if (agent.pathPending) return;

        bool arrive = !agent.hasPath || agent.remainingDistance <= agent.stoppingDistance;
        if (!arrive)
        {
            OrienterVers(agent.steeringTarget, true);
            return;
        }

        if (!enPause)
        {
            enPause = true;
            finPause = Time.time + Random.Range(pauseMinMax.x, pauseMinMax.y);
        }
        else if (Time.time >= finPause)
        {
            // Point au hasard ramené sur le NavMesh, dans les zones permises à l'agent (ex. : pas dans la rue)
            Vector3 point = origineErrance + Random.insideUnitSphere * rayonErrance;
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, rayonErrance, agent.areaMask))
            {
                agent.SetDestination(hit.position);
                enPause = false;
            }
        }
    }

    /// <summary>
    /// Tourne le PNJ vers un point. En déplacement, l'agent est freiné tant que
    /// l'écart d'angle est grand : le PNJ se tourne d'abord, puis avance.
    /// </summary>
    private void OrienterVers(Vector3 point, bool enDeplacement)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) return;

        Quaternion rotationVoulue = Quaternion.LookRotation(direction);
        transform.rotation = Quaternion.RotateTowards(transform.rotation, rotationVoulue, (180f / dureeDemiTour) * Time.deltaTime);

        if (enDeplacement)
        {
            float angle = Quaternion.Angle(transform.rotation, rotationVoulue);
            float facteur = Mathf.Clamp01(1f - angle / angleMaxPourAvancer);
            agent.speed = vitesseVoulue * facteur;
        }
    }

    private void MettreAJourAnimation()
    {
        if (animator == null) return;

        bool bouge = agent.velocity.magnitude > SeuilMarche;
        animator.SetBool(parametreMarche, bouge);
        if (peutCourir) animator.SetBool(parametreCourse, bouge && court);

        // Vitesse de lecture : l'animation accélère ou ralentit avec le déplacement réel,
        // pour que les pieds ne glissent pas sur le sol
        float vitesseClip = court ? vitesseClipCourse : vitesseClipMarche;
        if (bouge && vitesseClip > 0f)
            animator.speed = Mathf.Clamp(agent.velocity.magnitude / vitesseClip, VitesseAnimMin, VitesseAnimMax);
        else
            animator.speed = 1f;   // au repos, l'animation d'attente joue normalement
    }

    // Affiche les deux seuils dans la vue Scene (sélectionner le PNJ)
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, distanceArret);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, distanceDepart);
        if (!string.IsNullOrEmpty(parametreCourse))
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, distanceCourse);
        }
        if (distanceAbandon > 0f)
        {
            Gizmos.color = Color.gray;
            Gizmos.DrawWireSphere(transform.position, distanceAbandon);
        }
    }
}
