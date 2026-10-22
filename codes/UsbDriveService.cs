using System.Management;
namespace NoWrite;

public record UsbDrive(int Index, string Model, ulong Size, string Letters)
{
    public override string ToString() =>
        $"{(Letters == "" ? "(no letter)" : Letters)}  {Model}  ·  {Fmt.Bytes(Size)}";
}

public static class Fmt
{
    public static string Bytes(double b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" }; int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return $"{b:0.#} {u[i]}";
    }
}

public static class UsbDriveService
{
    public static List<UsbDrive> List()
    {
        var result = new List<UsbDrive>();
        using var disks = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDrive WHERE InterfaceType='USB'");
        foreach (ManagementObject d in disks.Get())
        {
            var id = ((string)d["DeviceID"]).Replace("\\", "\\\\");
            var letters = new List<string>();
            using var parts = new ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{id}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
            foreach (ManagementObject p in parts.Get())
            {
                var pid = ((string)p["DeviceID"]).Replace("\\", "\\\\");
                using var logs = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{pid}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                foreach (ManagementObject l in logs.Get()) letters.Add((string)l["DeviceID"]);
            }
            result.Add(new UsbDrive(Convert.ToInt32(d["Index"]), (string)d["Model"] ?? "USB Drive",
                Convert.ToUInt64(d["Size"] ?? 0UL), string.Join(" ", letters)));
        }
        return result;
    }
}
