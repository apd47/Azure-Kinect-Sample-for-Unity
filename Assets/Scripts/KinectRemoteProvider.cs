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
public class KinectRemoteProvider : KinectSource
{
    public TransmissionMode transmissionMode;
    public string serverHostname;
    //public int serverPort;
    public string providerName;
    public string clientName;
    public int udpPort = 1935;
    public int tcpPort = 1936;

    private static ManualResetEvent connectDone = new ManualResetEvent(false);
    private static ManualResetEvent sendDone = new ManualResetEvent(false);
    private static ManualResetEvent receiveDone = new ManualResetEvent(true);

    public ConnectionState connectionState = ConnectionState.Disconnected;

    ClientRole clientRole = ClientRole.RECEIVER;
    IPEndPoint serverEndpoint;
    Socket serverSocket;

    public KinectRemoteProvider(string hostname, TransmissionMode transmissionMode, KinectConfiguration overrideConfiguration)
    {
        this.serverHostname = hostname;
        //this.serverPort = port;
        this.configuration = overrideConfiguration;
        this.frames = new LinkedList<KinectFrame>();
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
                        int frameNumber = int.Parse(split[3]);
                        int totalSegments = int.Parse(split[4]);
                        int segmentNumber = int.Parse(split[5]);
                        string segmentString = split[6];
                        bool foundInList = false;
                        foreach (KinectSocketFrame frame in frames)
                        {
                            if (frame.frameNumber == frameNumber)
                            {
                                frame.ImportSegment(segmentNumber, segmentString);
                                foundInList = true;
                            }
                        }
                        if (!foundInList)
                        {
                            KinectSocketFrame temp = new KinectSocketFrame(this, frameNumber, totalSegments);
                            temp.ImportSegment(segmentNumber, segmentString);
                            frames.AddLast(temp);
                        }

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


