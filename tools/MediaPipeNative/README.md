# MediaPipe native build artifacts

This directory contains the iOS `MediaPipeUnity.framework` binary extracted from the official
MediaPipeUnityPlugin v0.16.3 tarball:

https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3

The Git package referenced by `Packages/manifest.json` contains the framework metadata but does
not contain the native framework files. `MediaPipeIOSNativeLibraryPreprocessor` validates this
payload by SHA-256 and restores the missing files to Unity's resolved package cache before an iOS
build. Keep the package version, payload version, and checksums in sync when upgrading MediaPipe.

The framework binary is tracked with Git LFS. License and third-party notices are stored alongside
the versioned payload.
