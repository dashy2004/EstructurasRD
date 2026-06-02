using System;

namespace LosasPlus.Views;

public static class PlantaValidationRules
{
    public static bool IsValidDimension(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static bool IsValidCoordinate(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static bool IsValidName(string? name)
    {
        return !string.IsNullOrWhiteSpace(name);
    }

    public static bool IsValidEspesor(double value)
    {
        return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static bool IsValidCarga(double value)
    {
        return value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
