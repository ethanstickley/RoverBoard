using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BoardVisual : MonoBehaviour
{
    public enum FlipStyle { RotateOnly, RotateAndSwapSprite }

    [Header("References")]
    [Tooltip("SpriteRenderer that shows the board.")]
    public SpriteRenderer boardSR;

    [Header("Base Sprites")]
    [Tooltip("Default board sprite when no trick is active.")]
    public Sprite defaultBoardSprite;

    [Header("Ollie (rise only, no rotation)")]
    [Tooltip("How high the board rises visually during an ollie (world units).")]
    public float ollieRise = 0.25f;
    [Tooltip("Shape of the rise/fall (0..1 time → 0..1 height).")]
    public AnimationCurve ollieCurve = AnimationCurve.EaseInOut(0, 0, 1, 0);

    [Header("Flip Tricks (per trick index 0..3)")]
    [Tooltip("Flip behavior per trick (RotateOnly or RotateAndSwapSprite).")]
    public FlipStyle[] flipStyles = new FlipStyle[4] { FlipStyle.RotateOnly, FlipStyle.RotateOnly, FlipStyle.RotateOnly, FlipStyle.RotateOnly };
    [Tooltip("Optional alternate sprites for RotateAndSwapSprite flips.")]
    public Sprite[] flipSprites = new Sprite[4];

    [Tooltip("Axis to rotate around for flips (use Z for top-down 2D).")]
    public Vector3 flipAxis = new Vector3(0, 0, 1);

    [Header("Grab Tricks (per trick index 0..3)")]
    [Tooltip("Sprites to display while each grab is held.")]
    public Sprite[] grabSprites = new Sprite[4];

    [Header("Grind")]
    [Tooltip("Optional sprite to display while grinding.")]
    public Sprite grindSprite;

    // ---- internals ----
    Vector3 _baseLocalPos;
    Quaternion _baseLocalRot;
    Sprite _cachedSprite;

    Coroutine _ollieCo;
    Coroutine _flipCo;

    bool _activeFlip;
    int _activeFlipIndex = -1;
    bool _activeGrab;
    int _activeGrabIndex = -1;
    bool _activeGrind;

    void Awake()
    {
        if (!boardSR) boardSR = GetComponent<SpriteRenderer>();
        if (!boardSR) Debug.LogWarning("[BoardVisual] Missing SpriteRenderer reference.");
        _baseLocalPos = transform.localPosition;
        _baseLocalRot = transform.localRotation;
        if (boardSR) _cachedSprite = boardSR.sprite;
        if (!defaultBoardSprite && boardSR) defaultBoardSprite = boardSR.sprite;
    }

    // =======================
    // Public API (Ollie)
    // =======================
    public void PlayOllie(float airSeconds)
    {
        if (airSeconds <= 0.03f) airSeconds = 0.3f;
        if (_ollieCo != null) StopCoroutine(_ollieCo);
        // capture base each ollie to avoid snapping to an old position
        _baseLocalPos = transform.localPosition;
        _ollieCo = StartCoroutine(OllieRiseRoutine(airSeconds));
    }

    IEnumerator OllieRiseRoutine(float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            float n = Mathf.Clamp01(t / Mathf.Max(0.0001f, dur));
            float y = ollieCurve.Evaluate(n) * ollieRise;
            transform.localPosition = _baseLocalPos + new Vector3(0f, y, 0f);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localPosition = _baseLocalPos;
        _ollieCo = null;
    }

    // =======================
    // Public API (Flips)
    // =======================
    /// <summary>
    /// Rotate the board to complete 'spins' full rotations in 'duration' seconds.
    /// Call with the trick index (0..3) so the component can optionally swap sprites depending on the trick.
    /// </summary>
    public void PlayFlip(float duration, int spins, int trickIndex)
    {
        _activeFlip = true;
        _activeFlipIndex = Mathf.Clamp(trickIndex, 0, 3);

        // sprite choice for flip (if configured)
        UpdateSpriteVisual(priority: "flip_start");

        if (_flipCo != null) StopCoroutine(_flipCo);
        _flipCo = StartCoroutine(FlipRoutine(duration, Mathf.Max(1, spins)));
    }

    IEnumerator FlipRoutine(float dur, int spins)
    {
        float t = 0f;
        float totalDeg = 360f * spins;

        // start from a neutral rotation (respect any grab/grind/static usage by composing at the end)
        _baseLocalRot = Quaternion.identity;

        while (t < dur)
        {
            float n = Mathf.Clamp01(t / Mathf.Max(0.0001f, dur));
            float angle = totalDeg * n;
            Quaternion spin = Quaternion.AngleAxis(angle, flipAxis.normalized);
            transform.localRotation = _baseLocalRot * spin;
            t += Time.deltaTime;
            yield return null;
        }

        // End aligned (no spin)
        transform.localRotation = Quaternion.identity;
        _flipCo = null;

        // end of flip
        _activeFlip = false;
        _activeFlipIndex = -1;
        UpdateSpriteVisual(priority: "flip_end");
    }

    // =======================
    // Public API (Grabs)
    // =======================
    /// <summary>Enter a grab (no rotation). Supply the grab trick index 0..3.</summary>
    public void BeginGrab(int grabIndex)
    {
        _activeGrab = true;
        _activeGrabIndex = Mathf.Clamp(grabIndex, 0, 3);
        UpdateSpriteVisual(priority: "grab_begin");
    }

    /// <summary>Exit a grab (restore previous sprite if appropriate).</summary>
    public void EndGrab()
    {
        _activeGrab = false;
        _activeGrabIndex = -1;
        UpdateSpriteVisual(priority: "grab_end");
    }

    // =======================
    // Public API (Grind)
    // =======================
    public void StartGrind()
    {
        _activeGrind = true;
        UpdateSpriteVisual(priority: "grind_begin");
    }

    public void StopGrind()
    {
        _activeGrind = false;
        UpdateSpriteVisual(priority: "grind_end");
    }

    // =======================
    // Legacy back-compat (optional)
    // If older code calls tilt-based grab methods, keep them compiling
    // but route to non-rotational grab visuals.
    // =======================
    public void BeginGrabTilt(float degrees)
    {
        // No rotation for grabs now; treat as a generic grab using index 0 if caller doesn't pass one.
        BeginGrab(0);
    }
    public void EndGrabTilt()
    {
        EndGrab();
    }
    public void ResetGrabs()
    {
        // End any grab and restore visuals.
        _activeGrab = false; _activeGrabIndex = -1;
        UpdateSpriteVisual(priority: "reset_grabs");
    }

    // =======================
    // Internal sprite selection
    // =======================
    void UpdateSpriteVisual(string priority)
    {
        if (!boardSR) return;

        // Choose which sprite should be visible based on current states.
        // Priority: Flip (if set to swap) > Grab > Grind > Default

        // 1) Flip
        if (_activeFlip && _activeFlipIndex >= 0 && _activeFlipIndex < flipStyles.Length)
        {
            if (flipStyles[_activeFlipIndex] == FlipStyle.RotateAndSwapSprite)
            {
                var fs = (flipSprites != null && _activeFlipIndex < flipSprites.Length) ? flipSprites[_activeFlipIndex] : null;
                if (fs) { boardSR.sprite = fs; return; }
            }
        }

        // 2) Grab
        if (_activeGrab && _activeGrabIndex >= 0 && grabSprites != null && _activeGrabIndex < grabSprites.Length)
        {
            var gs = grabSprites[_activeGrabIndex];
            if (gs) { boardSR.sprite = gs; return; }
        }

        // 3) Grind
        if (_activeGrind && grindSprite)
        {
            boardSR.sprite = grindSprite; return;
        }

        // 4) Default
        if (defaultBoardSprite) boardSR.sprite = defaultBoardSprite;
        else if (_cachedSprite) boardSR.sprite = _cachedSprite;
    }
}
