using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.VisualScripting.Member;

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
    [SerializeField] private bool _debugging = false;

    private Camera _camera;
    private Camera _maskCamera;
    private Material _stencilWriteMaterial;
    private Material _blackOverlayMaterial;
    private RenderTexture _tempRT;
    private bool _saveDebugMask = false;
    private List<Renderer> _affectedRenderers = new List<Renderer>();
    private Dictionary<Renderer, Material[]> _originalMaterials = new Dictionary<Renderer, Material[]>();
    private List<Renderer> _allRenderers = new List<Renderer>();
    private List<Vector3> _objectScreenPositions = new List<Vector3>();

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
            if (Application.isPlaying) Destroy(_maskCamera.gameObject);
            else DestroyImmediate(_maskCamera.gameObject);
            _maskCamera = null;
        }
        if (_stencilWriteMaterial != null)
        {
            if (Application.isPlaying) Destroy(_stencilWriteMaterial);
            else DestroyImmediate(_stencilWriteMaterial);
            _stencilWriteMaterial = null;
        }
        if (_blackOverlayMaterial != null)
        {
            if (Application.isPlaying) Destroy(_blackOverlayMaterial);
            else DestroyImmediate(_blackOverlayMaterial);
            _blackOverlayMaterial = null;
        }

        _affectedRenderers.Clear();
        _originalMaterials.Clear();
        _allRenderers.Clear();
        _objectScreenPositions.Clear();
    }

    private void OnValidate()
    {
        if (isActiveAndEnabled)
            InitResources();
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
            _saveDebugMask = true;
    }

    // Ensure mask camera tracks the main camera before culling/renders
    private void OnPreCull()
    {
        if (_maskCamera == null || _camera == null) return;
        _maskCamera.transform.position = _camera.transform.position;
        _maskCamera.transform.rotation = _camera.transform.rotation;
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
            // Keep the mask camera aligned with the main camera at all times
            go.transform.SetParent(_camera.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            _maskCamera.enabled = false;
        }
    }

    private void InitRT(int width, int height)
    {
        if (_tempRT != null && (_tempRT.width != width || _tempRT.height != height))
            CleanupRT();

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
            if (Application.isPlaying) Destroy(_tempRT);
            else DestroyImmediate(_tempRT);
            _tempRT = null;
        }
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        InitRender(ref source, ref destination);
        RenderMask();
        if (_debugging) DebugRenderState();
        CollectAllObjectScreenPositions();
        BlitOutline(ref source, ref destination);
    }

    #region Helper methods
    private void InitRender(ref RenderTexture source, ref RenderTexture destination)
    {
        if (_camera == null)
            _camera = GetComponent<Camera>();
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

        _affectedRenderers.Clear();
        _originalMaterials.Clear();
        _allRenderers.Clear();
        _objectScreenPositions.Clear();
        _allRenderers = Object.FindObjectsOfType<Renderer>().ToList();
    }

    private void RenderMask()
    {
        // Create a mask texture using a secondary camera
        _maskCamera.CopyFrom(_camera);
        _maskCamera.clearFlags = CameraClearFlags.SolidColor;
        _maskCamera.backgroundColor = Color.black;
        _maskCamera.targetTexture = _tempRT;


        for (int r = 0; r < _allRenderers.Count; r++)
        {
            var renderer = _allRenderers[r];
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
                _originalMaterials[renderer] = renderer.sharedMaterials;
                var tempMats = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < tempMats.Length; i++)
                    tempMats[i] = _stencilWriteMaterial;
                renderer.sharedMaterials = tempMats;
                _affectedRenderers.Add(renderer);
                Debug.Log($"Swapped material on: {renderer.gameObject.name}");
            }
        }

        // Render the mask
        _maskCamera.Render();

        // Restore original materials
        foreach (var renderer in _affectedRenderers)
        {
            if (renderer != null && _originalMaterials.ContainsKey(renderer))
                renderer.sharedMaterials = _originalMaterials[renderer];
        }
    }

    /// <summary>
    /// Saves the current mask render texture to a PNG file under Assets if use presses Space Bar in Play Mode
    /// </summary>
	private void DebugRenderState()
    {
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
    }

    /// <summary>
    /// Collects screenspace locations of all target objects for multi-object falloff
    /// </summary>
	private void CollectAllObjectScreenPositions()
    {
        _objectScreenPositions.Clear();
        for (int r = 0; r < _allRenderers.Count; r++)
        {
            var renderer = _allRenderers[r];
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
                var objectScreenPos = _camera.WorldToScreenPoint(renderer.bounds.center);
                objectScreenPos.x /= Screen.width;
                objectScreenPos.y /= Screen.height;
                _objectScreenPositions.Add(objectScreenPos);
            }
        }
    }

    private void BlitOutline(ref RenderTexture source, ref RenderTexture destination)
    {
        // Composite: keep source where mask is white, edge color elsewhere with falloff
        _blackOverlayMaterial.SetTexture("_MainTex", source);
        _blackOverlayMaterial.SetTexture("_MaskTex", _tempRT);
        _blackOverlayMaterial.SetFloat("_FalloffDistance", falloffDistance);
        _blackOverlayMaterial.SetFloat("_FalloffPower", falloffPower);
        _blackOverlayMaterial.SetColor("_EdgeColor", edgeColor);

        // Pass object count and positions as arrays (limited to reasonable number)
        int objectCount = Mathf.Min(_objectScreenPositions.Count, 8); // Limit to 8 objects for shader arrays
        _blackOverlayMaterial.SetInt("_ObjectCount", objectCount);

        // Set object positions (shader will use first 8)
        for (int i = 0; i < objectCount; i++)
        {
            string propName = $"_ObjectPos{i}";
            _blackOverlayMaterial.SetVector(propName, _objectScreenPositions[i]);
        }

        Graphics.Blit(source, destination, _blackOverlayMaterial);
    }
    #endregion Helper methods
}
