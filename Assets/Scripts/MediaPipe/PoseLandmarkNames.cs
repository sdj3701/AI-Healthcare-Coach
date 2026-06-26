namespace AIHealthcareCoach.MediaPipe
{
    public struct PoseConnection
    {
        public readonly int start;
        public readonly int end;

        public PoseConnection(int start, int end)
        {
            this.start = start;
            this.end = end;
        }
    }

    public static class PoseLandmarkNames
    {
        public const int Count = 33;

        private static readonly string[] Names =
        {
            "nose",
            "left_eye_inner",
            "left_eye",
            "left_eye_outer",
            "right_eye_inner",
            "right_eye",
            "right_eye_outer",
            "left_ear",
            "right_ear",
            "mouth_left",
            "mouth_right",
            "left_shoulder",
            "right_shoulder",
            "left_elbow",
            "right_elbow",
            "left_wrist",
            "right_wrist",
            "left_pinky",
            "right_pinky",
            "left_index",
            "right_index",
            "left_thumb",
            "right_thumb",
            "left_hip",
            "right_hip",
            "left_knee",
            "right_knee",
            "left_ankle",
            "right_ankle",
            "left_heel",
            "right_heel",
            "left_foot_index",
            "right_foot_index"
        };

        public static readonly PoseConnection[] Connections =
        {
            new PoseConnection(11, 12),
            new PoseConnection(11, 13),
            new PoseConnection(13, 15),
            new PoseConnection(15, 17),
            new PoseConnection(15, 19),
            new PoseConnection(15, 21),
            new PoseConnection(17, 19),
            new PoseConnection(12, 14),
            new PoseConnection(14, 16),
            new PoseConnection(16, 18),
            new PoseConnection(16, 20),
            new PoseConnection(16, 22),
            new PoseConnection(18, 20),
            new PoseConnection(11, 23),
            new PoseConnection(12, 24),
            new PoseConnection(23, 24),
            new PoseConnection(23, 25),
            new PoseConnection(25, 27),
            new PoseConnection(27, 29),
            new PoseConnection(29, 31),
            new PoseConnection(27, 31),
            new PoseConnection(24, 26),
            new PoseConnection(26, 28),
            new PoseConnection(28, 30),
            new PoseConnection(30, 32),
            new PoseConnection(28, 32)
        };

        public static string GetName(int id)
        {
            if (id < 0 || id >= Names.Length)
            {
                return "unknown";
            }

            return Names[id];
        }
    }
}
