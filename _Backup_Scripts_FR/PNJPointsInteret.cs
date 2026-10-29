using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class PNJPointsInteret : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Transform[] pointsInteret;
    [SerializeField] private Vector2 pauseMinMax = new Vector2(2f, 6f);

    private NavMeshAgent agent;
    private float heureDepart = -1f;
    private Transform pointActuel;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        // Petite variation pour éviter des NPC tous identiques
        agent.speed *= Random.Range(0.9f, 1.1f);
        agent.avoidancePriority = Random.Range(30, 70);
    }

    private void Start() => AllerVersPointAleatoire();

    private void Update()
    {
        bool arrive = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;

        if (arrive)
        {
            if (heureDepart < 0f)
                heureDepart = Time.time + Random.Range(pauseMinMax.x, pauseMinMax.y);
            else if (Time.time >= heureDepart)
            {
                AllerVersPointAleatoire();
                heureDepart = -1f;
            }
        }

        animator.SetFloat("Vitesse", agent.velocity.magnitude, 0.1f, Time.deltaTime);
    }

    private void AllerVersPointAleatoire()
    {
        if (pointsInteret.Length == 0) return;

        Transform prochain;
        do { prochain = pointsInteret[Random.Range(0, pointsInteret.Length)]; }
        while (prochain == pointActuel && pointsInteret.Length > 1);   // pas deux fois le même

        pointActuel = prochain;
        agent.SetDestination(pointActuel.position);
    }
}