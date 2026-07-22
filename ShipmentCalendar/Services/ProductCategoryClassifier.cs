using ShipmentCalendar.Models;

namespace ShipmentCalendar.Services;

/// <summary>機種コード登録マスタの区分に基づき、注文を「製品」「半製品」「どちらでもない」の3つに排他分類する。
/// 「半製品（工程未登録）」は分類ではなく、この3分類とは別軸で「半製品のうち工程マスタ未登録のもの」を
/// 絞り込むための判定のため、IsUnregisteredSemiProductとして別途提供する。
/// 機種コードが製品・半製品の両方に登録されている場合（本来あってはならないマスタ不整合）は製品側を優先する</summary>
public class ProductCategoryClassifier {
    public const string Product = "製品";
    public const string SemiProduct = "半製品";
    public const string Other = "どちらでもない";

    private readonly HashSet<string> _productModelCodes;
    private readonly HashSet<string> _semiProductModelCodes;
    private readonly HashSet<string> _registeredItemNumbers;

    public ProductCategoryClassifier(
        IEnumerable<string> productModelCodes,
        IEnumerable<string> semiProductModelCodes,
        IEnumerable<string> registeredItemNumbers) {
        _productModelCodes = new HashSet<string>(productModelCodes, StringComparer.OrdinalIgnoreCase);
        _semiProductModelCodes = new HashSet<string>(semiProductModelCodes, StringComparer.OrdinalIgnoreCase);
        _registeredItemNumbers = new HashSet<string>(registeredItemNumbers, StringComparer.OrdinalIgnoreCase);
    }

    public string Classify(Order order) {
        if (_productModelCodes.Contains(order.ModelCode)) return Product;
        if (_semiProductModelCodes.Contains(order.ModelCode)) return SemiProduct;
        return Other;
    }

    public bool IsUnregisteredSemiProduct(Order order) =>
        Classify(order) == SemiProduct && !_registeredItemNumbers.Contains(order.ItemNumber);
}
