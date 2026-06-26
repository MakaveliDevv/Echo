namespace Assets.EchoProtocol.Scripts.Player
{
    using System;
    using UnityEngine;

    /// <summary>
    /// Small projectile fired by ScannerController.
    /// It moves forward until it hits a collider or reaches its maximum distance, then reports that final
    /// position back through a callback so the scan wave can start there.
    /// </summary>
    public class ScanProbeBall : MonoBehaviour
    {
        [Header("Visual")]
        // Purely visual spinning speed.
        [SerializeField] private float spinSpeed = 180f;

        // Short delay after scanning so the probe can still be seen for a moment.
        [SerializeField] private float destroyDelay = 0.15f;

        [Header("Collision")]
        // Sphere radius is used for SphereCast collision so the probe does not pass through thin walls.
        [SerializeField] private SphereCollider probeCollider;

        // Stops the probe slightly before the surface to avoid visually clipping into the wall.
        [SerializeField] private float surfaceStopOffset = 0.02f;

        // Runtime movement data assigned by Launch().
        private Vector3 direction;
        private Vector3 startPosition;

        private float maxDistance;
        private float speed;
        private float travelledDistance;

        private LayerMask collisionMask;

        // The player root is ignored so the probe does not immediately collide with the player/scanner.
        private Transform ignoredCollisionRoot;

        // Callback into ScannerController. It receives the final scan position.
        private Action<Vector3> onScanReady;

        private bool launched;
        private bool finished;

        // Reused hit array to avoid allocating garbage every frame during SphereCastNonAlloc.
        private readonly RaycastHit[] collisionHits = new RaycastHit[16];

        private void Awake()
        {
            // Auto-fill the collider if it was not manually assigned in the prefab.
            if (probeCollider == null)
                probeCollider = GetComponent<SphereCollider>();
        }

        /// <summary>
        /// Simple launch overload for cases where no object needs to be ignored.
        /// </summary>
        public void Launch(
            Vector3 launchDirection,
            float travelDistance,
            float travelSpeed,
            LayerMask probeCollisionMask,
            Action<Vector3> scanCallback)
        {
            Launch(
                launchDirection,
                travelDistance,
                travelSpeed,
                probeCollisionMask,
                null,
                scanCallback
            );
        }

        /// <summary>
        /// Starts the probe movement.
        /// ScannerController calls this immediately after instantiating the prefab.
        /// </summary>
        public void Launch(
            Vector3 launchDirection,
            float travelDistance,
            float travelSpeed,
            LayerMask probeCollisionMask,
            Transform collisionRootToIgnore,
            Action<Vector3> scanCallback)
        {
            // If the input direction is invalid, use the object's own forward direction as a fallback.
            direction = launchDirection.sqrMagnitude > 0.001f
                ? launchDirection.normalized
                : transform.forward;

            startPosition = transform.position;

            maxDistance = Mathf.Max(0.01f, travelDistance);
            speed = Mathf.Max(0.01f, travelSpeed);
            travelledDistance = 0f;

            // If the inspector mask is accidentally empty, use Unity's default raycast layers instead.
            collisionMask = probeCollisionMask.value == 0
                ? Physics.DefaultRaycastLayers
                : probeCollisionMask;

            ignoredCollisionRoot = collisionRootToIgnore;
            onScanReady = scanCallback;

            launched = true;
            finished = false;
        }

        private void Update()
        {
            if (!launched || finished)
                return;

            MoveProbe();
            RotateVisual();
        }

        private void MoveProbe()
        {
            float remainingDistance = maxDistance - travelledDistance;

            if (remainingDistance <= 0f)
            {
                // No wall was hit, so the scan starts at the maximum travel position.
                FinishProbe(transform.position);
                return;
            }

            float moveDistance = Mathf.Min(speed * Time.deltaTime, remainingDistance);

            if (TryFindCollision(moveDistance, out RaycastHit hit))
            {
                // Stop just before the surface, but scan exactly from the collision point.
                float stopDistance = Mathf.Max(0f, hit.distance - surfaceStopOffset);

                transform.position += direction * stopDistance;
                travelledDistance += stopDistance;

                FinishProbe(hit.point);
                return;
            }

            transform.position += direction * moveDistance;
            travelledDistance += moveDistance;

            if (travelledDistance >= maxDistance - 0.001f)
            {
                // Snap to the exact max-distance position to avoid tiny floating point differences.
                transform.position = startPosition + direction * maxDistance;
                FinishProbe(transform.position);
            }
        }

        /// <summary>
        /// Checks whether the probe would collide during this frame's movement.
        /// SphereCast is used instead of Raycast because the probe has a visible radius.
        /// </summary>
        private bool TryFindCollision(float castDistance, out RaycastHit closestHit)
        {
            closestHit = default;

            float radius = GetWorldCollisionRadius();
            int hitCount = Physics.SphereCastNonAlloc(
                transform.position,
                radius,
                direction,
                collisionHits,
                castDistance,
                collisionMask,
                QueryTriggerInteraction.Ignore
            );

            bool foundValidHit = false;
            float closestDistance = float.PositiveInfinity;

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = collisionHits[i];

                // Ignore the player and the probe itself, but keep normal level colliders.
                if (ShouldIgnoreHit(hit.collider))
                    continue;

                if (hit.distance >= closestDistance)
                    continue;

                closestHit = hit;
                closestDistance = hit.distance;
                foundValidHit = true;
            }

            return foundValidHit;
        }

        private float GetWorldCollisionRadius()
        {
            if (probeCollider == null)
                return 0.25f;

            // A SphereCollider radius is local-space, so multiply by the largest world scale axis.
            Vector3 scale = probeCollider.transform.lossyScale;
            float largestAxis = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)
            );

            return Mathf.Max(0.01f, probeCollider.radius * largestAxis);
        }

        /// <summary>
        /// Filters out collisions that should not stop the probe.
        /// </summary>
        private bool ShouldIgnoreHit(Collider hitCollider)
        {
            if (hitCollider == null)
                return true;

            Transform hitTransform = hitCollider.transform;

            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                return true;

            return ignoredCollisionRoot != null && hitTransform.IsChildOf(ignoredCollisionRoot);
        }

        private void RotateVisual()
        {
            transform.Rotate(
                Vector3.up,
                spinSpeed * Time.deltaTime,
                Space.World
            );
        }

        /// <summary>
        /// Finishes the probe once, notifies ScannerController, and destroys the visual object.
        /// </summary>
        private void FinishProbe(Vector3 scanPosition)
        {
            if (finished)
                return;

            finished = true;

            onScanReady?.Invoke(scanPosition);

            Destroy(gameObject, destroyDelay);
        }
    }
}
