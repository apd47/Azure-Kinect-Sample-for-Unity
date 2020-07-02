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

public class Depth2Texture : MonoBehaviour
{
    public Texture2D depthTexture;

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


    DateTime lastFrame;
    float dt;

    Image finalDepth;
    Image finalColor;

    public MeshRenderer mesh;

    [HideInInspector]
    public byte[] depthData;

    bool running = true;

    DepthMode lastDepthMode;
    ColorResolution lastColorResolution;
    FPS lastFPS;
    TransformationMode lastTransformationMode;
    Vector3 lastVolumeScale;
    Vector2 lastDepthRangeModifier;

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

        if (device != null)
        {
            device.StopCameras();
            device.StopImu();
            device.Dispose();
        }
       
        if (depthTexture != null)
            Destroy(depthTexture);
      
    }

  
    private void Update()
    {
        CheckForChanges();
    }

    void CheckForChanges()
    {
        if ((lastDepthMode != depthMode) || (lastColorResolution != colorResolution) || (lastFPS != fps) || (lastTransformationMode != transformationMode))
        {
            StartKinect();
            lastDepthMode = depthMode;
            lastColorResolution = colorResolution;
            lastFPS = fps;
            lastTransformationMode = transformationMode;
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
        print("Started");
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

                
                    if (depthTexture == null)
                    {
                        depthTexture = new Texture2D(finalDepth.WidthPixels, finalDepth.HeightPixels, TextureFormat.R16, false);
                        depthData = new byte[finalDepth.Memory.Length];
                        print("Made Depth Texture");
                    }

                    depthData = finalDepth.Memory.ToArray();
                    depthTexture.LoadRawTextureData(depthData);
                    depthTexture.Apply();

                    matt.SetTexture("_MainTex", depthTexture);
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
        while (running)
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
