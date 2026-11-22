using System.Reflection;
using System.Windows;
using Tessera.Desktop;

namespace Tessera.RegressionTests;

internal static class WindowTestAccess
{
#nullable disable annotations
    public static object FindControl(this FrameworkElement owner, string name)
    {
        var element = owner.FindName(name);
        if (element is not null || owner is not MainWindow window) return element;
        return window.Editor.FindName(name) ?? window.Editor.Atlas.FindName(name) ?? window.LibraryPage.FindName(name);
    }
#nullable restore annotations

    public static object? GetField(object target, string name, BindingFlags flags)
    {
        var (field, owner) = FindMember(target, type => type.GetField(name, flags));
        return field?.GetValue(owner);
    }

    public static void SetField(MainWindow window, string name, BindingFlags flags, object? value)
    {
        var (field, owner) = FindMember(window, type => type.GetField(name, flags));
        (field ?? throw new MissingFieldException(name)).SetValue(owner, value);
    }

    public static object? GetProperty(MainWindow window, string name, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
    {
        var (property, owner) = FindMember(window, type => type.GetProperty(name, flags));
        return (property ?? throw new MissingMemberException(name)).GetValue(owner);
    }

    public static object? Invoke(object target, string name, BindingFlags flags, object?[]? args)
    {
        var (method, owner) = FindMember(target, type => type.GetMethod(name, flags));
        return (method ?? throw new MissingMethodException(name)).Invoke(owner, args);
    }

    private static (T? Member, object? Owner) FindMember<T>(object target, Func<Type, T?> find) where T : MemberInfo
    {
        if (target is not MainWindow window) return (find(target.GetType()), target);
        foreach (object owner in new object[] { window, window.Editor, window.Editor.History, window.Editor.Preview, window.Editor.Tools,
            window.Editor.Dock, window.Editor.Info, window.Editor.Atlas, window.LibraryPage })
            if (find(owner.GetType()) is { } member) return (member, owner);
        return (null, null);
    }
}
