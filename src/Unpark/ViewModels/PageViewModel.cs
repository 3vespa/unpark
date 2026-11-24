using CommunityToolkit.Mvvm.ComponentModel;

namespace Unpark.ViewModels;

/// <summary>Une page du menu de gauche : titre + icône (police Segoe MDL2 Assets, présente sur Windows 10 et 11).</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(string title, string icon)
    {
        Title = title;
        Icon = icon;
    }

    public string Title { get; }
    public string Icon { get; }
}
