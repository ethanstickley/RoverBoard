// TrickManager.cs — HUD-integrated version
using System;
using UnityEngine;

[DisallowMultipleComponent]
public class TrickManager : MonoBehaviour
{
    // ======= External events =======
    public event Action<bool, string> AirFinished;
    public event Action<string> Bail;

    // ======= References =======
    [Header("References")]
    public BoardVisual boardVisual;            // optional but recommended
    public PlayerOllieAnimator2D riderVisual;  // optional

    // ======= Airtime =======
    [Header("Airtime")]
    [Tooltip("If true, ignore trick inputs unless airborne.")]
    public bool onlyTrickWhileAirborne = true;

    // ======= Inputs =======
    [Header("Flip / Grab Inputs (no new keys)")]
    public KeyCode flipKey = KeyCode.J;
    public KeyCode grabKey = KeyCode.K;
    [Tooltip("Allow Arrow keys in addition to WASD for direction input.")]
    public bool useArrowsAlso = true;

    // ======= Tricks =======
    [Header("Flip Tricks (5 total; index 4 = no-direction flip)")]
    public float[] flipDurations = new float[5] { 0.35f, 0.40f, 0.45f, 0.50f, 0.45f };
    public int[] flipSpins = new int[5] { 1, 2, 2, 3, 2 };
    public int[] flipPoints = new int[5] { 200, 300, 350, 500, 400 };
    public string[] flipNames = new string[5] { "Kickflip", "Heelflip", "Varial", "360 Flip", "No-Dir Flip" };

    [Header("Grab Tricks (5 total; index 4 = no-direction grab)")]
    public int[] grabPointsPerSecond = new int[5] { 120, 140, 160, 200, 220 };
    public string[] grabNames = new string[5] { "Melon", "Indy", "Nosegrab", "Tailgrab", "No-Dir Grab" };

    [Header("Manager Bail Rules (used when manager decides landing)")]
    [Tooltip("If airtime ends with an unfinished flip, count as bail.")]
    public bool bailOnUnfinishedFlip = true;
    [Tooltip("If still holding a grab when airtime ends, count as bail.")]
    public bool bailOnGrabHeldAtLanding = true;

    // ======= Runtime state =======
    bool _airborne;
    float _airRemaining;
    float _airTotal;

    bool _flipActive;
    int _flipIndex;       // 0..4
    float _flipTime;
    float _flipDuration;

    bool _grabActive;
    int _grabIndex;       // 0..4
    float _grabHeldTime;

    int _points;

    // Optional global access for pickups that don't have a reference handy
    public static TrickManager Instance { get; private set; }

    void Awake()
    {
        if (Instance == null) Instance = this;
        // Initialize HUD with current counters
        HUDController.Instance?.SetPoints(_points);
        // Treats: let game code drive it; default shown as 0 by HUD
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

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
                NotifyLanded();
            }
        }

        if (_flipActive) _flipTime += Time.fixedDeltaTime;
        if (_grabActive) _grabHeldTime += Time.fixedDeltaTime;
    }

    // ======================
    // Airtime control
    // ======================
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

    public void NotifyLanded()
    {
        if (!_airborne) return;

        bool landed = true;
        string reason = null;

        if (_flipActive && (_flipTime + 0.0001f < _flipDuration) && bailOnUnfinishedFlip)
        {
            landed = false;
            reason = "unfinished_flip";
        }
        if (_grabActive && bailOnGrabHeldAtLanding)
        {
            landed = false;
            reason = string.IsNullOrEmpty(reason) ? "grab_held_at_landing" : (reason + "+grab_held");
        }

        ResolveAirOutcome(landed, reason, fromController: false);
    }

    public void OnAirEnd(bool landed)
    {
        if (!_airborne) return;
        ResolveAirOutcome(landed, landed ? null : "controller_bail", fromController: true);
    }

    public void OnBail(string reason = null)
    {
        if (_airborne)
        {
            ResolveAirOutcome(false, string.IsNullOrEmpty(reason) ? "external_bail" : reason, fromController: true);
            return;
        }

        ClearTrickStatesAndVisuals();
        string r = string.IsNullOrEmpty(reason) ? "external_bail_ground" : reason;
        Bail?.Invoke(r);
        AirFinished?.Invoke(false, r);
        Debug.Log($"BAIL (ground): {r}");
    }

    // ======================
    // Flip logic
    // ======================
    void HandleFlipInput()
    {
        if (!Input.GetKeyDown(flipKey)) return;
        if (onlyTrickWhileAirborne && !_airborne) return;

        int dirIdx = ReadDirectionIndexOrMinusOne();   // -1 if no direction held
        _flipIndex = (dirIdx < 0) ? 4 : Mathf.Clamp(dirIdx, 0, 3);

        _flipDuration = SafeGet(flipDurations, _flipIndex, 0.4f);

        _flipActive = true;
        _flipTime = 0f;

        int spins = SafeGet(flipSpins, _flipIndex, 1);
        if (boardVisual) boardVisual.PlayFlip(_flipDuration, spins, _flipIndex);

        
    }

    // ======================
    // Grab logic
    // ======================
    void HandleGrabInput()
    {
        if (Input.GetKeyDown(grabKey))
        {
            if (!onlyTrickWhileAirborne || _airborne)
            {
                if (!_grabActive)
                {
                    int dirIdx = ReadDirectionIndexOrMinusOne();  // -1 if no direction
                    _grabIndex = (dirIdx < 0) ? 4 : Mathf.Clamp(dirIdx, 0, 3);

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
            HUDController.Instance?.AddPoints(add); // <-- HUD update
            Debug.Log($"+{add}  GRAB LANDED: {SafeGet(grabNames, _grabIndex, "Grab")}  ({_grabHeldTime:0.00}s)   (Total: {_points})");
            scored = true;
        }

        _grabActive = false;
        _grabHeldTime = 0f;

        if (boardVisual) boardVisual.EndGrab();
        if (!scored) Debug.Log("GRAB END (no score).");
    }

    // ======================
    // Unified outcome resolver
    // ======================
    void ResolveAirOutcome(bool landed, string reason, bool fromController)
    {
        // Flip resolution
        if (_flipActive)
        {
            bool finished = (_flipTime + 0.0001f >= _flipDuration);
            if (landed && finished)
            {
                int pts = SafeGet(flipPoints, _flipIndex, 200);
                _points += pts;
                HUDController.Instance?.AddPoints(pts); // <-- HUD update
                Debug.Log($"+{pts}  FLIP LANDED: {SafeGet(flipNames, _flipIndex, "Flip")}   (Total: {_points})");
            }
            else if (!landed)
            {
                string why = string.IsNullOrEmpty(reason) ? "bail" : reason;
                if (!fromController && !finished) why = "unfinished_flip";
                Debug.Log($"BAIL! {why}");
            }
            else if (landed && !finished)
            {
                Debug.Log($"NO SCORE (unfinished flip): {SafeGet(flipNames, _flipIndex, "Flip")}");
            }
        }

        // Grab resolution at landing
        if (_grabActive)
        {
            if (!landed)
            {
                Debug.Log($"GRAB END (bail): {SafeGet(grabNames, _grabIndex, "Grab")}");
            }
            else
            {
                Debug.Log($"GRAB END at landing: {SafeGet(grabNames, _grabIndex, "Grab")} (no extra score)");
            }
        }

        // End airtime + clear trick states + visuals
        _airborne = false;
        _airRemaining = 0f;
        _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
        _grabActive = false; _grabHeldTime = 0f;
        if (boardVisual) boardVisual.ResetGrabs();

        // Fire external events so PlayerController can do bail sprite + LooseBoard, or reset on land
        if (!landed)
        {
            string r = string.IsNullOrEmpty(reason) ? "bail" : reason;
            Bail?.Invoke(r);
            AirFinished?.Invoke(false, r);
            Debug.Log("CLEANUP: BAIL.");
        }
        else
        {
            AirFinished?.Invoke(true, null);
            Debug.Log("CLEAN LAND.");
        }
    }

    void ClearTrickStatesAndVisuals()
    {
        _flipActive = false; _flipTime = 0f; _flipDuration = 0f;
        _grabActive = false; _grabHeldTime = 0f;
        if (boardVisual) boardVisual.ResetGrabs();
    }

    // ======================
    // Treats API (simple HUD hook)
    // ======================
    /// <summary>Add to the treats counter shown on the HUD (call from Treat pickup / DogAI).</summary>
    public void AddTreat(int delta = 1)
    {
        HUDController.Instance?.AddTreat(delta);
    }

    // ======================
    // Helpers
    // ======================
    int ReadDirectionIndexOrMinusOne()
    {
        bool up = Input.GetKey(KeyCode.W) || (useArrowsAlso && Input.GetKey(KeyCode.UpArrow));
        bool right = Input.GetKey(KeyCode.D) || (useArrowsAlso && Input.GetKey(KeyCode.RightArrow));
        bool down = Input.GetKey(KeyCode.S) || (useArrowsAlso && Input.GetKey(KeyCode.DownArrow));
        bool left = Input.GetKey(KeyCode.A) || (useArrowsAlso && Input.GetKey(KeyCode.LeftArrow));

        int dirCount = (up ? 1 : 0) + (right ? 1 : 0) + (down ? 1 : 0) + (left ? 1 : 0);
        if (dirCount == 0) return -1;

        if (up && !right && !left) return 0;
        if (right && !up && !down) return 1;
        if (down && !right && !left) return 2;
        if (left && !up && !down) return 3;

        if (up && right) return 1;
        if (right && down) return 2;
        if (down && left) return 3;
        if (left && up) return 0;

        return -1;
    }

    static T SafeGet<T>(T[] arr, int idx, T fallback)
    {
        if (arr == null || idx < 0 || idx >= arr.Length) return fallback;
        return arr[idx];
    }

    // Grind passthroughs (compat)
    public void StartGrind() { if (boardVisual) boardVisual.StartGrind(); }
    public void StopGrind() { if (boardVisual) boardVisual.StopGrind(); }
}
