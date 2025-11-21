// PlayerController2D.cs — adds a stronger _isBailed guard in UpdateBodyAnim()
// Ensures bailSprite is actively re-applied every frame while bailed.
// Keeps freeze-during-bail, loose board spawn, and event-driven integration with TrickManager.

using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController2D : MonoBehaviour
{
    [Header("Refs")]
    public Rigidbody2D rb;
    public Transform boardVisual;     // child object for the board sprite
    public SpriteRenderer playerSR;   // body sprite renderer (on the Player)
    public Camera cam;

    [Header("Movement")]
    public float moveSpeedOnBoard = 6.0f;
    public float moveSpeedOnFoot = 4.0f;
    public float accel = 30f;
    [Range(0f, 1f)] public float dragPerSec = 0.12f;
    public bool faceMoveDirection = true;

    [Header("State")]
    public bool IsOnBoard = true;

    [Header("Air / Ollie")]
    public KeyCode ollieKey = KeyCode.Space;
    public float baseAirTime = 1.1f;  // flat ollie air
    public float airTime;              // active air time remaining
    public bool IsAirborne { get; private set; }

    [Header("Toggle Board")]
    public KeyCode toggleBoardKey = KeyCode.E;

    [Header("Sprites (2×2 sheet logic)")]
    public Sprite[] onBoardFrames = new Sprite[2];
    public Sprite[] offBoardFrames = new Sprite[2];
    public Sprite bailSprite;
    public float animFrameTime = 0.15f;

    [Header("Bail -> Loose Board")]
    public GameObject looseBoardPrefab;    // prefab with Rigidbody2D + LooseBoard.cs
    public float looseBoardLifetime = 2.0f;

    [Header("Bail Settings")]
    [Tooltip("How long the player stays down (no input, no movement) after a bail.")]
    public float bailLockSeconds = 1.0f;

    // Exposed so DogAI can anticipate based on camera-relative input intent
    [HideInInspector] public Vector2 lastMoveInputWorld;

    TrickManager trickMgr;

    // runtime anim
    float _animTimer;
    int _animIndex;        // 0 or 1
    bool _isBailed;        // show bailSprite while true

    // freeze handling
    RigidbodyConstraints2D _preBailConstraints;
    bool _frozenByBail;

    void Reset() { rb = GetComponent<Rigidbody2D>(); }

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!cam) cam = Camera.main;
        if (!playerSR) playerSR = GetComponentInChildren<SpriteRenderer>();
        trickMgr = FindObjectOfType<TrickManager>();
        ApplyBoardVisual();
        ForceRefreshBodySprite();
    }

    void OnEnable()
    {
        if (!trickMgr) trickMgr = FindObjectOfType<TrickManager>();
        if (trickMgr != null)
        {
            trickMgr.Bail += OnTM_Bail;
            trickMgr.AirFinished += OnTM_AirFinished;
        }
    }

    void OnDisable()
    {
        if (trickMgr != null)
        {
            trickMgr.Bail -= OnTM_Bail;
            trickMgr.AirFinished -= OnTM_AirFinished;
        }
    }

    void Update()
    {
        // No toggles or ollies while bailed
        if (!_isBailed && Input.GetKeyDown(toggleBoardKey))
        {
            IsOnBoard = !IsOnBoard;
            ApplyBoardVisual();
            Debug.Log($"Player: {(IsOnBoard ? "On-board" : "On-foot")}");
        }

        if (!_isBailed && IsOnBoard && Input.GetKeyDown(ollieKey) && !IsAirborne)
        {
            StartAir(baseAirTime);
        }

        if (IsAirborne)
        {
            airTime -= Time.deltaTime;
            if (airTime <= 0f) EndAir(landed: true); // TrickManager decides bail/land outcome
        }

        UpdateBodyAnim(Time.deltaTime);
    }

    void FixedUpdate()
    {
        // Hard stop if bailed. Constraints are frozen, but keep this early-out too.
        if (_isBailed) return;

        rb.linearVelocity *= Mathf.Clamp01(1f - dragPerSec * Time.fixedDeltaTime);

        Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        input = Vector2.ClampMagnitude(input, 1f);

        // Camera-relative (WASD always screen-up/right)
        Vector2 moveDir = input;
        if (cam)
        {
            Vector2 right = new Vector2(cam.transform.right.x, cam.transform.right.y).normalized;
            Vector2 up = new Vector2(cam.transform.up.x, cam.transform.up.y).normalized;
            moveDir = (right * input.x + up * input.y);
        }
        moveDir = moveDir.sqrMagnitude > 1e-4f ? moveDir.normalized : Vector2.zero;

        // expose world-intent for DogAI
        lastMoveInputWorld = moveDir;

        float targetSpeed = IsOnBoard ? moveSpeedOnBoard : moveSpeedOnFoot;
        Vector2 desired = moveDir * targetSpeed;

        Vector2 dv = desired - rb.linearVelocity;
        float maxDv = accel * Time.fixedDeltaTime;
        if (dv.magnitude > maxDv) dv = dv.normalized * maxDv;
        rb.linearVelocity += dv;

        if (faceMoveDirection && rb.linearVelocity.sqrMagnitude > 1e-3f)
        {
            float ang = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
            rb.MoveRotation(ang);
        }
    }

    // ---- Air window helpers ----
    public void StartAir(float timeAdd)
    {
        IsAirborne = true;
        airTime = Mathf.Max(airTime, 0f) + Mathf.Max(0.05f, timeAdd);
        trickMgr?.TriggerAirtime(airTime);  // canonical call
        Debug.Log($"AIR: start (window={airTime:0.00}s)");
    }

    public void EndAir(bool landed)
    {
        if (!IsAirborne) return;
        trickMgr?.OnAirEnd(landed); // TM resolves, then events come back to us
        IsAirborne = false;
        airTime = 0f;
        Debug.Log("AIR: end");
    }

    // ---- Event handlers from TrickManager ----
    void OnTM_Bail(string reason)
    {
        DoBailVisualsAndLooseBoard(reason);
    }

    void OnTM_AirFinished(bool landed, string reason)
    {
        if (landed)
        {
            if (_isBailed) { _isBailed = false; }
            ForceRefreshBodySprite();
            ApplyBoardVisual();
        }
        else
        {
            if (!_isBailed) DoBailVisualsAndLooseBoard(string.IsNullOrEmpty(reason) ? "air_bail" : reason);
        }
    }

    // ---- Bail visuals (event-driven)
    void DoBailVisualsAndLooseBoard(string reason)
    {
        if (_isBailed) return;

        // Cache pre-bail velocity for the loose board
        Vector2 preBailVel = rb.linearVelocity;

        // Hide board & stop our motion
        IsOnBoard = false;
        ApplyBoardVisual();
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;

        // Freeze physics so the dog/leash can't tow us
        _preBailConstraints = rb.constraints;
        rb.constraints = RigidbodyConstraints2D.FreezeAll;
        _frozenByBail = true;

        // Lockout + sprite
        _isBailed = true;
        if (playerSR && bailSprite) playerSR.sprite = bailSprite;

        // Spawn loose board moving away with pre-bail velocity
        if (looseBoardPrefab)
        {
            var spawnPos = boardVisual ? boardVisual.position : transform.position;
            var go = Instantiate(looseBoardPrefab, spawnPos, Quaternion.identity);
            var lbrb = go.GetComponent<Rigidbody2D>();
            if (!lbrb) lbrb = go.AddComponent<Rigidbody2D>();
            lbrb.gravityScale = 0f;
            lbrb.linearVelocity = preBailVel;
            var lb = go.GetComponent<LooseBoard>();
            if (!lb) lb = go.AddComponent<LooseBoard>();
            lb.lifetime = looseBoardLifetime;
        }

        Debug.Log($"Player BAILED ({reason}) (LooseBoard spawned)");
        StartCoroutine(StandUpAfter(bailLockSeconds));
    }

    System.Collections.IEnumerator StandUpAfter(float seconds)
    {
        yield return new WaitForSeconds(seconds);

        // Unfreeze physics
        if (_frozenByBail)
        {
            rb.constraints = _preBailConstraints;
            _frozenByBail = false;
        }

        _isBailed = false;
        ForceRefreshBodySprite();
        ApplyBoardVisual();
    }

    void ApplyBoardVisual()
    {
        if (boardVisual) boardVisual.gameObject.SetActive(IsOnBoard && !_isBailed);
    }

    void UpdateBodyAnim(float dt)
    {
        if (!playerSR) return;

        // STRONG GUARD: while bailed, keep re-applying the bail sprite and exit immediately.
        if (_isBailed)
        {
            if (bailSprite && playerSR.sprite != bailSprite)
                playerSR.sprite = bailSprite;
            return;
        }

        bool moving = rb.linearVelocity.sqrMagnitude > 0.05f * 0.05f;
        if (!moving)
        {
            _animIndex = 0;
            playerSR.sprite = IsOnBoard ? SafeGet(onBoardFrames, 0) : SafeGet(offBoardFrames, 0);
            return;
        }

        _animTimer += dt;
        if (_animTimer >= animFrameTime)
        {
            _animTimer = 0f;
            _animIndex = 1 - _animIndex; // toggle 0 <-> 1
        }

        var frames = IsOnBoard ? onBoardFrames : offBoardFrames;
        playerSR.sprite = SafeGet(frames, _animIndex);
    }

    void ForceRefreshBodySprite()
    {
        _animTimer = 0f;
        _animIndex = 0;
        if (!playerSR) return;
        if (_isBailed && bailSprite) { playerSR.sprite = bailSprite; return; }
        playerSR.sprite = IsOnBoard ? SafeGet(onBoardFrames, 0) : SafeGet(offBoardFrames, 0);
    }

    static Sprite SafeGet(Sprite[] arr, int i)
    {
        if (arr == null || arr.Length == 0) return null;
        if (i < 0 || i >= arr.Length) i = 0;
        return arr[i];
    }
}
