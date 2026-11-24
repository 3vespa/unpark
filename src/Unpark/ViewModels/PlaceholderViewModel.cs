namespace Unpark.ViewModels;

/// <summary>Page pas encore développée (étapes suivantes).</summary>
public sealed class PlaceholderViewModel(string title, string icon, string description) : PageViewModel(title, icon)
{
    public string Description { get; } = description;
}
