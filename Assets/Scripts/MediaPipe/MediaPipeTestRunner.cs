using System.Collections;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using AIHealthcareCoach.Tts;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AIHealthcareCoach.MediaPipe
{
    public sealed class MediaPipeTestRunner : MonoBehaviour
    {
        [Header("Pose Sampling")]
        [SerializeField] private int targetPoseFps = 15;
        [SerializeField, Tooltip("Draws a synthetic pose for overlay UI testing only. It does not track the camera image.")]
        private bool simulatePoseWhenNativeUnavailable;

        [Header("Editor MediaPipe")]
        [SerializeField] private bool usePythonMediaPipeInEditor = true;
        [SerializeField] private string editorPythonExecutablePath;
        [SerializeField] private string editorPythonWorkerRelativePath = "MediaPipe/editor_pose_worker.py";
        [SerializeField, Tooltip("Shows a Unity Editor button that creates .venv-mediapipe and installs mediapipe/numpy.")]
        private bool showEditorPythonSetupButton = true;

        [Header("MediaPipe")]
        [SerializeField] private string modelRelativePath = "MediaPipe/pose_landmarker_lite.task";
        [SerializeField] private float minPoseDetectionConfidence = 0.35f;
        [SerializeField] private float minPosePresenceConfidence = 0.5f;
        [SerializeField] private float minTrackingConfidence = 0.5f;
        [SerializeField] private float requiredVisibility = 0.45f;
        [SerializeField] private float requiredPresence = 0.45f;
        [SerializeField] private float averageVisibilityThreshold = 0.55f;
        [SerializeField] private float landmarkEdgeMargin = 0.02f;

        [Header("Calibration")]
        [SerializeField] private float calibrationSeconds = 2f;

        [Header("Exercise Feedback")]
        [SerializeField] private bool analyzeExerciseFeedback = true;
        [SerializeField] private bool speakExerciseFeedback;
        [SerializeField, Range(0f, 1f)] private float exerciseFeedbackMinimumConfidence = 0.5f;
        [SerializeField, Range(0f, 10f)] private float exerciseFeedbackCooldownSeconds = 2f;

        [Header("QA Logging")]
        [SerializeField] private bool writeQaLog = true;
        [SerializeField] private float qaLogIntervalSeconds = 1f;
        [SerializeField] private string qaLogFileName = "mediapipe_pose_qa.jsonl";

        [Header("Session Storage")]
        [SerializeField] private bool recordSessionData = true;
        [SerializeField] private string sessionExerciseName = "squat";
        [SerializeField] private string sessionDataFolderName = "pose_sessions";
        [SerializeField] private float sessionRingBufferSeconds = 5f;
        [SerializeField] private bool writeDebugLandmarkLog;
        [SerializeField] private float debugLogRetentionHours = 24f;

        private CameraPreviewController cameraPreview;
        private PoseOverlayRenderer overlayRenderer;
        private PoseDebugHud debugHud;
        private MediaPipeQaLogger qaLogger;
        private IPoseEstimator poseEstimator;
        private PoseQualityGate qualityGate;
        private PoseQualityReport qualityReport;
        private PoseExerciseFeedbackAnalyzer exerciseFeedbackAnalyzer;
        private TtsController ttsController;
        private readonly List<PoseExerciseFeedbackMessage> exerciseFeedback = new List<PoseExerciseFeedbackMessage>();
        private readonly PoseFrameRingBuffer poseFrameRingBuffer = new PoseFrameRingBuffer();
        private readonly PoseFeedbackEventRecorder feedbackEventRecorder = new PoseFeedbackEventRecorder();
        private readonly PoseSessionSummaryBuilder sessionSummaryBuilder = new PoseSessionSummaryBuilder();
        private readonly PoseStorageRetentionPolicy storageRetentionPolicy = new PoseStorageRetentionPolicy();
        private PoseSessionStorage sessionStorage;
        private PoseSessionData currentSession;
        private PoseDebugLandmarkLogger debugLandmarkLogger;
        private LandmarkFrame latestFrame;
        private Color32[] pixelBuffer;
        private string status = "Stopped";
        private bool isStartingCamera;
        private float nextPoseSampleAt;
        private float poseFpsWindowStartedAt;
        private int poseFrameCount;
        private float poseFps;
        private float nextQaLogAt;
        private float readyStartedAt = -1f;
        private string lastError;
        private float lastInferenceMs;
        private int successfulFrameCount;
        private int failedFrameCount;
        private int droppedFrameCount;
        private bool isSettingUpPython;
        private string pythonSetupStatus;
        private string sessionStorageStatus;

        private void Awake()
        {
            cameraPreview = GetComponent<CameraPreviewController>();
            overlayRenderer = GetComponent<PoseOverlayRenderer>();
            debugHud = GetComponent<PoseDebugHud>();
            qualityGate = new PoseQualityGate(
                requiredVisibility,
                requiredPresence,
                averageVisibilityThreshold,
                landmarkEdgeMargin);
            qualityReport = qualityGate.Evaluate(false, null);
            exerciseFeedbackAnalyzer = new PoseExerciseFeedbackAnalyzer
            {
                minimumConfidence = exerciseFeedbackMinimumConfidence,
                feedbackCooldownSeconds = exerciseFeedbackCooldownSeconds
            };
            sessionStorage = new PoseSessionStorage(sessionDataFolderName);
            ttsController = FindFirstObjectByType<TtsController>();

            if (cameraPreview == null)
            {
                cameraPreview = gameObject.AddComponent<CameraPreviewController>();
            }

            if (overlayRenderer == null)
            {
                overlayRenderer = gameObject.AddComponent<PoseOverlayRenderer>();
            }

            if (debugHud == null)
            {
                debugHud = gameObject.AddComponent<PoseDebugHud>();
            }
        }

        private void Update()
        {
            cameraPreview.TickFps();

            if (!cameraPreview.IsRunning || !cameraPreview.DidUpdateThisFrame)
            {
                qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
                return;
            }

            if (Time.unscaledTime < nextPoseSampleAt)
            {
                return;
            }

            var interval = 1f / Mathf.Max(1f, targetPoseFps);
            var skippedSamples = Mathf.FloorToInt(Mathf.Max(0f, Time.unscaledTime - nextPoseSampleAt) / interval);
            if (skippedSamples > 0)
            {
                droppedFrameCount += skippedSamples;
            }

            nextPoseSampleAt = Time.unscaledTime + interval;
            ProcessPoseSample();
        }

        private void ProcessPoseSample()
        {
            var startedAt = Time.realtimeSinceStartup;

            if (poseEstimator == null || !poseEstimator.IsReady)
            {
                lastError = poseEstimator == null ? "Pose estimator has not been created." : poseEstimator.LastError;
                latestFrame = LandmarkFrame.Empty(CurrentTimestampMs(), "POSE_BACKEND_NOT_READY", lastError);
                failedFrameCount++;
                exerciseFeedback.Clear();
                qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
                lastInferenceMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
                RecordSessionSample(false);
                MaybeWriteQaLog();
                return;
            }

            pixelBuffer = cameraPreview.GetPixels(pixelBuffer);
            if (pixelBuffer == null || pixelBuffer.Length == 0)
            {
                latestFrame = LandmarkFrame.Empty(CurrentTimestampMs(), "CAMERA_FRAME_EMPTY", "Camera frame is not ready yet.");
                failedFrameCount++;
                exerciseFeedback.Clear();
                qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
                lastInferenceMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
                RecordSessionSample(false);
                MaybeWriteQaLog();
                return;
            }

            var timestampMs = CurrentTimestampMs();
            LandmarkFrame frame;
            var success = poseEstimator.TryProcessFrame(
                pixelBuffer,
                cameraPreview.Width,
                cameraPreview.Height,
                timestampMs,
                cameraPreview.IsDisplayMirrored,
                cameraPreview.RotationAngle,
                out frame);

            latestFrame = frame;
            if (latestFrame != null)
            {
                latestFrame.cameraFps = cameraPreview.CameraFps;
                latestFrame.poseFps = poseFps;
            }

            if (success)
            {
                successfulFrameCount++;
                CountPoseFrame();
                status = "Running";
                lastError = string.Empty;
            }
            else
            {
                failedFrameCount++;
                lastError = poseEstimator.LastError;
                status = string.IsNullOrEmpty(lastError) ? "Running without pose" : "Pose error";
            }

            qualityReport = qualityGate.Evaluate(cameraPreview.IsRunning, latestFrame);
            lastInferenceMs = (Time.realtimeSinceStartup - startedAt) * 1000f;
            RecordSessionSample(success);
            AnalyzeExerciseFeedback(success);
            RecordSessionFeedback();
            UpdateCalibrationStatus(success);
            MaybeWriteQaLog();
        }

        private void OnGUI()
        {
            DrawToolbar();

            var previewRect = CalculatePreviewRect();
            cameraPreview.DrawPreview(previewRect);

            if (latestFrame != null)
            {
                overlayRenderer.DrawOverlay(previewRect, latestFrame, cameraPreview.IsDisplayMirrored);
            }

            var hudHeight = Mathf.Min(540f, Screen.height - 92f);
            var hudRect = new Rect(12f, Screen.height - hudHeight - 12f, Mathf.Min(940f, Screen.width - 24f), hudHeight);
            debugHud.DrawHud(
                hudRect,
                status,
                poseEstimator == null ? string.Empty : poseEstimator.BackendName,
                cameraPreview.ActiveDeviceName,
                latestFrame,
                qualityReport,
                cameraPreview.CameraFps,
                poseFps,
                lastInferenceMs,
                successfulFrameCount,
                failedFrameCount,
                droppedFrameCount,
                exerciseFeedback,
                BuildVisibleError(),
                BuildDiagnosticInfo());
        }

        private void DrawToolbar()
        {
            GUI.Box(new Rect(8f, 8f, Screen.width - 16f, 54f), string.Empty);

            GUI.enabled = !cameraPreview.IsRunning && !isStartingCamera;
            if (GUI.Button(new Rect(18f, 20f, 120f, 30f), "Start Camera"))
            {
                StartCoroutine(StartCameraAndPose());
            }

            GUI.enabled = cameraPreview.IsRunning || isStartingCamera;
            if (GUI.Button(new Rect(148f, 20f, 120f, 30f), "Stop Camera"))
            {
                StopCameraAndPose();
            }

            GUI.enabled = !isStartingCamera;
            if (GUI.Button(new Rect(278f, 20f, 130f, 30f), "Switch Camera"))
            {
                StartCoroutine(SwitchCameraAndRestart());
            }

#if UNITY_EDITOR
            var labelX = 424f;
            if (showEditorPythonSetupButton)
            {
                GUI.enabled = !isSettingUpPython && !cameraPreview.IsRunning && !isStartingCamera;
                if (GUI.Button(new Rect(418f, 20f, 140f, 30f), isSettingUpPython ? "Setting Up..." : "Setup Python"))
                {
                    StartCoroutine(SetupEditorPythonEnvironment());
                }

                labelX = 568f;
            }
#else
            const float labelX = 424f;
#endif

            GUI.enabled = true;
            GUI.Label(new Rect(labelX, 24f, Screen.width - labelX - 16f, 24f), BuildToolbarStatus());
        }

        private IEnumerator StartCameraAndPose()
        {
            isStartingCamera = true;
            status = "Starting camera";
            lastError = string.Empty;

            yield return StartCoroutine(cameraPreview.StartCamera());

            isStartingCamera = false;

            if (!cameraPreview.IsRunning)
            {
                status = "Camera failed";
                lastError = cameraPreview.LastError;
                qualityReport = qualityGate.Evaluate(false, null);
                yield break;
            }

            var poseReady = CreatePoseEstimator();
            nextPoseSampleAt = Time.unscaledTime;
            poseFpsWindowStartedAt = Time.unscaledTime;
            poseFrameCount = 0;
            poseFps = 0f;
            ResetPerformanceCounters();
            if (poseReady)
            {
                BeginPoseSession();
            }

            if (poseReady)
            {
                status = "Running";
            }
        }

        private void StopCameraAndPose()
        {
            StopCameraAndPose(true);
        }

        private void StopCameraAndPose(bool stopCoroutines)
        {
            if (stopCoroutines)
            {
                StopAllCoroutines();
            }

            isStartingCamera = false;
            EndPoseSession();
            cameraPreview.StopCamera();

            if (poseEstimator != null)
            {
                poseEstimator.Dispose();
                poseEstimator = null;
            }

            pixelBuffer = null;
            latestFrame = null;
            exerciseFeedback.Clear();
            poseFps = 0f;
            lastInferenceMs = 0f;
            nextQaLogAt = 0f;
            readyStartedAt = -1f;
            lastError = string.Empty;
            status = "Stopped";
            qualityReport = qualityGate.Evaluate(false, null);
        }

        private IEnumerator SwitchCameraAndRestart()
        {
            var wasRunning = cameraPreview.IsRunning;
            StopCameraAndPose(false);
            cameraPreview.TogglePreferredCameraFacing();

            if (!wasRunning)
            {
                status = cameraPreview.PreferFrontCamera ? "Front camera preferred" : "Rear camera preferred";
                yield break;
            }

            yield return StartCoroutine(StartCameraAndPose());
        }

        private bool CreatePoseEstimator()
        {
            if (poseEstimator != null)
            {
                poseEstimator.Dispose();
            }

            var settings = new PoseEstimatorSettings
            {
                modelPath = Path.Combine(Application.streamingAssetsPath, modelRelativePath),
                numPoses = 1,
                minPoseDetectionConfidence = minPoseDetectionConfidence,
                minPosePresenceConfidence = minPosePresenceConfidence,
                minTrackingConfidence = minTrackingConfidence,
                targetPoseFps = targetPoseFps,
                simulatePoseWhenNativeUnavailable = simulatePoseWhenNativeUnavailable,
                usePythonMediaPipeInEditor = usePythonMediaPipeInEditor,
                editorPythonExecutablePath = editorPythonExecutablePath,
                editorPythonWorkerRelativePath = editorPythonWorkerRelativePath
            };

            poseEstimator = PoseEstimatorFactory.Create(settings);
            if (!poseEstimator.Initialize(settings))
            {
                lastError = poseEstimator.LastError;
                status = "Pose backend failed";
                return false;
            }

            return true;
        }

        private Rect CalculatePreviewRect()
        {
            var top = 74f;
            var bottom = 288f;
            var availableWidth = Mathf.Max(240f, Screen.width - 24f);
            var availableHeight = Mathf.Max(160f, Screen.height - top - bottom);
            var aspect = 4f / 3f;

            if (cameraPreview.Width > 16 && cameraPreview.Height > 16)
            {
                var width = cameraPreview.Width;
                var height = cameraPreview.Height;
                if (cameraPreview.RotationAngle == 90 || cameraPreview.RotationAngle == 270)
                {
                    var temp = width;
                    width = height;
                    height = temp;
                }

                aspect = width / (float)height;
            }

            var rectWidth = availableWidth;
            var rectHeight = rectWidth / aspect;
            if (rectHeight > availableHeight)
            {
                rectHeight = availableHeight;
                rectWidth = rectHeight * aspect;
            }

            return new Rect(
                (Screen.width - rectWidth) * 0.5f,
                top + (availableHeight - rectHeight) * 0.5f,
                rectWidth,
                rectHeight);
        }

        private string BuildVisibleError()
        {
            if (!string.IsNullOrEmpty(lastError))
            {
                return lastError;
            }

            if (!string.IsNullOrEmpty(cameraPreview.LastError))
            {
                return cameraPreview.LastError;
            }

            if (latestFrame != null && !string.IsNullOrEmpty(latestFrame.errorMessage))
            {
                return latestFrame.errorMessage;
            }

            return string.Empty;
        }

        private string BuildDiagnosticInfo()
        {
            var editorPythonEstimator = poseEstimator as EditorPythonMediaPipePoseEstimator;
            return editorPythonEstimator == null ? string.Empty : editorPythonEstimator.DiagnosticInfo;
        }

        private void MaybeWriteQaLog()
        {
            if (!writeQaLog || Time.unscaledTime < nextQaLogAt)
            {
                return;
            }

            nextQaLogAt = Time.unscaledTime + Mathf.Max(0.1f, qaLogIntervalSeconds);

            try
            {
                if (qaLogger == null)
                {
                    qaLogger = new MediaPipeQaLogger(BuildQaLogPath());
                }

                qaLogger.Write(
                    status,
                    poseEstimator == null ? string.Empty : poseEstimator.BackendName,
                    cameraPreview.ActiveDeviceName,
                    latestFrame,
                    qualityReport,
                    lastInferenceMs,
                    successfulFrameCount,
                    failedFrameCount,
                    droppedFrameCount,
                    exerciseFeedback,
                    BuildVisibleError());
            }
            catch (System.Exception exception)
            {
                lastError = "QA log write failed: " + exception.Message;
                writeQaLog = false;
            }
        }

        private void BeginPoseSession()
        {
            EndPoseSession();
            sessionStorageStatus = string.Empty;
            DisposeQaLogger();

            if (!recordSessionData)
            {
                poseFrameRingBuffer.Configure(sessionRingBufferSeconds, targetPoseFps);
                poseFrameRingBuffer.Clear();
                return;
            }

            sessionStorage = new PoseSessionStorage(sessionDataFolderName);
            var deletedDebugLogs = storageRetentionPolicy.DeleteExpiredDebugLogs(
                sessionStorage.DebugDirectory,
                debugLogRetentionHours);

            var startedAtUtc = DateTime.UtcNow;
            currentSession = new PoseSessionData
            {
                sessionId = startedAtUtc.ToString("yyyyMMdd_HHmmss_fff"),
                exercise = string.IsNullOrWhiteSpace(sessionExerciseName) ? "unknown" : sessionExerciseName.Trim(),
                startedAtUtc = startedAtUtc.ToString("o"),
                platform = Application.platform.ToString(),
                backend = poseEstimator == null ? string.Empty : poseEstimator.BackendName,
                cameraDevice = cameraPreview == null ? string.Empty : cameraPreview.ActiveDeviceName,
                storagePolicy = "Option C: summary + feedback events, no raw video, no full landmark persistence",
                startedAtRealtimeSeconds = Time.realtimeSinceStartup
            };

            poseFrameRingBuffer.Configure(sessionRingBufferSeconds, targetPoseFps);
            feedbackEventRecorder.Begin(currentSession);
            sessionSummaryBuilder.Begin(currentSession);

            if (writeDebugLandmarkLog)
            {
                debugLandmarkLogger = new PoseDebugLandmarkLogger(
                    sessionStorage.CreateDebugLandmarkLogPath(currentSession.sessionId));
            }

            sessionStorageStatus = deletedDebugLogs > 0
                ? $"Session recording: {currentSession.sessionId} / deleted debug logs: {deletedDebugLogs}"
                : $"Session recording: {currentSession.sessionId}";
            Debug.Log("[Pose Session] Started " + currentSession.sessionId);
        }

        private void EndPoseSession()
        {
            debugLandmarkLogger?.Dispose();
            debugLandmarkLogger = null;
            DisposeQaLogger();

            if (currentSession == null)
            {
                poseFrameRingBuffer.Clear();
                feedbackEventRecorder.Clear();
                sessionSummaryBuilder.Clear();
                return;
            }

            var summary = sessionSummaryBuilder.Build(
                successfulFrameCount,
                failedFrameCount,
                droppedFrameCount,
                feedbackEventRecorder.Events,
                poseFrameRingBuffer);

            if (recordSessionData && sessionStorage != null && summary != null)
            {
                var result = sessionStorage.SaveSession(summary, feedbackEventRecorder.Events);
                if (result.success)
                {
                    sessionStorageStatus = "Session saved: " + Path.GetFileName(result.summaryPath);
                    Debug.Log(
                        "[Pose Session] Saved " + currentSession.sessionId
                        + "\nSummary: " + result.summaryPath
                        + "\nEvents: " + result.eventsPath);
                }
                else
                {
                    sessionStorageStatus = "Session save failed. See Console.";
                    Debug.LogError("[Pose Session] Save failed: " + result.error);
                }
            }

            currentSession = null;
            poseFrameRingBuffer.Clear();
            feedbackEventRecorder.Clear();
            sessionSummaryBuilder.Clear();
        }

        private void RecordSessionSample(bool poseSuccess)
        {
            if (currentSession == null)
            {
                return;
            }

            sessionSummaryBuilder.RecordFrame(latestFrame, qualityReport, lastInferenceMs);

            if (poseSuccess && latestFrame != null && latestFrame.HasPose)
            {
                poseFrameRingBuffer.Add(latestFrame, qualityReport, lastInferenceMs);
            }

            if (writeDebugLandmarkLog)
            {
                debugLandmarkLogger?.Write(latestFrame);
            }
        }

        private void RecordSessionFeedback()
        {
            if (currentSession == null || exerciseFeedback.Count == 0)
            {
                return;
            }

            feedbackEventRecorder.Record(latestFrame, qualityReport, exerciseFeedback);
        }

        private string BuildQaLogPath()
        {
            if (currentSession != null && sessionStorage != null)
            {
                return sessionStorage.CreateDebugQaLogPath(currentSession.sessionId);
            }

            return qaLogFileName;
        }

        private void DisposeQaLogger()
        {
            qaLogger?.Dispose();
            qaLogger = null;
        }

        private void AnalyzeExerciseFeedback(bool poseSuccess)
        {
            exerciseFeedback.Clear();

            if (!analyzeExerciseFeedback || !poseSuccess || latestFrame == null || !latestFrame.HasPose)
            {
                return;
            }

            exerciseFeedbackAnalyzer.minimumConfidence = exerciseFeedbackMinimumConfidence;
            exerciseFeedbackAnalyzer.feedbackCooldownSeconds = exerciseFeedbackCooldownSeconds;
            exerciseFeedbackAnalyzer.Analyze(latestFrame, exerciseFeedback);

            if (!speakExerciseFeedback || exerciseFeedback.Count == 0)
            {
                return;
            }

            ttsController ??= FindFirstObjectByType<TtsController>();
            if (ttsController == null)
            {
                return;
            }

            for (var i = 0; i < exerciseFeedback.Count; i++)
            {
                if (exerciseFeedback[i].confidence < exerciseFeedbackMinimumConfidence)
                {
                    continue;
                }

                ttsController.TrySpeak(exerciseFeedback[i].text, out _);
                break;
            }
        }

        private void UpdateCalibrationStatus(bool poseSuccess)
        {
            if (!poseSuccess || qualityReport == null || !qualityReport.IsReady)
            {
                readyStartedAt = -1f;
                return;
            }

            if (readyStartedAt < 0f)
            {
                readyStartedAt = Time.unscaledTime;
            }

            var requiredSeconds = Mathf.Max(0f, calibrationSeconds);
            var elapsed = Time.unscaledTime - readyStartedAt;
            if (elapsed < requiredSeconds)
            {
                status = $"Calibrating {elapsed:0.0}/{requiredSeconds:0.0}s";
                return;
            }

            status = "Ready";
        }

        private string BuildToolbarStatus()
        {
            if (!string.IsNullOrWhiteSpace(pythonSetupStatus))
            {
                return pythonSetupStatus;
            }

            var facing = cameraPreview.ActiveCameraIsFrontFacing ? "front" : "rear/unknown";
            var preferred = cameraPreview.PreferFrontCamera ? "front" : "rear";
            if (!cameraPreview.IsRunning)
            {
                return AppendSessionStorageStatus($"{status} / preferred camera: {preferred}");
            }

            return AppendSessionStorageStatus($"{status} / active camera: {facing} / preferred: {preferred}");
        }

        private string AppendSessionStorageStatus(string value)
        {
            return string.IsNullOrWhiteSpace(sessionStorageStatus)
                ? value
                : value + " / " + sessionStorageStatus;
        }

#if UNITY_EDITOR
        private IEnumerator SetupEditorPythonEnvironment()
        {
            isSettingUpPython = true;
            pythonSetupStatus = "Setting up .venv-mediapipe...";
            lastError = string.Empty;

            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var configuredPython = editorPythonExecutablePath;
            var setupTask = Task.Run(() => RunEditorPythonSetup(projectRoot, configuredPython));

            while (!setupTask.IsCompleted)
            {
                yield return null;
            }

            isSettingUpPython = false;

            if (setupTask.Exception != null)
            {
                var message = setupTask.Exception.GetBaseException().Message;
                pythonSetupStatus = "Python setup failed. See Console.";
                lastError = message;
                Debug.LogError("[Editor Python MediaPipe Setup] " + message);
                yield break;
            }

            var result = setupTask.Result;
            if (result.success)
            {
                editorPythonExecutablePath = result.venvPythonPath;
                pythonSetupStatus = "Python setup complete. Start Camera again.";
                lastError = string.Empty;
                Debug.Log("[Editor Python MediaPipe Setup] " + result.message);
            }
            else
            {
                pythonSetupStatus = "Python setup failed. See Console.";
                lastError = result.message;
                Debug.LogError("[Editor Python MediaPipe Setup] " + result.message);
            }
        }

        private static PythonSetupResult RunEditorPythonSetup(string projectRoot, string configuredPython)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Directory.Exists(projectRoot))
            {
                return PythonSetupResult.Fail("Project root could not be resolved.");
            }

            var venvDirectory = Path.Combine(projectRoot, ".venv-mediapipe");
            var venvPython = GetVenvPythonPath(venvDirectory);
            var log = new StringBuilder();

            var bootstrapPython = File.Exists(venvPython)
                ? venvPython
                : ResolveBootstrapPython(projectRoot, configuredPython);
            if (string.IsNullOrWhiteSpace(bootstrapPython))
            {
                return PythonSetupResult.Fail("Python executable was not found. Install Python 3 first, then retry.");
            }

            log.Append("projectRoot: ").AppendLine(projectRoot);
            log.Append("venvDirectory: ").AppendLine(venvDirectory);
            log.Append("bootstrapPython: ").AppendLine(bootstrapPython);

            if (!File.Exists(venvPython))
            {
                var createVenv = RunProcess(bootstrapPython, "-m venv " + QuoteProcessArgument(venvDirectory), projectRoot, 120000);
                log.AppendLine(createVenv.ToLog("create venv"));
                if (createVenv.exitCode != 0 || !File.Exists(venvPython))
                {
                    return PythonSetupResult.Fail("Failed to create .venv-mediapipe.\n" + log);
                }
            }

            var ensurePip = RunProcess(venvPython, "-m ensurepip --upgrade", projectRoot, 120000);
            log.AppendLine(ensurePip.ToLog("ensurepip"));

            var upgradePip = RunProcess(venvPython, "-m pip install --upgrade pip setuptools wheel", projectRoot, 300000);
            log.AppendLine(upgradePip.ToLog("upgrade pip"));
            if (upgradePip.exitCode != 0)
            {
                return PythonSetupResult.Fail("Failed to upgrade pip.\n" + log);
            }

            var installPackages = RunProcess(venvPython, "-m pip install mediapipe numpy", projectRoot, 600000);
            log.AppendLine(installPackages.ToLog("install mediapipe numpy"));
            if (installPackages.exitCode != 0)
            {
                return PythonSetupResult.Fail("Failed to install mediapipe/numpy.\n" + log);
            }

            var verify = RunProcess(
                venvPython,
                "-c \"import sys, numpy, mediapipe; print(sys.executable); print('numpy', numpy.__version__); print('mediapipe', mediapipe.__version__)\"",
                projectRoot,
                120000);
            log.AppendLine(verify.ToLog("verify imports"));
            if (verify.exitCode != 0)
            {
                return PythonSetupResult.Fail("mediapipe/numpy install verification failed.\n" + log);
            }

            return PythonSetupResult.Ok(venvPython, log.ToString());
        }

        private static string ResolveBootstrapPython(string projectRoot, string configuredPython)
        {
            if (!string.IsNullOrWhiteSpace(configuredPython) && File.Exists(configuredPython.Trim()))
            {
                return configuredPython.Trim();
            }

            var existingVenv = GetVenvPythonPath(Path.Combine(projectRoot, ".venv-mediapipe"));
            if (File.Exists(existingVenv))
            {
                return existingVenv;
            }

#if UNITY_EDITOR_OSX
            var candidates = new[]
            {
                "/opt/homebrew/bin/python3.12",
                "/opt/homebrew/bin/python3.11",
                "/opt/homebrew/bin/python3.10",
                "/opt/homebrew/bin/python3",
                "/usr/local/bin/python3.12",
                "/usr/local/bin/python3.11",
                "/usr/local/bin/python3.10",
                "/usr/local/bin/python3",
                "/usr/bin/python3"
            };
#elif UNITY_EDITOR_WIN
            var candidates = new[] { "python" };
#else
            var candidates = new[] { "/usr/bin/python3", "python3", "python" };
#endif

            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (Path.IsPathRooted(candidate))
                {
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                else
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        private static string GetVenvPythonPath(string venvDirectory)
        {
#if UNITY_EDITOR_WIN
            return Path.Combine(venvDirectory, "Scripts", "python.exe");
#else
            return Path.Combine(venvDirectory, "bin", "python");
#endif
        }

        private static ProcessResult RunProcess(string executable, string arguments, string workingDirectory, int timeoutMs)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var output = new StringBuilder();
            var error = new StringBuilder();
            process.OutputDataReceived += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    output.AppendLine(args.Data);
                }
            };
            process.ErrorDataReceived += (_, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    error.AppendLine(args.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (!process.WaitForExit(timeoutMs))
            {
                try
                {
                    process.Kill();
                }
                catch
                {
                    // Ignore kill failures during setup diagnostics.
                }

                return new ProcessResult(-1, output.ToString(), "Timed out after " + timeoutMs + "ms.\n" + error);
            }

            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
        }

        private static string QuoteProcessArgument(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private readonly struct ProcessResult
        {
            public readonly int exitCode;
            public readonly string stdout;
            public readonly string stderr;

            public ProcessResult(int exitCode, string stdout, string stderr)
            {
                this.exitCode = exitCode;
                this.stdout = stdout ?? string.Empty;
                this.stderr = stderr ?? string.Empty;
            }

            public string ToLog(string title)
            {
                var builder = new StringBuilder();
                builder.Append("== ").Append(title).Append(" ==").AppendLine();
                builder.Append("exitCode: ").Append(exitCode).AppendLine();
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    builder.AppendLine("stdout:");
                    builder.AppendLine(stdout.TrimEnd());
                }

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    builder.AppendLine("stderr:");
                    builder.AppendLine(stderr.TrimEnd());
                }

                return builder.ToString();
            }
        }

        private readonly struct PythonSetupResult
        {
            public readonly bool success;
            public readonly string venvPythonPath;
            public readonly string message;

            private PythonSetupResult(bool success, string venvPythonPath, string message)
            {
                this.success = success;
                this.venvPythonPath = venvPythonPath;
                this.message = message ?? string.Empty;
            }

            public static PythonSetupResult Ok(string venvPythonPath, string message)
            {
                return new PythonSetupResult(true, venvPythonPath, message);
            }

            public static PythonSetupResult Fail(string message)
            {
                return new PythonSetupResult(false, string.Empty, message);
            }
        }
#endif

        private void CountPoseFrame()
        {
            poseFrameCount++;
            var elapsed = Time.unscaledTime - poseFpsWindowStartedAt;
            if (elapsed < 1f)
            {
                return;
            }

            poseFps = poseFrameCount / elapsed;
            poseFrameCount = 0;
            poseFpsWindowStartedAt = Time.unscaledTime;
        }

        private void ResetPerformanceCounters()
        {
            successfulFrameCount = 0;
            failedFrameCount = 0;
            droppedFrameCount = 0;
            lastInferenceMs = 0f;
            exerciseFeedback.Clear();
        }

        private static long CurrentTimestampMs()
        {
            return (long)(Time.realtimeSinceStartup * 1000f);
        }

        private void OnDestroy()
        {
            StopCameraAndPose();
            DisposeQaLogger();
        }
    }
}
