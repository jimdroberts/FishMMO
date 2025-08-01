# FishMMO Updater

## Overview

The FishMMO Updater is a robust patching utility designed to update the FishMMO game client by applying versioned patch files. It ensures that the client is brought up-to-date with the latest version by processing patch manifests, handling new, modified, and deleted files, and providing transactional safety with rollback on failure. The updater is intended to be launched by the FishMMO launcher and can also restart the client executable after patching.

## Features

- **Transactional patching:** Applies patches atomically with backup and rollback on failure.
- **Parallel file operations:** New and modified files are processed in parallel for speed.
- **Robust file handling:** Retries, error handling, and detailed logging for file operations.
- **Launcher process management:** Gracefully closes or forcefully kills the launcher before patching.
- **Automatic client restart:** Optionally starts the client executable after patching.

## How It Works

1. **Argument Parsing:** Reads command-line arguments for current version, latest version, launcher PID, and executable to start.
2. **Launcher Shutdown:** Attempts to close or kill the launcher process before patching.
3. **Patch Application:**
   - Loads the patch manifest from a ZIP file in the `Patches` directory.
   - Pre-creates required directories.
   - Adds new files and applies binary patches to modified files (with hash verification).
   - Deletes files marked for removal.
   - Moves patched files into place, with backup and rollback if needed.
4. **Rollback:** If any critical step fails, restores original files from backups.
5. **Cleanup:** Removes temporary files and backups.
6. **Restart:** Optionally starts the client executable and exits.

## Command-Line Arguments

- `-version=<currentVersion>`: The current version of the client.
- `-latestversion=<latestVersion>`: The version to update to.
- `-pid=<launcherPID>`: The process ID of the launcher to close/kill before patching.
- `-exe=<executablePath>`: The relative path or name of the client executable to start after patching.

## Patch File Structure

Patch files are ZIP archives located in the `Patches` directory, named as `<oldVersion>-<newVersion>.zip`. Each patch contains:
- `manifest.json`: Describes new, modified, and deleted files, with hashes and patch data.
- File data and binary patch data for new/modified files.

## Configuration Options

- **MaxFileOperationRetries:** Number of times to retry file operations (default: 5).
- **FileOperationRetryDelayMs:** Delay (ms) between file operation retries (default: 200).
- **PatchesDirectory:** Directory where patch ZIP files are stored (default: `Patches` under the working directory).

## Logging

The updater logs all actions, warnings, and errors to the console, including detailed information about file operations, patch progress, and rollback actions.

## Requirements

- .NET 8.0 or later
- Patch files generated in the expected format (with manifest and data entries)

## Usage Example

```
Updater.exe -version=1.0.0 -latestversion=1.1.0 -pid=1234 -exe=FishMMOClient.exe
```

This will update the client from version 1.0.0 to 1.1.0, close the launcher with PID 1234, and start `FishMMOClient.exe` after patching.

---

For more details, see the code in `Program.cs` and the `Patch/` directory for patching logic.