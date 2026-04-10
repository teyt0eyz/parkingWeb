using Microsoft.AspNetCore.Mvc;
using ParkingWeb.Data;
using Microsoft.AspNetCore.SignalR;
using ParkingWeb.Hubs;
using ParkingWeb.Models;
using ParkingWeb.Services;
using System.Runtime.InteropServices;

namespace ParkingWeb.Controllers;

public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly LogService _logger;
    private readonly IHubContext<ParkingHub> _hubContext;
    public AdminController(ApplicationDbContext db, IWebHostEnvironment env, LogService logger,IHubContext<ParkingHub> hubContext)
    {
        _db = db;
        _env = env;
        _logger = logger;
        _hubContext = hubContext;
    }

    // 1. หน้าหลัก Admin + ระบบคำนวณพื้นที่ Disk (SD Card)
    public IActionResult Index()
    {
        try
        {
            // เช็ค OS: ถ้าเป็น Linux (Pi) ให้ดูที่ "/" ถ้าเป็น Windows ให้ดูที่ Drive "C"
            string drivePath = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/" : "C";
            var drive = new DriveInfo(drivePath);

            double totalSize = drive.TotalSize;
            double freeSpace = drive.AvailableFreeSpace;
            double usedSpace = totalSize - freeSpace;

            // ส่งค่าไปโชว์ที่หน้า View (หน่วยเป็น GB)
            ViewBag.FreeSpace = (freeSpace / (1024 * 1024 * 1024)).ToString("F2");
            ViewBag.TotalSpace = (totalSize / (1024 * 1024 * 1024)).ToString("F2");
            ViewBag.UsedPercent = ((usedSpace / totalSize) * 100).ToString("F0");
        }
        catch
        {
            ViewBag.FreeSpace = "0";
            ViewBag.TotalSpace = "0";
            ViewBag.UsedPercent = "0";
        }

        var states = _db.ParkingStates.ToList();
        return View(states);
    }

    // 2. อัปเดตวิดีโอตอนไม่มีรถ (Idle Video)
    [HttpPost]
    public async Task<IActionResult> UpdateIdleVideo(IFormFile videoFile)
    {
        if (videoFile != null && videoFile.Length > 0)
        {
            string uploadDir = Path.Combine(_env.WebRootPath, "videos");
            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

            // บันทึกเป็น idle.mp4 เสมอ เพื่อให้ Home/index โหลดได้ทันที
            string path = Path.Combine(uploadDir, "idle.mp4");
            using (var stream = new FileStream(path, FileMode.Create))
            {
                await videoFile.CopyToAsync(stream);
            }
        }
        return RedirectToAction("Index");
    }

    // 3. อัปเดตรูปภาพสถานะช่องจอด
    [HttpPost]
    public async Task<IActionResult> UpdateStateImage(int id, IFormFile imageFile)
    {
        var state = _db.ParkingStates.Find(id);
        if (state != null && imageFile != null && imageFile.Length > 0)
        {
            if (!string.IsNullOrEmpty(state.ImagePath))
            {
                string oldPath = Path.Combine(_env.WebRootPath, "uploads", state.ImagePath);
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }

            string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
            string path = Path.Combine(_env.WebRootPath, "uploads", fileName);
            
            if (!Directory.Exists(Path.Combine(_env.WebRootPath, "uploads"))) 
                Directory.CreateDirectory(Path.Combine(_env.WebRootPath, "uploads"));

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await imageFile.CopyToAsync(stream);
            }

            state.ImagePath = fileName;
            _db.SaveChanges();
        }
        return RedirectToAction("Index");
    }

    // 4. ลบรูปภาพสถานะ
    [HttpPost]
    public IActionResult DeleteStateImage(int id)
    {
        var state = _db.ParkingStates.Find(id);
        if (state != null && !string.IsNullOrEmpty(state.ImagePath))
        {
            string path = Path.Combine(_env.WebRootPath, "uploads", state.ImagePath);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

            state.ImagePath = ""; 
            _db.SaveChanges();
        }
        return RedirectToAction("Index");
    }
    // เพิ่มฟังก์ชันสำหรับบันทึกค่าการหมุน (ถ้าต้องการเก็บลง DB)
    [HttpPost]
    public async Task<IActionResult> SaveRotation(int degree)
    {
        // ยิง SignalR ไปหาทุกเครื่อง (รวมถึง Kiosk) ให้หมุนตามองศาที่ส่งมา
        await _hubContext.Clients.All.SendAsync("RotateScreen", degree);
        
        return Ok(new { degree = degree });
    }
    
    // 5. ระบบดูรายการ Log ย้อนหลัง (เก็บส่วนคำนวณพื้นที่ไว้ครบถ้วน)
    public IActionResult ViewLogs()
    {
        string logFolder = Path.Combine(_env.ContentRootPath, "ParkingLogs");
        if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

        var files = Directory.GetFiles(logFolder, "*.csv");
        
        // 1. คำนวณพื้นที่ Log รวม (ของเดิม Toey)
        long totalLogBytes = files.Select(f => new FileInfo(f).Length).Sum();
        ViewBag.TotalLogSize = (totalLogBytes / (1024.0 * 1024.0)).ToString("F2") + " MB";

        // 2. คำนวณพื้นที่ SD Card ทั้งหมด (ของเดิม Toey)
        string drivePath = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "/" : "C";
        var drive = new DriveInfo(drivePath);
        ViewBag.TotalDriveSize = (drive.TotalSize / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB";

        // 3. ส่ง Model เป็น "ชื่อไฟล์|ขนาดไบต์"
        var logs = files.Select(f => $"{Path.GetFileName(f)}|{new FileInfo(f).Length}")
                        .OrderByDescending(f => f)
                        .ToList();
                            
        return View(logs); 
    }

    // 6. ระบบดาวน์โหลดไฟล์ Log
    public IActionResult DownloadLog(string fileName)
    {
        string filePath = Path.Combine(_env.ContentRootPath, "ParkingLogs", fileName);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        return File(System.IO.File.ReadAllBytes(filePath), "text/csv", fileName);
    }


    // 7. ระบบดูรายละเอียด Log ข้างในไฟล์ (แสดงเป็นตาราง)
    [HttpGet]
    public IActionResult LogDetails(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return RedirectToAction("ViewLogs");

        // ตรวจสอบนามสกุล
        string fullFileName = fileName.EndsWith(".csv") ? fileName : fileName + ".csv";
        string filePath = Path.Combine(_env.ContentRootPath, "ParkingLogs", fullFileName);

        if (!System.IO.File.Exists(filePath))
        {
            TempData["Error"] = $"หาไฟล์ไม่เจอที่: {filePath}";
            return RedirectToAction("ViewLogs");
        }

        try 
        {
            // อ่านไฟล์ด้วย UTF8 ป้องกันภาษาไทยพัง
            var lines = System.IO.File.ReadAllLines(filePath, System.Text.Encoding.UTF8).ToList();
            ViewBag.FileName = fullFileName.Replace(".csv", "");
            return View(lines);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"เกิดข้อผิดพลาดในการอ่านไฟล์: {ex.Message}";
            return RedirectToAction("ViewLogs");
        }
    }

    // 1. หน้า Dev Console
    public IActionResult Dev()
    {
        var states = _db.ParkingStates.ToList();
        return View(states);
    }

    // 2. ระบบสั่งงาน GPIO จริง (Manual Trigger)
    [HttpPost]
    public async Task<IActionResult> ManualTrigger(int pin, bool isActive)
    {
        try
        {
            // 🚀 สั่งบันทึก Log
            string status = isActive ? "รถเข้าจอด (Manual)" : "รถออก (Manual)";
            _logger.SaveLog(pin, status);

            // 🚀 ดึงข้อมูลภาพจาก DB
            var state = _db.ParkingStates.FirstOrDefault(s => s.GpioPin == pin);
            string imagePath = state?.ImagePath ?? "";

            // 🚀 ส่ง SignalR บังคับหน้า Kiosk ให้เปลี่ยนภาพทันที
            await _hubContext.Clients.All.SendAsync("ReceiveStatus", imagePath, isActive, pin);

            return Ok(new { success = true, pin = pin, state = isActive });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}