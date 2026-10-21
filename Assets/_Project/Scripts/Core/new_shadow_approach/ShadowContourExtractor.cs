using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// High-performance utility for converting greyscale shadow snapshot textures into
/// clean 2D polygonal contours and wall-space colliders.
/// Implements Marching Squares, Ramer-Douglas-Peucker simplification, and spatial mapping.
/// </summary>
public static class ShadowContourExtractor
{
    public struct ContourData
    {
        public List<Vector2> wallSpacePoints; // Wall-local 2D coordinates for PolygonCollider2D
        public List<Vector2> uvPoints;        // Normalized [0..1] texture coordinates
        public float averageIntensity;        // Average shadow intensity [0..1]
        public float softness;                // 0.0 = crisp edge, 1.0 = highly diffuse
        public float area;                    // Area in wall square meters
        public bool isCrisp;                  // Solid vs soft quicksand
    }

    private struct DirectedSegment
    {
        public Vector2 a;
        public Vector2 b;

        public DirectedSegment(Vector2 a, Vector2 b)
        {
            this.a = a;
            this.b = b;
        }
    }

    /// <summary>
    /// Converts a Texture2D snapshot into a normalized 2D shadow intensity grid [0..1],
    /// where 1.0 = darkest shadow, 0.0 = fully lit wall.
    /// </summary>
    public static float[,] BuildShadowIntensityGrid(Texture2D snapshot, bool autoNormalizeContrast = true)
    {
        if (snapshot == null) return new float[0, 0];

        int w = snapshot.width;
        int h = snapshot.height;
        float[,] grid = new float[w, h];
        Color[] pixels = snapshot.GetPixels();

        float minLuma = 1.0f;
        float maxLuma = 0.0f;

        // 1. Calculate luminance for each pixel
        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                Color c = pixels[rowOffset + x];
                // Standard Rec. 709 greyscale luminance
                float luma = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                grid[x, y] = luma;

                if (luma < minLuma) minLuma = luma;
                if (luma > maxLuma) maxLuma = luma;
            }
        }

        // 2. Invert and normalize: Lit surface -> 0.0, Dark shadow -> 1.0
        float lumaRange = Mathf.Max(0.001f, maxLuma - minLuma);
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float val = grid[x, y];
                if (autoNormalizeContrast && lumaRange > 0.05f)
                {
                    // Stretch contrast to full 0..1 range
                    float normLuma = Mathf.Clamp01((val - minLuma) / lumaRange);
                    grid[x, y] = 1.0f - normLuma;
                }
                else
                {
                    grid[x, y] = 1.0f - Mathf.Clamp01(val);
                }
            }
        }

        return grid;
    }

    /// <summary>
    /// Extracts both Crisp and Soft shadow contours from the shadow intensity grid.
    /// </summary>
    public static void ExtractAllShadowContours(
        float[,] grid,
        int width,
        int height,
        float crispThreshold,
        float softThreshold,
        float minPixelArea,
        float simplificationTolerance,
        Bounds wallBounds,
        bool isPlaneMesh,
        out List<ContourData> crispContours,
        out List<ContourData> softContours)
    {
        crispContours = new List<ContourData>();
        softContours = new List<ContourData>();

        if (grid == null || width < 4 || height < 4) return;

        // 1. Extract crisp core shadow contours (intensity >= crispThreshold)
        List<List<Vector2>> rawCrispUVs = MarchingSquares(grid, width, height, crispThreshold, true, 1.0f);
        for (int i = 0; i < rawCrispUVs.Count; i++)
        {
            List<Vector2> loop = ClampUVLoopToSnapshot(rawCrispUVs[i]);
            float area = ComputePolygonArea(loop) * width * height;
            if (area < minPixelArea) continue;

            List<Vector2> simplified = SimplifyPolygonRDP(loop, simplificationTolerance);
            if (simplified.Count < 3) continue;

            float avgIntensity = SampleAverageIntensityInsidePolygon(grid, width, height, simplified);
            List<Vector2> wallPts = MapUVToWallSpace(simplified, wallBounds, isPlaneMesh);

            crispContours.Add(new ContourData
            {
                wallSpacePoints = wallPts,
                uvPoints = simplified,
                averageIntensity = avgIntensity,
                softness = 0.0f,
                area = ComputePolygonArea(wallPts),
                isCrisp = true
            });
        }

        // 2. Extract soft shadow contours (softThreshold <= intensity < crispThreshold)
        // We create a band mask for soft penumbra regions
        float[,] softMask = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float val = grid[x, y];
                if (val >= softThreshold && val < crispThreshold)
                {
                    softMask[x, y] = 1.0f;
                }
                else
                {
                    softMask[x, y] = 0.0f;
                }
            }
        }

        List<List<Vector2>> rawSoftUVs = MarchingSquares(softMask, width, height, 0.5f, false, 0f);
        for (int i = 0; i < rawSoftUVs.Count; i++)
        {
            List<Vector2> loop = ClampUVLoopToSnapshot(rawSoftUVs[i]);
            float area = ComputePolygonArea(loop) * width * height;
            if (area < minPixelArea) continue;

            List<Vector2> simplified = SimplifyPolygonRDP(loop, simplificationTolerance);
            if (simplified.Count < 3) continue;

            float avgIntensity = SampleAverageIntensityInsidePolygon(grid, width, height, simplified);
            
            // Softness calculation:
            // If avgIntensity is near crispThreshold -> softness is low (near 0, slow sink)
            // If avgIntensity is near softThreshold -> softness is high (near 1, fast sink)
            float softness = Mathf.Clamp01((crispThreshold - avgIntensity) / Mathf.Max(0.001f, crispThreshold - softThreshold));

            List<Vector2> wallPts = MapUVToWallSpace(simplified, wallBounds, isPlaneMesh);

            softContours.Add(new ContourData
            {
                wallSpacePoints = wallPts,
                uvPoints = simplified,
                averageIntensity = avgIntensity,
                softness = softness,
                area = ComputePolygonArea(wallPts),
                isCrisp = false
            });
        }
    }

    /// <summary>
    /// Executes the Marching Squares algorithm to extract closed polygonal loops at a given iso-value threshold.
    /// </summary>
    private static List<List<Vector2>> MarchingSquares(
        float[,] grid,
        int width,
        int height,
        float threshold,
        bool interpolate,
        float maxClamp)
    {
        List<DirectedSegment> segments = new List<DirectedSegment>();

        float invW = 1.0f / (width - 1);
        float invH = 1.0f / (height - 1);

        // Include one virtual lit-cell border around the snapshot. Without this
        // border, a shadow touching the wall image edge produces an open contour
        // and is discarded by AssembleSegmentsIntoLoops. The virtual border makes
        // the contour close against the physical wall boundary instead.
        for (int y = -1; y < height; y++)
        {
            for (int x = -1; x < width; x++)
            {
                // Values outside the snapshot are lit (zero shadow intensity).
                float v0 = SampleGridOrLit(grid, width, height, x, y);         // Bottom-Left
                float v1 = SampleGridOrLit(grid, width, height, x + 1, y);     // Bottom-Right
                float v2 = SampleGridOrLit(grid, width, height, x + 1, y + 1); // Top-Right
                float v3 = SampleGridOrLit(grid, width, height, x, y + 1);     // Top-Left

                int mask = 0;
                if (v0 >= threshold) mask |= 1;
                if (v1 >= threshold) mask |= 2;
                if (v2 >= threshold) mask |= 4;
                if (v3 >= threshold) mask |= 8;

                if (mask == 0 || mask == 15) continue;

                // Corner UV positions
                Vector2 p0 = new Vector2(x * invW, y * invH);
                Vector2 p1 = new Vector2((x + 1) * invW, y * invH);
                Vector2 p2 = new Vector2((x + 1) * invW, (y + 1) * invH);
                Vector2 p3 = new Vector2(x * invW, (y + 1) * invH);

                // Edge crossings
                Vector2 e0 = interpolate ? Interpolate(p0, p1, v0, v1, threshold) : (p0 + p1) * 0.5f; // Bottom
                Vector2 e1 = interpolate ? Interpolate(p1, p2, v1, v2, threshold) : (p1 + p2) * 0.5f; // Right
                Vector2 e2 = interpolate ? Interpolate(p3, p2, v3, v2, threshold) : (p3 + p2) * 0.5f; // Top
                Vector2 e3 = interpolate ? Interpolate(p0, p3, v0, v3, threshold) : (p0 + p3) * 0.5f; // Left

                switch (mask)
                {
                    case 1:  segments.Add(new DirectedSegment(e3, e0)); break;
                    case 2:  segments.Add(new DirectedSegment(e0, e1)); break;
                    case 3:  segments.Add(new DirectedSegment(e3, e1)); break;
                    case 4:  segments.Add(new DirectedSegment(e1, e2)); break;
                    case 5:
                        // Saddle ambiguity: resolve using center average
                        float centerVal = (v0 + v1 + v2 + v3) * 0.25f;
                        if (centerVal >= threshold)
                        {
                            segments.Add(new DirectedSegment(e3, e2));
                            segments.Add(new DirectedSegment(e0, e1));
                        }
                        else
                        {
                            segments.Add(new DirectedSegment(e3, e0));
                            segments.Add(new DirectedSegment(e1, e2));
                        }
                        break;
                    case 6:  segments.Add(new DirectedSegment(e0, e2)); break;
                    case 7:  segments.Add(new DirectedSegment(e3, e2)); break;
                    case 8:  segments.Add(new DirectedSegment(e2, e3)); break;
                    case 9:  segments.Add(new DirectedSegment(e2, e0)); break;
                    case 10:
                        float centerVal2 = (v0 + v1 + v2 + v3) * 0.25f;
                        if (centerVal2 >= threshold)
                        {
                            segments.Add(new DirectedSegment(e0, e3));
                            segments.Add(new DirectedSegment(e2, e1));
                        }
                        else
                        {
                            segments.Add(new DirectedSegment(e2, e3));
                            segments.Add(new DirectedSegment(e0, e1));
                        }
                        break;
                    case 11: segments.Add(new DirectedSegment(e2, e1)); break;
                    case 12: segments.Add(new DirectedSegment(e1, e3)); break;
                    case 13: segments.Add(new DirectedSegment(e1, e0)); break;
                    case 14: segments.Add(new DirectedSegment(e0, e3)); break;
                }
            }
        }

        return AssembleSegmentsIntoLoops(segments);
    }

    private static float SampleGridOrLit(float[,] grid, int width, int height, int x, int y)
    {
        if (x < 0 || x >= width || y < 0 || y >= height)
            return 0.0f;

        return grid[x, y];
    }

    private static List<Vector2> ClampUVLoopToSnapshot(List<Vector2> loop)
    {
        List<Vector2> clamped = new List<Vector2>(loop.Count);
        for (int i = 0; i < loop.Count; i++)
        {
            clamped.Add(new Vector2(
                Mathf.Clamp01(loop[i].x),
                Mathf.Clamp01(loop[i].y)));
        }

        return clamped;
    }

    private static Vector2 Interpolate(Vector2 pA, Vector2 pB, float valA, float valB, float threshold)
    {
        float diff = valB - valA;
        if (Mathf.Abs(diff) < 1e-6f) return (pA + pB) * 0.5f;
        float t = Mathf.Clamp01((threshold - valA) / diff);
        return Vector2.Lerp(pA, pB, t);
    }

    /// <summary>
    /// Chains directed segments into closed polygon loops.
    /// </summary>
    private static List<List<Vector2>> AssembleSegmentsIntoLoops(List<DirectedSegment> segments)
    {
        List<List<Vector2>> loops = new List<List<Vector2>>();
        if (segments == null || segments.Count == 0) return loops;

        const float joinTolerance = 0.003f;
        const float joinToleranceSqr = joinTolerance * joinTolerance;
        bool[] used = new bool[segments.Count];

        for (int i = 0; i < segments.Count; i++)
        {
            if (used[i]) continue;

            List<Vector2> loop = new List<Vector2>();
            used[i] = true;
            loop.Add(segments[i].a);
            loop.Add(segments[i].b);

            Vector2 currentEnd = segments[i].b;
            bool closed = false;

            // Follow chain
            for (int step = 0; step < segments.Count; step++)
            {
                int bestNext = -1;
                float bestDistSqr = float.MaxValue;

                for (int j = 0; j < segments.Count; j++)
                {
                    if (used[j]) continue;

                    float distSqr = (segments[j].a - currentEnd).sqrMagnitude;
                    if (distSqr < joinToleranceSqr && distSqr < bestDistSqr)
                    {
                        bestDistSqr = distSqr;
                        bestNext = j;
                    }
                }

                if (bestNext != -1)
                {
                    used[bestNext] = true;
                    currentEnd = segments[bestNext].b;
                    loop.Add(currentEnd);

                    // Check if looped back to start
                    if ((currentEnd - loop[0]).sqrMagnitude < joinToleranceSqr)
                    {
                        closed = true;
                        break;
                    }
                }
                else
                {
                    // Check if close enough to start to close loop (wider tolerance)
                    if ((currentEnd - loop[0]).sqrMagnitude < joinToleranceSqr * 16f)
                    {
                        closed = true;
                    }
                    break;
                }
            }

            if (closed && loop.Count >= 3)
            {
                // Remove redundant duplicate end vertex
                if ((loop[loop.Count - 1] - loop[0]).sqrMagnitude < joinToleranceSqr)
                {
                    loop.RemoveAt(loop.Count - 1);
                }
                loops.Add(loop);
            }
        }

        return loops;
    }

    /// <summary>
    /// Simplifies polygon vertices using the Ramer-Douglas-Peucker algorithm.
    /// </summary>
    public static List<Vector2> SimplifyPolygonRDP(List<Vector2> points, float tolerance)
    {
        if (points == null || points.Count <= 3 || tolerance <= 1e-6f)
            return new List<Vector2>(points);

        // Find two points furthest apart to split the closed loop into two open chains
        int startIndex = 0;
        int splitIndex = 0;
        float maxDistSqr = 0f;

        for (int i = 1; i < points.Count; i++)
        {
            float d = (points[i] - points[0]).sqrMagnitude;
            if (d > maxDistSqr)
            {
                maxDistSqr = d;
                splitIndex = i;
            }
        }

        // Chain 1: 0 -> splitIndex
        List<Vector2> chain1 = new List<Vector2>();
        for (int i = startIndex; i <= splitIndex; i++) chain1.Add(points[i]);

        // Chain 2: splitIndex -> 0
        List<Vector2> chain2 = new List<Vector2>();
        for (int i = splitIndex; i < points.Count; i++) chain2.Add(points[i]);
        chain2.Add(points[0]);

        List<Vector2> simplified1 = RDPRecursive(chain1, 0, chain1.Count - 1, tolerance);
        List<Vector2> simplified2 = RDPRecursive(chain2, 0, chain2.Count - 1, tolerance);

        List<Vector2> result = new List<Vector2>();
        for (int i = 0; i < simplified1.Count - 1; i++) result.Add(simplified1[i]);
        for (int i = 0; i < simplified2.Count - 1; i++) result.Add(simplified2[i]);

        return result.Count >= 3 ? result : points;
    }

    private static List<Vector2> RDPRecursive(List<Vector2> pts, int first, int last, float tolerance)
    {
        float maxDist = 0f;
        int index = first;
        Vector2 a = pts[first];
        Vector2 b = pts[last];

        for (int i = first + 1; i < last; i++)
        {
            float dist = PerpendicularDistance(pts[i], a, b);
            if (dist > maxDist)
            {
                maxDist = dist;
                index = i;
            }
        }

        List<Vector2> result = new List<Vector2>();
        if (maxDist > tolerance)
        {
            List<Vector2> rec1 = RDPRecursive(pts, first, index, tolerance);
            List<Vector2> rec2 = RDPRecursive(pts, index, last, tolerance);

            result.AddRange(rec1);
            result.RemoveAt(result.Count - 1);
            result.AddRange(rec2);
        }
        else
        {
            result.Add(a);
            result.Add(b);
        }

        return result;
    }

    private static float PerpendicularDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float mag = ab.magnitude;
        if (mag < 1e-6f) return Vector2.Distance(p, a);
        return Mathf.Abs((b.x - a.x) * (a.y - p.y) - (a.x - p.x) * (b.y - a.y)) / mag;
    }

    /// <summary>
    /// Computes the signed polygon area using the Shoelace formula.
    /// </summary>
    public static float ComputePolygonArea(List<Vector2> points)
    {
        if (points == null || points.Count < 3) return 0f;
        float area = 0f;
        int n = points.Count;
        for (int i = 0; i < n; i++)
        {
            Vector2 p1 = points[i];
            Vector2 p2 = points[(i + 1) % n];
            area += (p1.x * p2.y) - (p2.x * p1.y);
        }
        return Mathf.Abs(area * 0.5f);
    }

    /// <summary>
    /// Samples average shadow intensity within the UV polygon.
    /// </summary>
    private static float SampleAverageIntensityInsidePolygon(float[,] grid, int w, int h, List<Vector2> polyUV)
    {
        if (polyUV == null || polyUV.Count < 3) return 0.5f;

        // Compute UV bounding box
        float minU = 1f, maxU = 0f, minV = 1f, maxV = 0f;
        for (int i = 0; i < polyUV.Count; i++)
        {
            Vector2 p = polyUV[i];
            if (p.x < minU) minU = p.x;
            if (p.x > maxU) maxU = p.x;
            if (p.y < minV) minV = p.y;
            if (p.y > maxV) maxV = p.y;
        }

        int startX = Mathf.Clamp(Mathf.FloorToInt(minU * (w - 1)), 0, w - 1);
        int endX = Mathf.Clamp(Mathf.CeilToInt(maxU * (w - 1)), 0, w - 1);
        int startY = Mathf.Clamp(Mathf.FloorToInt(minV * (h - 1)), 0, h - 1);
        int endY = Mathf.Clamp(Mathf.CeilToInt(maxV * (h - 1)), 0, h - 1);

        float sum = 0f;
        int count = 0;

        for (int y = startY; y <= endY; y++)
        {
            float v = (float)y / (h - 1);
            for (int x = startX; x <= endX; x++)
            {
                float u = (float)x / (w - 1);
                if (IsPointInPolygon(new Vector2(u, v), polyUV))
                {
                    sum += grid[x, y];
                    count++;
                }
            }
        }

        return count > 0 ? (sum / count) : 0.5f;
    }

    private static bool IsPointInPolygon(Vector2 pt, List<Vector2> poly)
    {
        bool inside = false;
        int n = poly.Count;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (((poly[i].y > pt.y) != (poly[j].y > pt.y)) &&
                (pt.x < (poly[j].x - poly[i].x) * (pt.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x))
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Maps normalized [0..1] UV coordinates to Wall-Local 2D coordinates.
    /// Handles both horizontal Planes (X, Z) and vertical Quads/MRUK walls (X, Y).
    /// </summary>
    public static List<Vector2> MapUVToWallSpace(List<Vector2> uvPoints, Bounds wallBounds, bool isPlaneMesh)
    {
        List<Vector2> wallPts = new List<Vector2>(uvPoints.Count);

        Vector3 min = wallBounds.min;
        Vector3 max = wallBounds.max;

        for (int i = 0; i < uvPoints.Count; i++)
        {
            Vector2 uv = uvPoints[i];
            float localX = Mathf.Lerp(min.x, max.x, uv.x);
            float localY;

            if (isPlaneMesh)
            {
                // In horizontal Unity Planes, Z maps to vertical 2D wall Y
                localY = Mathf.Lerp(min.z, max.z, uv.y);
            }
            else
            {
                // In vertical Quads/MRUK walls, Y is vertical 2D wall Y
                localY = Mathf.Lerp(min.y, max.y, uv.y);
            }

            wallPts.Add(new Vector2(localX, localY));
        }

        return wallPts;
    }
}
