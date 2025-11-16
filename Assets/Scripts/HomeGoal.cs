using UnityEngine;

/// <summary>
/// Place at home door with a trigger. Finishes level ONLY if dog's bladder is empty.
/// </summary>
public class HomeGoal : MonoBehaviour
{
    public DogAI2D dog;

    void OnTriggerEnter2D(Collider2D other)
    {
        var pc = other.GetComponentInParent<PlayerController2D>();
        if (!pc || dog == null) return;

        if (dog.bladder <= 0.01f)
        {
            Debug.Log("LEVEL COMPLETE: Dog’s bladder empty. You can go home!");
        }
        else
        {
            Debug.Log($"Cannot go home yet. Dog still needs to pee: {dog.bladder:0.0}/{dog.bladderMax:0}");
        }
    }
}
