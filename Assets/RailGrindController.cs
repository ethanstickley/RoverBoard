// RailGrindController.cs
// Attach to the Player. Lets you grind along RailPath2D curves by ollie + pressing the grind key near a rail.
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody2D))]
public class RailGrindController : MonoBehaviour
{
    [Header("References")]
    public PlayerController2D player;      // required (to read ollieKey and call StartAir)
    public TrickManager trickManager;      // optional but recommended for StartGrind/StopGrind visuals
    public Rigidbody2D rb;                 // required

    [Header("Input")]
    [Tooltip("Key to start grinding while airborne and near a rail.")]
    public KeyCode grindKey = KeyCode.L;

    [Header("Detection")]
    [Tooltip("How far from a rail you can be to snap onto it when you press grind (world units).")]
    public float snapDistance = 0.6f;
    [Tooltip("We only allow grind if approach velocity along the rail tangent exceeds this.")]
    public float minApproachSpeed = 1.0f;

    [Header("Motion")]
    [Tooltip("Desired grind speed along the rail in units/second.")]
    public float targetGrindSpeed = 7.5f;
    [Tooltip("How quickly we accelerate to target grind speed.")]
    public float grindAcceleration = 25f;
    [Tooltip("Friction during grind when no input; lowers speed towards 0.")]
    public float grindFriction = 0.0f;

    [Header("Exit / Jump Off")]
    [Tooltip("Small hop airtime when you ollie off a rail.")]
    public float exitOllieAir = 0.35f;
    [Tooltip("Velocity imparted along tangent when exiting a rail.")]
    public float exitBoost = 6f;

    // State
    bool _grinding;
    RailPath2D _rail;
    float _t;           // normalized [0..1] along rail
    float _speed;       // scalar speed along rail (signed via _dir)
    int _dir = 1;       // +1 forward, -1 backward

    void Reset()
    {
        rb = GetComponent<Rigidbody2D>();
        player = GetComponent<PlayerController2D>();
        trickManager = GetComponent<TrickManager>();
    }

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>();
        if (!player) player = GetComponent<PlayerController2D>();
        if (!trickManager) trickManager = GetComponent<TrickManager>();
    }

    void Update()
    {
        if (!_grinding)
        {
            TryStartGrind();
        }
        else
        {
            // Jump/ollie to exit the rail
            if (Input.GetKeyDown(player.ollieKey))
            {
                ExitGrind(jumpOff: true);
            }
        }
    }

    void FixedUpdate()
    {
        if (!_grinding) return;

        // accelerate speed towards target
        float desired = targetGrindSpeed * Mathf.Sign(_dir);
        float dv = desired - _speed;
        float maxStep = grindAcceleration * Time.fixedDeltaTime;
        if (Mathf.Abs(dv) > maxStep) dv = Mathf.Sign(dv) * maxStep;
        _speed += dv;

        // friction
        if (Mathf.Abs(_speed) > 0f && grindFriction > 0f)
        {
            float f = grindFriction * Time.fixedDeltaTime * Mathf.Sign(_speed);
            if (Mathf.Abs(f) > Mathf.Abs(_speed)) _speed = 0f;
            else _speed -= f;
        }

        if (_rail == null || _rail.TotalLength <= 1e-5f)
        {
            ExitGrind(jumpOff: false);
            return;
        }

        float dt = (_rail.TotalLength > 1e-5f) ? (_speed / _rail.TotalLength) * Time.fixedDeltaTime : 0f;
        _t += dt;

        // Clamp to ends (non-loop rails)
        if (!_rail.loop)
        {
            if (_t >= 1f)
            {
                _t = 1f;
                ExitGrind(jumpOff: true); // small hop off end
                return;
            }
            else if (_t <= 0f)
            {
                _t = 0f;
                ExitGrind(jumpOff: true);
                return;
            }
        }
        else
        {
            // wrap
            if (_t > 1f) _t -= 1f;
            if (_t < 0f) _t += 1f;
        }

        Vector2 p = _rail.GetPointAtT(_t);
        Vector2 tan = _rail.GetTangentAtT(_t);
        if (tan.sqrMagnitude < 1e-6f) tan = new Vector2(_dir, 0f);

        // while grinding we drive the transform (kinematic-like)
        rb.isKinematic = true;
        rb.MovePosition(p);

        // face along tangent (optional)
        float ang = Mathf.Atan2(tan.y, tan.x) * Mathf.Rad2Deg;
        rb.MoveRotation(ang);
    }

    void TryStartGrind()
    {
        // Must be in air, press grind, and have a candidate rail close enough.
        if (!player || !player.IsAirborne) return;
        if (!Input.GetKeyDown(grindKey)) return;

        RailPath2D best = null;
        float bestDist = float.MaxValue;
        float bestT = 0f;
        Vector2 bestPoint = Vector2.zero;

        Vector2 pos = rb.position;
        foreach (var r in RailPath2D.AllRails)
        {
            if (!r || r.SegmentCount <= 0) continue;
            Vector2 cp;
            float t = r.FindClosestT(pos, out cp);
            float d = Vector2.Distance(pos, cp);
            if (d < bestDist)
            {
                bestDist = d;
                best = r;
                bestT = t;
                bestPoint = cp;
            }
        }

        if (best == null || bestDist > snapDistance) return;

        // Require reasonable approach speed along tangent
        Vector2 tan = best.GetTangentAtT(bestT).normalized;
        float along = Vector2.Dot(rb.linearVelocity, tan);
        if (Mathf.Abs(along) < minApproachSpeed) return;

        // Begin grind
        _rail = best;
        _t = bestT;
        _dir = (along >= 0f) ? 1 : -1;
        _speed = Mathf.Max(minApproachSpeed, Mathf.Abs(along)) * Mathf.Sign(_dir);

        _grinding = true;
        rb.isKinematic = true; // we directly place the body during grind
        trickManager?.StartGrind();
        Debug.Log($"GRIND START on rail '{_rail.name}', t={_t:0.00}, dir={_dir}");
    }

    void ExitGrind(bool jumpOff)
    {
        if (!_grinding)
            return;

        // leave along tangent
        Vector2 tan = (_rail != null) ? _rail.GetTangentAtT(Mathf.Clamp01(_t)) : Vector2.right;
        tan.Normalize();
        rb.isKinematic = false;

        if (jumpOff && player != null)
        {
            // small ollie on exit
            player.StartAir(exitOllieAir);
            rb.linearVelocity = tan * exitBoost;
        }

        trickManager?.StopGrind();
        Debug.Log("GRIND END");

        _grinding = false;
        _rail = null;
        _t = 0f;
        _speed = 0f;
        _dir = 1;
    }
}
