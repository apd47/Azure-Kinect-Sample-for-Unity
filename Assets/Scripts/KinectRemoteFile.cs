using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class KinectRemoteFile
{
    public TransmissionMode transmissionMode;
    public string serverHostname;
    //public int serverPort;
    public string providerName;
    public string clientName;
<<<<<<< Updated upstream:Assets/Scripts/KinectRemoteFile.cs
    public LinkedList<KinectSocketFrame> frames;
    public KinectConfiguration configuration;
    public Vector3Int matrixSize;

=======
    public int udpPort = 1935;
    public int tcpPort = 1936;

    private static ManualResetEvent connectDone = new ManualResetEvent(false);
    private static ManualResetEvent sendDone = new ManualResetEvent(false);
    private static ManualResetEvent receiveDone = new ManualResetEvent(true);

    public ConnectionState connectionState = ConnectionState.Disconnected;
>>>>>>> Stashed changes:Assets/Scripts/KinectRemoteProvider.cs

    ClientRole clientRole = ClientRole.RECEIVER;
    IPEndPoint serverEndpoint;
    Socket serverSocket;

<<<<<<< Updated upstream:Assets/Scripts/KinectRemoteFile.cs
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
=======
    public KinectRemoteProvider(string hostname, TransmissionMode transmissionMode, KinectConfiguration overrideConfiguration)
>>>>>>> Stashed changes:Assets/Scripts/KinectRemoteProvider.cs
    {
        this.serverHostname = hostname;
        //this.serverPort = port;
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
            serverSocket = null;
            if (string.IsNullOrEmpty(serverHostname))
            {
                Debug.Log("Kinect Provider hostname required!");
                return;
            }

            var addresses = Dns.GetHostAddresses(serverHostname);

            switch (transmissionMode)
            {
                case TransmissionMode.UDP:
                    serverEndpoint = new IPEndPoint(addresses[0], udpPort);
                    serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    serverSocket.Bind(new IPEndPoint(IPAddress.Any, 0));
                    break;
                case TransmissionMode.TCP:
                    serverEndpoint = new IPEndPoint(addresses[0], tcpPort);
                    serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    serverSocket.BeginConnect(serverEndpoint, new AsyncCallback(ConnectCallback), serverSocket);
                    if (!connectDone.WaitOne(5000)) // 5 second connection timeout
                    {
                        Debug.Log("Wasn't able to connect to server before timeout.");
                        serverSocket = null;
                    }
                    break;
            }

            if (serverSocket != null)
            {
                Debug.Log("My name is " + clientName);
                // TODO: Make this a string builder, and not a either/or switch
                string message = "JOIN" + "|" + clientRole + "|" + clientName;
                Send(serverSocket, message);
                Debug.Log("Server JOIN request sent");
            }
        }
        catch (Exception x)
        {
            Debug.Log("Error: " + x.ToString());
        }
    }

    // Uses UTF8 Encoding not ASCII
    private void Send(Socket client, String message)
    {
        byte[] byteData = Encoding.UTF8.GetBytes(message);

        switch (transmissionMode)
        {
            case TransmissionMode.UDP:
                serverSocket.SendTo(byteData, serverEndpoint);
                break;
            case TransmissionMode.TCP:
                client.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), client);
                sendDone.WaitOne();
                break;
        }
    }

    private void ConnectCallback(IAsyncResult ar)
    {
        try
        {
            Socket client = (Socket)ar.AsyncState;
            client.EndConnect(ar);
            Console.WriteLine("Socket connected to {0}",
                client.RemoteEndPoint.ToString());
            connectDone.Set();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private void SendCallback(IAsyncResult ar)
    {
        try
        {
            Socket client = (Socket)ar.AsyncState;
            int bytesSent = client.EndSend(ar);
            Console.WriteLine("Sent {0} bytes to server.", bytesSent);

            // Signal that all bytes have been sent.  
            sendDone.Set();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    public void SubscribeProvider(string providerName)
    {

        this.providerName = providerName;
        try
        {
            Debug.Log("My name is " + clientName);
            // TODO: Make this a string builder, and not a either/or switch
            string message = "SUBSCRIBE" + "|" + providerName + "|" + clientName;
            Send(serverSocket, message);
            Debug.Log("Server SUBSCRIBE request sent: " + message);
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
            Send(serverSocket, message);
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
            Send(serverSocket, message);
            Debug.Log("Server LEAVE request sent");
            serverSocket.Close();
        }
        catch (Exception x)
        {
            Debug.Log("Error: " + x.ToString());
        }
    }


    public void Listen()
    {
        if (serverSocket != null && receiveDone.WaitOne(0))
        {
            Receive(serverSocket);
        }
    }

    public class StateObject
    {
        // Client socket.  
        public Socket workSocket = null;
        // Size of receive buffer.  
        public const int BufferSize = 256;
        public int Available;
        // Receive buffer.  
        public byte[] buffer = new byte[BufferSize];
        // Received data string.  
        public StringBuilder sb = new StringBuilder();
    }

    private void Receive(Socket client)
    {
        try
        {
            receiveDone.Reset();
            StateObject state = new StateObject();
            state.workSocket = client;
            client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReceiveCallback), state);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private void ReceiveCallback(IAsyncResult ar)
    {
        try
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket client = state.workSocket;

            int bytesRead = client.EndReceive(ar);
            if (bytesRead > 0)
            {
                // There might be more data, so store the data received so far.  
                state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));

                // Get the rest of the data.
                if (client.Available > 0)
                {
                    client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                        new AsyncCallback(ReceiveCallback), state);
                }
                else
                {
                    // All the data has arrived
                    if (state.sb.Length > 1)
                    {
                        receiveDone.Set();
                        ProcessReceivedData(state.sb.ToString());
                    }
                }
            }
            else
            {
                // All the data has arrived
                if (state.sb.Length > 1)
                {
                    receiveDone.Set();
                    ProcessReceivedData(state.sb.ToString());
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }
    public enum ConnectionState
    {
        Disconnected,
        JoinedServer,
        SubscribedToProvider
    }

    public ConnectionState connectionState = ConnectionState.Disconnected;

    private void ProcessReceivedData(string received)
    {
        string[] split = received.Split('|');
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
                Debug.Log(received);
                break;
        }
    }
}


