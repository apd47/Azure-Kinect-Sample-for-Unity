using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class KinectProcessor : MonoBehaviour
{
    [HideInInspector]
    public Vector3 currentIMUAngles;
    [HideInInspector]
    public Texture2D colorTexture;
    [HideInInspector]
    public Texture2D depthTexture;
    [HideInInspector]
    public Texture2D oldDepthTexture;
    [HideInInspector]
    public Vector3Int matrixSize;
    [HideInInspector]
    public int maxDepthMM = 10000;
    [HideInInspector]
    public int minDepthMM = 500;

    public abstract void ProcessKinectData();

    void OnDestroy()
    {
        Destroy(colorTexture);
        Destroy(depthTexture);
        Destroy(oldDepthTexture);
    }
}
