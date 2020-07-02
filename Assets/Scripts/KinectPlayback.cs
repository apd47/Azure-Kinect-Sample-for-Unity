using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class KinectPlayback : MonoBehaviour
{
    public float defaultSecondsToBuffer = 3;
    public bool strictBuffering = false;

    protected KinectSource source;
    public MeshRenderer targetMesh;
    public KinectConfiguration kinectSettings;
    protected ComputeBuffer volume;

    protected float lastFrameTime = 0;

    public float frameDuration
    {
        get
        {
            return 1 / (float)KinectUtilities.FPStoInt(source.configuration.fps);
        }
    }

    public void UpdateVisualization()
    {
        if (source != null)
        {
            if (CheckAndMaintainBuffer(defaultSecondsToBuffer) != 0)
            {
                if (strictBuffering && Time.time - lastFrameTime >= frameDuration)
                {
                    print("Strict buffering is delaying frame playback");
                    lastFrameTime = Time.time;
                }
            }
            else
            {
                if (Time.time - lastFrameTime >= frameDuration)
                {
                    if (ApplyNextFrame(targetMesh, volume))
                    {
                        print("Frame Loaded");
                        lastFrameTime = Time.time;
                    }
                    else
                    {
                        print("BUFFER FAILURE: Current frame not yet decompressed -  increasing buffer depth and pausing for defaultSecondsToBuffer");
                        defaultSecondsToBuffer = defaultSecondsToBuffer + 0.2f;
                        lastFrameTime = Time.time + defaultSecondsToBuffer;
                    }
                }
            }
        }
    }

    public void ConfigureVisualization()
    {
        if (volume == null)
        {
            volume = new ComputeBuffer(source.matrixSize.x * source.matrixSize.y * source.matrixSize.z, 4 * sizeof(float), ComputeBufferType.Default);
        }
        float minDepthMM = (KinectUtilities.depthRanges[(int)source.configuration.depthMode].x * source.configuration.depthRangeModifier.x);
        float maxDepthMM = (KinectUtilities.depthRanges[(int)source.configuration.depthMode].y * source.configuration.depthRangeModifier.y);
        float worldscaleDepth = (maxDepthMM - minDepthMM) / 1000;

        targetMesh.transform.localPosition = new Vector3(0, 0, worldscaleDepth / 2);

        if (source.configuration.transformationMode == TransformationMode.DepthToColor)
            targetMesh.transform.localScale = new Vector3(1.6f * worldscaleDepth / 3, 0.9f * worldscaleDepth / 3, worldscaleDepth);
        if (source.configuration.transformationMode == TransformationMode.ColorToDepth)
            targetMesh.transform.localScale = new Vector3(worldscaleDepth / 3, worldscaleDepth / 3, worldscaleDepth);
    }

    public bool ApplyNextFrame(MeshRenderer targetMesh, ComputeBuffer targetBuffer)
    {
        if (source.frames.First == null)
            return false;

        KinectFrame currentFrame = source.frames.First.Value;
        if (currentFrame.frameState == KinectFrameState.Decompressed)
        {
            print(currentFrame.frameNumber);
            Material matt = targetMesh.material;
            targetBuffer.SetData(currentFrame.decompressedData);
            source.frames.RemoveFirst();
            matt.SetBuffer("colors", targetBuffer);
            matt.SetInt("_MatrixX", source.matrixSize.x);
            matt.SetInt("_MatrixY", source.matrixSize.y);
            matt.SetInt("_MatrixZ", source.matrixSize.z);
            return true;
        }
        else
        {
            return false;
        }

    }

    public int CheckAndMaintainBuffer(float secondsToBuffer)
    {
        LinkedListNode<KinectFrame> currentFrame = source.frames.First;

        int futureToCheck = (int)(secondsToBuffer / frameDuration);
        int notBuffered = 0;

        for (int i = 0; i < futureToCheck; i++)
        {
            if (currentFrame != null)
            {
                if (currentFrame.Value.frameState != KinectFrameState.Decompressed)
                {
                    notBuffered++;
                    //currentFrame.Value.Decompress();
                    new Thread(() => currentFrame.Value.Decompress()).Start();
                }
                currentFrame = currentFrame.Next;
            }
        }
        return notBuffered;
    }

    private void OnDestroy()
    {
        if (volume != null)
        {
            volume.Release();
            volume = null;
        }
    }

}
