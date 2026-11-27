using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using UnityEngine;
using WeightList = Il2CppSystem.Collections.Generic.List<SkinnedCollisionHelper.VertexWeight>;

namespace ALCollisionPlus;

// WeightList is a native value type containing two object references. The
// normal Il2CppReferenceArray indexer value-boxes it on every read. Read the
// same references in place, including in-place replacements, and retain normal
// interop wrappers/GC handles for every object used outside this read.
internal static class NativeWeightGroups
{
    private static readonly int stride, boneOffset, weightsOffset, itemsOffset;
    // Matches Il2CppArrayBase.ArrayStartPointer in the bundled interop runtime.
    private static readonly int arrayHeader = Marshal.SizeOf<Il2CppObject>() + 2 * IntPtr.Size;

    static NativeWeightGroups()
    {
        var boxed = new SkinnedCollisionHelper.WeightList();
        var type = Il2CppClassPointerStore<SkinnedCollisionHelper.WeightList>.NativeClassPtr;
        int objectHeader = checked((int)(IL2CPP.il2cpp_object_unbox(boxed.Pointer).ToInt64() - boxed.Pointer.ToInt64()));
        uint alignment = 0;
        stride = IL2CPP.il2cpp_class_value_size(type, ref alignment);
        boneOffset = checked((int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(type, "Transform"))) - objectHeader;
        weightsOffset = checked((int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(type, "Weights"))) - objectHeader;
        if (boneOffset < 0 || weightsOffset < 0 || boneOffset + IntPtr.Size > stride || weightsOffset + IntPtr.Size > stride)
            throw new InvalidOperationException("Unexpected SkinnedCollisionHelper.WeightList native layout.");
        var list = new WeightList();
        itemsOffset = checked((int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(
            Il2CppClassPointerStore<WeightList>.NativeClassPtr, "_items")));
        GC.KeepAlive(list);
        GC.KeepAlive(boxed);
    }

    internal static IntPtr Data(Il2CppReferenceArray<SkinnedCollisionHelper.WeightList> groups)
        => IntPtr.Add(groups.Pointer, arrayHeader);

    internal static void Read(IntPtr data, int index, ref Transform bone, ref WeightList weights)
    {
        var entry = IntPtr.Add(data, index * stride);
        var bonePointer = Marshal.ReadIntPtr(entry, boneOffset);
        var weightsPointer = Marshal.ReadIntPtr(entry, weightsOffset);
        if (bonePointer != (bone?.Pointer ?? IntPtr.Zero))
            bone = bonePointer == IntPtr.Zero ? null : Il2CppObjectPool.Get<Transform>(bonePointer);
        if (weightsPointer != (weights?.Pointer ?? IntPtr.Zero))
            weights = weightsPointer == IntPtr.Zero ? null : Il2CppObjectPool.Get<WeightList>(weightsPointer);
    }

    internal static Il2CppStructArray<SkinnedCollisionHelper.VertexWeight> Items(WeightList weights,
        ref Il2CppStructArray<SkinnedCollisionHelper.VertexWeight> cached)
    {
        // List<T>._items constructs an array wrapper through Activator on every
        // access. Its backing pointer is still checked every frame, so capacity
        // growth and same-count array replacements remain immediately visible.
        var pointer = Marshal.ReadIntPtr(weights.Pointer, itemsOffset);
        if (pointer != (cached?.Pointer ?? IntPtr.Zero))
            cached = pointer == IntPtr.Zero ? null : new Il2CppStructArray<SkinnedCollisionHelper.VertexWeight>(pointer);
        return cached;
    }
}
