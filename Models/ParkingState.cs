namespace ParkingWeb.Models;

public class ParkingState
{
    public int Id { get; set; }
    public string StateName { get; set; } = ""; // เช่น "ถอยหลัง", "หยุด"
    public string ImagePath { get; set; } = ""; // ชื่อไฟล์รูปภาพ
    public int GpioPin { get; set; }            // เลขขาบน Pi (17, 27...)
}