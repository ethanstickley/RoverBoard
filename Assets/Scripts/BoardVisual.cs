using System.Collections;
using UnityEngine;

/// <summary>
/// BoardVisual
/// - Visual spin for FLIP tricks (complete N spins over duration).
/// - Visual sustained tilt for GRAB tricks (enter/hold/exit).
/// - Visual ollie tilt/roll + rider hop trigger passthrough (optional).
/// This script is purely visual—no physics.
/// </summary>
[DisallowMultipleComponent]
public class BoardVisual : MonoBehaviour
{
    [Header("Refs")]
    public SpriteRenderer boardSR;

    [Header("Ollie Visual")]
    public float ollieTiltDegrees = 18f;
    public float ollieOscillations = 1.0f; // back-and-forth wobble count
    public AnimationCurve ollieTiltCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    public float ollieOptionalRoll = 0f; // additive Z-roll across air time

    [Header("Flip Visuals")]
    [Tooltip("Axis to spin around for flips (Z for top-down 2D).")]
    public Vector3 flipAxis = new Vector3(0, 0, 1);

    [Header("Grab Visuals")]
    [Tooltip("How quickly we lerp into grab tilt (deg/s).")]
    public float grabTiltInSpeed = 180f;
    [Tooltip("How quickly we lerp out of grab tilt (deg/s).")]
    public float grabTiltOutSpeed = 240f;

    Quaternion _baseLocalRot;
    Coroutine _ollieCo;
    Coroutine _flipCo;
    float _currentGrabTilt;      // current degrees
    float _targetGrabTilt;       // target degrees while holding grab

    void Awake()
    {
        if (!boardSR) boardSR = GetComponent<SpriteRenderer>();
        _baseLocalRot = transform.localRotation;
    }

    void LateUpdate()
    {
        // Smooth the grab tilt each frame
        if (Mathf.Abs(_targetGrabTilt - _currentGrabTilt) > 0.01f)
        {
            float speed = (_targetGrabTilt > _currentGrabTilt) ? grabTiltInSpeed : grabTiltOutSpeed;
            float step = speed * Time.deltaTime * Mathf.Sign(_targetGrabTilt - _currentGrabTilt);
            float next = _currentGrabTilt + step;

            if (Mathf.Sign(_targetGrabTilt - next) != Mathf.Sign(_targetGrabTilt - _currentGrabTilt))
                next = _targetGrabTilt;

            _currentGrabTilt = next;

            // Compose final rotation: base + grab tilt (around Z) + any ongoing flip rotation already set in coroutine
            // Since flips also rotate Z, we combine by adding an extra local Z here:
            var e = transform.localRotation.eulerAngles;
            e.z = _baseLocalRot.eulerAngles.z + e.z; // maintain any active flip rotation in 'e.z'
            transform.localRotation = Quaternion.Euler(0f, 0f, e.z + _currentGrabTilt);
        }
    }

    // ---------------- OLLIE ----------------
    public void PlayOllie(float airSeconds)
    {
        if (_ollieCo != null) StopCoroutine(_ollieCo);
        _ollieCo = StartCoroutine(OllieRoutine(airSeconds));
    }

    IEnumerator OllieRoutine(float dur)
    {
        float t = 0f;
        var startRot = _baseLocalRot;
        while (t < dur)
        {
            float n = Mathf.Clamp01(t / Mathf.Max(0.0001f, dur));
            float shaped = ollieTiltCurve.Evaluate(n);
            float osc = Mathf.Sin(n * Mathf.PI * 2f * ollieOscillations);
            float tilt = (shaped * ollieTiltDegrees) * osc;
            float roll = ollieOptionalRoll * n;

            transform.localRotation = startRot * Quaternion.Euler(0f, 0f, tilt + roll + _currentGrabTilt);
            t += Time.deltaTime;
            yield return null;
        }
        transform.localRotation = _baseLocalRot * Quaternion.Euler(0f, 0f, _currentGrabTilt);
        _ollieCo = null;
    }

    // ---------------- FLIP (spin N times over duration) ----------------
    public void PlayFlip(float duration, int spins)
    {
        if (_flipCo != null) StopCoroutine(_flipCo);
        _flipCo = StartCoroutine(FlipRoutine(duration, spins));
    }

    IEnumerator FlipRoutine(float dur, int spins)
    {
        float t = 0f;
        float totalDeg = 360f * Mathf.Max(1, spins);
        while (t < dur)
        {
            float n = Mathf.Clamp01(t / Mathf.Max(0.0001f, dur));
            float angle = totalDeg * n;
            // Spin around flipAxis (Z for top-down) and add current grab tilt on Z
            Quaternion spin = Quaternion.AngleAxis(angle, flipAxis.normalized);
            transform.localRotation = spin * Quaternion.Euler(0f, 0f, _currentGrabTilt);
            t += Time.deltaTime;
            yield return null;
        }
        // End aligned (no spin), keep grab tilt if any
        transform.localRotation = _baseLocalRot * Quaternion.Euler(0f, 0f, _currentGrabTilt);
        _flipCo = null;
    }

    // ---------------- GRAB (sustained tilt) ----------------
    public void BeginGrabTilt(float degrees)
    {
        _targetGrabTilt = degrees;
    }

    public void EndGrabTilt()
    {
        _targetGrabTilt = 0f;
    }

    public void ResetGrabs()
    {
        _targetGrabTilt = 0f;
        _currentGrabTilt = 0f;
        transform.localRotation = _baseLocalRot;
    }
}
