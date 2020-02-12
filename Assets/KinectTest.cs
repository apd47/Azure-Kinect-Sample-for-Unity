
using UnityEngine;
using Microsoft.Azure.Kinect.Sensor;
using System.Threading.Tasks;

public class KinectTest : MonoBehaviour
{
    int depthWidth;
    int depthHeight;
    int pointNum;

    Mesh mesh;
    int[] indeces;
    Color32[] col;
    Texture2D texture;

    Transformation transformation;
    Device device;

    private void OnDestroy()
    {
        device.StopCameras();
    }

    void Start()
    {
        InitKinect(); 
        Task t = KinectLoop(device);
    }

    void InitKinect()
    {
        device = Device.Open(0);
        device.StartCameras(new DeviceConfiguration
        {
            ColorFormat = ImageFormat.ColorBGRA32,
            ColorResolution = ColorResolution.R720p,
            DepthMode = DepthMode.NFOV_Unbinned,
            SynchronizedImagesOnly = true,
            CameraFPS = FPS.FPS30,
        });

        transformation = device.GetCalibration().CreateTransformation();
        depthWidth = device.GetCalibration().DepthCameraCalibration.ResolutionWidth;
        depthHeight = device.GetCalibration().DepthCameraCalibration.ResolutionHeight;
        pointNum = depthWidth * depthHeight;
    }
   
    private async Task KinectLoop(Device device)
    {
        while (true)
        {
            using (Capture capture = await Task.Run(() => device.GetCapture()).ConfigureAwait(true))
            {
                Image modifiedColor = transformation.ColorImageToDepthCamera(capture);
                BGRA[] colorArray = modifiedColor.GetPixels<BGRA>().ToArray();

                int pointIndex = 0;
                for (int y = 0; y < depthHeight; y++)
                {
                    for (int x = 0; x < depthWidth; x++)
                    {
                        col[pointIndex].a = 255;
                        col[pointIndex].b = colorArray[pointIndex].B;
                        col[pointIndex].g = colorArray[pointIndex].G;
                        col[pointIndex].r = colorArray[pointIndex].R;
                        pointIndex++;
                    }
                }

                texture.SetPixels32(col);
                texture.Apply();
            }
        }
    }
}
