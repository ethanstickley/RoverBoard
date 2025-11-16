using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class PeeDecal : MonoBehaviour
{
    [Tooltip("Seconds until fully faded then destroyed.")]
    public float lifetime = 10f;

    [Tooltip("Optional random initial alpha multiplier (min..max).")]
    public Vector2 startAlphaRange = new Vector2(0.8f, 1.0f);

    SpriteRenderer _sr;
    float _t;
    float _startAlpha = 1f;

    void Awake()
    {
        _sr = GetComponent<SpriteRenderer>();
        if (_sr)
        {
            _startAlpha = Mathf.Clamp01(Random.Range(startAlphaRange.x, startAlphaRange.y));
            var c = _sr.color;
            c.a = _startAlpha;
            _sr.color = c;
            // Sort slightly under characters if needed
            _sr.sortingOrder = -1;
        }
    }

    void Update()
    {
        if (!_sr) return;

        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / Mathf.Max(0.01f, lifetime));
        float a = Mathf.Lerp(_startAlpha, 0f, k);

        var c = _sr.color;
        c.a = a;
        _sr.color = c;

        if (_t >= lifetime) Destroy(gameObject);
    }
}
