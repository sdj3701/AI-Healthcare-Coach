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

        public void CopyFrom(PoseFeatureFrame source)
        {
            if (source == null)
            {
                Reset();
                return;
            }

            TimestampUnixMilliseconds = source.TimestampUnixMilliseconds;
            Exercise = source.Exercise;
            HasLeftKneeAngle = source.HasLeftKneeAngle;
            HasRightKneeAngle = source.HasRightKneeAngle;
            HasTorsoTilt = source.HasTorsoTilt;
            HasHipLevel = source.HasHipLevel;
            HasShoulderLevel = source.HasShoulderLevel;
            HasCenterBalance = source.HasCenterBalance;
            HasLeftKneeValgus = source.HasLeftKneeValgus;
            HasRightKneeValgus = source.HasRightKneeValgus;
            HasLeftFootVisibility = source.HasLeftFootVisibility;
            HasRightFootVisibility = source.HasRightFootVisibility;
            LeftKneeAngle = source.LeftKneeAngle;
            RightKneeAngle = source.RightKneeAngle;
            AverageKneeAngle = source.AverageKneeAngle;
            TorsoTiltDegrees = source.TorsoTiltDegrees;
            HipLevelDelta = source.HipLevelDelta;
            ShoulderLevelDelta = source.ShoulderLevelDelta;
            CenterBalanceOffset = source.CenterBalanceOffset;
            LeftKneeValgusOffset = source.LeftKneeValgusOffset;
            RightKneeValgusOffset = source.RightKneeValgusOffset;
            HipCenterY = source.HipCenterY;
            HipCenterYVelocityPerSecond = source.HipCenterYVelocityPerSecond;
            KneeAngleVelocityDegreesPerSecond = source.KneeAngleVelocityDegreesPerSecond;
            ValidityScore = source.ValidityScore;
        }

        public void Reset()
        {
            TimestampUnixMilliseconds = 0L;
            Exercise = null;
            HasLeftKneeAngle = false;
            HasRightKneeAngle = false;
            HasTorsoTilt = false;
            HasHipLevel = false;
            HasShoulderLevel = false;
            HasCenterBalance = false;
            HasLeftKneeValgus = false;
            HasRightKneeValgus = false;
            HasLeftFootVisibility = false;
            HasRightFootVisibility = false;
            LeftKneeAngle = 0f;
            RightKneeAngle = 0f;
            AverageKneeAngle = 0f;
            TorsoTiltDegrees = 0f;
            HipLevelDelta = 0f;
            ShoulderLevelDelta = 0f;
            CenterBalanceOffset = 0f;
            LeftKneeValgusOffset = 0f;
            RightKneeValgusOffset = 0f;
            HipCenterY = 0f;
            HipCenterYVelocityPerSecond = 0f;
            KneeAngleVelocityDegreesPerSecond = 0f;
            ValidityScore = 0f;
        }
    }
}
