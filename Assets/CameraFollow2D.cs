using UnityEngine;

/// <summary>
/// 2D orthographic camera follow that always keeps the rider in frame,
/// and pulls toward the dog when the dog strays far enough,
/// zooming out as needed to fit both with padding.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow2D : MonoBehaviour
{
    [Header("Targets")]
    public Transform player;     // REQUIRED
    public Transform dog;        // OPTIONAL (camera still follows player if null)

    [Header("Follow Tuning")]
    [Tooltip("How quickly camera position moves toward its target (0..1 per frame).")]
    [Range(0.01f, 1f)] public float followLerp = 0.15f;

    [Tooltip("How quickly camera size (zoom) moves toward its target (0..1 per frame).")]
    [Range(0.01f, 1f)] public float sizeLerp = 0.2f;

    [Tooltip("Deadzone in world units: camera won't translate if player stays within this radius around current focus.")]
    public float positionDeadzone = 0.25f;

    [Header("Zoom / Framing")]
    [Tooltip("Smallest orthographic size (half of vertical view).")]
    public float minOrthoSize = 8f;

    [Tooltip("Largest orthographic size (half of vertical view).")]
    public float maxOrthoSize = 20f;

    [Tooltip("Extra world-units padding around the targets when computing size.")]
    public float framePadding = 2f;

    [Header("Dog Influence")]
    [Tooltip("Dog starts to influence the camera when farther than this from the player.")]
    public float dogInfluenceStart = 6f;

    [Tooltip("Dog has full influence by this distance.")]
    public float dogFullInfluence = 20f;

    [Tooltip("Bias keeping the camera closer to the player when blending toward midpoint (0=exact midpoint, 1=stick to player).")]
    [Range(0f, 1f)] public float playerBiasAtMax = 0.2f;

    [Header("World Bounds (optional)")]
    public bool clampToBounds = false;
    public Vector2 worldMin = new Vector2(-80, -80);
    public Vector2 worldMax = new Vector2(80, 80);

    Camera cam;
    float startZ;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;                 // force ortho
        startZ = transform.position.z;           // usually -10
        transform.rotation = Quaternion.identity; // constant orientation
    }

    void LateUpdate()
    {
        if (player == null) return;

        // --- 1) Compute desired focus point ---
        Vector2 playerPos = player.position;
        Vector2 desiredPos = playerPos;

        bool considerDog = (dog != null);
        float dogWeight = 0f;

        if (considerDog)
        {
            Vector2 dogPos = dog.position;
            float d = Vector2.Distance(playerPos, dogPos);

            if (d > dogInfluenceStart)
            {
                // how much the dog influences the framing (0..1)
                dogWeight = Mathf.InverseLerp(dogInfluenceStart, dogFullInfluence, d);
                dogWeight = Mathf.Clamp01(dogWeight);

                // midpoint biased toward player (player always favored by playerBiasAtMax at full influence)
                float bias = Mathf.Lerp(0f, playerBiasAtMax, dogWeight);  // 0 → midpoint, up to 'playerBiasAtMax'
                Vector2 midpoint = (playerPos + dogPos) * 0.5f;
                Vector2 biasedFocus = Vector2.Lerp(midpoint, playerPos, bias);

                desiredPos = Vector2.Lerp(playerPos, biasedFocus, dogWeight);
            }
            else
            {
                desiredPos = playerPos;
            }
        }

        // Maintain constant orientation (no rotation)
        transform.rotation = Quaternion.identity;

        // Deadzone to reduce tiny jitters
        Vector2 camXY = new Vector2(transform.position.x, transform.position.y);
        Vector2 toTarget = desiredPos - camXY;
        if (toTarget.magnitude > positionDeadzone)
        {
            camXY = Vector2.Lerp(camXY, desiredPos, followLerp);
        }

        // --- 2) Compute desired orthographic size to keep targets in frame ---
        float desiredSize = minOrthoSize;

        if (considerDog)
        {
            // If dog is influencing (or simply far enough), include both in bounds
            Vector2 dogPos = dog.position;

            // Always include player; include dog if dogWeight > 0 or if already outside current view
            bool includeDog = dogWeight > 0f;

            if (includeDog)
            {
                Bounds b = new Bounds(playerPos, Vector3.zero);
                b.Encapsulate(dogPos);
                desiredSize = OrthoSizeToFitBounds(b, cam.aspect, framePadding);
            }
            else
            {
                // Only player: keep min size
                desiredSize = minOrthoSize;
            }
        }
        else
        {
            desiredSize = minOrthoSize;
        }

        desiredSize = Mathf.Clamp(desiredSize, minOrthoSize, maxOrthoSize);
        float smoothSize = Mathf.Lerp(cam.orthographicSize, desiredSize, sizeLerp);

        // --- 3) Optional world bounds clamp ---
        if (clampToBounds)
        {
            Vector2 halfView = new Vector2(smoothSize * cam.aspect, smoothSize);
            float clampedX = Mathf.Clamp(camXY.x, worldMin.x + halfView.x, worldMax.x - halfView.x);
            float clampedY = Mathf.Clamp(camXY.y, worldMin.y + halfView.y, worldMax.y - halfView.y);
            camXY = new Vector2(clampedX, clampedY);
        }

        // --- 4) Apply ---
        transform.position = new Vector3(camXY.x, camXY.y, startZ);
        cam.orthographicSize = smoothSize;
    }

    static float OrthoSizeToFitBounds(Bounds b, float aspect, float padding)
    {
        // Required half-extents with padding
        float halfWidth = (b.size.x * 0.5f) + padding;
        float halfHeight = (b.size.y * 0.5f) + padding;

        // Ortho size is half of vertical view; must also fit width via aspect
        float sizeByHeight = halfHeight;
        float sizeByWidth = halfWidth / Mathf.Max(0.01f, aspect);

        return Mathf.Max(sizeByHeight, sizeByWidth);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // Visualize dog influence distances around the player in editor
        if (player != null)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.4f);
            Gizmos.DrawWireSphere(player.position, dogInfluenceStart);
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.4f);
            Gizmos.DrawWireSphere(player.position, dogFullInfluence);
        }

        if (clampToBounds)
        {
            Gizmos.color = Color.green;
            Vector3 center = new Vector3((worldMin.x + worldMax.x) * 0.5f, (worldMin.y + worldMax.y) * 0.5f, 0f);
            Vector3 size = new Vector3(worldMax.x - worldMin.x, worldMax.y - worldMin.y, 0f);
            Gizmos.DrawWireCube(center, size);
        }
    }
#endif
}
