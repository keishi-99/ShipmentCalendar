using ShipmentCalendar.Models;
using ShipmentCalendar.Services;

namespace ShipmentCalendar.Tests;

public class ProductCategoryClassifierTests
{
    private static Order MakeOrder(string modelCode, string itemNumber = "I1") => new() {
        ModelCode = modelCode,
        ItemNumber = itemNumber
    };

    [Fact]
    public void Classify_ProductModelCode_ReturnsProduct() {
        var classifier = new ProductCategoryClassifier(productModelCodes: ["P1"], semiProductModelCodes: [], registeredItemNumbers: []);

        var result = classifier.Classify(MakeOrder("P1"));

        Assert.Equal(ProductCategoryClassifier.Product, result);
    }

    [Fact]
    public void Classify_SemiProductModelCode_ReturnsSemiProduct() {
        var classifier = new ProductCategoryClassifier(productModelCodes: [], semiProductModelCodes: ["S1"], registeredItemNumbers: []);

        var result = classifier.Classify(MakeOrder("S1"));

        Assert.Equal(ProductCategoryClassifier.SemiProduct, result);
    }

    [Fact]
    public void Classify_UnknownModelCode_ReturnsOther() {
        var classifier = new ProductCategoryClassifier(productModelCodes: ["P1"], semiProductModelCodes: ["S1"], registeredItemNumbers: []);

        var result = classifier.Classify(MakeOrder("X1"));

        Assert.Equal(ProductCategoryClassifier.Other, result);
    }

    /// <summary>機種コードが製品・半製品の両方に登録されている場合（本来あってはならないマスタ不整合）は製品側を優先する</summary>
    [Fact]
    public void Classify_ModelCodeInBothProductAndSemiProduct_PrefersProduct() {
        var classifier = new ProductCategoryClassifier(productModelCodes: ["X1"], semiProductModelCodes: ["X1"], registeredItemNumbers: []);

        var result = classifier.Classify(MakeOrder("X1"));

        Assert.Equal(ProductCategoryClassifier.Product, result);
    }

    [Fact]
    public void IsUnregisteredSemiProduct_RegisteredItemNumber_ReturnsFalse() {
        var classifier = new ProductCategoryClassifier(productModelCodes: [], semiProductModelCodes: ["S1"], registeredItemNumbers: ["I1"]);

        var result = classifier.IsUnregisteredSemiProduct(MakeOrder("S1", "I1"));

        Assert.False(result);
    }

    [Fact]
    public void IsUnregisteredSemiProduct_UnregisteredItemNumber_ReturnsTrue() {
        var classifier = new ProductCategoryClassifier(productModelCodes: [], semiProductModelCodes: ["S1"], registeredItemNumbers: ["OtherItem"]);

        var result = classifier.IsUnregisteredSemiProduct(MakeOrder("S1", "I1"));

        Assert.True(result);
    }

    /// <summary>
    /// 機種コードが製品・半製品の両方に登録されている場合、Classify()は製品を返すため
    /// 半製品（工程未登録）判定からも除外されるべき（CodeRabbitの指摘で見つかった不整合の再発防止）
    /// </summary>
    [Fact]
    public void IsUnregisteredSemiProduct_ModelCodeAlsoRegisteredAsProduct_ReturnsFalse() {
        var classifier = new ProductCategoryClassifier(productModelCodes: ["X1"], semiProductModelCodes: ["X1"], registeredItemNumbers: []);

        var result = classifier.IsUnregisteredSemiProduct(MakeOrder("X1", "UnregisteredItem"));

        Assert.False(result);
    }

    [Fact]
    public void IsUnregisteredSemiProduct_NonSemiProductModelCode_ReturnsFalse() {
        var classifier = new ProductCategoryClassifier(productModelCodes: [], semiProductModelCodes: [], registeredItemNumbers: []);

        var result = classifier.IsUnregisteredSemiProduct(MakeOrder("X1"));

        Assert.False(result);
    }
}
