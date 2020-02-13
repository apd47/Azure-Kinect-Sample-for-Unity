using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Threading.Tasks;
using UnityEngine;

public class KinectGeoshader : MonoBehaviour
{
    int depthWidth;
    int depthHeight;
    int pointNum;
    int nearClip = 300;
    int colorWidth;
    int colorHeight;

    int width;
    int height;

    Mesh mesh;
    Vector3[] vertices;
    int[] indeces;
    Color32[] col;
    Texture2D texture;
    Vector3 currentAngles;


    Transformation transformation;
    Device device;

    public TransformationMode transformationMode;
    public ColorResolution colorResolution;
    public DepthMode depthMode;
    public FPS fps;

    public bool collectIMUData;
    public bool collectCameraData;

    public float accelerometerMinValid = 0.5f;
    public float accelerometerMaxValid = 2.0f;
    public float accelerometerWeighting = 0.02f;

    public enum TransformationMode
    {
        ColorToDepth,
        DepthToColor
    }

    private void OnDestroy()
    {
        device.StopCameras();
        device.StopImu();
    }

    void Start()
    {
        InitKinect();
        InitMesh();

        Task CameraLooper = CameraLoop(device);
        Task IMULooper = IMULoop(device);
    }

    void InitKinect()
    {
        device = Device.Open(0);
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

        depthWidth = device.GetCalibration().DepthCameraCalibration.ResolutionWidth;
        depthHeight = device.GetCalibration().DepthCameraCalibration.ResolutionHeight;

        colorWidth = device.GetCalibration().ColorCameraCalibration.ResolutionWidth;
        colorHeight = device.GetCalibration().ColorCameraCalibration.ResolutionHeight;

        switch (transformationMode)
        {
            case TransformationMode.ColorToDepth:
                width = depthWidth;
                height = depthHeight;
                pointNum = depthWidth * depthHeight;
                break;
            case TransformationMode.DepthToColor:
                width = colorWidth;
                height = colorHeight;
                pointNum = colorWidth * colorHeight;
                break;
        }
    }

    void InitMesh()
    {
        mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        texture = new Texture2D(width, height);

        col = new Color32[pointNum];

        vertices = new Vector3[pointNum];
        Vector2[] uv = new Vector2[pointNum];

        Vector3[] normals = new Vector3[pointNum];
        indeces = new int[6 * (width - 1) * (height - 1)];
        int index = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                uv[index] = new Vector2(((float)(x + 0.5f) / (float)(width)), ((float)(y + 0.5f) / ((float)(height))));
                normals[index] = new Vector3(0, -1, 0);
                index++;
            }
        }

        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.normals = normals;
        gameObject.GetComponent<MeshRenderer>().materials[0].mainTexture = texture;

        gameObject.GetComponent<MeshFilter>().mesh = mesh;
    }

    DateTime lastFrame;
    float dt;

    private async Task CameraLoop(Device device)
    {
        lastFrame = DateTime.Now;
        currentAngles = this.transform.rotation.eulerAngles;
        while (true)
        {
            if (collectCameraData)
            {
                using (Capture capture = await Task.Run(() => device.GetCapture()).ConfigureAwait(true))
                {
                    BGRA[] colorArray;
                    Image cloudImage;
                    Short3[] PointCloud;

                    int triangleIndex = 0;
                    int pointIndex = 0;
                    int topLeft, topRight, bottomLeft, bottomRight;
                    int tl, tr, bl, br;

                    switch (transformationMode)
                    {
                        case TransformationMode.ColorToDepth:
                            Image modifiedColor = transformation.ColorImageToDepthCamera(capture);
                            colorArray = modifiedColor.GetPixels<BGRA>().ToArray();
                            cloudImage = transformation.DepthImageToPointCloud(capture.Depth);
                            PointCloud = cloudImage.GetPixels<Short3>().ToArray();
                            break;

                        case TransformationMode.DepthToColor:
                            Image modifiedDepth = transformation.DepthImageToColorCamera(capture);
                            colorArray = capture.Color.GetPixels<BGRA>().ToArray();
                            cloudImage = transformation.DepthImageToPointCloud(modifiedDepth, CalibrationDeviceType.Color);
                            PointCloud = cloudImage.GetPixels<Short3>().ToArray();
                            break;

                        default:
                            colorArray = new BGRA[width * height];
                            PointCloud = new Short3[width * height];
                            break;
                    }

                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {

                            vertices[pointIndex].x = PointCloud[pointIndex].X * 0.001f;
                            vertices[pointIndex].y = -PointCloud[pointIndex].Y * 0.001f;
                            vertices[pointIndex].z = PointCloud[pointIndex].Z * 0.001f;

                            col[pointIndex].a = 255;
                            col[pointIndex].b = colorArray[pointIndex].B;
                            col[pointIndex].g = colorArray[pointIndex].G;
                            col[pointIndex].r = colorArray[pointIndex].R;

                            if (x != (width - 1) && y != (height - 1))
                            {
                                topLeft = pointIndex;
                                topRight = topLeft + 1;
                                bottomLeft = topLeft + width;
                                bottomRight = bottomLeft + 1;
                                tl = PointCloud[topLeft].Z;
                                tr = PointCloud[topRight].Z;
                                bl = PointCloud[bottomLeft].Z;
                                br = PointCloud[bottomRight].Z;

                                if (tl > nearClip && tr > nearClip && bl > nearClip)
                                {
                                    indeces[triangleIndex++] = topLeft;
                                    indeces[triangleIndex++] = topRight;
                                    indeces[triangleIndex++] = bottomLeft;
                                }
                                else
                                {
                                    indeces[triangleIndex++] = 0;
                                    indeces[triangleIndex++] = 0;
                                    indeces[triangleIndex++] = 0;
                                }

                                if (bl > nearClip && tr > nearClip && br > nearClip)
                                {
                                    indeces[triangleIndex++] = bottomLeft;
                                    indeces[triangleIndex++] = topRight;
                                    indeces[triangleIndex++] = bottomRight;
                                }
                                else
                                {
                                    indeces[triangleIndex++] = 0;
                                    indeces[triangleIndex++] = 0;
                                    indeces[triangleIndex++] = 0;
                                }
                            }
                            pointIndex++;
                        }
                    }

                    texture.SetPixels32(col);
                    texture.Apply();

                    mesh.vertices = vertices;

                    mesh.triangles = indeces;
                    mesh.RecalculateBounds();
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
