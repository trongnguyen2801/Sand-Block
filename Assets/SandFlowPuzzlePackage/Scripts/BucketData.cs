using UnityEngine;

namespace SandFlowPuzzle
{
    public enum BucketStatus { Grid, Belt, Done, Removed }

    [System.Serializable]
    public class BucketData
    {
        public string id;
        public int col;
        public int row;
        public int trueColorId;
        public string colorStr;
        public bool isMystery;
        public bool isHidden;
        public BucketStatus status;
        public int quota;
        public int maxQuota;

        // Belt-specific runtime state
        public float beltX;
        public int extractedThisLoop;
        public int stuckCount;
        public float scale;
        public float opacity;
        public float beltY;
        public float popVy;
        public float rotation;

        // Wobble animation
        public float wobbleTime;
        public float wobbleIntensity;

        // Grid animations (jump on becoming clickable, squeeze on landing)
        public bool prevClickable;
        public float jumpVy;
        public float jumpY;
        public float squeezeTime;
        public float suctionPulse; // brief scale-up when absorbing pixels

        // Fly-to-belt animation
        public bool flyToBelt;
        public float flyToBeltElapsed;
        public float flyToBeltDuration;
        public Vector3 flyToBeltStartPos;
        public Vector3 flyToBeltEndPos;
        public float flyToBeltTargetLocalX;

        // 3D references (set at runtime)
        public Transform transform3D;           // root transform of the 3D bucket
        public MeshRenderer bodyRenderer;        // cylinder body renderer
        public Collider bodyCollider;            // for raycast click detection
        public MeshRenderer innerCircleRenderer; // dark opening at top
        public TMPro.TextMeshPro percentText3D;   // percentage text (belt)
        public TextMesh mysteryText3D;           // "?" text (grid, hidden)
        public Transform fillContainer;          // parent of stationary fill blocks inside bucket
        public float suctionCooldown;             // delay before suction starts after joining belt

        public BucketData(string id, int col, int row, int trueColorId, string colorStr, bool isMystery)
        {
            this.id = id;
            this.col = col;
            this.row = row;
            this.trueColorId = trueColorId;
            this.colorStr = colorStr;
            this.isMystery = isMystery;
            this.isHidden = isMystery;
            this.status = BucketStatus.Grid;
            this.quota = 0;
            this.maxQuota = 0;

            // Belt defaults
            this.beltX = -25f;
            this.extractedThisLoop = 0;
            this.stuckCount = 0;
            this.scale = 1f;
            this.opacity = 1f;
            this.beltY = 0f;
            this.popVy = -4f;
            this.rotation = 0f;
            this.wobbleTime = 0f;
            this.wobbleIntensity = 0f;
            this.prevClickable = false;
            this.jumpVy = 0f;
            this.jumpY = 0f;
            this.squeezeTime = 0f;
            this.suctionPulse = 0f;
            this.flyToBelt = false;
            this.flyToBeltElapsed = 0f;
            this.flyToBeltDuration = 0.35f;
            this.flyToBeltStartPos = Vector3.zero;
            this.flyToBeltEndPos = Vector3.zero;
            this.flyToBeltTargetLocalX = 0f;
        }
    }
}
