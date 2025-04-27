using UnityEngine;
using UnityEngine.AI;

public class TestMove : MonoBehaviour
{
    public Transform target;

    private NavMeshAgent agent;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.SetDestination(target.position);
    }
}
