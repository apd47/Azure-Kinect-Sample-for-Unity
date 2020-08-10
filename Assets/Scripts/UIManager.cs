using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_WSA	
using UnityEngine.XR.WSA.Input;
#endif

public class UIManager : MonoBehaviour
{

    public KinectRemotePlayback remote;
    public InputField username;
    public InputField hostname;
    public InputField port;
    public InputField providername;
    public Dropdown transmissionMode;


#if UNITY_WSA
    private GestureRecognizer recognizer;
#endif

    public Transform gazedot;

    private Button currentButton;
    public LayerMask buttonLayer;

    private void Update()
    {
        RaycastHit hit;
        if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out hit))
        {
            gazedot.transform.position = hit.point;
            currentButton = hit.collider.GetComponentInParent<Button>();
        }
        else
            currentButton = null;

        remote.clientName = username.text;
        remote.serverHostname = hostname.text;
        remote.serverPort = int.Parse(port.text);
        remote.providerName = providername.text;
        remote.transmissionMode = (TransmissionMode)transmissionMode.value;

    }

#if UNITY_WSA
    // What is the non-deprecated replacement for SetRecognizableGestures?
    void Start()
    {

        recognizer = new GestureRecognizer();
        recognizer.SetRecognizableGestures(GestureSettings.Tap);
        recognizer.Tapped += Recognizer_Tapped;
        recognizer.StartCapturingGestures();
    }

    private void Recognizer_Tapped(TappedEventArgs obj)
    {
        print("TAPPED");
        if (currentButton != null)
        {
            currentButton.onClick.Invoke();
        }
    }
#endif




}
