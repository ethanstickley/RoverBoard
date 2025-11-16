using UnityEngine;

public class RectWorldGenerator : MonoBehaviour
{
    [Header("World Area (centered at 0,0)")]
    public Vector2 areaSize = new Vector2(80, 80);

    [Header("Colors")]
    public Color bgColor = new Color(1f, 1f, 1f, 1f);      // background “ground”
    public Color streetColor = new Color(0.92f, 0.92f, 0.92f, 1f);
    public Color sidewalkColor = new Color(0.85f, 0.85f, 0.85f, 1f);
    public Color buildingColor = new Color(0.75f, 0.78f, 0.82f, 1f);
    public Color shortObsColor = new Color(0.2f, 0.2f, 0.2f, 1f);
    public Color spawnPadColor = new Color(0.95f, 0.98f, 1f, 1f);

    [Header("Grid-ish Layout")]
    public int blocksX = 5;
    public int blocksY = 5;
    public float streetThickness = 2.0f;   // street gaps between “blocks”
    public float sidewalkInset = 1.2f;   // inner ring around block

    [Header("Buildings per Block")]
    public int minBuildingsPerBlock = 2;
    public int maxBuildingsPerBlock = 5;
    public Vector2 buildingMinSize = new Vector2(3, 3);
    public Vector2 buildingMaxSize = new Vector2(10, 8);

    [Header("Short Barriers (rails/fences/etc.)")]
    public int shortBarrierCount = 24;
    public Vector2 shortBarrierMinSize = new Vector2(1.2f, 0.4f);
    public Vector2 shortBarrierMaxSize = new Vector2(5.0f, 0.6f);

    [Header("Interactables")]
    public int ramps = 5;
    public int rails = 5;
    public int hydrants = 6;
    public GameObject rampPrefab;
    public GameObject railPrefab;
    public GameObject hydrantPrefab;
    public GameObject homePrefab;

    [Header("Layers (must exist)")]
    public string shortObstacleLayerName = "ShortObstacle";
    public string tallObstacleLayerName = "TallObstacle";
    public string interestLayerName = "Interest";

    [Header("Spawn Safety")]
    public Vector2 spawnCenter = Vector2.zero;
    public float spawnClearRadius = 4.0f;       // removes blocking colliders in this radius
    public bool drawSpawnPad = true;            // draws a visible pad under the spawn

    [Header("Randomness")]
    public int seed = 12345;

    void Start()
    {
        Random.InitState(seed);
        GenerateBackground();
        GenerateGridBlocksAndBuildings();
        GenerateShortBarriers();
        ScatterInteractables();

        // Make sure the starting area is clear of solid colliders
        ClearSpawnArea();

        // Optional: draw a visible pad so you can see the safe zone
        if (drawSpawnPad)
            CreateRect("SpawnPad", spawnCenter, Vector2.one * (spawnClearRadius * 1.8f), spawnPadColor, collidable: false, isTall: false, layerName: null);
    }

    // ====== World Pieces ======

    void GenerateBackground()
    {
        // background: NO collider
        CreateRect("BG", Vector2.zero, areaSize * 2f, bgColor, collidable: false, isTall: false, layerName: null);
    }

    void GenerateGridBlocksAndBuildings()
    {
        float totalW = areaSize.x * 2f;
        float totalH = areaSize.y * 2f;

        float cellW = totalW / blocksX;
        float cellH = totalH / blocksY;

        for (int gx = 0; gx < blocksX; gx++)
        {
            for (int gy = 0; gy < blocksY; gy++)
            {
                float x0 = -areaSize.x + gx * cellW + streetThickness * 0.5f;
                float y0 = -areaSize.y + gy * cellH + streetThickness * 0.5f;
                float w = cellW - streetThickness;
                float h = cellH - streetThickness;

                var blockCenter = new Vector2(x0 + w * 0.5f, y0 + h * 0.5f);
                var blockSize = new Vector2(w, h);

                // sidewalks: NO collider
                CreateRect($"Sidewalk_{gx}_{gy}", blockCenter, blockSize, sidewalkColor, false, false, null);

                // block core / street fill: NO collider
                var innerCenter = blockCenter;
                var innerSize = new Vector2(Mathf.Max(0.5f, w - sidewalkInset * 2f), Mathf.Max(0.5f, h - sidewalkInset * 2f));
                CreateRect($"BlockCore_{gx}_{gy}", innerCenter, innerSize, streetColor, false, false, null);

                // buildings: WITH collider (TallObstacle)
                int bcount = Random.Range(minBuildingsPerBlock, maxBuildingsPerBlock + 1);
                for (int i = 0; i < bcount; i++)
                {
                    Vector2 size = new Vector2(
                        Random.Range(buildingMinSize.x, buildingMaxSize.x),
                        Random.Range(buildingMinSize.y, buildingMaxSize.y)
                    );

                    float xMin = innerCenter.x - innerSize.x * 0.5f + size.x * 0.5f;
                    float xMax = innerCenter.x + innerSize.x * 0.5f - size.x * 0.5f;
                    float yMin = innerCenter.y - innerSize.y * 0.5f + size.y * 0.5f;
                    float yMax = innerCenter.y + innerSize.y * 0.5f - size.y * 0.5f;
                    if (xMin >= xMax || yMin >= yMax) break;

                    Vector2 pos = new Vector2(Random.Range(xMin, xMax), Random.Range(yMin, yMax));
                    var go = CreateRect($"Bld_{gx}_{gy}_{i}", pos, size, buildingColor, collidable: true, isTall: true, layerName: tallObstacleLayerName);

                    var obs = go.GetComponent<Obstacle2D>();
                    if (!obs) obs = go.AddComponent<Obstacle2D>();
                    obs.heightClass = Obstacle2D.HeightClass.Tall;
                }
            }
        }
    }

    void GenerateShortBarriers()
    {
        for (int i = 0; i < shortBarrierCount; i++)
        {
            Vector2 size = new Vector2(
                Random.Range(shortBarrierMinSize.x, shortBarrierMaxSize.x),
                Random.Range(shortBarrierMinSize.y, shortBarrierMaxSize.y)
            );
            Vector2 pos = RandInsideArea(size);

            var go = CreateRect($"ShortBarrier_{i}", pos, size, shortObsColor, collidable: true, isTall: false, layerName: shortObstacleLayerName);

            var obs = go.GetComponent<Obstacle2D>();
            if (!obs) obs = go.AddComponent<Obstacle2D>();
            obs.heightClass = Obstacle2D.HeightClass.Short;
        }
    }

    void ScatterInteractables()
    {
        if (homePrefab) Spawn(homePrefab, Vector2.zero);

        Scatter(rampPrefab, ramps, 0f, 360f);
        Scatter(railPrefab, rails, 0f, 360f, putOnLayer: interestLayerName);
        Scatter(hydrantPrefab, hydrants, 0f, 360f, putOnLayer: interestLayerName);
    }

    // ====== Spawn clearing ======

    void ClearSpawnArea()
    {
        // Remove ONLY non-trigger colliders in a circle (keeps Home/Hydrant/Rail triggers intact).
        var hits = Physics2D.OverlapCircleAll(spawnCenter, spawnClearRadius);
        foreach (var h in hits)
        {
            if (h.isTrigger) continue; // leave triggers
            // Only care about our generated solid obstacles
            if (h.gameObject.layer == LayerMask.NameToLayer(shortObstacleLayerName) ||
                h.gameObject.layer == LayerMask.NameToLayer(tallObstacleLayerName))
            {
                // safest: just disable the collider
                h.enabled = false;
                // (optional) you could also Destroy(h) or move it away if you prefer
            }
        }
    }

    // ====== Helpers ======

    // In RectWorldGenerator.cs, replace CreateRect with this version:
    GameObject CreateRect(string name, Vector2 center, Vector2 size, Color color, bool collidable, bool isTall, string layerName)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(center.x, center.y, isTall ? 0f : 0.01f);

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = RectSpriteFactory.WhiteSprite; // 1x1 world-unit sprite
        sr.color = color;
        sr.sortingOrder = isTall ? 1 : 0;

        // Scale drives BOTH the look and the collider’s world size
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        if (collidable)
        {
            var col = go.AddComponent<BoxCollider2D>();
            col.offset = Vector2.zero;
            col.size = Vector2.one;   // <-- IMPORTANT: unit size (scale does the rest)
        }

        if (!string.IsNullOrEmpty(layerName))
        {
            int l = LayerMask.NameToLayer(layerName);
            if (l >= 0) go.layer = l;
        }

        return go;
    }


    void Scatter(GameObject prefab, int count, float rotMin, float rotMax, string putOnLayer = null)
    {
        if (!prefab) return;
        for (int i = 0; i < count; i++)
        {
            Vector2 pos = RandInsideArea(Vector2.zero);
            float rot = Random.Range(rotMin, rotMax);
            var go = Spawn(prefab, pos, rot);
            if (!string.IsNullOrEmpty(putOnLayer))
            {
                int l = LayerMask.NameToLayer(putOnLayer);
                if (l >= 0) SetLayerRecursive(go, l);
            }
        }
    }

    GameObject Spawn(GameObject prefab, Vector2 pos, float rot = 0f)
    {
        return Instantiate(prefab, new Vector3(pos.x, pos.y, 0f), Quaternion.Euler(0, 0, rot), transform);
    }

    Vector2 RandInsideArea(Vector2 pad)
    {
        float x = Random.Range(-areaSize.x + pad.x, areaSize.x - pad.x);
        float y = Random.Range(-areaSize.y + pad.y, areaSize.y - pad.y);
        return new Vector2(x, y);
    }

    void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(new Vector3(spawnCenter.x, spawnCenter.y, 0f), spawnClearRadius);
        Gizmos.color = Color.gray;
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(areaSize.x * 2f, areaSize.y * 2f, 0));
    }
}