package com.aihealthcarecoach.tts;

import android.app.Activity;
import android.os.Build;
import android.speech.tts.TextToSpeech;

import com.unity3d.player.UnityPlayer;

import java.util.HashMap;
import java.util.Locale;

public final class AhcTtsBridge implements TextToSpeech.OnInitListener {
    private static AhcTtsBridge instance;

    private final Activity activity;
    private TextToSpeech synthesizer;
    private boolean ready;
    private String pendingText = "";
    private String lastError = "";

    private AhcTtsBridge(Activity activity) {
        this.activity = activity;
    }

    public static synchronized void initialize() {
        if (instance != null) {
            return;
        }

        final Activity currentActivity = UnityPlayer.currentActivity;
        if (currentActivity == null) {
            return;
        }

        instance = new AhcTtsBridge(currentActivity);
        currentActivity.runOnUiThread(() -> {
            synchronized (AhcTtsBridge.class) {
                if (instance != null && instance.synthesizer == null) {
                    instance.synthesizer = new TextToSpeech(
                        currentActivity.getApplicationContext(), instance);
                }
            }
        });
    }

    public static synchronized boolean speak(String text) {
        initialize();
        if (instance == null) {
            return false;
        }

        return instance.speakInternal(text);
    }

    public static synchronized boolean isSpeaking() {
        return instance != null && instance.synthesizer != null && instance.synthesizer.isSpeaking();
    }

    public static synchronized void stop() {
        if (instance != null && instance.synthesizer != null) {
            instance.synthesizer.stop();
            instance.pendingText = "";
        }
    }

    public static synchronized String getLastError() {
        if (instance == null || instance.lastError.isEmpty()) {
            return "Android TTS를 초기화하지 못했습니다.";
        }

        return instance.lastError;
    }

    public static synchronized void shutdown() {
        if (instance == null) {
            return;
        }

        final Activity currentActivity = instance.activity;
        final TextToSpeech current = instance.synthesizer;
        instance = null;
        if (current != null && currentActivity != null) {
            currentActivity.runOnUiThread(() -> {
                current.stop();
                current.shutdown();
            });
        }
    }

    @Override
    public synchronized void onInit(int status) {
        if (status != TextToSpeech.SUCCESS || synthesizer == null) {
            lastError = "Android TTS 엔진 초기화에 실패했습니다. status=" + status;
            ready = false;
            return;
        }

        int languageResult = synthesizer.setLanguage(Locale.KOREA);
        if (languageResult == TextToSpeech.LANG_MISSING_DATA ||
            languageResult == TextToSpeech.LANG_NOT_SUPPORTED) {
            lastError = "ko-KR 음성이 없어 시스템 기본 음성을 사용합니다.";
        } else {
            lastError = "";
        }

        ready = true;
        if (!pendingText.isEmpty()) {
            String text = pendingText;
            pendingText = "";
            speakNow(text);
        }
    }

    private synchronized boolean speakInternal(String text) {
        if (text == null || text.trim().isEmpty()) {
            lastError = "읽을 문장이 비어 있습니다.";
            return false;
        }

        if (!ready || synthesizer == null) {
            pendingText = text.trim();
            return true;
        }

        return speakNow(text.trim());
    }

    @SuppressWarnings("deprecation")
    private boolean speakNow(String text) {
        int result;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            result = synthesizer.speak(
                text, TextToSpeech.QUEUE_FLUSH, null, "ahc-tts-" + System.nanoTime());
        } else {
            result = synthesizer.speak(
                text, TextToSpeech.QUEUE_FLUSH, new HashMap<String, String>());
        }

        if (result == TextToSpeech.ERROR) {
            lastError = "Android TTS 엔진이 재생 요청을 거부했습니다.";
            return false;
        }

        return true;
    }
}
