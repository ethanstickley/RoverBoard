// LooseBoard.cs � slides with given velocity and despawns
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class LooseBoard : MonoBehaviour
{
    public float lifetime = 2f;
    public float dragPerSec = 0.25f;   // slight slow-down while sliding

    Rigidbody2D rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void Start()
    {
        if (lifetime > 0f) Destroy(gameObject, lifetime);
    }

    void FixedUpdate()
    {
        // light slide damping
        rb.linearVelocity *= Mathf.Clamp01(1f - dragPerSec * Time.fixedDeltaTime);
    }
}
