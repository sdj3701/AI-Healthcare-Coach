using System.Collections.Generic;
using Rag.Healthcare.Pose.Analysis;
using UnityEngine;
using UnityEngine.UI;

namespace Rag.Healthcare.Pose.Rendering
{
    public sealed class PoseSkeletonRenderer : MonoBehaviour
    {
        private enum LowConfidenceMode
        {
            Hide,
            Dim
        }

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
            new BoneSegment(PoseJointNames.RightShoulder, PoseJointNames.RightHip)
        };

        [SerializeField] private JointTrackingController trackingController;
        [SerializeField] private RectTransform overlayRoot;
        [SerializeField] private bool mirrorX = true;
        [SerializeField] private bool invertY = true;
        [SerializeField, Range(0f, 1f)] private float minimumVisibility = 0.5f;
        [SerializeField] private LowConfidenceMode lowConfidenceMode = LowConfidenceMode.Dim;
        [SerializeField] private float jointSize = 10f;
        [SerializeField] private float lineThickness = 4f;
        [SerializeField] private Color leftColor = new Color(0.14f, 0.58f, 0.95f, 0.95f);
        [SerializeField] private Color rightColor = new Color(0.95f, 0.42f, 0.2f, 0.95f);
        [SerializeField] private Color centerColor = new Color(0.95f, 0.95f, 0.95f, 0.9f);
        [SerializeField] private Color lineColor = new Color(0.1f, 0.9f, 0.6f, 0.85f);
        [SerializeField] private Color lowConfidenceColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);

        private readonly Dictionary<string, Image> jointViews = new Dictionary<string, Image>();
        private readonly Dictionary<string, TrackedJoint> renderableJoints = new Dictionary<string, TrackedJoint>();
        private readonly List<Image> lineViews = new List<Image>();

        private void Awake()
        {
            overlayRoot ??= GetComponent<RectTransform>();
            trackingController ??= FindFirstObjectByType<JointTrackingController>();
        }

        private void OnEnable()
        {
            if (trackingController != null)
            {
                trackingController.TrackingFrameReceived += RenderFrame;
            }
        }

        private void OnDisable()
        {
            if (trackingController != null)
            {
                trackingController.TrackingFrameReceived -= RenderFrame;
            }
        }

        public void RenderFrame(JointTrackingFrame frame)
        {
            if (overlayRoot == null || frame == null || frame.joints == null)
            {
                HideAll();
                return;
            }

            renderableJoints.Clear();
            foreach (var joint in frame.joints)
            {
                if (CanRender(joint))
                {
                    renderableJoints[joint.name] = joint;
                }
            }

            RenderJoints();
            RenderLines();
        }

        private void RenderJoints()
        {
            foreach (var view in jointViews.Values)
            {
                view.gameObject.SetActive(false);
            }

            foreach (var jointName in PoseJointNames.MediaPipe33)
            {
                if (!renderableJoints.TryGetValue(jointName, out var joint))
                {
                    continue;
                }

                var image = GetOrCreateJointView(jointName);
                var rect = image.rectTransform;
                rect.anchoredPosition = ToAnchoredPosition(joint.x, joint.y);
                var score = PoseGeometry.GetJointScore(joint);
                var scale = score >= minimumVisibility ? 1f : 0.8f;
                rect.sizeDelta = new Vector2(jointSize * scale, jointSize * scale);
                image.color = GetJointColor(jointName, score);
                image.gameObject.SetActive(true);
            }
        }

        private void RenderLines()
        {
            EnsureLinePool(BoneSegments.Length);

            for (var i = 0; i < BoneSegments.Length; i++)
            {
                var segment = BoneSegments[i];
                var image = lineViews[i];
                if (!renderableJoints.TryGetValue(segment.From, out var from) ||
                    !renderableJoints.TryGetValue(segment.To, out var to))
                {
                    image.gameObject.SetActive(false);
                    continue;
                }

                var start = ToAnchoredPosition(from.x, from.y);
                var end = ToAnchoredPosition(to.x, to.y);
                var delta = end - start;
                var length = delta.magnitude;
                if (length <= Mathf.Epsilon)
                {
                    image.gameObject.SetActive(false);
                    continue;
                }

                var rect = image.rectTransform;
                rect.anchoredPosition = (start + end) * 0.5f;
                rect.sizeDelta = new Vector2(length, lineThickness);
                rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                var minScore = Mathf.Min(PoseGeometry.GetJointScore(from), PoseGeometry.GetJointScore(to));
                image.color = minScore >= minimumVisibility ? lineColor : lowConfidenceColor;
                image.gameObject.SetActive(true);
            }
        }

        private Image GetOrCreateJointView(string jointName)
        {
            if (jointViews.TryGetValue(jointName, out var image))
            {
                return image;
            }

            var viewObject = new GameObject(jointName, typeof(RectTransform), typeof(Image));
            viewObject.transform.SetParent(overlayRoot, false);

            image = viewObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.gameObject.SetActive(false);

            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            jointViews[jointName] = image;
            return image;
        }

        private void EnsureLinePool(int count)
        {
            while (lineViews.Count < count)
            {
                var lineObject = new GameObject("Bone Line", typeof(RectTransform), typeof(Image));
                lineObject.transform.SetParent(overlayRoot, false);

                var image = lineObject.GetComponent<Image>();
                image.raycastTarget = false;
                image.gameObject.SetActive(false);

                var rect = image.rectTransform;
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);

                lineViews.Add(image);
            }
        }

        private Vector2 ToAnchoredPosition(float normalizedX, float normalizedY)
        {
            var rect = overlayRoot.rect;
            var x = mirrorX ? 1f - normalizedX : normalizedX;
            var y = invertY ? 1f - normalizedY : normalizedY;
            return new Vector2((x - 0.5f) * rect.width, (y - 0.5f) * rect.height);
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

            return lowConfidenceMode == LowConfidenceMode.Dim ||
                   PoseGeometry.GetJointScore(joint) >= minimumVisibility;
        }

        private Color GetJointColor(string jointName, float score)
        {
            if (score < minimumVisibility)
            {
                return lowConfidenceColor;
            }

            if (jointName.StartsWith("left_"))
            {
                return leftColor;
            }

            if (jointName.StartsWith("right_"))
            {
                return rightColor;
            }

            return centerColor;
        }

        private void HideAll()
        {
            foreach (var view in jointViews.Values)
            {
                view.gameObject.SetActive(false);
            }

            foreach (var view in lineViews)
            {
                view.gameObject.SetActive(false);
            }
        }
    }
}
