// BoardVisual.cs — stays active; exposes SetVisible(bool) to show/hide only the SpriteRenderer.
// Also includes the hardened coroutine guards + half-turn spins + shared flip sheet logic.
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class BoardVisual : MonoBehaviour
{
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

    [Header("Flip Tricks (shared sprite sheet + per-trick rules)")]
    [Tooltip("Shared sprite sheet used for ALL flip tricks. A 'flip' is one full cycle through these frames.")]
    public Sprite[] flipSheetFrames;

    [Tooltip("Per-trick: whether to animate the sprite sheet while flipping (one or more full cycles). Length should match your flip trick count (e.g., 5).")]
    public bool[] flipUseSheet = new bool[5] { true, true, true, true, true };

    [Tooltip("Per-trick: how many full sheet cycles ('flips') to complete during the flip duration. Length should match your flip trick count.")]
    public int[] flipSheetCycles = new int[5] { 1, 1, 1, 1, 1 };

    [Tooltip("Per-trick: number of 180° half-turns to rotate the BoardVisual during the flip. E.g., 2 = 360°, 3 = 540°. Length should match your flip trick count.")]
    public int[] flipHalfTurns = new int[5] { 2, 2, 2, 3, 2 };

    [Tooltip("Axis to rotate around for spins (use Z for top-down 2D).")]
    public Vector3 flipAxis = new Vector3(0, 0, 1);

    [Header("Grab Tricks (per-trick static sprites)")]
    [Tooltip("Sprites to display while each grab is held. Length should match your grab trick count (e.g., 5).")]
    public Sprite[] grabSprites = new Sprite[5];

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
        _baseLocalPos = transform.localPosition;
        _baseLocalRot = transform.localRotation;
        if (boardSR) _cachedSprite = boardSR.sprite;
        if (!defaultBoardSprite && boardSR) defaultBoardSprite = boardSR.sprite;
    }

    void OnDisable()
    {
        // Stop any running anims when this component is disabled
        SafeStop(ref _ollieCo);
        SafeStop(ref _flipCo);

        if (this != null)
        {
            transform.localPosition = _baseLocalPos;
            transform.localRotation = Quaternion.identity;
        }
    }

    void OnDestroy()
    {
        SafeStop(ref _ollieCo);
        SafeStop(ref _flipCo);
    }

    // ======== Visibility (Option 1) ========
    public void SetVisible(bool visible)
    {
        if (boardSR) boardSR.enabled = visible;
        // Do NOT disable this GameObject. We keep this component active at all times.
    }

    // =======================
    // Public API (Ollie)
    // =======================
    public void PlayOllie(float airSeconds)
    {
        if (!isActiveAndEnabled || this == null) return;
        if (airSeconds <= 0.03f) airSeconds = 0.3f;
        SafeStop(ref _ollieCo);
        _baseLocalPos = transform.localPosition; // capture base each ollie
        _ollieCo = StartCoroutine(OllieRiseRoutine(airSeconds));
    }

    IEnumerator OllieRiseRoutine(float dur)
    {
        float t = 0f;
        while (t < dur)
        {
            if (!IsUsable()) yield break;

            float n = Mathf.Clamp01(t / Mathf.Max(0.0001f, dur));
            float y = ollieCurve.Evaluate(n) * ollieRise;
            transform.localPosition = _baseLocalPos + new Vector3(0f, y, 0f);

            t += Time.deltaTime;
            yield return null;
        }

        if (IsUsable()) transform.localPosition = _baseLocalPos;
        _ollieCo = null;
    }

    // =======================
    // Public API (Flips)
    // =======================
    /// <summary>
    /// Play a flip trick.
    /// NOTE: spins parameter kept for backward-compat; total rotation prefers per-trick flipHalfTurns (180° units).
    /// If flipHalfTurns lacks an entry, we fall back to spins (full 360° rotations).
    /// </summary>
    public void PlayFlip(float duration, int spins /* legacy full-rotations */, int trickIndex)
    {
        if (!isActiveAndEnabled || this == null) return;

        _activeFlip = true;
        _activeFlipIndex = Mathf.Clamp(trickIndex, 0, Mathf.Max(0, (flipUseSheet != null ? flipUseSheet.Length : 0) - 1));

        SafeStop(ref _flipCo);
        _flipCo = StartCoroutine(FlipRoutine(duration, spins, _activeFlipIndex));
    }

    IEnumerator FlipRoutine(float dur, int legacySpins, int trickIdx)
    {
        float t = 0f;

        // Total rotation degrees (prefer half-turns)
        int halfTurns = SafeGet(flipHalfTurns, trickIdx, -1);
        float totalDeg = (halfTurns >= 0) ? 180f * halfTurns : 360f * Mathf.Max(1, legacySpins);

        // Sheet usage
        bool useSheet = SafeGet(flipUseSheet, trickIdx, false);
        int cycles = Mathf.Max(0, SafeGet(flipSheetCycles, trickIdx, 0));
        int frameCount = (flipSheetFrames != null) ? flipSheetFrames.Length : 0;

        _baseLocalRot = Quaternion.identity;

        while (t < dur)
        {
            if (!IsUsable()) yield break;

            float n = Mathf.Clamp01(t / Mathf.Max(0.0001f, dur));

            // SPIN (full GO rotation)
            float angle = totalDeg * n;
            Quaternion spin = Quaternion.AngleAxis(angle, flipAxis.normalized);
            transform.localRotation = _baseLocalRot * spin;

            // FLIP (sheet animation cycles)
            if (useSheet && frameCount > 0 && cycles > 0)
            {
                float framesF = n * (cycles * frameCount);
                int frameIndex = Mathf.FloorToInt(framesF) % frameCount;
                if (boardSR && frameIndex >= 0 && frameIndex < frameCount)
                {
                    var sprite = flipSheetFrames[frameIndex];
                    if (sprite) boardSR.sprite = sprite;
                }
            }
            else
            {
                UpdateSpriteVisual(priority: "flip_tick_no_sheet");
            }

            t += Time.deltaTime;
            yield return null;
        }

        if (IsUsable())
        {
            transform.localRotation = Quaternion.identity;
            _activeFlip = false;
            _activeFlipIndex = -1;
            UpdateSpriteVisual(priority: "flip_end");
        }

        _flipCo = null;
    }

    // =======================
    // Public API (Grabs)
    // =======================
    public void BeginGrab(int grabIndex)
    {
        if (!isActiveAndEnabled || this == null) return;
        _activeGrab = true;
        _activeGrabIndex = Mathf.Clamp(grabIndex, 0, Mathf.Max(0, grabSprites.Length - 1));
        UpdateSpriteVisual(priority: "grab_begin");
    }

    public void EndGrab()
    {
        if (!isActiveAndEnabled || this == null) return;
        _activeGrab = false;
        _activeGrabIndex = -1;
        UpdateSpriteVisual(priority: "grab_end");
    }

    // =======================
    // Public API (Grind)
    // =======================
    public void StartGrind()
    {
        if (!isActiveAndEnabled || this == null) return;
        _activeGrind = true;
        UpdateSpriteVisual(priority: "grind_begin");
    }

    public void StopGrind()
    {
        if (!isActiveAndEnabled || this == null) return;
        _activeGrind = false;
        UpdateSpriteVisual(priority: "grind_end");
    }

    // =======================
    // Legacy helpers
    // =======================
    public void BeginGrabTilt(float degrees) { BeginGrab(0); }
    public void EndGrabTilt() { EndGrab(); }
    public void ResetGrabs()
    {
        if (!isActiveAndEnabled || this == null) return;
        _activeGrab = false; _activeGrabIndex = -1;
        UpdateSpriteVisual(priority: "reset_grabs");
    }

    // =======================
    // Internal sprite selection
    // =======================
    void UpdateSpriteVisual(string priority)
    {
        if (!IsUsable()) return;

        // If a flip is actively sheet-animating, that coroutine writes frames directly.
        // Otherwise resolve: Grab > Grind > Default.

        // 1) Grab
        if (_activeGrab && _activeGrabIndex >= 0 && grabSprites != null && _activeGrabIndex < grabSprites.Length)
        {
            var gs = grabSprites[_activeGrabIndex];
            if (gs && boardSR) { boardSR.sprite = gs; return; }
        }

        // 2) Grind
        if (_activeGrind && grindSprite && boardSR)
        {
            boardSR.sprite = grindSprite; return;
        }

        // 3) Default
        if (boardSR)
        {
            if (defaultBoardSprite) boardSR.sprite = defaultBoardSprite;
            else if (_cachedSprite) boardSR.sprite = _cachedSprite;
        }
    }

    // =======================
    // Helpers
    // =======================
    static T SafeGet<T>(T[] arr, int idx, T fallback)
    {
        if (arr == null || idx < 0 || idx >= arr.Length) return fallback;
        return arr[idx];
    }

    void SafeStop(ref Coroutine co)
    {
        if (co != null)
        {
            try { StopCoroutine(co); }
            catch { }
            co = null;
        }
    }

    bool IsUsable()
    {
        if (this == null) return false;
        if (!isActiveAndEnabled) return false;
        return true;
    }
}
