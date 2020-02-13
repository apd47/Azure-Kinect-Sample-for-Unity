using System.Collections;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent (typeof(MeshFilter), typeof(MeshRenderer))]
public class FeedbackRendering3DTest : MonoBehaviour {

    [SerializeField, Range(16, 256)] protected int width = 128, height = 64, depth = 128;
	

    #region Shader property keys

    protected const string kColorReadKey = "_ColorRead", kColorWriteKey = "_ColorWrite";

    #endregion

    protected new Renderer renderer;
    protected MaterialPropertyBlock block;

	protected void Start () {
        renderer = GetComponent<Renderer>();
        var mesh = Build(width, height, depth);
        GetComponent<MeshFilter>().sharedMesh = mesh;
	}
	
	protected void Update () {

	}

    protected void OnDestroy()
    {
    }


    protected Mesh Build(int width, int height, int depth)
    {
        var mesh = new Mesh();
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        var inv = new Vector3(1f / width, 1f / height, 1f / depth);
        var offset = -new Vector3(0.5f, 0.5f, 0.5f);
        for(int z = 0; z < depth; z++)
        {
            for(int y = 0; y < height; y++)
            {
                for(int x = 0; x < width; x++)
                {
                    var p = new Vector3(x, y, z);
                    indices.Add(vertices.Count);
                    vertices.Add(Vector3.Scale(p, inv) + offset);
                }
            }
        }
        mesh.SetVertices(vertices);
        mesh.indexFormat = vertices.Count < 65535 ? IndexFormat.UInt16 : IndexFormat.UInt32;
        mesh.SetIndices(indices.ToArray(), MeshTopology.Points, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.white;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
    }

}
