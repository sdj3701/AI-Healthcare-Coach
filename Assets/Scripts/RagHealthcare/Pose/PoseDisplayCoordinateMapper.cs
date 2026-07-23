using UnityEngine;

namespace Rag.Healthcare.Pose
{
    /// <summary>
    /// Maps Provider landmarks (upright display-normalized [0,1]) into the fitted camera preview
    /// rectangle. Front-camera selfie mirroring is preview-only (ResolvePreviewScale); the live
    /// overlay path uses ToDisplayPoint(..., mirrorX: false). This mapper does not apply camera
    /// rotation. mirrorX:true remains available for tests and special callers.
    /// </summary>
    public static class PoseDisplayCoordinateMapper
    {
        public const float MinimumRenderableScore = 0.45f;

        public static bool CanRender(TrackedJoint joint)
        {
            if (joint == null || string.IsNullOrWhiteSpace(joint.name))
            {
                return false;
            }

            var score = Mathf.Max(joint.confidence, joint.visibility);
            return score >= MinimumRenderableScore &&
                   joint.x >= -0.2f && joint.x <= 1.2f &&
                   joint.y >= -0.2f && joint.y <= 1.2f;
        }

        /// <summary>
        /// Maps an upright provider landmark into <paramref name="rect"/>.
        /// Live overlay default is mirrorX=false (rear and front share the same path).
        /// Pass mirrorX=true only for util/API tests or special non-preview-mirrored paths.
        /// </summary>
        public static Vector2 ToDisplayPoint(TrackedJoint joint, Rect rect, bool mirrorX)
        {
            var normalizedX = mirrorX ? 1f - joint.x : joint.x;
            return new Vector2(
                rect.x + Mathf.Clamp01(normalizedX) * rect.width,
                rect.y + Mathf.Clamp01(joint.y) * rect.height);
        }

        public static Vector3 ResolvePreviewScale(
            int rotation,
            bool verticallyMirrored,
            bool selfieMirrored)
        {
            var scaleX = 1f;
            var scaleY = verticallyMirrored ? -1f : 1f;

            // The selfie mirror is horizontal in the final upright display space.
            // For a quarter-turn raw texture, that display X axis corresponds to
            // the texture's local Y axis before the element rotation is applied.
            if (selfieMirrored)
            {
                if (rotation == 90 || rotation == 270)
                {
                    scaleY *= -1f;
                }
                else
                {
                    scaleX = -1f;
                }
            }

            return new Vector3(scaleX, scaleY, 1f);
        }
    }
}
