# ShipmentCalendar（出荷カレンダー）

受注データの出荷日（納期）から逆算して、各工程の期限日・進捗・部署ごとの負荷を一覧表示する Windows デスクトップアプリです。
工程の遅れや締切の集中を早めに把握し、出荷遅延を防ぐことを目的としています。

## 主な機能

### メイン画面（ホーム）
- 受注ごとの出荷日・完了期限日・品目番号・機種コード・品目名・製番・計画数を一覧表示
- 工程バー、または工程ごとの列で進捗と期限日を表示
- 並べ替え（出荷日順 / 完了日順 / 工程期限順）
- クイックフィルター（超過 / 警告 / 着手前 / 進行中 / 完了 / 完了以外）
- 製品・半製品の区分、次工程の部署による絞り込み
- 表示列、フォントサイズ、行の高さなどの表示設定
- 一定間隔での自動更新

### 分析
- 進捗ダッシュボード
- 締切集中度（部署別の負荷カレンダー。欠員を考慮した充足率で判定）
- 実績分析
- 工程別ボトルネック分析（標準時間に対する超過・未達の判定）

### 設定（マスタ管理）
- 基本設定（ODBC 接続、共有データフォルダなど）
- 部署、部署ごとの欠員、休日、工程定義、表示設定
- 工程定義は品目番号を指定して ODBC データソースから取り込み、工程名・標準時間などを登録

## 動作環境

- Windows 10 / 11（x64）
- .NET 10（ビルド時。発行物は自己完結型のため実行側のランタイムインストールは不要）
- 受注データの取得元となる ODBC データソース（DSN）

## 使用技術

| 用途 | ライブラリ |
| --- | --- |
| UI | WPF、[Fluent.Ribbon](https://github.com/fluentribbon/Fluent.Ribbon)、MahApps.Metro.IconPacks |
| MVVM | CommunityToolkit.Mvvm |
| 受注データ取得 | System.Data.Odbc |
| マスタ DB | SQLite（Microsoft.Data.Sqlite） |
| テスト | xUnit v3 |

## 構成

```
ShipmentCalendar.slnx
├─ ShipmentCalendar/         アプリ本体（WPF）
│   ├─ Models/               データモデル、アプリ設定
│   ├─ ViewModels/           MainViewModel など
│   ├─ Views/                各ウィンドウ（XAML）
│   ├─ Services/             営業日計算、負荷計算、ボトルネック計算などのロジック
│   ├─ Repositories/         ODBC / SQLite からのデータ取得
│   ├─ Data/                 SQLite の初期化・接続
│   └─ Converters/
└─ ShipmentCalendar.Tests/   単体テスト（xUnit）
```

## ビルドと実行

```bash
dotnet build ShipmentCalendar.slnx
dotnet run --project ShipmentCalendar
dotnet test
```

自己完結型で発行する場合（発行プロファイル `FolderProfile` と同じ設定）:

```bash
dotnet publish ShipmentCalendar -c Release -r win-x64 --self-contained true
```

## 初期設定

初回起動後、リボンの「設定」タブ > 「基本」から次を設定します。

| 項目 | 内容 |
| --- | --- |
| ODBC DSN | 受注データを取得する ODBC データソース名 |
| 工場番号 | 受注データ・休日データの絞り込みに使う工場番号 |
| 共有データフォルダ | マスタ DB と編集ロックファイルを置くフォルダ（例: `\\server\share\ShipmentCalendarData`） |

- 共有データフォルダが未設定の場合、マスタ DB を扱う機能は使用できません。ローカルで運用する場合は、ローカルのパスを明示的に指定してください。
- 共有データフォルダの変更は、アプリの再起動後に反映されます。
- 設定は実行ファイルと同じ場所の `data/appsettings.json` に保存されます。

## データの保存先

| ファイル | 内容 |
| --- | --- |
| `data/appsettings.json`（実行ファイルのフォルダ） | アプリ設定 |
| `process.db`（共有データフォルダ） | 工程定義 |
| `department.db`（共有データフォルダ） | 部署、欠員 |
| `holiday.db`（共有データフォルダ） | 休日 |
| `*.lock`（共有データフォルダ） | 編集ロック |

障害時の影響範囲を限定するため、用途ごとに DB ファイルを分けています。
複数 PC からの同時編集は、画面単位の編集ロックで防いでいます（ロックが 30 分間更新されない場合は、異常終了などで残ったものとして引き継ぎ可能）。
