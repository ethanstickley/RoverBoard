using UnityEngine;

/// <summary>
/// Optional helper: attach to objects the Dog can notice in future systems.
/// Not currently required by CatAI2D or TreatItem, but safe to include.
/// </summary>
public class DogInterestSignal : MonoBehaviour
{
    [Tooltip("How noticeable this interest is.")]
    public float weight = 1f;

    public void SetWeight(float w) => weight = Mathf.Max(0f, w);
    public void Multiply(float m) => weight *= Mathf.Max(0f, m);
}
