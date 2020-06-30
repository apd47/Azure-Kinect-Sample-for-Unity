using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class KinectLocalPlayback : MonoBehaviour
{
    public float defaultSecondsToBuffer = 3;
    public bool clearUsedFrames = false;
    public bool strictBuffering = false;

    public bool play;

    KinectLocalFile file;
    public MeshRenderer targetMesh;
    public KinectConfiguration kinectSettings;
    public string filepath;
    ComputeBuffer volume;

    int playheadFrame = 0;
    float lastFrameTime = 0;

    public void Update()
    {
        if (file != null && play)
        {
            if (!MaintainBuffer(playheadFrame, defaultSecondsToBuffer))
            {
                if (strictBuffering && Time.time - lastFrameTime >= (1 / (float)file.playbackFPS))
                {
                    print("Strict buffering is delaying frame playback");
                    lastFrameTime = Time.time;
                }
            }
            else
            {
                if (Time.time - lastFrameTime >= (1 / (float)file.playbackFPS))
                {
                    if (file.frames[playheadFrame] == null)
                    {
                        print("Invalid playhead location");
                    }
                    else
                    {
                        if (ApplyFrame(targetMesh, volume, file.frames[playheadFrame]))
                        {
                            playheadFrame = (playheadFrame + 1) % file.numberOfFrames;
                            lastFrameTime = Time.time;
                        }
                        else
                        {
                            print("BUFFER FAILURE: Current frame not yet decompressed -  increasing buffer depth ");
                            defaultSecondsToBuffer = defaultSecondsToBuffer + 0.2f;
                        }
                    }
                }
            }
        }
    }

    public void Load(string filepath, KinectConfiguration configuration, MeshRenderer targetMesh)
    {
        this.targetMesh = targetMesh;
        file = new KinectLocalFile(filepath, configuration);
        ConfigureVisualization();
        MaintainBuffer(0, defaultSecondsToBuffer);
    }

    void ConfigureVisualization()
    {
        if (volume == null)
        {
            volume = new ComputeBuffer(file.matrixSize.x * file.matrixSize.y * file.matrixSize.z, 4 * sizeof(float), ComputeBufferType.Default);
            print("Made Volume Buffer");
        }
        float minDepthMM = (KinectUtilities.depthRanges[(int)file.configuration.depthMode].x * file.configuration.depthRangeModifier.x);
        float maxDepthMM = (KinectUtilities.depthRanges[(int)file.configuration.depthMode].y * file.configuration.depthRangeModifier.y);
        float worldscaleDepth = (maxDepthMM - minDepthMM) / 1000;

        targetMesh.transform.localPosition = new Vector3(0, 0, worldscaleDepth / 2);

        if (file.configuration.transformationMode == TransformationMode.DepthToColor)
            targetMesh.transform.localScale = new Vector3(1.6f * worldscaleDepth / 3, 0.9f * worldscaleDepth / 3, worldscaleDepth);
        if (file.configuration.transformationMode == TransformationMode.ColorToDepth)
            targetMesh.transform.localScale = new Vector3(worldscaleDepth / 3, worldscaleDepth / 3, worldscaleDepth);
    }

    public bool ApplyFrame(MeshRenderer targetMesh, ComputeBuffer targetBuffer, KinectVolumeFrame frame)
    {
        if (frame.decompressed)
        {
            Material matt = targetMesh.material;
            targetBuffer.SetData(frame.decompressedData);
            if (clearUsedFrames)
            {
                frame.ReleaseDecompressedData();
            }
            matt.SetBuffer("colors", targetBuffer);
            matt.SetInt("_MatrixX", frame.sourceFile.matrixSize.x);
            matt.SetInt("_MatrixY", frame.sourceFile.matrixSize.y);
            matt.SetInt("_MatrixZ", frame.sourceFile.matrixSize.z);
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool MaintainBuffer(int currentFrame, float secondsToBuffer)
    {
        int futureToCheck = (int)(file.playbackFPS * secondsToBuffer);
        int stillCompressed = 0;
        for (int i = 0; i < futureToCheck; i++)
        {
            int indexToCheck = (currentFrame + i) % file.numberOfFrames;
            if (!file.frames[indexToCheck].decompressed)
            {
                stillCompressed++;
                new Thread(() => file.frames[indexToCheck].Decompress()).Start();
            }
        }
        if (stillCompressed > 0)
        {
            return false;
        }
        else
        {
            return true;
        }
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
