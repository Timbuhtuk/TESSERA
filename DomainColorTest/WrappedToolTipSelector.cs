using System.Windows;
using System.Windows.Controls;

namespace DomainColorTest;

public sealed class WrappedToolTipSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is string ? TextTemplate : null;
}
