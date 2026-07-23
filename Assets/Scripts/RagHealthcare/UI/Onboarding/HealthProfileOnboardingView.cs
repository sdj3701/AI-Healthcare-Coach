using System;
using System.Globalization;
using Rag.Healthcare.Product;
using Rag.Healthcare.Rag.Runtime;
using UnityEngine;
using UnityEngine.UIElements;

namespace Rag.Healthcare.UI
{
    /// <summary>
    /// Two-step health/workout profile onboarding form (PBI-110 Phase 4).
    /// </summary>
    public sealed class HealthProfileOnboardingView
    {
        private const int ColorBg = 0x0B1119;
        private const int ColorMuted = 0x8F9AAF;
        private const int ColorAccent = 0x34D399;
        private const int ColorButton = 0x161B22;
        private const int ColorSelected = 0x064E3B;
        private const int ColorSelectedText = 0xBBF7D0;
        private const int ColorPrimary = 0x22C55E;
        private const int ColorPrimaryText = 0x07110C;
        private const int ColorFieldBg = 0xF1F5F9;
        private const int ColorFieldText = 0x020617;

        private readonly OnboardingStatusManager manager;
        private readonly Action onCompleted;
        private readonly Font font;
        private readonly VisualElement root;

        private int stepIndex;
        private bool step2Seeded;
        private TextField ageField;
        private TextField heightField;
        private TextField weightField;
        private Gender selectedGender = Gender.Unspecified;
        private InjuryRegions selectedInjuries = InjuryRegions.None;
        private WorkoutGoal selectedGoal = WorkoutGoal.GeneralFitness;
        private WorkoutPlace selectedPlace = WorkoutPlace.Home;
        private int sessionsPerWeek = 3;
        private SkillLevel selectedSkill = SkillLevel.Beginner;
        private Label stepTitleLabel;
        private Label errorLabel;
        private VisualElement stepBody;
        private Button genderMaleButton;
        private Button genderFemaleButton;
        private Button genderOtherButton;

        public HealthProfileOnboardingView(OnboardingStatusManager manager, Action onCompleted, Font font)
        {
            this.manager = manager;
            this.onCompleted = onCompleted;
            this.font = font;
            root = BuildRoot();
            ShowStep(0);
        }

        public VisualElement Root => root;

        public void Bind(VisualElement parent)
        {
            if (parent == null)
            {
                return;
            }

            if (root.parent != null)
            {
                root.RemoveFromHierarchy();
            }

            parent.Add(root);
        }

        private VisualElement BuildRoot()
        {
            var container = new VisualElement { name = "health-profile-onboarding" };
            container.style.flexGrow = 1f;
            container.style.backgroundColor = ColorFromHex(ColorBg);
            container.style.paddingTop = 8f;
            container.style.paddingBottom = 12f;

            stepTitleLabel = CreateLabel("건강 프로필 · 1/2", 16, Color.white, FontStyle.Bold);
            container.Add(stepTitleLabel);

            var subtitle = CreateLabel(
                "안전하게 코칭하기 위한 기본 정보예요. 의료 진단이 아닙니다.",
                11,
                ColorFromHex(ColorMuted),
                FontStyle.Normal);
            subtitle.style.marginTop = 4f;
            subtitle.style.marginBottom = 12f;
            container.Add(subtitle);

            stepBody = new VisualElement { name = "profile-step-body" };
            stepBody.style.flexGrow = 1f;
            container.Add(stepBody);

            errorLabel = CreateLabel(string.Empty, 11, ColorFromHex(0xFB7185), FontStyle.Normal);
            errorLabel.style.marginTop = 8f;
            errorLabel.style.display = DisplayStyle.None;
            container.Add(errorLabel);

            var nav = new VisualElement { name = "profile-nav" };
            nav.style.flexDirection = FlexDirection.Row;
            nav.style.marginTop = 12f;
            nav.style.height = 52f;

            var back = CreateButton("이전", ColorFromHex(ColorButton), Color.white, () =>
            {
                if (stepIndex <= 0)
                {
                    return;
                }

                ShowStep(0);
            });
            back.style.flexGrow = 1f;
            back.style.marginRight = 8f;
            nav.Add(back);

            var next = CreateButton("다음", ColorFromHex(ColorPrimary), ColorFromHex(ColorPrimaryText), () =>
            {
                if (stepIndex == 0)
                {
                    if (!TryReadBodyMetrics(out var age, out var height, out var weight))
                    {
                        return;
                    }

                    manager.SetBodyMetrics(age, selectedGender, height, weight);
                    ShowStep(1);
                    return;
                }

                CommitAndFinish();
            });
            next.name = "profile-next-button";
            next.style.flexGrow = 1f;
            nav.Add(next);
            container.Add(nav);

            return container;
        }

        private void ShowStep(int index)
        {
            var next = Mathf.Clamp(index, 0, 1);
            if (next == 0)
            {
                step2Seeded = false;
            }
            else if (!step2Seeded)
            {
                SeedStep2FromProfile();
                step2Seeded = true;
            }

            stepIndex = next;
            errorLabel.style.display = DisplayStyle.None;
            stepBody.Clear();

            if (stepIndex == 0)
            {
                stepTitleLabel.text = "건강 프로필 · 1/2";
                BuildStep1Body();
            }
            else
            {
                stepTitleLabel.text = "건강 프로필 · 2/2";
                BuildStep2Body();
            }

            var nextButton = root.Q<Button>("profile-next-button");
            if (nextButton != null)
            {
                nextButton.text = stepIndex == 0 ? "다음" : "완료하고 시작";
            }
        }

        private void SeedStep2FromProfile()
        {
            if (manager.Profile == null)
            {
                return;
            }

            selectedInjuries = manager.Profile.injuries;
            selectedGoal = manager.Profile.goal == WorkoutGoal.Unspecified
                ? WorkoutGoal.GeneralFitness
                : manager.Profile.goal;
            selectedPlace = manager.Profile.place == WorkoutPlace.Unspecified
                ? WorkoutPlace.Home
                : manager.Profile.place;
            sessionsPerWeek = manager.Profile.sessionsPerWeek > 0 ? manager.Profile.sessionsPerWeek : 3;
            selectedSkill = manager.Profile.skill;
        }

        private void BuildStep1Body()
        {
            stepBody.Add(CreateLabel("나이", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            ageField = CreateNumberField(manager.Profile != null && manager.Profile.ageYears > 0
                ? manager.Profile.ageYears.ToString()
                : string.Empty);
            stepBody.Add(ageField);

            stepBody.Add(Spacer(10f));
            stepBody.Add(CreateLabel("성별", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            selectedGender = manager.Profile != null ? manager.Profile.gender : Gender.Unspecified;
            var genderRow = CreateFlexRow(6f);
            genderMaleButton = CreateChip("남", selectedGender == Gender.Male, () => SelectGender(Gender.Male));
            genderFemaleButton = CreateChip("여", selectedGender == Gender.Female, () => SelectGender(Gender.Female));
            genderOtherButton = CreateChip("기타", selectedGender == Gender.Other, () => SelectGender(Gender.Other));
            genderRow.Add(genderMaleButton);
            genderRow.Add(genderFemaleButton);
            genderRow.Add(genderOtherButton);
            stepBody.Add(genderRow);

            stepBody.Add(Spacer(10f));
            stepBody.Add(CreateLabel("키 (cm)", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            heightField = CreateNumberField(manager.Profile != null && manager.Profile.heightCm > 0f
                ? manager.Profile.heightCm.ToString("0.#")
                : string.Empty);
            stepBody.Add(heightField);

            stepBody.Add(Spacer(10f));
            stepBody.Add(CreateLabel("몸무게 (kg)", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            weightField = CreateNumberField(manager.Profile != null && manager.Profile.weightKg > 0f
                ? manager.Profile.weightKg.ToString("0.#")
                : string.Empty);
            stepBody.Add(weightField);
        }

        private void BuildStep2Body()
        {
            stepBody.Add(CreateLabel("불편한 부위 (다중 선택)", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            var note = CreateLabel("무리가 가지 않도록 안전하게 코칭해 드려요.", 10, ColorFromHex(ColorMuted), FontStyle.Normal);
            note.style.marginBottom = 6f;
            stepBody.Add(note);

            var injuryRow = CreateFlexRow(0f, wrap: true);
            injuryRow.Add(CreateInjuryChip("어깨", InjuryRegions.Shoulder));
            injuryRow.Add(CreateInjuryChip("허리", InjuryRegions.LowerBack));
            injuryRow.Add(CreateInjuryChip("무릎", InjuryRegions.Knee));
            injuryRow.Add(CreateInjuryChip("목", InjuryRegions.Neck));
            stepBody.Add(injuryRow);

            stepBody.Add(Spacer(12f));
            stepBody.Add(CreateLabel("운동 목적", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            var goalRow = CreateFlexRow(6f, wrap: true);
            goalRow.Add(CreateEnumChip("건강", WorkoutGoal.GeneralFitness, () => selectedGoal, v => selectedGoal = v));
            goalRow.Add(CreateEnumChip("체중", WorkoutGoal.WeightLoss, () => selectedGoal, v => selectedGoal = v));
            goalRow.Add(CreateEnumChip("근력", WorkoutGoal.MuscleGain, () => selectedGoal, v => selectedGoal = v));
            goalRow.Add(CreateEnumChip("가동성", WorkoutGoal.Mobility, () => selectedGoal, v => selectedGoal = v));
            stepBody.Add(goalRow);

            stepBody.Add(Spacer(12f));
            stepBody.Add(CreateLabel("운동 장소", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            var placeRow = CreateFlexRow(6f, wrap: true);
            placeRow.Add(CreateEnumChip("집", WorkoutPlace.Home, () => selectedPlace, v => selectedPlace = v));
            placeRow.Add(CreateEnumChip("헬스장", WorkoutPlace.Gym, () => selectedPlace, v => selectedPlace = v));
            placeRow.Add(CreateEnumChip("야외", WorkoutPlace.Outdoor, () => selectedPlace, v => selectedPlace = v));
            stepBody.Add(placeRow);

            stepBody.Add(Spacer(12f));
            stepBody.Add(CreateLabel("주당 운동 횟수", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            var freqRow = CreateFlexRow(6f);
            freqRow.style.alignItems = Align.Center;
            freqRow.style.height = 40f;
            var minus = CreateButton("-", ColorFromHex(ColorButton), Color.white, () =>
            {
                sessionsPerWeek = Mathf.Max(1, sessionsPerWeek - 1);
                ShowStep(1);
            });
            minus.style.width = 44f;
            minus.style.flexGrow = 0f;
            var freqLabel = CreateLabel(sessionsPerWeek + "회", 14, ColorFromHex(ColorAccent), FontStyle.Bold);
            freqLabel.style.flexGrow = 1f;
            freqLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            var plus = CreateButton("+", ColorFromHex(ColorButton), Color.white, () =>
            {
                sessionsPerWeek = Mathf.Min(14, sessionsPerWeek + 1);
                ShowStep(1);
            });
            plus.style.width = 44f;
            plus.style.flexGrow = 0f;
            freqRow.Add(minus);
            freqRow.Add(freqLabel);
            freqRow.Add(plus);
            stepBody.Add(freqRow);

            stepBody.Add(Spacer(12f));
            stepBody.Add(CreateLabel("숙련도", 11, ColorFromHex(ColorMuted), FontStyle.Bold));
            var skillRow = CreateFlexRow(6f, wrap: true);
            skillRow.Add(CreateEnumChip("초보", SkillLevel.Beginner, () => selectedSkill, v => selectedSkill = v));
            skillRow.Add(CreateEnumChip("보통", SkillLevel.Standard, () => selectedSkill, v => selectedSkill = v));
            skillRow.Add(CreateEnumChip("숙련", SkillLevel.Advanced, () => selectedSkill, v => selectedSkill = v));
            stepBody.Add(skillRow);
        }

        private void SelectGender(Gender gender)
        {
            selectedGender = gender;
            ApplyChipSelected(genderMaleButton, selectedGender == Gender.Male);
            ApplyChipSelected(genderFemaleButton, selectedGender == Gender.Female);
            ApplyChipSelected(genderOtherButton, selectedGender == Gender.Other);
        }

        private Button CreateInjuryChip(string label, InjuryRegions region)
        {
            var selected = (selectedInjuries & region) != 0;
            var button = CreateChip(label, selected, () =>
            {
                if ((selectedInjuries & region) != 0)
                {
                    selectedInjuries &= ~region;
                }
                else
                {
                    selectedInjuries |= region;
                }

                ShowStep(1);
            });
            return button;
        }

        private Button CreateEnumChip<T>(string label, T value, Func<T> getter, Action<T> setter) where T : struct
        {
            var selected = Equals(getter(), value);
            return CreateChip(label, selected, () =>
            {
                setter(value);
                ShowStep(1);
            });
        }

        private bool TryReadBodyMetrics(out int age, out float height, out float weight)
        {
            age = 0;
            height = 0f;
            weight = 0f;

            if (selectedGender == Gender.Unspecified)
            {
                ShowError("성별을 선택해 주세요.");
                return false;
            }

            if (!int.TryParse(ageField?.value, out age) || age < 10 || age > 100)
            {
                ShowError("나이를 올바르게 입력해 주세요.");
                return false;
            }

            if ((!float.TryParse(heightField?.value, NumberStyles.Float, CultureInfo.InvariantCulture, out height) &&
                 !float.TryParse(heightField?.value, out height)) ||
                height < 100f || height > 250f)
            {
                ShowError("키(cm)를 올바르게 입력해 주세요.");
                return false;
            }

            if ((!float.TryParse(weightField?.value, NumberStyles.Float, CultureInfo.InvariantCulture, out weight) &&
                 !float.TryParse(weightField?.value, out weight)) ||
                weight < 30f || weight > 250f)
            {
                ShowError("몸무게(kg)를 올바르게 입력해 주세요.");
                return false;
            }

            return true;
        }

        private void CommitAndFinish()
        {
            manager.CommitWorkoutPreferences(
                selectedInjuries,
                selectedGoal,
                selectedPlace,
                EquipmentFlags.Bodyweight,
                sessionsPerWeek,
                selectedSkill,
                new PersonalizedRomEvaluator());
            onCompleted?.Invoke();
        }

        private void ShowError(string message)
        {
            errorLabel.text = message;
            errorLabel.style.display = DisplayStyle.Flex;
        }

        private TextField CreateNumberField(string value)
        {
            var field = new TextField { value = value ?? string.Empty };
            field.style.marginTop = 6f;
            field.style.height = 40f;
            field.style.backgroundColor = ColorFromHex(ColorFieldBg);
            field.style.color = ColorFromHex(ColorFieldText);
            field.style.unityFont = font;
            field.style.borderTopLeftRadius = 10f;
            field.style.borderTopRightRadius = 10f;
            field.style.borderBottomLeftRadius = 10f;
            field.style.borderBottomRightRadius = 10f;
            field.style.paddingLeft = 10f;
            field.style.paddingRight = 10f;
            return field;
        }

        private Button CreateChip(string text, bool selected, Action onClick)
        {
            var button = CreateButton(text, selected ? ColorFromHex(ColorSelected) : ColorFromHex(ColorButton),
                selected ? ColorFromHex(ColorSelectedText) : Color.white, onClick);
            button.style.height = 36f;
            button.style.flexGrow = 0f;
            button.style.marginRight = 6f;
            button.style.marginBottom = 6f;
            button.style.minWidth = 64f;
            return button;
        }

        private static void ApplyChipSelected(Button button, bool selected)
        {
            if (button == null)
            {
                return;
            }

            button.style.backgroundColor = selected ? ColorFromHex(ColorSelected) : ColorFromHex(ColorButton);
            button.style.color = selected ? ColorFromHex(ColorSelectedText) : Color.white;
        }

        private Button CreateButton(string text, Color background, Color textColor, Action action)
        {
            var button = new Button(action) { text = text };
            button.style.backgroundColor = background;
            button.style.color = textColor;
            button.style.unityFont = font;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.fontSize = 12f;
            button.style.borderTopLeftRadius = 12f;
            button.style.borderTopRightRadius = 12f;
            button.style.borderBottomLeftRadius = 12f;
            button.style.borderBottomRightRadius = 12f;
            button.style.borderTopWidth = 0f;
            button.style.borderRightWidth = 0f;
            button.style.borderBottomWidth = 0f;
            button.style.borderLeftWidth = 0f;
            return button;
        }

        private Label CreateLabel(string text, int size, Color color, FontStyle style)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFont = font;
            label.style.unityFontStyleAndWeight = style;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static VisualElement CreateFlexRow(float marginTop, bool wrap = false)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.marginTop = marginTop;
            if (wrap)
            {
                row.style.flexWrap = Wrap.Wrap;
            }

            return row;
        }

        private static VisualElement Spacer(float height)
        {
            var spacer = new VisualElement();
            spacer.style.height = height;
            return spacer;
        }

        private static Color ColorFromHex(int rgb)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                1f);
        }
    }
}
