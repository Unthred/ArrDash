namespace ArrDash.Models;

public sealed class ActivityLayoutItem
{
    public string Id { get; set; } = "";
    public int Span { get; set; } = 1;
    /// <summary>1 = left column, 2 = right column, 0 = auto (first free cell).</summary>
    public int Column { get; set; }

    public ActivityLayoutItem() { }

    public ActivityLayoutItem(string id, int span = 1, int column = 0)
    {
        Id = id;
        Span = span is 2 ? 2 : 1;
        Column = Span == 2 ? 0 : column is 1 or 2 ? column : 0;
    }
}
