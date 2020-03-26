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

public class KinectBuffer : MonoBehaviour
{
    [HideInInspector]
    public Texture2D colorTexture;
    [HideInInspector]
    public Texture2D depthTexture;
    [HideInInspector]
    public Texture2D oldDepthTexture;

    Vector3 currentAngles;
    Transformation transformation;
    Device device;

    public int deviceID = 0;

    public TransformationMode transformationMode;
    public ColorResolution colorResolution;
    public DepthMode depthMode;
    public FPS fps;

    public bool collectIMUData;
    public bool collectCameraData;

    public float accelerometerMinValid = 0.5f;
    public float accelerometerMaxValid = 2.0f;
    public float accelerometerWeighting = 0.02f;

    [Header("Rendering Settings")]
    public ComputeShader shader;
    public Filter filter;
    public Vector3 volumeScale = new Vector3(0.5f, 0.5f, 1f);
    public Vector3Int matrixSize;

    DateTime lastFrame;
    float dt;

    Image finalDepth;
    Image finalColor;

    [HideInInspector]
    public byte[] colorData;

    public MeshRenderer mesh;

    [HideInInspector]
    public byte[] depthData;

    public ComputeBuffer volumeBuffer;

    public int maxDepthMM = 10000;
    public int minDepthMM = 500;
    public Vector2 depthRangeModifier;
    bool running = true;

    DepthMode lastDepthMode;
    ColorResolution lastColorResolution;
    FPS lastFPS;
    TransformationMode lastTransformationMode;
    Vector3 lastVolumeScale;
    Vector2 lastDepthRangeModifier;

    int frameID = 0;

    [Header("Networking")]
    public bool performCompression;
    public bool sendVolumeDataToClients;
    public string localServerIP;
    public int portNumber;

    int computeShaderKernelIndex = -1;
    float worldscaleDepth;

    public enum TransformationMode
    {
        ColorToDepth,
        DepthToColor,
        None
    }

    static Vector2Int[] depthRanges =
    {
        new Vector2Int(0,0),
        new Vector2Int(500, 3860),
        new Vector2Int(500, 5460),
        new Vector2Int(250, 2880),
        new Vector2Int(250, 2210)
    };

    private void OnDestroy()
    {
        running = false;
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

    public byte[] Compress(byte[] data)
    {
        int thisFrameID = frameID;
        frameID++;
        Stopwatch stopWatch = new Stopwatch();
        stopWatch.Start();
        using (MemoryStream output = new MemoryStream())
        {
            using (DeflateStream dstream = new DeflateStream(output, System.IO.Compression.CompressionLevel.Optimal))
            {
                dstream.Write(data, 0, data.Length);
            }

            byte[] outArray = output.ToArray();
            stopWatch.Stop();

            TimeSpan ts = stopWatch.Elapsed;
            print(thisFrameID + " || Raw Volume Frame Size: " + (float)data.Length / (1024 * 1024) + "Mb" + "|| Compressed Volume Frame to Size: " + (float)outArray.Length / (1024 * 1024) + "Mb in " + ts.TotalMilliseconds + "ms");

            if (myList == null)
            {
                print("TCP Socket not found. Initializing.");
                StartTCPServer();
            }
            else
            {
                if (connectedToClient)
                {
                    stopWatch.Reset();
                    stopWatch.Start();
                    SendOverSocket(outArray);
                    stopWatch.Stop();
                    print(thisFrameID + " || Sent Volume Frame of Size: " + (float)outArray.Length / (1024 * 1024) + "Mb in " + ts.TotalMilliseconds + "ms");
                }
                else
                {
                    print("No client available");
                }
            }

            return output.ToArray();
        }
    }

    Socket tcpSocket;
    TcpListener myList;
    bool connectedToClient = false;

    public void StartTCPServer()
    {
        try
        {
            IPAddress ipAd = IPAddress.Parse(localServerIP);
            myList = new TcpListener(ipAd, portNumber);

            /* Start Listeneting at the specified port */
            myList.Start();

            print("The server is running at port: " + portNumber);
            //print("The local End point is  :" + myList.LocalEndpoint);
            print("Waiting for a connection.....");

            tcpSocket = myList.AcceptSocket();
            connectedToClient = true;
            print("Connection accepted from " + tcpSocket.RemoteEndPoint);
        }
        catch (Exception e)
        {
            print("Error..... " + e.StackTrace);
        }
    }

    public void SendOverSocket(byte[] data)
    {
        tcpSocket.Send(data);
    }

    public byte[] Decompress(byte[] data)
    {
        MemoryStream input = new MemoryStream(data);
        MemoryStream output = new MemoryStream();
        using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
        {
            dstream.CopyTo(output);
        }
        return output.ToArray();
    }

    private void Update()
    {
        CheckForChanges();
    }

    void CheckForChanges()
    {
        if ((lastDepthMode != depthMode) || (lastColorResolution != colorResolution) || (lastFPS != fps) || (lastTransformationMode != transformationMode) || (lastVolumeScale != volumeScale) || (depthRangeModifier != lastDepthRangeModifier))
        {
            StartKinect();

            minDepthMM = (int)(depthRanges[(int)device.CurrentDepthMode].x * depthRangeModifier.x);
            maxDepthMM = (int)(depthRanges[(int)device.CurrentDepthMode].y * depthRangeModifier.y);
            worldscaleDepth = (maxDepthMM - minDepthMM) / 1000;

            mesh.transform.localPosition = new Vector3(0, 0, worldscaleDepth / 2);

            if (transformationMode == TransformationMode.DepthToColor)
                mesh.transform.localScale = new Vector3(1.6f * worldscaleDepth / 3, 0.9f * worldscaleDepth / 3, worldscaleDepth);
            if (transformationMode == TransformationMode.ColorToDepth)
                mesh.transform.localScale = new Vector3(worldscaleDepth / 3, worldscaleDepth / 3, worldscaleDepth);

            lastDepthMode = depthMode;
            lastColorResolution = colorResolution;
            lastFPS = fps;
            lastTransformationMode = transformationMode;
            lastVolumeScale = volumeScale;
            lastDepthRangeModifier = depthRangeModifier;
        }
    }


    void StartKinect()
    {
        OnDestroy();
        device = Device.Open(deviceID);
        device.StartCameras(new DeviceConfiguration
        {
            ColorFormat = ImageFormat.ColorBGRA32,
            ColorResolution = colorResolution,
            DepthMode = depthMode,
            SynchronizedImagesOnly = true,
            CameraFPS = fps,
        });
        device.StartImu();
        transformation = device.GetCalibration().CreateTransformation();
        running = true;
        Task CameraLooper = CameraLoop(device);
        Task IMULooper = IMULoop(device);
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
                    switch (transformationMode)
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
                        matrixSize = new Vector3Int((int)(finalColor.WidthPixels * volumeScale.x), (int)(finalColor.HeightPixels * volumeScale.y), (int)((depthRanges[(int)device.CurrentDepthMode].y - depthRanges[(int)device.CurrentDepthMode].x) / 11 * volumeScale.z));
                        volumeBuffer = new ComputeBuffer(matrixSize.x * matrixSize.y * matrixSize.z, 4 * sizeof(float), ComputeBufferType.Default);
                        print("Made Volume Buffer");
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

                    shader.Dispatch(computeShaderKernelIndex, matrixSize.x / 16, matrixSize.y / 16, 1);

                    byte[] colors = new byte[matrixSize.x * matrixSize.y * matrixSize.z];
                    volumeBuffer.GetData(colors);

                    ThreadPool.QueueUserWorkItem((state) => Compress((Byte[])state), colors);

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

    private int _a;
    private int _b;

    public KinectBuffer(int a, int b)
    {
        (_a, _b) = (a, b);
    }

    private void configureComputeShader()
    {
        // Apply Buffer Updates
        computeShaderKernelIndex = shader.FindKernel("ToBuffer");
        shader.SetInt("_MatrixX", matrixSize.x);
        shader.SetInt("_MatrixY", matrixSize.y);
        shader.SetInt("_MatrixZ", matrixSize.z);

        shader.SetTexture(computeShaderKernelIndex, "ColorTex", colorTexture);
        shader.SetTexture(computeShaderKernelIndex, "DepthTex", depthTexture);
        shader.SetTexture(computeShaderKernelIndex, "oldDepthTexture", oldDepthTexture);
        shader.SetBuffer(computeShaderKernelIndex, "ResultBuffer", volumeBuffer);
        shader.SetInt("minDepth", minDepthMM);
        shader.SetInt("maxDepth", maxDepthMM);

        if (filter.useFilter)
        {
            Vector4 filterValue = filter.getFilterValue();
            shader.SetVector("filterColor", filterValue);
            shader.SetInt("filterType", (int)filter.filterType);
        }
        else
        {
            shader.SetVector("filterColor", new Vector4(0.5f, 0.5f, 0.5f, 0.5f));
            shader.SetInt("filterType", 0);
        }
    }

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

}
