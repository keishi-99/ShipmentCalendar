## Git & PR Workflow section
- Before claiming a PR's status or comparing branches, always run `git fetch` and inspect the remote state (e.g., `git log origin/main`) rather than relying on stale local main references.

## Data Flow / BuildProcesses section
- When adding a new field to OrderProcess (e.g. a value loaded from ODBC), verify it survives BuildProcesses in BusinessDayCalculator.cs — completed processes are rebuilt from the `completedByDestNumber` dictionary in MainViewModel.cs, so any new field must be added to that tuple too or it will silently reset to its default (0/empty) in the UI.

## Editing Conventions section
- Never use PowerShell to edit source files containing Japanese text; use the Edit tool instead to avoid encoding corruption.

<!-- ## Debugging UI section
- Before reporting a UI fix as complete, verify it against the actual running app (screenshot or manual check) rather than asserting the layout/logic is correct from code inspection alone. -->