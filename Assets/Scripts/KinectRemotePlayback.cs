using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

public class KinectRemotePlayback : MonoBehaviour
{
<<<<<<< Updated upstream
    public float defaultSecondsToBuffer = 3;
    public bool clearUsedFrames = false;
    public bool strictBuffering = false;

=======
    public float MaxWaitSeconds = 1;
    public TransmissionMode transmissionMode;
>>>>>>> Stashed changes
    public string clientName;
    public string providerName;
    public string serverHostname;
    public int serverPort;

    KinectRemoteFile server;
    public MeshRenderer targetMesh;
    public KinectConfiguration kinectSettings;
    ComputeBuffer volume;

    int playheadFrame = 0;
    float lastFrameTime = 0;


    KinectSocketFrame lastFrame;


    public void Update()
    {
        if (server != null)
        {
            server.Listen();

            if (server.connectionState == KinectRemoteFile.ConnectionState.SubscribedToProvider)
            {
                if(volume == null)
                {
                    kinectSettings = server.configuration;
                    ConfigureVisualization();
                }
                if (server.frames.Last != null)
                {
                    server.frames.Last.Value.Decompress();
                    if (Time.time - lastFrameTime >= (1 / (float)server.playbackFPS))
                    {
                        if (ApplyFrame(targetMesh, volume, server.frames.First.Value))
                        {
                            server.frames.RemoveFirst();
                        }
                        else
                        {
                            print("Buffering...");
                            server.frames.First.Value.Decompress();
                        }
                        lastFrameTime = Time.time;
                    }
                }

                //print("hi");
                //if (MaintainBuffer(defaultSecondsToBuffer))
                //{
                //    if (Time.time - lastFrameTime >= (1 / (float)server.playbackFPS))
                //    {
                //        ApplyFrame(targetMesh, volume, server.frames.First.Value);
                //        server.frames.RemoveFirst();
                //    }
                //}
                //else
                //{
                //    print("Buffering...." + server.frames.Count);
                //    lastFrameTime = Time.time;
                //}
            }

        }
    }

    void ConfigureVisualization()
    {
        server.CalculateMatrixSize();
        if (volume == null)
        {
            volume = new ComputeBuffer(server.matrixSize.x * server.matrixSize.y * server.matrixSize.z, 4 * sizeof(float), ComputeBufferType.Default);
            print("Made Volume Buffer");
        }
        float minDepthMM = (KinectUtilities.depthRanges[(int)server.configuration.depthMode].x * server.configuration.depthRangeModifier.x);
        float maxDepthMM = (KinectUtilities.depthRanges[(int)server.configuration.depthMode].y * server.configuration.depthRangeModifier.y);
        float worldscaleDepth = (maxDepthMM - minDepthMM) / 1000;

        print("Depth Range:" + worldscaleDepth);
        targetMesh.transform.localPosition = new Vector3(0, 0, worldscaleDepth / 2);
        if (kinectSettings.transformationMode == TransformationMode.DepthToColor)
            targetMesh.transform.localScale = new Vector3(1.6f * worldscaleDepth, 0.9f * worldscaleDepth, worldscaleDepth);
        if (kinectSettings.transformationMode == TransformationMode.ColorToDepth)
            targetMesh.transform.localScale = new Vector3(worldscaleDepth * 1.6f, worldscaleDepth * 1.6f, worldscaleDepth);

    }

    public bool ApplyFrame(MeshRenderer targetMesh, ComputeBuffer targetBuffer, KinectSocketFrame frame)
    {
        if (frame.decompressed)
        {
            try
            {
                Material matt = targetMesh.material;
                targetBuffer.SetData(frame.decompressedData);
                if (clearUsedFrames)
                {
                    frame.ReleaseDecompressedData();
                }
                matt.SetBuffer("colors", targetBuffer);
                matt.SetInt("_MatrixX", frame.source.matrixSize.x);
                matt.SetInt("_MatrixY", frame.source.matrixSize.y);
                matt.SetInt("_MatrixZ", frame.source.matrixSize.z);
            }
            catch (Exception ex)
            {
                print(frame.frameNumber + " was corrupt, causing error: " + ex.Message);
            }
            return true;
        }
        else
        {
            return false;
        }
    }

    public bool MaintainBuffer(float secondsToBuffer)
    {
        int futureToCheck = (int)(server.playbackFPS * secondsToBuffer);
        int stillCompressed = futureToCheck;

        if (server.frames.Count > futureToCheck)
        {
            for (int i = 0; i < futureToCheck; i++)
            {
                if (server.frames.ElementAt(i).decompressed)
                {
                    stillCompressed--;
                }
                else
                {
                    new Thread(() => server.frames.ElementAt(i).Decompress()).Start();
                }
            }
            if (stillCompressed < futureToCheck * 0.75f) // BUFFER DEPTH VARIABLE - this should be a control probably?
            {
                return false;
            }
            else
            {
                return true;
            }
        }
        else
        {
            return false;
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

    public void Join()
    {
<<<<<<< Updated upstream
        server = new KinectRemoteFile(serverHostname, serverPort, kinectSettings);
        server.JoinServer(clientName);
=======
        source = new KinectRemoteProvider(serverHostname, transmissionMode, kinectSettings);
        (source as KinectRemoteProvider).JoinServer(clientName);
>>>>>>> Stashed changes
    }

    public void Subscribe()
    {
        server.SubscribeProvider(providerName);
    }

    public void Leave()
    {
        server.LeaveServer();
    }

    public void Unsubscribe()
    {
        server.UnsubscribeProvider();
    }
}