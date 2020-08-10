using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[System.Serializable]
public class KinectLocalFile : KinectSource
{
    public string filepath;
    public string[] compressedFrames;

    public KinectLocalFile(string filepath)
    {
        this.filepath = filepath;
        frames = Interpret();
    }

    public KinectLocalFile(string filepath, KinectConfiguration backupConfiguration)
    {
        this.filepath = filepath;
        configuration = backupConfiguration;
        frames = Interpret();
    }

    public LinkedList<KinectFrame> Interpret()
    {
        string fileContents;

        using (StreamReader sr = new StreamReader(filepath))
        {
            fileContents = sr.ReadToEnd();
        }

        //TODO: This is very inefficient and makes many copies of the compressed data. Fix it. 
        string configString = fileContents.Split(new string[] {KinectUtilities.configBreak}, StringSplitOptions.None)[0];
        configuration.Import(configString);

        string dataString = fileContents.Split(new string[] { KinectUtilities.configBreak }, StringSplitOptions.None)[1];

        compressedFrames = dataString.Split(new string[] {KinectUtilities.frameBreak}, StringSplitOptions.None);

        LinkedList<KinectFrame> fileFrames = new LinkedList<KinectFrame>();
        for (int frameNumber = 0; frameNumber < compressedFrames.Length; frameNumber++)
        {
            fileFrames.AddLast(new KinectVolumeFrame(this, frameNumber));
        }

        frames = fileFrames;
        return frames;
    }

}

