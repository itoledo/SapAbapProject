using System.Reflection;
using System.Runtime.InteropServices;

namespace SapAbapProject.RfcExtractor.Extractors;

/// <summary>
/// Low-level helper that reads STRING_TABLE parameters from an SAP RFC function
/// via reflection + the SapNwRfc interop layer. This works around the SapNwRfc
/// limitation that it cannot map STRING_TABLE (TABLE OF STRING) parameters
/// to C# types, which is needed for RPY_FUNCTIONMODULE_READ_NEW's
/// NEW_SOURCE CHANGING parameter.
/// </summary>
internal static class SapRfcStringTableReader
{
    /// <summary>
    /// Reads a STRING_TABLE (TABLE OF STRING) parameter from a SapFunction
    /// that has already been invoked.
    /// </summary>
    public static List<string>? ReadStringTable(object sapFunction, string paramName)
    {
        try
        {
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var funcType = sapFunction.GetType();

            var functionHandle = (IntPtr?)funcType.GetField("_functionHandle", flags)?.GetValue(sapFunction);
            var interop = funcType.GetField("_interop", flags)?.GetValue(sapFunction);

            if (functionHandle is null || functionHandle.Value == IntPtr.Zero || interop is null)
                return null;

            return ReadTableViaInterop(interop, functionHandle.Value, paramName);
        }
        catch
        {
            return null;
        }
    }

    private static List<string>? ReadTableViaInterop(object interop, IntPtr funcHandle, string paramName)
    {
        var interopType = interop.GetType();

        // Call: GetTable(IntPtr dataHandle, string name, out IntPtr tableHandle, out errorInfo)
        var tableHandle = CallGetTable(interop, interopType, funcHandle, paramName);
        if (tableHandle == IntPtr.Zero)
            return null;

        // Call: GetRowCount(IntPtr tableHandle, out uint rowCount, out errorInfo)
        var rowCount = CallGetRowCount(interop, interopType, tableHandle);
        if (rowCount == 0)
            return null;

        var result = new List<string>((int)rowCount);

        // Move to first row
        CallMoveToFirstRow(interop, interopType, tableHandle);

        for (uint i = 0; i < rowCount; i++)
        {
            if (i > 0)
                CallMoveToNextRow(interop, interopType, tableHandle);

            var rowHandle = CallGetCurrentRow(interop, interopType, tableHandle);
            if (rowHandle == IntPtr.Zero)
                break;

            var value = CallGetString(interop, interopType, rowHandle);
            result.Add(value ?? "");
        }

        return result;
    }

    private static IntPtr CallGetTable(object interop, Type interopType, IntPtr dataHandle, string name)
    {
        // GetTable(IntPtr, string, out IntPtr, out ErrorInfo) → ResultCode
        var method = FindMethod(interopType, "GetTable", 4);
        if (method is null) return IntPtr.Zero;

        var args = new object?[] { dataHandle, name, IntPtr.Zero, null };
        var result = method.Invoke(interop, args);
        return IsSuccess(result) ? (IntPtr)(args[2] ?? IntPtr.Zero) : IntPtr.Zero;
    }

    private static uint CallGetRowCount(object interop, Type interopType, IntPtr tableHandle)
    {
        var method = FindMethod(interopType, "GetRowCount", 3);
        if (method is null) return 0;

        var args = new object?[] { tableHandle, (uint)0, null };
        var result = method.Invoke(interop, args);
        return IsSuccess(result) ? (uint)(args[1] ?? 0u) : 0u;
    }

    private static void CallMoveToFirstRow(object interop, Type interopType, IntPtr tableHandle)
    {
        var method = FindMethod(interopType, "MoveToFirstRow", 2);
        if (method is null) return;

        var args = new object?[] { tableHandle, null };
        method.Invoke(interop, args);
    }

    private static void CallMoveToNextRow(object interop, Type interopType, IntPtr tableHandle)
    {
        var method = FindMethod(interopType, "MoveToNextRow", 2);
        if (method is null) return;

        var args = new object?[] { tableHandle, null };
        method.Invoke(interop, args);
    }

    private static IntPtr CallGetCurrentRow(object interop, Type interopType, IntPtr tableHandle)
    {
        var method = FindMethod(interopType, "GetCurrentRow", 2);
        if (method is null) return IntPtr.Zero;

        var args = new object?[] { tableHandle, null };
        var result = method.Invoke(interop, args);
        return result is IntPtr ptr ? ptr : IntPtr.Zero;
    }

    private static string? CallGetString(object interop, Type interopType, IntPtr rowHandle)
    {
        var method = FindMethod(interopType, "GetString", 6);
        if (method is null) return null;

        // For STRING_TABLE, each row is a flat string. Try reading with a large buffer directly.
        // The field name for flat string tables is empty "".
        var buffer = new char[8192];
        var args = new object?[] { rowHandle, "", buffer, (uint)buffer.Length, (uint)0, null };
        var result = method.Invoke(interop, args);

        if (IsSuccess(result))
        {
            var len = (uint)(args[4] ?? 0u);
            if (len > 0)
                return new string(buffer, 0, (int)Math.Min(len, (uint)buffer.Length));
        }
        else
        {
            // If buffer was too small (result != 0), read the required length and retry
            var neededLen = (uint)(args[4] ?? 0u);
            if (neededLen > 0 && neededLen > buffer.Length)
            {
                buffer = new char[neededLen + 1];
                args = new object?[] { rowHandle, "", buffer, (uint)buffer.Length, (uint)0, null };
                result = method.Invoke(interop, args);
                if (IsSuccess(result))
                {
                    var len = (uint)(args[4] ?? 0u);
                    if (len > 0)
                        return new string(buffer, 0, (int)Math.Min(len, (uint)buffer.Length));
                }
            }
        }

        return "";
    }

    private static MethodInfo? FindMethod(Type type, string name, int paramCount)
    {
        return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == paramCount);
    }

    private static bool IsSuccess(object? result)
    {
        // RfcResultCode: 0 = RFC_OK
        if (result is int i) return i == 0;
        if (result is Enum e) return Convert.ToInt32(e) == 0;
        return result is IntPtr;
    }
}
