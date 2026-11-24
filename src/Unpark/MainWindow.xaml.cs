using System.Windows;
using Unpark.Interop;

namespace Unpark;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Barre de titre sombre (Windows 10 20H1+ et Windows 11), sans effet ailleurs.
        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
    }
}
