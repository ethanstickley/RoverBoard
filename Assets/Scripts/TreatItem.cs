using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TreatItem : MonoBehaviour
{
    public float goodEnergy = 0.15f;

    void OnValidate()
    {
        var c = GetComponent<Collider2D>();
        if (c) c.isTrigger = true;
    }

    void Reset()
    {
        var c = GetComponent<Collider2D>();
        if (c) c.isTrigger = true;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var dog = other.GetComponentInParent<DogAI2D>();
        if (!dog) return;
        dog.AddGoodDogEnergy(goodEnergy);
        Debug.Log("Treat: Good boy!");
        Destroy(gameObject);
    }
}
