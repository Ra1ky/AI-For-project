using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

public enum GuardState
{
    Patrol,
    Investigating,
    Greeting,
    Combat
}

public class GuardAI : MonoBehaviour
{
    [Header("Components")]
    public NavMeshAgent agent;
    public Transform player;
    public LayerMask obstacleMask;
    private Renderer meshRenderer;

    [Header("UI Detection Bar")]
    public Slider detectionSlider;
    public Canvas detectionCanvas;
    public Camera mainCamera;

    [Header("Patrol")]
    public Transform[] waypoints;
    private int currentWaypointIndex = 0;

    [Header("Vision & Settings")]
    public float viewDistance = 12f;
    [Range(0, 360)] public float viewAngle = 70f;
    public float detectionLevel = 0f;
    public float detectionSpeed = 40f;

    [Header("Memory & Combat Pursuit")]
    [Tooltip("seconds to remember player position after line of sight is lost")]
    public float combatMemoryDuration = 3.5f;
    [Tooltip("seconds to search target point before going back to patrol")]
    public float searchTimeAtPoint = 5f;

    private float timeSinceLostPlayer = 0f;
    private float searchTimer = 0f;

    [Header("Alert Settings")]
    public float alertRadius = 15f;
    public float greetingRadius = 2.5f;

    [Header("Current State")]
    public GuardState currentState = GuardState.Patrol;

    [Header("Guard Traffic Avoidance")]
    public float guardAvoidanceRadius = 2.5f; // detection radius for incoming guards
    private float priorityResetTimer = 0f;

    private Vector3 lastInvestigationPoint;
    private bool isGreeting = false;
    private bool isSearchingAtPoint = false;


    void OnEnable() => AlertSystem.RegisterGuard(this);
    void OnDisable() => AlertSystem.UnregisterGuard(this);

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        meshRenderer = GetComponent<Renderer>();
        if (mainCamera == null) mainCamera = Camera.main;

        if (detectionSlider != null)
        {
            detectionSlider.minValue = 0f;
            detectionSlider.maxValue = 100f;
            detectionSlider.value = 0f;
        }

        GoToNextWaypoint();
    }

    void Update()
    {
        bool canSeePlayer = CheckVision();

        // build detection (non-combat states)
        if (canSeePlayer)
        {
            float dist = Vector3.Distance(transform.position, player.position);
            detectionLevel += (1f - (dist / viewDistance)) * detectionSpeed * Time.deltaTime;
            lastInvestigationPoint = player.position;
        }
        else if (currentState != GuardState.Combat)
        {
            if (!isSearchingAtPoint)
            {
                detectionLevel -= 15f * Time.deltaTime;
            }
        }

        detectionLevel = Mathf.Clamp(detectionLevel, 0f, 100f);

        UpdateDetectionUI();
        EvaluateStates(canSeePlayer);
        UpdateVisuals();
        ResolveGuardTraffic();
    }

    void EvaluateStates(bool canSeePlayer)
    {
        // trigger combat at 100% detection
        if (detectionLevel >= 100f && currentState != GuardState.Combat)
        {
            RaiseAlert();
            return;
        }

        switch (currentState)
        {
            case GuardState.Patrol:
                CheckForNearbyGuards();

                if (detectionLevel > 10f)
                {
                    currentState = GuardState.Investigating;
                }
                else if (!agent.pathPending && agent.remainingDistance < 0.5f)
                {
                    GoToNextWaypoint();
                }
                break;

            case GuardState.Investigating:
                if (canSeePlayer)
                {
                    agent.isStopped = true;
                    // rotate towards player
                    Vector3 targetDir = (player.position - transform.position).normalized;
                    targetDir.y = 0;
                    if (targetDir != Vector3.zero)
                    {
                        Quaternion targetRotation = Quaternion.LookRotation(targetDir);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
                    }
                }
                else
                {
                    agent.isStopped = false;
                    agent.SetDestination(lastInvestigationPoint);

                    if (!agent.pathPending && agent.remainingDistance < 0.8f && !isSearchingAtPoint)
                    {
                        StartCoroutine(SearchAtPointRoutine());
                    }
                }
                break;

            case GuardState.Greeting:
                agent.isStopped = true;
                break;

            case GuardState.Combat:
                agent.isStopped = false; // ensure agent is moving

                if (canSeePlayer)
                {
                    timeSinceLostPlayer = 0f;
                    searchTimer = 0f;
                    lastInvestigationPoint = player.position;

                    // calculate surround spot
                    Vector3 combatPos = GetCombatPosition();

                    // check navmesh validity
                    if (NavMesh.SamplePosition(combatPos, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                    {
                        agent.SetDestination(hit.position);
                    }
                    else
                    {
                        // fallback: head to player but stop within melee range (2m)
                        Vector3 dirToPlayer = (player.position - transform.position).normalized;
                        Vector3 fallbackPos = player.position - dirToPlayer * 2f;
                        agent.SetDestination(fallbackPos);
                    }

                    // face player even when close
                    Vector3 lookDir = (player.position - transform.position).normalized;
                    lookDir.y = 0;
                    if (lookDir != Vector3.zero)
                    {
                        Quaternion targetRot = Quaternion.LookRotation(lookDir);
                        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
                    }
                }
                else
                {
                    // memory pursuit after line of sight is broken
                    timeSinceLostPlayer += Time.deltaTime;

                    if (timeSinceLostPlayer <= combatMemoryDuration)
                    {
                        agent.SetDestination(player.position);
                        lastInvestigationPoint = player.position;
                    }
                    else
                    {
                        agent.SetDestination(lastInvestigationPoint);
                        searchTimer += Time.deltaTime;

                        if (searchTimer >= searchTimeAtPoint)
                        {
                            timeSinceLostPlayer = 0f;
                            searchTimer = 0f;
                            detectionLevel = 0f;
                            currentState = GuardState.Patrol;
                            GoToNextWaypoint();
                        }
                    }
                }
                break;
        }
    }

    void ResolveGuardTraffic()
    {
        if (currentState != GuardState.Patrol && currentState != GuardState.Investigating) return;

        Collider[] nearbyGuards = Physics.OverlapSphere(transform.position, guardAvoidanceRadius);

        foreach (var col in nearbyGuards)
        {
            if (col.gameObject != gameObject && col.TryGetComponent<GuardAI>(out var otherGuard))
            {
                if (otherGuard.currentState == GuardState.Patrol)
                {
                    // unique object id without obsolete warnings
                    if (this.GetHashCode() < otherGuard.GetHashCode())
                    {
                        agent.avoidancePriority = 60;
                        agent.speed = Mathf.Lerp(agent.speed, 1.5f, Time.deltaTime * 2f);
                    }
                    else
                    {
                        agent.avoidancePriority = 30;
                    }

                    priorityResetTimer = 1.5f;
                    return;
                }
            }
        }

        if (priorityResetTimer > 0)
        {
            priorityResetTimer -= Time.deltaTime;
        }
        else
        {
            agent.avoidancePriority = 50;
            agent.speed = 1.3f;
        }
    }

    IEnumerator SearchAtPointRoutine()
    {
        isSearchingAtPoint = true;
        yield return new WaitForSeconds(3f);

        if (currentState == GuardState.Investigating && !CheckVision())
        {
            detectionLevel = 0f;
            isSearchingAtPoint = false;
            currentState = GuardState.Patrol;
            GoToNextWaypoint();
        }
    }

    void UpdateDetectionUI()
    {
        if (detectionSlider != null)
        {
            detectionSlider.value = detectionLevel;

            if (detectionCanvas != null)
            {
                detectionCanvas.gameObject.SetActive(detectionLevel > 0f);
            }
        }

        if (detectionCanvas != null && mainCamera != null)
        {
            detectionCanvas.transform.LookAt(detectionCanvas.transform.position + mainCamera.transform.rotation * Vector3.forward,
                                             mainCamera.transform.rotation * Vector3.up);
        }
    }

    bool CheckVision()
    {
        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer < viewDistance)
        {
            // field of view check
            if (Vector3.Angle(transform.forward, dirToPlayer) < viewAngle / 2f)
            {
                // line of sight check
                if (!Physics.Raycast(transform.position, dirToPlayer, distanceToPlayer, obstacleMask))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public void OnHearNoise(Vector3 noisePosition)
    {
        Debug.Log($"[NOISE HEARD] {gameObject.name} heard noise at: {noisePosition}");
        if (currentState == GuardState.Patrol || currentState == GuardState.Investigating)
        {
            lastInvestigationPoint = noisePosition;
            detectionLevel = Mathf.Max(detectionLevel, 25f);
            currentState = GuardState.Investigating;
        }
    }

    void RaiseAlert()
    {
        currentState = GuardState.Combat;
        detectionLevel = 100f;
        AlertSystem.BroadcastAlert(transform.position, alertRadius, this);
    }

    public void ReceiveGlobalAlert(Vector3 dangerPosition)
    {
        if (currentState != GuardState.Combat)
        {
            currentState = GuardState.Combat;
            detectionLevel = 100f;
            lastInvestigationPoint = dangerPosition;
        }
    }

    Vector3 GetCombatPosition()
    {
        // pick sector (0, 45, 90...) via hashcode
        int sector = Mathf.Abs(GetHashCode()) % 8;
        float angle = sector * 45f;

        // ring distance around player
        float attackDistance = 1.4f;

        float rad = angle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), 0, Mathf.Sin(rad)) * attackDistance;

        return player.position + offset;
    }

    void CheckForNearbyGuards()
    {
        if (isGreeting) return;

        Collider[] nearby = Physics.OverlapSphere(transform.position, greetingRadius);
        foreach (var col in nearby)
        {
            if (col.gameObject != gameObject && col.TryGetComponent<GuardAI>(out var otherGuard))
            {
                if (otherGuard.currentState == GuardState.Patrol && !otherGuard.isGreeting)
                {
                    StartCoroutine(PerformGreeting(otherGuard));
                    break;
                }
            }
        }
    }

    IEnumerator PerformGreeting(GuardAI partner)
    {
        isGreeting = true;
        partner.isGreeting = true;

        currentState = GuardState.Greeting;
        partner.currentState = GuardState.Greeting;

        transform.LookAt(partner.transform.position);
        partner.transform.LookAt(transform.position);

        yield return new WaitForSeconds(2.5f);

        agent.isStopped = false;
        partner.agent.isStopped = false;

        currentState = GuardState.Patrol;
        partner.currentState = GuardState.Patrol;

        yield return new WaitForSeconds(10f);
        isGreeting = false;
        partner.isGreeting = false;
    }

    void GoToNextWaypoint()
    {
        if (waypoints.Length == 0) return;
        agent.isStopped = false;
        agent.SetDestination(waypoints[currentWaypointIndex].position);
        currentWaypointIndex = (currentWaypointIndex + 1) % waypoints.Length;
    }

    void UpdateVisuals()
    {
        switch (currentState)
        {
            case GuardState.Patrol: meshRenderer.material.color = Color.green; break;
            case GuardState.Greeting: meshRenderer.material.color = Color.blue; break;
            case GuardState.Investigating: meshRenderer.material.color = Color.yellow; break;
            case GuardState.Combat: meshRenderer.material.color = Color.red; break;
        }
    }
}