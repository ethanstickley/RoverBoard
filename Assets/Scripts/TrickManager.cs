using System;
using UnityEngine;

/// <summary>
/// TrickManager
/// - Owns AIRTIME windows (from ollies or ramps) and all in-air trick logic.
/// - Supports 4 FLIP tricks (button + direction; each with a fixed duration/spins).
/// - Supports 4 GRAB (HOLD) tricks (hold button; release before landing to land clean).
/// - Integrates visuals via BoardVisual (spin/tilt) + Rider hop (optional).
/// - Emits Debug.Log points/results; hook to your scoring/health later.
/// 
/// HOW TO USE:
/// 1) Call TriggerAirtime(seconds) when an ollie begins, or from a ramp.
/// 2) Call NotifyLanded() the moment your player hits ground.
/// 3) (Optional) For ramps that "boost" air, call ExtendAirtime(extraSeconds).
/// 
/// INPUTS (default; can be changed in Inspector):
///   Flip: KeyCode.J  (+ direction from WASD/Arrow keys)
///   Grab: KeyCode.K  (hold; release to land the grab)
/// Directions map to 4 flip tricks: Up, Right, Down, Left.
/// Grabs cycle by direction as well, or use default index 0 if no direction pressed.
/// </summary>
[DisallowMultipleComponent]
public class TrickManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Board visual driver for spins/tilt. Optional but recommended.")]
    public BoardVisual boardVisual;
    [Tooltip("Optional: player hop visual. If null, hop visuals are skipped.")]
    public PlayerOllieAnimator2D riderVisual;

    [Header("Airtime")]
    [Tooltip("Minimum air time to allow tricks to start.")]
    public float minAirtimeToTrick = 0.15f;
    [Tooltip("If true, ignore inputs unless airborne.")]
    public bool onlyTrickWhileAirborne = true;

    [Header("Flip Input")]
    public KeyCode flipKey = KeyCode.J;
    [Tooltip("Directions read from WASD or Arrow keys.")]
    public bool useArrowsAlso = true;

    [Header("Grab Input")]
    public KeyCode grabKey = KeyCode.K;

    [Header("Flip Tricks (4)")]
    [Tooltip("Duration (s) each flip must run to count.")]
    public float[] flipDurations = new float[4] { 0.35f, 0.4f, 0.45f, 0.5f };
    [Tooltip("Spin count for each flip (visual board spins).")]
    public int[] flipSpins = new int[4] { 1, 2, 2, 3 };
    [Tooltip("Point value for each flip that lands clean.")]
    public int[] flipPoints = new int[4] { 200, 300, 350, 500 };
    [Tooltip("Human-readable names.")]
    public string[] flipNames = new string[4] { "Kickflip", "Heelflip", "Varial", "360 Flip" };

    [Header("Grab Tricks (4)")]
    [Tooltip("Points per full second of hold (scaled by air left).")]
    public int[] grabPointsPerSecond = new int[4] { 120, 140, 160, 200 };
    [Tooltip("Tilt degrees per grab index (visual flavor).")]
    public float[] grabTiltDegrees = new float[4] { 10f, 14f, 18f, 22f };
    [Tooltip("Human-readable names.")]
    public string[] grabNames = new string[4] { "Melon", "Indy", "Nosegrab", "Tailgrab" };

    [Header("Safety / Landing")]
    [Tooltip("If any flip is unfinished at landing → fall.")]
    public bool bailOnUnfinishedFlip = true;
    [Tooltip("If a grab is still held at landing → fall.")]
    public bool bailOnGrabHeldAtLanding = true;

    // --- runtime state -----------------------------------------------------

    bool _airborne;
    float _airRemaining;
    float _airTotal;
    float _time;

    // current flip
    bool _flipActive;
    int _flipIndex;
    float _flipTime;
    float _flipDuration;

    // current grab
    bool _grabActive;
    int _grabIndex;
    float _grabHeldTime;

    // points (demo)
    int _points;

    void Update()
    {
        _time += Time.deltaTime;

        // Optional: read inputs here. If you drive inputs elsewhere, call the public methods instead.
        if (!onlyTrickWhileAirborne || _airborne)
        {
            HandleFlipInput();
            HandleGrabInput();
        }
    }

    void FixedUpdate()
    {
        if (_airborne)
        {
            float dt = Time.fixedDeltaTime;
            _airRemaining -= dt;
            if (_airRemaining <= 0f)
            {
                // air window ended mid-flight → simulate landing now
                NotifyLanded();
            }
        }

        // Tick active flip timer
        if (_flipActive)
        {
            _flipTime += Time.fixedDeltaTime;
            // (Visual spin handled by BoardVisual coroutine that we kicked off when flip started)
        }

        // Tick active grab time
        if (_grabActive)
        {
            _grabHeldTime += Time.fixedDeltaTime;
            // (Visual tilt is held as long as grabActive true; BoardVisual handles sustained tilt)
        }
    }

    // ---------------- Airtime control API ----------------

    /// <summary> Begin a new airtime window (ollie, ramp). </summary>
    public void TriggerAirtime(float airSeconds)
    {
        airSeconds = Mathf.Max(airSeconds, 0.01f);
        _airborne = true;
        _airTotal = airSeconds;
        _airRemaining = airSeconds;

        // reset trick states
        _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
        _grabActive = false; _grabHeldTime = 0f;

        // visuals
        if (boardVisual) boardVisual.PlayOllie(airSeconds);
        if (riderVisual) riderVisual.PlayOllieHop(airSeconds);

        Debug.Log($"AIR START ({airSeconds:0.00}s)");
    }

    /// <summary> Add time to the current airtime window. </summary>
    public void ExtendAirtime(float extraSeconds)
    {
        if (!_airborne) return;
        _airRemaining += Mathf.Max(0f, extraSeconds);
        _airTotal += Mathf.Max(0f, extraSeconds);
        Debug.Log($"AIR EXTEND (+{extraSeconds:0.00}s) → remaining {_airRemaining:0.00}s");
    }

    /// <summary> Should be called by your PlayerController when feet hit ground. </summary>
    public void NotifyLanded()
    {
        if (!_airborne)
            return;

        _airborne = false;

        // Evaluate trick outcomes at landing time
        bool bailed = false;

        // Flip outcome
        if (_flipActive)
        {
            if (_flipTime + 0.0001f >= _flipDuration)
            {
                // success
                int pts = SafeGet(flipPoints, _flipIndex, 200);
                _points += pts;
                Debug.Log($"+{pts}  FLIP LANDED: {SafeGet(flipNames, _flipIndex, "Flip")}   (Total: {_points})");
            }
            else
            {
                if (bailOnUnfinishedFlip)
                {
                    bailed = true;
                    Debug.Log($"BAIL! Unfinished flip: {SafeGet(flipNames, _flipIndex, "Flip")}  time={_flipTime:0.00}/{_flipDuration:0.00}");
                }
                else
                {
                    Debug.Log($"NO SCORE (unfinished flip): {SafeGet(flipNames, _flipIndex, "Flip")}");
                }
            }
        }

        // Grab outcome
        if (_grabActive)
        {
            if (bailOnGrabHeldAtLanding)
            {
                bailed = true;
                Debug.Log($"BAIL! Still holding grab at landing: {SafeGet(grabNames, _grabIndex, "Grab")}");
            }
            else
            {
                // If you prefer "auto release on landing for some score", uncomment below:
                // ReleaseGrab(true);
            }
        }

        // Reset active states
        _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
        _grabActive = false; _grabHeldTime = 0f;

        if (boardVisual) boardVisual.ResetGrabs();

        if (bailed)
        {
            // TODO: hook your health/fall/bail here
            Debug.Log("BAIL RESOLVED → apply health penalty / knockdown here.");
        }
        else
        {
            Debug.Log("CLEAN LAND.");
        }
    }

    // ---------------- Flip logic ----------------

    void HandleFlipInput()
    {
        if (!Input.GetKeyDown(flipKey)) return;
        if (onlyTrickWhileAirborne && !_airborne) return;

        // pick index via direction
        int idx = ReadDirectionIndex(); // 0=Up, 1=Right, 2=Down, 3=Left
        _flipIndex = idx;

        _flipDuration = SafeGet(flipDurations, idx, 0.4f);

        // only allow if enough time remains to finish
        if (_airRemaining < Mathf.Max(minAirtimeToTrick, _flipDuration * 0.85f))
        {
            Debug.Log($"Not enough air for {SafeGet(flipNames, idx, "Flip")}.");
            return;
        }

        // start the flip
        _flipActive = true;
        _flipTime = 0f;

        int spins = SafeGet(flipSpins, idx, 1);
        if (boardVisual) boardVisual.PlayFlip(_flipDuration, spins);

        Debug.Log($"FLIP START: {SafeGet(flipNames, idx, "Flip")}  ({_flipDuration:0.00}s, {spins} spins)");
    }

    // ---------------- Grab logic ----------------

    void HandleGrabInput()
    {
        // Press: start grab
        if (Input.GetKeyDown(grabKey))
        {
            if (!onlyTrickWhileAirborne || _airborne)
            {
                if (!_grabActive)
                {
                    _grabIndex = ReadDirectionIndex(); // direction chooses grab flavor
                    _grabActive = true;
                    _grabHeldTime = 0f;

                    float tilt = SafeGet(grabTiltDegrees, _grabIndex, 14f);
                    if (boardVisual) boardVisual.BeginGrabTilt(tilt);

                    Debug.Log($"GRAB START: {SafeGet(grabNames, _grabIndex, "Grab")}");
                }
            }
        }

        // Release: end grab and award points if airborne (or at landing handled in NotifyLanded)
        if (Input.GetKeyUp(grabKey))
        {
            ReleaseGrab(true);
        }
    }

    void ReleaseGrab(bool awardIfAirborne)
    {
        if (!_grabActive) return;

        bool scored = false;
        if (_airborne && awardIfAirborne)
        {
            int pps = SafeGet(grabPointsPerSecond, _grabIndex, 120);
            int add = Mathf.RoundToInt(pps * _grabHeldTime);
            _points += add;
            Debug.Log($"+{add}  GRAB LANDED: {SafeGet(grabNames, _grabIndex, "Grab")}  ({_grabHeldTime:0.00}s)   (Total: {_points})");
            scored = true;
        }

        _grabActive = false;
        _grabHeldTime = 0f;

        if (boardVisual) boardVisual.EndGrabTilt();

        if (!scored) Debug.Log($"GRAB END (no score).");
    }

    // ---------------- Helpers ----------------

    int ReadDirectionIndex()
    {
        // 0=Up, 1=Right, 2=Down, 3=Left
        bool up = Input.GetKey(KeyCode.W) || (useArrowsAlso && Input.GetKey(KeyCode.UpArrow));
        bool right = Input.GetKey(KeyCode.D) || (useArrowsAlso && Input.GetKey(KeyCode.RightArrow));
        bool down = Input.GetKey(KeyCode.S) || (useArrowsAlso && Input.GetKey(KeyCode.DownArrow));
        bool left = Input.GetKey(KeyCode.A) || (useArrowsAlso && Input.GetKey(KeyCode.LeftArrow));

        if (up && !right && !left) return 0;
        if (right && !up && !down) return 1;
        if (down && !right && !left) return 2;
        if (left && !up && !down) return 3;

        // tie-breakers / diagonals: prioritize clockwise Up->Right->Down->Left
        if (up && right) return 1;
        if (right && down) return 2;
        if (down && left) return 3;
        if (left && up) return 0;

        // no direction: default to Up (index 0)
        return 0;
    }

    static T SafeGet<T>(T[] arr, int idx, T fallback)
    {
        if (arr == null || idx < 0 || idx >= arr.Length) return fallback;
        return arr[idx];
    }

    // ---------------- Back-compat shim for rails ----------------

    // Some of your earlier code called these on TrickManager; keep them as no-ops to avoid errors.
    public void StartGrind() { /* hook your grind entry here later */ }
    public void StopGrind() { /* hook your grind exit  here later */ }

    // ---------------- External hooks requested by PlayerManager ----------------

    /// <summary>
    /// External hook: start airtime from PlayerManager (alias for TriggerAirtime).
    /// </summary>
    public void OnAirStart(float airSeconds)
    {
        TriggerAirtime(airSeconds);
    }

    /// <summary>
    /// External hook: end airtime due to a normal landing (alias for NotifyLanded).
    /// </summary>
    public void OnAirEnd(bool landed)
    {
        if (landed)
        {
            NotifyLanded();
        }
        else
        {
            Debug.Log("landed = false");
        }
    }

    /// <summary>
    /// External hook: force a bail. Cancels active tricks, clears visuals, and ends airtime.
    /// </summary>
    public void OnBail(string reason = null)
    {
        // If we were mid-air, end the airtime; treat as bail regardless of unfinished trick settings.
        bool wasAirborne = _airborne;

        // Cancel active tricks immediately (no scoring on bail)
        _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
        _grabActive = false; _grabHeldTime = 0f;

        // Clear visuals (e.g., sustained tilt)
        if (boardVisual) boardVisual.ResetGrabs();

        if (wasAirborne)
        {
            _airborne = false;
            _airRemaining = 0f;
        }

        if (string.IsNullOrEmpty(reason))
            Debug.Log("BAIL triggered by PlayerManager.");
        else
            Debug.Log($"BAIL triggered by PlayerManager: {reason}");
    }
}
