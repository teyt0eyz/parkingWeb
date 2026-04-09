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
            string fileName = "idle_" + Guid.NewGuid().ToString().Substring(0, 8) + Path.GetExtension(videoFile.FileName);
            string uploadDir = Path.Combine(_env.WebRootPath, "videos");
            if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

            // ลบไฟล์เก่าทิ้งก่อนเพื่อประหยัดที่
            var oldFiles = Directory.GetFiles(uploadDir, "idle_*");
            foreach (var oldFile in oldFiles)
            {
                try { System.IO.File.Delete(oldFile); } catch { }
            }

            string path = Path.Combine(uploadDir, fileName);
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
    public IActionResult SaveRotation(int degree)
    {
        // ในที่นี้เราจะใช้ Client-side storage (LocalStorage) เป็นหลัก 
        // เพราะมันจำค่าแยกตาม Browser ได้ดี
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
    [HttpPost]
    public async Task<IActionResult> TestLog(int pin, string status)
    {
        try
        {
            // 1. บันทึกลง Log CSV
            _logger.SaveLog(pin, status);

            // 2. (แถม) ส่ง SignalR ไปอัปเดตหน้าจอ Kiosk และหน้า Admin Monitor ด้วย
            // หาข้อมูลรูปภาพจาก DB ตาม Pin ที่กด
            var state = _db.ParkingStates.FirstOrDefault(s => s.GpioPin == pin);
            string imagePath = state?.ImagePath ?? "";
            bool hasCar = status.Contains("Parked") || status.Contains("เข้าจอด");

            await _hubContext.Clients.All.SendAsync("ReceiveStatus", imagePath, hasCar, pin);

            return Ok(new { message = $"Test log for PIN {pin} saved!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}