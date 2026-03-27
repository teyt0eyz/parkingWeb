using Microsoft.AspNetCore.SignalR;

namespace ParkingWeb.Hubs;

public class ParkingHub : Hub
{
    // ฟังก์ชันนี้จะถูกเรียกเมื่อมีการอัปเดตสถานะ (เช่น GPIO เปลี่ยน หรือเรากดปุ่มทดสอบ)
    public async Task UpdateStatus(string imagePath, bool hasCar)
    {
        // ส่งข้อมูลไปยัง "ทุกเครื่อง" ที่เปิดหน้าจอ Home อยู่
        await Clients.All.SendAsync("ReceiveStatus", imagePath, hasCar);
    }
}