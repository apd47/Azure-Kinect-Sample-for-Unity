# Server Communication Protocol

Server communication is done via string sommuniciation based on delimiters (| and ~) with binary image data encoded via base64

Ex: PROVIDER|andrew|ADDFRAME|10~COLOR~100~LZ4|

Content Ordering: (String list delimited by "|")
[0] = Client Type (Provider, Reciever, or Server)
[1] = CLient Name (String identifier)
[2] = Task to Perform (JOIN, LEAVE, SUBSCRIBE, UNSUBSCRIBE, ADDFRAME, ADDBLOCK, REQUESTFRAME, REQUESTBLOCK, FINISHFRAME, CONFIRM, FAIL, ALERT)
[3] = Task Variables (String list delimited by "~" of parameters that varies with each task, see below guide)

Tasks:

JOIN
	No Task-Specific Variables

LEAVE
	No Task-Specific Variables

SUBSCRIBE
	[0] = Provider Name (string)

UNSUBSCRIBE
	No Task-Specific Variables

ADDFRAME
	[0] = Frame Number (int)
	[1] = Frame Type (COLOR, DEPTH, or VOLUME)
	[2] = Number of Blocks (int)
	[3] = Compression Type (NONE or LZ4)
	[4] = Kinect Configurcation (JSON String)

ADDBLOCK
	[0] = Frame Number (int)
	[1] = Frame Type (COLOR, DEPTH, or VOLUME)
	[2] = Block Number (int)
	[3] = Block Data (Base64 string)

REQUESTBLOCK
	[0] = Frame Number (int)
	[1] = Frame Type (COLOR, DEPTH, or VOLUME)
	[2] = Block Number (int)

REQUESTFRAME
	[0] = Frame Number (int)
	[1] = Frame Type (COLOR, DEPTH, or VOLUME)

FINISHFRAME
	[0] = Frame Number (int)
	[1] = Frame Type (COLOR, DEPTH, or VOLUME)

CONFIRM
	[0] = Task to Confirm
	[1+] = Varies with task

FAIL
	[0] = Task that Failed
	[1] = Failure Reason
	[2+] = Varies with task

ALERT
	[0] = Task to alert others about
	[1+] = Varies with task




(1) Clone official SDK repository.<br>
https://github.com/microsoft/Azure-Kinect-Sensor-SDK/tree/develop/docs

(2) Build Projects with Visual Studio by referring to the following guide.<br>
https://github.com/microsoft/Azure-Kinect-Sensor-SDK/blob/develop/docs/building.md#building-using-visual-studio

(3) Build C# Wrapper.<br>
https://github.com/microsoft/Azure-Kinect-Sensor-SDK/blob/develop/docs/building.md#c-wrapper
<br><b>depthengine_2_0.dll</b> is required to build C# wrapper. Please copy depthengine_2_0.dll from the directory which Azure Kinect SDK is installed.

(4) Install dll files into Unity Project.<br>
Find following directory and copy all files into <b>Plugins</b> folder of this Uity Smaple.<br> 
<i>C:\YOUR DIRECTORY\Azure-Kinect-Sensor-SDK\build\Win-x64-Release-Ninja\bin\Release\x64\Microsoft.Azure.Kinect.Sensor.Examples.WinForms </i>

(5) You can test following demo.<br>
[![](https://img.youtube.com/vi/Nt0oMN5Ece0/0.jpg)](https://www.youtube.com/watch?v=Nt0oMN5Ece0)


