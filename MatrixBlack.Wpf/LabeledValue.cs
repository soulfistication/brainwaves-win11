namespace MatrixBlack;

public sealed class LabeledValue
{
    public LabeledValue(double value, string label, bool isAll = false)
    {
        Value = value;
        Label = label;
        IsAll = isAll;
    }

    public double Value { get; }
    public string Label { get; }
    public bool IsAll { get; }
}
