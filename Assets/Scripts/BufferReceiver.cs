using Microsoft.Azure.Kinect.Sensor;
using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class BufferReceiver : MonoBehaviour
{
    Vector3 currentAngles;
    Transformation transformation;

    public int deviceID = 0;

    public TransformationMode transformationMode;
    public ColorResolution colorResolution;
    public DepthMode depthMode;
    public FPS fps;

    public bool receiveIMUData;
    public bool receiveCameraData;

    [Header("Rendering Settings")]
    public Vector3Int matrixSize;

    DateTime lastFrame;
    float dt;

    public MeshRenderer mesh;

    public ComputeBuffer volumeBuffer;

    public int maxDepthMM = 10000;
    public int minDepthMM = 500;
    public Vector2 depthRangeModifier;
    bool running = true;

    int frameID = 0;

    [Header("Networking")]
    public bool performCompression;
    public bool receiveDataFromClients;
    public string localServerIP;
    public int portNumber;
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
    }

    Socket tcpSocket;
    TcpListener myList;
    bool connectedToServer = false;
    ASCIIEncoding asen;
    TcpClient tcpclnt;
    byte[] compressedData;
    public int dataReceiveBufferLength;


    public void StartTCPClient()
    {
        try
        {
            IPAddress ipAd = IPAddress.Parse(localServerIP);

            TcpClient tcpclnt = new TcpClient();
            Stream stm = tcpclnt.GetStream();
            print("Connecting.....");
            tcpclnt.Connect(ipAd, portNumber);

            print("Connected to server. Sending config request.");
            connectedToServer = true;

            asen = new ASCIIEncoding();
            byte[] ba = asen.GetBytes("CONFIG_REQUESTED");
            stm.Write(ba, 0, ba.Length);

            print("Request sent. Waiting for config from server.");
            byte[] bb = new byte[100];
            int k = stm.Read(bb, 0, 100);
            for (int i = 0; i < k; i++)
                print(Convert.ToChar(bb[i]));

            compressedData = new byte[dataReceiveBufferLength];

            print("Config valid. Requesting volume data and opening listener. ");

            ba = asen.GetBytes("VOLUME_DATA_REQUESTED");
            stm.Write(ba, 0, ba.Length);
            Task ReceiveVolumeLoop = VolumeReceiverLoop();
        }
        catch (Exception e)
        {
            print("Error..... " + e.StackTrace);
        }
    }

    private async Task VolumeReceiverLoop()
    {
        Material matt = mesh.sharedMaterial;
        while (running)
        {
            if (receiveCameraData)
            {
                await tcpclnt.GetStream().ReadAsync(compressedData, 0, compressedData.Length);
                
                if (volumeBuffer == null)
                {
                    volumeBuffer = new ComputeBuffer(matrixSize.x * matrixSize.y * matrixSize.z, 4 * sizeof(float), ComputeBufferType.Default);
                    print("Made Volume Buffer");
                }

                // This might be a reace condition, with dataReceived changing before/during decompression. Need to look into. 
                byte[] decompressedData;
                //ThreadPool.QueueUserWorkItem(state => { Decompress(compressedData, out decompressedData); });

                //volumeBuffer.GetData(colors);


                matt.SetBuffer("colors", volumeBuffer);
                matt.SetInt("_MatrixX", matrixSize.x);
                matt.SetInt("_MatrixY", matrixSize.y);
                matt.SetInt("_MatrixZ", matrixSize.z);



            }
            else
            {
                await Task.Run(() => { });
            }
        }
    }

    //public void Decompress(byte[] compressed, out byte[] decompressed)
    //{
    //    ////MemoryStream input = new MemoryStream(data);
    //    //MemoryStream output = new MemoryStream();
    //    //using (DeflateStream dstream = new DeflateStream(input, CompressionMode.Decompress))
    //    //{
    //    //    dstream.CopyTo(output);
    //    //}
    //    //return output.ToArray();
    //}

    private void IMULoop()
    {
        currentAngles = this.transform.rotation.eulerAngles;
        while (true)
        {
            if (receiveIMUData)
            {
                this.transform.rotation = Quaternion.Euler(currentAngles);
            }
        }
    }

}
