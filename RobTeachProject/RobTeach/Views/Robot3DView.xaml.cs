using System.Windows;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace RobTeach.Views
{
    public partial class Robot3DView : Window
    {
        public Robot3DView()
        {
            InitializeComponent();
            var grid = new GridLinesVisual3D();
            Viewport.Children.Add(grid);
        }
    }
}
