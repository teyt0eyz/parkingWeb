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
    private readonly SimulatorModeService _simulator;

    private readonly int[] _pins = { 17, 27, 22, 23, 24 };
    private int _activePin = -1;

    public GpioService(IHubContext<ParkingHub> hub, IServiceScopeFactory scopeFactory, LogService logService, GpioBaselineService baseline, SimulatorModeService simulator)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logService = logService;
        _baseline = baseline;
        _simulator = simulator;

        _hasGpio = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        _gpio = _hasGpio ? new GpioController() : null;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_hasGpio)
        {
            _logService.SaveSystemLog("GpioService started (non-Linux — GPIO skipped)");
            return Task.CompletedTask;
        }

        foreach (var pin in _pins)
            _gpio!.OpenPin(pin, PinMode.InputPullDown);

        var startupBaselines = new List<string>();
        foreach (var pin in _pins)
        {
            bool isHigh = _gpio!.Read(pin) == PinValue.High;
            _baseline.SetStartupBaseline(pin, isHigh);
            startupBaselines.Add($"Pin{pin}={( isHigh ? "H" : "L")}");
        }
        _logService.SaveSystemLog($"GpioService started — pins: {string.Join(", ", _pins)} | startup baseline: {string.Join(", ", startupBaselines)}");

        int manualCount = 0;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var pin in _pins)
            {
                var state = db.ParkingStates.FirstOrDefault(s => s.GpioPin == pin);
                if (state?.IdleBaseline.HasValue == true)
                {
                    _baseline.SetManualBaseline(pin, state.IdleBaseline.Value);
                    manualCount++;
                    _logService.SaveSystemLog($"Baseline override loaded from DB — Pin{pin}={( state.IdleBaseline.Value ? "H" : "L")}");
                }
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

            // กลับมาโหมด Normal → บังคับ sync สถานะจากขา GPIO จริงในรอบนี้ทันที
            if (_simulator.ConsumeResyncRequest())
                _activePin = int.MinValue;

            if (!_simulator.Enabled)
            {
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
            }

            await Task.Delay(150);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logService.SaveSystemLog("GpioService stopped");
        _gpio?.Dispose();
        return Task.CompletedTask;
    }
}
