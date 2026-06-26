using System.Runtime.InteropServices;
using System.Collections.Generic;
using Assets.EchoProtocol.Scripts.Core;
using Assets.EchoProtocol.Scripts.Player;
using UnityEngine;
using UnityEngine.Rendering;

namespace Assets.EchoProtocol.Scripts.Enemies
{
    /// <summary>
    /// Data for one enemy as stored on the CPU and sent to the GPU.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUEnemy
    {
        public Vector2 position;
        public Vector2 velocity;
        public Vector2 targetPosition; // Current goal position: patrol point, scan point, player position, or return point.
        public float maxSpeed;
        public float detectionDistance;
        public float attackDistance;

        // State machine values used by the compute shader.
        public int state;
        public float stateTimer;
        public int patrolIndex;

        // 1 = enemy is active, 0 = enemy is ignored by simulation logic.
        public int active;

        // Visual blend values read by GPUEnemyInstancedHologram.shader.
        public float investigateVisual;
        public float chaseVisual;
    }

    /// <summary>
    /// Simplified obstacle data sent to the compute shader.
    /// The shader receives every obstacle as a top-down rectangle on the X/Z plane.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUObstacle
    {
        public Vector2 center;
        public Vector2 halfSize;
    }

    /// <summary>
    /// Enemy behaviour states. The integer values must match the state checks in EnemySimulation.compute.
    /// </summary>
    public enum GPUEnemyState
    {
        // Does not move until the shader receives a scan event.
        Dormant = 0,

        // Follows the generated patrol route.
        Patrol = 1,

        // Moves toward the last scan position.
        Investigate = 2,

        // Follows the player when the player is close enough.
        Chase = 3,

        // Waits/searches around the last known player or scan position.
        Search = 4,

        // Goes back to the patrol route after losing the player.
        ReturnToPatrol = 5
    }

    /// <summary>
    /// CPU-side manager for the GPU enemy system.
    /// This script prepares data for the compute shader, dispatches the simulation each frame,
    /// draws the enemies with GPU instancing, and receives the "player was caught" result back from the GPU.
    /// </summary>
    public class EnemyComputeController : MonoBehaviour
    {
        // Safety limits. ComputeBuffers can handle more, but keeping the project small avoids GPU overload.
        private const int MaxNavigationPointsOnGpu = 512;
        private const int MaxPatrolPointsOnGpu = 512;

        [Header("References")]
        [SerializeField] private Transform player;
        [SerializeField] private ScannerController scanner;
        [SerializeField] private GameManager gameManager;

        [Header("Compute")]
        // Compute shader that owns the actual enemy movement/state-machine logic.
        [SerializeField] private ComputeShader enemyComputeShader;

        [Header("Rendering")]
        [SerializeField] private Mesh enemyMesh;
        [SerializeField] private Material enemyMaterial;

        // Rendering height/scale are visual only; the simulation itself is top-down X/Z movement.
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
        [SerializeField] private float searchDuration = 4f; // Search duration is used after enemies lose the player or finish investigating.
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
        [SerializeField] private BoxCollider[] obstacleColliders; // These are simplified into GPUObstacle rectangles and sent to the compute shader.
        [SerializeField] private float enemyRadius = 0.4f;
        [SerializeField] private float probeDistance = 4f;

        [Header("World Bounds")]
        [SerializeField] private Vector2 worldMinimum = new(-30f, -30f);
        [SerializeField] private Vector2 worldMaximum = new(30f, 30f);

        [Header("GPU Readback")]
        // How often the CPU asks the GPU if an enemy has caught the player.
        // This is not every frame because GPU readback can be expensive.
        [SerializeField, Min(0.02f)] private float attackCheckInterval = 0.1f;

        // ComputeBuffers are arrays stored on the GPU.
        // enemyBuffer is both simulated by the compute shader and read by the instanced material.
        private ComputeBuffer enemyBuffer;
        private ComputeBuffer patrolPointBuffer;
        private ComputeBuffer navigationPointBuffer;
        private ComputeBuffer obstacleBuffer;
        private ComputeBuffer attackResultBuffer; // One-value buffer used like a flag: 0 = safe, 1 = player caught.

        // Kernel IDs are the compute shader functions we dispatch.
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

        // PropertyToID values must match names in EnemySimulation.compute and GPUEnemyInstancedHologram.shader.
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

        /// <summary>
        /// Creates the enemy buffer and fills it with starting data.
        /// After SetData(), the compute shader owns the movement changes each frame.
        /// </summary>
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

                // The patrol index decides where this enemy enters the generated patrol route.
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

        /// <summary>
        /// Finds the patrol point that best matches a spawn position.
        /// Visible/reachable points are preferred so enemies do not start with a target behind a wall.
        /// </summary>
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

                // Keep the absolute nearest point as a fallback, even if the direct line is blocked.
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

                // Prefer the nearest point with a clear line of sight.
                if (sqrDistance < bestVisibleDistance)
                {
                    bestVisibleDistance = sqrDistance;
                    bestVisibleIndex = i;
                }
            }

            return bestVisibleIndex >= 0 ? bestVisibleIndex : bestIndex;
        }

        /// <summary>
        /// Picks a spawn position for one enemy.
        /// </summary>
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
                // If there are more enemies than points, modulo wraps back to the first point.
                return activeNavigationPoints[index % activeNavigationPoints.Length];
            }

            // Last normal fallback: try random positions that are not inside an obstacle.
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

            // Emergency fallback if every random point was blocked.
            return (worldMinimum + worldMaximum) * 0.5f;
        }

        /// <summary>
        /// CPU-side check used while generating navigation/patrol points.
        /// It tests if a top-down point is inside any expanded obstacle rectangle.
        /// </summary>
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

                // Expand by enemyRadius so the center point does not get too close to wall edges.
                Vector2 expandedHalfSize = halfSize + Vector2.one * (enemyRadius + extraPadding);

                if (difference.x <= expandedHalfSize.x &&
                    difference.y <= expandedHalfSize.y)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether a straight line from start to end crosses an obstacle.
        /// Used to avoid generating patrol links that would cut through walls.
        /// </summary>
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

        /// <summary>
        /// Tests a line segment against an axis-aligned rectangle.
        /// This is the same top-down simplification used by the GPU obstacle system.
        /// </summary>
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

        /// <summary>
        /// One axis of the segment-vs-box test.
        /// "Slab" means the min/max range of a rectangle on one axis.
        /// </summary>
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

            // A ComputeBuffer cannot have a count of 0, so allocate at least 1 item.
            // The real count sent to the shader is still activePatrolPoints.Length.
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

        /// <summary>
        /// Creates the route enemies follow while patrolling.
        /// At the moment this is fully automatic and based on the generated navigation points.
        /// </summary>
        private Vector2[] BuildPatrolPoints()
        {
            if (activeNavigationPoints.Length > 0)
            {
                return BuildAutomaticPatrolRoute();
            }

            return System.Array.Empty<Vector2>();
        }

        /// <summary>
        /// Builds a semi-random patrol route from reachable navigation points.
        /// A new route is generated when the scene starts, so each play session can feel slightly different.
        /// </summary>
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

            // Start from a random navigation point.
            int currentIndex = Random.Range(0, activeNavigationPoints.Length);
            Vector2 currentPoint = activeNavigationPoints[currentIndex];

            route.Add(currentPoint);
            recentlyUsedIndices.Add(currentIndex);

            for (int i = 1; i < forwardRoutePointCount; i++)
            {
                // Pick another navigation point that is far enough away and not blocked by walls.
                int nextIndex = FindNextReachablePatrolPointIndex(
                    currentPoint,
                    recentlyUsedIndices
                );

                if (nextIndex < 0)
                {
                    // If every nearby point was recently used, allow reuse and try again.
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
                // Not enough points to create a back-and-forth route.
                return route.ToArray();
            }

            return BuildPingPongPatrolRoute(route, maxRoutePointCount);
        }

        /// <summary>
        /// Turns a forward route into a back-and-forth route.
        /// Example: A, B, C, D becomes A, B, C, D, C, B.
        /// This prevents a long teleport-like jump from the last point directly back to the first.
        /// </summary>
        private static Vector2[] BuildPingPongPatrolRoute(
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

        /// <summary>
        /// Chooses a valid next patrol point from the generated navigation grid.
        /// A valid point must be a useful distance away and have no obstacle between it and the current point.
        /// </summary>
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

            // A ComputeBuffer cannot be empty, so allocate at least one slot.
            // The shader still receives the real point count separately.
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

        /// <summary>
        /// Entry point for navigation point creation.
        /// Keeping this wrapper makes it easy to swap in another navigation generation method later.
        /// </summary>
        private Vector2[] BuildNavigationPoints()
        {
            return BuildAutomaticNavigationPoints();
        }

        /// <summary>
        /// Generates walkable top-down points inside worldMinimum/worldMaximum.
        /// If too many points are generated, spacing is increased until the result fits the GPU limit.
        /// </summary>
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

        /// <summary>
        /// Creates a grid of navigation points while skipping points inside/too close to obstacles.
        /// The grid is simple but reliable for this project because enemies only need top-down movement.
        /// </summary>
        private List<Vector2> GenerateNavigationGrid(float spacing, int stopAfterPointCount)
        {
            List<Vector2> points = new();

            // Start positions are added first so enemies can always spawn near assigned locations.
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
                        // Do not place navigation points inside walls or too close to wall edges.
                        continue;
                    }

                    if (ContainsNearbyPoint(points, point, spacing * 0.45f))
                    {
                        // Avoid duplicates/near-duplicates, especially near manual start positions.
                        continue;
                    }

                    points.Add(point);
                }
            }

            return points;
        }

        /// <summary>
        /// Adds transform positions to the navigation point list if they are valid.
        /// This lets assigned enemy starting positions become useful navigation anchors too.
        /// </summary>
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

        /// <summary>
        /// Returns true when a new point is too close to an already added point.
        /// </summary>
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

        /// <summary>
        /// Converts Unity BoxColliders into lightweight GPUObstacle structs.
        /// The compute shader only needs top-down center/half-size values, not full collider objects.
        /// </summary>
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
                    // Convert from Unity X/Y/Z to top-down X/Z.
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

        /// <summary>
        /// Creates a one-value GPU buffer used by the shader to report if an enemy attacked the player.
        /// </summary>
        private void InitializeAttackBuffer()
        {
            attackResultBuffer = new ComputeBuffer(
                1,
                sizeof(uint),
                ComputeBufferType.Structured
            );

            attackResultBuffer.SetData(new uint[] { 0 });
        }

        /// <summary>
        /// Connects all buffers and constant settings to the compute shader.
        /// This is the bridge between inspector values in C# and the GPU simulation.
        /// </summary>
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

        /// <summary>
        /// Connects the enemy buffer to the material and prepares procedural drawing.
        /// The material/shader reads the same _Enemies buffer that the compute shader updates.
        /// </summary>
        private void InitializeRendering()
        {
            enemyMaterial.enableInstancing = true;
            enemyMaterial.SetBuffer(EnemiesId, enemyBuffer);
            enemyMaterial.SetFloat(EnemyHeightId, enemyHeight);
            enemyMaterial.SetFloat(EnemyScaleId, enemyScale);
            drawBounds = CalculateDrawBounds();
        }

        /// <summary>
        /// Calculates a large bounds area for procedural instanced rendering.
        /// Unity uses this for visibility culling. If the bounds are too small, enemies may disappear.
        /// </summary>
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
                // Include the player so enemy chase movement near the player remains inside the draw bounds.
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
                // The simulation is flat, but the render bounds still need enough height for the camera.
                bounds.size = new Vector3(bounds.size.x, 20f, bounds.size.z);
            }

            return bounds;
        }

        /// <summary>
        /// Helper for adding optional transform references to a Bounds object.
        /// </summary>
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

        /// <summary>
        /// Sends per-frame values to the compute shader and enemy material.
        /// These values change during play, so they cannot only be set during initialization.
        /// </summary>
        private void UpdateComputeParameters()
        {
            Vector3 playerWorldPosition = player.position;

            // The enemy simulation is top-down, so Unity world X/Z becomes Vector2 X/Y.
            Vector2 flatPlayerPosition = new(playerWorldPosition.x, playerWorldPosition.z);

            enemyComputeShader.SetVector(
                PlayerPositionId,
                flatPlayerPosition
            );

            if (enemyMaterial != null)
            {
                // The material uses player position for visual feedback while enemies chase the player.
                enemyMaterial.SetVector(
                    PlayerPositionId,
                    new Vector4(flatPlayerPosition.x, flatPlayerPosition.y, 0f, 0f)
                );
            }

            // Clamp delta time to avoid huge simulation jumps after editor pauses or frame spikes.
            enemyComputeShader.SetFloat(DeltaTimeId, Mathf.Min(Time.deltaTime, 0.05f));
            enemyComputeShader.SetInt(ScanTriggeredId, scanWasTriggered ? 1 : 0);

            if (scanWasTriggered)
            {
                // The shader only needs a scan position on the frame where a scan was triggered.
                enemyComputeShader.SetVector(ScanPositionId, latestScanPosition);
            }
        }

        /// <summary>
        /// Clears the GPU attack flag before the next simulation step.
        /// If the simulation catches the player, it will write 1 into this buffer again.
        /// </summary>
        private void ResetAttackResult()
        {
            enemyComputeShader.Dispatch(
                resetAttackKernel,
                1,
                1,
                1
            );
        }

        /// <summary>
        /// Runs the compute shader simulation.
        /// The shader uses 64 threads per group, so the group count must cover every enemy.
        /// </summary>
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

        /// <summary>
        /// Draws all enemies from the GPU buffer without creating normal enemy GameObjects.
        /// The instance ID inside the shader selects which GPUEnemy data belongs to each drawn mesh.
        /// </summary>
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

        /// <summary>
        /// Receives scan events from ScannerController.
        /// The stored position is sent to the compute shader during the next Update().
        /// </summary>
        private void HandleScan(Vector3 worldPosition)
        {
            latestScanPosition = new Vector2(worldPosition.x, worldPosition.z);
            scanWasTriggered = true;
        }

        /// <summary>
        /// Starts an asynchronous GPU readback for the attack result.
        /// Asynchronous readback prevents the CPU from freezing while waiting for the GPU.
        /// </summary>
        private void RequestAttackReadback()
        {
            if (readbackPending)
            {
                return;
            }

            readbackPending = true;
            AsyncGPUReadback.Request(attackResultBuffer, HandleAttackReadback);
        }

        /// <summary>
        /// Called by Unity when the GPU readback finishes.
        /// If the GPU wrote a non-zero attack result, the GameManager triggers the lose state.
        /// </summary>
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
            // ComputeBuffers are GPU resources and must be released manually.
            // Nulling them afterwards avoids accidental reuse after destruction.
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

        // Unity has Mathf.Abs for floats, but not for Vector2, so this helper applies it to both axes.
        private static Vector2 Abs(Vector2 value)
        {
            return new Vector2(Mathf.Abs(value.x), Mathf.Abs(value.y));
        }

#if UNITY_EDITOR
        /// <summary>
        /// Debug visualization shown only in the Unity editor when this object is selected.
        /// It helps check world bounds, spawn positions, generated navigation points, patrol route,
        /// and obstacle boxes without entering play mode blindly.
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Vector2 size = worldMaximum - worldMinimum;
            Vector2 center = (worldMinimum + worldMaximum) * 0.5f;

            // Cyan rectangle: the area used for automatic point generation.
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(
                new Vector3(center.x, enemyHeight, center.y),
                new Vector3(size.x, 0.2f, size.y)
            );

            if (startingPositions != null)
            {
                // Red spheres: manual enemy spawn positions.
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
                // Orange dots/lines: patrol route generated from the navigation points.
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
                // Green squares: automatically generated walkable navigation points.
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

            // Magenta boxes: obstacle colliders that are sent to the GPU.
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
