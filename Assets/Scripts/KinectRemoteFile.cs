using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class KinectRemoteFile
{
    public string serverHostname;
    public int serverPort;
    public string providerName;
    public string clientName;
    public LinkedList<KinectSocketFrame> frames;
    public KinectConfiguration configuration;
    public Vector3Int matrixSize;


    ClientRole clientRole = ClientRole.RECEIVER;
    IPEndPoint serverEndpoint;
    Socket serverSocket;

    public enum ClientRole
    {
        PROVIDER,
        RECEIVER
    }

    public int playbackFPS
    {
        get
        {
            switch (configuration.fps)
            {
                case FPS.FPS5:
                    return 5;
                case FPS.FPS15:
                    return 15;
                case FPS.FPS30:
                    return 30;
                default:
                    return 15;
            }

        }
    }

    public KinectRemoteFile(string hostname, int port, KinectConfiguration overrideConfiguration)
    {
        this.serverHostname = hostname;
        this.serverPort = port;
        this.configuration = overrideConfiguration;
        this.matrixSize = CalculateMatrixSize();
        this.frames = new LinkedList<KinectSocketFrame>();
    }

    public Vector3Int CalculateMatrixSize()
    {
        Vector2Int resolution = new Vector2Int(0, 0);
        switch (configuration.transformationMode)
        {
            case TransformationMode.ColorToDepth:
                switch (configuration.depthMode)
                {
                    case DepthMode.Off:
                        resolution = new Vector2Int(-1, -1);
                        break;
                    case DepthMode.NFOV_2x2Binned:
                        resolution = new Vector2Int(320, 288);
                        break;
                    case DepthMode.NFOV_Unbinned:
                        resolution = new Vector2Int(640, 576);
                        break;
                    case DepthMode.WFOV_2x2Binned:
                        resolution = new Vector2Int(512, 512);
                        break;
                    case DepthMode.WFOV_Unbinned:
                        resolution = new Vector2Int(1024, 1024);
                        break;
                    case DepthMode.PassiveIR:
                        resolution = new Vector2Int(1024, 1024);
                        break;
                    default:
                        break;
                }
                break;
            case TransformationMode.DepthToColor:
                switch (configuration.colorResolution)
                {
                    case ColorResolution.Off:
                        resolution = new Vector2Int(-1, -1);
                        break;
                    case ColorResolution.R720p:
                        resolution = new Vector2Int(1280, 720);
                        break;
                    case ColorResolution.R1080p:
                        resolution = new Vector2Int(1920, 1080);
                        break;
                    case ColorResolution.R1440p:
                        resolution = new Vector2Int(2560, 1440);
                        break;
                    case ColorResolution.R1536p:
                        resolution = new Vector2Int(2048, 1536);
                        break;
                    case ColorResolution.R2160p:
                        resolution = new Vector2Int(3840, 2160);
                        break;
                    case ColorResolution.R3072p:
                        resolution = new Vector2Int(4096, 3072);
                        break;
                    default:
                        break;
                }
                break;
            case TransformationMode.None:
                Debug.Log("Transformation Mode Invalid - select either DepthToColor or ColorToDepth");
                break;
        }
        matrixSize = new Vector3Int((int)(resolution.x * configuration.volumeScale.x), (int)(resolution.y * configuration.volumeScale.y), (int)((KinectUtilities.depthRanges[(int)configuration.depthMode].y - KinectUtilities.depthRanges[(int)configuration.depthMode].x) / 11 * configuration.volumeScale.z));
        return matrixSize;
    }

    public void JoinServer(string clientName)
    {

        this.clientName = clientName;
        try
        {
            if (string.IsNullOrEmpty(serverHostname))
            {
                Debug.Log("Kinect Provider hostname required!");
                return;
            }

            var addresses = Dns.GetHostAddresses(serverHostname);
            serverEndpoint = new IPEndPoint(addresses[0], serverPort);

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // TODO: Make this a string builder, and not a either/or switch
            string message = "JOIN" + "|" + clientRole + "|" + clientName;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            serverSocket.SendTo(data, serverEndpoint);
            Debug.Log("Server JOIN request sent");
        }
        catch (Exception x)
        {
            Debug.Log("Error: " + x.ToString());
        }
    }

    public void SubscribeProvider(string providerName)
    {

        this.providerName = providerName;
        try
        {
            // TODO: Make this a string builder, and not a either/or switch
            string message = "SUBSCRIBE" + "|" + providerName + "|" + clientName;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            serverSocket.SendTo(data, serverEndpoint);
            Debug.Log("Server SUBSCRIBE request sent");
        }
        catch (Exception x)
        {
            Debug.Log("Error: " + x.ToString());
        }
    }

    public void UnsubscribeProvider()
    {

        try
        {
            // TODO: Make this a string builder, and not a either/or switch
            string message = "UNSUBSCRIBE" + "|" + providerName + "|" + clientName;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            serverSocket.SendTo(data, serverEndpoint);
            Debug.Log("Server UNSUBSCRIBE request sent");
        }
        catch (Exception x)
        {
            Debug.Log("Error: " + x.ToString());
        }
    }

    public void LeaveServer()
    {

        try
        {
            // TODO: Make this a string builder, and not a either/or switch
            string message = "LEAVE" + "|" + clientRole + "|" + clientName;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            serverSocket.SendTo(data, serverEndpoint);
            Debug.Log("Server LEAVE request sent");
        }
        catch (Exception x)
        {
            Debug.Log("Error: " + x.ToString());
        }
    }

    public void Listen()
    {
        if (serverSocket != null)
        {
            int available = serverSocket.Available;
            if (available > 0)
            {
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.SetBuffer(new byte[available], 0, available);
                args.Completed += OnDataRecieved;
                serverSocket.ReceiveAsync(args);
            }
        }
        else
        {
            //Debug.Log("Nothing recieved");
        }
    }
    public enum ConnectionState
    {
        Disconnected,
        JoinedServer,
        SubscribedToProvider
    }

    public ConnectionState connectionState = ConnectionState.Disconnected;

    private void OnDataRecieved(object sender, SocketAsyncEventArgs e)
    {
        string recieved = Encoding.ASCII.GetString(e.Buffer);
        //Debug.Log("Recieved: " + recieved);
        string[] split = recieved.Split('|');
        switch (split[0])
        {
            case "CONFIRM":
                switch (split[1])
                {
                    case "JOIN":
                        if (split[3] == clientName)
                        {
                            Debug.Log("Successfully joined server as " + split[2] + ":" + split[3]);
                            connectionState = ConnectionState.JoinedServer;
                        }
                        break;
                    case "LEAVE":
                        if (split[3] == clientName)
                        {
                            Debug.Log("Successfully left server as " + split[2] + ":" + split[3]);
                            connectionState = ConnectionState.Disconnected;
                        }
                        break;
                    case "SUBSCRIBE":
                        if (split[3] == clientName)
                        {
                            Debug.Log("Successfully subscribed to provider " + split[2] + " as " + split[3]);
                            if (configuration.Import(split[4]))
                            {
                                Debug.Log("Successfully recieved provider configuration");
                                connectionState = ConnectionState.SubscribedToProvider;
                            }
                            else
                            {
                                Debug.Log("Provider configuration not imported successfully");
                            }
                        }
                        break;
                    case "UNSUBSCRIBE":
                        if (split[3] == clientName)
                        {
                            Debug.Log("Successfully unsubscribed from provider");
                            connectionState = ConnectionState.JoinedServer;
                        }
                        break;
                    case "PROVIDE":
                        if (split[2] == clientName)
                        {
                            Debug.Log("Successfully sent frame " + split[3] + " to server");
                        }
                        break;

                    default:
                        Debug.Log(split[1]);
                        break;
                }
                break;
            case "NOTICE":
                switch (split[1])
                {
                    case "JOIN":
                        Debug.Log(split[2] + " " + split[3] + " has joined the server");
                        break;
                    case "LEAVE":
                        Debug.Log(split[2] + " " + split[3] + " has left the server");
                        break;
                    case "SUBSCRIBE":
                        Debug.Log(split[3] + " has subscribed to the provider " + split[2]);
                        break;
                    case "PROVIDE":
                        frames.AddLast(new KinectSocketFrame(this, int.Parse(split[3]), split[4]));
                        //Debug.Log("Frame " + split[3] + " recieved from provider " + split[2]);
                        break;
                    default:
                        Debug.Log(split[1]);
                        break;
                }
                break;
            default:
                Debug.Log(recieved);
                break;
        }
    }
}


