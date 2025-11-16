using UnityEngine;

/// <summary>
/// Directional ramp:
/// - Add a 2D trigger collider that covers the ramp surface
/// - Orient the GameObject's right (+X) in the desired approach direction
/// - Requires player approaching within angle & speed thresholds to launch
/// - On success: calls player.StartAir(extraAirTime)
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Ramp2D : MonoBehaviour
{
    public float minApproachSpeed = 3.0f;
    [Range(0f, 90f)] public float maxApproachAngle = 35f; // degrees from forward
    public float extraAirTime = 1.0f;                     // added to player's air window

    void OnTriggerEnter2D(Collider2D other)
    {
        var pc = other.GetComponentInParent<PlayerController2D>();
        if (!pc) return;

        Vector2 v = pc.rb.linearVelocity;
        float speed = v.magnitude;
        if (speed < minApproachSpeed) return;

        // ramp forward (world)
        Vector2 forward = transform.right.normalized;

        float ang = Vector2.Angle(forward, v.normalized);
        if (ang <= maxApproachAngle)
        {
            // launch!
            pc.StartAir(extraAirTime);
            Debug.Log($"RAMP LAUNCH (+{extraAirTime:0.00}s) angle={ang:0.0} speed={speed:0.0}");
        }
        else
        {
            // too oblique: no launch
        }
    }
}
