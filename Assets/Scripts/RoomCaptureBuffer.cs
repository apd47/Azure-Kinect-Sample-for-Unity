using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Threading.Tasks;
using UnityEngine;

public class RoomCaptureBuffer : MonoBehaviour
{

    Texture2D colorTexture;
    Texture2D depthTexture;
    Texture2D oldDepthTexture;

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
    public ComputeBuffer volumeBuffer2;

    public int maxDepthMM = 10000;
    public int minDepthMM = 500;
    bool running = true;

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
        volumeBuffer.Release();
        running = false;
        device.StopCameras();
        device.StopImu();
        Destroy(colorTexture);
        Destroy(depthTexture);
        Destroy(oldDepthTexture);
    }

    void Start()
    {
        InitKinect();
        Task CameraLooper = CameraLoop(device);
        Task IMULooper = IMULoop(device);
    }

    void InitKinect()
    {
        device = Device.Open(deviceID);
        device.StartCameras(new DeviceConfiguration
        {
            ColorFormat = ImageFormat.ColorBGRA32,
            ColorResolution = colorResolution,
            DepthMode = depthMode,
            SynchronizedImagesOnly = true,
            CameraFPS = fps,
        });

        minDepthMM = depthRanges[(int)device.CurrentDepthMode].x;
        maxDepthMM = depthRanges[(int)device.CurrentDepthMode].y;


        float worldscaleDepth = (maxDepthMM - minDepthMM) / 1000;
        Vector3 currentScale = mesh.transform.localScale;
        mesh.transform.localScale = new Vector3(currentScale.x, currentScale.y, worldscaleDepth);
        mesh.transform.localPosition = new Vector3(0, 0, 0.5f * worldscaleDepth);
        device.StartImu();
        transformation = device.GetCalibration().CreateTransformation();
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

                    if(volumeBuffer == null)
                    {
                        matrixSize = new Vector3Int((int)(finalColor.WidthPixels * volumeScale.x), (int)(finalColor.HeightPixels * volumeScale.y), (int)((depthRanges[(int)device.CurrentDepthMode].y - depthRanges[(int)device.CurrentDepthMode].x) / 11 * volumeScale.z));
                        volumeBuffer = new ComputeBuffer(matrixSize.x * matrixSize.y * matrixSize.z, 4 * sizeof(float), ComputeBufferType.Default);
                    }

                    if (colorTexture == null)
                    {
                        colorTexture = new Texture2D(finalColor.WidthPixels, finalColor.HeightPixels, TextureFormat.BGRA32, false);
                        colorData = new byte[finalColor.Memory.Length];
                    }

                    if (depthTexture == null)
                    {
                        depthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        oldDepthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        depthData = new byte[finalDepth.Memory.Length];
                    }

                    colorData = finalColor.Memory.ToArray();
                    colorTexture.LoadRawTextureData(colorData);
                    colorTexture.Apply();

                    depthData = finalDepth.Memory.ToArray();
                    depthTexture.LoadRawTextureData(depthData);
                    depthTexture.Apply();

                    // Apply Buffer Updates
                    int kernelIndex = shader.FindKernel("ToBuffer");
                    shader.SetInt("_MatrixX", matrixSize.x);
                    shader.SetInt("_MatrixY", matrixSize.y);
                    shader.SetInt("_MatrixZ", matrixSize.z);
                    
                    shader.SetTexture(kernelIndex, "ColorTex", colorTexture);
                    shader.SetTexture(kernelIndex, "DepthTex", depthTexture);
                    shader.SetTexture(kernelIndex, "oldDepthTexture", oldDepthTexture);
                    shader.SetBuffer(kernelIndex, "ResultBuffer", volumeBuffer);
                    shader.SetInt("minDepth", minDepthMM);
                    shader.SetInt("maxDepth", maxDepthMM);
                    shader.Dispatch(kernelIndex, matrixSize.x/8, matrixSize.y/8, matrixSize.z/8);

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

    //public static void CompressTexture(Texture texture)
    //{

    //    foreach (FileInfo file in directorySelected.GetFiles("*.xml"))
    //        using (FileStream originalFileStream = file.OpenRead())
    //        {
    //            if ((File.GetAttributes(file.FullName) & FileAttributes.Hidden)
    //                != FileAttributes.Hidden & file.Extension != ".cmp")
    //            {
    //                using (FileStream compressedFileStream = File.Create(file.FullName + ".cmp"))
    //                {
    //                    using (DeflateStream compressionStream = new DeflateStream(compressedFileStream, CompressionMode.Compress))
    //                    {
    //                        originalFileStream.CopyTo(compressionStream);
    //                    }
    //                }

    //                FileInfo info = new FileInfo(directoryPath + "\\" + file.Name + ".cmp");
    //                Console.WriteLine("Compressed {0} from {1} to {2} bytes.", file.Name, file.Length, info.Length);
    //            }
    //        }
    //}

}
