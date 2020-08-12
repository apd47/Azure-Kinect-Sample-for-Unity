using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class KinectProvider : MonoBehaviour
{

    [Header("Device")]
    public int deviceID = 0;
    public KinectConfiguration kinectSettings;


    [Header("Networking")]
    public string providerName;
    public string serverHostname;
    public int serverPort;
    public int maxPacketBytes = 256;

}
