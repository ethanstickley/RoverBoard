using UnityEngine;

public class PoopStreaksEmitter : MonoBehaviour
{
    [Header("Follow Target")]
    [Tooltip("Transform to follow (the player's BoardVisual).")]
    public Transform target;

    [Tooltip("Seconds to follow and emit trails after being spawned.")]
    public float emitDuration = 2.0f;

    [Header("Trail Appearance")]
    [Tooltip("How long the trails remain visible after emission stops (seconds).")]
    public float trailTime = 10f;

    [Tooltip("Trail width (units).")]
    public float width = 0.08f;

    [Tooltip("Horizontal offset from the board center to each wheel trail (units).")]
    public float offsetX = 0.25f;

    [Tooltip("Optional slight backward offset to place the trails under the ‘wheels’.")]
    public float backwardOffset = 0.0f;

    [Tooltip("Primary color of the streaks.")]
    public Color brownColor = new Color(0.35f, 0.20f, 0.05f, 1f);

    [Tooltip("Alpha at start/end of trail lifetime.")]
    public Vector2 alphaRange = new Vector2(1f, 0f);

    [Tooltip("Optional: use this material; if null, a Sprites/Default material is created.")]
    public Material trailMaterial;

    [Header("Safety (no physics)")]
    [Tooltip("Name of a layer that does NOT collide with the player/board. Leave empty to skip.")]
    public string nonCollidingLayerName = "Decals";
    [Tooltip("If true, set this layer on the emitter and all children on Awake.")]
    public bool applyLayerOnAwake = true;
    [Tooltip("If true, remove any Collider2D/Rigidbody2D found on this object or children.")]
    public bool strip2DPhysicsOnAwake = true;

    // internals
    TrailRenderer _left, _right;
    float _t;
    bool _emitting = true;
    float _cleanupAt = -1f;

    void Awake()
    {
        // Safety first: ensure this hierarchy can never block movement
        if (applyLayerOnAwake && !string.IsNullOrEmpty(nonCollidingLayerName))
        {
            int layer = LayerMask.NameToLayer(nonCollidingLayerName);
            if (layer >= 0) SetLayerRecursively(gameObject, layer);
        }
        if (strip2DPhysicsOnAwake)
        {
            foreach (var c in GetComponentsInChildren<Collider2D>(true)) Destroy(c);
            foreach (var rb in GetComponentsInChildren<Rigidbody2D>(true)) Destroy(rb);
        }

        // Create child trail objects
        _left = CreateTrail("LeftTrail");
        _right = CreateTrail("RightTrail");
    }

    TrailRenderer CreateTrail(string name)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);

        // keep child on same layer as parent
        child.layer = gameObject.layer;

        var tr = child.AddComponent<TrailRenderer>();

        // Material
        if (!trailMaterial)
        {
            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = Color.white;
            tr.material = mat;
        }
        else tr.material = trailMaterial;

        // Time & width
        tr.time = trailTime;
        tr.widthMultiplier = width;

        // Brown → transparent gradient
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new GradientColorKey(brownColor, 0f), new GradientColorKey(brownColor, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(alphaRange.x, 0f), new GradientAlphaKey(alphaRange.y, 1f) }
        );
        tr.colorGradient = grad;

        // Draw with sprites sorting so it sits under characters but above ground
        tr.sortingLayerName = string.IsNullOrEmpty(nonCollidingLayerName) ? tr.sortingLayerName : "Decals";
        tr.sortingOrder = 5;

        tr.minVertexDistance = 0.03f;
        tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        tr.receiveShadows = false;
        tr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        tr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        return tr;
    }

    void Update()
    {
        if (_emitting)
        {
            _t += Time.deltaTime;
            if (_t >= emitDuration)
            {
                _emitting = false;
                _cleanupAt = Time.time + Mathf.Max(0.1f, trailTime + 0.5f);
            }
        }

        if (_emitting && target)
        {
            transform.position = target.position;
            transform.rotation = target.rotation;

            Vector3 right = target.right;
            Vector3 forward = target.up;
            Vector3 back = -forward * backwardOffset;

            Vector3 leftPos = target.position - right * offsetX + back;
            Vector3 rightPos = target.position + right * offsetX + back;

            _left.transform.position = leftPos;
            _right.transform.position = rightPos;
        }

        if (!_emitting && _cleanupAt > 0f && Time.time >= _cleanupAt)
        {
            Destroy(gameObject);
        }
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform)
            SetLayerRecursively(t.gameObject, layer);
    }
}
