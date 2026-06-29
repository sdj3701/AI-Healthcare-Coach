#!/usr/bin/env python3
import argparse
import json
import sys
import traceback


LANDMARK_NAMES = [
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
    "right_foot_index",
]


def write_json(payload):
    sys.stdout.write(json.dumps(payload, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def read_exact(stream, length):
    chunks = []
    remaining = length
    while remaining > 0:
        chunk = stream.read(remaining)
        if not chunk:
            return None
        chunks.append(chunk)
        remaining -= len(chunk)
    return b"".join(chunks)


def empty_frame(timestamp_ms, code, message):
    return {
        "timestampMs": timestamp_ms,
        "cameraMode": "editor_python_mediapipe",
        "sourceWidth": 0,
        "sourceHeight": 0,
        "mirrored": False,
        "rotationAngle": 0,
        "landmarks": [],
        "worldLandmarks": [],
        "errorCode": code,
        "errorMessage": message,
    }


def landmark_payload(landmarks):
    if landmarks is None:
        return []

    payload = []
    for index, landmark in enumerate(landmarks):
        visibility = float(getattr(landmark, "visibility", 1.0))
        presence = float(getattr(landmark, "presence", visibility))
        payload.append(
            {
                "id": index,
                "name": LANDMARK_NAMES[index] if index < len(LANDMARK_NAMES) else "unknown",
                "x": float(landmark.x),
                "y": float(landmark.y),
                "z": float(landmark.z),
                "visibility": visibility,
                "presence": presence,
            }
        )
    return payload


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--min_detection_confidence", type=float, default=0.5)
    parser.add_argument("--min_tracking_confidence", type=float, default=0.5)
    parser.add_argument("--min_presence_confidence", type=float, default=0.5)
    return parser.parse_args()


def main():
    args = parse_args()

    try:
        import numpy as np
        import mediapipe as mp
    except Exception as exc:
        write_json(
            {
                "ready": False,
                "errorCode": "PYTHON_IMPORT_FAILED",
                "errorMessage": (
                    "Python MediaPipe import failed. Install dependencies with "
                    "`python3 -m pip install mediapipe numpy`. Details: " + str(exc)
                ),
            }
        )
        return 2

    try:
        pose = mp.solutions.pose.Pose(
            static_image_mode=False,
            model_complexity=1,
            smooth_landmarks=True,
            enable_segmentation=False,
            min_detection_confidence=args.min_detection_confidence,
            min_tracking_confidence=args.min_tracking_confidence,
        )
    except Exception as exc:
        write_json(
            {
                "ready": False,
                "errorCode": "MEDIAPIPE_POSE_INIT_FAILED",
                "errorMessage": "Failed to initialize MediaPipe Pose: " + str(exc),
            }
        )
        return 3

    write_json({"ready": True, "errorCode": "", "errorMessage": ""})

    input_stream = sys.stdin.buffer
    while True:
        header_line = input_stream.readline()
        if not header_line:
            break

        try:
            header = json.loads(header_line.decode("utf-8"))
            width = int(header["width"])
            height = int(header["height"])
            timestamp_ms = int(header.get("timestampMs", 0))
            mirrored = bool(header.get("mirrored", False))
            rotation_angle = int(header.get("rotationAngle", 0))
            byte_count = width * height * 4
            raw = read_exact(input_stream, byte_count)
            if raw is None:
                break

            frame = np.frombuffer(raw, dtype=np.uint8).reshape((height, width, 4))
            rgb = np.ascontiguousarray(frame[:, :, :3])
            rgb.flags.writeable = False
            result = pose.process(rgb)

            if not result.pose_landmarks:
                write_json(
                    empty_frame(
                        timestamp_ms,
                        "NO_POSE",
                        "No pose landmarks detected in the current frame.",
                    )
                )
                continue

            write_json(
                {
                    "timestampMs": timestamp_ms,
                    "cameraMode": "editor_python_mediapipe",
                    "sourceWidth": width,
                    "sourceHeight": height,
                    "mirrored": mirrored,
                    "rotationAngle": rotation_angle,
                    "landmarks": landmark_payload(result.pose_landmarks.landmark),
                    "worldLandmarks": landmark_payload(
                        result.pose_world_landmarks.landmark
                        if result.pose_world_landmarks
                        else None
                    ),
                    "errorCode": "",
                    "errorMessage": "",
                }
            )
        except Exception as exc:
            write_json(
                empty_frame(
                    0,
                    "PYTHON_WORKER_FRAME_FAILED",
                    str(exc) + "\n" + traceback.format_exc(limit=2),
                )
            )

    pose.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
