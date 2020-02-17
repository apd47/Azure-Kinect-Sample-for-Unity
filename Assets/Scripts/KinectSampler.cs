using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Threading.Tasks;
using UnityEngine;

public class KinectSampler : MonoBehaviour
{
    Vector3 currentAngles;
    Transformation transformation;
    Device device;

    public int deviceID = 0;

    [Header("Configuration")]
    public TransformationMode transformationMode;
    public ColorResolution colorResolution;
    public DepthMode depthMode;
    public Vector3 volumeScale = new Vector3(0.5f, 0.5f, 1f);

    public FPS fps;

    [Header("Data Collection")]
    public bool collectIMUData;
    public bool collectCameraData;


    float accelerometerMinValid = 0.5f;
    float accelerometerMaxValid = 2.0f;
    float accelerometerWeighting = 0.02f;

    Image finalDepth;
    Image finalColor;
    [HideInInspector]
    public byte[] colorData;
    [HideInInspector]
    public byte[] depthData;
    bool running = true;

    TimeSpan lastFrame;

    public KinectProcessor processor;


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

        processor.minDepthMM = depthRanges[(int)device.CurrentDepthMode].x;
        processor.maxDepthMM = depthRanges[(int)device.CurrentDepthMode].y;
        device.StartImu();
        transformation = device.GetCalibration().CreateTransformation();
    }


    private async Task CameraLoop(Device device)
    {
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

                    processor.matrixSize = new Vector3Int((int)(finalColor.WidthPixels * volumeScale.x), (int)(finalColor.HeightPixels * volumeScale.y), (int)((depthRanges[(int)device.CurrentDepthMode].y - depthRanges[(int)device.CurrentDepthMode].x) / 11 * volumeScale.z));

                    if (processor.colorTexture == null)
                    {
                        processor.colorTexture = new Texture2D(finalColor.WidthPixels, finalColor.HeightPixels, TextureFormat.BGRA32, false);
                        colorData = new byte[finalColor.Memory.Length];
                    }

                    if (processor.depthTexture == null)
                    {
                        processor.depthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        processor.oldDepthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        depthData = new byte[finalDepth.Memory.Length];
                    }

                    colorData = finalColor.Memory.ToArray();
                    processor.colorTexture.LoadRawTextureData(colorData);
                    processor.colorTexture.Apply();

                    depthData = finalDepth.Memory.ToArray();
                    processor.depthTexture.LoadRawTextureData(depthData);
                    processor.depthTexture.Apply();
                    processor.ProcessKinectData();
                    Graphics.CopyTexture(processor.depthTexture, processor.oldDepthTexture);
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
        currentAngles = this.transform.rotation.eulerAngles;
        while (true)
        {
            if (collectIMUData)
            {
                ImuSample imuSample = new ImuSample();
                await Task.Run(() => imuSample = device.GetImuSample()).ConfigureAwait(true);
                if (lastFrame == null)
                {
                    lastFrame = imuSample.AccelerometerTimestamp;
                }
                currentAngles = ComplementaryFilterEuler(imuSample, currentAngles, imuSample.AccelerometerTimestamp.Subtract(lastFrame).Milliseconds / 1000);
                lastFrame = imuSample.AccelerometerTimestamp;
                processor.currentIMUAngles = currentAngles;
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
