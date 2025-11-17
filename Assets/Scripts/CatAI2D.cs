using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class CatAI2D : MonoBehaviour
{
    public Rigidbody2D rb;
    public SpriteRenderer sr;
    public Sprite idleSprite;
    public Sprite movingSprite;
    public Color[] randomPalette;

    [Header("Move")]
    public float wanderSpeed = 2.0f;
    public float changeDirInterval = 2.5f;
    float _timer;
    Vector2 _dir;

    [Header("Flee")]
    public float fleeFromDogDist = 3.0f;
    public float fleeSpeed = 5.0f;
    public float despawnAfterFleeSec = 1.5f;
    bool fleeing;

    // ── facing + flip controls ─────────────────────────────────────────────
    public enum FlipAxis { X, Y }

    [Header("Facing / Flip")]
    public bool faceMovementDirection = true; // rotate to velocity
    public float minFacingSpeed = 0.1f;
    public bool flipWhileMoving = true;
    public FlipAxis flipAxis = FlipAxis.Y;    // choose axis to flip on
    public float flipHzMin = 6f, flipHzMax = 12f, flipSpeedRef = 4f;
    float _flipTimer;
    // ──────────────────────────────────────────────────────────────────────

    void Reset() { rb = GetComponent<Rigidbody2D>(); }

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!sr) sr = GetComponentInChildren<SpriteRenderer>();
        _timer = changeDirInterval;
        _dir = Random.insideUnitCircle.normalized;

        if (sr && randomPalette != null && randomPalette.Length > 0)
            sr.color = randomPalette[Random.Range(0, randomPalette.Length)];

        SetSprite(false);
    }

    void Update()
    {
        if (fleeing) { HandleFlipWiggle(); return; }

        _timer -= Time.deltaTime;
        if (_timer <= 0f)
        {
            _dir = Random.insideUnitCircle.normalized;
            _timer = changeDirInterval;
        }

        HandleFlipWiggle();
        SetSprite(true);
    }

    void FixedUpdate()
    {
        // 1) Apply wander motion when NOT fleeing
        if (!fleeing)
        {
            rb.linearVelocity = _dir * wanderSpeed;
        }

        // 2) Detect dog & enter flee (set flee velocity) — only when not already fleeing
        if (!fleeing)
        {
            var dog = Object.FindFirstObjectByType<DogAI2D>();
            if (dog)
            {
                float d = Vector2.Distance(dog.rb.position, rb.position);
                if (d <= fleeFromDogDist)
                {
                    Vector2 flee = (rb.position - dog.rb.position).normalized;
                    rb.linearVelocity = flee * fleeSpeed;
                    fleeing = true;
                    SetSprite(true);
                    Invoke(nameof(Despawn), despawnAfterFleeSec);
                }
            }
        }

        // 3) Face movement direction in ALL cases (wander or flee), AFTER velocity is finalized
        if (faceMovementDirection)
        {
            Vector2 v = rb.linearVelocity;
            if (v.sqrMagnitude >= minFacingSpeed * minFacingSpeed)
            {
                float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                rb.MoveRotation(ang);
            }
        }
    }

    void HandleFlipWiggle()
    {
        if (!flipWhileMoving || !sr) return;

        float speed = rb.linearVelocity.magnitude;
        if (speed < 0.1f) return;

        float hz = Mathf.Lerp(flipHzMin, flipHzMax, Mathf.Clamp01(speed / Mathf.Max(0.01f, flipSpeedRef)));
        float interval = 1f / Mathf.Max(0.01f, hz);
        _flipTimer += Time.deltaTime;

        if (_flipTimer >= interval)
        {
            if (flipAxis == FlipAxis.X)
                sr.flipX = !sr.flipX;
            else
                sr.flipY = !sr.flipY;

            _flipTimer = 0f;
        }
    }

    void SetSprite(bool moving)
    {
        if (!sr) return;
        if (moving && movingSprite) sr.sprite = movingSprite;
        else if (idleSprite) sr.sprite = idleSprite;
    }

    void Despawn() { Destroy(gameObject); }
}
