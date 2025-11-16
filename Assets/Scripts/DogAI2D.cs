using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class DogAI2D : MonoBehaviour
{
    [Header("Refs")]
    public Rigidbody2D rb;
    [Tooltip("Player transform the dog follows/tows around.")]
    public Transform player;
    [Tooltip("Player Rigidbody2D used to read current velocity (for anticipation).")]
    public Rigidbody2D playerRb;
    [Tooltip("SpriteRenderer used for flipping/facing/poop/pee pose.")]
    public SpriteRenderer spriteRenderer;
    [Tooltip("PlayerController2D reference to read camera-relative input intent.")]
    public PlayerController2D playerCtrl;   // assign in Inspector

    [Header("Mood / Strength")]
    [Tooltip("0 = naughty (chases distractions/orbits), 1 = very good (anticipates & tows).")]
    [Range(0f, 1f)] public float goodBad01 = 0.8f;
    [Tooltip("How strong the dog is overall (used by leash dynamics indirectly).")]
    [Range(0f, 1f)] public float baseStrength01 = 0.55f;

    [Header("Movement (general)")]
    [Tooltip("Threshold speed above which the player is considered 'moving'.")]
    public float playerMovingSpeed = 0.15f;
    [Tooltip("Base movement acceleration for general moves (not towing).")]
    public float maxAccel = 20f;
    [Tooltip("Base max speed for general moves (not towing).")]
    public float maxSpeed = 5f;
    [Tooltip("Per-second velocity damping; higher = more drag.")]
    [Range(0f, 1f)] public float dragPerSec = 0.08f;

    [Header("Good Dog Anticipation / Towing")]
    [Tooltip("How far in front of the player the dog aims while towing (min..max scales with player speed).")]
    public Vector2 goodLeadDistanceRange = new Vector2(2.5f, 4.5f);
    [Tooltip("Weight of the player's current input direction when predicting where to run.")]
    public float anticipInputWeight = 1.5f;
    [Tooltip("Weight of the player's current velocity direction when predicting where to run.")]
    public float anticipVelocityWeight = 1.0f;
    [Tooltip("Dog's top speed while in 'good boy' towing mode.")]
    public float goodTowSpeed = 8.0f;
    [Tooltip("How quickly the dog accelerates into the towing position.")]
    public float goodTowAccel = 30f;

    [Header("Good Return (when player stops)")]
    [Tooltip("When ON, if the player stops the dog returns and idles near the player.")]
    public bool goodReturnEnabled = true;
    [Tooltip("Ring around the player where the dog will stand while waiting (min..max).")]
    public Vector2 returnDistanceRange = new Vector2(0.7f, 1.1f);
    [Tooltip("Speed the dog uses to return to the idle ring.")]
    public float returnSpeed = 4.0f;
    [Tooltip("Acceleration used while returning to the idle ring.")]
    public float returnAccel = 20f;
    [Tooltip("Stop moving if within this distance of the chosen idle spot (prevents jitter).")]
    public float returnStopWithin = 0.12f;

    [Header("Interests")]
    [Tooltip("Current target of distraction (Treat/Hydrant/Cat). Leave null for none.")]
    public Transform CurrentInterest;
    [Tooltip("Within this distance of an interest, the dog slows/stops.")]
    public float interestArriveDist = 0.5f;
    [Tooltip("Chase speed used while pursuing an interest.")]
    public float interestChaseSpeed = 6f;

    [Header("Bladder / Pee")]
    [Tooltip("Max pee capacity; starts full each level. Must be emptied at hydrants to finish level.")]
    public float bladderMax = 10f;
    [Tooltip("Current bladder amount (drains while peeing).")]
    public float bladder;
    [Tooltip("How fast bladder drains per second while peeing at a hydrant.")]
    public float peeDrainPerSecond = 2.5f;

    // Pee visuals (optional; keep your existing assignments)
    [Header("Pee Visuals")]
    public GameObject peePrefab;
    public int peeDropsPerBurst = 5;
    public float peeBurstEvery = 0.35f;
    public Vector2 peeSpreadRadius = new Vector2(0.08f, 0.38f);
    public Vector2 peeScaleRange = new Vector2(0.85f, 1.25f);
    public Vector2 peeRotationJitter = new Vector2(-22f, 22f);

    bool _isPeeing;
    Transform _peeSource;
    Coroutine _peeCo;

    [Header("Poop")]
    public GameObject poopPrefab;
    [Tooltip("Sprite shown while dog is pooping AND peeing (shared pose).")]
    public Sprite poopPoseSprite;
    public float poopPauseSeconds = 2.0f;
    public Vector2 poopIntervalSec = new Vector2(12f, 25f);
    float _poopTimer;
    Sprite _defaultSprite;

    // Facing + flip controls (2.2)
    public enum FlipAxis { X, Y }

    [Header("Facing / Flip")]
    public bool faceMovementDirection = true;
    public float minFacingSpeed = 0.1f;
    public bool flipWhileMoving = true;
    public FlipAxis flipAxis = FlipAxis.Y;
    public float flipHzMin = 6f, flipHzMax = 12f;
    public float flipSpeedRef = 4f;
    float _flipTimer;

    // Player separation bubble
    [Header("Player Separation")]
    public float avoidNearPlayerRadius = 0.35f;
    public float avoidNearPlayerStrength = 30f;
    public float avoidNearPlayerDamp = 1.5f;

    // internal
    string _fixationName = "None";
    Vector2 _idleOffset;
    float _idleTimer;

    enum DogState { Free, Interest, Peeing, Pooping }
    DogState _state = DogState.Free;

    void Reset() { rb = GetComponent<Rigidbody2D>(); }

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!spriteRenderer) spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        _defaultSprite = spriteRenderer ? spriteRenderer.sprite : null;

        bladder = bladderMax; // start full
        PickIdleOffset();
        _idleTimer = Random.Range(1.0f, 2.0f);
        _poopTimer = Random.Range(poopIntervalSec.x, poopIntervalSec.y);
    }

    void Update()
    {
        HandleFlipWiggle();

        // SAFETY: if bladder is empty, never keep/accept a hydrant interest
        if (CurrentInterest && IsHydrant(CurrentInterest) && !NeedsToPee())
        {
            CurrentInterest = null;
            if (_state == DogState.Interest) _state = DogState.Free;
        }

        if (_state == DogState.Free || _state == DogState.Interest)
        {
            _poopTimer -= Time.deltaTime;
            if (_poopTimer <= 0f)
            {
                StartCoroutine(DoPoop());
                _poopTimer = Random.Range(poopIntervalSec.x, poopIntervalSec.y);
            }
        }

        if (_state == DogState.Peeing) _fixationName = "Peeing";
        else if (CurrentInterest) _fixationName = DeriveInterestName(CurrentInterest);
        else _fixationName = "None";

        if (!IsPlayerMoving() && _state == DogState.Free)
        {
            _idleTimer -= Time.deltaTime;
            if (_idleTimer <= 0f)
            {
                PickIdleOffset();
                _idleTimer = Random.Range(1.0f, 2.0f);
            }
        }
    }

    void FixedUpdate()
    {
        rb.linearVelocity *= Mathf.Clamp01(1f - dragPerSec * Time.fixedDeltaTime);

        if (faceMovementDirection)
        {
            Vector2 v = rb.linearVelocity;
            if (v.sqrMagnitude >= minFacingSpeed * minFacingSpeed)
            {
                float ang = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
                rb.MoveRotation(ang);
            }
        }

        switch (_state)
        {
            case DogState.Pooping:
                rb.linearVelocity *= 0.9f;
                return;
            case DogState.Peeing:
                if (bladder > 0f)
                {
                    float before = bladder;
                    bladder = Mathf.Max(0f, bladder - peeDrainPerSecond * Time.fixedDeltaTime);
                    if (Mathf.FloorToInt(before) != Mathf.FloorToInt(bladder))
                        Debug.Log($"Dog Peeing... bladder {bladder:0.0}/{bladderMax:0}");
                }
                else
                {
                    StopPee(); // this will also clear hydrant interest
                }
                rb.linearVelocity *= 0.8f;
                return;
        }

        if (CurrentInterest)
        {
            // If hydrant but we don't need to pee anymore -> drop it
            if (IsHydrant(CurrentInterest) && !NeedsToPee())
            {
                CurrentInterest = null;
                _state = DogState.Free;
            }

            if (CurrentInterest)
            {
                Vector2 to = (Vector2)CurrentInterest.position - rb.position;
                if (to.magnitude > interestArriveDist) MoveToward(CurrentInterest.position, interestChaseSpeed, maxAccel);
                else rb.linearVelocity *= 0.85f;
                ApplyPlayerSeparation();
                return;
            }
        }

        // Good vs Bad behavior (unchanged)
        if (goodBad01 >= 0.65f)
        {
            if (IsPlayerMoving())
            {
                Vector2 inputDir = (playerCtrl && playerCtrl.lastMoveInputWorld.sqrMagnitude > 1e-6f)
                    ? playerCtrl.lastMoveInputWorld.normalized : Vector2.zero;

                Vector2 velDir = (playerRb && playerRb.linearVelocity.sqrMagnitude > 1e-6f)
                    ? playerRb.linearVelocity.normalized : Vector2.zero;

                Vector2 anticipDir = inputDir * anticipInputWeight + velDir * anticipVelocityWeight;
                if (anticipDir.sqrMagnitude < 1e-6f) anticipDir = Vector2.right; else anticipDir.Normalize();

                float playerSpeed = playerRb ? playerRb.linearVelocity.magnitude : 0f;
                float speed01 = Mathf.Clamp01(playerSpeed / Mathf.Max(0.01f, goodTowSpeed));
                float lead = Mathf.Lerp(goodLeadDistanceRange.x, goodLeadDistanceRange.y, speed01);

                Vector2 target = (Vector2)player.position + anticipDir * lead;
                MoveToward(target, goodTowSpeed, goodTowAccel);
            }
            else if (goodReturnEnabled)
            {
                Vector2 center = player ? (Vector2)player.position : rb.position;
                Vector2 target = center + _idleOffset;
                float distToTarget = Vector2.Distance(rb.position, target);
                if (distToTarget <= returnStopWithin) rb.linearVelocity = Vector2.zero;
                else MoveToward(target, returnSpeed, returnAccel);
            }
            else
            {
                Vector2 center = player ? (Vector2)player.position : rb.position;
                Vector2 target = center + _idleOffset;
                MoveToward(target, Mathf.Min(maxSpeed * 0.6f, 4f), maxAccel);
            }
        }
        else
        {
            Vector2 toPlayer = ((Vector2)player.position - rb.position);
            Vector2 tangent = new Vector2(-toPlayer.y, toPlayer.x).normalized;
            Vector2 target = (Vector2)player.position + tangent * 0.6f;
            MoveToward(target, Mathf.Max(maxSpeed * 0.65f, 3.5f), maxAccel);
        }

        ApplyPlayerSeparation();
    }

    public void SetInterest(Transform t)
    {
        // Block hydrants when bladder is empty
        if (t && IsHydrant(t) && !NeedsToPee()) return;
        CurrentInterest = t;
        if (t) _state = DogState.Interest;
    }

    public string GetCurrentFixationName() => _fixationName;

    public void BeginPee(Transform source)
    {
        if (!NeedsToPee()) return;

        _peeSource = source;
        _state = DogState.Peeing;
        _isPeeing = true;

        if (spriteRenderer && poopPoseSprite) spriteRenderer.sprite = poopPoseSprite;

        if (peePrefab && _peeCo == null)
            _peeCo = StartCoroutine(PeeSpawnRoutine());

        Debug.Log($"Dog: BEGIN PEE at {source?.name ?? "?"} (bladder {bladder:0.0}/{bladderMax:0})");
    }

    public void StopPee()
    {
        if (!_isPeeing) return;
        _isPeeing = false;
        _peeSource = null;

        if (_peeCo != null) { StopCoroutine(_peeCo); _peeCo = null; }

        if (spriteRenderer && _defaultSprite) spriteRenderer.sprite = _defaultSprite;

        // IMPORTANT: after peeing, drop hydrant interest so we leave
        if (CurrentInterest && IsHydrant(CurrentInterest)) CurrentInterest = null;

        _state = DogState.Free;
        Debug.Log("Dog: STOP PEE");
    }

    System.Collections.IEnumerator PeeSpawnRoutine()
    {
        while (_isPeeing)
        {
            SpawnPeeBurst();
            yield return new WaitForSeconds(Mathf.Max(0.05f, peeBurstEvery));
        }
        _peeCo = null;
    }

    void SpawnPeeBurst()
    {
        if (!peePrefab) return;

        const float peeZ = 0f;
        for (int i = 0; i < Mathf.Max(1, peeDropsPerBurst); i++)
        {
            float r = Random.Range(peeSpreadRadius.x, peeSpreadRadius.y);
            float a = Random.Range(0f, Mathf.PI * 2f);
            Vector2 offset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;

            Vector3 pos = new Vector3(transform.position.x + offset.x,
                                      transform.position.y + offset.y,
                                      peeZ);

            float rotZ = Random.Range(peeRotationJitter.x, peeRotationJitter.y);
            float scale = Random.Range(peeScaleRange.x, peeScaleRange.y);

            var go = Instantiate(peePrefab, pos, Quaternion.Euler(0, 0, rotZ));
            go.transform.localScale *= scale;
        }
    }

    public float AddGoodDogEnergy(float delta)
    {
        goodBad01 = Mathf.Clamp01(goodBad01 + delta);
        Debug.Log($"Dog mood: {goodBad01:0.00}");
        return goodBad01;
    }

    bool IsPlayerMoving()
    {
        if (!playerRb) return false;
        return playerRb.linearVelocity.sqrMagnitude > playerMovingSpeed * playerMovingSpeed;
    }

    void PickIdleOffset()
    {
        float minR = Mathf.Max(returnDistanceRange.x, avoidNearPlayerRadius + 0.05f);
        float r = Random.Range(minR, returnDistanceRange.y);
        float a = Random.Range(0f, Mathf.PI * 2f);
        _idleOffset = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
    }

    void MoveToward(Vector2 worldTarget, float speed, float accelOverride)
    {
        Vector2 to = worldTarget - rb.position;
        float dist = to.magnitude;
        if (dist < 1e-3f) return;

        Vector2 dir = to / Mathf.Max(1e-3f, dist);
        Vector2 desired = dir * speed;

        float maxDv = accelOverride * Time.fixedDeltaTime;
        Vector2 dv = desired - rb.linearVelocity;
        if (dv.magnitude > maxDv) dv = dv.normalized * maxDv;

        rb.linearVelocity += dv;
        if (rb.linearVelocity.magnitude > speed) rb.linearVelocity = rb.linearVelocity.normalized * speed;
    }

    void ApplyPlayerSeparation()
    {
        if (!player) return;
        Vector2 fromPlayer = rb.position - (Vector2)player.position;
        float d = fromPlayer.magnitude;
        if (d <= 1e-4f) return;

        if (d < avoidNearPlayerRadius)
        {
            float t = 1f - (d / Mathf.Max(0.0001f, avoidNearPlayerRadius));
            Vector2 pushDir = fromPlayer / d;

            rb.AddForce(pushDir * (avoidNearPlayerStrength * t), ForceMode2D.Force);

            float inward = Vector2.Dot(rb.linearVelocity, -pushDir);
            if (inward > 0f)
            {
                float reduce = Mathf.Min(inward, avoidNearPlayerDamp * t);
                rb.linearVelocity += pushDir * reduce;
            }
        }
    }

    System.Collections.IEnumerator DoPoop()
    {
        if (_state == DogState.Peeing) yield break;

        _state = DogState.Pooping;
        var old = spriteRenderer ? spriteRenderer.sprite : null;
        if (spriteRenderer && poopPoseSprite) spriteRenderer.sprite = poopPoseSprite;
        rb.linearVelocity = Vector2.zero;

        Debug.Log("Dog: Pooping...");
        yield return new WaitForSeconds(poopPauseSeconds);

        if (poopPrefab) Instantiate(poopPrefab, transform.position, Quaternion.identity);
        Debug.Log("Dog: Poop dropped.");

        if (spriteRenderer) spriteRenderer.sprite = _defaultSprite ?? old;
        _state = CurrentInterest ? DogState.Interest : DogState.Free;
    }

    void HandleFlipWiggle()
    {
        if (!flipWhileMoving || !spriteRenderer) return;

        float speed = rb.linearVelocity.magnitude;
        if (speed < 0.1f) return;

        float hz = Mathf.Lerp(flipHzMin, flipHzMax, Mathf.Clamp01(speed / Mathf.Max(0.01f, flipSpeedRef)));
        float interval = 1f / Mathf.Max(0.01f, hz);
        _flipTimer += Time.deltaTime;

        if (_flipTimer >= interval)
        {
            if (flipAxis == FlipAxis.X) spriteRenderer.flipX = !spriteRenderer.flipX;
            else spriteRenderer.flipY = !spriteRenderer.flipY;
            _flipTimer = 0f;
        }
    }

    string DeriveInterestName(Transform t)
    {
        if (!t) return "None";
        if (t.GetComponent<CatAI2D>() || t.GetComponentInParent<CatAI2D>()) return "Cat";
        if (t.GetComponent<TreatItem>() || t.GetComponentInParent<TreatItem>()) return "Treat";
        if (t.GetComponent<Hydrant>() || t.GetComponentInParent<Hydrant>()) return "Hydrant";
        return string.IsNullOrEmpty(t.gameObject.name) ? "Interest" : t.gameObject.name;
    }

    bool IsHydrant(Transform t)
    {
        return t && (t.GetComponent<Hydrant>() || t.GetComponentInParent<Hydrant>());
    }

    bool NeedsToPee() => bladder > 0f && !_isPeeing;
}
