using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{

    public KinectRemotePlayback remote;
    public InputField username;
    public InputField hostname;
    public InputField port;
    public InputField providername;

    private void Update()
    {
        remote.clientName = username.text;
        remote.serverHostname = hostname.text;
        remote.serverPort = int.Parse(port.text);
        remote.providerName = providername.text;
    }

}
