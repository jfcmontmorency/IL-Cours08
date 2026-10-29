using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class NPCPointsOfInterest : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Transform[] pointsOfInterest;
    [SerializeField] private Vector2 pauseMinMax = new Vector2(2f, 6f);

    private NavMeshAgent agent;
    private float departureTime = -1f;
    private Transform currentPoint;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        // Petite variation pour éviter des NPC tous identiques
        agent.speed *= Random.Range(0.9f, 1.1f);
        agent.avoidancePriority = Random.Range(30, 70);
    }

    private void Start() => GoToRandomPoint();

    private void Update()
    {
        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance;

        if (arrived)
        {
            if (departureTime < 0f)
                departureTime = Time.time + Random.Range(pauseMinMax.x, pauseMinMax.y);
            else if (Time.time >= departureTime)
            {
                GoToRandomPoint();
                departureTime = -1f;
            }
        }

        // "Vitesse" : nom du paramètre Float dans l'Animator Controller
        animator.SetFloat("Vitesse", agent.velocity.magnitude, 0.1f, Time.deltaTime);
    }

    private void GoToRandomPoint()
    {
        if (pointsOfInterest.Length == 0) return;

        Transform next;
        do { next = pointsOfInterest[Random.Range(0, pointsOfInterest.Length)]; }
        while (next == currentPoint && pointsOfInterest.Length > 1);   // pas deux fois le même

        currentPoint = next;
        agent.SetDestination(currentPoint.position);
    }
}
