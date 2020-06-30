using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WorldBufferPopulator : KinectProcessor
{
    public ComputeBuffer volumeBuffer;
    public ComputeShader shader;
    public Shader bufferShader;
    public MeshRenderer visualizer;
    Material bufferMaterialInstance;
    public Queue<float> framerates;

    public void Start()
    {
        framerates = new Queue<float>();
        float worldscaleDepth = (maxDepthMM - minDepthMM) / 1000;
        Vector3 currentScale = visualizer.transform.localScale;
        visualizer.transform.localScale = new Vector3(currentScale.x, currentScale.y, worldscaleDepth);
        visualizer.transform.localPosition = new Vector3(0, 0, 0.5f * worldscaleDepth);
        bufferMaterialInstance = visualizer.material;
        bufferMaterialInstance.shader = bufferShader;
    }

    public void Update()
    {
        this.transform.rotation = Quaternion.Euler(currentIMUAngles);
    }

    public override void ProcessKinectData()
    {
        if (volumeBuffer == null)
        {
            volumeBuffer = new ComputeBuffer(matrixSize.x * 2 * matrixSize.y * 2 * matrixSize.z * 2, 4 * sizeof(float), ComputeBufferType.Default);
        }

        // Apply Buffer Updates
        int kernelIndex = shader.FindKernel("ToBuffer");
        shader.SetInt("_MatrixX", matrixSize.x);
        shader.SetInt("_MatrixY", matrixSize.y);
        shader.SetInt("_MatrixZ", matrixSize.z);

        Quaternion now = Quaternion.Euler(currentIMUAngles);
        shader.SetVector("_IMUQuaternion", new Vector4(now.x, now.y, now.z, now.w)) ;

        shader.SetTexture(kernelIndex, "ColorTex", colorTexture);
        shader.SetTexture(kernelIndex, "DepthTex", depthTexture);
        shader.SetTexture(kernelIndex, "oldDepthTexture", oldDepthTexture);
        shader.SetBuffer(kernelIndex, "ResultBuffer", volumeBuffer);
        shader.SetInt("minDepth", minDepthMM);
        shader.SetInt("maxDepth", maxDepthMM);
        shader.Dispatch(kernelIndex, matrixSize.x / 16, matrixSize.y / 16, 1);

        bufferMaterialInstance.SetBuffer("colors", volumeBuffer);
        bufferMaterialInstance.SetInt("_MatrixX", matrixSize.x*2);
        bufferMaterialInstance.SetInt("_MatrixY", matrixSize.y*2);
        bufferMaterialInstance.SetInt("_MatrixZ", matrixSize.z*2);
    }

}
