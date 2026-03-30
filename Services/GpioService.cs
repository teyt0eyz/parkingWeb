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
    
    // รายชื่อขาทั้งหมดที่ใช้งาน
    private readonly int[] _pins = { 17, 27, 22, 23, 24 };
    
    // จำว่าตอนนี้ขาไหนทำงานอยู่ (-1 คือไม่มีรถจอด)
    private int _activePin = -1;

    public GpioService(IHubContext<ParkingHub> hub, IServiceScopeFactory scopeFactory)
    {
        _gpio = new GpioController();
        _hub = hub;
        _scopeFactory = scopeFactory;
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
        Console.WriteLine("--- GpioService: Scanning All Pins... ---");

        while (!ct.IsCancellationRequested)
        {
            int triggeredPin = -1;

            // วนลูปเช็คทีละขาว่าขาไหนมีไฟเข้า (High)
            foreach (var pin in _pins)
            {
                if (_gpio.Read(pin) == PinValue.High)
                {
                    triggeredPin = pin;
                    break; // เจอขาแรกที่มีไฟ ก็หยุดเช็คขาอื่นทันที
                }
            }

            // กรณีที่ 1: มีไฟเข้า (และเปลี่ยนสถานะจากเดิม)
            if (triggeredPin != -1 && _activePin != triggeredPin)
            {
                string imagePath = "";
                
                // ดึงชื่อรูปจาก Database ตามขาที่โดนจิ้ม
                using (var scope = _scopeFactory.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var state = db.ParkingStates.FirstOrDefault(s => s.GpioPin == triggeredPin);
                    if (state != null && !string.IsNullOrEmpty(state.ImagePath))
                    {
                        imagePath = state.ImagePath;
                    }
                }

                Console.WriteLine($">>> [DETECTED] GPIO {triggeredPin} is HIGH! Image: {imagePath}");
                await _hub.Clients.All.SendAsync("ReceiveStatus", imagePath, true);
                _activePin = triggeredPin;
            }
            // กรณีที่ 2: ไม่มีไฟเข้าเลยทุกขา (และก่อนหน้านี้มีรถจอดอยู่)
            else if (triggeredPin == -1 && _activePin != -1)
            {
                Console.WriteLine("<<< [CLEARED] All GPIOs are LOW. Back to Idle.");
                await _hub.Clients.All.SendAsync("ReceiveStatus", "", false);
                _activePin = -1;
            }

            await Task.Delay(50); // พัก 0.15 วิ
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _gpio.Dispose();
        return Task.CompletedTask;
    }
}