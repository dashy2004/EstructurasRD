using Xunit;
using LosasPlus.Views;

namespace LosasPlus.Tests;

public class PlantaValidationRulesTests
{
    [Theory]
    [InlineData(2.5, true)]
    [InlineData(0.1, true)]
    [InlineData(0.0, false)]
    [InlineData(-1.5, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void IsValidDimension_works_correctly(double val, bool expected)
    {
        Assert.Equal(expected, PlantaValidationRules.IsValidDimension(val));
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(-100.5, true)]
    [InlineData(100.5, true)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    public void IsValidCoordinate_works_correctly(double val, bool expected)
    {
        Assert.Equal(expected, PlantaValidationRules.IsValidCoordinate(val));
    }

    [Theory]
    [InlineData("Col-1", true)]
    [InlineData(" V-2 ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsValidName_works_correctly(string? name, bool expected)
    {
        Assert.Equal(expected, PlantaValidationRules.IsValidName(name));
    }

    [Theory]
    [InlineData(0.15, true)]
    [InlineData(0.0, false)]
    [InlineData(-0.1, false)]
    [InlineData(double.NaN, false)]
    public void IsValidEspesor_works_correctly(double val, bool expected)
    {
        Assert.Equal(expected, PlantaValidationRules.IsValidEspesor(val));
    }

    [Theory]
    [InlineData(2.5, true)]
    [InlineData(0.0, true)]
    [InlineData(-0.5, false)]
    [InlineData(double.NaN, false)]
    public void IsValidCarga_works_correctly(double val, bool expected)
    {
        Assert.Equal(expected, PlantaValidationRules.IsValidCarga(val));
    }
}
