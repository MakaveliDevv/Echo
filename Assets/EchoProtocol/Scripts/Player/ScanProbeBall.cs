namespace Assets.EchoProtocol.Scripts.Player
{
    using System;
    using UnityEngine;

    public class ScanProbeBall : MonoBehaviour
    {
        [Header("Visual")]
        [SerializeField] private float spinSpeed = 180f;

        [SerializeField] private float destroyDelay = 0.15f;

        [Header("Collision")]
        [SerializeField] private SphereCollider probeCollider;

        [SerializeField] private float surfaceStopOffset = 0.02f;

        private Vector3 direction;
        private Vector3 startPosition;

        private float maxDistance;
        private float speed;
        private float travelledDistance;

        private LayerMask collisionMask;

        private Transform ignoredCollisionRoot;

        private Action<Vector3> onScanReady;

        private bool launched;
        private bool finished;

        private readonly RaycastHit[] collisionHits = new RaycastHit[16];

        private void Awake()
        {
            if (probeCollider == null)
                probeCollider = GetComponent<SphereCollider>();
        }

        public void Launch(
            Vector3 launchDirection,
            float travelDistance,
            float travelSpeed,
            LayerMask probeCollisionMask,
            Transform collisionRootToIgnore,
            Action<Vector3> scanCallback)
        {
            direction = launchDirection.sqrMagnitude > 0.001f
                ? launchDirection.normalized
                : transform.forward;

            startPosition = transform.position;

            maxDistance = Mathf.Max(0.01f, travelDistance);
            speed = Mathf.Max(0.01f, travelSpeed);
            travelledDistance = 0f;

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
                FinishProbe(transform.position);
                return;
            }

            float moveDistance = Mathf.Min(speed * Time.deltaTime, remainingDistance);

            if (TryFindCollision(moveDistance, out RaycastHit hit))
            {
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
                transform.position = startPosition + direction * maxDistance;
                FinishProbe(transform.position);
            }
        }

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

            Vector3 scale = probeCollider.transform.lossyScale;
            float largestAxis = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)
            );

            return Mathf.Max(0.01f, probeCollider.radius * largestAxis);
        }

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
