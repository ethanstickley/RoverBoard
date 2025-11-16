using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// WorldSpawns2D — Block Grid with Non-Overlapping Sidewalk Strips (v2.4-compatible)
/// - Splits world into a tunable grid of BLOCKS separated by STREETS with SIDEWALK STRIPS (two per street).
/// - Streets render ABOVE sidewalks (configurable sorting orders).
/// - Buildings spawn inside blocks as solids (TallObstacle layer).
/// - Hydrants spawn ONLY on sidewalk strips, near the curb edge bordering the street.
/// - Cats avoid solids at spawn and can collide with them (Characters vs TallObstacle).
/// - Ramps/Rails/Hydrants/Treats are triggers.
/// - Seeded randomness option for deterministic layouts.
/// </summary>
public class WorldSpawns2D : MonoBehaviour
{
    // ---------- Parents ----------
    [Header("Parent Roots (optional)")]
    public Transform groundRoot;
    public Transform streetsRoot;
    public Transform sidewalksRoot;
    public Transform buildingsRoot;
    public Transform propsRoot;

    // ---------- Visual ----------
    [Header("Visual (Colors)")]
    public string sortingLayerName = "Ground";
    public int sortingOrder = 0; // base; not used by streets/sidewalks (we override below)
    public Color groundColor = new Color(0.95f, 0.95f, 0.95f, 1f);
    public Color streetColor = new Color(0.18f, 0.18f, 0.20f, 1f);
    public Color sidewalkColor = new Color(0.75f, 0.75f, 0.78f, 1f);
    public Color buildingColor = new Color(0.55f, 0.55f, 0.60f, 1f);

    [Header("Rendering Order")]
    [Tooltip("Higher draws on top. Streets should be above sidewalks.")]
    public int streetSortingOrder = 1;
    public int sidewalkSortingOrder = 0;

    // ---------- Prefabs ----------
    [Header("Prefabs (trigger-only where noted)")]
    public GameObject rampPrefab;          // trigger-only
    public GameObject railPrefab;          // trigger-only
    public GameObject hydrantPrefab;       // trigger
    public GameObject treatPrefab;         // trigger
    public GameObject catPrefab;           // collider recommended
    public GameObject carPrefab;           // solid/kinematic per prefab
    public GameObject tallObstaclePrefab;  // solid blockers (optional extras)

    // ---------- World & Grid ----------
    [Header("World Size & Block Grid")]
    public Vector2 worldSize = new Vector2(96, 96);

    [Tooltip("Number of block rows (vertical split).")]
    public int blockRows = 3;
    [Tooltip("Number of block columns (horizontal split).")]
    public int blockCols = 3;

    [Tooltip("Street core width (blacktop).")]
    public float streetWidth = 6f;
    [Tooltip("Sidewalk thickness on each side of a street (each strip width).")]
    public float sidewalkThickness = 1.5f;

    [Tooltip("Small jitter to break perfect symmetry of block rectangles (0–0.5).")]
    public float blockJitter = 0.3f;

    // ---------- Buildings ----------
    [Header("Buildings Inside Blocks")]
    [Tooltip("Attempts per block (more attempts → fuller blocks).")]
    public int buildingAttemptsPerBlock = 30;

    [Tooltip("Target fill (0..1) of each block with buildings; actual fill may be lower due to overlap avoidance.")]
    [Range(0f, 1f)] public float blockBuildingFillTarget = 0.45f;

    [Tooltip("Per-building width range.")]
    public Vector2 buildingWidthRange = new Vector2(2.0f, 6.0f);
    [Tooltip("Per-building height range.")]
    public Vector2 buildingHeightRange = new Vector2(2.0f, 6.0f);

    [Tooltip("Keep this much distance from sidewalks (setback) when placing buildings in a block.")]
    public float buildingSetbackFromSidewalk = 0.5f;

    [Tooltip("Extra random culling (chance to skip a candidate even if valid).")]
    [Range(0f, 1f)] public float buildingRandomCull = 0.15f;

    // ---------- Counts ----------
    [Header("Counts for Props")]
    public int rampCount = 8;
    public int railCount = 6;
    public int hydrantCount = 14;
    public int treatCount = 14;
    public int catCount = 5;
    public int carCount = 3;

    // ---------- Physics / Colliders ----------
    [Header("Physics / Colliders")]
    public bool collidersOnBuildings = true;
    public float buildingColliderInset = 0.05f;
    public bool collidersOnStreets = false;
    public bool collidersOnSidewalks = false;
    public bool collidersOnGround = false;
    public bool rampsAreTriggers = true;
    public bool railsAreTriggers = true;
    public bool hydrantsAreTriggers = true;

    // ---------- Layers / Tags ----------
    [Header("Layer/Tag Hygiene")]
    public string solidLayerName = "TallObstacle";  // buildings / blockers
    public string triggerLayerName = "Triggers";    // ramps/rails/hydrants/treats
    public string charactersLayerName = "Characters";
    public string generatedTag = "Generated";

    // ---------- Hydrants (curb-side) ----------
    [Header("Hydrants (curb-side on strips)")]
    [Tooltip("Offset measured from the curb (inner edge of the sidewalk strip) into the strip.")]
    public float hydrantCurbOffset = 0.35f;
    [Tooltip("Small jitter perpendicular to curb to avoid a perfect line.")]
    public float hydrantCurbJitter = 0.15f;

    // ---------- Cats / Spawn Rules ----------
    [Header("Cats / Spawn Rules")]
    [Tooltip("Ensure cats spawn outside building colliders.")]
    public bool preventCatsSpawningInsideBuildings = true;
    [Tooltip("Force cat colliders to be solid and collide with TallObstacle.")]
    public bool enforceCatsCollideWithBuildings = true;

    // ---------- Seeded Randomness ----------
    [Header("Seeded Randomness")]
    public bool useFixedSeed = true;
    public int seed = 12345;

    // ---------- Debug ----------
    [Header("Debug")]
    public bool drawGizmos = false;
    public Color gizmoColorSolid = new Color(1, 0, 0, 0.2f);

    // ---------- Internals ----------
    readonly List<GameObject> _spawned = new List<GameObject>();
    static Sprite _whiteTile;
    System.Random _rng;
    Rect _worldRect;

    struct SidewalkStrip
    {
        public Rect r;
        public bool horizontal; // true = horizontal strip (runs along X); false = vertical strip
        public float innerEdge; // y (if horizontal) or x (if vertical) coordinate that borders the street
    }

    List<Rect> _streetRects = new List<Rect>();
    List<SidewalkStrip> _sidewalkStrips = new List<SidewalkStrip>();
    List<Rect> _blockRects = new List<Rect>();
    List<Rect> _buildingRects = new List<Rect>();

    // ----------------- Lifecycle -----------------

    void Start()
    {
        EnsureWhiteTile();
        if (_spawned.Count == 0) RebuildWorld();
    }

    [ContextMenu("Rebuild World (Clear & Generate)")]
    public void RebuildWorld()
    {
        InitPRNG();
        ClearAll();

        _worldRect = new Rect(-worldSize.x * 0.5f, -worldSize.y * 0.5f, worldSize.x, worldSize.y);

        SpawnGround();

        BuildGridStreetsAndSidewalkStrips();
        SpawnSidewalksThenStreets(); // sidewalks first, streets on top

        BuildBlocksFromGrid();
        FillBlocksWithBuildings();
        SpawnBuildings();

        ScatterProps();
    }

    void InitPRNG()
    {
        _rng = useFixedSeed ? new System.Random(seed)
                            : new System.Random(UnityEngine.Random.Range(int.MinValue, int.MaxValue));
    }

    void ClearAll()
    {
        foreach (var go in _spawned) if (go) DestroyImmediate(go);
        _spawned.Clear();

        _streetRects.Clear();
        _sidewalkStrips.Clear();
        _blockRects.Clear();
        _buildingRects.Clear();
    }

    // ----------------- Stage 0: Ground -----------------
    void SpawnGround()
    {
        var ground = SpawnRectColor("Ground", worldSize, Vector2.zero, groundColor, groundRoot, collidersOnGround, false);
        _spawned.Add(ground);
    }

    // ----------------- Stage 1: Streets + Sidewalk STRIPS -----------------
    void BuildGridStreetsAndSidewalkStrips()
    {
        float totalW = worldSize.x;
        float totalH = worldSize.y;
        float cellW = totalW / Mathf.Max(1, blockCols);
        float cellH = totalH / Mathf.Max(1, blockRows);

        float halfStreet = streetWidth * 0.5f;
        float sw = Mathf.Max(0f, sidewalkThickness);

        // Vertical streets (between columns)
        for (int c = 1; c < blockCols; c++)
        {
            float x = -totalW * 0.5f + c * cellW + J(blockJitter);

            // Street core
            Rect street = new Rect(x - halfStreet, -totalH * 0.5f, streetWidth, totalH);
            _streetRects.Add(street);

            // Left sidewalk strip (west of street)
            Rect leftStrip = new Rect(street.xMin - sw, -totalH * 0.5f, sw, totalH);
            // Right sidewalk strip (east of street)
            Rect rightStrip = new Rect(street.xMax, -totalH * 0.5f, sw, totalH);

            // Add strips with inner edges
            _sidewalkStrips.Add(new SidewalkStrip { r = leftStrip, horizontal = false, innerEdge = leftStrip.xMax }); // inner edge at xMax (touches street)
            _sidewalkStrips.Add(new SidewalkStrip { r = rightStrip, horizontal = false, innerEdge = rightStrip.xMin }); // inner edge at xMin
        }

        // Horizontal streets (between rows)
        for (int r = 1; r < blockRows; r++)
        {
            float y = -totalH * 0.5f + r * cellH + J(blockJitter);

            Rect street = new Rect(-totalW * 0.5f, y - halfStreet, totalW, streetWidth);
            _streetRects.Add(street);

            // Bottom sidewalk strip (south of street)
            Rect bottomStrip = new Rect(-totalW * 0.5f, street.yMin - sw, totalW, sw);
            // Top sidewalk strip (north of street)
            Rect topStrip = new Rect(-totalW * 0.5f, street.yMax, totalW, sw);

            _sidewalkStrips.Add(new SidewalkStrip { r = bottomStrip, horizontal = true, innerEdge = bottomStrip.yMax }); // inner edge at yMax
            _sidewalkStrips.Add(new SidewalkStrip { r = topStrip, horizontal = true, innerEdge = topStrip.yMin });   // inner edge at yMin
        }
    }

    void SpawnSidewalksThenStreets()
    {
        // Sidewalks (lower order)
        foreach (var s in _sidewalkStrips)
        {
            Rect r = s.r;
            var go = SpawnRectColorWithOrder(
                "Sidewalk",
                new Vector2(r.width, r.height),
                r.center,
                sidewalkColor,
                sidewalksRoot,
                collidersOnSidewalks,
                false,
                0f,
                sidewalkSortingOrder
            );
            _spawned.Add(go);
        }

        // Streets (draw on top)
        foreach (var r in _streetRects)
        {
            var go = SpawnRectColorWithOrder(
                "Street",
                new Vector2(r.width, r.height),
                r.center,
                streetColor,
                streetsRoot,
                collidersOnStreets,
                false,
                0f,
                streetSortingOrder
            );
            _spawned.Add(go);
        }
    }

    // ----------------- Stage 2: Blocks (areas between streets) -----------------
    void BuildBlocksFromGrid()
    {
        float totalW = worldSize.x;
        float totalH = worldSize.y;
        float cellW = totalW / Mathf.Max(1, blockCols);
        float cellH = totalH / Mathf.Max(1, blockRows);

        float totalBandX = streetWidth + 2f * sidewalkThickness; // full band taken up at each internal vertical line
        float totalBandY = streetWidth + 2f * sidewalkThickness; // full band taken up at each internal horizontal line

        for (int r = 0; r < blockRows; r++)
        {
            for (int c = 0; c < blockCols; c++)
            {
                Rect cell = new Rect(-totalW * 0.5f + c * cellW, -totalH * 0.5f + r * cellH, cellW, cellH);

                float leftInset = (c > 0) ? (totalBandX * 0.5f) : 0f;
                float rightInset = (c < blockCols - 1) ? (totalBandX * 0.5f) : 0f;
                float bottomInset = (r > 0) ? (totalBandY * 0.5f) : 0f;
                float topInset = (r < blockRows - 1) ? (totalBandY * 0.5f) : 0f;

                Rect block = new Rect(
                    cell.xMin + leftInset + J(blockJitter),
                    cell.yMin + bottomInset + J(blockJitter),
                    cell.width - (leftInset + rightInset) - JAbs(blockJitter * 0.5f),
                    cell.height - (bottomInset + topInset) - JAbs(blockJitter * 0.5f)
                );

                block = ClipRectToWorld(block);
                if (block.width > 0.8f && block.height > 0.8f)
                    _blockRects.Add(block);
            }
        }
    }

    // ----------------- Stage 3: Buildings -----------------
    void FillBlocksWithBuildings()
    {
        foreach (var block in _blockRects)
        {
            float blockArea = block.width * block.height;
            float targetArea = Mathf.Clamp01(blockBuildingFillTarget) * blockArea;
            float placedArea = 0f;

            int attempts = Mathf.Max(1, buildingAttemptsPerBlock);
            int i = 0;

            Rect safe = Inflate(block, -buildingSetbackFromSidewalk);
            List<Rect> locals = new List<Rect>();

            while (i < attempts && placedArea < targetArea)
            {
                i++;

                float w = SnapQuarter(RandomRange(buildingWidthRange.x, buildingWidthRange.y));
                float h = SnapQuarter(RandomRange(buildingHeightRange.x, buildingHeightRange.y));

                Vector2 center = new Vector2(
                    RandomRange(safe.xMin + w * 0.5f, safe.xMax - w * 0.5f),
                    RandomRange(safe.yMin + h * 0.5f, safe.yMax - h * 0.5f)
                );

                Rect cand = RectFromCenter(center, w, h);
                if (IntersectsAny(cand, locals)) continue;
                if (RandomValue() < buildingRandomCull) continue;

                locals.Add(cand);
                _buildingRects.Add(cand);
                placedArea += w * h;
            }
        }
    }

    void SpawnBuildings()
    {
        foreach (var r in _buildingRects)
        {
            var go = SpawnRectColor("Building", new Vector2(r.width, r.height), r.center, buildingColor, buildingsRoot, collidersOnBuildings, true, buildingColliderInset);
            _spawned.Add(go);
        }
    }

    // ----------------- Stage 4: Props -----------------
    void ScatterProps()
    {
        SpawnManyTrigger(rampPrefab, rampCount, rampsAreTriggers, propsRoot);
        SpawnManyTrigger(railPrefab, railCount, railsAreTriggers, propsRoot);

        // HYDRANTS → sidewalk strips only; position near curb (inner edge of strip)
        if (hydrantPrefab && hydrantCount > 0 && _sidewalkStrips.Count > 0)
        {
            for (int i = 0; i < hydrantCount; i++)
            {
                Vector2 pos = RandomOnSidewalkCurbFromStrips();

                var go = Instantiate(hydrantPrefab, pos, Quaternion.identity, propsRoot);
                go.tag = string.IsNullOrEmpty(generatedTag) ? go.tag : generatedTag;
                _spawned.Add(go);

                var col = go.GetComponent<Collider2D>();
                if (!col) col = go.AddComponent<CircleCollider2D>();
                col.isTrigger = true;

                ApplyLayerIfValid(go, triggerLayerName);
            }
        }

        // TREATS
        SpawnManyTrigger(treatPrefab, treatCount, true, propsRoot);

        // CATS → avoid solids at spawn; collide with solids if desired
        if (catPrefab && catCount > 0)
        {
            for (int i = 0; i < catCount; i++)
            {
                Vector2 pos = RandomNotInsideSolids(100);
                var go = Instantiate(catPrefab, pos, Quaternion.identity, propsRoot);
                go.tag = string.IsNullOrEmpty(generatedTag) ? go.tag : generatedTag;
                _spawned.Add(go);

                ApplyLayerIfValid(go, charactersLayerName);

                if (enforceCatsCollideWithBuildings)
                {
                    var col = go.GetComponent<Collider2D>();
                    if (!col) col = go.AddComponent<CircleCollider2D>();
                    col.isTrigger = false;

                    var rb = go.GetComponent<Rigidbody2D>();
                    if (!rb) rb = go.AddComponent<Rigidbody2D>();
                    rb.gravityScale = 0f;
                    rb.constraints = RigidbodyConstraints2D.FreezeRotation;
                }
            }
        }

        // CARS / Tall obstacles (optional sprinkle)
        SpawnMany(carPrefab, carCount, false, propsRoot, applySolidLayer: true);
        if (tallObstaclePrefab) SpawnMany(tallObstaclePrefab, Mathf.Max(0, _buildingRects.Count / 10), false, propsRoot, applySolidLayer: true);
    }

    // ----------------- Spawners & helpers -----------------
    GameObject SpawnRectColor(string name, Vector2 size, Vector2 center, Color tint,
                              Transform parent, bool addCollider, bool solid, float inset = 0f)
    {
        var go = new GameObject(name);
        if (parent) go.transform.SetParent(parent, false);
        go.transform.position = center;
        go.tag = string.IsNullOrEmpty(generatedTag) ? go.tag : generatedTag;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = _whiteTile;
        sr.drawMode = SpriteDrawMode.Tiled;
        sr.size = size;
        sr.color = tint;
        sr.sortingLayerName = sortingLayerName;
        sr.sortingOrder = sortingOrder;

        if (addCollider)
        {
            var bc = go.AddComponent<BoxCollider2D>();
            bc.isTrigger = !solid;

            Vector2 colSize = size - new Vector2(inset * 2f, inset * 2f);
            colSize.x = Mathf.Max(0.01f, colSize.x);
            colSize.y = Mathf.Max(0.01f, colSize.y);
            bc.size = colSize;

            if (solid) ApplyLayerIfValid(go, solidLayerName);
            else ApplyLayerIfValid(go, triggerLayerName);
        }
        else
        {
            ApplyLayerIfValid(go, solidLayerName);
        }

        return go;
    }

    GameObject SpawnRectColorWithOrder(string name, Vector2 size, Vector2 center, Color tint,
                                       Transform parent, bool addCollider, bool solid, float inset,
                                       int sortingOrderOverride)
    {
        var go = SpawnRectColor(name, size, center, tint, parent, addCollider, solid, inset);
        var sr = go.GetComponent<SpriteRenderer>();
        if (sr) sr.sortingOrder = sortingOrderOverride;
        return go;
    }

    void SpawnManyTrigger(GameObject prefab, int count, bool forceTrigger, Transform parent)
    {
        if (!prefab || count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            var pos = RandomInsideWorld();
            var go = Instantiate(prefab, pos, Quaternion.identity, parent);
            go.tag = string.IsNullOrEmpty(generatedTag) ? go.tag : generatedTag;
            _spawned.Add(go);

            if (forceTrigger)
            {
                var col = go.GetComponent<Collider2D>();
                if (!col) col = go.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
            }

            ApplyLayerIfValid(go, triggerLayerName);
        }
    }

    void SpawnMany(GameObject prefab, int count, bool asTrigger, Transform parent, bool applySolidLayer = false)
    {
        if (!prefab || count <= 0) return;
        for (int i = 0; i < count; i++)
        {
            var pos = RandomInsideWorld();
            var go = Instantiate(prefab, pos, Quaternion.identity, parent);
            go.tag = string.IsNullOrEmpty(generatedTag) ? go.tag : generatedTag;
            _spawned.Add(go);

            if (asTrigger)
            {
                var col = go.GetComponent<Collider2D>();
                if (!col) col = go.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
                ApplyLayerIfValid(go, triggerLayerName);
            }
            else if (applySolidLayer)
            {
                ApplyLayerIfValid(go, solidLayerName);
            }
        }
    }

    // ----------------- Random helpers (seed-aware) -----------------
    float RandomValue() => useFixedSeed ? (float)_rng.NextDouble() : UnityEngine.Random.value;
    float RandomRange(float min, float max) => useFixedSeed ? (min + (float)_rng.NextDouble() * (max - min)) : Random.Range(min, max);
    float J(float mag) => (RandomValue() * 2f - 1f) * mag; // jitter
    float JAbs(float mag) => Mathf.Abs(J(mag));
    float SnapQuarter(float v) => Mathf.Round(v * 4f) * 0.25f;

    // Missing helpers previously referenced:
    Vector2 RandomInsideWorld() => RandomInsideWorld(1f);
    Vector2 RandomInsideWorld(float edgeMargin) => RandomInsideWorld(new Vector2(edgeMargin, edgeMargin));
    Vector2 RandomInsideWorld(Vector2 margin) =>
        new Vector2(
            RandomRange(_worldRect.xMin + margin.x, _worldRect.xMax - margin.x),
            RandomRange(_worldRect.yMin + margin.y, _worldRect.yMax - margin.y)
        );

    // ----------------- Geometry utils -----------------
    static Rect ClipRectToWorld(Rect r) => r; // world already centered; callers clamp if needed
    static Rect RectFromCenter(Vector2 center, float width, float height)
        => new Rect(center.x - width * 0.5f, center.y - height * 0.5f, width, height);
    static Rect Inflate(Rect r, float delta)
        => new Rect(r.xMin + delta, r.yMin + delta, r.width - 2f * delta, r.height - 2f * delta);
    static bool Intersects(Rect a, Rect b)
        => a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;
    static bool IntersectsAny(Rect r, List<Rect> rs)
    {
        for (int i = 0; i < rs.Count; i++) if (Intersects(r, rs[i])) return true;
        return false;
    }

    // ----------------- Hydrant curb sampling on strips -----------------
    Vector2 RandomOnSidewalkCurbFromStrips()
    {
        var s = _sidewalkStrips[useFixedSeed ? _rng.Next(0, _sidewalkStrips.Count) : Random.Range(0, _sidewalkStrips.Count)];
        Rect r = s.r;

        float offset = Mathf.Min(hydrantCurbOffset + JAbs(hydrantCurbJitter), s.horizontal ? r.height : r.width);
        offset = Mathf.Max(0.02f, offset); // keep inside the strip

        if (s.horizontal)
        {
            // runs along X; curb is at y = innerEdge
            bool innerIsMin = Mathf.Abs(s.innerEdge - r.yMin) < 1e-4f;
            float y = innerIsMin ? s.innerEdge + offset : s.innerEdge - offset;
            float x = RandomRange(r.xMin, r.xMax);
            return new Vector2(x, Mathf.Clamp(y, r.yMin, r.yMax));
        }
        else
        {
            // runs along Y; curb is at x = innerEdge
            bool innerIsMin = Mathf.Abs(s.innerEdge - r.xMin) < 1e-4f;
            float x = innerIsMin ? s.innerEdge + offset : s.innerEdge - offset;
            float y = RandomRange(r.yMin, r.yMax);
            return new Vector2(Mathf.Clamp(x, r.xMin, r.xMax), y);
        }
    }

    // ----------------- Solids spawn constraints -----------------
    Vector2 RandomNotInsideSolids(int maxTries)
    {
        for (int i = 0; i < maxTries; i++)
        {
            Vector2 p = RandomInsideWorld();
            if (!IsPointInsideSolid(p)) return p;
        }
        return Vector2.zero; // fallback
    }

    bool IsPointInsideSolid(Vector2 p)
    {
        if (string.IsNullOrEmpty(solidLayerName)) return false;
        int solidLayer = LayerMask.NameToLayer(solidLayerName);
        if (solidLayer < 0) return false;

        int mask = 1 << solidLayer;
        Collider2D hit = Physics2D.OverlapBox(p, new Vector2(0.2f, 0.2f), 0f, mask);
        return hit != null;
    }

    // ----------------- Gizmos -----------------
    void OnDrawGizmos()
    {
        if (!drawGizmos) return;
        Gizmos.color = gizmoColorSolid;
        Gizmos.DrawWireCube(transform.position, new Vector3(worldSize.x, worldSize.y, 0));
    }

    // ----------------- White tile sprite -----------------
    void EnsureWhiteTile()
    {
        if (_whiteTile) return;

        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
        tex.filterMode = FilterMode.Point;
        var pixels = new Color[4];
        for (int i = 0; i < 4; i++) pixels[i] = Color.white;
        tex.SetPixels(pixels);
        tex.Apply(false, true);

        _whiteTile = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                   new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        _whiteTile.name = "RuntimeWhiteTile";
    }

    // ----------------- Utility -----------------
    void ApplyLayerIfValid(GameObject go, string layerName)
    {
        if (string.IsNullOrEmpty(layerName)) return;
        int idx = LayerMask.NameToLayer(layerName);
        if (idx >= 0 && idx <= 31) go.layer = idx;
    }
}
