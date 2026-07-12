using System.Collections.Generic;
using Rag.Healthcare.Pose.Analysis;
using UnityEngine;
using UnityEngine.UI;

namespace Rag.Healthcare.Pose.Rendering
{
    public sealed class PoseAvatar3DPreview : MonoBehaviour
    {
        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private Vector2 viewportSize = new Vector2(260f, 260f);
        [SerializeField] private Vector2 viewportOffset = new Vector2(-380f, 24f);
        [SerializeField, Range(128, 1024)] private int renderTextureSize = 512;
        [SerializeField] private Color backgroundColor = new Color(0.025f, 0.03f, 0.035f, 1f);

        private RenderTexture renderTexture;
        private RawImage viewportImage;
        private UnityEngine.Camera previewCamera;
        private Light previewLight;
        private PoseAvatar3DRenderer avatarRenderer;
        private bool subscribed;

        public void Initialize(JointTrackingController controller)
        {
            trackingController = controller;
            if (isActiveAndEnabled)
            {
                Subscribe();
            }
        }

        private void Awake()
        {
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            EnsurePreviewObjects();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
                renderTexture = null;
            }
        }

        private void HandleFrame(JointTrackingFrame frame)
        {
            RenderFrame(frame);
        }

        public void RenderFrame(JointTrackingFrame frame)
        {
            EnsurePreviewObjects();
            avatarRenderer?.RenderFrame(frame);
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            trackingController ??= FindFirstObjectByType<JointTrackingController>();
            if (trackingController == null)
            {
                return;
            }

            trackingController.TrackingFrameReceived += HandleFrame;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || trackingController == null)
            {
                return;
            }

            trackingController.TrackingFrameReceived -= HandleFrame;
            subscribed = false;
        }

        private void EnsurePreviewObjects()
        {
            EnsureRenderTexture();
            EnsureViewportImage();
            EnsureWorldObjects();
        }

        private void EnsureRenderTexture()
        {
            if (renderTexture != null &&
                renderTexture.width == renderTextureSize &&
                renderTexture.height == renderTextureSize)
            {
                return;
            }

            if (renderTexture != null)
            {
                renderTexture.Release();
                Destroy(renderTexture);
            }

            renderTexture = new RenderTexture(renderTextureSize, renderTextureSize, 16, RenderTextureFormat.ARGB32)
            {
                name = "PoseAvatar3DPreviewRT",
                antiAliasing = 2
            };
            renderTexture.Create();

            if (viewportImage != null)
            {
                viewportImage.texture = renderTexture;
            }

            if (previewCamera != null)
            {
                previewCamera.targetTexture = renderTexture;
            }
        }

        private void EnsureViewportImage()
        {
            if (viewportImage != null)
            {
                viewportImage.texture = renderTexture;
                return;
            }

            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var previewObject = new GameObject("3D Pose Avatar Preview", typeof(RectTransform), typeof(RawImage));
            previewObject.transform.SetParent(canvas.transform, false);

            var rect = previewObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.sizeDelta = viewportSize;
            rect.anchoredPosition = viewportOffset;

            viewportImage = previewObject.GetComponent<RawImage>();
            viewportImage.texture = renderTexture;
            viewportImage.color = Color.white;
            viewportImage.raycastTarget = false;
        }

        private void EnsureWorldObjects()
        {
            if (avatarRenderer != null && previewCamera != null)
            {
                return;
            }

            var origin = new Vector3(1000f, 0f, 0f);

            var avatarObject = new GameObject("Generated 3D Pose Avatar");
            avatarObject.transform.position = origin;
            avatarRenderer = avatarObject.AddComponent<PoseAvatar3DRenderer>();

            var cameraObject = new GameObject("3D Pose Avatar Camera");
            cameraObject.transform.position = origin + new Vector3(0f, 1.7f, -5.2f);
            cameraObject.transform.LookAt(origin + new Vector3(0f, 1.55f, 0f));

            previewCamera = cameraObject.AddComponent<UnityEngine.Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = backgroundColor;
            previewCamera.nearClipPlane = 0.05f;
            previewCamera.farClipPlane = 20f;
            previewCamera.fieldOfView = 35f;
            previewCamera.targetTexture = renderTexture;

            var lightObject = new GameObject("3D Pose Avatar Light");
            lightObject.transform.position = origin + new Vector3(0f, 3f, -2.5f);
            lightObject.transform.rotation = Quaternion.Euler(50f, -20f, 0f);
            previewLight = lightObject.AddComponent<Light>();
            previewLight.type = LightType.Directional;
            previewLight.intensity = 1.4f;
        }
    }

    public sealed class PoseAvatar3DRenderer : MonoBehaviour
    {
        private readonly struct BoneSegment
        {
            public BoneSegment(string from, string to)
            {
                From = from;
                To = to;
            }

            public string From { get; }
            public string To { get; }
        }

        private static readonly BoneSegment[] BoneSegments =
        {
            new BoneSegment(PoseJointNames.LeftShoulder, PoseJointNames.RightShoulder),
            new BoneSegment(PoseJointNames.LeftHip, PoseJointNames.RightHip),
            new BoneSegment(PoseJointNames.LeftShoulder, PoseJointNames.LeftElbow),
            new BoneSegment(PoseJointNames.LeftElbow, PoseJointNames.LeftWrist),
            new BoneSegment(PoseJointNames.RightShoulder, PoseJointNames.RightElbow),
            new BoneSegment(PoseJointNames.RightElbow, PoseJointNames.RightWrist),
            new BoneSegment(PoseJointNames.LeftHip, PoseJointNames.LeftKnee),
            new BoneSegment(PoseJointNames.LeftKnee, PoseJointNames.LeftAnkle),
            new BoneSegment(PoseJointNames.LeftAnkle, PoseJointNames.LeftHeel),
            new BoneSegment(PoseJointNames.LeftHeel, PoseJointNames.LeftFootIndex),
            new BoneSegment(PoseJointNames.RightHip, PoseJointNames.RightKnee),
            new BoneSegment(PoseJointNames.RightKnee, PoseJointNames.RightAnkle),
            new BoneSegment(PoseJointNames.RightAnkle, PoseJointNames.RightHeel),
            new BoneSegment(PoseJointNames.RightHeel, PoseJointNames.RightFootIndex),
            new BoneSegment(PoseJointNames.LeftShoulder, PoseJointNames.LeftHip),
            new BoneSegment(PoseJointNames.RightShoulder, PoseJointNames.RightHip),
            new BoneSegment(PoseJointNames.Nose, PoseJointNames.LeftShoulder),
            new BoneSegment(PoseJointNames.Nose, PoseJointNames.RightShoulder)
        };

        [SerializeField, Range(0f, 1f)] private float minimumVisibility = 0.45f;
        [SerializeField] private bool mirrorX = true;
        [SerializeField] private float widthScale = 2.8f;
        [SerializeField] private float heightScale = 3.4f;
        [SerializeField] private float depthScale = 1.2f;
        [SerializeField] private float jointRadius = 0.055f;
        [SerializeField] private float boneRadius = 0.035f;
        [SerializeField] private Color leftColor = new Color(0.14f, 0.58f, 0.95f, 1f);
        [SerializeField] private Color rightColor = new Color(0.95f, 0.42f, 0.2f, 1f);
        [SerializeField] private Color centerColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        [SerializeField] private Color boneColor = new Color(0.1f, 0.9f, 0.6f, 1f);

        private readonly Dictionary<string, Transform> jointViews = new Dictionary<string, Transform>();
        private readonly Dictionary<string, Vector3> jointPositions = new Dictionary<string, Vector3>();
        private readonly List<Transform> boneViews = new List<Transform>();

        private Material leftMaterial;
        private Material rightMaterial;
        private Material centerMaterial;
        private Material boneMaterial;

        private void Awake()
        {
            EnsureMaterials();
        }

        public void RenderFrame(JointTrackingFrame frame)
        {
            EnsureMaterials();

            if (frame == null || frame.joints == null)
            {
                HideAll();
                return;
            }

            jointPositions.Clear();
            foreach (var joint in frame.joints)
            {
                if (!CanRender(joint))
                {
                    continue;
                }

                jointPositions[joint.name] = ToLocalPosition(joint);
            }

            RenderJoints();
            RenderBones();
        }

        private void RenderJoints()
        {
            foreach (var jointView in jointViews.Values)
            {
                jointView.gameObject.SetActive(false);
            }

            foreach (var jointName in PoseJointNames.MediaPipe33)
            {
                if (!jointPositions.TryGetValue(jointName, out var position))
                {
                    continue;
                }

                var view = GetOrCreateJointView(jointName);
                view.localPosition = position;
                view.localScale = Vector3.one * GetJointDiameter(jointName);
                view.gameObject.SetActive(true);
            }
        }

        private void RenderBones()
        {
            EnsureBonePool(BoneSegments.Length);

            for (var i = 0; i < BoneSegments.Length; i++)
            {
                var segment = BoneSegments[i];
                var bone = boneViews[i];
                if (!jointPositions.TryGetValue(segment.From, out var from) ||
                    !jointPositions.TryGetValue(segment.To, out var to))
                {
                    bone.gameObject.SetActive(false);
                    continue;
                }

                SetCapsuleBetween(bone, from, to);
            }
        }

        private Transform GetOrCreateJointView(string jointName)
        {
            if (jointViews.TryGetValue(jointName, out var view))
            {
                return view;
            }

            var jointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            jointObject.name = "3D Joint " + jointName;
            jointObject.transform.SetParent(transform, false);
            RemoveCollider(jointObject);

            var renderer = jointObject.GetComponent<Renderer>();
            renderer.sharedMaterial = GetJointMaterial(jointName);

            view = jointObject.transform;
            view.gameObject.SetActive(false);
            jointViews[jointName] = view;
            return view;
        }

        private void EnsureBonePool(int count)
        {
            while (boneViews.Count < count)
            {
                var boneObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                boneObject.name = "3D Bone";
                boneObject.transform.SetParent(transform, false);
                RemoveCollider(boneObject);

                var renderer = boneObject.GetComponent<Renderer>();
                renderer.sharedMaterial = boneMaterial;

                boneObject.SetActive(false);
                boneViews.Add(boneObject.transform);
            }
        }

        private Vector3 ToLocalPosition(TrackedJoint joint)
        {
            var x = mirrorX ? 1f - joint.x : joint.x;
            var y = 1f - joint.y;
            var z = -joint.z;

            return new Vector3(
                (x - 0.5f) * widthScale,
                Mathf.Clamp01(y) * heightScale,
                Mathf.Clamp(z * depthScale, -1.2f, 1.2f));
        }

        private bool CanRender(TrackedJoint joint)
        {
            if (joint == null || string.IsNullOrWhiteSpace(joint.name))
            {
                return false;
            }

            if (joint.x < -0.2f || joint.x > 1.2f || joint.y < -0.2f || joint.y > 1.2f)
            {
                return false;
            }

            return PoseGeometry.GetJointScore(joint) >= minimumVisibility;
        }

        private float GetJointDiameter(string jointName)
        {
            if (jointName == PoseJointNames.Nose)
            {
                return jointRadius * 3.2f;
            }

            if (jointName.Contains("eye") || jointName.Contains("ear") || jointName.Contains("mouth"))
            {
                return jointRadius * 1.4f;
            }

            return jointRadius * 2f;
        }

        private Material GetJointMaterial(string jointName)
        {
            if (jointName.StartsWith("left_"))
            {
                return leftMaterial;
            }

            if (jointName.StartsWith("right_"))
            {
                return rightMaterial;
            }

            return centerMaterial;
        }

        private void SetCapsuleBetween(Transform capsule, Vector3 from, Vector3 to)
        {
            var delta = to - from;
            var length = delta.magnitude;
            if (length <= Mathf.Epsilon)
            {
                capsule.gameObject.SetActive(false);
                return;
            }

            capsule.localPosition = (from + to) * 0.5f;
            capsule.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            capsule.localScale = new Vector3(boneRadius * 2f, length * 0.5f, boneRadius * 2f);
            capsule.gameObject.SetActive(true);
        }

        private void EnsureMaterials()
        {
            if (leftMaterial != null)
            {
                return;
            }

            leftMaterial = CreateMaterial("3D Avatar Left", leftColor);
            rightMaterial = CreateMaterial("3D Avatar Right", rightColor);
            centerMaterial = CreateMaterial("3D Avatar Center", centerColor);
            boneMaterial = CreateMaterial("3D Avatar Bone", boneColor);
        }

        private static Material CreateMaterial(string name, Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader)
            {
                name = name,
                color = color
            };
            return material;
        }

        private static void RemoveCollider(GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private void HideAll()
        {
            foreach (var jointView in jointViews.Values)
            {
                jointView.gameObject.SetActive(false);
            }

            foreach (var boneView in boneViews)
            {
                boneView.gameObject.SetActive(false);
            }
        }
    }
}
