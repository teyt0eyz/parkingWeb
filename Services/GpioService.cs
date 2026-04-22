using System.Device.Gpio;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.SignalR;
using ParkingWeb.Hubs;
using ParkingWeb.Data;

namespace ParkingWeb.Services;

public class GpioService : IHostedService
{
    private readonly GpioController? _gpio;
    private readonly bool _hasGpio;
    private readonly IHubContext<ParkingHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LogService _logService;
    private readonly GpioBaselineService _baseline;

    private readonly int[] _pins = { 17, 27, 22, 23, 24 };
    private int _activePin = -1;

    public GpioService(IHubContext<ParkingHub> hub, IServiceScopeFactory scopeFactory, LogService logService, GpioBaselineService baseline)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logService = logService;
        _baseline = baseline;

        _hasGpio = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        _gpio = _hasGpio ? new GpioController() : null;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_hasGpio)
        {
            // ไม่ใช่ Linux (Pi) — ข้าม GPIO ทั้งหมด เว็บยังใช้งานได้ปกติ
            return Task.CompletedTask;
        }

        foreach (var pin in _pins)
            _gpio!.OpenPin(pin, PinMode.InputPullDown);

        // อ่านค่า raw ตอนเริ่มต้น → เก็บเป็น startup baseline
        foreach (var pin in _pins)
            _baseline.SetStartupBaseline(pin, _gpio!.Read(pin) == PinValue.High);

        // โหลด manual override ที่บันทึกไว้ใน DB กลับมา (ข้ามรีบูต)
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var pin in _pins)
            {
                var state = db.ParkingStates.FirstOrDefault(s => s.GpioPin == pin);
                if (state?.IdleBaseline.HasValue == true)
                    _baseline.SetManualBaseline(pin, state.IdleBaseline.Value);
            }
        }

        Task.Run(() => ListenToGpio(cancellationToken));
        return Task.CompletedTask;
    }

    private bool IsPinActive(int pin)
    {
        bool currentHigh = _gpio!.Read(pin) == PinValue.High;
        return currentHigh != _baseline.GetEffectiveBaseline(pin);
    }

    private async Task ListenToGpio(CancellationToken ct)
    {
        int rawBroadcastCounter = 0;

        while (!ct.IsCancellationRequested)
        {
            rawBroadcastCounter++;
            if (rawBroadcastCounter >= 7)
            {
                var rawValues = _pins.Select(p => new
                {
                    pin        = p,
                    isHigh     = _gpio!.Read(p) == PinValue.High,
                    idleIsHigh = _baseline.GetEffectiveBaseline(p),
                    isManual   = _baseline.IsManualOverride(p)
                }).ToList();
                await _hub.Clients.All.SendAsync("RawPinValues", rawValues, ct);
                rawBroadcastCounter = 0;
            }

            int triggeredPin = -1;
            foreach (var pin in _pins)
            {
                if (IsPinActive(pin)) { triggeredPin = pin; break; }
            }

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
                await _hub.Clients.All.SendAsync("ReceiveStatus", imagePath, true, triggeredPin);
                _activePin = triggeredPin;
            }
            else if (triggeredPin == -1 && _activePin != -1)
            {
                _logService.SaveLog(_activePin, $"CLEARED: Car departed from Pin {_activePin}. System returned to Idle Video.");
                await _hub.Clients.All.SendAsync("ReceiveStatus", "", false, -1);
                _activePin = -1;
            }

            await Task.Delay(150);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gpio?.Dispose();
        return Task.CompletedTask;
    }
}
