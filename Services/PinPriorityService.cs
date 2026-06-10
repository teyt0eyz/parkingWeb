namespace ParkingWeb.Services;

/// <summary>
/// เก็บลำดับความสำคัญ (priority) ของขา GPIO แต่ละขา
/// ลำดับนี้ใช้ทั้งฝั่ง GpioService (ขาไหนชนะถ้ามีรถพร้อมกันหลายขา — ตรวจตามลำดับนี้ ใครเจอก่อนชนะ)
/// และฝั่งหน้าจอ Kiosk/Dev (กำหนด z-index ของเลเยอร์ภาพ ตัวแรก = สำคัญสุด = อยู่บนสุด)
/// บันทึกลงไฟล์เพื่อให้จำข้ามรีบูต (เหมือน rotation.dat)
/// </summary>
public class PinPriorityService
{
    private static readonly int[] DefaultOrder = { 17, 27, 22, 23, 24 };

    private readonly string _filePath;
    private readonly object _lock = new();
    private int[] _order;

    public PinPriorityService(IWebHostEnvironment env)
    {
        _filePath = Path.Combine(env.ContentRootPath, "pin_priority.dat");
        _order = Load();
    }

    public int[] GetOrder()
    {
        lock (_lock) return (int[])_order.Clone();
    }

    public bool SetOrder(int[]? order)
    {
        if (!IsValidOrder(order)) return false;

        lock (_lock)
        {
            _order = (int[])order!.Clone();
            try { File.WriteAllText(_filePath, string.Join(",", _order)); } catch { }
        }
        return true;
    }

    private static bool IsValidOrder(int[]? order) =>
        order != null
        && order.Length == DefaultOrder.Length
        && order.OrderBy(x => x).SequenceEqual(DefaultOrder.OrderBy(x => x));

    private int[] Load()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                var parts = File.ReadAllText(_filePath).Trim()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(int.Parse).ToArray();
                if (IsValidOrder(parts)) return parts;
            }
        }
        catch { }
        return (int[])DefaultOrder.Clone();
    }
}
