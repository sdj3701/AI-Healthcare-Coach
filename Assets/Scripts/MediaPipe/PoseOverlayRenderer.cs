using UnityEngine;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class PoseOverlayRenderer : MonoBehaviour
    {
        private enum LowConfidenceMode
        {
            Hide,
            Dim
        }

        [SerializeField] private float minVisibleConfidence = 0.45f;
        [SerializeField] private LowConfidenceMode lowConfidenceMode = LowConfidenceMode.Dim;
        [SerializeField] private float lineWidth = 4f;
        [SerializeField] private float pointSize = 8f;
        [SerializeField] private Color highConfidenceLineColor = new Color(0.15f, 0.9f, 0.68f, 0.95f);
        [SerializeField] private Color highConfidencePointColor = new Color(0.08f, 1f, 0.74f, 0.95f);
        [SerializeField] private Color lowConfidenceColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);

        private static Texture2D whiteTexture;

        public void DrawOverlay(Rect previewRect, LandmarkFrame frame, bool mirrored)
        {
            if (frame == null || frame.landmarks == null || frame.landmarks.Length == 0)
            {
                return;
            }

            var connections = PoseLandmarkNames.Connections;
            for (var i = 0; i < connections.Length; i++)
            {
                var connection = connections[i];
                if (!TryGetPoint(frame, connection.start, previewRect, mirrored, out var start)
                    || !TryGetPoint(frame, connection.end, previewRect, mirrored, out var end))
                {
                    continue;
                }

                var startVisibility = frame.landmarks[connection.start].visibility;
                var endVisibility = frame.landmarks[connection.end].visibility;
                var confidence = Mathf.Min(startVisibility, endVisibility);
                if (confidence < minVisibleConfidence && lowConfidenceMode == LowConfidenceMode.Hide)
                {
                    continue;
                }

                var color = confidence >= minVisibleConfidence ? highConfidenceLineColor : lowConfidenceColor;

                DrawLine(start, end, color, lineWidth);
            }

            for (var i = 0; i < frame.landmarks.Length; i++)
            {
                if (!TryGetPoint(frame, i, previewRect, mirrored, out var point))
                {
                    continue;
                }

                var confidence = frame.landmarks[i].visibility;
                if (confidence < minVisibleConfidence && lowConfidenceMode == LowConfidenceMode.Hide)
                {
                    continue;
                }

                var color = confidence >= minVisibleConfidence ? highConfidencePointColor : lowConfidenceColor;
                var size = confidence >= minVisibleConfidence ? pointSize : pointSize * 0.8f;

                DrawPoint(point, color, size);
            }
        }

        private static bool TryGetPoint(LandmarkFrame frame, int id, Rect rect, bool mirrored, out Vector2 point)
        {
            point = Vector2.zero;
            if (id < 0 || id >= frame.landmarks.Length)
            {
                return false;
            }

            var landmark = frame.landmarks[id];
            var x = mirrored ? 1f - landmark.x : landmark.x;
            point = new Vector2(rect.x + x * rect.width, rect.y + landmark.y * rect.height);
            return true;
        }

        private static void DrawPoint(Vector2 point, Color color, float size)
        {
            var previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), WhiteTexture);
            GUI.color = previousColor;
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            var delta = end - start;
            var length = delta.magnitude;
            if (length <= 0.001f)
            {
                return;
            }

            var previousMatrix = GUI.matrix;
            var previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, length, width), WhiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static Texture2D WhiteTexture
        {
            get
            {
                if (whiteTexture != null)
                {
                    return whiteTexture;
                }

                whiteTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                whiteTexture.SetPixel(0, 0, Color.white);
                whiteTexture.Apply();
                return whiteTexture;
            }
        }
    }
}
