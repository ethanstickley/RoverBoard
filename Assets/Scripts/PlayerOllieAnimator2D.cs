using System.Collections;
using UnityEngine;

/// <summary>
/// PlayerOllieAnimator2D
/// Purely visual hop animation for the rider during an ollie/airtime.
/// - Does NOT affect physics.
/// - Optionally swaps to an airborne sprite during the hop, then restores.
/// - Provides subtle lateral sway while airborne (optional).
///
/// Fixes:
/// - Re-captures the base local position at the start of EVERY ollie to avoid
///   snapping back to a stale position captured at Awake.
/// 
/// Use:
///   • Attach to the Player root (or a visual child).
///   • Assign 'riderVisual' (ideally a visual child, not the physics root).
///   • (Optional) Assign 'riderSR' and 'airborneSprite' to swap sprite in-air.
///   • Call PlayOllieHop(airTimeSeconds) when airtime starts (e.g., from TrickManager.TriggerAirtime).
/// </summary>
[DisallowMultipleComponent]
public class PlayerOllieAnimator2D : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The transform to animate up/down visually (ideally a child that holds the sprite).")]
    public Transform riderVisual;

    [Tooltip("Optional SpriteRenderer used to temporarily display an 'airborne' sprite.")]
    public SpriteRenderer riderSR;

    [Tooltip("Optional sprite shown while airborne. Leave null to keep current sprite.")]
    public Sprite airborneSprite;

    [Header("Hop Settings (purely visual)")]
    [Tooltip("How high the visual hop goes (world units).")]
    public float hopHeight = 0.35f;

    [Tooltip("Vertical hop curve. X=normalized time 0..1, Y=offset multiplier 0..1.")]
    public AnimationCurve hopCurve = AnimationCurve.EaseInOut(0, 0, 0.5f, 1);

    [Tooltip("Ease down portion of the hop (multiplies the second half of the curve). 1 = symmetric.")]
    [Range(0.5f, 2f)]
    public float fallEase = 1.0f;

    [Header("Optional Bob While Airborne")]
    [Tooltip("Small lateral sway during air time for style. 0 disables.")]
    public float lateralSway = 0.05f;

    [Tooltip("Sway frequency (Hz) while airborne.")]
    public float swayHz = 3f;

    // Internal state
    Vector3 _baseLocalPos;        // recaptured at the start of every hop
    Coroutine _hopRoutine;
    Sprite _cachedSprite;

    void Awake()
    {
        if (!riderVisual) riderVisual = transform;
        if (!riderSR) riderSR = GetComponentInChildren<SpriteRenderer>();
        // Do not cache _baseLocalPos here; we capture it fresh each hop.
    }

    /// <summary>
    /// Triggers the rider's visual hop for an ollie. Does not affect physics.
    /// </summary>
    /// <param name="airTimeSeconds">Duration of the hop animation (ideally matches actual airtime).</param>
    public void PlayOllieHop(float airTimeSeconds)
    {
        if (airTimeSeconds <= 0.03f) airTimeSeconds = 0.3f;

        // Capture the base local position RIGHT NOW so the animation is relative
        // to the player's CURRENT location, not a stale value from Awake.
        _baseLocalPos = riderVisual.localPosition;

        if (_hopRoutine != null) StopCoroutine(_hopRoutine);
        _hopRoutine = StartCoroutine(HopRoutine(airTimeSeconds));
    }

    IEnumerator HopRoutine(float dur)
    {
        float t = 0f;
        bool swapped = false;

        // Optional sprite swap while airborne
        if (riderSR && airborneSprite)
        {
            _cachedSprite = riderSR.sprite;
            riderSR.sprite = airborneSprite;
            swapped = true;
        }

        while (t < dur)
        {
            float n = Mathf.Clamp01(t / Mathf.Max(0.0001f, dur));

            // Shape the up/down offset with configurable curve
            float shaped = hopCurve.Evaluate(n <= 0.5f ? n : Mathf.Lerp(0.5f, 1f, (n - 0.5f) * fallEase));
            float y = shaped * hopHeight;

            // Optional lateral sway
            float x = 0f;
            if (lateralSway > 0f)
                x = Mathf.Sin(t * Mathf.PI * 2f * swayHz) * lateralSway;

            riderVisual.localPosition = _baseLocalPos + new Vector3(x, y, 0f);

            t += Time.deltaTime;
            yield return null;
        }

        // Reset position and restore sprite
        riderVisual.localPosition = _baseLocalPos;

        if (swapped && riderSR)
            riderSR.sprite = _cachedSprite;

        _hopRoutine = null;
    }

    /// <summary>Immediately resets any visual offsets/sprite swap.</summary>
    public void ResetVisual()
    {
        if (_hopRoutine != null) StopCoroutine(_hopRoutine);
        _hopRoutine = null;
        // Safely restore to the most recent base (if we never hopped yet, this will likely be zero)
        if (riderVisual) riderVisual.localPosition = _baseLocalPos;
        if (riderSR && _cachedSprite) riderSR.sprite = _cachedSprite;
    }
}
