using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tessera.Core.Alignment;
using Bitmap = System.Drawing.Bitmap;

namespace Tessera.Desktop;

public partial class ImageEditorPage
{
    internal static T? Within<T>(DependencyObject? target) where T : DependencyObject
    {
        for (var current = target; current is not null; current = ParentOf(current)) if (current is T match) return match;
        return null;
    }

    internal static DependencyObject? ParentOf(DependencyObject target) => target is Visual or System.Windows.Media.Media3D.Visual3D
        ? VisualTreeHelper.GetParent(target) : LogicalTreeHelper.GetParent(target);
}
