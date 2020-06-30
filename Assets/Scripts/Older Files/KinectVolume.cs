using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Threading.Tasks;
using UnityEngine;

public class KinectVolume : MonoBehaviour
{

    public Texture2D colorTexture;
    public Texture2D depthTexture;
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
    [HideInInspector]
    public RenderTexture volumeTexture;

    public Vector3 volumeScale = new Vector3(0.5f, 0.5f, 1f);
    Vector3Int matrixSize;

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
        device.StopCameras();
        device.StopImu();
        Destroy(volumeTexture);
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

    DateTime lastFrame;
    float dt;

    Image finalDepth;
    Image finalColor;

    [HideInInspector]
    public byte[] colorData;

    public MeshRenderer mesh;

    [HideInInspector]
    public byte[] depthData;

    public int maxDepthMM = 10000;
    public int minDepthMM = 500;
    bool running = true;

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

                    if (volumeTexture == null)
                    {
                        matrixSize = new Vector3Int((int)(finalColor.WidthPixels * volumeScale.x), (int)(finalColor.HeightPixels * volumeScale.y), (int)((depthRanges[(int)device.CurrentDepthMode].y - depthRanges[(int)device.CurrentDepthMode].x) / 11 * volumeScale.z));
                        volumeTexture = new RenderTexture(matrixSize.x, matrixSize.y, 0, RenderTextureFormat.ARGB32);
                        volumeTexture.enableRandomWrite = true;
                        volumeTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
                        volumeTexture.volumeDepth = matrixSize.z;
                        volumeTexture.Create();
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

                    // Apply Texture Updates
                    int kernelIndex = shader.FindKernel("ToTexture");
                    shader.SetTexture(kernelIndex, "ColorTex", colorTexture);
                    shader.SetTexture(kernelIndex, "DepthTex", depthTexture);
                    shader.SetTexture(kernelIndex, "oldDepthTexture", oldDepthTexture);
                    shader.SetTexture(kernelIndex, "ResultTexture", volumeTexture);
                    shader.SetVector("_Size", new Vector4(matrixSize.x, matrixSize.y, matrixSize.z, 1));
                    shader.SetInt("minDepth", minDepthMM);
                    shader.SetInt("maxDepth", maxDepthMM);
                    shader.Dispatch(kernelIndex, matrixSize.x / 16, matrixSize.y / 16, 1);

                    matt.SetTexture("_Volume", volumeTexture);
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


}
