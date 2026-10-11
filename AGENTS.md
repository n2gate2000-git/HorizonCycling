# HorizonCycling Development Guidelines & Agent Rules

## Mandatory Workflow Rule: Post-Modification Build & Packaging

Whenever any code changes, bug fixes, UI/front-end asset edits (`wwwroot/`), or configuration updates are completed:
1. **Always execute the `build-and-package` skill**:
   - Run the automated build and packaging script:
     ```powershell
     powershell -ExecutionPolicy Bypass -File .agents/skills/build-and-package/scripts/package.ps1
     ```
   - Ensure unit tests pass, the Release binaries compile without error, and the distribution package in `release/HorizonCycling_V1.1.0/` along with `release/HorizonCycling_V1.1.0.zip` are updated.
2. **Report build and packaging status** to the user at the conclusion of the task, including:
   - Test execution result
   - Release executable timestamp
   - Distribution ZIP timestamp and size
