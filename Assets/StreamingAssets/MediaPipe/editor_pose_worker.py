#!/usr/bin/env python3
import argparse
import importlib.util
import json
import os
import platform
import subprocess
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


def get_pip_version():
    try:
        result = subprocess.run(
            [sys.executable, "-m", "pip", "--version"],
            capture_output=True,
            text=True,
            timeout=5,
        )
        output = (result.stdout or result.stderr or "").strip()
        if output:
            return output
        return "pip returned no output, exit_code=" + str(result.returncode)
    except Exception as exc:
        return "pip check failed: " + str(exc)


def get_package_diagnostic(package_name):
    lines = []
    try:
        spec = importlib.util.find_spec(package_name)
        if spec is None:
            lines.append(package_name + ": not found by importlib")
        else:
            lines.append(package_name + " spec: " + str(spec.origin))
    except Exception as exc:
        lines.append(package_name + " spec check failed: " + str(exc))

    try:
        module = __import__(package_name)
        version = str(getattr(module, "__version__", "unknown"))
        location = str(getattr(module, "__file__", "unknown"))
        lines.append(package_name + " import: OK")
        lines.append(package_name + " version: " + version)
        lines.append(package_name + " file: " + location)
    except Exception as exc:
        lines.append(package_name + " import: FAILED")
        lines.append(package_name + " error: " + repr(exc))

    return "\n".join(lines)


def build_python_diagnostics():
    path_head = os.environ.get("PATH", "").split(os.pathsep)[:6]
    python_path_head = sys.path[:8]
    venv_value = os.environ.get("VIRTUAL_ENV", "")
    in_venv = sys.prefix != getattr(sys, "base_prefix", sys.prefix)

    lines = [
        "Python diagnostics",
        "executable: " + sys.executable,
        "version: " + sys.version.replace("\n", " "),
        "prefix: " + sys.prefix,
        "base_prefix: " + getattr(sys, "base_prefix", ""),
        "in_venv: " + str(in_venv),
        "VIRTUAL_ENV: " + (venv_value if venv_value else "-"),
        "cwd: " + os.getcwd(),
        "platform: " + platform.platform(),
        "pip: " + get_pip_version(),
        "PATH head: " + " | ".join(path_head),
        "sys.path head: " + " | ".join(python_path_head),
        get_package_diagnostic("numpy"),
        get_package_diagnostic("mediapipe"),
    ]
    return "\n".join(lines)


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


def map_normalized_point_to_display(x, y, transform_name):
    # Unity WebCamTexture.GetPixels32 arrives bottom-to-top. The display space used by
    # the IMGUI overlay is top-to-bottom, so return landmarks in preview-display space.
    if transform_name == "identity":
        return x, 1.0 - y
    if transform_name == "flip_vertical":
        return x, y
    if transform_name == "flip_horizontal":
        return 1.0 - x, 1.0 - y
    if transform_name == "rotate_180":
        return 1.0 - x, y
    if transform_name == "rotate_ccw":
        return 1.0 - y, 1.0 - x
    if transform_name == "rotate_cw":
        return y, x
    return x, 1.0 - y


def normalized_landmark_payload(landmarks, transform_name):
    if landmarks is None:
        return []

    payload = []
    for index, landmark in enumerate(landmarks):
        x, y = map_normalized_point_to_display(float(landmark.x), float(landmark.y), transform_name)
        raw_visibility = float(getattr(landmark, "visibility", 0.0))
        raw_presence = float(getattr(landmark, "presence", 0.0))
        visibility = raw_visibility if raw_visibility > 0.0 else raw_presence
        presence = raw_presence if raw_presence > 0.0 else visibility
        if visibility <= 0.0 and presence <= 0.0:
            visibility = 1.0
            presence = 1.0
        payload.append(
            {
                "id": index,
                "name": LANDMARK_NAMES[index] if index < len(LANDMARK_NAMES) else "unknown",
                "x": max(0.0, min(1.0, x)),
                "y": max(0.0, min(1.0, y)),
                "z": float(landmark.z),
                "visibility": visibility,
                "presence": presence,
            }
        )
    return payload


def world_landmark_payload(landmarks):
    if landmarks is None:
        return []

    payload = []
    for index, landmark in enumerate(landmarks):
        raw_visibility = float(getattr(landmark, "visibility", 0.0))
        raw_presence = float(getattr(landmark, "presence", 0.0))
        visibility = raw_visibility if raw_visibility > 0.0 else raw_presence
        presence = raw_presence if raw_presence > 0.0 else visibility
        if visibility <= 0.0 and presence <= 0.0:
            visibility = 1.0
            presence = 1.0
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


def image_candidates(rgb, preferred_transform):
    transforms = {
        "identity": lambda image: image,
        "flip_vertical": lambda image: __import__("numpy").flipud(image),
        "flip_horizontal": lambda image: __import__("numpy").fliplr(image),
        "rotate_180": lambda image: __import__("numpy").rot90(image, 2),
        "rotate_ccw": lambda image: __import__("numpy").rot90(image, 1),
        "rotate_cw": lambda image: __import__("numpy").rot90(image, 3),
    }
    order = [
        preferred_transform,
        "identity",
        "flip_vertical",
        "flip_horizontal",
        "rotate_180",
        "rotate_ccw",
        "rotate_cw",
    ]
    seen = set()
    for name in order:
        if not name or name in seen or name not in transforms:
            continue
        seen.add(name)
        yield name, transforms[name](rgb)


def parse_args():
    parser = argparse.ArgumentParser()
    parser.add_argument("--min_detection_confidence", type=float, default=0.5)
    parser.add_argument("--min_tracking_confidence", type=float, default=0.5)
    parser.add_argument("--min_presence_confidence", type=float, default=0.5)
    return parser.parse_args()


def main():
    args = parse_args()
    diagnostics = build_python_diagnostics()

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
                    "`" + sys.executable + " -m pip install mediapipe numpy`. Details: " + str(exc)
                ),
                "pythonDiagnostics": diagnostics,
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
                "pythonDiagnostics": diagnostics,
            }
        )
        return 3

    write_json(
        {
            "ready": True,
            "errorCode": "",
            "errorMessage": "",
            "pythonDiagnostics": diagnostics,
        }
    )

    input_stream = sys.stdin.buffer
    preferred_transform = "identity"
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
            base_rgb = frame[:, :, :3]

            result = None
            used_transform = ""
            for transform_name, candidate in image_candidates(base_rgb, preferred_transform):
                rgb = np.ascontiguousarray(candidate)
                rgb.flags.writeable = False
                candidate_result = pose.process(rgb)
                if candidate_result.pose_landmarks:
                    result = candidate_result
                    used_transform = transform_name
                    preferred_transform = transform_name
                    break

            if result is None or not result.pose_landmarks:
                write_json(
                    empty_frame(
                        timestamp_ms,
                        "NO_POSE",
                        "No pose landmarks detected in the current frame after trying image orientations.",
                    )
                )
                continue

            write_json(
                {
                    "timestampMs": timestamp_ms,
                    "cameraMode": "editor_python_mediapipe/" + used_transform,
                    "sourceWidth": width,
                    "sourceHeight": height,
                    "mirrored": mirrored,
                    "rotationAngle": rotation_angle,
                    "landmarks": normalized_landmark_payload(
                        result.pose_landmarks.landmark,
                        used_transform,
                    ),
                    "worldLandmarks": world_landmark_payload(
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
