using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using UnityEngine;
using Microsoft.Azure.Kinect.Sensor;

public class KinectUtilities : MonoBehaviour
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
}

public enum ClientRole
{
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

    public string Serialize()
    {
        return JsonUtility.ToJson(this);
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
            Debug.Log("Kinect Configuration deserialization failed with :" +ex.Message);
            return false;
        }
    }
}

public class KinectSocketFrame
{
    public KinectRemoteFile source;
    public int frameNumber;
    public byte[] compressedData;
    public byte[] decompressedData;
    public bool processingDecompression;
    public bool decompressed;

    //public Frame(int frameNumber, byte[] compressedData)
    //{
    //    this.frameNumber = frameNumber;
    //    this.compressedData = compressedData;
    //    this.decompressed = false;
    //}

    public KinectSocketFrame(KinectRemoteFile source, int frameNumber, string compressedData)
    {
        this.source = source;
        this.frameNumber = frameNumber;
        this.compressedData = Convert.FromBase64String(compressedData);
        this.decompressed = false;
        //print(source.frames.Count);
    }

    public byte[] Decompress()
    {
        // If there's already a decompression in progress, don't start another
        if (processingDecompression)
        {
            return null;
        }
        else
        {
            processingDecompression = true;
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
                    decompressed = true;
                }
            }
            catch (Exception exception)
            {
                Debug.Log(exception.ToString());
            }
            processingDecompression = false;
            return decompressedData;
        }
    }

    // TODO: Is this ok / the right way in .NET?
    public void ReleaseDecompressedData()
    {
        decompressedData = null;
        decompressed = false;
    }
}

public class KinectVolumeFrame
{
    public KinectLocalFile sourceFile;
    public int frameNumber;
    public byte[] compressedData;
    public byte[] decompressedData;
    public bool processingDecompression;
    public bool decompressed;

    //public Frame(int frameNumber, byte[] compressedData)
    //{
    //    this.frameNumber = frameNumber;
    //    this.compressedData = compressedData;
    //    this.decompressed = false;
    //}

    public KinectVolumeFrame(KinectLocalFile sourceFile, int frameNumber)
    {
        this.sourceFile = sourceFile;
        this.frameNumber = frameNumber;
        this.compressedData = Convert.FromBase64String(sourceFile.compressedFrames[frameNumber]);
        this.decompressed = false;
    }

    public byte[] Decompress()
    {
        // If there's already a decompression in progress, don't start another
        if (processingDecompression)
        {
            return null;
        }
        else
        {
            processingDecompression = true;
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
                    decompressed = true;
                    Debug.Log("Frames Decompressed: " + sourceFile.NumberOfDecompressedFrames + " || Frames Total: " + sourceFile.numberOfFrames);
                }
            }
            catch (Exception exception)
            {
                Debug.Log(exception.ToString());
            }
            processingDecompression = false;
            return decompressedData;
        }
    }

    // TODO: Is this ok / the right way in .NET?
    public void ReleaseDecompressedData()
    {
        decompressedData = null;
        decompressed = false;
    }
}
