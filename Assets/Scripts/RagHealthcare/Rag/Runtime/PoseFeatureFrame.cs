namespace Rag.Healthcare.Rag.Runtime
{
    public sealed class PoseFeatureFrame
    {
        public long TimestampUnixMilliseconds;
        public string Exercise;

        public bool HasLeftKneeAngle;
        public bool HasRightKneeAngle;
        public bool HasTorsoTilt;
        public bool HasHipLevel;
        public bool HasShoulderLevel;
        public bool HasCenterBalance;
        public bool HasLeftKneeValgus;
        public bool HasRightKneeValgus;
        public bool HasLeftFootVisibility;
        public bool HasRightFootVisibility;

        public float LeftKneeAngle;
        public float RightKneeAngle;
        public float AverageKneeAngle;
        public float TorsoTiltDegrees;
        public float HipLevelDelta;
        public float ShoulderLevelDelta;
        public float CenterBalanceOffset;
        public float LeftKneeValgusOffset;
        public float RightKneeValgusOffset;
        public float HipCenterY;
        public float HipCenterYVelocityPerSecond;
        public float KneeAngleVelocityDegreesPerSecond;
        public float ValidityScore;

        public bool HasReliableSquatCore =>
            HasLeftKneeAngle &&
            HasRightKneeAngle &&
            HasTorsoTilt &&
            HasCenterBalance;
    }
}
