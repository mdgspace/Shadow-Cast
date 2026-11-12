using System.Collections.Generic;
using UnityEngine;

public class object_manager : MonoBehaviour
{
    [Header("wall coordinates")]
    
    public MeshFilter wall;

    [Header("lights")]  

    public List<Light> lights;

    [Header("objects")]
   
    public List<MeshFilter> objects = new List<MeshFilter>();

  


}