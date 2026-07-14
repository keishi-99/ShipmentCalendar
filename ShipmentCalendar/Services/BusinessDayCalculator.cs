using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>営業日計算サービス（休日・土日を除く）</summary>
public class BusinessDayCalculator(IEnumerable<Holiday> holidays) {
    private readonly HashSet<DateOnly> _holidays = holidays.Select(h => h.Date).ToHashSet();

    /// <summary>基準日からN営業日前の日付を返す</summary>
    public DateOnly SubtractBusinessDays(DateOnly baseDate, int businessDays) {
        var current = baseDate;
        var remaining = businessDays;

        while (remaining > 0) {
            current = current.AddDays(-1);
            if (IsBusinessDay(current))
                remaining--;
        }
        return current;
    }

    /// <summary>指定日が営業日かどうかを判定</summary>
    public bool IsBusinessDay(DateOnly date) {
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        return !_holidays.Contains(date);
    }

    /// <summary>
    /// 工程定義から期限日を計算する。
    /// 末尾工程（完了日に近い工程）から逆向きに残り所要時間を累積し、480分（8時間）単位の
    /// 日チャンクに割り当てることで、各工程の「着手必須日（startBucket）」と
    /// 「完了必須日（finishBucket）」を別々に算出する。
    /// 工程自身の所要時間が日をまたぐ場合（例: 当日の残り枠に収まらず前営業日から
    /// 着手しないと完了できない場合）、着手必須日と完了必須日が異なる日になる。
    /// </summary>
    /// <summary>
    /// completedByDestNumber: 完了済み指示先番号→（受入日, 作業者名, 実工数(分)） のマッピング（指示先番号は工程ごとに一意）
    /// </summary>
    public List<OrderProcess> BuildProcesses(Order order, IEnumerable<ProcessDefinition> definitions, Dictionary<string, (DateOnly? ActualDate, string WorkerName, double ActualWorkMinutes)> completedByDestNumber) {
        var sorted = definitions.OrderBy(d => d.SortOrder).ToList();
        if (sorted.Count == 0) return [];

        // 末尾工程から逆向きに、完了日から数えた日チャンク番号
        // （1=完了日当日、2=その前営業日…）で着手・完了それぞれの必須バケットを求める
        double runningIn = 0;
        var startBucket = new int[sorted.Count];
        var finishBucket = new int[sorted.Count];
        for (int i = sorted.Count - 1; i >= 0; i--) {
            var def = sorted[i];
            double minutes = def.LeadTimeMinutes * order.PlannedQuantity;
            double adjusted = runningIn;

            // 外注リードタイム（数量に依存しない営業日単位の待機）。
            // この工程の後にOutsourceLeadDays営業日分の空白（待機専用の日）が入るため、
            // その日数分だけ完了必須日を前倒しする。待機ゲート自体は日単位で固定する
            // （外注の出荷・受け取りは営業日単位で発生するため）。
            // daysSoFarはrunningIn（後続工程が実際に消費した位置）を基準にする。
            // 外注待ちが複数回連続する場合、前回の待機による丸め分（繰り越し）も
            // ここに含まれている必要があるため、素の合計時間ではなくrunningInを使う
            if (def.OutsourceLeadDays > 0) {
                // adjustedがちょうど480の倍数（例: 1440）の場合、floor+1だと1日多く繰り上がってしまう
                // （1440分=3日ぴったり消費なのに4日と判定される）ため、Ceilingで判定する。
                // adjusted=0（末尾工程自体が外注待ちで、後続が何も消費していない）の場合は
                // Ceiling(0/480)=0となってしまうため、1日目として扱うために1に補正する
                var daysSoFar = adjusted > 0 ? (int)Math.Ceiling(adjusted / 480.0) : 1;
                adjusted = (daysSoFar + def.OutsourceLeadDays) * 480.0;
            }

            // 滞留時間（数量に依存しない固定の待機時間）。外注リードタイムや末尾工程など
            // どのケースでも、adjustedの基準値に上乗せする。
            // 480分を超える分は、後段の計算により自動的に前営業日以前へ繰り越される
            if (def.DwellTimeMinutes > 0) {
                adjusted += def.DwellTimeMinutes;
            }

            // ゲート（待機日数分の空白）自体は共有させないが、工程自身の所要時間は
            // 分単位で正確に積む。所要時間が480分未満で余りがある場合、その余りは
            // 前工程（より着手が早い工程）が同じ日の枠として使えるようにする
            finishBucket[i] = (int)(adjusted / 480.0) + 1;
            runningIn = adjusted + minutes;
            // (runningIn - 1) / 480.0 は小数の場合に丸め誤差で1日不足することがあるため、Ceilingで判定する
            startBucket[i] = runningIn > 0 ? (int)Math.Ceiling(runningIn / 480.0) : 1;
        }

        // 各工程の必須日 = 完了日 - (バケット番号 - 1) 営業日（バケット1=完了日当日）
        var results = new List<OrderProcess>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++) {
            var def = sorted[i];
            double requiredMinutes = def.LeadTimeMinutes * order.PlannedQuantity;
            var dueDate = SubtractBusinessDays(order.CompletionDate, finishBucket[i] - 1);
            var startDate = SubtractBusinessDays(order.CompletionDate, startBucket[i] - 1);
            // 指示先番号（一意）で完了判定。指示内容（表示名）の重複の影響を受けない
            var isCompleted = completedByDestNumber.TryGetValue(def.DestinationCode, out var completed);
            results.Add(new OrderProcess {
                ProcessName = def.ProcessName,
                DestinationCode = def.DestinationCode,
                StartDate = startDate,
                DueDate = dueDate,
                ActualDate = isCompleted ? completed.ActualDate : null,
                Status = isCompleted ? ProcessStatus.Completed : ProcessStatus.NotStarted,
                SortOrder = def.SortOrder,
                DepartmentId = def.DepartmentId,
                RequiredMinutes = requiredMinutes,
                OutsourceLeadDays = def.OutsourceLeadDays,
                DwellTimeMinutes = def.DwellTimeMinutes,
                WorkerName = isCompleted ? completed.WorkerName : string.Empty,
                ActualWorkMinutes = isCompleted ? completed.ActualWorkMinutes : 0
            });
        }
        return results;
    }

    /// <summary>完了実績データ（製番横断）から、製番ごとの実績工程バー用OrderProcessリストを組み立てる。
    /// RequiredMinutesに実績作業時間を入れることで、ProcessBarControlをそのまま実績バーとして再利用する。</summary>
    public static Dictionary<string, List<OrderProcess>> BuildActualProcesses(
        IEnumerable<ProcessDefinition> definitions,
        IEnumerable<(string Seiban, string DestinationCode, DateOnly ActualDate, string WorkerName, double ActualWorkMinutes)> completedRows) {

        // マスタ側にDestinationCodeの重複がある場合でもクラッシュしないよう、先勝ちで一意化してから辞書化する
        var defByDest = definitions
            .DistinctBy(d => d.DestinationCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(d => d.DestinationCode, StringComparer.OrdinalIgnoreCase);
        var result = new Dictionary<string, List<OrderProcess>>();

        foreach (var group in completedRows.GroupBy(r => r.Seiban, StringComparer.OrdinalIgnoreCase)) {
            // 同一工程（指示先番号）に複数の受入実績がある場合は作業時間を合計し、より新しい受入日の担当者・日付を採用する
            // （事前に集約しないと、後続でDestinationCodeが重複したOrderProcessが複数生成されてしまう）
            var aggregated = group
                .GroupBy(r => r.DestinationCode, StringComparer.OrdinalIgnoreCase)
                .Select(g => {
                    var latest = g.OrderByDescending(r => r.ActualDate).First();
                    return (DestinationCode: g.Key, ActualDate: latest.ActualDate, WorkerName: latest.WorkerName, ActualWorkMinutes: g.Sum(r => r.ActualWorkMinutes));
                });

            var matched = aggregated
                .Select(r => (Row: r, Def: defByDest.GetValueOrDefault(r.DestinationCode)))
                .Where(x => x.Def != null)
                .Select(x => (x.Row, Def: x.Def!))
                .OrderBy(x => x.Def.SortOrder)
                .ToList();

            var processes = new List<OrderProcess>();
            DateOnly? previousActualDate = null;
            foreach (var (row, def) in matched) {
                var dueDate = row.ActualDate;
                // 実績データの登録順序が前後している場合（入力ミスや並行作業等）、前工程の受入日がこの工程の受入日より
                // 未来になることがある。StartDate > DueDateはProcessBarControl側で描画スキップの原因になるため、
                // その場合はdueDate自身をStartDateとして使う
                var startDate = previousActualDate is { } prev && prev < dueDate ? prev : dueDate;

                processes.Add(new OrderProcess {
                    ProcessName = def.ProcessName,
                    DestinationCode = def.DestinationCode,
                    SortOrder = def.SortOrder,
                    StartDate = startDate,
                    DueDate = dueDate,
                    ActualDate = row.ActualDate,
                    WorkerName = row.WorkerName,
                    Status = ProcessStatus.Completed,
                    RequiredMinutes = row.ActualWorkMinutes,
                    ActualWorkMinutes = row.ActualWorkMinutes,
                    OutsourceLeadDays = def.OutsourceLeadDays,
                    DwellTimeMinutes = def.DwellTimeMinutes,
                    DepartmentId = def.DepartmentId
                });

                previousActualDate = row.ActualDate;
            }
            // 実績行が工程定義に1件もマッチしなかった場合、空のResultGroupを表示させないため追加しない
            if (processes.Count > 0)
                result[group.Key] = processes;
        }

        return result;
    }

    /// <summary>今日の日付を基準に工程ステータスを自動判定する</summary>
    public static ProcessStatus DetermineStatus(OrderProcess process, DateOnly today, int warningDays = 0) {
        if (process.Status == ProcessStatus.Completed)
            return ProcessStatus.Completed;

        if (today > process.DueDate)
            return ProcessStatus.Overdue;

        if (warningDays > 0 && (process.DueDate.DayNumber - today.DayNumber) <= warningDays)
            return ProcessStatus.Warning;

        if (today >= process.StartDate)
            return ProcessStatus.InProgress;

        return ProcessStatus.NotStarted;
    }
}
