using Microsoft.AspNetCore.Mvc;
using ParkingWeb.Data;
using ParkingWeb.Models;
using ParkingWeb.Services;

namespace ParkingWeb.Controllers;

public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly LogService _logger;

    public AdminController(ApplicationDbContext db, IWebHostEnvironment env, LogService logger)
    {
        _db = db;
        _env = env;
        _logger = logger;
    }

    // 1. หน้าหลัก Admin
    public IActionResult Index()
    {
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

            // ลบไฟล์เก่า idle_* ทั้งหมด
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
            
           // _logger.SaveLog(0, $"Admin: Updated idle video to {fileName}");
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

            //_logger.SaveLog(id, $"Admin: Changed image for Slot {id} (GPIO {state.GpioPin})");
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
         //   _logger.SaveLog(id, $"Admin: Deleted image for Slot {id}");
        }
        return RedirectToAction("Index");
    }

    // 5. ระบบดูรายการ Log ย้อนหลัง
    public IActionResult ViewLogs()
    {
        string logFolder = Path.Combine(_env.ContentRootPath, "ParkingLogs");
        if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

        var logs = Directory.GetFiles(logFolder, "*.csv")
                            .Select(f => Path.GetFileName(f))
                            .OrderByDescending(f => f)
                            .ToList();
        return View(logs); 
    }

    // 6. ระบบดาวน์โหลดไฟล์ Log มาอ่าน (เพิ่มให้ใหม่)
    public IActionResult DownloadLog(string fileName)
    {
        string filePath = Path.Combine(_env.ContentRootPath, "ParkingLogs", fileName);
        if (!System.IO.File.Exists(filePath)) return NotFound();

        var fileBytes = System.IO.File.ReadAllBytes(filePath);
        return File(fileBytes, "text/csv", fileName);
    }

    // 7. สำหรับทดสอบ Logic การบันทึก Log ผ่าน Web
    [HttpPost]
    public IActionResult DetectCar(int id)
    {
        _logger.SaveLog(id, "Car Arrived (Manual Trigger)");
        return Ok();
    }

    // ฟังก์ชันสำหรับอ่านข้อมูลข้างในไฟล์ Log มาโชว์เป็นตาราง
    public IActionResult LogDetails(string fileName)
    {
        string filePath = Path.Combine(_env.ContentRootPath, "ParkingLogs", fileName);
        
        if (!System.IO.File.Exists(filePath)) return NotFound();

        // อ่านทุกบรรทัดใน CSV
        var lines = System.IO.File.ReadAllLines(filePath).ToList();
        
        ViewBag.FileName = fileName.Replace(".csv", "");
        return View(lines);
    }
}