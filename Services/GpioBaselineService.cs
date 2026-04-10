namespace ParkingWeb.Services;

/// <summary>
/// Single source of truth สำหรับ idle baseline ของแต่ละ GPIO pin
/// Priority: manual override > startup auto-detect
/// </summary>
public class GpioBaselineService
{
    private readonly Dictionary<int, bool> _startupValues  = [];
    private readonly Dictionary<int, bool> _manualOverrides = [];

    // เรียกโดย GpioService ตอน startup
    public void SetStartupBaseline(int pin, bool idleIsHigh) =>
        _startupValues[pin] = idleIsHigh;

    // เรียกโดย Admin เมื่อต้องการ override
    public void SetManualBaseline(int pin, bool idleIsHigh) =>
        _manualOverrides[pin] = idleIsHigh;

    public void ClearManualBaseline(int pin) =>
        _manualOverrides.Remove(pin);

    // effective baseline: manual override > startup > default false
    public bool GetEffectiveBaseline(int pin)
    {
        if (_manualOverrides.TryGetValue(pin, out var manual)) return manual;
        if (_startupValues.TryGetValue(pin, out var startup)) return startup;
        return false;
    }

    public bool IsManualOverride(int pin) => _manualOverrides.ContainsKey(pin);
}
