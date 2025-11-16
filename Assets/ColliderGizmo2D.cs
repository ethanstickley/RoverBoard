using UnityEngine;

public class ColliderGizmos2D : MonoBehaviour
{
    public Color color = new Color(0f, 1f, 0f, 0.5f);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = color;
        foreach (var col in GetComponentsInChildren<BoxCollider2D>(true))
        {
            var t = col.transform;
            var scale = t.lossyScale;
            var size = new Vector2(col.size.x * Mathf.Abs(scale.x), col.size.y * Mathf.Abs(scale.y));
            var pos = (Vector2)t.position + col.offset; // offset is in local space; for axis-aligned it's fine
            Gizmos.DrawWireCube(new Vector3(pos.x, pos.y, 0f), new Vector3(size.x, size.y, 0f));
        }
    }
}
