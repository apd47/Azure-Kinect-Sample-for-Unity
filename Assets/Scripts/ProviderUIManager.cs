using Microsoft.Azure.Kinect.Sensor;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class ProviderUIManager : MonoBehaviour
{

    public KinectBuffer buffer;
    public InputField hostname;
    public InputField port;
    public InputField providerName;
    public InputField maxPacketSize;

    public InputField deviceID;
    public Dropdown transformationMode;
    public Dropdown colorMode;
    public Dropdown depthMode;
    public Dropdown fpsMode;
    public InputField matrixScale;

    private void Update()
    {
        buffer.serverHostname = hostname.text;
        buffer.serverPort = int.Parse(port.text);
        buffer.providerName = providerName.text;
        buffer.maxPacketBytes = int.Parse(maxPacketSize.text);

        buffer.deviceID = int.Parse(deviceID.text);
        buffer.kinectSettings.transformationMode = (TransformationMode) transformationMode.value;
        buffer.kinectSettings.colorResolution = (ColorResolution) colorMode.value + 1;
        buffer.kinectSettings.depthMode = (DepthMode)depthMode.value + 1;
        buffer.kinectSettings.fps = (FPS)fpsMode.value;
        buffer.kinectSettings.volumeScale.x = float.Parse(matrixScale.text);
        buffer.kinectSettings.volumeScale.y = float.Parse(matrixScale.text);
        buffer.kinectSettings.volumeScale.z = float.Parse(matrixScale.text);

    }

}
