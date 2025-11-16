using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Hydrant : MonoBehaviour
{
    [Tooltip("Optional: if true, dog must be inside this trigger to pee here.")]
    public bool requireInsideTriggerToPee = true;

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col) col.isTrigger = true; // hydrants should be trigger volumes
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var dog = other.GetComponentInParent<DogAI2D>();
        if (!dog) return;

        // Dog should only be interested in hydrants if it actually needs to pee
        if (dog.bladder <= 0f) return;

        // Mark interest and (optionally) start peeing immediately upon contact
        dog.SetInterest(transform);

        if (requireInsideTriggerToPee)
        {
            dog.BeginPee(transform);
        }
    }

    // If you prefer to start peeing on stay instead of just enter,
    // uncomment this and disable BeginPee in OnTriggerEnter2D above.
    /*
    void OnTriggerStay2D(Collider2D other)
    {
        var dog = other.GetComponentInParent<DogAI2D>();
        if (!dog) return;
        if (dog.bladder <= 0f) return;

        dog.SetInterest(transform);
        dog.BeginPee(transform);
    }
    */
}
