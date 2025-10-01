using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class OutlineEffect : MonoBehaviour
{
	[SerializeField] private Material[] targetMaterials;
	[SerializeField] private Shader stencilWriteShader; // "Hidden/OutlineMask" updated to write stencil, ColorMask 0
	[SerializeField] private Shader blackOverlayShader; // shader that draws black where stencil != 1
	[SerializeField] private float falloffDistance = 0.1f;
	[SerializeField] private float falloffPower = 2.0f;
	[SerializeField] private Color edgeColor = Color.yellow;

	private Camera _camera;
	private Camera _maskCamera;
	private Material _stencilWriteMaterial;
	private Material _blackOverlayMaterial;
	private RenderTexture _tempRT;
	private bool _saveDebugMask = false;

	private void OnEnable()
	{
		_camera = GetComponent<Camera>();
		InitResources();
	}

	private void OnDisable()
	{
		CleanupRT();
		if (_maskCamera != null)
		{
			if (Application.isPlaying) Destroy(_maskCamera.gameObject); else DestroyImmediate(_maskCamera.gameObject);
			_maskCamera = null;
		}
		if (_stencilWriteMaterial != null) { if (Application.isPlaying) Destroy(_stencilWriteMaterial); else DestroyImmediate(_stencilWriteMaterial); _stencilWriteMaterial = null; }
		if (_blackOverlayMaterial != null) { if (Application.isPlaying) Destroy(_blackOverlayMaterial); else DestroyImmediate(_blackOverlayMaterial); _blackOverlayMaterial = null; }
	}

	private void OnValidate()
	{
		if (isActiveAndEnabled)
		{
			InitResources();
		}
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.Space))
		{
			_saveDebugMask = true;
		}
	}

	private void InitResources()
	{
		if (_camera == null) _camera = GetComponent<Camera>();

		if (stencilWriteShader == null)
			stencilWriteShader = Shader.Find("Hidden/OutlineMask");
		if (blackOverlayShader == null)
			blackOverlayShader = Shader.Find("Hidden/MaskComposite");

		if (_stencilWriteMaterial == null && stencilWriteShader != null)
			_stencilWriteMaterial = new Material(stencilWriteShader) { hideFlags = HideFlags.HideAndDontSave };
		if (_blackOverlayMaterial == null && blackOverlayShader != null)
			_blackOverlayMaterial = new Material(blackOverlayShader) { hideFlags = HideFlags.HideAndDontSave };

		if (_maskCamera == null)
		{
			var go = new GameObject("OutlineEffect_MaskCamera");
			go.hideFlags = HideFlags.HideAndDontSave;
			_maskCamera = go.AddComponent<Camera>();
			_maskCamera.enabled = false;
		}
	}

	private void InitRT(int width, int height)
	{
		if (_tempRT != null && (_tempRT.width != width || _tempRT.height != height))
		{
			CleanupRT();
		}
		if (_tempRT == null && width > 0 && height > 0)
		{
			_tempRT = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
			_tempRT.name = "OutlineEffect_Temp";
			_tempRT.Create();
		}
	}

	private void CleanupRT()
	{
		if (_tempRT != null)
		{
			_tempRT.Release();
			if (Application.isPlaying) Destroy(_tempRT); else DestroyImmediate(_tempRT);
			_tempRT = null;
		}
	}

	private void OnRenderImage(RenderTexture source, RenderTexture destination)
	{
		if (_camera == null) _camera = GetComponent<Camera>();
		InitResources();

		if (targetMaterials == null || targetMaterials.Length == 0 || _stencilWriteMaterial == null || _blackOverlayMaterial == null)
		{
			Graphics.Blit(source, destination);
			return;
		}

		InitRT(source.width, source.height);
		if (_tempRT == null)
		{
			Graphics.Blit(source, destination);
			return;
		}

		// Create a mask texture using a secondary camera
		_maskCamera.CopyFrom(_camera);
		_maskCamera.clearFlags = CameraClearFlags.SolidColor;
		_maskCamera.backgroundColor = Color.black;
		_maskCamera.targetTexture = _tempRT;

		// Temporarily swap materials on target objects
		var affectedRenderers = new List<Renderer>();
		var originalMaterials = new Dictionary<Renderer, Material[]>();

		var allRenderers = Object.FindObjectsOfType<Renderer>();
		for (int r = 0; r < allRenderers.Length; r++)
		{
			var renderer = allRenderers[r];
			var mats = renderer.sharedMaterials;
			if (mats == null || mats.Length == 0) continue;
			
			bool hasTarget = false;
			for (int i = 0; i < mats.Length; i++) 
			{ 
				for (int j = 0; j < targetMaterials.Length; j++)
				{
					if (mats[i] == targetMaterials[j]) 
					{ 
						hasTarget = true; 
						break; 
					}
				}
				if (hasTarget) break;
			}
			
			if (hasTarget)
			{
				originalMaterials[renderer] = renderer.sharedMaterials;
				var tempMats = new Material[renderer.sharedMaterials.Length];
				for (int i = 0; i < tempMats.Length; i++)
					tempMats[i] = _stencilWriteMaterial;
				renderer.sharedMaterials = tempMats;
				affectedRenderers.Add(renderer);
				Debug.Log($"Swapped material on: {renderer.gameObject.name}");
			}
		}

		// Render the mask
		_maskCamera.Render();

		// Restore original materials
		foreach (var renderer in affectedRenderers)
		{
			if (renderer != null && originalMaterials.ContainsKey(renderer))
				renderer.sharedMaterials = originalMaterials[renderer];
		}

		// DEBUG: Save the mask texture to see what's in it
		if (_saveDebugMask)
		{
			try
			{
				RenderTexture.active = _tempRT;
				Texture2D tex = new Texture2D(_tempRT.width, _tempRT.height, TextureFormat.RGB24, false);
				tex.ReadPixels(new Rect(0, 0, _tempRT.width, _tempRT.height), 0, 0);
				tex.Apply();
				string path = System.IO.Path.Combine(Application.dataPath, "mask_debug.png");
				System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
				Debug.Log($"Mask saved to: {path}");
				DestroyImmediate(tex);
				_saveDebugMask = false;
			}
			catch (System.Exception e)
			{
				Debug.LogError($"Failed to save mask: {e.Message}");
				_saveDebugMask = false;
			}
		}

		// Calculate object's screen position (use first target object found)
		Vector3 objectScreenPos = Vector3.zero;
		bool foundObject = false;
		for (int r = 0; r < allRenderers.Length; r++)
		{
			var renderer = allRenderers[r];
			var mats = renderer.sharedMaterials;
			if (mats == null || mats.Length == 0) continue;
			
			bool hasTarget = false;
			for (int i = 0; i < mats.Length; i++) 
			{ 
				for (int j = 0; j < targetMaterials.Length; j++)
				{
					if (mats[i] == targetMaterials[j]) 
					{ 
						hasTarget = true; 
						break; 
					}
				}
				if (hasTarget) break;
			}
			
			if (hasTarget)
			{
				objectScreenPos = _camera.WorldToScreenPoint(renderer.bounds.center);
				objectScreenPos.x /= Screen.width;
				objectScreenPos.y /= Screen.height;
				foundObject = true;
				break;
			}
		}

		// Composite: keep source where mask is white, edge color elsewhere with falloff
		_blackOverlayMaterial.SetTexture("_MainTex", source);
		_blackOverlayMaterial.SetTexture("_MaskTex", _tempRT);
		_blackOverlayMaterial.SetFloat("_FalloffDistance", falloffDistance);
		_blackOverlayMaterial.SetFloat("_FalloffPower", falloffPower);
		_blackOverlayMaterial.SetVector("_ObjectScreenPos", foundObject ? objectScreenPos : Vector3.one * 0.5f);
		_blackOverlayMaterial.SetColor("_EdgeColor", edgeColor);
		Graphics.Blit(source, destination, _blackOverlayMaterial);
        
	}
}
