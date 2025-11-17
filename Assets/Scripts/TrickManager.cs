using System;
using UnityEngine;

[DisallowMultipleComponent]
public class TrickManager : MonoBehaviour
{
    [Header("References")]
    public BoardVisual boardVisual;
    public PlayerOllieAnimator2D riderVisual;

    [Header("Airtime")]
    public float minAirtimeToTrick = 0.15f;
    public bool onlyTrickWhileAirborne = true;

    [Header("Flip Input")]
    public KeyCode flipKey = KeyCode.J;
    public bool useArrowsAlso = true;

    [Header("Grab Input")]
    public KeyCode grabKey = KeyCode.K;

    [Header("Flip Tricks (4)")]
    public float[] flipDurations = new float[4] { 0.35f, 0.4f, 0.45f, 0.5f };
    public int[] flipSpins = new int[4] { 1, 2, 2, 3 };
    public int[] flipPoints = new int[4] { 200, 300, 350, 500 };
    public string[] flipNames = new string[4] { "Kickflip", "Heelflip", "Varial", "360 Flip" };

    [Header("Grab Tricks (4)")]
    public int[] grabPointsPerSecond = new int[4] { 120, 140, 160, 200 };
    public string[] grabNames = new string[4] { "Melon", "Indy", "Nosegrab", "Tailgrab" };

    [Header("Safety / Landing")]
    public bool bailOnUnfinishedFlip = true;       // used only when *we* decide landing internally
    public bool bailOnGrabHeldAtLanding = true;    // used only when *we* decide landing internally

    // runtime
    bool _airborne;
    float _airRemaining;
    float _airTotal;

    bool _flipActive;
    int _flipIndex;
    float _flipTime;
    float _flipDuration;

    bool _grabActive;
    int _grabIndex;
    float _grabHeldTime;

    int _points;

    void Update()
    {
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
            _airRemaining -= Time.fixedDeltaTime;
            if (_airRemaining <= 0f)
            {
                // If airtime expires without the controller telling us, treat as a normal land.
                NotifyLanded();
            }
        }

        if (_flipActive) _flipTime += Time.fixedDeltaTime;
        if (_grabActive) _grabHeldTime += Time.fixedDeltaTime;
    }

    // ===== Airtime control =====
    public void TriggerAirtime(float airSeconds)
    {
        airSeconds = Mathf.Max(airSeconds, 0.01f);
        _airborne = true;
        _airTotal = airSeconds;
        _airRemaining = airSeconds;

        _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
        _grabActive = false; _grabHeldTime = 0f;

        if (boardVisual) boardVisual.PlayOllie(airSeconds);
        if (riderVisual) riderVisual.PlayOllieHop(airSeconds);

        Debug.Log($"AIR START ({airSeconds:0.00}s)");
    }

    public void ExtendAirtime(float extraSeconds)
    {
        if (!_airborne) return;
        float add = Mathf.Max(0f, extraSeconds);
        _airRemaining += add;
        _airTotal += add;
        Debug.Log($"AIR EXTEND (+{add:0.00}s) → remaining {_airRemaining:0.00}s");
    }

    /// <summary>
    /// Internal convenience for when the manager itself decides landing (e.g., airtime exhausted).
    /// Uses the manager’s own bail rules.
    /// </summary>
    public void NotifyLanded()
    {
        if (!_airborne) return;
        bool bailed = false;

        // Evaluate with *manager* rules
        if (_flipActive && (_flipTime + 0.0001f < _flipDuration) && bailOnUnfinishedFlip)
            bailed = true;
        if (_grabActive && bailOnGrabHeldAtLanding)
            bailed = true;

        FinishAirAndTricks(landed: !bailed, forceNoScore: bailed, fromController: false);
    }

    // ===== New external hook the controller will call =====
    /// <summary>
    /// Controller-owned landing decision. If landed==true → clean land (no manager-enforced bails).
    /// If landed==false → bail (force). We still handle awarding flip/grab points appropriately.
    /// </summary>
    public void OnAirEnd(bool landed)
    {
        if (!_airborne) return;
        // When the controller decides the outcome, we DO NOT apply manager bail rules.
        FinishAirAndTricks(landed: landed, forceNoScore: !landed, fromController: true);
    }

    /// <summary>
    /// Legacy overload kept for compatibility. Defaults to a clean land.
    /// </summary>
    public void OnAirEnd() => OnAirEnd(true);

    // ===== External hooks you already have =====
    public void OnAirStart(float airSeconds) => TriggerAirtime(airSeconds);

    /// <summary>Force a bail from outside (e.g., leash yank). Cancels tricks and ends airtime.</summary>
    public void OnBail(string reason = null)
    {
        if (!_airborne)
        {
            // Even if not airborne, clear visuals/tricks to be safe.
            _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
            _grabActive = false; _grabHeldTime = 0f;
            if (boardVisual) boardVisual.ResetGrabs();
            Debug.Log(string.IsNullOrEmpty(reason) ? "BAIL" : $"BAIL: {reason}");
            return;
        }
        FinishAirAndTricks(landed: false, forceNoScore: true, fromController: true);
        Debug.Log(string.IsNullOrEmpty(reason) ? "BAIL" : $"BAIL: {reason}");
    }

    // ===== Flip logic =====
    void HandleFlipInput()
    {
        if (!Input.GetKeyDown(flipKey)) return;
        if (onlyTrickWhileAirborne && !_airborne) return;

        int idx = ReadDirectionIndex(); // 0=Up,1=Right,2=Down,3=Left
        _flipIndex = idx;
        _flipDuration = SafeGet(flipDurations, idx, 0.4f);

        if (_airRemaining < Mathf.Max(minAirtimeToTrick, _flipDuration * 0.85f))
        {
            Debug.Log($"Not enough air for {SafeGet(flipNames, idx, "Flip")}.");
            return;
        }

        _flipActive = true;
        _flipTime = 0f;

        int spins = SafeGet(flipSpins, idx, 1);
        if (boardVisual) boardVisual.PlayFlip(_flipDuration, spins, _flipIndex);

        Debug.Log($"FLIP START: {SafeGet(flipNames, idx, "Flip")}  ({_flipDuration:0.00}s, {spins} spins)");
    }

    // ===== Grab logic =====
    void HandleGrabInput()
    {
        if (Input.GetKeyDown(grabKey))
        {
            if (!onlyTrickWhileAirborne || _airborne)
            {
                if (!_grabActive)
                {
                    _grabIndex = ReadDirectionIndex();
                    _grabActive = true;
                    _grabHeldTime = 0f;

                    if (boardVisual) boardVisual.BeginGrab(_grabIndex);
                    Debug.Log($"GRAB START: {SafeGet(grabNames, _grabIndex, "Grab")}");
                }
            }
        }

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

        if (boardVisual) boardVisual.EndGrab();

        if (!scored) Debug.Log("GRAB END (no score).");
    }

    // ===== Outcome finisher (shared) =====
    /// <param name="landed">Final outcome to present externally.</param>
    /// <param name="forceNoScore">If true, cancel all scoring (used on bail).</param>
    /// <param name="fromController">
    /// If true, the controller decided outcome; ignore manager bail rules.
    /// If false, we applied our internal rules already.
    /// </param>
    void FinishAirAndTricks(bool landed, bool forceNoScore, bool fromController)
    {
        // Flip outcome
        if (_flipActive)
        {
            if (!forceNoScore && landed && (_flipTime + 0.0001f >= _flipDuration))
            {
                int pts = SafeGet(flipPoints, _flipIndex, 200);
                _points += pts;
                Debug.Log($"+{pts}  FLIP LANDED: {SafeGet(flipNames, _flipIndex, "Flip")}   (Total: {_points})");
            }
            else if (!landed && !fromController)
            {
                // Only log manager-driven unfinished bail detail; controller-owned bail already knows why.
                Debug.Log($"BAIL! Unfinished flip: {SafeGet(flipNames, _flipIndex, "Flip")}  time={_flipTime:0.00}/{_flipDuration:0.00}");
            }
            else if (landed && (_flipTime + 0.0001f < _flipDuration))
            {
                // Landed clean per controller, but flip unfinished → no score, no bail.
                Debug.Log($"NO SCORE (unfinished flip): {SafeGet(flipNames, _flipIndex, "Flip")}");
            }
        }

        // Grab outcome
        if (_grabActive)
        {
            if (!forceNoScore && landed)
            {
                // If the player released before landing, ReleaseGrab would have scored already.
                // If still holding at land and controller says 'landed', we end grab with no extra penalty.
                Debug.Log($"GRAB END at landing: {SafeGet(grabNames, _grabIndex, "Grab")} (no extra score)");
            }
        }

        // Reset trick states
        _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
        _grabActive = false; _grabHeldTime = 0f;

        // End airtime
        _airborne = false;
        _airRemaining = 0f;

        // Clear sustained visuals
        if (boardVisual) boardVisual.ResetGrabs();

        Debug.Log(landed ? "CLEAN LAND." : "BAIL.");
    }

    // ===== Helpers =====
    int ReadDirectionIndex()
    {
        bool up = Input.GetKey(KeyCode.W) || (useArrowsAlso && Input.GetKey(KeyCode.UpArrow));
        bool right = Input.GetKey(KeyCode.D) || (useArrowsAlso && Input.GetKey(KeyCode.RightArrow));
        bool down = Input.GetKey(KeyCode.S) || (useArrowsAlso && Input.GetKey(KeyCode.DownArrow));
        bool left = Input.GetKey(KeyCode.A) || (useArrowsAlso && Input.GetKey(KeyCode.LeftArrow));

        if (up && !right && !left) return 0;
        if (right && !up && !down) return 1;
        if (down && !right && !left) return 2;
        if (left && !up && !down) return 3;

        if (up && right) return 1;
        if (right && down) return 2;
        if (down && left) return 3;
        if (left && up) return 0;

        return 0;
    }

    static T SafeGet<T>(T[] arr, int idx, T fallback)
    {
        if (arr == null || idx < 0 || idx >= arr.Length) return fallback;
        return arr[idx];
    }

    // ===== Grind passthroughs (compat) =====
    public void StartGrind() { if (boardVisual) boardVisual.StartGrind(); }
    public void StopGrind() { if (boardVisual) boardVisual.StopGrind(); }
}
