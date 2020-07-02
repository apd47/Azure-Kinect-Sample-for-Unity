using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

public class KinectLocalPlayback : KinectPlayback
{
    public string filepath;
    public bool load;
    public bool play;

    public void Update()
    {
        if (load)
        {
            Load(filepath);
            load = false;
        }

        if (play)
        {
            UpdateVisualization();
        }
    }

    public void Load(string filepath)
    {
        source = new KinectLocalFile(filepath, kinectSettings);
        ConfigureVisualization();
        ThreadPool.QueueUserWorkItem((state) => CheckAndMaintainBuffer((float)state), defaultSecondsToBuffer);
    }

}
