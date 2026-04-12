namespace Hex.Scaffold.Domain.Common;

public abstract class SmartEnum<TEnum> where TEnum : SmartEnum<TEnum>
{
  private static readonly Dictionary<int, TEnum> _items = [];

  public string Name { get; }
  public int Value { get; }

  protected SmartEnum(string name, int value)
  {
    Name = name;
    Value = value;
    _items[value] = (TEnum)this;
  }

  public static TEnum FromValue(int value) =>
    _items.TryGetValue(value, out var item)
      ? item
      : throw new ArgumentException($"No {typeof(TEnum).Name} with value {value}.");

  public static TEnum FromName(string name) =>
    _items.Values.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
    ?? throw new ArgumentException($"No {typeof(TEnum).Name} with name '{name}'.");

  public override string ToString() => Name;
}
