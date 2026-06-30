using Rag.Healthcare.Camera;
using UnityEngine;
using UnityEngine.UI;

namespace Rag.Healthcare.Pose.Rendering
{
    public sealed class PosePreviewOverlayBinder : MonoBehaviour
    {
        [SerializeField] private CameraCaptureSource cameraSource;
        [SerializeField] private RawImage previewImage;
        [SerializeField] private RectTransform overlayRoot;
        [SerializeField] private AspectRatioFitter aspectRatioFitter;
        [SerializeField] private bool preserveAspect = true;
        [SerializeField] private bool hidePreviewWhenCameraStops = true;

        private RectTransform previewRectTransform;

        private void Awake()
        {
            cameraSource ??= FindFirstObjectByType<CameraCaptureSource>();
            previewImage ??= GetComponentInChildren<RawImage>();

            if (previewImage != null)
            {
                previewRectTransform = previewImage.rectTransform;
                aspectRatioFitter ??= previewImage.GetComponent<AspectRatioFitter>();
            }

            if (overlayRoot == null)
            {
                var skeletonRenderer = GetComponentInChildren<PoseSkeletonRenderer>();
                overlayRoot = skeletonRenderer == null ? null : skeletonRenderer.GetComponent<RectTransform>();
            }
        }

        private void OnEnable()
        {
            if (cameraSource != null)
            {
                cameraSource.PreviewTextureChanged += HandlePreviewTextureChanged;
                HandlePreviewTextureChanged(cameraSource.PreviewTexture);
            }
        }

        private void OnDisable()
        {
            if (cameraSource != null)
            {
                cameraSource.PreviewTextureChanged -= HandlePreviewTextureChanged;
            }
        }

        private void LateUpdate()
        {
            UpdatePreviewAspect();
            MatchOverlayToPreview();
        }

        private void HandlePreviewTextureChanged(Texture texture)
        {
            if (previewImage == null)
            {
                return;
            }

            previewImage.texture = texture;
            if (hidePreviewWhenCameraStops)
            {
                previewImage.enabled = texture != null;
            }

            UpdatePreviewAspect();
            MatchOverlayToPreview();
        }

        private void UpdatePreviewAspect()
        {
            if (!preserveAspect || aspectRatioFitter == null || cameraSource == null)
            {
                return;
            }

            var width = cameraSource.FrameWidth;
            var height = cameraSource.FrameHeight;
            if (width <= 16 || height <= 16)
            {
                return;
            }

            aspectRatioFitter.aspectRatio = width / (float)height;
        }

        private void MatchOverlayToPreview()
        {
            if (overlayRoot == null || previewRectTransform == null)
            {
                return;
            }

            if (overlayRoot.parent == previewRectTransform)
            {
                overlayRoot.anchorMin = Vector2.zero;
                overlayRoot.anchorMax = Vector2.one;
                overlayRoot.offsetMin = Vector2.zero;
                overlayRoot.offsetMax = Vector2.zero;
                overlayRoot.pivot = new Vector2(0.5f, 0.5f);
                return;
            }

            if (overlayRoot.parent != previewRectTransform.parent)
            {
                return;
            }

            overlayRoot.anchorMin = previewRectTransform.anchorMin;
            overlayRoot.anchorMax = previewRectTransform.anchorMax;
            overlayRoot.anchoredPosition = previewRectTransform.anchoredPosition;
            overlayRoot.sizeDelta = previewRectTransform.sizeDelta;
            overlayRoot.pivot = previewRectTransform.pivot;
            overlayRoot.localRotation = previewRectTransform.localRotation;
            overlayRoot.localScale = previewRectTransform.localScale;
        }
    }
}
