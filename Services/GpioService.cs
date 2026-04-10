using System.Device.Gpio;
using Microsoft.AspNetCore.SignalR;
using ParkingWeb.Hubs;
using ParkingWeb.Data;
namespace ParkingWeb.Services;

public class GpioService : IHostedService
{
    private readonly GpioController _gpio;
    private readonly IHubContext<ParkingHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LogService _logService;

    private readonly int[] _pins = { 17, 27, 22, 23, 24 };
    private int _activePin = -1;

    // ค่า baseline ตอนไม่มีรถ (อ่านตอน startup)
    // key = GpioPin, value = isHigh ตอน idle
    private readonly Dictionary<int, bool> _idleValues = [];

    public GpioService(IHubContext<ParkingHub> hub, IServiceScopeFactory scopeFactory, LogService logService)
    {
        _gpio = new GpioController();
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logService = logService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var pin in _pins)
        {
            _gpio.OpenPin(pin, PinMode.InputPullDown);
        }

        // อ่านค่า raw ตอนเริ่มต้น — ถือว่าตอนนี้ไม่มีรถ (idle baseline)
        CaptureIdleValues();

        Task.Run(() => ListenToGpio(cancellationToken));
        return Task.CompletedTask;
    }

    // อ่านค่าปัจจุบันของทุก pin แล้วเก็บเป็น "idle state"
    private void CaptureIdleValues()
    {
        foreach (var pin in _pins)
        {
            _idleValues[pin] = _gpio.Read(pin) == PinValue.High;
        }
    }

    // pin "active" (มีรถ) = ค่าปัจจุบันต่างจาก baseline
    private bool IsPinActive(int pin)
    {
        bool currentHigh = _gpio.Read(pin) == PinValue.High;
        bool idleHigh    = _idleValues.TryGetValue(pin, out var v) && v;
        return currentHigh != idleHigh;
    }

    private async Task ListenToGpio(CancellationToken ct)
    {
        int rawBroadcastCounter = 0;

        while (!ct.IsCancellationRequested)
        {
            // Broadcast ค่าดิบทุก ~1 วินาที (1000ms / 150ms ≈ 7 รอบ)
            rawBroadcastCounter++;
            if (rawBroadcastCounter >= 7)
            {
                var rawValues = _pins.Select(p => new
                {
                    pin        = p,
                    isHigh     = _gpio.Read(p) == PinValue.High,
                    idleIsHigh = _idleValues.TryGetValue(p, out var iv) && iv
                }).ToList();
                await _hub.Clients.All.SendAsync("RawPinValues", rawValues);
                rawBroadcastCounter = 0;
            }

            int triggeredPin = -1;

            foreach (var pin in _pins)
            {
                if (IsPinActive(pin))
                {
                    triggeredPin = pin;
                    break;
                }
            }

            // กรณีที่ 1: มีรถมาจอด (pin active และสถานะเปลี่ยน)
            if (triggeredPin != -1 && _activePin != triggeredPin)
            {
                string imagePath = "";
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var state = db.ParkingStates.FirstOrDefault(s => s.GpioPin == triggeredPin);
                    if (state != null)
                    {
                        imagePath = state.ImagePath;
                        _logService.SaveLog(triggeredPin, $"DETECTED: Car parked at Pin {triggeredPin} (State: {state.StateName})");
                    }
                }

                await _hub.Clients.All.SendAsync("ReceiveStatus", imagePath, true);
                _activePin = triggeredPin;
            }
            // กรณีที่ 2: รถออก (กลับสู่หน้า Video ปกติ)
            else if (triggeredPin == -1 && _activePin != -1)
            {
                _logService.SaveLog(_activePin, $"CLEARED: Car departed from Pin {_activePin}. System returned to Idle Video.");

                await _hub.Clients.All.SendAsync("ReceiveStatus", "", false);
                _activePin = -1;
            }

            await Task.Delay(150);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gpio.Dispose();
        return Task.CompletedTask;
    }
}
