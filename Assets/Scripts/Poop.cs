using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Poop : MonoBehaviour
{
    [Tooltip("Prefab with PoopStreaksEmitter; spawns when the player rides over this poop.")]
    public GameObject poopStreaksPrefab;

    [Tooltip("Optional: destroy this Poop when triggered (prevents repeated spawning).")]
    public bool destroyOnHit = true;

    void Reset()
    {
        var col = GetComponent<Collider2D>();
        if (col) col.isTrigger = true; // act as a trigger volume
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerController2D>();
        if (!player) return;

        if (poopStreaksPrefab && player.boardVisual)
        {
            var go = Instantiate(poopStreaksPrefab, player.boardVisual.position, player.boardVisual.rotation);
            var em = go.GetComponent<PoopStreaksEmitter>();
            if (em)
            {
                em.target = player.boardVisual;    // follow the board
                em.emitDuration = 2.0f;           // smear for 2 seconds
                // tweakable: em.trailTime, em.offsetX, em.width, em.brownColor etc.
            }
        }

        if (destroyOnHit) Destroy(gameObject);
    }
}
