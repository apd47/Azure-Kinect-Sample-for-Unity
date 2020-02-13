using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ComputeShaderRunnerNew : MonoBehaviour {

	public ComputeShader shader;

	WebCamTexture input1;
	//public Texture2D input2;

	public RenderTexture render;

	public int size = 64;

	// Use this for initialization
	void Start () {
		render = new RenderTexture(size, size, size, RenderTextureFormat.ARGBFloat);
		render.enableRandomWrite = true;
		render.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
		render.volumeDepth = size;

		input1 = new WebCamTexture(size, size, 60);
		input1.Play();

		render.Create();
		oldTexture = new Texture2D(input1.width, input1.height,TextureFormat.ARGB32, false);// Texture2D.blackTexture;

	}

	Texture2D oldTexture;

	// Update is called once per frame
	void Update () {
		int kernelIndex = shader.FindKernel("CSMain");

		shader.SetTexture(kernelIndex, "Input1", input1);
		shader.SetTexture(kernelIndex, "Input2", input1);
		shader.SetTexture(kernelIndex, "oldDepthTexture", oldTexture);
		shader.SetTexture(kernelIndex, "Result", render);
		shader.SetFloat("_Size", size);

		shader.Dispatch(kernelIndex, size, size, 1);

		GetComponent<MeshRenderer>().material.SetTexture("_Color", render);
		Graphics.CopyTexture(input1, oldTexture);
	}
}
