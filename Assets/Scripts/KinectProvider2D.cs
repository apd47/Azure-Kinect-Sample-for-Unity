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
using System.Data.SqlTypes;
using K4os.Compression.LZ4;
using System.Linq;
using System.Net.Http.Headers;

public class KinectProvider2D : KinectProvider
{
    #region Variables
    [Header("Device")]
    public bool collectCameraData;
    Device device;
    Transformation transformation;
    bool running = true;
    Vector2Int matrixSize;

    [Header("Reconstruction")]
    public ComputeShader kinectProcessingShader;
    int frameID = 0;
    int computeShaderKernelIndex = -1;
    ComputeBuffer colorBuffer;
    ComputeBuffer depthBuffer;

    byte[] extractedColorBytes;
    byte[] extractedDepthBytes;

    Image finalColor;
    byte[] colorData;
    Image finalDepth;
    byte[] depthData;
    Texture2D colorTexture;
    Texture2D depthTexture;

    [Header("Rendering")]
    public MeshRenderer colorMesh;
    public MeshRenderer depthMesh;

    [Header("Networking")]
    public bool sendToServer;
    ConnectionState connectionState = ConnectionState.Disconnected;
    IPEndPoint serverEndpoint;
    Socket serverSocket;

    //[Header("IMU Settings")]
    //public bool collectIMUData;
    //public float accelerometerMinValid = 0.5f;
    //public float accelerometerMaxValid = 2.0f;
    //public float accelerometerWeighting = 0.02f;
    //Vector3 currentAngles;
    //DateTime lastFrame;
    //float dt;
    #endregion

    #region Unity


    private void Update()
    {
        ListenForData(serverSocket);
    }

    private void OnDestroy()
    {
        running = false;
        if (colorBuffer != null)
        {
            colorBuffer.Release();
            colorBuffer = null;
        }

        if (depthBuffer != null)
        {
            depthBuffer.Release();
            depthBuffer = null;
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

        if (connectionState != ConnectionState.Disconnected)
        {
            LeaveServer(providerName, EntityType.PROVIDER);
            serverSocket.Close();
        }
    }

    #endregion

    #region Device

    public void StartKinect()
    {
        OnDestroy();
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

        running = true;
        Task CameraLooper = CameraLoop(device);
    }

    public void StopKinect()
    {
        running = false;
    }

    private async Task CameraLoop(Device device)
    {
        Material colorMatt = colorMesh.material;
        Material depthMatt = depthMesh.material;

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

                    if (colorBuffer == null)
                    {
                        matrixSize = new Vector2Int((int)(finalColor.WidthPixels * kinectSettings.volumeScale.x), (int)(finalColor.HeightPixels * kinectSettings.volumeScale.y));
                        colorBuffer = new ComputeBuffer(matrixSize.x * matrixSize.y, 3 * sizeof(float), ComputeBufferType.Default);
                        print("Made Color GPU Buffer || Matrix Size: " + matrixSize);
                        extractedColorBytes = new byte[matrixSize.x * matrixSize.y * 3 * 4];
                    }

                    if (depthBuffer == null)
                    {
                        matrixSize = new Vector2Int((int)(finalDepth.WidthPixels * kinectSettings.volumeScale.x), (int)(finalDepth.HeightPixels * kinectSettings.volumeScale.y));
                        depthBuffer = new ComputeBuffer(matrixSize.x * matrixSize.y, 3 * sizeof(float), ComputeBufferType.Default);
                        print("Made Depth GPU Buffer || Matrix Size: " + matrixSize);
                        extractedDepthBytes = new byte[matrixSize.x * matrixSize.y * 3 * 4];
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

                    // Get the buffer data as byte arrays 
                    colorBuffer.GetData(extractedColorBytes);
                    depthBuffer.GetData(extractedDepthBytes);

                    // TODO: Test which is faster, or if a dedicated thread would be best
                    //Option 1: Use the UserWorkItem Threadpool to manage thread for me - do i need to use a state here? is this threadsafe as written? 
                    ThreadPool.QueueUserWorkItem((state) => Postprocess(extractedColorBytes, extractedDepthBytes, KinectCompressionType.LZ4));

                    //Option 2: Spawn a thread for each frame
                    //new Thread(() => Postprocess(extractedColorBytes, extractedDepthBytes)).Start();

                    colorMatt.SetBuffer("colors", colorBuffer);
                    colorMatt.SetInt("_MatrixX", matrixSize.x);
                    colorMatt.SetInt("_MatrixY", matrixSize.y);

                    depthMatt.SetBuffer("colors", depthBuffer);
                    depthMatt.SetInt("_MatrixX", matrixSize.x);
                    depthMatt.SetInt("_MatrixY", matrixSize.y);

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
        computeShaderKernelIndex = kinectProcessingShader.FindKernel("ToBuffers");

        kinectProcessingShader.SetInt("_MatrixX", matrixSize.x);
        kinectProcessingShader.SetInt("_MatrixY", matrixSize.y);

        kinectProcessingShader.SetTexture(computeShaderKernelIndex, "ColorTex", colorTexture);
        kinectProcessingShader.SetTexture(computeShaderKernelIndex, "DepthTex", depthTexture);
        kinectProcessingShader.SetBuffer(computeShaderKernelIndex, "ColorResultBuffer", colorBuffer);
        kinectProcessingShader.SetBuffer(computeShaderKernelIndex, "DepthResultBuffer", depthBuffer);
    }

    public void Postprocess(byte[] colorData, byte[] depthData, KinectCompressionType compressionType)
    {
        MaintainFramePair(colorData, depthData, compressionType);

        if (sendToServer && connectionState == ConnectionState.JoinedServer)
        {
            try
            {
                if (!currentFramePair.transmissionStarted)
                {
                    currentFramePair.SendFramePairToServer(maxPacketBytes);
                }
            }
            catch (Exception ex)
            {
                print(ex.Message);
                currentFramePair.transmissionStarted = false;
            }
        }
    }


    public static byte[] CompressDataLZ4(byte[] data)
    {
        byte[] compressedArray = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
        int num = LZ4Codec.Encode(data, 0, data.Length, compressedArray, 0, compressedArray.Length, LZ4Level.L00_FAST);
        Array.Resize(ref compressedArray, num);
        return compressedArray;
    }

    public static byte[] CompressData(byte[] data)
    {
        byte[] compressArray = null;
        try
        {
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (DeflateStream deflateStream = new DeflateStream(memoryStream, System.IO.Compression.CompressionLevel.Fastest))
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

    #region Networking 

    private void ListenForData(Socket socket)
    {
        if (socket != null)
        {
            int available = socket.Available;
            if (available > 0)
            {
                SocketAsyncEventArgs args = new SocketAsyncEventArgs();
                args.SetBuffer(new byte[available], 0, available);
                args.Completed += OnDataRecieved;
                socket.ReceiveAsync(args);
            }
        }
    }

    public void Join()
    {
        JoinServer(providerName, EntityType.PROVIDER);
    }

    public void Leave()
    {
        sendToServer = false;
        LeaveServer(providerName, EntityType.PROVIDER);
    }

    public void StartSendingFrames()
    {
        sendToServer = true;
    }

    public void StopSendingFrames()
    {
        sendToServer = false;
    }

    char majorDelimiter = '|';
    char minorDelimiter = '~';

    string BuildMessage(EntityType entityType, string entityName, KinectTask task, object[] taskVariables)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(entityType.ToString());
        sb.Append(majorDelimiter);
        sb.Append(entityName);
        sb.Append(majorDelimiter);
        sb.Append(task.ToString());
        sb.Append(majorDelimiter);
        foreach (object variable in taskVariables)
        {
            sb.Append(variable.ToString());
            sb.Append(minorDelimiter);
        }
        sb.Append(majorDelimiter);
        return sb.ToString();
    }

    void SendServerMessage(KinectTask task, object[] taskVariables)
    {
        string message = BuildMessage(EntityType.PROVIDER, providerName, task, taskVariables);
        byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
        serverSocket.SendTo(data, serverEndpoint);
    }


    void SendServerMessage(KinectTask task)
    {
        string message = BuildMessage(EntityType.PROVIDER, providerName, task, new object[0]);
        byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
        serverSocket.SendTo(data, serverEndpoint);
    }


    void JoinServer(string clientName, EntityType clientRole)
    {
        try
        {
            if (string.IsNullOrEmpty(serverHostname))
            {
                print("Please specify UDP server !");
                return;
            }

            var addresses = Dns.GetHostAddresses(serverHostname);
            serverEndpoint = new IPEndPoint(addresses[0], serverPort);

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // TODO: Make this a string builder, and not a either/or switch
            SendServerMessage(KinectTask.JOIN);
            print("Server JOIN request sent");
        }
        catch (Exception x)
        {
            print("Error: " + x.ToString());
        }
    }

    void LeaveServer(string clientName, EntityType clientRole)
    {
        try
        {
            SendServerMessage(KinectTask.LEAVE);
            print("Server LEAVE request sent");
        }
        catch (Exception x)
        {
            print("Error: " + x.ToString());
        }
    }

    private void ProcessMessage(KinectTask task, string[] taskVariables)
    {
        int frameNumber, blockNumber;
        FrameType frameType;
        switch (task)
        {
            case KinectTask.CONFIRM:
                KinectTask confirmedTask = (KinectTask)Enum.Parse(typeof(KinectTask), taskVariables[0]);
                int frameNum, blockNum;
                FrameType type;
                switch (confirmedTask)
                {
                    case KinectTask.FINISHFRAME:
                        frameNum = int.Parse(taskVariables[1]);
                        type = (FrameType)Enum.Parse(typeof(FrameType), taskVariables[2]);
                        if (frameNum == currentFramePair.frameNumber && type == FrameType.COLOR)
                            currentFramePair.color.isFinished = true;
                        else if (frameNum == currentFramePair.frameNumber && type == FrameType.DEPTH)
                            currentFramePair.depth.isFinished = true;
                        print("Data transfer finished for " + frameNum + " ( " + type + ")");
                        break;

                    case KinectTask.ADDFRAME:
                        frameNum = int.Parse(taskVariables[1]);
                        type = (FrameType)Enum.Parse(typeof(FrameType), taskVariables[2]);
                        print("Frame " + frameNum + "(" + type + ") was successfully added to the server");
                        break;

                    case KinectTask.ADDBLOCK:
                        frameNum = int.Parse(taskVariables[1]);
                        type = (FrameType)Enum.Parse(typeof(FrameType), taskVariables[2]);
                        blockNum = int.Parse(taskVariables[3]);
                        print("Frame " + frameNum + "(" + type + ") Block # " + blockNum + " was successfully added to the server");
                        break;

                    case KinectTask.JOIN:
                        EntityType role = (EntityType)Enum.Parse(typeof(EntityType), taskVariables[1]);
                        string name = taskVariables[2];
                        print("Successfully joined server as " + name + "(" + role + ")");
                        connectionState = ConnectionState.JoinedServer;
                        break;
                }
                break;
            case KinectTask.ALERT:
                KinectTask alertedTask = (KinectTask)Enum.Parse(typeof(KinectTask), taskVariables[0]);
                print("Alerted: " + alertedTask);
                break;
            case KinectTask.FAIL:
                KinectTask failedTask = (KinectTask)Enum.Parse(typeof(KinectTask), taskVariables[0]);
                FailReason failedReason = (FailReason)Enum.Parse(typeof(FailReason), taskVariables[1]);
                print("Failed: " + failedTask + "(" + failedReason + ")");

                if (failedTask == KinectTask.ADDBLOCK && failedReason == FailReason.MISSINGFRAME) {
                    currentFramePair.color.notifyServer();
                    currentFramePair.depth.notifyServer();
                }
                break;
            case KinectTask.REQUESTBLOCK:
                frameNumber = int.Parse(taskVariables[0]);
                frameType = (FrameType)Enum.Parse(typeof(FrameType), taskVariables[1]);
                blockNumber = int.Parse(taskVariables[2]);
                print("Block Requested: " + frameNumber + "||" + frameType + "||" + blockNumber);
                if(currentFramePair.frameNumber == frameNumber)
                {
                    currentFramePair.sendBlock(frameType, blockNumber);
                }
                break;
            default:
                break;
        }
    }

    private void OnDataRecieved(object sender, SocketAsyncEventArgs e)
    {
        string received = Encoding.ASCII.GetString(e.Buffer);
        //print("Recieved: " + received);
        string[] split = received.Split(majorDelimiter);
        EntityType entityType = (EntityType)Enum.Parse(typeof(EntityType), split[0]);
        string entityName = split[1];
        KinectTask task = (KinectTask)Enum.Parse(typeof(KinectTask), split[2]);
        string[] taskVariables = split[3].Split(minorDelimiter);
        ProcessMessage(task, taskVariables);
    }

    public class FramePair
    {
        public int frameNumber;
        public Frame color;
        public Frame depth;
        public bool transmissionStarted;
        public bool transmissionFinished
        {
            get
            {
                return (color.isFinished && depth.isFinished);
            }
        }

        public FramePair(KinectProvider2D provider, int frameNumber, byte[] colorData, byte[] depthData, KinectCompressionType compressionType)
        {
            this.frameNumber = frameNumber;
            // ToArray is used to make a copy of the arrays in memory that wont be touched by the capture thread
            color = new Frame(provider, frameNumber, colorData.ToArray(), FrameType.COLOR, compressionType);
            depth = new Frame(provider, frameNumber, depthData.ToArray(), FrameType.DEPTH, compressionType);
        }
        
        public void SendFramePairToServer(int maxBytesPerBlock)
        {
            transmissionStarted = true;
            color.prepareFrame(maxBytesPerBlock);
            color.notifyServer();
            color.sendAllBlocks();
            color.sendFrameFinishedSignaltoServer();

            depth.prepareFrame(maxBytesPerBlock);
            depth.notifyServer();
            depth.sendAllBlocks();
            depth.sendFrameFinishedSignaltoServer();
        }

        public void sendBlock(FrameType type, int blockNumber)
        {
            switch (type)
            {
                case FrameType.COLOR:
                    color.sendBlock(blockNumber);
                    break;
                case FrameType.DEPTH:
                    depth.sendBlock(blockNumber);
                    break;
            }
        }

    }

    public class Frame
    {
        public KinectProvider2D provider;
        public int frameNumber;
        public byte[] raw;
        public byte[] compressed;
        public List<string> blocks;
        public KinectCompressionType compressionType;
        public FrameType type;
        public int numberOfBlocks;
        public KinectConfiguration configuration;
        public bool isFinished;

        public Frame(KinectProvider2D provider, int frameNumber, byte[] raw, FrameType type, KinectCompressionType compressionType)
        {
            this.provider = provider;
            this.configuration = provider.kinectSettings;
            this.frameNumber = frameNumber;
            this.raw = raw;
            this.type = type;
            this.compressionType = compressionType;
        }

        public static byte[] CompressLZ4(byte[] data)
        {
            byte[] compressedArray = new byte[LZ4Codec.MaximumOutputSize(data.Length)];
            int num = LZ4Codec.Encode(data, 0, data.Length, compressedArray, 0, compressedArray.Length, LZ4Level.L00_FAST);
            Array.Resize(ref compressedArray, num);
            return compressedArray;
        }

        public void prepareFrame( int maxBytesPerBlock)
        {
            string finalData = "";
            switch (compressionType)
            {
                case KinectCompressionType.NONE:
                    finalData = Convert.ToBase64String(raw);
                    break;
                case KinectCompressionType.LZ4:
                    finalData = Convert.ToBase64String(CompressDataLZ4(raw));
                    break;
            }

            blocks = new List<string>();
            for (int i = 0; i < finalData.Length; i += maxBytesPerBlock)
            {
                if ((i + maxBytesPerBlock) < finalData.Length)
                    blocks.Add(finalData.Substring(i, maxBytesPerBlock));
                else
                    blocks.Add(finalData.Substring(i));
            }
            numberOfBlocks = blocks.Count;
            print(numberOfBlocks);
        }

        public void notifyServer()
        {
            provider.SendServerMessage(KinectTask.ADDFRAME, new object[] { frameNumber, type, numberOfBlocks, compressionType, configuration.Serialized });
        }

        public void sendFrameFinishedSignaltoServer()
        {
            provider.SendServerMessage(KinectTask.FINISHFRAME, new object[] {frameNumber, type});
        }

        public void sendAllBlocks()
        {
            for (int i = 0; i < numberOfBlocks; i++)
            {
                provider.SendServerMessage(KinectTask.ADDBLOCK, new object[] { frameNumber, type, i, blocks[i] });
            }
        }

        public void sendBlock(int blockNumber)
        {
            provider.SendServerMessage(KinectTask.ADDBLOCK, new object[] { frameNumber, type, blockNumber, blocks[blockNumber]});
        }

    }

    public FramePair currentFramePair;

    public bool MaintainFramePair(byte[] colorData, byte[] depthData, KinectCompressionType compressionType)
    {
        if (currentFramePair == null || currentFramePair.transmissionFinished)
        {
            int lastFrameNumber = 0;
            if(currentFramePair != null)
            {
                lastFrameNumber = currentFramePair.frameNumber;
            }
            currentFramePair = new FramePair(this, lastFrameNumber+1, colorData, depthData, compressionType);
            return true;
        }
        else
        {
            return false;
        }
    }


    #endregion

  

}
