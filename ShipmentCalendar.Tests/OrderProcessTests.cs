using System.Globalization;
using ShipmentCalendar.Models;

namespace ShipmentCalendar.Tests;

public class OrderProcessTests : IDisposable {
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;

    // F1書式はCurrentCultureに依存し、小数点がカンマの環境では固定文字列と一致しないため、ピリオドになるカルチャに固定する
    // xUnitはテストごとにクラスを生成・破棄するので、コンストラクタで固定し、Disposeで元に戻す
    public OrderProcessTests() {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose() {
        CultureInfo.CurrentCulture = _originalCulture;
    }

    // お手本: 分表記では、0以下は「0分」、それ以外は小数点1桁で「N.N分」になる
    // Theory は InlineData の組ごとにテストが1回ずつ実行される（引数 = 入力, 期待値）
    [Theory]
    [InlineData(0, "0分")]
    [InlineData(-5, "0分")]
    [InlineData(90, "90.0分")]
    [InlineData(12.34, "12.3分")]
    public void GetRequiredTimeDescription_InMinutes_FormatsWithOneDecimal(double requiredMinutes, string expected) {
        var process = new OrderProcess { RequiredMinutes = requiredMinutes };

        Assert.Equal(expected, process.GetRequiredTimeDescription(showInMinutes: true));
    }

    [Theory]
    [InlineData(0, "0.0h")]
    [InlineData(-5, "0.0h")]
    [InlineData(30, "0.5h")]
    [InlineData(100, "1.7h")]
    public void GetRequiredTimeDescription_NotInMinutes_FormatsWithOneDecimal(double requiredMinutes, string expected) {
        var process = new OrderProcess { RequiredMinutes = requiredMinutes };

        Assert.Equal(expected, process.GetRequiredTimeDescription(showInMinutes: false));
    }

    [Theory]
    [InlineData(0, "0分")]
    [InlineData(-5, "0分")]
    [InlineData(90, "90.0分")]
    [InlineData(12.34, "12.3分")]
    public void GetActualWorkTimeDescription_InMinutes_DefaultsToMinutes(double actualWorkMinutes, string expected) {
        var process = new OrderProcess { ActualWorkMinutes = actualWorkMinutes };

        Assert.Equal(expected, process.GetActualWorkTimeDescription(showInMinutes: true));
    }

    [Theory]
    [InlineData(0, "0.0h")]
    [InlineData(-5, "0.0h")]
    [InlineData(30, "0.5h")]
    [InlineData(100, "1.7h")]
    public void GetActualWorkTimeDescription_NotInMinutes_FormatsWithOneDecimal(double actualWorkMinutes, string expected) {
        var process = new OrderProcess { ActualWorkMinutes = actualWorkMinutes };

        Assert.Equal(expected, process.GetActualWorkTimeDescription(showInMinutes: false));
    }
}
