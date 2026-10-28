using System.Management;

namespace HpPanel.Core;

internal static class HpBios
{
    private const string Scope = @"root\wmi";
    private static readonly byte[] Sign = { 0x53, 0x45, 0x43, 0x55 };

    public const uint CmdRead = 1;
    public const uint CmdWrite = 2;

    public const uint TypeFeatures = 13;
    public const uint TypeThermalMode = 76;

    public readonly record struct Result(uint ReturnCode, byte[] Data);

    public static Result Exec(uint command, uint commandType, byte[] input, int returnSize)
    {
        string method = returnSize switch
        {
            0 => "hpqBIOSInt0",
            4 => "hpqBIOSInt4",
            128 => "hpqBIOSInt128",
            1024 => "hpqBIOSInt1024",
            4096 => "hpqBIOSInt4096",
            _ => throw new ArgumentOutOfRangeException(nameof(returnSize), "allowed: 0, 4, 128, 1024, 4096"),
        };
        if (input.Length > 128)
        {
            throw new ArgumentException("input data longer than 128 bytes is not supported");
        }

        int[] lengths = input.Length == 0 ? new[] { 128, 0 } : new[] { input.Length, 128 };
        var errors = new List<string>();

        foreach (int len in lengths)
        {
            try
            {
                return Invoke(method, command, commandType, input, len);
            }
            catch (ManagementException e) when (e.ErrorCode == ManagementStatus.InvalidParameter
                                                || e.ErrorCode == ManagementStatus.InvalidMethodParameters)
            {
                errors.Add($"hpqBData length {len}: {e.Message} (0x{(uint)e.ErrorCode:X8})");
            }
        }
        throw new InvalidOperationException($"{method} rejected every argument shape: " + string.Join("; ", errors));
    }

    private static Result Invoke(string method, uint command, uint commandType, byte[] input, int arrayLength)
    {
        using var cls = new ManagementClass(Scope, "hpqBIntM", null);
        foreach (ManagementObject inst in cls.GetInstances().Cast<ManagementObject>())
        {
            using (inst)
            {
                using ManagementBaseObject inParams = inst.GetMethodParameters(method);
                using var dataInClass = new ManagementClass(Scope, "hpqBDataIn", null);
                using ManagementObject dataIn = dataInClass.CreateInstance();

                var buf = new byte[arrayLength];
                Array.Copy(input, buf, Math.Min(input.Length, arrayLength));
                dataIn["Sign"] = Sign;
                dataIn["Command"] = command;
                dataIn["CommandType"] = commandType;
                dataIn["Size"] = (uint)input.Length;
                dataIn["hpqBData"] = buf;
                inParams["InData"] = dataIn;

                using ManagementBaseObject outParams = inst.InvokeMethod(method, inParams, null);
                var outData = (ManagementBaseObject)outParams["OutData"];
                uint rc = Convert.ToUInt32(GetProp(outData, "rwReturnCode") ?? 0u);
                byte[] data = GetProp(outData, "Data") as byte[] ?? [];
                return new Result(rc, data);
            }
        }
        throw new InvalidOperationException("hpqBIntM not found (root\\wmi). Not an HP machine, or not running as administrator.");
    }

    private static object? GetProp(ManagementBaseObject o, string name)
    {
        foreach (PropertyData p in o.Properties)
        {
            if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return p.Value;
            }
        }

        return null;
    }
}
