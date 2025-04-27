using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;
using TMPro;
using System.Collections;

public class MonsterAI : MonoBehaviour
{
    #region Variables

    // State Mashine
    public enum MonsterState { Guard, Attack, Danger }
    public MonsterState currentState = MonsterState.Guard;

    [Header("Patrol Settings")]
    public List<Transform> patrolPoints; // lista punktów patrolu
    private int currentPatrolIndex;

    private NavMeshAgent agent;
    private Transform targetEnemy;
    private readonly List<Transform> enemiesInRange = new();

    [Header("State Settings")] // prędkości jednostek w różnych stanach
    [SerializeField] private float patrolSpeed = 3.5f;
    [SerializeField] private float attackSpeed = 5f;

    [Header("Visual Cues")]
    [SerializeField] private TextMeshPro stateText;
    [SerializeField] private string patrolText = "Patrol";
    [SerializeField] private string dangerText = "Danger";
    [SerializeField] private string attackText = "Attack";
    private Renderer bodyRenderer;
    [Space]
    [SerializeField] private Color patrolColor;
    [SerializeField] private Color dangerColor;
    [SerializeField] private Color attackColor;
    private Material bodyMaterial;

    [Header("Vision Settings")]
    [SerializeField] private float viewRadius = 10f;
    [SerializeField] private float viewAngle = 120f;
    private float closeRangeRadius = 3f; // zasięg wykrywania jednostek w obszarze 360 stopni
    [SerializeField] private LayerMask enemyMask;
    [SerializeField] private LayerMask allyMask;
    [SerializeField] private LayerMask obstructionMask;
    [Space]
    [SerializeField] private float scanInterval = 0.5f;
    [SerializeField] private float threatIdentifyTime = 1f;
    [SerializeField] private float targetLoseTime = 2f;
    private float loseTargetTimer = 0f;
    private float startViewRadius;
    private float startViewAngle;

    [Header("Attack Settings")]
    [SerializeField] private float attackDamage = 15f;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1f;
    private bool isAttacking;

    #endregion

    private void Start()
    {
        bodyRenderer = GetComponent<Renderer>();
        agent = GetComponent<NavMeshAgent>();
        bodyMaterial = bodyRenderer.material;

        startViewRadius = viewRadius;
        startViewAngle = viewAngle;

        if (patrolPoints.Count > 0)
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);

        InvokeRepeating(nameof(ScanForEnemies), 0f, scanInterval);
    }

    private void Update()
    {
        UpdateVisualCues();
        AvoidAllies();
        UpdateEnemiesInRange();

        switch (currentState)
        {
            case MonsterState.Guard:
                Patrol();
                break;
            case MonsterState.Danger:
                agent.isStopped = true;
                break;
            case MonsterState.Attack:
                if (targetEnemy != null) Attack();
                else ReturnToPatrol();
                break;
        }
    }

    #region Main Behaviors

    private void Patrol()
    {
        if (agent.remainingDistance <= agent.stoppingDistance && !agent.pathPending)
        {
            if (patrolPoints.Count > 0)
            {
                currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Count;
                agent.SetDestination(patrolPoints[currentPatrolIndex].position);
            }
        }
    }

    private void Attack()
    {
        if (targetEnemy == null || targetEnemy.GetComponent<Health>()?.currentHealth <= 0)
        {
            FindNextTarget();
            if (targetEnemy == null)
            {
                ReturnToPatrol();
                return;
            }
        }

        agent.SetDestination(targetEnemy.position);
        agent.speed = attackSpeed;
        viewAngle = 360f;

        CheckTargetVisibility();
        AlertNearbyAllies(targetEnemy);

        if (targetEnemy != null && !isAttacking && Vector3.Distance(transform.position, targetEnemy.position) <= attackRange)
        {
            StartCoroutine(AttackTarget());
        }
    }

    private IEnumerator AttackTarget()
    {
        isAttacking = true;

        if (targetEnemy != null)
        {
            Health targetHealth = targetEnemy.GetComponent<Health>();
            if (targetHealth != null)
            {
                targetHealth.TakeDamage(attackDamage);
                Debug.Log($"{gameObject.name} zadał {attackDamage} obrażeń {targetEnemy.name}");
                if (targetHealth.currentHealth <= 0)
                    targetEnemy = null;
            }
        }

        yield return new WaitForSeconds(attackCooldown);
        isAttacking = false;
    }

    private void FindNextTarget()
    {
        enemiesInRange.RemoveAll(e => e == null);

        if (enemiesInRange.Count > 0)
        {
            Transform closestEnemy = enemiesInRange[0];
            float closestDistance = Vector3.Distance(transform.position, closestEnemy.position);

            foreach (var enemy in enemiesInRange)
            {
                float distance = Vector3.Distance(transform.position, enemy.position);
                if (distance < closestDistance)
                {
                    closestEnemy = enemy;
                    closestDistance = distance;
                }
            }

            targetEnemy = closestEnemy;
        }
        else
        {
            targetEnemy = null;
        }
    }

    private void ReturnToPatrol()
    {
        viewRadius = startViewRadius;
        viewAngle = startViewAngle;
        agent.speed = patrolSpeed;

        currentState = MonsterState.Guard;
        targetEnemy = null;
        agent.isStopped = false;

        if (patrolPoints.Count > 0)
            agent.SetDestination(patrolPoints[currentPatrolIndex].position);
    }

    #endregion

    #region Vision and Awareness

    private void ScanForEnemies()
    {
        if (currentState != MonsterState.Guard) return;

        // field of view razem z zasięgiem widzenia, w którym jednostka zauważa wroge jednostki
        Collider[] targetsInViewRadius = Physics.OverlapSphere(transform.position, viewRadius, enemyMask);

        foreach (var target in targetsInViewRadius)
        {
            Vector3 dirToTarget = (target.transform.position - transform.position).normalized;
            float dstToTarget = Vector3.Distance(transform.position, target.transform.position);

            if (Vector3.Angle(transform.forward, dirToTarget) < viewAngle / 2 &&
                !Physics.Raycast(transform.position, dirToTarget, dstToTarget, obstructionMask))
            {
                currentState = MonsterState.Danger;
                agent.isStopped = true;
                StartCoroutine(IdentifyThreat(target.transform));
                return;
            }
        }

        // na bliskim dystansie jednostka widzi wokół siebie
        Collider[] targetsInCloseRange = Physics.OverlapSphere(transform.position, closeRangeRadius, enemyMask);

        foreach (var target in targetsInCloseRange)
        {
            float dstToTarget = Vector3.Distance(transform.position, target.transform.position);

            if (!Physics.Raycast(transform.position, (target.transform.position - transform.position).normalized, dstToTarget, obstructionMask))
            {
                currentState = MonsterState.Danger;
                agent.isStopped = true;
                StartCoroutine(IdentifyThreat(target.transform));
                return;
            }
        }
    }

    private IEnumerator IdentifyThreat(Transform enemy)
    {
        yield return new WaitForSeconds(threatIdentifyTime);

        if (enemy == null || enemy.GetComponent<Health>()?.currentHealth <= 0)
        {
            ReturnToPatrol();
            yield break;
        }

        Vector3 dirToEnemy = (enemy.position - transform.position).normalized;
        float dstToEnemy = Vector3.Distance(transform.position, enemy.position);

        bool isObstructed = Physics.Raycast(transform.position, dirToEnemy, dstToEnemy, obstructionMask);

        if (!isObstructed)
        {
            currentState = MonsterState.Attack;
            targetEnemy = enemy;
            agent.isStopped = false;
            AlertNearbyAllies(enemy);
        }
        else
        {
            ReturnToPatrol();
        }
    }

    private void CheckTargetVisibility()
    {
        if (targetEnemy == null || targetEnemy.GetComponent<Health>()?.currentHealth <= 0)
        {
            ReturnToPatrol();
            return;
        }

        Vector3 dirToTarget = (targetEnemy.position - transform.position).normalized;
        float dstToTarget = Vector3.Distance(transform.position, targetEnemy.position);

        bool isObstructed = Physics.Raycast(transform.position, dirToTarget, dstToTarget, obstructionMask);
        bool isOutOfRange = dstToTarget > viewRadius;

        if (isObstructed || isOutOfRange)
        {
            loseTargetTimer += Time.deltaTime;

            if (loseTargetTimer >= targetLoseTime)
            {
                ReturnToPatrol();
            }
        }
        else
        {
            loseTargetTimer = 0f;
        }
    }

    private void AlertNearbyAllies(Transform enemy)
    {
        Collider[] allies = Physics.OverlapSphere(transform.position, viewRadius, allyMask);

        foreach (var ally in allies)
        {
            MonsterAI ai = ally.GetComponent<MonsterAI>();
            if (ai != null && ai != this)
                ai.OnThreatSpotted(enemy);
        }
    }

    public void OnThreatSpotted(Transform enemy)
    {
        if (currentState == MonsterState.Guard)
        {
            currentState = MonsterState.Danger;
            targetEnemy = enemy;
            agent.isStopped = true;
            StartCoroutine(IdentifyThreat(enemy));
        }
    }

    #endregion

    #region Helpers

    private void UpdateEnemiesInRange() // aktualizacja listy przeciwników w zasięgu wzroku
    {
        Collider[] targets = Physics.OverlapSphere(transform.position, viewRadius, enemyMask);
        HashSet<Transform> detected = new();

        foreach (var target in targets)
        {
            var health = target.GetComponent<Health>();
            if (health != null && health.currentHealth > 0)
            {
                detected.Add(target.transform);
                if (!enemiesInRange.Contains(target.transform))
                    enemiesInRange.Add(target.transform);
            }
        }

        enemiesInRange.RemoveAll(e => !detected.Contains(e));
    }

    private void UpdateVisualCues()
    {
        if (stateText == null || bodyMaterial == null) return;

        switch (currentState)
        {
            case MonsterState.Guard:
                stateText.text = patrolText;
                bodyMaterial.color = patrolColor;
                break;
            case MonsterState.Danger:
                stateText.text = dangerText;
                bodyMaterial.color = dangerColor;
                break;
            case MonsterState.Attack:
                stateText.text = attackText;
                bodyMaterial.color = attackColor;
                break;
        }
    }

    private void AvoidAllies() // zapobieganie blokowaniu się jednostek na sobie
    {
        float avoidRadius = 1f;
        float avoidForce = 5f;

        Collider[] allies = Physics.OverlapSphere(transform.position, avoidRadius, 1 << gameObject.layer);
        Vector3 avoidDir = Vector3.zero;
        int count = 0;

        foreach (var ally in allies)
        {
            if (ally.gameObject != gameObject)
            {
                avoidDir += (transform.position - ally.transform.position).normalized;
                count++;
            }
        }

        if (count > 0)
            agent.Move(avoidDir.normalized * avoidForce * Time.deltaTime);
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmosSelected() // Gizmosy do debugu
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        Vector3 viewAngleA = DirectionFromAngle(-viewAngle / 2);
        Vector3 viewAngleB = DirectionFromAngle(viewAngle / 2);
        Vector3 pos = transform.position;

        Gizmos.color = Color.blue;
        Gizmos.DrawLine(pos, pos + viewAngleA * viewRadius);
        Gizmos.DrawLine(pos, pos + viewAngleB * viewRadius);

        if (targetEnemy != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, targetEnemy.position);
        }
    }

    private Vector3 DirectionFromAngle(float angle)
    {
        angle += transform.eulerAngles.y;
        return new Vector3(Mathf.Sin(angle * Mathf.Deg2Rad), 0, Mathf.Cos(angle * Mathf.Deg2Rad));
    }

    #endregion
}