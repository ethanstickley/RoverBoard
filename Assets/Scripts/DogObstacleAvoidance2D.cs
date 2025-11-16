using UnityEngine;

/// <summary>
/// Non-collider obstacle avoidance for the Dog.
/// Steers/brakes when approaching solids so it "appears" to collide, and gently slides along walls.
/// Attach to the same GameObject as DogAI2D + Rigidbody2D.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class DogObstacleAvoidance2D : MonoBehaviour
{
    [Header("Layers")]
    [Tooltip("Layers considered SOLID (match your Buildings/Tall Obstacles).")]
    public LayerMask solidMask; // e.g., Solids or Default

    [Header("Probes")]
    [Tooltip("Radius for overlap & cast checks.")]
    public float probeRadius = 0.25f;
    [Tooltip("How far ahead to probe in front of current velocity (units).")]
    public float forwardProbeDistance = 1.0f;
    [Tooltip("Side probe angle relative to velocity (deg).")]
    public float sideProbeAngle = 35f;
    [Tooltip("Side probe distance (units).")]
    public float sideProbeDistance = 0.8f;

    [Header("Response")]
    [Tooltip("Lateral steering strength applied away from obstacle normals.")]
    public float avoidForce = 30f;
    [Tooltip("How much to damp velocity if heading into a wall (0..1 per second).")]
    [Range(0f, 1f)] public float brakePerSec = 0.35f;
    [Tooltip("Max fraction of current speed we�ll remove in one FixedUpdate when facing a wall.")]
    [Range(0f, 1f)] public float maxInstantBrakeFraction = 0.3f;
    [Tooltip("Project remaining velocity to slide along walls instead of stopping.")]
    public bool slideAlongWalls = true;

    [Header("Unstuck")]
    [Tooltip("If overlapping a solid, push out by this factor.")]
    public float depenetrationStrength = 3f;

    [Header("Gates")]
    [Tooltip("Ignore avoidance when moving slower than this.")]
    public float minSpeedToAvoid = 0.2f;

    Rigidbody2D rb;

    void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        // A sensible default: everything except Triggers/Characters. Set explicitly in Inspector.
        solidMask = LayerMask.GetMask("Default", "Solids");
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void FixedUpdate()
    {
        if (!rb) return;
        Vector2 v = rb.linearVelocity;
        float speed = v.magnitude;
        if (speed < minSpeedToAvoid) { TryDepenetrate(); return; }

        Vector2 dir = v / Mathf.Max(0.0001f, speed);
        bool adjusted = false;

        // 1) Forward circle cast
        RaycastHit2D hitF = Physics2D.CircleCast(rb.position, probeRadius, dir, forwardProbeDistance, solidMask);
        if (hitF.collider)
        {
            AdjustForHit(hitF, ref v, dir);
            adjusted = true;
        }

        // 2) Side probes (left/right) to bias early deflection
        Vector2 dirLeft = Rotate(dir, +sideProbeAngle * Mathf.Deg2Rad);
        Vector2 dirRight = Rotate(dir, -sideProbeAngle * Mathf.Deg2Rad);

        RaycastHit2D hitL = Physics2D.CircleCast(rb.position, probeRadius * 0.9f, dirLeft, sideProbeDistance, solidMask);
        RaycastHit2D hitR = Physics2D.CircleCast(rb.position, probeRadius * 0.9f, dirRight, sideProbeDistance, solidMask);

        if (hitL.collider && !hitR.collider)
        {
            // steer to the right if left side is blocked
            ApplyLateral(ref v, dir, +1f);
            adjusted = true;
        }
        else if (hitR.collider && !hitL.collider)
        {
            // steer to the left if right side is blocked
            ApplyLateral(ref v, dir, -1f);
            adjusted = true;
        }

        // 3) If overlapping (already too close), nudge out
        if (TryDepenetrate()) adjusted = true;

        if (adjusted)
        {
            rb.linearVelocity = v;
        }
    }

    void AdjustForHit(RaycastHit2D hit, ref Vector2 v, Vector2 dir)
    {
        // Brake component that faces the normal (remove inward velocity)
        Vector2 n = hit.normal.normalized;
        float inward = Vector2.Dot(v, -n);
        if (inward > 0f)
        {
            float dt = Time.fixedDeltaTime;
            float remove = Mathf.Min(inward, v.magnitude * maxInstantBrakeFraction + brakePerSec * v.magnitude * dt);
            v += n * remove; // push velocity away from wall

            // Optional slide: project velocity onto tangent
            if (slideAlongWalls)
            {
                Vector2 tangent = new Vector2(-n.y, n.x);
                float along = Vector2.Dot(v, tangent);
                v = tangent * along; // eliminates remaining normal component
            }
        }

        // Add a lateral steer away from the wall normal
        Vector2 lateral = PerpAway(dir, n);
        v += lateral * (avoidForce * Time.fixedDeltaTime);
    }

    bool TryDepenetrate()
    {
        // If overlapping a solid, push out along gradient (average of hit normals)
        Collider2D[] overlaps = Physics2D.OverlapCircleAll(rb.position, probeRadius * 0.95f, solidMask);
        if (overlaps == null || overlaps.Length == 0) return false;

        Vector2 push = Vector2.zero;
        int count = 0;
        foreach (var col in overlaps)
        {
            if (!col) continue;
            Vector2 closest = col.ClosestPoint(rb.position);
            Vector2 away = (rb.position - closest);
            if (away.sqrMagnitude > 1e-6f)
            {
                push += away.normalized;
                count++;
            }
        }

        if (count > 0)
        {
            push /= count;
            rb.position += push * (depenetrationStrength * Time.fixedDeltaTime);
            // Also remove inward component of velocity
            float inward = Vector2.Dot(rb.linearVelocity, -push);
            if (inward > 0f)
                rb.linearVelocity += push * inward;
            return true;
        }
        return false;
    }

    // Returns a lateral vector (perpendicular to dir) that points away from wall normal.
    static Vector2 PerpAway(Vector2 dir, Vector2 wallNormal)
    {
        // Two perpendiculars: ( -dir.y, dir.x ) and ( dir.y, -dir.x )
        Vector2 p1 = new Vector2(-dir.y, dir.x);
        Vector2 p2 = -p1;
        // Pick the one that points away from the wall (positive dot with normal)
        return (Vector2.Dot(p1, wallNormal) > Vector2.Dot(p2, wallNormal)) ? p1 : p2;
    }

    static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector2(c * v.x - s * v.y, s * v.x + c * v.y);
    }

    void ApplyLateral(ref Vector2 v, Vector2 dir, float sideSign)
    {
        Vector2 lateral = new Vector2(-dir.y, dir.x) * sideSign; // left/right
        v += lateral * (avoidForce * 0.5f * Time.fixedDeltaTime);
    }
}
