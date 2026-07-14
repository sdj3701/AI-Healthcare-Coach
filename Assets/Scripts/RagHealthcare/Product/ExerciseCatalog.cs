using System;
using System.Collections.Generic;

namespace Rag.Healthcare.Product
{
    public enum ExerciseAvailability { Available, ComingSoon, Hidden }
    public enum ExerciseDifficulty { Beginner, Standard, Advanced }

    [Serializable]
    public sealed class ExercisePreset
    {
        public ExerciseDifficulty difficulty;
        public int defaultTargetRepetitions;
        public float minimumDepthRatio;
        public float maximumTorsoTiltDegrees;
        public float minimumJointConfidence;
    }

    [Serializable]
    public sealed class ExerciseDefinition
    {
        public string id;
        public string displayName;
        public string summary;
        public ExerciseAvailability availability;
        public string unavailableReason;
        public ExercisePreset[] presets;
    }

    public static class ExerciseCatalog
    {
        private static readonly ExerciseDefinition[] Items =
        {
            new ExerciseDefinition
            {
                id = "squat",
                displayName = "스쿼트",
                summary = "무릎 정렬, 깊이, 상체 기울기를 실시간으로 확인합니다.",
                availability = ExerciseAvailability.Available,
                presets = new[]
                {
                    new ExercisePreset { difficulty = ExerciseDifficulty.Beginner, defaultTargetRepetitions = 8, minimumDepthRatio = 0.42f, maximumTorsoTiltDegrees = 42f, minimumJointConfidence = 0.45f },
                    new ExercisePreset { difficulty = ExerciseDifficulty.Standard, defaultTargetRepetitions = 12, minimumDepthRatio = 0.50f, maximumTorsoTiltDegrees = 35f, minimumJointConfidence = 0.55f },
                    new ExercisePreset { difficulty = ExerciseDifficulty.Advanced, defaultTargetRepetitions = 15, minimumDepthRatio = 0.58f, maximumTorsoTiltDegrees = 30f, minimumJointConfidence = 0.65f }
                }
            },
            new ExerciseDefinition
            {
                id = "lunge",
                displayName = "런지",
                summary = "좌우 하체 균형과 무릎 정렬 코칭",
                availability = ExerciseAvailability.ComingSoon,
                unavailableReason = "Beta 이후 제공 예정",
                presets = Array.Empty<ExercisePreset>()
            }
        };

        public static IReadOnlyList<ExerciseDefinition> All => Items;

        public static bool TryGet(string id, out ExerciseDefinition definition)
        {
            definition = Array.Find(Items, item => string.Equals(item.id, id, StringComparison.OrdinalIgnoreCase));
            return definition != null;
        }

        public static bool TryGetPreset(string exerciseId, ExerciseDifficulty difficulty, out ExercisePreset preset)
        {
            preset = null;
            if (!TryGet(exerciseId, out var exercise) || exercise.presets == null)
            {
                return false;
            }

            preset = Array.Find(exercise.presets, item => item.difficulty == difficulty);
            return preset != null;
        }

        public static string[] BuildPreflightChecklist(bool modelRequired)
        {
            var values = new List<string>
            {
                "주변 장애물을 치우고 미끄럽지 않은 바닥인지 확인",
                "전신과 발끝이 카메라에 보이는지 확인",
                "통증·어지럼증이 있으면 시작하지 않기",
                "카메라 영상은 저장되지 않으며 좌표 로그 저장은 설정에서 선택"
            };
            if (modelRequired)
            {
                values.Add("선택한 온디바이스 모델 다운로드 용량과 Wi-Fi 상태 확인");
            }
            return values.ToArray();
        }
    }
}
