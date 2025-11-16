using UnityEngine;

/// <summary>
/// Simple grind trigger:
/// - Add a thin trigger collider along rail
/// - When player enters and is airborne or grounded with small vertical velocity,
///   we start grind; on exit we stop.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RailGrind : MonoBehaviour
{
    public TrickManager trick;
    public float alignGraceAngle = 50f; // not strict for top-down

    void Awake()
    {
        if (!trick) trick = FindObjectOfType<TrickManager>();
        gameObject.tag = "Rail";
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var pc = other.GetComponentInParent<PlayerController2D>();
        if (!pc || trick == null) return;

        // Optional: check alignment loosely
        Vector2 forward = transform.right;
        float ang = Vector2.Angle(forward, pc.rb.linearVelocity.normalized);
        if (ang <= alignGraceAngle)
        {
            trick.StartGrind();
        }
    }

    void OnTriggerExit2D(Collider2D other)
    {
        var pc = other.GetComponentInParent<PlayerController2D>();
        if (!pc || trick == null) return;
        trick.StopGrind();
    }
}
