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
using Microsoft.Azure.Kinect.Sensor.WinForms;
using BitMiracle.LibJpeg;

public class KinectBuffer2D : MonoBehaviour
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
    float[] extractedVolumeBuffer;
    byte[] extractedVolumeBytes;
    Microsoft.Azure.Kinect.Sensor.Image finalColor;
    byte[] colorData;
    Microsoft.Azure.Kinect.Sensor.Image finalDepth;
    byte[] depthData;
    Texture2D colorTexture;
    Texture2D depthTexture;
    Texture2D oldDepthTexture;

    [Header("Rendering")]
    public MeshRenderer colorPreview;
    public MeshRenderer depthPreview;

    [Header("Recording")]
    public string filepath;
    public int maxFileSizeMb = 500;
    public bool startRecording;
    public bool pauseRecording;
    public bool stopRecording;
    bool saveToFile;
    bool triggerWriteToFile;
    int compressedBytes = 0;
    int maxRecordingSeconds;
    int savedFrameCount;
    string[] base64Frames;

    [Header("Networking")]
    public bool sendToServer;
    public string providerName;
    public string serverHostname;
    public int serverPort;
    ConnectionState connectionState = ConnectionState.Disconnected;
    IPEndPoint serverEndpoint;
    Socket serverSocket;

    #endregion

    #region Unity


    private void Update()
    {
        ListenForData(serverSocket);
        SavingLoop();

        if (startRecording)
        {
            StartRecording();
            startRecording = false;
        }
        if (pauseRecording)
        {
            PauseRecording();
            pauseRecording = false;
        }
        if (stopRecording)
        {
            StopRecording();
            stopRecording = false;
        }
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

    public void StartKinect()
    {
        if (running)
        {
            OnCloseKinect();
        }
        device = Device.Open(deviceID);
        device.StartCameras(new DeviceConfiguration
        {
            ColorFormat = Microsoft.Azure.Kinect.Sensor.ImageFormat.ColorBGRA32,
            ColorResolution = kinectSettings.colorResolution,
            DepthMode = kinectSettings.depthMode,
            SynchronizedImagesOnly = true,
            CameraFPS = kinectSettings.fps,
        });

        transformation = device.GetCalibration().CreateTransformation();
        running = true;
        Task CameraLooper = CameraLoop(device);
    }

    public void StopKinect()
    {
        running = false;
        OnCloseKinect();
    }

    private async Task CameraLoop(Device device)
    {
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

                    if (colorTexture == null)
                    {
                        colorTexture = new Texture2D(finalColor.WidthPixels, finalColor.HeightPixels, TextureFormat.BGRA32, false);
                        colorData = new byte[finalColor.Memory.Length];
                        colorPreview.material.SetTexture("_MainTex", colorTexture);
                        print("Made Color Texture");
                    }

                    if (depthTexture == null)
                    {
                        depthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        oldDepthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        depthData = new byte[finalDepth.Memory.Length];
                        depthPreview.material.SetTexture("_MainTex", colorTexture);
                        print("Made Depth Texture");
                    }

                    colorData = finalColor.Memory.ToArray();
                    System.Drawing.Bitmap colorBitmap = finalColor.CreateBitmap();
                    colorTexture.LoadRawTextureData(colorData);
                    colorTexture.Apply();

                    depthData = finalDepth.Memory.ToArray();
                    System.Drawing.Bitmap depthBitmap = finalDepth.CreateBitmap();

                    depthTexture.LoadRawTextureData(depthData);
                    depthTexture.Apply();

                    // TODO: Test which is faster, or if a dedicated thread would be best
                    //Option 1: Use the UserWorkItem Threadpool to manage thread for me

                    ThreadPool.QueueUserWorkItem((state) => Postprocess2D(colorBitmap, depthBitmap));

                    //Option 2: Spawn a thread for each frame
                    //new Thread(() => Postprocess(extractedVolumeBytes)).Start();

                    if (compressedBytes == 0)
                    {
                        byte[] compressedArray = CompressData(colorData);
                        compressedBytes = compressedArray.Length;
                        maxRecordingSeconds = (maxFileSizeMb * 1000 * 1000) / (compressedBytes * KinectUtilities.FPStoInt(kinectSettings.fps));
                    }

                    colorPreview.material.SetTexture("_MainTex", colorTexture);
                    depthPreview.material.SetTexture("_MainTex", depthTexture);
                    UnityEngine.Graphics.CopyTexture(depthTexture, oldDepthTexture);
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

    public MemoryStream GetJpegStream(System.Drawing.Bitmap kinectImage, CompressionParameters parameters)
    {
        MemoryStream output = new MemoryStream();
        JpegImage image = new JpegImage(kinectImage);
        image.WriteJpeg(output, parameters);
        return output;
    }

    public void Postprocess2D(System.Drawing.Bitmap colorData, System.Drawing.Bitmap depthData)
    {
        int thisFrameID = frameID;
        frameID++;

        if (sendToServer)
        {
            //byte[] compressedColor = CompressDataJpeg(colorData);

            //byte[] compressedColor= CompressDataLZ4(colorData);
            //byte[] compressedDepth = CompressDataLZ4(depthData);

            CompressionParameters parameters = new CompressionParameters();
            parameters.Quality = 0;
            parameters.SmoothingFactor = 50;
            parameters.SimpleProgressive = false;

            try
            {
                if (connectionState == ConnectionState.JoinedServer)
                {
                    SendFrameToServer(thisFrameID, FrameType.Color, Convert.ToBase64String(GetJpegStream(colorData, parameters).ToArray()));
                    SendFrameToServer(thisFrameID, FrameType.Depth, Convert.ToBase64String(GetJpegStream(depthData, parameters).ToArray()));
                }
            }
            catch (Exception ex)
            {
                print(ex.Message);
            }
        }

        if (saveToFile)
        {
            print("2D saving not implemented yet");
            // Use LZ4 
            //byte[] compressedColor = CompressDataLZ4(colorData);
            //byte[] compressedDepth = CompressDataLZ4(depthData);

            //try
            //{
            //    if (thisFrameID < base64Frames.Length)
            //    {
            //        base64Frames[thisFrameID] = Convert.ToBase64String(compressedColor);
            //        savedFrameCount++;
            //    }
            //    else
            //    {
            //        StopRecording();
            //    }
            //}
            //catch (Exception ex)
            //{
            //    print(ex.Message);
            //}
        }
    }


    // TODO: Not the best solution, because it loses the absolute frame ID, but not sure what I'd use that for?
    public void StartRecording()
    {
        base64Frames = new string[maxRecordingSeconds * KinectUtilities.FPStoInt(kinectSettings.fps)];

        frameID = savedFrameCount;
        saveToFile = true;
        print("Recording started. Max recording length is " + maxRecordingSeconds + " seconds");
    }

    public void PauseRecording()
    {
        saveToFile = false;
        print("Recording paused with " + savedFrameCount + " frames in the write buffer. Click stop to save to file, or record to continue appending to the current buffer");
    }

    public void StopRecording()
    {
        saveToFile = false;
        triggerWriteToFile = true;
        print("Recording stopped with " + savedFrameCount + " frames in the write buffer. Attempting to write to file...");
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

    #region Saving

    private void SavingLoop()
    {
        if (triggerWriteToFile)
        {
            new Thread(() => WriteDataToFile()).Start();
            triggerWriteToFile = false;
        }
    }

    private void WriteDataToFile()
    {
        print("Saving volume data....");
        saveToFile = false;
        int i = 0;
        StreamWriter writer = new System.IO.StreamWriter(filepath, false, Encoding.UTF8);

        writer.Write(kinectSettings.Serialized + KinectUtilities.configBreak);
        foreach (string frame in base64Frames)
        {
            if (frame != null && frame != "")
            {
                i++;
                writer.Write(frame + KinectUtilities.frameBreak);
            }
        }
        writer.Close();
        writer.Dispose();
        print(i + " frames of volume data have been saved to " + filepath);
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
                print("Please specify UDP server !");
                return;
            }

            var addresses = Dns.GetHostAddresses(serverHostname);
            serverEndpoint = new IPEndPoint(addresses[0], serverPort);

            serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            serverSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            // TODO: Make this a string builder, and not a either/or switch
            string message = "JOIN" + "|" + clientRole + "|" + clientName + "|" + kinectSettings.Serialized;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            serverSocket.SendTo(data, serverEndpoint);
            print("Server JOIN request sent");
        }
        catch (Exception x)
        {
            print("Error: " + x.ToString());
        }
    }

    void LeaveServer(string clientName, ClientRole clientRole)
    {

        try
        {
            // TODO: Make this a string builder, and not a either/or switch
            string message = "LEAVE" + "|" + clientRole + "|" + clientName;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
            serverSocket.SendTo(data, serverEndpoint);
            print("Server LEAVE request sent");
        }
        catch (Exception x)
        {
            print("Error: " + x.ToString());
        }
    }

    private void OnDataRecieved(object sender, SocketAsyncEventArgs e)
    {
        string recieved = Encoding.ASCII.GetString(e.Buffer);
        //print("Recieved: " + recieved);
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
                            //print("Successfully sent frame " + split[3] + " to server");
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
                        //print("Frame " + split[3] + " recieved from provider " + split[2]);
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
    void SendFrameToServer(int frameNumber, FrameType type, string frameData)
    {
        if (connectionState == ConnectionState.JoinedServer)
        {
            string message = "PROVIDE" + "|" + providerName + "|" + frameNumber + "|" + type.ToString() + "|" + frameData;

            byte[] data = System.Text.Encoding.UTF8.GetBytes(message);

            try
            {
                serverSocket.SendTo(data, serverEndpoint);
            }
            catch (Exception x)
            {
                print("Error: " + x.ToString());
                print(data.Length);
            }
        }
    }
    #endregion

}
