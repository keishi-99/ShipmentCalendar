## Build & Test Loop section
- ビルド: `dotnet build ShipmentCalendar.slnx`
- テスト: `dotnet test ShipmentCalendar.Tests/ShipmentCalendar.Tests.csproj`
- コードを変更したら、作業完了を報告する前に必ずビルドとテストを実行すること。
- ビルドエラーやテスト失敗が出たら、原因を調べて修正し、ビルドとテストが全件成功するまで「修正 → ビルド → テスト」を繰り返すこと。
- テストを通すためにテストを削除・スキップしたり、期待値を実装に合わせて書き換えたりしないこと。テスト側が間違っていると判断した場合は、修正せずに理由を報告して確認を求めること。
- 同じ失敗を3回修正しても解決しない場合は、それ以上続けず、試したことと考えられる原因をまとめて報告すること。
- `Services/` の計算・判定ロジックを追加・変更した場合は、`ShipmentCalendar.Tests` に対応するテストも追加すること。
- ODBC 接続、Repositories の実 DB アクセス、`Views/` の画面表示はテストで確認できない。これらを変更した場合は「テストでは未確認」であることを報告に明記すること。
- 完了報告には、実行したテストの件数と結果（成功数・失敗数）を含めること。

## Git & PR Workflow section
- Before claiming a PR's status or comparing branches, always run `git fetch` and inspect the remote state (e.g., `git log origin/main`) rather than relying on stale local main references.

## Data Flow / BuildProcesses section
- When adding a new field to OrderProcess (e.g. a value loaded from ODBC), verify it survives BuildProcesses in BusinessDayCalculator.cs — completed processes are rebuilt from the `completedByDestNumber` dictionary in MainViewModel.cs, so any new field must be added to that tuple too or it will silently reset to its default (0/empty) in the UI.

## Editing Conventions section
- Never use PowerShell to edit source files containing Japanese text; use the Edit tool instead to avoid encoding corruption.

<!-- ## Debugging UI section
- Before reporting a UI fix as complete, verify it against the actual running app (screenshot or manual check) rather than asserting the layout/logic is correct from code inspection alone. -->