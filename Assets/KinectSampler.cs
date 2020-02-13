//using UnityEngine;
//using System.Collections;

//public class KinectSampler : MonoBehaviour
//{
//    public enum Mode
//    {
//        Volume,
//        Pointcloud
//    }

//    public Mode mode = Mode.Volume;
//    [HideInInspector]
//    public Texture2D colorTexture;
//    public byte[] colorData;
//    public int colorWidth, colorHeight;

//    [HideInInspector]
//    public Texture2D depthTexture;
//    public ushort[] depthData;
//    private byte[] rawDepthTextureData;
//    public int depthWidth, depthHeight;
//    public int maxDepthMM = 10000;
//    public int minDepthMM = 500;
//    private KinectSensor _Sensor;
//    private CoordinateMapper _Mapper;
//    private MultiSourceFrameReader _Reader;

//    [Header("Rendering Settings")]
//    public ComputeShader shader;
//    [HideInInspector]
//    public RenderTexture volumeTexture;

//    public int size = 64;


//    void Start()
//    {
//        _Sensor = KinectSensor.GetDefault();

//        if (_Sensor != null)
//        {
//            _Reader = _Sensor.OpenMultiSourceFrameReader(FrameSourceTypes.Color | FrameSourceTypes.Depth);
//            _Mapper = _Sensor.CoordinateMapper;

//            Setup Color Sampling
//           var colorFrameDesc = _Sensor.ColorFrameSource.CreateFrameDescription(ColorImageFormat.Rgba);
//            colorWidth = colorFrameDesc.Width;
//            colorHeight = colorFrameDesc.Height;
//            colorTexture = new Texture2D(colorWidth, colorHeight, TextureFormat.RGBA32, false);
//            colorData = new byte[colorFrameDesc.BytesPerPixel * colorFrameDesc.LengthInPixels];

//            Setup Depth Sampling
//           var depthFrameDesc = _Sensor.DepthFrameSource.FrameDescription;
//            depthWidth = depthFrameDesc.Width;
//            depthHeight = depthFrameDesc.Height;
//            depthTexture = new Texture2D(depthWidth, depthHeight, TextureFormat.RGB24, false);
//            depthData = new ushort[depthFrameDesc.LengthInPixels];
//            rawDepthTextureData = new byte[depthFrameDesc.LengthInPixels * 3];

//            If the Kinect isn't started yet, open the Sensor. 
//            if (!_Sensor.IsOpen)
//            {
//                _Sensor.Open();
//            }
//        }

//        Create RenderTexture Generator
//       volumeTexture = new RenderTexture(size, size, size, RenderTextureFormat.ARGBFloat);
//        volumeTexture.enableRandomWrite = true;
//        volumeTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
//        volumeTexture.volumeDepth = size;
//        volumeTexture.Create();
//    }

//    void Update()
//    {
//        //Do Texture Updates
//        if (_Reader != null)
//        {
//            var frame = _Reader.AcquireLatestFrame();
//            if (frame != null)
//            {
//                var colorFrame = frame.ColorFrameReference.AcquireFrame();
//                if (colorFrame != null)
//                {
//                    var depthFrame = frame.DepthFrameReference.AcquireFrame();
//                    if (depthFrame != null)
//                    {
//                       // Transfer color data to array and texture
//                        colorFrame.CopyConvertedFrameDataToArray(colorData, ColorImageFormat.Rgba);
//                        colorTexture.LoadRawTextureData(colorData);
//                        colorTexture.Apply();

//                        //Transfer depth data to array, map data to Color space, and generate texture
//                        depthFrame.CopyFrameDataToArray(depthData);
//                        ColorSpacePoint[] colorSpace = new ColorSpacePoint[depthData.Length];
//                        _Mapper.MapDepthFrameToColorSpace(depthData, colorSpace);

//                    //TODO: rescale the ushorts to floats prior to conversion to bytes?
//                        for (int i = 0; i < depthData.Length; ++i)      // Data must be in byte format prior to loading to a texture 
//                        {
//                            if (minDepthMM < depthData[i])
//                            {
//                                rawDepthTextureData[3 * i + 0] = (byte)(((float)depthData[i] * 256 / maxDepthMM));
//                                rawDepthTextureData[3 * i + 1] = (byte)(((float)depthData[i] * 256 / maxDepthMM));
//                                rawDepthTextureData[3 * i + 2] = (byte)(((float)depthData[i] * 256 / maxDepthMM));
//                            }
//                        }
//                        depthTexture.LoadRawTextureData(rawDepthTextureData);
//                        depthTexture.Apply();

//                        //Dispose the depth frame
//                        depthFrame.Dispose();
//                        depthFrame = null;
//                    }
//                    //Dispose the color frame
//                    colorFrame.Dispose();
//                    colorFrame = null;
//                }
//                frame = null;
//            }
//        }

//        //Apply Texture Updates
//        int kernelIndex = shader.FindKernel("CSMain");
//        shader.SetTexture(kernelIndex, "Input1", colorTexture);
//        shader.SetTexture(kernelIndex, "Input2", depthTexture);
//        shader.SetTexture(kernelIndex, "Result", volumeTexture);
//        shader.SetFloat("_Size", size);
//        shader.Dispatch(kernelIndex, size, size, size);

//        switch (mode)
//        {
//            case Mode.Pointcloud:
//                GetComponent<MeshRenderer>().material.SetTexture("_Color", volumeTexture);
//                break;
//            case Mode.Volume:
//                GetComponent<MeshRenderer>().material.SetTexture("_Volume", volumeTexture);
//                break;

//        }
//    }

//    void OnApplicationQuit()
//    {
//        if (_Reader != null)
//        {
//            _Reader.Dispose();
//            _Reader = null;
//        }

//        if (_Sensor != null)
//        {
//            if (_Sensor.IsOpen)
//            {
//                _Sensor.Close();
//            }

//            _Sensor = null;
//        }
//    }
//}
