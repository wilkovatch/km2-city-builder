using System;
using Godot;

namespace GDExtension.Wrappers;

internal static class Terrain3DWrapperUtils
{
    internal static T Bind<T>(Variant value, Func<GodotObject, T> bind) where T : GodotObject
    {
        var nativeObject = value.AsGodotObject();
        return nativeObject == null ? null : bind(nativeObject);
    }
}
