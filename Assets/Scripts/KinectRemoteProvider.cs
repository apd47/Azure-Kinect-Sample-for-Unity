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
public class KinectRemoteProvider : KinectSource
{
    public string serverHostname;
    public int serverPort;
    public string providerName;
    public string clientName;
    public ConnectionState connectionState = ConnectionState.Disconnected;

    EntityType clientRole = EntityType.RECEIVER;
    IPEndPoint serverEndpoint;
    Socket serverSocket;

    public KinectRemoteProvider(string hostname, int port, KinectConfiguration overrideConfiguration)
    {
        this.serverHostname = hostname;
        this.serverPort = port;
        this.configuration = overrideConfiguration;
        this.frames = new LinkedList<KinectFrame>();
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
                Debug.Log(recieved);
                break;
        }
    }
}


