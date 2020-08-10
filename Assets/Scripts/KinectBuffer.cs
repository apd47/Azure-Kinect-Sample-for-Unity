using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using System.Collections.Generic;

public class KinectBuffer : MonoBehaviour
{
    #region Variables
    [Header("Device")]
    public int deviceID = 0;
    public bool collectCameraData;
    public KinectConfiguration kinectSettings;
    Device device;
    Transformation transformation;
    bool running = true;
    Vector3Int matrixSize;

    [Header("Reconstruction")]
    public ComputeShader kinectProcessingShader;
    public Filter filter;
    int frameID = 0;
    int computeShaderKernelIndex = -1;
    ComputeBuffer volumeBuffer;
    Image finalColor;
    byte[] colorData;
    Image finalDepth;
    byte[] depthData;
    Texture2D colorTexture;
    Texture2D depthTexture;
    Texture2D oldDepthTexture;

    [Header("Rendering")]
    public MeshRenderer mesh;

    [Header("Recording")]
    public bool saveToFile;
    public string filepath;
    public int numberFramesToRecord;
    public bool triggerWriteToFile;
    string[] base64Frames;

    [Header("Networking")]
    public bool sendToServer;
    public TransmissionMode transmissionMode;
    public string providerName;
    public string serverHostname;
<<<<<<< Updated upstream
    public int serverPort;
=======
    public int udpPort = 1935;
    public int tcpPort = 1936;

    public int maxPacketBytes = 1024;
>>>>>>> Stashed changes
    ConnectionState connectionState = ConnectionState.Disconnected;
    IPEndPoint serverEndpoint;
    Socket serverSocket;
    private static ManualResetEvent connectDone = new ManualResetEvent(false);
    private static ManualResetEvent sendDone = new ManualResetEvent(false);
    private static ManualResetEvent receiveDone = new ManualResetEvent(true);

    [Header("IMU Settings")]
    public bool collectIMUData;
    public float accelerometerMinValid = 0.5f;
    public float accelerometerMaxValid = 2.0f;
    public float accelerometerWeighting = 0.02f;
    Vector3 currentAngles;
    DateTime lastFrame;
    float dt;
    #endregion

    #region Unity

    private void Start()
    {
        StartKinect();
        Task CameraLooper = CameraLoop(device);
        Task IMULooper = IMULoop(device);
        //Task NetworkingLooper = NetworkingLoop();
        Task SavingLooper = SavingLoop();
    }

    private void Update()
    {
        ListenForData(serverSocket);
    }

    private void OnCloseKinect()
    {
        if (volumeBuffer != null)
        {
            volumeBuffer.Release();
            volumeBuffer = null;
        }

        if (device != null)
        {
            device.StopCameras();
            device.StopImu();
            device.Dispose();
        }

        if (colorTexture != null)
            Destroy(colorTexture);
        if (depthTexture != null)
            Destroy(depthTexture);
        if (oldDepthTexture != null)
            Destroy(oldDepthTexture);

    }

    private void OnCloseSockets()
    {

        if (connectionState != ConnectionState.Disconnected)
        {
            LeaveServer(providerName, ClientRole.PROVIDER);
            serverSocket.Close();
        }
    }

    private void OnDestroy()
    {
        running = false;
        OnCloseKinect();
        OnCloseSockets();
    }

    #endregion

    #region Device

    void StartKinect()
    {
        if (running)
        {
            OnCloseKinect();
        }
        device = Device.Open(deviceID);
        device.StartCameras(new DeviceConfiguration
        {
            ColorFormat = ImageFormat.ColorBGRA32,
            ColorResolution = kinectSettings.colorResolution,
            DepthMode = kinectSettings.depthMode,
            SynchronizedImagesOnly = true,
            CameraFPS = kinectSettings.fps,
        });

        device.StartImu();
        transformation = device.GetCalibration().CreateTransformation();

        base64Frames = new string[numberFramesToRecord];
        float worldscaleDepth = ((KinectUtilities.depthRanges[(int)device.CurrentDepthMode].y * kinectSettings.depthRangeModifier.y) - (KinectUtilities.depthRanges[(int)device.CurrentDepthMode].x * kinectSettings.depthRangeModifier.x)) / 1000;
        print("Depth Range:" + worldscaleDepth);
        mesh.transform.localPosition = new Vector3(0, 0, worldscaleDepth / 2);
        if (kinectSettings.transformationMode == TransformationMode.DepthToColor)
            mesh.transform.localScale = new Vector3(1.6f * worldscaleDepth, 0.9f * worldscaleDepth, worldscaleDepth);
        if (kinectSettings.transformationMode == TransformationMode.ColorToDepth)
            mesh.transform.localScale = new Vector3(worldscaleDepth*2, worldscaleDepth*2, worldscaleDepth);

        running = true;
<<<<<<< Updated upstream
=======
        Task CameraLooper = CameraLoop(device);
    }

    public void StopKinect()
    {
        running = false;
        OnCloseKinect();
>>>>>>> Stashed changes
    }

    private async Task CameraLoop(Device device)
    {
        Material matt = mesh.material;
        while (running)
        {
            if (collectCameraData)
            {
                using (Capture capture = await Task.Run(() => device.GetCapture()).ConfigureAwait(true))
                {
                    switch (kinectSettings.transformationMode)
                    {
                        case TransformationMode.ColorToDepth:
                            finalColor = transformation.ColorImageToDepthCamera(capture);
                            finalDepth = capture.Depth;
                            break;
                        case TransformationMode.DepthToColor:
                            finalColor = capture.Color;
                            finalDepth = transformation.DepthImageToColorCamera(capture);
                            break;
                        case TransformationMode.None:
                            finalColor = capture.Color;
                            finalDepth = capture.Depth;
                            break;
                    }

                    if (volumeBuffer == null)
                    {
                        matrixSize = new Vector3Int((int)(finalColor.WidthPixels * kinectSettings.volumeScale.x), (int)(finalColor.HeightPixels * kinectSettings.volumeScale.y), (int)((KinectUtilities.depthRanges[(int)device.CurrentDepthMode].y - KinectUtilities.depthRanges[(int)device.CurrentDepthMode].x) / 11 * kinectSettings.volumeScale.z));
                        print(matrixSize);
                        volumeBuffer = new ComputeBuffer(matrixSize.x * matrixSize.y * matrixSize.z, 4 * sizeof(float), ComputeBufferType.Default);
                        print("Made Volume Buffer || Matrix Size: " + matrixSize);
                    }

                    if (colorTexture == null)
                    {
                        colorTexture = new Texture2D(finalColor.WidthPixels, finalColor.HeightPixels, TextureFormat.BGRA32, false);
                        colorData = new byte[finalColor.Memory.Length];
                        print("Made Color Texture");
                    }

                    if (depthTexture == null)
                    {
                        depthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        oldDepthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        depthData = new byte[finalDepth.Memory.Length];
                        print("Made Depth Texture");
                    }

                    colorData = finalColor.Memory.ToArray();
                    colorTexture.LoadRawTextureData(colorData);
                    colorTexture.Apply();

                    depthData = finalDepth.Memory.ToArray();
                    depthTexture.LoadRawTextureData(depthData);
                    depthTexture.Apply();

                    configureComputeShader();

                    kinectProcessingShader.Dispatch(computeShaderKernelIndex, matrixSize.x / 16, matrixSize.y / 16, 1);

                    float[] frame = new float[matrixSize.x * matrixSize.y * matrixSize.z * 4];
                    volumeBuffer.GetData(frame);

                    // create a byte array and copy the floats into it...
                    var byteArray = new byte[frame.Length * 4];
                    Buffer.BlockCopy(frame, 0, byteArray, 0, byteArray.Length);

                    //new Thread(() => Postprocess(byteArray)).Start();

                    ThreadPool.QueueUserWorkItem((state) => Postprocess((Byte[])state), byteArray);

                    matt.SetBuffer("colors", volumeBuffer);
                    matt.SetInt("_MatrixX", matrixSize.x);
                    matt.SetInt("_MatrixY", matrixSize.y);
                    matt.SetInt("_MatrixZ", matrixSize.z);

                    Graphics.CopyTexture(depthTexture, oldDepthTexture);
                }
            }
            else
            {
                await Task.Run(() => { });
            }
        }
    }

    #endregion

    #region Reconstruction

    // I don't know if this actually needs to get done every frame?
    private void configureComputeShader()
    {
        // Apply Buffer Updates
        computeShaderKernelIndex = kinectProcessingShader.FindKernel("ToBuffer");
        kinectProcessingShader.SetInt("_MatrixX", matrixSize.x);
        kinectProcessingShader.SetInt("_MatrixY", matrixSize.y);
        kinectProcessingShader.SetInt("_MatrixZ", matrixSize.z);

        kinectProcessingShader.SetTexture(computeShaderKernelIndex, "ColorTex", colorTexture);
        kinectProcessingShader.SetTexture(computeShaderKernelIndex, "DepthTex", depthTexture);
        kinectProcessingShader.SetTexture(computeShaderKernelIndex, "oldDepthTexture", oldDepthTexture);
        kinectProcessingShader.SetBuffer(computeShaderKernelIndex, "ResultBuffer", volumeBuffer);
        kinectProcessingShader.SetInt("minDepth", (int)(KinectUtilities.depthRanges[(int)device.CurrentDepthMode].x * kinectSettings.depthRangeModifier.x));
        kinectProcessingShader.SetInt("maxDepth", (int)(KinectUtilities.depthRanges[(int)device.CurrentDepthMode].y * kinectSettings.depthRangeModifier.y));

        if (filter.useFilter)
        {
            Vector4 filterValue = filter.getFilterValue();
            kinectProcessingShader.SetVector("filterColor", filterValue);
            kinectProcessingShader.SetInt("filterType", (int)filter.filterType);
        }
        else
        {
            kinectProcessingShader.SetVector("filterColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
            kinectProcessingShader.SetInt("filterType", 0);
        }
    }

    public void Postprocess(byte[] data)
    {
        int thisFrameID = frameID;
        frameID++;

        if (sendToServer)
        {

            byte[] compressedArray = CompressData(data);

            try
            {
                if (connectionState == ConnectionState.JoinedServer)
                {
<<<<<<< Updated upstream
=======
                    //SplitAndSend(maxPacketBytes, thisFrameID, Convert.ToBase64String(compressedArray));
>>>>>>> Stashed changes
                    SendFrameToServer(thisFrameID, Convert.ToBase64String(compressedArray));
                }
            }
            catch (Exception ex)
            {
                print(ex.Message);
            }
        }

        if (saveToFile)
        {
            byte[] compressedArray = CompressData(data);

            try
            {
                if (thisFrameID < numberFramesToRecord)
                {
                    base64Frames[thisFrameID] = Convert.ToBase64String(compressedArray);
                }
                else
                {
                    saveToFile = false;
                    triggerWriteToFile = true;
                }
            }
            catch (Exception ex)
            {
                print(ex.Message);
            }

        }
    }

    public static byte[] CompressData(byte[] data)
    {
        byte[] compressArray = null;
        try
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress))
                {
                    deflateStream.Write(data, 0, data.Length);
                }
                compressArray = memoryStream.ToArray();
            }
        }
        catch (Exception exception)
        {
            print(exception.ToString());
        }
        return compressArray;
    }

    #endregion

    #region Saving

    private async Task SavingLoop()
    {
        if (triggerWriteToFile)
        {
            await WriteDataToFile();
        }
    }

    // This is a "mock" async method that needs to get turned into a real one
    private Task<bool> WriteDataToFile()
    {
        print("Saving volume data....");
        saveToFile = false;
        int i = 0;
        StreamWriter writer = new System.IO.StreamWriter(filepath, false, Encoding.UTF8);
        foreach (string frame in base64Frames)
        {
            if (frame != "")
            {
                i++;
                print(i);
                writer.Write(frame + KinectUtilities.frameBreak);
            }
        }
        writer.Close();
        writer.Dispose();
        print(i + " frames of volume data have been saved to " + filepath);
        triggerWriteToFile = false;
        return Task.FromResult(true);
    }

    #endregion

    #region Networking 

    //private async Task NetworkingLoop()
    //{
    //    while (sendToServer)
    //    {
    //        ListenForData(serverSocket);
    //    }
    //}

    //// This is a "mock" async method that needs to get turned into a real one
    //private Task<SocketAsyncEventArgs> ListenForData(Socket socket)
    //{
    //    if (socket != null)
    //    {
    //        int available = socket.Available;
    //        if (available > 0)
    //        {
    //            print("Available:" + available);
    //            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
    //            args.SetBuffer(new byte[available], 0, available);
    //            args.Completed += OnDataRecieved;
    //            socket.ReceiveAsync(args);
    //            return Task.FromResult(args);
    //        }
    //    }
    //    return null;
    //}

    private void ListenForData(Socket socket)
    {
        if (socket != null && receiveDone.WaitOne(0))
        {
            Receive(socket);
            //int available = socket.Available;
            //if (available > 0)
            //{
            //    SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            //    args.SetBuffer(new byte[available], 0, available);
            //    args.Completed += OnDataRecieved;
            //    socket.ReceiveAsync(args);
            //}
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
<<<<<<< Updated upstream
                print("Available:" + available);
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.SetBuffer(new byte[available], 0, available);
                args.Completed += OnDataRecieved;
                socket.ReceiveAsync(args);
=======
                // All the data has arrived
                if (state.sb.Length > 1)
                {
                    receiveDone.Set();
                    ProcessReceivedData(state.sb.ToString());
                }
>>>>>>> Stashed changes
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    public void Join()
    {
        JoinServer(providerName, ClientRole.PROVIDER);
    }

    public void Leave()
    {
        sendToServer = false;
        LeaveServer(providerName, ClientRole.PROVIDER);
    }

    public void StartSendingFrames()
    {
        sendToServer = true;
    }

    public void StopSendingFrames()
    {
        sendToServer = false;
    }

    void JoinServer(string clientName, ClientRole clientRole)
    {
        try
        {
            if (string.IsNullOrEmpty(serverHostname))
            {
                print("Please specify server hostname !");
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
                        print("Wasn't able to connect to server before timeout.");
                        serverSocket = null;
                    }
                    break;
            }

<<<<<<< Updated upstream
            // TODO: Make this a string builder, and not a either/or switch
            string message = "JOIN" + "|" + clientRole + "|" + clientName + "|" + kinectSettings.Serialize();
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            serverSocket.SendTo(data, serverEndpoint);
            print("Server JOIN request sent");
=======
            if (serverSocket != null)
            {
                // TODO: Make this a string builder, and not a either/or switch
                string message = "JOIN" + "|" + clientRole + "|" + clientName + "|" + kinectSettings.Serialized;
                Send(serverSocket, message);
                print("Server JOIN request sent");
            }
>>>>>>> Stashed changes
        }
        catch (Exception x)
        {
            print("Error: " + x.ToString());
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

    void LeaveServer(string clientName, ClientRole clientRole)
    {

        try
        {
            // TODO: Make this a string builder, and not a either/or switch
            string message = "LEAVE" + "|" + clientRole + "|" + clientName;
            Send(serverSocket, message);
            print("Server LEAVE request sent");
        }
        catch (Exception x)
        {
            print("Error: " + x.ToString());
        }
    }

    private void ProcessReceivedData(string recieved)
    {
        string[] split = recieved.Split('|');
        switch (split[0])
        {
            case "CONFIRM":
                switch (split[1])
                {
                    case "JOIN":
                        if (split[3].Contains(providerName)) // this is a gross hack to get around blank space after the split - fix it
                        {
                            print("Successfully joined server as " + split[2] + ":" + split[3]);
                            connectionState = ConnectionState.JoinedServer;
                        }
                        break;
                    case "LEAVE":
                        if (split[3].Contains(providerName)) // this is a gross hack to get around blank space after the split - fix it
                        {
                            print("Successfully left server as " + split[2] + ":" + split[3]);
                            connectionState = ConnectionState.Disconnected;
                        }
                        break;
                    case "PROVIDE":
                        if (split[2].Contains(providerName)) // this is a gross hack to get around blank space after the split - fix it
                        {
                            print("Successfully sent frame " + split[3] + " to server");
                        }
                        break;
                    default:
                        print(split[1]);
                        break;
                }
                break;
            case "NOTICE":
                switch (split[1])
                {
                    case "JOIN":
                        print(split[2] + " " + split[3] + " has joined the server");
                        break;
                    case "LEAVE":
                        print(split[2] + " " + split[3] + " has left the server");
                        break;
                    case "PROVIDE":
                        print("Frame " + split[3] + " recieved from provider " + split[2]);
                        break;
                    default:
                        print(split[1]);
                        break;
                }
                break;
            default:
                print(recieved);
                break;
        }
    }

    private void OnDataReceived(object sender, SocketAsyncEventArgs e)
    {
        string received = Encoding.ASCII.GetString(e.Buffer);
        ProcessReceivedData(received);
    }

    void SendFrameToServer(int frameNumber, string frameData)
    {
        if (connectionState == ConnectionState.JoinedServer)
        {
            try
            {
                string message = "PROVIDE" + "|" + providerName + "|" + frameNumber + "|" + frameData;
<<<<<<< Updated upstream
                byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
                serverSocket.SendTo(data, serverEndpoint);
=======
                Send(serverSocket, message);
>>>>>>> Stashed changes
                //print("Frame " + frameNumber + " sent to server");
            }
            catch (Exception x)
            {
                print("Error: " + x.ToString());
            }
        }
    }

<<<<<<< Updated upstream
=======
    public void SplitAndSend(int maxBytesPerMessage, int frameNumber, string frameData)
    {
        //print(frameData.Length);
        List<string> segments = new List<string>();

        int numberOfSegments = 0;
        int availableBytesPerMessage = maxBytesPerMessage - 50; // Subtract 50 extra for the header

        for (int i = 0; i < frameData.Length;)
        {
            if ((frameData.Length - i - 1) > availableBytesPerMessage)
            {
                segments.Add(frameData.Substring(i, availableBytesPerMessage));
                i += availableBytesPerMessage;
                numberOfSegments++;
            }
            else
            {
                segments.Add(frameData.Substring(i, frameData.Length - i));
                i += availableBytesPerMessage;
                numberOfSegments++;
            }
        }
        print(segments.Count);
        string messageHeader = "PROVIDE" + "|" + providerName + "|" + frameNumber + "|" + numberOfSegments + "|";

        for (int s = 0; s < segments.Count; s++)
        {
            string message = messageHeader + s + "|" + segments[s];
            Send(serverSocket, message);
        }
    }

>>>>>>> Stashed changes
    #endregion

    #region IMU

    private async Task IMULoop(Device device)
    {
        lastFrame = DateTime.Now;
        currentAngles = this.transform.rotation.eulerAngles;
        while (true)
        {
            if (collectIMUData)
            {
                ImuSample imuSample = new ImuSample();
                await Task.Run(() => imuSample = device.GetImuSample()).ConfigureAwait(true);
                dt = (DateTime.Now - lastFrame).Milliseconds;
                lastFrame = DateTime.Now;
                currentAngles = ComplementaryFilterEuler(imuSample, currentAngles, dt / 1000);
                this.transform.rotation = Quaternion.Euler(currentAngles);
            }
            else
            {
                await Task.Run(() => { });
            }
        }
    }

    Vector3 ComplementaryFilterEuler(ImuSample sample, Vector3 currentOrientation, float dt)
    {
        // Integrate the gyroscope data -> int(angularSpeed) = angle
        float[] accData = { sample.AccelerometerSample.X, sample.AccelerometerSample.Y, sample.AccelerometerSample.Z };
        float[] gyrData = { sample.GyroSample.X, sample.GyroSample.Y, sample.GyroSample.Z };

        currentOrientation.x += (dt * gyrData[1]) * (180f / (float)Math.PI);    // Angle around the X-axis
        currentOrientation.y += (dt * gyrData[2]) * (180f / (float)Math.PI);     // Angle around the Y-axis
        currentOrientation.z += (dt * gyrData[0]) * (180f / (float)Math.PI);     // Angle around the Z-axis

        currentOrientation.x = ((currentOrientation.x % 360) + 360) % 360;
        currentOrientation.y = ((currentOrientation.y % 360) + 360) % 360;
        currentOrientation.z = ((currentOrientation.z % 360) + 360) % 360;

        // Compensate for drift with accelerometer data if in force in range
        float forceMagnitudeApprox = Math.Abs(accData[0] / 9.8f) + Math.Abs(accData[1] / 9.8f) + Math.Abs(accData[2] / 9.8f);
        if (forceMagnitudeApprox > accelerometerMinValid && forceMagnitudeApprox < accelerometerMaxValid)
        {
            float roll = Mathf.Atan2(accData[1], accData[2]) * (180f / Mathf.PI) - 180;
            roll = ((roll % 360) + 360) % 360;

            float pitch = Mathf.Atan2(accData[2], accData[0]) * (180f / Mathf.PI) - 270;
            pitch = ((pitch % 360) + 360) % 360;

            currentOrientation.x = Mathf.LerpAngle(currentOrientation.x, pitch, accelerometerWeighting);
            currentOrientation.z = Mathf.LerpAngle(currentOrientation.z, roll, accelerometerWeighting);
        }

        return currentOrientation;
    }
    #endregion

}
