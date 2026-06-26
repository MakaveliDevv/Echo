using System.Runtime.InteropServices;
using System.Collections.Generic;
using Assets.EchoProtocol.Scripts.Core;
using Assets.EchoProtocol.Scripts.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Assets.EchoProtocol.Scripts.Enemies
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUEnemy
    {
        public Vector2 position;
        public Vector2 velocity;
        public Vector2 targetPosition; 
        public float maxSpeed;
        public float detectionDistance;
        public float attackDistance;

        public int state;
        public float stateTimer;
        public int patrolIndex;

        public int active;

        public float investigateVisual;
        public float chaseVisual;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GPUObstacle
    {
        public Vector2 center;
        public Vector2 halfSize;
    }

    public enum GPUEnemyState
    {
        Dormant = 0,

        Patrol = 1,

        Investigate = 2,

        Chase = 3,

        Search = 4,

        ReturnToPatrol = 5
    }

    public class EnemyComputeController : MonoBehaviour
    {
        private const int MaxNavigationPointsOnGpu = 512;
        private const int MaxPatrolPointsOnGpu = 512;

        [Header("References")]
        [SerializeField] private Transform player;
        [SerializeField] private ScannerController scanner;
        [SerializeField] private GameManager gameManager;

        [Header("Compute")]
        [SerializeField] private ComputeShader enemyComputeShader;

        [Header("Rendering")]
        [SerializeField] private Mesh enemyMesh;
        [SerializeField] private Material enemyMaterial;

        [SerializeField] private float enemyHeight = 1f;
        [SerializeField] private float enemyScale = 1f;

        [Header("Enemies")]
        [SerializeField, Min(1)] private int enemyCount = 2;
        [SerializeField] private Transform[] startingPositions;
        [SerializeField] private bool startDormant;

        [Header("Enemy Behaviour")]
        [SerializeField] private Vector2 speedRange = new Vector2(2f, 3.5f);
        [SerializeField] private float detectionDistance = 5f;
        [SerializeField] private float attackDistance = 1.2f;
        [SerializeField] private float searchDuration = 4f; 
        [SerializeField] private float stoppingDistance = 0.5f;
        [SerializeField] private float steeringSpeed = 5f;

        [Header("Patrol Route")]
        [SerializeField, Min(2)] private int generatedPatrolPointCount = 48;
        [SerializeField, Min(1f)] private float patrolStepSearchRadius = 12f;
        [SerializeField] private bool drawGeneratedPatrolRoute = true;

        [Header("Navigation Grid")]
        [SerializeField, Min(0.5f)] private float navigationPointSpacing = 3f;
        [SerializeField, Min(16)] private int maxGeneratedNavigationPoints = 384;
        [SerializeField, Min(0f)] private float navigationPointObstaclePadding = 0.15f;
        [SerializeField] private bool drawGeneratedNavigationPoints = true;

        [Header("Obstacle Boxes")]
        [SerializeField] private BoxCollider[] obstacleColliders; 
        [SerializeField] private float enemyRadius = 0.4f;
        [SerializeField] private float probeDistance = 4f;

        [Header("World Bounds")]
        [SerializeField] private Vector2 worldMinimum = new(-30f, -30f);
        [SerializeField] private Vector2 worldMaximum = new(30f, 30f);

        [Header("GPU Readback")]
        [SerializeField, Min(0.02f)] private float attackCheckInterval = 0.1f;

        private ComputeBuffer enemyBuffer;
        private ComputeBuffer patrolPointBuffer;
        private ComputeBuffer navigationPointBuffer;
        private ComputeBuffer obstacleBuffer;
        private ComputeBuffer attackResultBuffer; 

        private int simulateKernel;
        private int resetAttackKernel;

        private Bounds drawBounds;

        private Vector2[] activeNavigationPoints = System.Array.Empty<Vector2>();
        private Vector2[] activePatrolPoints = System.Array.Empty<Vector2>();

        private bool scanWasTriggered;
        private Vector2 latestScanPosition;

        // AsyncGPUReadback state
        private bool readbackPending;
        private float nextAttackCheckTime;

        private static readonly int EnemiesId = Shader.PropertyToID("_Enemies");
        private static readonly int PatrolPointsId = Shader.PropertyToID("_PatrolPoints");
        private static readonly int NavigationPointsId = Shader.PropertyToID("_NavigationPoints");
        private static readonly int ObstaclesId = Shader.PropertyToID("_Obstacles");
        private static readonly int AttackResultId = Shader.PropertyToID("_AttackResult");

        private static readonly int EnemyCountId = Shader.PropertyToID("_EnemyCount");
        private static readonly int PatrolPointCountId = Shader.PropertyToID("_PatrolPointCount");
        private static readonly int NavigationPointCountId = Shader.PropertyToID("_NavigationPointCount");
        private static readonly int ObstacleCountId = Shader.PropertyToID("_ObstacleCount");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int PlayerPositionId = Shader.PropertyToID("_PlayerPosition");
        private static readonly int ScanPositionId = Shader.PropertyToID("_ScanPosition");
        private static readonly int ScanTriggeredId = Shader.PropertyToID("_ScanTriggered");
        private static readonly int SearchDurationId = Shader.PropertyToID("_SearchDuration");
        private static readonly int StoppingDistanceId = Shader.PropertyToID("_StoppingDistance");
        private static readonly int SteeringSpeedId = Shader.PropertyToID("_SteeringSpeed");
        private static readonly int EnemyRadiusId = Shader.PropertyToID("_EnemyRadius");
        private static readonly int ProbeDistanceId = Shader.PropertyToID("_ProbeDistance");
        private static readonly int EnemyHeightId = Shader.PropertyToID("_EnemyHeight");
        private static readonly int EnemyScaleId = Shader.PropertyToID("_EnemyScale");

        private void Start()
        {
            ValidateReferences();
            InitializeKernels();
            InitializeNavigationBuffer();
            InitializePatrolBuffer();
            InitializeEnemyBuffer();
            InitializeObstacleBuffer();
            InitializeAttackBuffer();
            InitializeComputeShader();
            InitializeRendering();
        }

        private void OnEnable()
        {
            if (scanner != null)
            {
                scanner.OnScanTriggered += HandleScan;
            }
        }

        private void OnDisable()
        {
            if (scanner != null)
            {
                scanner.OnScanTriggered -= HandleScan;
            }
        }

        private void Update()
        {
            if (enemyBuffer == null ||
                gameManager == null ||
                gameManager.GameHasEnded)
            {
                return;
            }
            
            UpdateComputeParameters();
            ResetAttackResult();
            DispatchSimulation();
            DrawEnemies();

            scanWasTriggered = false;

            if (Time.time >= nextAttackCheckTime)
            {
                RequestAttackReadback();
                nextAttackCheckTime = Time.time + attackCheckInterval;
            }
        }

        private void ValidateReferences()
        {
            if (player == null)
            {
                Debug.LogError("EnemyComputeController requires a Player.", this);
            }

            if (scanner == null)
            {
                Debug.LogError("EnemyComputeController requires a ScannerController.", this);
            }

            if (gameManager == null)
            {
                Debug.LogError("EnemyComputeController requires a GameManager.", this);
            }

            if (enemyComputeShader == null)
            {
                Debug.LogError("EnemyComputeController requires a compute shader.", this);
            }

            if (enemyMesh == null)
            {
                Debug.LogError("EnemyComputeController requires an enemy mesh.", this);
            }

            if (enemyMaterial == null)
            {
                Debug.LogError("EnemyComputeController requires an enemy material.", this);
            }
        }


        private void InitializeKernels()
        {
            simulateKernel = enemyComputeShader.FindKernel("SimulateEnemies");
            resetAttackKernel = enemyComputeShader.FindKernel("ResetAttackResult");
        }

        private void InitializeEnemyBuffer()
        {
            enemyCount = Mathf.Max(1, enemyCount);

            if (startingPositions != null && startingPositions.Length > 0)
            {
                enemyCount = Mathf.Max(enemyCount, startingPositions.Length);
            }

            // Marshal.SizeOf keeps the stride in sync with the C# struct layout.
            int stride = Marshal.SizeOf<GPUEnemy>();

            enemyBuffer = new ComputeBuffer(
                enemyCount,
                stride,
                ComputeBufferType.Structured
            );

            GPUEnemy[] enemies = new GPUEnemy[enemyCount];

            for (int i = 0; i < enemyCount; i++)
            {
                Vector2 startPosition = GetStartingPosition(i);
                GPUEnemyState initialState = startDormant ? GPUEnemyState.Dormant : GPUEnemyState.Patrol;

                int patrolIndex = GetClosestPatrolPointIndex(startPosition, i);

                enemies[i] = new GPUEnemy
                {
                    position = startPosition,
                    velocity = Vector2.zero,
                    targetPosition = startPosition,
                    maxSpeed = Random.Range(speedRange.x, speedRange.y),
                    detectionDistance = detectionDistance,
                    attackDistance = attackDistance,
                    state = (int)initialState,
                    stateTimer = 0f,
                    patrolIndex = patrolIndex,
                    active = 1,
                    investigateVisual = 0f,
                    chaseVisual = 0f,
                };
            }

            enemyBuffer.SetData(enemies);
        }

        private int GetClosestPatrolPointIndex(Vector2 position, int fallbackIndex)
        {
            if (activePatrolPoints == null || activePatrolPoints.Length == 0)
            {
                return 0;
            }

            int fallback = fallbackIndex % activePatrolPoints.Length;
            int bestVisibleIndex = -1;
            float bestVisibleDistance = float.MaxValue;
            int bestIndex = fallback;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < activePatrolPoints.Length; i++)
            {
                float sqrDistance = (activePatrolPoints[i] - position).sqrMagnitude;

                if (sqrDistance < bestDistance)
                {
                    bestDistance = sqrDistance;
                    bestIndex = i;
                }

                if (IsSegmentBlockedByObstacleColliders(
                        position,
                        activePatrolPoints[i],
                        navigationPointObstaclePadding))
                {
                    continue;
                }

                if (sqrDistance < bestVisibleDistance)
                {
                    bestVisibleDistance = sqrDistance;
                    bestVisibleIndex = i;
                }
            }

            return bestVisibleIndex >= 0 ? bestVisibleIndex : bestIndex;
        }

        private Vector2 GetStartingPosition(int index)
        {
            if (startingPositions != null && startingPositions.Length > 0)
            {
                Transform startTransform = startingPositions[index % startingPositions.Length];

                if (startTransform != null)
                {
                    Vector3 position = startTransform.position;
                    return new Vector2(position.x, position.z);
                }
            }

            if (activeNavigationPoints != null && activeNavigationPoints.Length > 0)
            {
                return activeNavigationPoints[index % activeNavigationPoints.Length];
            }

            for (int attempt = 0; attempt < 30; attempt++)
            {
                Vector2 randomPosition = new(
                    Random.Range(worldMinimum.x, worldMaximum.x),
                    Random.Range(worldMinimum.y, worldMaximum.y)
                );

                if (!IsBlockedByObstacleColliders(randomPosition))
                {
                    return randomPosition;
                }
            }

            return (worldMinimum + worldMaximum) * 0.5f;
        }

        private bool IsBlockedByObstacleColliders(Vector2 position, float extraPadding = 0f)
        {
            if (obstacleColliders == null)
            {
                return false;
            }

            foreach (BoxCollider obstacleCollider in obstacleColliders)
            {
                if (obstacleCollider == null)
                {
                    continue;
                }

                Bounds bounds = obstacleCollider.bounds;
                Vector2 center = new(bounds.center.x, bounds.center.z);
                Vector2 halfSize = new(bounds.extents.x, bounds.extents.z);
                Vector2 difference = Abs(position - center);

                Vector2 expandedHalfSize = halfSize + Vector2.one * (enemyRadius + extraPadding);

                if (difference.x <= expandedHalfSize.x &&
                    difference.y <= expandedHalfSize.y)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSegmentBlockedByObstacleColliders(
            Vector2 start,
            Vector2 end,
            float extraPadding = 0f)
        {
            if (obstacleColliders == null)
            {
                return false;
            }

            foreach (BoxCollider obstacleCollider in obstacleColliders)
            {
                if (obstacleCollider == null)
                {
                    continue;
                }

                Bounds bounds = obstacleCollider.bounds;
                Vector2 center = new(bounds.center.x, bounds.center.z);
                Vector2 halfSize = new(bounds.extents.x, bounds.extents.z);
                Vector2 expandedHalfSize =
                    halfSize +
                    Vector2.one * (enemyRadius + extraPadding + 0.15f);

                if (SegmentIntersectsExpandedBox(
                        start,
                        end,
                        center,
                        expandedHalfSize))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SegmentIntersectsExpandedBox(
            Vector2 start,
            Vector2 end,
            Vector2 center,
            Vector2 expandedHalfSize)
        {
            Vector2 direction = end - start;

            float tMin = 0f;
            float tMax = 1f;

            if (!UpdateSlabIntersection(
                    start.x,
                    direction.x,
                    center.x - expandedHalfSize.x,
                    center.x + expandedHalfSize.x,
                    ref tMin,
                    ref tMax))
            {
                return false;
            }

            if (!UpdateSlabIntersection(
                    start.y,
                    direction.y,
                    center.y - expandedHalfSize.y,
                    center.y + expandedHalfSize.y,
                    ref tMin,
                    ref tMax))
            {
                return false;
            }

            return tMax >= 0f && tMin <= 1f;
        }

        private static bool UpdateSlabIntersection(
            float start,
            float direction,
            float minimum,
            float maximum,
            ref float tMin,
            ref float tMax)
        {
            if (Mathf.Abs(direction) < 0.0001f)
            {
                return start >= minimum && start <= maximum;
            }

            float inverseDirection = 1f / direction;
            float t1 = (minimum - start) * inverseDirection;
            float t2 = (maximum - start) * inverseDirection;

            tMin = Mathf.Max(tMin, Mathf.Min(t1, t2));
            tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));

            return tMin <= tMax;
        }

        private void InitializePatrolBuffer()
        {
            activePatrolPoints = BuildPatrolPoints();

            int bufferCount = Mathf.Max(1, activePatrolPoints.Length);
            Vector2[] patrolPoints = new Vector2[bufferCount];

            for (int i = 0; i < activePatrolPoints.Length; i++)
            {
                patrolPoints[i] = activePatrolPoints[i];
            }

            patrolPointBuffer = new ComputeBuffer(
                bufferCount,
                sizeof(float) * 2,
                ComputeBufferType.Structured
            );

            patrolPointBuffer.SetData(patrolPoints);
        }

        private Vector2[] BuildPatrolPoints()
        {
            if (activeNavigationPoints.Length > 0)
            {
                return BuildAutomaticPatrolRoute();
            }

            return System.Array.Empty<Vector2>();
        }

        private Vector2[] BuildAutomaticPatrolRoute()
        {
            int maxRoutePointCount = Mathf.Clamp(
                generatedPatrolPointCount,
                2,
                Mathf.Min(MaxPatrolPointsOnGpu, activeNavigationPoints.Length)
            );
            int forwardRoutePointCount = Mathf.Max(2, (maxRoutePointCount + 1) / 2);

            if (activeNavigationPoints.Length <= 1)
            {
                return activeNavigationPoints;
            }

            List<Vector2> route = new(forwardRoutePointCount);
            HashSet<int> recentlyUsedIndices = new();

            int currentIndex = Random.Range(0, activeNavigationPoints.Length);
            Vector2 currentPoint = activeNavigationPoints[currentIndex];

            route.Add(currentPoint);
            recentlyUsedIndices.Add(currentIndex);

            for (int i = 1; i < forwardRoutePointCount; i++)
            {
                int nextIndex = FindNextReachablePatrolPointIndex(
                    currentPoint,
                    recentlyUsedIndices
                );

                if (nextIndex < 0)
                {
                    recentlyUsedIndices.Clear();

                    nextIndex = FindNextReachablePatrolPointIndex(
                        currentPoint,
                        recentlyUsedIndices
                    );
                }

                if (nextIndex < 0)
                {
                    break;
                }

                currentIndex = nextIndex;
                currentPoint = activeNavigationPoints[currentIndex];

                route.Add(currentPoint);
                recentlyUsedIndices.Add(currentIndex);
            }

            if (route.Count < 2)
            {
                return route.ToArray();
            }

            return BuildReversedPatrolRoute(route, maxRoutePointCount);
        }

        private static Vector2[] BuildReversedPatrolRoute(
            List<Vector2> forwardRoute,
            int maxRoutePointCount)
        {
            if (forwardRoute.Count <= 2)
            {
                return forwardRoute.ToArray();
            }

            List<Vector2> closedRoute = new(maxRoutePointCount);
            closedRoute.AddRange(forwardRoute);

            for (int i = forwardRoute.Count - 2; i > 0; i--)
            {
                if (closedRoute.Count >= maxRoutePointCount)
                {
                    break;
                }

                closedRoute.Add(forwardRoute[i]);
            }

            return closedRoute.ToArray();
        }

        private int FindNextReachablePatrolPointIndex(
            Vector2 currentPoint,
            HashSet<int> recentlyUsedIndices)
        {
            // Avoid picking points so close together that enemies barely move.
            float minimumDistance = Mathf.Max(
                stoppingDistance * 2f,
                navigationPointSpacing * 0.75f
            );

            // Start with the inspector radius, then expand it if no candidate can be found.
            float searchRadius = Mathf.Max(
                patrolStepSearchRadius,
                navigationPointSpacing * 2f
            );

            for (int pass = 0; pass < 4; pass++)
            {
                List<int> candidates = new();

                for (int i = 0; i < activeNavigationPoints.Length; i++)
                {
                    if (recentlyUsedIndices.Contains(i))
                    {
                        continue;
                    }

                    Vector2 candidate = activeNavigationPoints[i];
                    float distanceToCandidate = Vector2.Distance(currentPoint, candidate);

                    if (distanceToCandidate < minimumDistance ||
                        distanceToCandidate > searchRadius)
                    {
                        continue;
                    }

                    if (IsSegmentBlockedByObstacleColliders(
                            currentPoint,
                            candidate,
                            navigationPointObstaclePadding))
                    {
                        continue;
                    }

                    candidates.Add(i);
                }

                if (candidates.Count > 0)
                {
                    // Random choice keeps the route from always looking exactly the same.
                    return candidates[Random.Range(0, candidates.Count)];
                }

                // Try a wider search area before giving up.
                searchRadius *= 1.75f;
            }

            return -1;
        }

        private void InitializeNavigationBuffer()
        {
            activeNavigationPoints = BuildNavigationPoints();

            int bufferCount = Mathf.Max(1, activeNavigationPoints.Length);
            Vector2[] navigationPoints = new Vector2[bufferCount];

            for (int i = 0; i < activeNavigationPoints.Length; i++)
            {
                navigationPoints[i] = activeNavigationPoints[i];
            }

            navigationPointBuffer = new ComputeBuffer(
                bufferCount,
                sizeof(float) * 2,
                ComputeBufferType.Structured
            );

            navigationPointBuffer.SetData(navigationPoints);
        }

        private Vector2[] BuildNavigationPoints()
        {
            return BuildAutomaticNavigationPoints();
        }

        private Vector2[] BuildAutomaticNavigationPoints()
        {
            float spacing = Mathf.Max(0.5f, navigationPointSpacing);
            int maxPointCount = Mathf.Clamp(maxGeneratedNavigationPoints, 16, MaxNavigationPointsOnGpu);
            List<Vector2> points = null;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                points = GenerateNavigationGrid(spacing, maxPointCount + 1);

                if (points.Count <= maxPointCount)
                {
                    break;
                }

                spacing *= 1.25f;
            }

            points ??= new List<Vector2>();

            if (points.Count > maxPointCount)
            {
                points.RemoveRange(maxPointCount, points.Count - maxPointCount);
            }

            return points.ToArray();
        }


        private List<Vector2> GenerateNavigationGrid(float spacing, int stopAfterPointCount)
        {
            List<Vector2> points = new();

            AddTransformsAsNavigationPoints(points, startingPositions, stopAfterPointCount);

            float minX = Mathf.Min(worldMinimum.x, worldMaximum.x);
            float maxX = Mathf.Max(worldMinimum.x, worldMaximum.x);
            float minZ = Mathf.Min(worldMinimum.y, worldMaximum.y);
            float maxZ = Mathf.Max(worldMinimum.y, worldMaximum.y);

            float startX = minX + spacing * 0.5f;
            float startZ = minZ + spacing * 0.5f;

            for (float x = startX; x <= maxX; x += spacing)
            {
                for (float z = startZ; z <= maxZ; z += spacing)
                {
                    if (points.Count > stopAfterPointCount)
                    {
                        return points;
                    }

                    Vector2 point = new(x, z);

                    if (IsBlockedByObstacleColliders(point, navigationPointObstaclePadding))
                    {
                        continue;
                    }

                    if (ContainsNearbyPoint(points, point, spacing * 0.45f))
                    {
                        continue;
                    }

                    points.Add(point);
                }
            }

            return points;
        }


        private void AddTransformsAsNavigationPoints(
            List<Vector2> points,
            Transform[] sourcePoints,
            int stopAfterPointCount)
        {
            if (sourcePoints == null)
            {
                return;
            }

            foreach (Transform sourcePoint in sourcePoints)
            {
                if (points.Count > stopAfterPointCount)
                {
                    return;
                }

                if (sourcePoint == null)
                {
                    continue;
                }

                Vector3 position = sourcePoint.position;
                Vector2 point = new(position.x, position.z);

                if (IsBlockedByObstacleColliders(point, navigationPointObstaclePadding))
                {
                    continue;
                }

                if (ContainsNearbyPoint(points, point, Mathf.Max(0.25f, navigationPointSpacing * 0.25f)))
                {
                    continue;
                }

                points.Add(point);
            }
        }

        private static bool ContainsNearbyPoint(List<Vector2> points, Vector2 point, float minimumDistance)
        {
            float minimumDistanceSquared = minimumDistance * minimumDistance;

            foreach (Vector2 existingPoint in points)
            {
                if ((existingPoint - point).sqrMagnitude <= minimumDistanceSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private void InitializeObstacleBuffer()
        {
            int count = obstacleColliders != null ? obstacleColliders.Length : 0;
            int validCount = 0;

            if (obstacleColliders != null)
            {
                foreach (BoxCollider obstacleCollider in obstacleColliders)
                {
                    if (obstacleCollider != null)
                    {
                        validCount++;
                    }
                }
            }

            int bufferCount = Mathf.Max(1, validCount);
            GPUObstacle[] obstacles = new GPUObstacle[bufferCount];
            int writeIndex = 0;

            for (int i = 0; i < count; i++)
            {
                BoxCollider obstacleCollider = obstacleColliders[i];

                if (obstacleCollider == null)
                {
                    continue;
                }

                Bounds bounds = obstacleCollider.bounds;

                obstacles[writeIndex] = new GPUObstacle
                {
                    center = new Vector2(bounds.center.x, bounds.center.z),
                    halfSize = new Vector2(bounds.extents.x, bounds.extents.z),
                };

                writeIndex++;
            }

            obstacleBuffer = new ComputeBuffer(
                bufferCount,
                Marshal.SizeOf<GPUObstacle>(),
                ComputeBufferType.Structured
            );

            obstacleBuffer.SetData(obstacles);
        }


        private void InitializeAttackBuffer()
        {
            attackResultBuffer = new ComputeBuffer(
                1,
                sizeof(uint),
                ComputeBufferType.Structured
            );

            attackResultBuffer.SetData(new uint[] { 0 });
        }

        private void InitializeComputeShader()
        {
            int patrolCount = GetPatrolPointCount();
            int navigationPointCount = GetNavigationPointCount();
            int obstacleCount = CountValidObstacleColliders();

            // Counts tell the shader how many valid entries exist inside each buffer.
            enemyComputeShader.SetInt(EnemyCountId, enemyCount);
            enemyComputeShader.SetInt(PatrolPointCountId, patrolCount);
            enemyComputeShader.SetInt(NavigationPointCountId, navigationPointCount);
            enemyComputeShader.SetInt(ObstacleCountId, obstacleCount);

            // Behaviour constants copied from the inspector.
            enemyComputeShader.SetFloat(SearchDurationId, searchDuration);
            enemyComputeShader.SetFloat(StoppingDistanceId, stoppingDistance);
            enemyComputeShader.SetFloat(SteeringSpeedId, steeringSpeed);
            enemyComputeShader.SetFloat(EnemyRadiusId, enemyRadius);
            enemyComputeShader.SetFloat(ProbeDistanceId, probeDistance);

            // Buffers must be set on every kernel that reads/writes them.
            enemyComputeShader.SetBuffer(simulateKernel, EnemiesId, enemyBuffer);
            enemyComputeShader.SetBuffer(simulateKernel, PatrolPointsId, patrolPointBuffer);
            enemyComputeShader.SetBuffer(simulateKernel, NavigationPointsId, navigationPointBuffer);
            enemyComputeShader.SetBuffer(simulateKernel, ObstaclesId, obstacleBuffer);
            enemyComputeShader.SetBuffer(simulateKernel, AttackResultId, attackResultBuffer);
            enemyComputeShader.SetBuffer(resetAttackKernel, AttackResultId, attackResultBuffer);
        }

        private int GetNavigationPointCount()
        {
            return activeNavigationPoints != null ? activeNavigationPoints.Length : 0;
        }

        private int GetPatrolPointCount()
        {
            return activePatrolPoints != null ? activePatrolPoints.Length : 0;
        }

        private int CountValidObstacleColliders()
        {
            if (obstacleColliders == null)
            {
                return 0;
            }

            int count = 0;

            foreach (BoxCollider obstacleCollider in obstacleColliders)
            {
                if (obstacleCollider != null)
                {
                    count++;
                }
            }

            return count;
        }

        private void InitializeRendering()
        {
            enemyMaterial.enableInstancing = true;
            enemyMaterial.SetBuffer(EnemiesId, enemyBuffer);
            enemyMaterial.SetFloat(EnemyHeightId, enemyHeight);
            enemyMaterial.SetFloat(EnemyScaleId, enemyScale);
            drawBounds = CalculateDrawBounds();
        }

        private Bounds CalculateDrawBounds()
        {
            Bounds bounds = new(
                new Vector3(
                    (worldMinimum.x + worldMaximum.x) * 0.5f,
                    enemyHeight,
                    (worldMinimum.y + worldMaximum.y) * 0.5f
                ),
                new Vector3(
                    Mathf.Abs(worldMaximum.x - worldMinimum.x),
                    20f,
                    Mathf.Abs(worldMaximum.y - worldMinimum.y)
                )
            );

            if (player != null)
            {
                bounds.Encapsulate(player.position);
            }

            EncapsulateTransforms(ref bounds, startingPositions);

            if (obstacleColliders != null)
            {
                foreach (BoxCollider obstacleCollider in obstacleColliders)
                {
                    if (obstacleCollider == null)
                    {
                        continue;
                    }

                    bounds.Encapsulate(obstacleCollider.bounds);
                }
            }

            bounds.Expand(new Vector3(20f, 20f, 20f));

            if (bounds.size.y < 20f)
            {
                bounds.size = new Vector3(bounds.size.x, 20f, bounds.size.z);
            }

            return bounds;
        }

        private static void EncapsulateTransforms(ref Bounds bounds, Transform[] transforms)
        {
            if (transforms == null)
            {
                return;
            }

            foreach (Transform targetTransform in transforms)
            {
                if (targetTransform != null)
                {
                    bounds.Encapsulate(targetTransform.position);
                }
            }
        }

        private void UpdateComputeParameters()
        {
            Vector3 playerWorldPosition = player.position;

            Vector2 flatPlayerPosition = new(playerWorldPosition.x, playerWorldPosition.z);

            enemyComputeShader.SetVector(
                PlayerPositionId,
                flatPlayerPosition
            );

            if (enemyMaterial != null)
            {
                enemyMaterial.SetVector(
                    PlayerPositionId,
                    new Vector4(flatPlayerPosition.x, flatPlayerPosition.y, 0f, 0f)
                );
            }

            enemyComputeShader.SetFloat(DeltaTimeId, Mathf.Min(Time.deltaTime, 0.05f));
            enemyComputeShader.SetInt(ScanTriggeredId, scanWasTriggered ? 1 : 0);

            if (scanWasTriggered)
            {
                enemyComputeShader.SetVector(ScanPositionId, latestScanPosition);
            }
        }

        private void ResetAttackResult()
        {
            enemyComputeShader.Dispatch(
                resetAttackKernel,
                1,
                1,
                1
            );
        }

        private void DispatchSimulation()
        {
            const int threadCount = 64;
            int threadGroups = Mathf.CeilToInt(enemyCount / (float)threadCount);

            enemyComputeShader.Dispatch(
                simulateKernel,
                threadGroups,
                1,
                1
            );
        }

        private void DrawEnemies()
        {
            Graphics.DrawMeshInstancedProcedural(
                enemyMesh,
                0,
                enemyMaterial,
                drawBounds,
                enemyCount
            );
        }

        private void HandleScan(Vector3 worldPosition)
        {
            latestScanPosition = new Vector2(worldPosition.x, worldPosition.z);
            scanWasTriggered = true;
        }

        private void RequestAttackReadback()
        {
            if (readbackPending)
            {
                return;
            }

            readbackPending = true;
            AsyncGPUReadback.Request(attackResultBuffer, HandleAttackReadback);
        }

        private void HandleAttackReadback(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;

            if (request.hasError || gameManager == null || gameManager.GameHasEnded)
            {
                return;
            }

            uint attackResult = request.GetData<uint>()[0];

            if (attackResult != 0)
            {
                gameManager.PlayerCaught();
            }
        }

        private void OnDestroy()
        {
            enemyBuffer?.Release();
            patrolPointBuffer?.Release();
            navigationPointBuffer?.Release();
            obstacleBuffer?.Release();
            attackResultBuffer?.Release();

            enemyBuffer = null;
            patrolPointBuffer = null;
            navigationPointBuffer = null;
            obstacleBuffer = null;
            attackResultBuffer = null;
        }

        private static Vector2 Abs(Vector2 value)
        {
            return new Vector2(Mathf.Abs(value.x), Mathf.Abs(value.y));
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector2 size = worldMaximum - worldMinimum;
            Vector2 center = (worldMinimum + worldMaximum) * 0.5f;

            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                new Vector3(center.x, enemyHeight, center.y),
                new Vector3(size.x, 0.2f, size.y)
            );

            if (startingPositions != null)
            {
                Gizmos.color = Color.red;

                foreach (Transform startPosition in startingPositions)
                {
                    if (startPosition == null)
                    {
                        continue;
                    }

                    Vector3 position = startPosition.position;
                    Gizmos.DrawWireSphere(
                        new Vector3(position.x, enemyHeight, position.z),
                        enemyRadius
                    );
                }
            }

            if (drawGeneratedPatrolRoute &&
                activePatrolPoints != null &&
                activePatrolPoints.Length > 0)
            {
                Gizmos.color = new Color(1f, 0.55f, 0f);

                for (int i = 0; i < activePatrolPoints.Length; i++)
                {
                    Vector2 currentPoint = activePatrolPoints[i];
                    Vector3 currentPosition = new(
                        currentPoint.x,
                        enemyHeight,
                        currentPoint.y
                    );

                    Gizmos.DrawSphere(currentPosition, 0.18f);

                    if (activePatrolPoints.Length <= 1)
                    {
                        continue;
                    }

                    Vector2 nextPoint = activePatrolPoints[(i + 1) % activePatrolPoints.Length];
                    Vector3 nextPosition = new(
                        nextPoint.x,
                        enemyHeight,
                        nextPoint.y
                    );

                    Gizmos.DrawLine(currentPosition, nextPosition);
                }
            }

            if (drawGeneratedNavigationPoints)
            {
                Vector2[] debugNavigationPoints =
                    activeNavigationPoints != null && activeNavigationPoints.Length > 0
                        ? activeNavigationPoints
                        : BuildAutomaticNavigationPoints();

                Gizmos.color = Color.green;

                foreach (Vector2 navigationPoint in debugNavigationPoints)
                {
                    Gizmos.DrawWireCube(
                        new Vector3(navigationPoint.x, enemyHeight, navigationPoint.y),
                        new Vector3(0.35f, 0.05f, 0.35f)
                    );
                }
            }

            if (obstacleColliders == null)
            {
                return;
            }

            Gizmos.color = Color.magenta;

            foreach (BoxCollider obstacleCollider in obstacleColliders)
            {
                if (obstacleCollider == null)
                {
                    continue;
                }

                Bounds bounds = obstacleCollider.bounds;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }
        }
#endif
    }
}
