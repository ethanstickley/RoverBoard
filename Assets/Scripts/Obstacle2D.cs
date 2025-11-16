using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Obstacle2D : MonoBehaviour
{
    public enum HeightClass { Short, Tall }
    public HeightClass heightClass = HeightClass.Short;

    void Reset()
    {
        // Set recommended layer automatically if it exists
        string layerName = (heightClass == HeightClass.Short) ? "ShortObstacle" : "TallObstacle";
        int l = LayerMask.NameToLayer(layerName);
        if (l >= 0) gameObject.layer = l;
    }
}
