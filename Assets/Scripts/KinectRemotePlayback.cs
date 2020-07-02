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

public class KinectRemotePlayback : KinectPlayback
{
    public float MaxWaitSeconds = 1;

    public bool clearUsedFrames = false;

    public string clientName;
    public string providerName;
    public string serverHostname;
    public int serverPort;

    // TODO: add a re-up feature to bring current playing frame # closer to most recent received frame #
    public void Update()
    {
        UpdateVisualization();
    }


    public new void UpdateVisualization()
    {
        if (source != null)
        {
            (source as KinectRemoteProvider).Listen();

            if ((source as KinectRemoteProvider).connectionState == ConnectionState.SubscribedToProvider)
            {
                if (volume == null)
                {
                    kinectSettings = source.configuration;
                    ConfigureVisualization();
                }

                if (CheckAndMaintainBuffer(defaultSecondsToBuffer) != 0)
                {
                    if (strictBuffering && Time.time - lastFrameTime >= frameDuration)
                    {
                        print("Strict buffering is delaying frame playback");
                        lastFrameTime = Time.time;
                        return;
                    }
                }

                if (Time.time - lastFrameTime >= frameDuration)
                {
                    if (ApplyNextFrame(targetMesh, volume))
                    {
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

    private void OnDestroy()
    {
        Leave();
        Unsubscribe();
    }

    public void Join()
    {
        source = new KinectRemoteProvider(serverHostname, serverPort, kinectSettings);
        (source as KinectRemoteProvider).JoinServer(clientName);
    }

    public void Subscribe()
    {
        (source as KinectRemoteProvider).SubscribeProvider(providerName);
    }

    public void Leave()
    {
        (source as KinectRemoteProvider).LeaveServer();
    }

    public void Unsubscribe()
    {
        (source as KinectRemoteProvider).UnsubscribeProvider();
    }
}