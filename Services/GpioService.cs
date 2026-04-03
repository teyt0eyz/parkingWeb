using System.Device.Gpio;
using Microsoft.AspNetCore.SignalR;
using ParkingWeb.Hubs;
using ParkingWeb.Data;
using ParkingWeb.Services; // มั่นใจว่ามี using ตัวนี้

namespace ParkingWeb.Services;

public class GpioService : IHostedService
{
    private readonly GpioController _gpio;
    private readonly IHubContext<ParkingHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LogService _logService; // <--- เพิ่มตัวแปร LogService
    
    private readonly int[] _pins = { 17, 27, 22, 23, 24 };
    private int _activePin = -1;

    // แก้ไข Constructor ให้รับ LogService เข้ามาด้วย
    public GpioService(IHubContext<ParkingHub> hub, IServiceScopeFactory scopeFactory, LogService logService)
    {
        _gpio = new GpioController();
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logService = logService; // รับค่ามาใช้งาน
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var pin in _pins)
        {
            _gpio.OpenPin(pin, PinMode.InputPullDown);
        }
        Task.Run(() => ListenToGpio(cancellationToken));
        return Task.CompletedTask;
    }

    private async Task ListenToGpio(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int triggeredPin = -1;

            foreach (var pin in _pins)
            {
                if (_gpio.Read(pin) == PinValue.High)
                {
                    triggeredPin = pin;
                    break;
                }
            }

            // กรณีที่ 1: มีรถมาจอด (GPIO ขานั้นเป็น High และสถานะเปลี่ยน)
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
                        
                        // ✅ [LOGGING] บันทึกเมื่อมีรถมาจอดและรูปเปลี่ยน
                        _logService.SaveLog(triggeredPin, $"DETECTED: Car parked at Pin {triggeredPin} (State: {state.StateName})");
                    }
                }

                await _hub.Clients.All.SendAsync("ReceiveStatus", imagePath, true);
                _activePin = triggeredPin;
            }
            // กรณีที่ 2: รถออก (กลับสู่หน้า Video ปกติ)
            else if (triggeredPin == -1 && _activePin != -1)
            {
                // ✅ [LOGGING] บันทึกเมื่อรถออก (เพื่อให้รู้ว่าจอดนานแค่ไหน)
                _logService.SaveLog(_activePin, $"CLEARED: Car departed from Pin {_activePin}. System returned to Idle Video.");

                await _hub.Clients.All.SendAsync("ReceiveStatus", "", false);
                _activePin = -1;
            }

            await Task.Delay(150); // พัก 0.15 วินาที
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gpio.Dispose();
        return Task.CompletedTask;
    }
}