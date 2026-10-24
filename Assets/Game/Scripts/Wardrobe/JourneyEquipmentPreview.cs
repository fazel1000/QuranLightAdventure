using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class JourneyEquipmentPreview : MonoBehaviour
{
    public RawImage image;
    public Camera previewCamera;
    public Transform stage;
    public JourneyWardrobeDrag drag;
    [Range(256, 1024)] public int textureSize = 512;
    [Range(8, 31)] public int previewLayer = 30;
    private RenderTexture texture;
    private GameObject model;
    private readonly List<Mesh> meshes = new List<Mesh>();
    public void Show(JourneyEquipmentInventory.Item item)
    {
        ClearModel();
        if (item == null || stage == null || previewCamera == null || image == null) return;
        GameObject source = item.previewModel;
        if (source == null && item.attachments.Count > 0 && item.attachments[0].root != null) source = item.attachments[0].root.gameObject;
        if (source == null) return;
        stage.gameObject.SetActive(true);
        EnsureTexture();
        model = JourneyEquipmentVisuals.Copy(source, stage, previewLayer, meshes);
        model.transform.localRotation = Quaternion.Euler(item.previewEulerAngles);
        float cameraDistance = 2.5f;
        if (JourneyEquipmentVisuals.BoundsOf(model, out Bounds bounds))
        {
            float scale = 1.5f / Mathf.Max(0.01f, Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)));
            model.transform.localScale = Vector3.one * scale;
            JourneyEquipmentVisuals.BoundsOf(model, out bounds);
            model.transform.position += stage.position - bounds.center;
            cameraDistance = Mathf.Max(1f, bounds.extents.magnitude / Mathf.Max(0.1f, Mathf.Sin(previewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad))) * 1.15f;
        }
        previewCamera.transform.SetPositionAndRotation(stage.position + Vector3.back * cameraDistance, Quaternion.identity);
        previewCamera.transform.LookAt(stage.position);
        previewCamera.enabled = true; image.texture = texture;
        if (drag != null) { drag.target = model.transform; drag.previewCamera = previewCamera; drag.previewArea = image.rectTransform; }
    }
    private void EnsureTexture()
    {
        if (texture != null) return;
        int size = Mathf.Clamp(Mathf.NextPowerOfTwo(textureSize), 256, 1024);
        texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { name = "Equipment Preview", antiAliasing = 1 };
        texture.Create(); previewCamera.targetTexture = texture; previewCamera.aspect = 1f;
        previewCamera.clearFlags = CameraClearFlags.SolidColor; previewCamera.backgroundColor = Color.clear;
        previewCamera.cullingMask = 1 << previewLayer;
    }
    private void ClearModel()
    {
        if (model != null) { model.SetActive(false); Destroy(model); model = null; }
        JourneyEquipmentVisuals.ReleaseMeshes(meshes);
        if (drag != null) drag.target = null;
    }
    public void Hide()
    { ClearModel(); if (previewCamera != null) previewCamera.enabled = false; if (stage != null) stage.gameObject.SetActive(false); }
    private void OnDisable() { Hide(); }
    private void OnDestroy()
    {
        Hide();
        if (previewCamera != null) previewCamera.targetTexture = null;
        if (image != null) image.texture = null;
        if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
    }
}
