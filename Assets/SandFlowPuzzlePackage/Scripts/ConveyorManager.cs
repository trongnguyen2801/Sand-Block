using UnityEngine;
using System.Collections.Generic;
using TMPro;

namespace SandFlowPuzzle
{
    public class ConveyorManager : MonoBehaviour
    {
        [Header("References")]
        public Transform beltBucketsContainer;

        [Header("Settings")]
        public float beltSpeed = 0.55f;
        public float wrapRightThreshold = 100f;
        public float spawnX = 0f;
        public float overlapDistance = 18f;

        [Header("Belt Dimensions")]
        public float beltLeftX = -1.45f;
        public float beltWidth = 2.9f;
        public float beltCenterZ = 0.2f;
        public float beltY = 0.12f;

        public List<BucketData> beltBuckets = new List<BucketData>();
        public int maxSlots = 5;

        // Callback for counter text updates (GameManager sets this)
        public System.Action<int, int> onCounterChanged;

        // Sand display world bounds for flying block spawning
        public Vector3 sandWorldMin;
        public Vector3 sandWorldMax;

        // Flying block system — colored spheres that fly from sand display to bucket
        private struct FlyingBlock
        {
            public Transform transform;
            public Vector3 startPos;
            public BucketData targetBucket;
            public float elapsed;
            public float duration;
            public float spreadX; // random lateral spread
            public float spreadZ; // random depth spread
        }

        private List<FlyingBlock> flyingBlocks = new List<FlyingBlock>();
        private Dictionary<int, Material> flyingBlockMats = new Dictionary<int, Material>();

        // Explosion particle system — burst on bucket completion
        private struct ExplosionParticle
        {
            public Transform transform;
            public Vector3 velocity;
            public float elapsed;
            public float lifetime;
            public bool isCylinder; // true = return to cylinder pool, false = sphere pool
        }

        private List<ExplosionParticle> explosionParticles = new List<ExplosionParticle>();

        // Object pooling — pre-spawn spheres/cylinders to avoid runtime Instantiate/Destroy
        private Transform poolParent;
        private Stack<GameObject> spherePool = new Stack<GameObject>(128);
        private Stack<GameObject> cylinderPool = new Stack<GameObject>(32);
        private const int INITIAL_SPHERES = 64;
        private const int INITIAL_CYLINDERS = 32;
        private Mesh sphereMesh;
        private Mesh cylinderMesh;

        // Reusable list to avoid per-frame allocation
        private List<Vector2Int> reusableExtractedPositions = new List<Vector2Int>();

        private SandSimulator sandSimulator;
        private Mesh bucketMesh;

        public void Initialize(SandSimulator simulator)
        {
            sandSimulator = simulator;

            // Load custom bucket mesh from Resources
            var model = Resources.Load<GameObject>("cylinder-hole-new");
            if (model != null)
            {
                var mf = model.GetComponentInChildren<MeshFilter>();
                if (mf != null) bucketMesh = mf.sharedMesh;
            }

            InitPools();
            UpdateCounterText();
        }

        // =====================================================================
        // Object Pooling — reuse spheres/cylinders to avoid GC on mobile
        // =====================================================================

        private void InitPools()
        {
            poolParent = new GameObject("_ObjectPool").transform;
            poolParent.SetParent(transform, false);

            // Grab primitive meshes from temporary objects
            var tempSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereMesh = tempSphere.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempSphere);

            var tempCyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinderMesh = tempCyl.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tempCyl);

            for (int i = 0; i < INITIAL_SPHERES; i++)
                spherePool.Push(CreatePooledSphere());
            for (int i = 0; i < INITIAL_CYLINDERS; i++)
                cylinderPool.Push(CreatePooledCylinder());
        }

        private GameObject CreatePooledSphere()
        {
            var go = new GameObject("PooledSphere");
            go.AddComponent<MeshFilter>().sharedMesh = sphereMesh;
            go.AddComponent<MeshRenderer>();
            go.SetActive(false);
            go.transform.SetParent(poolParent, false);
            return go;
        }

        private GameObject CreatePooledCylinder()
        {
            var go = new GameObject("PooledCylinder");
            go.AddComponent<MeshFilter>().sharedMesh = cylinderMesh;
            go.AddComponent<MeshRenderer>();
            go.SetActive(false);
            go.transform.SetParent(poolParent, false);
            return go;
        }

        private GameObject AcquireSphere(Transform parent)
        {
            GameObject go = spherePool.Count > 0 ? spherePool.Pop() : CreatePooledSphere();
            go.transform.SetParent(parent, false);
            go.SetActive(true);
            return go;
        }

        private GameObject AcquireCylinder(Transform parent)
        {
            GameObject go = cylinderPool.Count > 0 ? cylinderPool.Pop() : CreatePooledCylinder();
            go.transform.SetParent(parent, false);
            go.SetActive(true);
            return go;
        }

        private void ReturnSphere(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            go.transform.SetParent(poolParent, false);
            spherePool.Push(go);
        }

        private void ReturnCylinder(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            go.transform.SetParent(poolParent, false);
            cylinderPool.Push(go);
        }

        public int ActiveBucketCount()
        {
            int count = 0;
            foreach (var b in beltBuckets)
                if (b.status == BucketStatus.Belt) count++;
            return count;
        }

        public bool IsFull()
        {
            return ActiveBucketCount() >= maxSlots;
        }

        public void AddBucket(BucketData bucket)
        {
            bucket.status = BucketStatus.Belt;
            bucket.beltX = spawnX;
            bucket.extractedThisLoop = 0;
            bucket.stuckCount = 0;
            bucket.scale = 1f;
            bucket.opacity = 1f;
            bucket.beltY = 0f;
            bucket.popVy = -4f;
            bucket.rotation = 0f;

            // Landing wobble + suction delay
            bucket.wobbleTime = 0.4f;
            bucket.wobbleIntensity = 0.15f;
            bucket.suctionCooldown = 0.04f;

            // Avoid overlap with existing belt buckets
            bool overlap;
            do
            {
                overlap = false;
                foreach (var p in beltBuckets)
                {
                    if (p.status == BucketStatus.Belt && Mathf.Abs(p.beltX - bucket.beltX) < overlapDistance)
                    {
                        overlap = true;
                        bucket.beltX -= overlapDistance;
                        break;
                    }
                }
            } while (overlap);

            // Create 3D belt visual
            Create3DBeltBucket(bucket);

            beltBuckets.Add(bucket);
            UpdateCounterText();
        }

        /// <summary>
        /// Finds a non-overlapping landing local X on the belt.
        /// Starts at -1.087, checks all belt buckets and in-flight buckets;
        /// if anything is within 0.35 of the candidate, increases by 0.4 and retries.
        /// </summary>
        public float CalculateSpawnLocalX(List<BucketData> flyingBuckets = null)
        {
            float localX = -1.087f;
            const float minDistance = 0.35f;
            const float step = 0.4f;

            bool overlap;
            do
            {
                overlap = false;
                foreach (var p in beltBuckets)
                {
                    if (p.status == BucketStatus.Belt && p.transform3D != null
                        && Mathf.Abs(p.transform3D.localPosition.x - localX) < minDistance)
                    {
                        overlap = true;
                        localX += step;
                        break;
                    }
                }
                if (!overlap && flyingBuckets != null)
                {
                    foreach (var f in flyingBuckets)
                    {
                        if (Mathf.Abs(f.flyToBeltTargetLocalX - localX) < minDistance)
                        {
                            overlap = true;
                            localX += step;
                            break;
                        }
                    }
                }
            } while (overlap);
            return localX;
        }

        /// <summary>
        /// Converts a local X position on the belt to world position.
        /// </summary>
        public Vector3 GetBeltWorldPositionFromLocalX(float localX)
        {
            return beltBucketsContainer.TransformPoint(new Vector3(localX, 0.34f, beltCenterZ));
        }

        /// <summary>
        /// Adds a bucket to the belt at a pre-calculated local X position.
        /// Converts local X back to beltX for the belt movement system.
        /// </summary>
        public void AddBucketAtLocalX(BucketData bucket, float localX)
        {
            bucket.status = BucketStatus.Belt;
            // Convert localX back to beltX (0-100 range) for belt movement
            bucket.beltX = (localX - beltLeftX) / beltWidth * 100f;
            bucket.extractedThisLoop = 0;
            bucket.stuckCount = 0;
            bucket.scale = 1f;
            bucket.opacity = 1f;
            bucket.beltY = 0f;
            bucket.popVy = -4f;
            bucket.rotation = 0f;

            bucket.wobbleTime = 0.4f;
            bucket.wobbleIntensity = 0.15f;
            bucket.suctionCooldown = 0.04f;

            Create3DBeltBucket(bucket);

            beltBuckets.Add(bucket);
            UpdateCounterText();
        }

        private void Create3DBeltBucket(BucketData bucket)
        {
            // Root empty GO
            GameObject root = new GameObject($"BeltBucket_{bucket.id}");
            root.transform.SetParent(beltBucketsContainer, false);
            bucket.transform3D = root.transform;

            // Body — use custom mesh if available, otherwise fall back to primitive
            GameObject body;
            if (bucketMesh != null)
            {
                body = new GameObject("Body");
                body.AddComponent<MeshFilter>().sharedMesh = bucketMesh;
                body.AddComponent<MeshRenderer>();
            }
            else
            {
                body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                // Remove collider from primitive — belt buckets aren't clickable
                var primCol = body.GetComponent<Collider>();
                if (primCol != null) Object.Destroy(primCol);
            }
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.255f, 0.255f, 0.255f);
            body.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            body.transform.localPosition = Vector3.zero;

            bucket.bodyRenderer = body.GetComponent<MeshRenderer>();
            Color baseColor = SandSimulator.PaletteColors[bucket.trueColorId];
            Material baseMat = CreateLitMaterial(baseColor);
            Material darkerMat = CreateLitMaterial(new Color(baseColor.r * 0.7f, baseColor.g * 0.7f, baseColor.b * 0.7f, baseColor.a));
            bucket.bodyRenderer.materials = new Material[] { baseMat, baseMat, darkerMat };

            // Percentage text (3D TextMeshPro with outline)
            GameObject textGo = new GameObject("PercentText");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0f, 0.024f, -0.196f);
            textGo.transform.localRotation = Quaternion.Euler(61.78f, 0f, 0f);
            textGo.transform.localScale = new Vector3(0.02687673f, 0.02687673f, 0.02687673f);

            TextMeshPro tmp = textGo.AddComponent<TextMeshPro>();
            tmp.text = "0%";
            tmp.fontSize = 55;
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = Color.white;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.sortingOrder = 10;

            // Bold weight + black outline
            tmp.fontWeight = FontWeight.Bold;
            tmp.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, 0.2f);
            tmp.outlineWidth = 0.35f;
            tmp.outlineColor = new Color32(0, 0, 0, 255);
            tmp.fontMaterial.EnableKeyword("OUTLINE_ON");

            bucket.percentText3D = tmp;

            // Fill container — stationary blocks inside bucket that visually fill from bottom to top
            GameObject fillGo = new GameObject("FillContainer");
            fillGo.transform.SetParent(root.transform, false);
            fillGo.transform.localPosition = Vector3.zero;
            bucket.fillContainer = fillGo.transform;
        }

        public void UpdateBelt()
        {
            for (int i = beltBuckets.Count - 1; i >= 0; i--)
            {
                BucketData pot = beltBuckets[i];

                if (pot.status == BucketStatus.Done)
                {
                    // Done animation: scale up, levitate, fade out, tumble (fast)
                    float dt = Time.deltaTime;
                    pot.scale += 3.6f * dt;
                    pot.opacity -= 4.8f * dt;
                    pot.popVy += 7f * dt;
                    pot.beltY += pot.popVy * dt;
                    pot.rotation += 1440f * dt;

                    if (pot.transform3D != null)
                    {
                        pot.transform3D.localScale = Vector3.one * pot.scale;
                        Vector3 pos = pot.transform3D.localPosition;
                        pos.y = 0.34f + pot.beltY;
                        pot.transform3D.localPosition = pos;
                        pot.transform3D.localEulerAngles = new Vector3(0, pot.rotation, 0);
                    }
                    // Fade ALL renderers (body + fill blocks)
                    float alpha = Mathf.Max(0, pot.opacity);
                    if (pot.bodyRenderer != null)
                        SetMaterialAlpha(pot.bodyRenderer, alpha);
                    if (pot.fillContainer != null)
                    {
                        foreach (var mr in pot.fillContainer.GetComponentsInChildren<MeshRenderer>())
                            SetMaterialAlpha(mr, alpha);
                    }

                    // Remove when fully faded — spawn circle burst at vanish point
                    if (pot.opacity <= 0)
                    {
                        if (pot.transform3D != null)
                        {
                            SpawnCircleExplosion(pot.transform3D.position, pot.trueColorId);
                            // Return fill blocks to pool before destroying bucket
                            ReturnFillBlocksToPool(pot);
                            Destroy(pot.transform3D.gameObject);
                        }
                        beltBuckets.RemoveAt(i);
                    }
                    continue;
                }

                // Move along belt
                pot.beltX += beltSpeed * Time.deltaTime * 60f;

                // Wrap around
                if (pot.beltX >= wrapRightThreshold)
                {
                    float newX = spawnX;
                    bool overlap;
                    do
                    {
                        overlap = false;
                        foreach (var p in beltBuckets)
                        {
                            if (p.id != pot.id && p.status == BucketStatus.Belt && Mathf.Abs(p.beltX - newX) < overlapDistance)
                            {
                                overlap = true;
                                newX -= overlapDistance;
                                break;
                            }
                        }
                    } while (overlap);

                    pot.beltX = newX;

                    if (pot.extractedThisLoop == 0) pot.stuckCount++;
                    else pot.stuckCount = 0;
                    pot.extractedThisLoop = 0;
                }

                // Suction cooldown
                if (pot.suctionCooldown > 0f)
                    pot.suctionCooldown -= Time.deltaTime;

                // Suction: extract sand when bucket is over the sand display area (world X)
                float actualBucketX = pot.transform3D != null ? pot.transform3D.position.x : 0f;
                bool overSand = actualBucketX >= sandWorldMin.x && actualBucketX <= sandWorldMax.x;

                if (pot.quota > 0 && overSand && pot.suctionCooldown <= 0f)
                {
                    int limit = Mathf.Min(pot.quota, 4);
                    float normalizedX = Mathf.InverseLerp(sandWorldMin.x, sandWorldMax.x, actualBucketX);
                    int centerGridX = Mathf.RoundToInt(normalizedX * (SandSimulator.GRID_SIZE - 1));
                    centerGridX = Mathf.Clamp(centerGridX, 0, SandSimulator.GRID_SIZE - 1);

                    reusableExtractedPositions.Clear();
                    int extractedCount = sandSimulator.ExtractExposedPixels(centerGridX, (byte)pot.trueColorId, limit, reusableExtractedPositions);
                    pot.quota -= extractedCount;
                    pot.extractedThisLoop += extractedCount;

                    // Spawn flying blocks + suction pulse + fill blocks
                    if (extractedCount > 0)
                    {
                        SpawnFlyingBlocks(pot, reusableExtractedPositions);
                        SpawnFillBlocks(pot, extractedCount);
                        pot.suctionPulse = 0.15f; // brief scale-up pop
                        pot.wobbleTime = 0.2f;
                        pot.wobbleIntensity = 0.05f;
                        if (HypercasualGameEngine.SoundManager.Instance != null) HypercasualGameEngine.SoundManager.Instance.PlaySandFlowSandPour();
                    }

                    // Check completion
                    if (pot.quota <= 0)
                    {
                        if (HypercasualGameEngine.SoundManager.Instance != null) HypercasualGameEngine.SoundManager.Instance.PlaySandFlowBucketComplete();
                        pot.status = BucketStatus.Done;
                        pot.popVy = 0.5f; // start with gentle upward lift
                        pot.beltY = 0f;

                        // Delete percent text
                        if (pot.percentText3D != null)
                        {
                            Destroy(pot.percentText3D.gameObject);
                            pot.percentText3D = null;
                        }
                    }
                    else if (pot.percentText3D != null)
                    {
                        int pct = Mathf.FloorToInt(((float)(pot.maxQuota - pot.quota) / pot.maxQuota) * 100f);
                        pot.percentText3D.text = $"{pct}%";
                    }
                }

                // Update visual position
                UpdateBeltBucketPosition(pot);

                // Scale animations: suction pulse + wobble
                if (pot.transform3D != null && pot.status == BucketStatus.Belt)
                {
                    float scaleX = 1f, scaleY = 1f, scaleZ = 1f;

                    // Suction pulse: brief scale-up pop when absorbing pixels
                    if (pot.suctionPulse > 0f)
                    {
                        pot.suctionPulse -= Time.deltaTime;
                        float pulseT = Mathf.Clamp01(pot.suctionPulse / 0.15f);
                        float pulse = Mathf.Sin(pulseT * Mathf.PI) * 0.12f;
                        scaleX += pulse;
                        scaleY += pulse * 0.5f;
                        scaleZ += pulse;
                    }

                    // Wobble: oscillating squash/stretch (landing, suction)
                    if (pot.wobbleTime > 0f)
                    {
                        pot.wobbleTime -= Time.deltaTime;
                        float fade = Mathf.Clamp01(pot.wobbleTime / 0.2f);
                        float wobble = Mathf.Sin(pot.wobbleTime * 30f) * pot.wobbleIntensity * fade;
                        scaleX += wobble;
                        scaleY -= wobble * 0.5f;
                        scaleZ += wobble;
                    }

                    pot.transform3D.localScale = new Vector3(scaleX, scaleY, scaleZ);
                }
            }

            UpdateCounterText();
            UpdateFlyingBlocks();
            UpdateExplosionParticles();
        }

        private void UpdateBeltBucketPosition(BucketData pot)
        {
            if (pot.transform3D == null) return;

            // beltX is 0-100 percent of belt width, map to world X
            float worldX = beltLeftX + (pot.beltX / 100f) * beltWidth;
            pot.transform3D.localPosition = new Vector3(worldX, 0.34f, beltCenterZ);
        }

        private void UpdateCounterText()
        {
            onCounterChanged?.Invoke(ActiveBucketCount(), maxSlots);
        }

        public bool AllBeltStuck()
        {
            int activeCount = 0;
            foreach (var b in beltBuckets)
            {
                if (b.status == BucketStatus.Belt)
                {
                    activeCount++;
                    if (b.stuckCount < 1) return false;
                }
            }
            return activeCount > 0 && activeCount >= maxSlots;
        }

        public bool HasActiveBeltBuckets()
        {
            foreach (var b in beltBuckets)
                if (b.status == BucketStatus.Belt || (b.status == BucketStatus.Done && b.opacity > 0))
                    return true;
            return false;
        }

        public void AddSlot()
        {
            maxSlots++;
            UpdateCounterText();
        }

        public void CleanupRemovedBuckets()
        {
            for (int i = beltBuckets.Count - 1; i >= 0; i--)
            {
                if (beltBuckets[i].status == BucketStatus.Removed)
                {
                    ReturnFillBlocksToPool(beltBuckets[i]);
                    if (beltBuckets[i].transform3D != null)
                        Destroy(beltBuckets[i].transform3D.gameObject);
                    beltBuckets.RemoveAt(i);
                }
            }
        }

        private void ReturnFillBlocksToPool(BucketData pot)
        {
            if (pot.fillContainer == null) return;
            for (int c = pot.fillContainer.childCount - 1; c >= 0; c--)
                ReturnSphere(pot.fillContainer.GetChild(c).gameObject);
        }

        // =====================================================================
        // Fill grains — stationary spheres inside bucket showing fill level
        // =====================================================================

        private void SpawnFillBlocks(BucketData pot, int count)
        {
            if (pot.fillContainer == null || pot.maxQuota <= 0) return;

            // Get or create cached material
            if (!flyingBlockMats.TryGetValue(pot.trueColorId, out Material mat))
            {
                mat = CreateLitMaterial(SandSimulator.PaletteColors[pot.trueColorId]);
                flyingBlockMats[pot.trueColorId] = mat;
            }

            // Bucket fill dimensions (local to bucket root)
            float bucketRadius = 0.1f;
            float bucketBottom = -0.02f;
            float bucketTop = 0.12f;
            float fillHeight = bucketTop - bucketBottom;
            float fillFraction = 1f - ((float)pot.quota / pot.maxQuota); // 0..1

            for (int i = 0; i < count; i++)
            {
                GameObject block = AcquireSphere(pot.fillContainer);

                float blockSize = Random.Range(0.025f, 0.045f);
                block.transform.localScale = new Vector3(blockSize, blockSize, blockSize);

                // Place at current fill level with some randomness
                float y = bucketBottom + fillFraction * fillHeight + Random.Range(-0.02f, 0.01f);
                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float r = Random.Range(0f, bucketRadius * 0.8f);
                float x = Mathf.Cos(angle) * r;
                float z = Mathf.Sin(angle) * r;
                block.transform.localPosition = new Vector3(x, y, z);
                block.transform.localRotation = Random.rotation;

                block.GetComponent<MeshRenderer>().sharedMaterial = mat;
            }
        }

        // =====================================================================
        // Flying Blocks — colored spheres that fly from sand display to bucket
        // =====================================================================

        private void SpawnFlyingBlocks(BucketData pot, List<Vector2Int> pixelPositions)
        {
            if (pot.transform3D == null || pixelPositions == null || pixelPositions.Count == 0) return;

            // Get or create cached material
            if (!flyingBlockMats.TryGetValue(pot.trueColorId, out Material mat))
            {
                mat = CreateLitMaterial(SandSimulator.PaletteColors[pot.trueColorId]);
                flyingBlockMats[pot.trueColorId] = mat;
            }

            float spawnWorldY = Mathf.Max(sandWorldMin.y, sandWorldMax.y) + 0.05f;

            for (int i = 0; i < pixelPositions.Count; i++)
            {
                Vector2Int gridPos = pixelPositions[i];

                // Map grid X to world X
                float normalizedX = (float)gridPos.x / (SandSimulator.GRID_SIZE - 1);
                float worldX = Mathf.Lerp(sandWorldMin.x, sandWorldMax.x, normalizedX);

                // Map grid Y to world Z (grid Y=0 is top/far, Y=79 is bottom/near belt)
                float normalizedY = (float)gridPos.y / (SandSimulator.GRID_SIZE - 1);
                float worldZ = Mathf.Lerp(sandWorldMax.z, sandWorldMin.z, normalizedY);

                GameObject block = AcquireSphere(beltBucketsContainer.parent);
                block.transform.localScale = new Vector3(0.06f, 0.06f, 0.06f);

                Vector3 startPos = new Vector3(worldX, spawnWorldY, worldZ);
                block.transform.position = startPos;

                block.GetComponent<MeshRenderer>().sharedMaterial = mat;

                flyingBlocks.Add(new FlyingBlock
                {
                    transform = block.transform,
                    startPos = startPos,
                    targetBucket = pot,
                    elapsed = 0f,
                    duration = Random.Range(0.3f, 0.6f),
                    spreadX = Random.Range(-0.1f, 0.1f),
                    spreadZ = Random.Range(-0.06f, 0.06f)
                });
            }
        }

        private void UpdateFlyingBlocks()
        {
            for (int i = flyingBlocks.Count - 1; i >= 0; i--)
            {
                FlyingBlock fb = flyingBlocks[i];
                fb.elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(fb.elapsed / fb.duration);

                // Always track the bucket's current position (it moves on the belt)
                Vector3 targetPos = (fb.targetBucket != null && fb.targetBucket.transform3D != null)
                    ? fb.targetBucket.transform3D.position
                    : fb.startPos;

                // Lerp position with Y arc + wide lateral spread that narrows toward bucket
                Vector3 pos = Vector3.Lerp(fb.startPos, targetPos, t);
                float spread = Mathf.Sin(t * Mathf.PI) ; // peaks at mid-flight, 0 at start and end
                pos.x += fb.spreadX * spread;
                pos.z += fb.spreadZ * spread;
                pos.y += spread * 0.3f;

                // Scale down as it approaches
                float scale = Mathf.Lerp(0.06f, 0.02f, t);

                if (fb.transform != null)
                {
                    fb.transform.position = pos;
                    fb.transform.localScale = Vector3.one * scale;
                }

                flyingBlocks[i] = fb;

                if (t >= 1f)
                {
                    if (fb.transform != null) ReturnSphere(fb.transform.gameObject);
                    flyingBlocks.RemoveAt(i);
                }
            }
        }

        // =====================================================================
        // Explosion Particles — burst of colored spheres on bucket completion
        // =====================================================================

        private void SpawnExplosion(Vector3 position, int colorId, int count = 12)
        {
            if (!flyingBlockMats.TryGetValue(colorId, out Material mat))
            {
                mat = CreateLitMaterial(SandSimulator.PaletteColors[colorId]);
                flyingBlockMats[colorId] = mat;
            }

            for (int i = 0; i < count; i++)
            {
                GameObject p = AcquireSphere(beltBucketsContainer.parent);
                p.transform.position = position;
                float s = Random.Range(0.03f, 0.07f);
                p.transform.localScale = new Vector3(s, s, s);

                p.GetComponent<MeshRenderer>().sharedMaterial = mat;

                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float speed = Random.Range(1.5f, 3.5f);
                float vy = Random.Range(2f, 5f);

                explosionParticles.Add(new ExplosionParticle
                {
                    transform = p.transform,
                    velocity = new Vector3(Mathf.Cos(angle) * speed, vy, Mathf.Sin(angle) * speed),
                    elapsed = 0f,
                    lifetime = Random.Range(0.5f, 0.9f),
                    isCylinder = false
                });
            }
        }

        private void SpawnCircleExplosion(Vector3 position, int colorId, int count = 16)
        {
            Color baseColor = SandSimulator.PaletteColors[colorId];

            for (int i = 0; i < count; i++)
            {
                GameObject p = AcquireCylinder(beltBucketsContainer.parent);
                p.transform.position = position;

                float s = Random.Range(0.36f, 0.88f);
                p.transform.localScale = new Vector3(s, 0.012f, s);

                // Random rotation so circles tumble in all directions
                p.transform.rotation = Random.rotation;

                // Slight color variation per particle
                float variation = Random.Range(0.85f, 1.15f);
                Color pColor = new Color(
                    Mathf.Clamp01(baseColor.r * variation),
                    Mathf.Clamp01(baseColor.g * variation),
                    Mathf.Clamp01(baseColor.b * variation), 1f);
                Material mat = CreateLitMaterial(pColor);
                mat.SetFloat("_Metallic", 0.3f);
                mat.SetFloat("_Smoothness", 0.8f);
                p.GetComponent<MeshRenderer>().material = mat;

                float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
                float speed = Random.Range(1f, 2.5f);
                float vy = Random.Range(2f, 4.5f);

                explosionParticles.Add(new ExplosionParticle
                {
                    transform = p.transform,
                    velocity = new Vector3(Mathf.Cos(angle) * speed, vy, Mathf.Sin(angle) * speed),
                    elapsed = 0f,
                    lifetime = Random.Range(0.6f, 1.1f),
                    isCylinder = true
                });
            }
        }

        private void UpdateExplosionParticles()
        {
            for (int i = explosionParticles.Count - 1; i >= 0; i--)
            {
                ExplosionParticle ep = explosionParticles[i];
                ep.elapsed += Time.deltaTime;
                float t = ep.elapsed / ep.lifetime;

                // Gravity
                ep.velocity.y -= 9.8f * Time.deltaTime;

                if (ep.transform != null)
                {
                    ep.transform.position += ep.velocity * Time.deltaTime;
                    // Spin
                    ep.transform.Rotate(Vector3.one * 360f * Time.deltaTime, Space.Self);
                    // Shrink
                    float s = Mathf.Lerp(0.05f, 0f, t);
                    ep.transform.localScale = Vector3.one * Mathf.Max(0.005f, s);
                }

                explosionParticles[i] = ep;

                if (t >= 1f)
                {
                    if (ep.transform != null)
                    {
                        if (ep.isCylinder) ReturnCylinder(ep.transform.gameObject);
                        else ReturnSphere(ep.transform.gameObject);
                    }
                    explosionParticles.RemoveAt(i);
                }
            }
        }

        // =====================================================================
        // Material helpers
        // =====================================================================

        private static Material CreateLitMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        private static void SetMaterialTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = 3000;
        }

        private static void SetMaterialAlpha(MeshRenderer renderer, float alpha)
        {
            if (renderer == null) return;
            Material[] mats = renderer.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Color c = mats[i].GetColor("_BaseColor");
                c.a = alpha;
                mats[i].SetColor("_BaseColor", c);

                if (alpha < 1f)
                {
                    SetMaterialTransparent(mats[i]);
                }
                else
                {
                    mats[i].SetFloat("_Surface", 0f);
                    mats[i].SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    mats[i].SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    mats[i].SetInt("_ZWrite", 1);
                    mats[i].DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mats[i].renderQueue = -1;
                }
            }
            renderer.materials = mats;
        }

        // =====================================================================
        // Gizmos — visualize each bucket's extraction zone on the sand display
        // =====================================================================

        private void OnDrawGizmos()
        {
            if (beltBuckets == null || sandWorldMin == sandWorldMax) return;

            float sandWidth = sandWorldMax.x - sandWorldMin.x;
            float sandHeight = sandWorldMax.z - sandWorldMin.z;
            // Bottom 5 rows in grid-Z space
            float bottomFraction = 5f / SandSimulator.GRID_SIZE;
            // Extraction radius in grid = 8 pixels, mapped to world
            float radiusFraction = 8f / SandSimulator.GRID_SIZE;
            float radiusWorld = radiusFraction * sandWidth;

            // The "bottom" of the picture: in grid Y, bottom = high Y values (75-79)
            // In world Z, bottom of picture = min Z (closest to belt)
            float bottomStartZ = Mathf.Min(sandWorldMin.z, sandWorldMax.z);
            float bottomEndZ = bottomStartZ + Mathf.Abs(sandHeight) * bottomFraction;
            float bandHeight = bottomEndZ - bottomStartZ;
            float gizmoY = Mathf.Max(sandWorldMin.y, sandWorldMax.y) + 0.02f;

            foreach (var pot in beltBuckets)
            {
                if (pot.status != BucketStatus.Belt) continue;
                if (pot.transform3D == null) continue;

                float bucketWorldX = pot.transform3D.position.x;
                bool overSand = bucketWorldX >= sandWorldMin.x && bucketWorldX <= sandWorldMax.x;
                if (!overSand) continue;

                // Draw extraction zone: a rect at the bottom of the sand display
                Color gizmoColor = SandSimulator.PaletteColors[pot.trueColorId];
                gizmoColor.a = 0.5f;
                Gizmos.color = gizmoColor;

                float zoneLeft = Mathf.Max(bucketWorldX - radiusWorld, sandWorldMin.x);
                float zoneRight = Mathf.Min(bucketWorldX + radiusWorld, sandWorldMax.x);
                float zoneWidth = zoneRight - zoneLeft;
                float zoneCenterX = (zoneLeft + zoneRight) * 0.5f;
                float zoneCenterZ = (bottomStartZ + bottomEndZ) * 0.5f;

                Vector3 center = new Vector3(zoneCenterX, gizmoY, zoneCenterZ);
                Vector3 size = new Vector3(zoneWidth, 0.01f, bandHeight);
                Gizmos.DrawCube(center, size);

                // Wire outline
                gizmoColor.a = 1f;
                Gizmos.color = gizmoColor;
                Gizmos.DrawWireCube(center, size);
            }

            // Draw the full bottom-5-rows band as a faint outline
            Gizmos.color = new Color(1f, 1f, 1f, 0.2f);
            float fullBandCenterX = (sandWorldMin.x + sandWorldMax.x) * 0.5f;
            float fullBandCenterZ = (bottomStartZ + bottomEndZ) * 0.5f;
            Gizmos.DrawWireCube(
                new Vector3(fullBandCenterX, gizmoY, fullBandCenterZ),
                new Vector3(sandWidth, 0.01f, bandHeight));
        }
    }
}
