using System.Collections.Generic;
using System.Linq;
using UnityEngine;






public class clustering : MonoBehaviour
{

    class points
        {
            public int index;
            public int visited;
            public List<int> neighbours;
            
        }
    
    private ray_cast raycast;
    public float max_dist= 0.5f;
    public int min_neighbors = 2;

    private List<Vector2> shadow_pts;
    private List<float> shadow_distances;
    public List<Vector2> current_cluster = new List<Vector2>();

    public List<float> current_distances = new List<float>();
    public List<List<Vector2>> clusters;
    List<points> distances = new List<points>();

    public List <float> avg_dist = new List<float>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        raycast = GetComponent<ray_cast>();
        shadow_pts = new List<Vector2>();
        shadow_distances = new List<float>();
        clusters =  new List<List<Vector2>>();
        
    }




    // Update is called once per frame
    void FixedUpdate()
    {   
        if (raycast == null)
            raycast = GetComponent<ray_cast>();
        if (raycast == null)
            return;

        if (shadow_pts == null) shadow_pts = new List<Vector2>();
        if (shadow_distances == null) shadow_distances = new List<float>();
        shadow_pts = raycast.shadow_points;
        shadow_distances = raycast.shadow_distances;
        if (shadow_pts == null)
            return;


        distances.Clear();
        clusters.Clear();
        avg_dist.Clear();

        int count = shadow_pts.Count;
        find_distances(0, count);


        foreach (points pt in distances)
            {
                if (pt.visited==0)
                    {
                        current_cluster.Clear();
                        current_distances.Clear();
                        new_cluster(pt);
                        
                        if (current_cluster.Count >= min_neighbors)
                            {
                                clusters.Add(new List<Vector2>(current_cluster));
                                if (current_distances.Count > 0)
                                    {
                                        avg_dist.Add(current_distances.Average());
                                    }
                                else
                                    {
                                        avg_dist.Add(0f);
                                    }
                            }
                    }
            }
        

    }






    void find_distances(int start_idx, int count)
        {
            for (int idx = 0; idx < count; idx++)
            {
                points point_struct = new points
                {
                    index = idx,
                    visited = 0,
                    neighbours = new List<int>()
                };
                for (int i = 0; i < count; i++)
                {
                    if (i != idx && (shadow_pts[idx] - shadow_pts[i]).magnitude < max_dist)
                    {
                        point_struct.neighbours.Add(i);
                    }
                }

                distances.Add(point_struct);
            }

        }

    
    void new_cluster(points pt)
        {
            pt.visited = 1;
            current_cluster.Add(shadow_pts[pt.index]);
            if (shadow_distances != null && pt.index < shadow_distances.Count)
            {
                current_distances.Add(shadow_distances[pt.index]);
            }
            foreach(int neighbor in pt.neighbours)
                {
                    points pt_new = distances[neighbor];
                    
                    if (pt_new.visited == 0)
                        {
                            new_cluster(pt_new);
                        }
                }
        }
}
