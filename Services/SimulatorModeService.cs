namespace ParkingWeb.Services;

/// <summary>
/// สลับโหมดระหว่าง Normal (รอสัญญาณ GPIO จริง) กับ Simulator (สั่งเปลี่ยนภาพจากหน้า Dev)
/// ตอนเปิด Simulator, GpioService จะหยุดส่ง ReceiveStatus จากการอ่านขา GPIO จริง
/// เพื่อไม่ให้สัญญาณค้างที่ยังไม่ได้ต่อ hardware ไปทับการจำลอง
/// </summary>
public class SimulatorModeService
{
    private volatile bool _enabled;
    private volatile bool _resyncRequested;

    public bool Enabled => _enabled;

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        // กลับสู่ Normal mode → ให้ GpioService sync สถานะจริงจากขา GPIO ทันทีในรอบถัดไป
        if (!enabled) _resyncRequested = true;
    }

    public bool ConsumeResyncRequest()
    {
        if (!_resyncRequested) return false;
        _resyncRequested = false;
        return true;
    }
}
