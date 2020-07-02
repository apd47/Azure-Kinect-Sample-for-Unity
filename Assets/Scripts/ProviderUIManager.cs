﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProviderUIManager : MonoBehaviour
{

    public KinectBuffer buffer;
    public InputField hostname;
    public InputField port;
    public InputField providername;
    public InputField maxPacketSize;

    private void Update()
    {
        buffer.serverHostname = hostname.text;
        buffer.serverPort = int.Parse(port.text);
        buffer.providerName = providername.text;
        buffer.maxPacketBytes = int.Parse(maxPacketSize.text);
    }

}