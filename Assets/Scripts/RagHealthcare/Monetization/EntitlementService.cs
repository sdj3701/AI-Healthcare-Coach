using System;
using System.Collections.Generic;

namespace Rag.Healthcare.Monetization
{
    public enum ProductFeature
    {
        CameraSafetyGuide,
        LiveSafetyFeedback,
        DataDeletion,
        BasicWorkout,
        BasicReport,
        AdvancedTrendReport,
        AvatarComparisonReplay,
        ExpertContent,
        AdditionalExercises
    }

    public sealed class EntitlementService
    {
        private static readonly HashSet<ProductFeature> AlwaysAvailable = new HashSet<ProductFeature>
        {
            ProductFeature.CameraSafetyGuide,
            ProductFeature.LiveSafetyFeedback,
            ProductFeature.DataDeletion,
            ProductFeature.BasicWorkout,
            ProductFeature.BasicReport
        };

        private readonly HashSet<ProductFeature> granted = new HashSet<ProductFeature>();

        public bool Has(ProductFeature feature) => AlwaysAvailable.Contains(feature) || granted.Contains(feature);
        public void Grant(ProductFeature feature) { if (!AlwaysAvailable.Contains(feature)) granted.Add(feature); }
        public void Revoke(ProductFeature feature) { if (!AlwaysAvailable.Contains(feature)) granted.Remove(feature); }

        public static bool IsSafetyFeature(ProductFeature feature) =>
            feature == ProductFeature.CameraSafetyGuide ||
            feature == ProductFeature.LiveSafetyFeedback ||
            feature == ProductFeature.DataDeletion;
    }
}
