using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SandFlowPuzzle
{
    /// <summary>
    /// A draggable DogJam-style shape that collects one sand color.
    /// Each occupied shape cell owns a separate collider so the manager can
    /// calculate the exact part of the shape touching the sand picture.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SandCollectorBlock : MonoBehaviour
    {
        private readonly List<Collider> cellColliders = new List<Collider>(8);

        private SandCollectorManager owner;
        private Rigidbody body;
        private TextMeshPro counterText;
        private Vector3 dragOffset;
        private Vector3 dragStartPosition;
        private Vector3 targetPosition;
        private float restingY;

        // Per-color quotas: colorId -> how many grains remain to collect for that color.
        private readonly Dictionary<int, int> remainingByColor = new Dictionary<int, int>();
        private int maxQuotaTotal;
        private int collectedTotal;
        private bool isMultiColor;

        public int ColorId { get; private set; }
        public int Remaining { get; private set; }
        public int MaxQuota { get; private set; }
        public bool IsDragging { get; private set; }
        public bool IsCompleting { get; private set; }
        public IReadOnlyList<Collider> CellColliders => cellColliders;
        public bool IsMultiColor => isMultiColor;

        // Returns remaining grains for a specific color, or 0 if not tracked.
        public int RemainingForColor(int colorId) => remainingByColor.TryGetValue(colorId, out int r) ? r : 0;

        public void Initialize(
            SandCollectorManager blockOwner,
            int colorId,
            int quota,
            IList<Collider> colliders,
            TextMeshPro quotaText)
        {
            Initialize(blockOwner, new Dictionary<int, int> { { colorId, quota } }, colliders, quotaText);
        }

        public void Initialize(
            SandCollectorManager blockOwner,
            Dictionary<int, int> colorQuotas,
            IList<Collider> colliders,
            TextMeshPro quotaText)
        {
            owner = blockOwner;

            remainingByColor.Clear();
            maxQuotaTotal = 0;
            collectedTotal = 0;
            isMultiColor = colorQuotas != null && colorQuotas.Count > 1;

            if (colorQuotas != null)
            {
                foreach (var kv in colorQuotas)
                {
                    if (kv.Value <= 0) continue;
                    remainingByColor[kv.Key] = kv.Value;
                    maxQuotaTotal += kv.Value;
                }
            }

            // Determine the block's primary color as the first (lowest) tracked color.
            ColorId = 0;
            foreach (var kv in remainingByColor) { ColorId = kv.Key; break; }

            MaxQuota = maxQuotaTotal;
            Remaining = maxQuotaTotal;
            counterText = quotaText;

            cellColliders.Clear();
            if (colliders != null)
            {
                for (int i = 0; i < colliders.Count; i++)
                {
                    if (colliders[i] != null)
                        cellColliders.Add(colliders[i]);
                }
            }

            body = GetComponent<Rigidbody>();
            body.useGravity = false;
            body.linearDamping = 10f;
            body.angularDamping = 10f;
            body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
            body.collisionDetectionMode = CollisionDetectionMode.Continuous;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.isKinematic = true;

            UpdateCounter();
        }

        public void BeginDrag(Vector3 pointerOnPlane)
        {
            if (IsCompleting || IsDragging) return;

            dragStartPosition = transform.position;
            restingY = transform.position.y;
            dragOffset = transform.position - pointerOnPlane;
            IsDragging = true;

            Vector3 liftedPosition = transform.position;
            liftedPosition.y = restingY + owner.DragHeight;
            transform.position = liftedPosition;
            targetPosition = liftedPosition;

            body.isKinematic = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        public void SetDragTarget(Vector3 pointerOnPlane)
        {
            if (!IsDragging || IsCompleting) return;

            Vector3 requestedPosition = pointerOnPlane + dragOffset;
            requestedPosition.y = restingY + owner.DragHeight;
            targetPosition = owner.ClampToPlayArea(this, requestedPosition);
        }

        private void FixedUpdate()
        {
            if (!IsDragging || IsCompleting || body == null) return;

            Vector3 direction = targetPosition - body.position;
            direction.y = 0f;
            float distance = direction.magnitude;
            if (distance <= 0.01f) return;

            Vector3 force = direction.normalized * owner.DragForce;
            force *= Mathf.Clamp01(distance * 2f);
            body.AddForce(force, ForceMode.Force);

            if (body.linearVelocity.magnitude > owner.MaxDragSpeed)
                body.linearVelocity = body.linearVelocity.normalized * owner.MaxDragSpeed;
        }

        public void EndDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Vector3 snappedPosition = owner.SnapToPlacementGrid(this, transform.position);
            snappedPosition.y = restingY;
            snappedPosition = owner.ClampToPlayArea(this, snappedPosition);

            transform.position = owner.IsPlacementValid(this, snappedPosition)
                ? snappedPosition
                : dragStartPosition;
            Physics.SyncTransforms();

            if (HypercasualGameEngine.SoundManager.Instance != null)
                HypercasualGameEngine.SoundManager.Instance.PlayBlockPlaced();
        }

        public void CancelDrag()
        {
            if (!IsDragging) return;
            IsDragging = false;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            Vector3 position = transform.position;
            position.y = restingY;
            transform.position = position;
        }

        public void Absorb(int amount, int colorId)
        {
            if (amount <= 0 || IsCompleting) return;

            // Subtract from the tracked color quota (fallback to primary color).
            int trackedColor = colorId > 0 && remainingByColor.ContainsKey(colorId) ? colorId : ColorId;
            int remaining = remainingByColor.TryGetValue(trackedColor, out int r) ? r : 0;
            int deducted = Mathf.Min(amount, remaining);
            remainingByColor[trackedColor] = Mathf.Max(0, remaining - deducted);
            collectedTotal += deducted;

            Remaining = 0;
            foreach (var kv in remainingByColor) Remaining += kv.Value;

            UpdateCounter();

            if (Remaining == 0)
                StartCoroutine(CompleteRoutine());
        }

        public void Absorb(int amount)
        {
            if (amount <= 0 || IsCompleting) return;
            Remaining = Mathf.Max(0, Remaining - amount);
            collectedTotal = Mathf.Min(maxQuotaTotal, collectedTotal + amount);
            UpdateCounter();

            if (Remaining == 0)
                StartCoroutine(CompleteRoutine());
        }

        public bool TryGetBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            for (int i = 0; i < cellColliders.Count; i++)
            {
                Collider cell = cellColliders[i];
                if (cell == null || !cell.enabled || !cell.gameObject.activeInHierarchy) continue;

                if (!found)
                {
                    bounds = cell.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(cell.bounds);
                }
            }
            return found;
        }

        private void UpdateCounter()
        {
            if (counterText == null) return;

            if (isMultiColor)
            {
                // Multi-color: show per-color "collected/remaining" lines on separate TMP lines.
                var lines = new System.Collections.Generic.List<string>();
                foreach (var kv in remainingByColor)
                {
                    int totalForColor = kv.Value + (collectedTotal > 0 ? 0 : 0);
                    // Recompute collected per color is complex; show remaining globally.
                    lines.Add($"C{kv.Key}:{kv.Value}");
                }
                int collected = Mathf.Max(0, maxQuotaTotal - Remaining);
                int fillPercent = maxQuotaTotal > 0
                    ? Mathf.Clamp(Mathf.RoundToInt(collected * 100f / maxQuotaTotal), 0, 100)
                    : 100;
                counterText.text = $"{string.Join(" ", lines)}\n{fillPercent}%";
            }
            else
            {
                int collected = Mathf.Max(0, MaxQuota - Remaining);
                int fillPercent = MaxQuota > 0
                    ? Mathf.Clamp(Mathf.RoundToInt(collected * 100f / MaxQuota), 0, 100)
                    : 100;
                counterText.text = $"{fillPercent}%";
            }
        }

        private IEnumerator CompleteRoutine()
        {
            IsCompleting = true;
            CancelDrag();
            owner.NotifyBlockCompleting(this);

            for (int i = 0; i < cellColliders.Count; i++)
            {
                if (cellColliders[i] != null)
                    cellColliders[i].enabled = false;
            }

            if (HypercasualGameEngine.SoundManager.Instance != null)
                HypercasualGameEngine.SoundManager.Instance.PlaySandFlowBucketComplete();

            yield return new WaitForSeconds(0.1f);

            Vector3 initialScale = transform.localScale;
            Vector3 initialPosition = transform.position;
            const float duration = 0.35f;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t;
                transform.localScale = Vector3.Lerp(initialScale, Vector3.zero, eased);
                transform.position = initialPosition + Vector3.up * (0.25f * t);
                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
