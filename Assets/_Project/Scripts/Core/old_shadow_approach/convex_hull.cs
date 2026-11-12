
//returns hulls, which is a list of hulls, where each hull is a list of points

using System.Collections.Generic;
using UnityEngine;


public class convex_hull : MonoBehaviour
{   
    private List<List<Vector2>> clusters;
    private clustering clustering;
    public List<List<Vector2>> hulls;
    public List<float> hull_distances;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        clustering = GetComponent<clustering>();
        hulls = new List<List<Vector2>>();
        hull_distances = new List<float>();
        
    }

    // Update is called once per frame
    void Update()
    {
        if (clustering == null)
            clustering = GetComponent<clustering>();
        if (clustering == null)
            return;

        if (hulls == null) hulls = new List<List<Vector2>>();
        if (hull_distances == null) hull_distances = new List<float>();
        clusters = clustering.clusters;
        hulls.Clear();
        hull_distances.Clear();

        if (clusters == null) return;

        for (int c = 0; c < clusters.Count; c++)
        {
            List<Vector2> cluster = clusters[c];
            if (cluster == null || cluster.Count < 3)
                continue;

      
            List<Vector2> pts = new List<Vector2>();
            for (int i = 0; i < cluster.Count; i++)
            {
                bool dup = false;
                for (int u = 0; u < pts.Count; u++)
                {
                    if ((cluster[i] - pts[u]).sqrMagnitude < 1e-8f)
                    {
                        dup = true;
                        break;
                    }
                }
                if (!dup) pts.Add(cluster[i]);
            }

            if (pts.Count < 3) continue;

            // pick bottom-left point as anchor
            Vector2 anchor = pts[0];
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].y < anchor.y || (Mathf.Approximately(pts[i].y, anchor.y) && pts[i].x < anchor.x))
                {
                    anchor = pts[i];
                }
            }

            // sort by polar angle using Atan2, breaking ties by distance
            pts.Remove(anchor);
            pts.Sort((a, b) =>
            {
                float angleA = Mathf.Atan2(a.y - anchor.y, a.x - anchor.x);
                float angleB = Mathf.Atan2(b.y - anchor.y, b.x - anchor.x);
                int cmp = angleA.CompareTo(angleB);
                if (cmp != 0) return cmp;
                float distA = (a - anchor).sqrMagnitude;
                float distB = (b - anchor).sqrMagnitude;
                return distA.CompareTo(distB);
            });
            
            List<Vector2> stack = new List<Vector2>();
            stack.Add(anchor);
            stack.Add(pts[0]);

            for (int j = 1; j < pts.Count; j++)
            {
               
                while (stack.Count >= 2 && anticw(stack[stack.Count - 2], stack[stack.Count - 1], pts[j]) != 1)
                {
                    stack.RemoveAt(stack.Count - 1);
                }
                stack.Add(pts[j]);
            }

            stack.Add(anchor);
            hulls.Add(stack);

            float dist = 0f;
            if (clustering.avg_dist != null && c < clustering.avg_dist.Count)
            {
                dist = clustering.avg_dist[c];
            }
            hull_distances.Add(dist);
        }
    }

    int anticw(Vector2 a, Vector2 b, Vector2 c)
    {
        float cross = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
        return cross > 0 ? 1 : 0;
    }
}
