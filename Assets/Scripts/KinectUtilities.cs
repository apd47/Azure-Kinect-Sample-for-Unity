using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using Microsoft.Azure.Kinect.Sensor;
using System.Text;
using K4os.Compression.LZ4;

public class KinectUtilities
{
    public static string configBreak = "CONFIG";
    public static string frameBreak = "FRAME";

    public static Vector2Int[] depthRanges =
    {
        new Vector2Int(0,0),
        new Vector2Int(500, 3860),
        new Vector2Int(500, 5460),
        new Vector2Int(250, 2880),
        new Vector2Int(250, 2210)
    };

    public static int FPStoInt(FPS input)
    {
        switch (input)
        {
            case FPS.FPS5:
                return 5;
            case FPS.FPS15:
                return 15;
            case FPS.FPS30:
                return 30;
            default:
                return 15;
        }

    }
}

public enum KinectTask
{
    NONE,
    JOIN,
    LEAVE,
    SUBSCRIBE,
    UNSUBSCRIBE,
    ADDFRAME,
    ADDBLOCK,
    REQUESTBLOCK,
    REQUESTFRAME,
    FINISHFRAME,
    CONFIRM,
    FAIL,
    ALERT
}

public enum KinectCompressionType
{
    NONE,
    LZ4
}

public enum FailReason
{
    NONE,
    MISSINGFRAME,
    MISSINGBLOCKS,
    MISSINGPROVIDER,
    NOTSUBSCRIBEDYET,
    INCOMPLETEFRAME,
    EMPTYBLOCK
}

public enum FrameType
{
    COLOR, 
    DEPTH, 
    VOLUME
}

public enum EntityType
{
    SERVER,
    PROVIDER,
    RECEIVER
}

public enum ConnectionState
{
    Disconnected,
    JoinedServer,
    SubscribedToProvider
}

public enum TransformationMode
{
    ColorToDepth,
    DepthToColor,
    None
}

[System.Serializable]
public class KinectConfiguration
{
    public TransformationMode transformationMode;
    public ColorResolution colorResolution;
    public DepthMode depthMode;
    public FPS fps;
    public Vector3 volumeScale;
    public Vector2 depthRangeModifier;

    public Vector2Int TextureResolutions
    {
        get
        {
            Vector2Int resolution = new Vector2Int();
            switch (transformationMode)
            {
                case TransformationMode.ColorToDepth:
                    switch (depthMode)
                    {
                        case DepthMode.Off:
                            resolution = new Vector2Int(-1, -1);
                            break;
                        case DepthMode.NFOV_2x2Binned:
                            resolution = new Vector2Int(320, 288);
                            break;
                        case DepthMode.NFOV_Unbinned:
                            resolution = new Vector2Int(640, 576);
                            break;
                        case DepthMode.WFOV_2x2Binned:
                            resolution = new Vector2Int(512, 512);
                            break;
                        case DepthMode.WFOV_Unbinned:
                            resolution = new Vector2Int(1024, 1024);
                            break;
                        case DepthMode.PassiveIR:
                            resolution = new Vector2Int(1024, 1024);
                            break;
                        default:
                            break;
                    }
                    break;
                case TransformationMode.DepthToColor:
                    switch (colorResolution)
                    {
                        case ColorResolution.Off:
                            resolution = new Vector2Int(-1, -1);
                            break;
                        case ColorResolution.R720p:
                            resolution = new Vector2Int(1280, 720);
                            break;
                        case ColorResolution.R1080p:
                            resolution = new Vector2Int(1920, 1080);
                            break;
                        case ColorResolution.R1440p:
                            resolution = new Vector2Int(2560, 1440);
                            break;
                        case ColorResolution.R1536p:
                            resolution = new Vector2Int(2048, 1536);
                            break;
                        case ColorResolution.R2160p:
                            resolution = new Vector2Int(3840, 2160);
                            break;
                        case ColorResolution.R3072p:
                            resolution = new Vector2Int(4096, 3072);
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    Debug.Log("Transformation Mode Invalid - select either DepthToColor or ColorToDepth");
                    resolution = new Vector2Int(-1, -1);
                    break;
            }
            return resolution;
        }
    }

    public string Serialized
    {
        get
        {
            return JsonUtility.ToJson(this);
        }
    }

    public bool Import(string json)
    {
        try
        {
            KinectConfiguration fromJson = JsonUtility.FromJson<KinectConfiguration>(json);
            this.transformationMode = fromJson.transformationMode;
            this.colorResolution = fromJson.colorResolution;
            this.depthMode = fromJson.depthMode;
            this.fps = fromJson.fps;
            this.volumeScale = fromJson.volumeScale;
            this.depthRangeModifier = fromJson.depthRangeModifier;
            return true;
        }
        catch (Exception ex)
        {
            Debug.Log("Kinect Configuration deserialization failed with :" + ex.Message);
            return false;
        }
    }

}

public enum KinectFrameState
{
    Empty,
    PartiallyPopulated,
    Compressed,
    ProcessingDecompression,
    Decompressed,
    Error
}

public class KinectSource
{
    public LinkedList<KinectFrame> frames;
    public KinectConfiguration configuration;
    public Vector3Int matrixSize
    {
        get
        {
            Vector2Int resolution = configuration.TextureResolutions;
            // Where did the divide by 11 come from?
            return new Vector3Int((int)(resolution.x * configuration.volumeScale.x), (int)(resolution.y * configuration.volumeScale.y), (int)((KinectUtilities.depthRanges[(int)configuration.depthMode].y - KinectUtilities.depthRanges[(int)configuration.depthMode].x) / 11 * configuration.volumeScale.z));
        }
    }
    public void DecompressAllFrames()
    {
        foreach (KinectFrame frame in frames)
        {
            frame.Decompress();
        }
    }
    public int NumberOfDecompressedFrames
    {
        get
        {
            int decompressed = 0;
            foreach (KinectFrame frame in frames)
            {
                if (frame.frameState == KinectFrameState.Decompressed)
                {
                    decompressed++;
                }
            }
            return decompressed;
        }
    }
    public int NumberOfFrames
    {
        get { return frames.Count; }
    }
}

public class KinectFrame
{
    public KinectSource source;
    public int frameNumber;
    public byte[] compressedData;
    public byte[] decompressedData;
    public KinectFrameState frameState;

    public void DecompressLZ4()
    {
        //Debug.Log(frameNumber +" | " + frameState);
        if (frameState == KinectFrameState.Compressed)
        {
            frameState = KinectFrameState.ProcessingDecompression;
            try
            {
                Vector3Int matrix = source.matrixSize;
                decompressedData = new byte[matrix.x * matrix.y * matrix.z * 4 * 4];
                LZ4Codec.Decode(compressedData, 0, compressedData.Length, decompressedData, 0, decompressedData.Length);
                frameState = KinectFrameState.Decompressed;
                //Debug.Log(frameNumber + " | " + frameState);
                compressedData = null;

            }
            catch (Exception exception)
            {
                frameState = KinectFrameState.Error;
                Debug.Log(exception.ToString());
            }
        }
    }

    public void Decompress()
    {
        if (frameState == KinectFrameState.Compressed)
        {
            frameState = KinectFrameState.ProcessingDecompression;
            try
            {
                using (MemoryStream decompressedStream = new MemoryStream())
                {
                    using (MemoryStream compressStream = new MemoryStream(compressedData))
                    {
                        using (DeflateStream deflateStream = new DeflateStream(compressStream, CompressionMode.Decompress))
                        {
                            deflateStream.CopyTo(decompressedStream);
                        }
                    }
                    decompressedData = decompressedStream.ToArray();
                    frameState = KinectFrameState.Decompressed;
                    compressedData = null;
                }
            }
            catch (Exception exception)
            {
                frameState = KinectFrameState.Error;
                Debug.Log(exception.ToString());
            }
        }
    }


}

public class KinectSocketFrame : KinectFrame
{
    public int populatedSegments;
    public int totalSegments;
    public string[] segments;
    public int framesSpentBuffering = 0;
    public event EventHandler OnFullyReceived = new EventHandler((e, a) => { });

    public KinectSocketFrame(KinectRemoteProvider source, int frameNumber, int totalSegments)
    {
        this.source = source;
        this.frameNumber = frameNumber;
        this.totalSegments = totalSegments;
        this.segments = new string[totalSegments];
        this.frameState = KinectFrameState.Empty;
        this.populatedSegments = 0;

    }

    public KinectFrameState ImportSegment(int segmentNumber, string segmentString)
    {
        segments[segmentNumber] = segmentString;
        populatedSegments++;

        if (totalSegments == populatedSegments)
        {
            StringBuilder combiner = new StringBuilder();
            foreach (string s in segments)
            {
                combiner.Append(s);
            }
            compressedData = Convert.FromBase64String(combiner.ToString());
            frameState = KinectFrameState.Compressed;
            OnFullyReceived(this, new EventArgs());
        }
        else
        {
            frameState = KinectFrameState.PartiallyPopulated;
        }
        return frameState;
    }
}

public class KinectVolumeFrame : KinectFrame
{
    public KinectVolumeFrame(KinectLocalFile sourceFile, int frameNumber)
    {
        this.source = sourceFile;
        this.frameNumber = frameNumber;
        this.compressedData = Convert.FromBase64String(sourceFile.compressedFrames[frameNumber]);
        frameState = KinectFrameState.Compressed;
    }
}
