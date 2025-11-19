// RailPath2D.cs
// Define a 2D rail as a polyline using child point transforms.
// Now visualized with a LineRenderer (no Gizmos rendering needed).
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public class RailPath2D : MonoBehaviour
{
    public static readonly List<RailPath2D> AllRails = new List<RailPath2D>();

    [Header("Points")]
    [Tooltip("Ordered point transforms that define the rail path (children are convenient).")]
    public List<Transform> points = new List<Transform>();

    [Tooltip("Treat the path as a loop (optional).")]
    public bool loop = false;

    [Header("Renderer")]
    [Tooltip("LineRenderer used to draw the rail in the scene and game view.")]
    public LineRenderer line;

    [Tooltip("Width of the line in world units.")]
    public float lineWidth = 0.06f;

    [Tooltip("Material for the line (optional). If null, a default unlit material will be created in playmode.")]
    public Material lineMaterial;

    [Tooltip("Color (multiplies material).")]
    public Color lineColor = new Color(1f, 0.8f, 0.2f, 1f);

    [Tooltip("How many straight sub-steps to draw per path segment.")]
    [Min(1)] public int stepsPerSegment = 12;

    [Tooltip("If enabled, closes the loop visually and in traversal when 'loop' is true.")]
    public bool renderLoopWhenLoopEnabled = true;

    // Cached arc lengths (for normalized traversal)
    float _totalLen;
    readonly List<float> _cumLen = new List<float>(); // cumulative length per segment edge

    void Reset()
    {
        line = GetComponent<LineRenderer>();
        ConfigureLineDefaults();
    }

    void OnEnable()
    {
        if (!AllRails.Contains(this)) AllRails.Add(this);
        EnsureLine();
        RebuildLengths();
        RefreshLineRenderer();
    }

    void OnDisable()
    {
        AllRails.Remove(this);
    }

    void OnValidate()
    {
        EnsureLine();
        RebuildLengths();
        RefreshLineRenderer();
    }

    void Update()
    {
#if UNITY_EDITOR
        // Keep renderer live-updated in the editor when moving points around
        if (!Application.isPlaying)
        {
            RebuildLengths();
            RefreshLineRenderer();
        }
#endif
    }

    // ===== Public API =====
    public int PointCount => points != null ? points.Count : 0;
    public int SegmentCount => Mathf.Max(0, loop ? PointCount : PointCount - 1);

    public float TotalLength => _totalLen;

    public Vector2 GetPoint(int i)
    {
        if (points == null || points.Count == 0) return (Vector2)transform.position;
        i = Mathf.Clamp(i, 0, points.Count - 1);
        return points[i] ? (Vector2)points[i].position : (Vector2)transform.position;
    }

    public Vector2 GetPointAtT(float t)
    {
        // t in [0..1] along entire path (by arc length)
        if (SegmentCount <= 0) return GetPoint(0);
        t = Mathf.Clamp01(t);
        float targetLen = t * _totalLen;

        int segIndex = 0;
        while (segIndex < _cumLen.Count - 1 && _cumLen[segIndex + 1] < targetLen) segIndex++;

        float segStartLen = _cumLen[segIndex];
        float segLen = SegmentLength(segIndex);
        float local = segLen <= 1e-5f ? 0f : (targetLen - segStartLen) / segLen;

        Vector2 a = GetPoint(segIndex);
        Vector2 b = GetPoint(NextIndex(segIndex));
        return Vector2.Lerp(a, b, Mathf.Clamp01(local));
    }

    public Vector2 GetTangentAtT(float t)
    {
        if (SegmentCount <= 0) return Vector2.right;
        t = Mathf.Clamp01(t);
        float eps = 0.001f;
        Vector2 p0 = GetPointAtT(Mathf.Clamp01(t - eps));
        Vector2 p1 = GetPointAtT(Mathf.Clamp01(t + eps));
        Vector2 v = p1 - p0;
        if (v.sqrMagnitude < 1e-8f) return Vector2.right;
        return v.normalized;
    }

    public float FindClosestT(Vector2 worldPos, out Vector2 closestPoint)
    {
        closestPoint = GetPoint(0);
        if (SegmentCount <= 0) return 0f;

        float bestDist2 = float.PositiveInfinity;
        float bestTAlong = 0f;

        for (int i = 0; i < SegmentCount; i++)
        {
            Vector2 a = GetPoint(i);
            Vector2 b = GetPoint(NextIndex(i));
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float u = (len2 < 1e-8f) ? 0f : Vector2.Dot(worldPos - a, ab) / len2;
            u = Mathf.Clamp01(u);
            Vector2 q = a + u * ab;
            float d2 = (worldPos - q).sqrMagnitude;
            if (d2 < bestDist2)
            {
                bestDist2 = d2;
                closestPoint = q;
                float lenToSegStart = _cumLen[i];
                float segLen = Mathf.Max(1e-6f, (b - a).magnitude);
                float arc = lenToSegStart + segLen * u;
                bestTAlong = _totalLen <= 1e-6f ? 0f : arc / _totalLen;
            }
        }
        return Mathf.Clamp01(bestTAlong);
    }

    public float SegmentLength(int segIndex)
    {
        if (SegmentCount <= 0) return 0f;
        Vector2 a = GetPoint(segIndex);
        Vector2 b = GetPoint(NextIndex(segIndex));
        return (b - a).magnitude;
    }

    // ===== Internals =====
    int NextIndex(int i)
    {
        if (loop) return (i + 1) % PointCount;
        return Mathf.Min(i + 1, PointCount - 1);
    }

    void EnsureLine()
    {
        if (!line) line = GetComponent<LineRenderer>();
        ConfigureLineDefaults();
    }

    void ConfigureLineDefaults()
    {
        if (!line) return;

        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.textureMode = LineTextureMode.Stretch;
        line.widthMultiplier = Mathf.Max(0.001f, lineWidth);
        line.numCornerVertices = 2;
        line.numCapVertices = 2;

        // Material / color
        if (lineMaterial != null)
        {
            line.sharedMaterial = lineMaterial;
        }
        else
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // keep whatever is assigned in editor; user can assign a material
            }
            else
#endif
            {
                // Create a very basic default material at runtime if none provided
                if (line.sharedMaterial == null)
                {
                    var mat = new Material(Shader.Find("Sprites/Default"));
                    mat.name = "RailPath2D_AutoMaterial";
                    line.material = mat;
                }
            }
        }

        // Apply color (multiplies material tint if it supports it)
        line.startColor = lineColor;
        line.endColor = lineColor;
    }

    void RebuildLengths()
    {
        _cumLen.Clear();
        _cumLen.Add(0f);
        _totalLen = 0f;

        if (SegmentCount <= 0) return;

        for (int i = 0; i < SegmentCount; i++)
        {
            float seg = SegmentLength(i);
            _totalLen += seg;
            _cumLen.Add(_totalLen);
        }
    }

    void RefreshLineRenderer()
    {
        if (!line) return;

        // No points → clear
        if (PointCount == 0 || SegmentCount == 0)
        {
            line.positionCount = 0;
            return;
        }

        // Sample the path with stepsPerSegment per segment for a smooth curve-like polyline
        int segCount = SegmentCount;
        int samplesPerSeg = Mathf.Max(1, stepsPerSegment);

        // If loop rendering is desired, we render the closing segment too
        bool closeVisually = loop && renderLoopWhenLoopEnabled;

        int totalSegmentsToRender = segCount + (closeVisually ? 1 : 0);
        int totalSamples = totalSegmentsToRender * samplesPerSeg + 1;

        // Build the sample positions
        var positions = new List<Vector3>(totalSamples);
        for (int s = 0; s < totalSegmentsToRender; s++)
        {
            int a = s % PointCount;
            int b = NextIndex(a);
            Vector2 pa = GetPoint(a);
            Vector2 pb = GetPoint(b);

            for (int i = 0; i < samplesPerSeg; i++)
            {
                float t = (float)i / samplesPerSeg;
                positions.Add(Vector2.Lerp(pa, pb, t));
            }
        }

        // Add the very last point (end of last segment)
        if (closeVisually)
        {
            positions.Add(GetPoint(0)); // loop closure
        }
        else
        {
            positions.Add(GetPoint(segCount)); // end of last open segment
        }

        line.widthMultiplier = Mathf.Max(0.001f, lineWidth);
        line.startColor = lineColor;
        line.endColor = lineColor;
        line.positionCount = positions.Count;
        line.SetPositions(positions.ToArray());
    }

    // Public helper to force a redraw if you alter points at runtime
    public void ForceRefresh()
    {
        RebuildLengths();
        RefreshLineRenderer();
    }
}
