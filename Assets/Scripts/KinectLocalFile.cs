using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class KinectLocalFile
{
    public static string framebreak = "FRAME";
    public string filepath;
    public string[] compressedFrames;
    public List<KinectVolumeFrame> frames;
    public KinectConfiguration configuration;
    public Vector3Int matrixSize;

    public int numberOfFrames
    {
        get { return frames.Count; }
    }

    public int playbackFPS
    {
        get
        {
            switch (configuration.fps)
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

    public KinectLocalFile(string filepath)
    {
        this.filepath = filepath;
        frames = Interpret();
    }

    public KinectLocalFile(string filepath, KinectConfiguration overrideConfiguration)
    {
        this.filepath = filepath;
        frames = Interpret();
        configuration = overrideConfiguration;
        matrixSize = CalculateMatrixSize();
    }

    public Vector3Int CalculateMatrixSize()
    {
        Vector2Int resolution = new Vector2Int(0, 0);
        switch (configuration.transformationMode)
        {
            case TransformationMode.ColorToDepth:
                switch (configuration.depthMode)
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
                switch (configuration.colorResolution)
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
            case TransformationMode.None:
                Debug.Log("Transformation Mode Invalid - select either DepthToColor or ColorToDepth");
                break;
        }
        matrixSize = new Vector3Int((int)(resolution.x * configuration.volumeScale.x), (int)(resolution.y * configuration.volumeScale.y), (int)((KinectUtilities.depthRanges[(int)configuration.depthMode].y - KinectUtilities.depthRanges[(int)configuration.depthMode].x) / 11 * configuration.volumeScale.z));
        return matrixSize;
    }

    public List<KinectVolumeFrame> Interpret()
    {
        string fileContents;

        using (StreamReader sr = new StreamReader(filepath))
        {
            fileContents = sr.ReadToEnd();
        }

        string[] stringSeparators = new string[] { framebreak };
        compressedFrames = fileContents.Split(stringSeparators, StringSplitOptions.None);
        List<KinectVolumeFrame> fileFrames = new List<KinectVolumeFrame>();
        for (int frameNumber = 0; frameNumber < compressedFrames.Length; frameNumber++)
        {
            fileFrames.Add(new KinectVolumeFrame(this, frameNumber));
        }

        frames = fileFrames;
        return frames;
    }

    public int NumberOfDecompressedFrames
    {
        get
        {
            int decompressed = 0;
            foreach (KinectVolumeFrame frame in frames)
            {
                if (frame.decompressed)
                {
                    decompressed++;
                }
            }
            return decompressed;
        }
    }

    public List<KinectVolumeFrame> DecompressAllFrames()
    {
        foreach (KinectVolumeFrame frame in frames)
        {
            frame.Decompress();
        }
        return frames;
    }

}

