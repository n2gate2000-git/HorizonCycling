---
name: build-and-package
description: >-
  Build, publish, and package HorizonCycling after any code, UI asset (HTML/CSS/JS),
  or configuration modifications. Use this skill whenever modifications are completed
  to verify tests, compile Release binaries, and produce the distribution ZIP package.
---

# HorizonCycling Build & Packaging Runbook

This skill defines the standard procedure for building, verifying, and packaging HorizonCycling for distribution.
Always run this procedure after completing any bug fixes, feature implementations, or UI asset modifications to ensure that the Release binaries and ZIP package in `release/` are fully synchronized with the repository.

---

## 1. Quick Automated Execution (Recommended)

Run the automated packaging script via PowerShell from the workspace root:

```powershell
powershell -ExecutionPolicy Bypass -File .agents/skills/build-and-package/scripts/package.ps1
```

### Options:
- **Skip Unit Tests** (e.g. for rapid UI-only asset verification):
  ```powershell
  powershell -ExecutionPolicy Bypass -File .agents/skills/build-and-package/scripts/package.ps1 -SkipTests
  ```
- **Custom Output Directory / ZIP**:
  ```powershell
  powershell -ExecutionPolicy Bypass -File .agents/skills/build-and-package/scripts/package.ps1 -OutputDir "release/HorizonCycling_V1.1.0" -ZipPath "release/HorizonCycling_V1.1.0.zip"
  ```

---

## 2. Manual Step-by-Step Execution

If running individual steps manually, follow this exact sequence:

### Step 1: Run Unit Tests
Ensure all automated tests pass before compiling distribution artifacts:
```powershell
dotnet test tests/HorizonCycling.Tests/HorizonCycling.Tests.csproj -c Release
```

### Step 2: Release Build
Compile the solution in Release mode to ensure no compilation errors or warnings:
```powershell
dotnet build src/HorizonCyclingApp/HorizonCyclingApp.csproj -c Release
```

### Step 3: Publish Self-Contained Release
Publish the application as a self-contained Windows x64 distribution package into `release/HorizonCycling_V1.1.0`:
```powershell
dotnet publish src/HorizonCyclingApp/HorizonCyclingApp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o release/HorizonCycling_V1.1.0
```

### Step 4: Synchronize Required Assets
Ensure critical runtime assets are present in the output folder:
- **`vJoyInterface.dll`**: Copy `src/HorizonCyclingBridge/vJoyInterface.dll` to `release/HorizonCycling_V1.1.0/`
- **`map/`**: Copy files from `map/` into `release/HorizonCycling_V1.1.0/map/` (e.g. `fh6_japan_map_full_8192x8192.jpg`, `fh6_japan_pins.json`)
- **`wwwroot/`**: Verified to contain latest frontend assets (`index.html`, `js/`, `css/`) automatically deployed by publish.

### Step 5: Generate Distribution ZIP
Compress the release folder into the release ZIP archive:
```powershell
Compress-Archive -Path "release/HorizonCycling_V1.1.0/*" -DestinationPath "release/HorizonCycling_V1.1.0.zip" -Force
```

---

## 3. Verification Checklist

After running the build and packaging process, verify the following:
1. `HorizonCyclingApp.exe` exists in `release/HorizonCycling_V1.1.0/` with current timestamp.
2. `vJoyInterface.dll` exists in `release/HorizonCycling_V1.1.0/`.
3. `release/HorizonCycling_V1.1.0/wwwroot/` contains updated web files.
4. `release/HorizonCycling_V1.1.0.zip` exists, is non-empty (>100MB), and matches the latest build timestamp.
