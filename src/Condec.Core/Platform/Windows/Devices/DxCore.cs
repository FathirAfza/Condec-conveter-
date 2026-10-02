// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Condec contributors

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Condec.Core.Platform.Windows.Devices;

// DXCore (dxcore.dll) as declared in the official header dxcore_interface.h of microsoft/DirectX-Headers
// (MIT licence). Only the first methods of each interface are declared: they are all Condec calls, and the order
// of the methods is the order of the vtable. The `bool` results of the header are one byte.

[GeneratedComInterface]
[Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e")]
internal partial interface IDXCoreAdapter
{
    [PreserveSig]
    byte IsValid();

    [PreserveSig]
    byte IsAttributeSupported(in Guid attributeGuid);

    [PreserveSig]
    byte IsPropertySupported(uint property);

    [PreserveSig]
    int GetProperty(uint property, nuint bufferSize, nint propertyData);

    [PreserveSig]
    int GetPropertySize(uint property, out nuint bufferSize);
}

[GeneratedComInterface]
[Guid("526c7776-40e9-459b-b711-f32ad76dfc28")]
internal partial interface IDXCoreAdapterList
{
    [PreserveSig]
    int GetAdapter(uint index, in Guid riid, out IDXCoreAdapter adapter);

    [PreserveSig]
    uint GetAdapterCount();
}

[GeneratedComInterface]
[Guid("78ee5945-c36e-4b13-a669-005dd11c0f06")]
internal partial interface IDXCoreAdapterFactory
{
    [PreserveSig]
    int CreateAdapterList(uint numAttributes, in Guid filterAttributes, in Guid riid, out IDXCoreAdapterList adapterList);
}

internal static partial class DxCore
{
    // DXCoreAdapterProperty
    public const uint DriverDescription = 2;
    public const uint DedicatedAdapterMemory = 7;
    public const uint IsHardware = 11;
    public const uint IsIntegrated = 12;

    public static readonly Guid FactoryId = new("78ee5945-c36e-4b13-a669-005dd11c0f06");
    public static readonly Guid AdapterListId = new("526c7776-40e9-459b-b711-f32ad76dfc28");
    public static readonly Guid AdapterId = new("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e");

    // DXCORE_HARDWARE_TYPE_ATTRIBUTE_GPU and _NPU
    public static readonly Guid GpuAttribute = new("b69eb219-3ded-4464-979f-a00bd4687006");
    public static readonly Guid NpuAttribute = new("d46140c4-add7-451b-9e56-06fe8c3b58ed");

    [LibraryImport("dxcore.dll")]
    private static partial int DXCoreCreateAdapterFactory(in Guid riid, out nint factory);

    public static IDXCoreAdapterFactory? CreateFactory()
    {
        if (DXCoreCreateAdapterFactory(FactoryId, out var pointer) < 0 || pointer == 0)
        {
            return null;
        }

        try
        {
            return (IDXCoreAdapterFactory)new StrategyBasedComWrappers().GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>The adapters of one hardware type (GPU or NPU), or an empty list when DXCore can't list them.</summary>
    public static List<IDXCoreAdapter> List(IDXCoreAdapterFactory factory, in Guid hardwareType)
    {
        var adapters = new List<IDXCoreAdapter>();
        if (factory.CreateAdapterList(1, hardwareType, AdapterListId, out var list) < 0)
        {
            return adapters;
        }

        var count = list.GetAdapterCount();
        for (uint i = 0; i < count; i++)
        {
            if (list.GetAdapter(i, AdapterId, out var adapter) >= 0)
            {
                adapters.Add(adapter);
            }
        }

        return adapters;
    }

    public static bool? ReadBool(IDXCoreAdapter adapter, uint property) =>
        ReadBytes(adapter, property, 1) is { } bytes ? bytes[0] != 0 : null;

    /// <summary>A size_t property such as DedicatedAdapterMemory.</summary>
    public static long? ReadSize(IDXCoreAdapter adapter, uint property) =>
        ReadBytes(adapter, property, (nuint)nuint.Size) is { } bytes
            ? nuint.Size == 8 ? BitConverter.ToInt64(bytes) : BitConverter.ToUInt32(bytes)
            : null;

    /// <summary>A narrow (ANSI) string property such as DriverDescription.</summary>
    public static string? ReadString(IDXCoreAdapter adapter, uint property)
    {
        if (adapter.IsPropertySupported(property) == 0 || adapter.GetPropertySize(property, out var size) < 0 || size == 0 || size > 4096)
        {
            return null;
        }

        return ReadBytes(adapter, property, size) is { } bytes
            ? System.Text.Encoding.Latin1.GetString(bytes).TrimEnd('\0').Trim()
            : null;
    }

    private static byte[]? ReadBytes(IDXCoreAdapter adapter, uint property, nuint size)
    {
        if (adapter.IsPropertySupported(property) == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((nint)size);
        try
        {
            if (adapter.GetProperty(property, size, buffer) < 0)
            {
                return null;
            }

            var bytes = new byte[(int)size];
            Marshal.Copy(buffer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
