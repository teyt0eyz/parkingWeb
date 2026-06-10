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
    private readonly GpioBaselineService _baselineSvc;
    private readonly SimulatorModeService _simulatorSvc;
    private readonly PinPriorityService _pinPrioritySvc;

    // เก็บ mute state ไว้บน server ให้ทุกหน้าดึงได้
    private static bool _kioskMuted = false;
    private static bool _debugMode  = false;

    public AdminController(ApplicationDbContext db, IWebHostEnvironment env, LogService logger, IHubContext<ParkingHub> hubContext, GpioBaselineService baselineSvc, SimulatorModeService simulatorSvc, PinPriorityService pinPrioritySvc)
    {
        _db = db;
        _env = env;
        _logger = logger;
        _hubContext = hubContext;
        _baselineSvc = baselineSvc;
        _simulatorSvc = simulatorSvc;
        _pinPrioritySvc = pinPrioritySvc;
    }

    // 1. หน้าหลัก Admin + ระบบคำนวณพื้นที่ Disk (SD Card)
    public IActionResult Index()
    {
        try
        {
            // เช็ค OS: ถ้าเป็น Linux (Pi) ให้ดูที่ "/" ถ้าเป็น Windows ให้ดูที่ Drive "C"
            string drivePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "C" : "/";
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

        // คำนวณขนาด Log files
        try
        {
            string logFolder = Path.Combine(_env.ContentRootPath, "ParkingLogs");
            if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);
            var logFiles = Directory.GetFiles(logFolder, "*.csv");
            long totalLogBytes = logFiles.Select(f => new FileInfo(f).Length).Sum();
            double totalLogMB = totalLogBytes / (1024.0 * 1024.0);

            double totalDriveGB = double.Parse(ViewBag.TotalSpace.ToString());
            double usedDriveGB  = totalDriveGB - double.Parse(ViewBag.FreeSpace.ToString());
            double logPct = totalDriveGB > 0 ? (totalLogMB / (totalDriveGB * 1024.0)) * 100.0 : 0;

            ViewBag.TotalLogSize = totalLogMB.ToString("F2");
            ViewBag.UsedDriveGB  = usedDriveGB.ToString("F1");
            ViewBag.LogUsedPct   = logPct.ToString("F2");
        }
        catch
        {
            ViewBag.TotalLogSize = "0.00";
            ViewBag.UsedDriveGB  = "0.0";
            ViewBag.LogUsedPct   = "0.00";
        }

        // อ่านค่าหมุนหน้าจอที่บันทึกไว้
        int rotation = 0;
        string rotFile = Path.Combine(_env.ContentRootPath, "rotation.dat");
        if (System.IO.File.Exists(rotFile) &&
            int.TryParse(System.IO.File.ReadAllText(rotFile).Trim(), out int savedRot))
            rotation = savedRot;
        ViewBag.ScreenRotation = rotation;

        // จำกัด video upload = ครึ่งหนึ่งของพื้นที่ว่าง (หน่วย bytes ส่งไป JS)
        try
        {
            string drivePath2 = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "C" : "/";
            long freeBytes = new DriveInfo(drivePath2).AvailableFreeSpace;
            ViewBag.MaxVideoBytes = freeBytes / 2;
        }
        catch { ViewBag.MaxVideoBytes = 200L * 1024 * 1024; }

        var states = _db.ParkingStates.ToList();
        return View(states);
    }

    // 2. อัปเดตวิดีโอตอนไม่มีรถ (Idle Video)
    [HttpPost]
    public async Task<IActionResult> UpdateIdleVideo(IFormFile videoFile)
    {
        // จำกัด = ครึ่งหนึ่งของพื้นที่ว่างตอนนี้
        long maxVideoBytes;
        try
        {
            string dp = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "C" : "/";
            maxVideoBytes = new DriveInfo(dp).AvailableFreeSpace / 2;
        }
        catch { maxVideoBytes = 200L * 1024 * 1024; }

        if (videoFile == null || videoFile.Length == 0)
        {
            _logger.SaveSystemLog("Video upload rejected — no file");
            TempData["UploadError"] = "กรุณาเลือกไฟล์วิดีโอ";
            return RedirectToAction("Index");
        }
        if (videoFile.Length > maxVideoBytes)
        {
            _logger.SaveSystemLog($"Video upload rejected — file too large: {videoFile.Length / 1024 / 1024} MB (limit: {maxVideoBytes / 1024 / 1024} MB)");
            TempData["UploadError"] = $"ไฟล์วิดีโอใหญ่เกินขนาดที่อนุญาต (ขนาดไฟล์: {videoFile.Length / 1024 / 1024} MB)";
            return RedirectToAction("Index");
        }
        var allowedVideoTypes = new[] { "video/mp4", "video/mpeg" };
        if (!allowedVideoTypes.Contains(videoFile.ContentType.ToLower()))
        {
            _logger.SaveSystemLog($"Video upload rejected — invalid type: {videoFile.ContentType}");
            TempData["UploadError"] = "รองรับเฉพาะไฟล์ MP4 เท่านั้น";
            return RedirectToAction("Index");
        }

        string uploadDir = Path.Combine(_env.WebRootPath, "videos");
        if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

        string path = Path.Combine(uploadDir, "idle.mp4");
        using (var stream = new FileStream(path, FileMode.Create))
        {
            await videoFile.CopyToAsync(stream);
        }

        _logger.SaveSystemLog($"Idle video updated — size: {videoFile.Length / 1024 / 1024} MB, file: {videoFile.FileName}");
        await _hubContext.Clients.All.SendAsync("ReloadPage");
        return RedirectToAction("Index");
    }

    // 2b. ลบวิดีโอ Idle ปัจจุบัน
    [HttpPost]
    public async Task<IActionResult> RemoveIdleVideo()
    {
        string path = Path.Combine(_env.WebRootPath, "videos", "idle.mp4");
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
            _logger.SaveSystemLog("Idle video removed");
            await _hubContext.Clients.All.SendAsync("ReloadPage");
        }
        return RedirectToAction("Index");
    }

    // 3. อัปเดตรูปภาพสถานะช่องจอด
    [HttpPost]
    public async Task<IActionResult> UpdateStateImage(int id, IFormFile imageFile)
    {
        const long MaxImageBytes = 10L * 1024 * 1024; // 10 MB

        if (imageFile == null || imageFile.Length == 0)
        {
            TempData["UploadError"] = "กรุณาเลือกไฟล์รูปภาพ";
            return RedirectToAction("Index");
        }
        if (imageFile.Length > MaxImageBytes)
        {
            TempData["UploadError"] = $"ไฟล์รูปภาพใหญ่เกิน 10 MB (ขนาดไฟล์: {imageFile.Length / 1024 / 1024} MB)";
            return RedirectToAction("Index");
        }
        var allowedImageTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp" };
        if (!allowedImageTypes.Contains(imageFile.ContentType.ToLower()))
        {
            TempData["UploadError"] = "รองรับเฉพาะไฟล์รูปภาพ (JPEG, PNG, GIF, WEBP, BMP)";
            return RedirectToAction("Index");
        }

        var state = _db.ParkingStates.Find(id);
        if (state == null) return RedirectToAction("Index");

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
        _logger.SaveSystemLog($"State image updated — GPIO Pin {state.GpioPin}, file: {imageFile.FileName} ({imageFile.Length / 1024} KB)");
        return RedirectToAction("Index");
    }

    // 3a. อัปโหลดรูปภาพสถานะหลายช่องพร้อมกัน (Bulk upload) — แต่ละไฟล์ส่งมาเป็น "imageFile_{id}"
    [HttpPost]
    public async Task<IActionResult> UpdateAllStateImages()
    {
        const long MaxImageBytes = 10L * 1024 * 1024; // 10 MB
        var allowedImageTypes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "image/bmp" };

        string uploadDir = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadDir)) Directory.CreateDirectory(uploadDir);

        int updated = 0;
        var errors = new List<string>();

        foreach (var file in Request.Form.Files)
        {
            if (file.Length == 0 || !file.Name.StartsWith("imageFile_")) continue;
            if (!int.TryParse(file.Name.Substring("imageFile_".Length), out int id)) continue;

            var state = _db.ParkingStates.Find(id);
            if (state == null) continue;

            if (file.Length > MaxImageBytes)
            {
                errors.Add($"GPIO {state.GpioPin}: ไฟล์ใหญ่เกิน 10 MB ({file.Length / 1024 / 1024} MB)");
                continue;
            }
            if (!allowedImageTypes.Contains(file.ContentType.ToLower()))
            {
                errors.Add($"GPIO {state.GpioPin}: รองรับเฉพาะไฟล์รูปภาพ (JPEG, PNG, GIF, WEBP, BMP)");
                continue;
            }

            if (!string.IsNullOrEmpty(state.ImagePath))
            {
                string oldPath = Path.Combine(uploadDir, state.ImagePath);
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }

            string fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
            using (var stream = new FileStream(Path.Combine(uploadDir, fileName), FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            state.ImagePath = fileName;
            updated++;
        }

        if (updated > 0) _db.SaveChanges();
        if (errors.Count > 0) TempData["UploadError"] = string.Join(" | ", errors);

        _logger.SaveSystemLog($"Bulk state image upload — {updated} image(s) updated" + (errors.Count > 0 ? $", {errors.Count} skipped" : ""));
        return RedirectToAction("Index");
    }

    // 3b. ตั้งค่า idle baseline ของ pin แบบ manual (override auto-detect) + บันทึกลง DB
    [HttpPost]
    public IActionResult SetBaseline(int pin, bool idleIsHigh)
    {
        _baselineSvc.SetManualBaseline(pin, idleIsHigh);

        var state = _db.ParkingStates.FirstOrDefault(s => s.GpioPin == pin);
        if (state != null)
        {
            state.IdleBaseline = idleIsHigh;
            _db.SaveChanges();
        }

        _logger.SaveSystemLog($"GPIO baseline set manually — Pin{pin} idle={(idleIsHigh ? "HIGH" : "LOW")}");
        return Ok(new { pin, idleIsHigh });
    }

    // 3c. ล้าง manual override กลับไปใช้ค่า startup + ล้างจาก DB
    [HttpPost]
    public IActionResult ClearBaseline(int pin)
    {
        _baselineSvc.ClearManualBaseline(pin);

        var state = _db.ParkingStates.FirstOrDefault(s => s.GpioPin == pin);
        if (state != null)
        {
            state.IdleBaseline = null;
            _db.SaveChanges();
        }

        _logger.SaveSystemLog($"GPIO baseline cleared — Pin{pin} returned to auto-detect");
        return Ok(new { pin });
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

    // 4b. ลบรูปภาพสถานะทุกช่องพร้อมกัน (Bulk remove)
    [HttpPost]
    public IActionResult DeleteAllStateImages()
    {
        var states = _db.ParkingStates.Where(s => s.ImagePath != null && s.ImagePath != "").ToList();
        foreach (var state in states)
        {
            string path = Path.Combine(_env.WebRootPath, "uploads", state.ImagePath);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            state.ImagePath = "";
        }

        if (states.Count > 0) _db.SaveChanges();
        _logger.SaveSystemLog($"Bulk state image removal — {states.Count} image(s) removed");
        return RedirectToAction("Index");
    }
    // เพิ่มฟังก์ชันสำหรับบันทึกค่าการหมุน (ถ้าต้องการเก็บลง DB)
    [HttpPost]
    public async Task<IActionResult> SaveRotation(int degree)
    {
        // บันทึกลงไฟล์เพื่อให้จำข้ามรีบูต
        string rotFile = Path.Combine(_env.ContentRootPath, "rotation.dat");
        await System.IO.File.WriteAllTextAsync(rotFile, degree.ToString());

        _logger.SaveSystemLog($"Screen rotation set to {degree}°");
        await _hubContext.Clients.All.SendAsync("RotateScreen", degree);
        return Ok(new { degree = degree });
    }

    [HttpGet]
    public IActionResult GetRotation()
    {
        string rotFile = Path.Combine(_env.ContentRootPath, "rotation.dat");
        int degree = 0;
        if (System.IO.File.Exists(rotFile) &&
            int.TryParse(System.IO.File.ReadAllText(rotFile).Trim(), out int saved))
            degree = saved;
        return Ok(new { degree });
    }

    [HttpPost]
    public async Task<IActionResult> SetMute(bool muted)
    {
        _kioskMuted = muted;
        _logger.SaveSystemLog($"Kiosk mute set to {muted}");
        await _hubContext.Clients.All.SendAsync("SetMute", muted);
        return Ok(new { muted = muted });
    }

    [HttpGet]
    public IActionResult GetMuteState()
    {
        return Ok(new { muted = _kioskMuted });
    }

    [HttpPost]
    public async Task<IActionResult> SetDebugMode(bool enabled)
    {
        _debugMode = enabled;
        _logger.SaveSystemLog($"Kiosk debug panel set to {(enabled ? "ON" : "OFF")}");
        await _hubContext.Clients.All.SendAsync("SetDebugMode", enabled);
        return Ok(new { enabled });
    }

    [HttpGet]
    public IActionResult GetDebugMode()
    {
        return Ok(new { enabled = _debugMode });
    }

    // ระบบสลับโหมด Simulator (สั่งภาพเอง) / Normal (รอสัญญาณ GPIO จริง)
    [HttpPost]
    public async Task<IActionResult> SetSimulatorMode(bool enabled)
    {
        _simulatorSvc.SetEnabled(enabled);
        _logger.SaveSystemLog($"Simulator mode set to {(enabled ? "ON" : "OFF")}");
        await _hubContext.Clients.All.SendAsync("SetSimulatorMode", enabled);
        return Ok(new { enabled });
    }

    [HttpGet]
    public IActionResult GetSimulatorMode()
    {
        return Ok(new { enabled = _simulatorSvc.Enabled });
    }

    [HttpGet]
    public IActionResult GetPinPriority()
    {
        return Ok(new { order = _pinPrioritySvc.GetOrder() });
    }

    // ตั้งลำดับ priority ของขา GPIO ใหม่ — กำหนดทั้งขาที่ชนะเมื่อมีรถพร้อมกันหลายขา
    // และลำดับ z-index ของเลเยอร์ภาพบนหน้า Kiosk/Dev
    [HttpPost]
    public async Task<IActionResult> SetPinPriority([FromBody] int[] order)
    {
        if (!_pinPrioritySvc.SetOrder(order))
            return BadRequest(new { success = false, message = "Invalid pin order" });

        _logger.SaveSystemLog($"GPIO layer priority updated: {string.Join(", ", order)}");
        await _hubContext.Clients.All.SendAsync("ReloadPage");
        return Ok(new { success = true, order });
    }


    // 5. ระบบดูรายการ Log ย้อนหลัง (เก็บส่วนคำนวณพื้นที่ไว้ครบถ้วน)
    public IActionResult ViewLogs()
    {
        string logFolder = Path.Combine(_env.ContentRootPath, "ParkingLogs");
        if (!Directory.Exists(logFolder)) Directory.CreateDirectory(logFolder);

        var files = Directory.GetFiles(logFolder, "*.csv");
        
        // 1. คำนวณพื้นที่ Log รวม
        long totalLogBytes = files.Select(f => new FileInfo(f).Length).Sum();
        double totalLogMB = totalLogBytes / (1024.0 * 1024.0);
        ViewBag.TotalLogSize = totalLogMB.ToString("F2");

        // 2. คำนวณพื้นที่ SD Card ทั้งหมด
        string drivePath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "C" : "/";
        var drive = new DriveInfo(drivePath);
        double totalDriveGB = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
        double freeSpaceGB  = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
        double usedSpaceGB  = totalDriveGB - freeSpaceGB;
        double driveUsedPct = totalDriveGB > 0 ? (usedSpaceGB / totalDriveGB) * 100.0 : 0;
        double logPct       = totalDriveGB > 0 ? (totalLogMB / (totalDriveGB * 1024.0)) * 100.0 : 0;

        ViewBag.TotalDriveGB  = totalDriveGB.ToString("F1");
        ViewBag.UsedDriveGB   = usedSpaceGB.ToString("F1");
        ViewBag.FreeSpaceGB   = freeSpaceGB.ToString("F1");
        ViewBag.DriveUsedPct  = driveUsedPct.ToString("F1");
        ViewBag.LogUsedPct    = logPct.ToString("F2");

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
        ViewBag.PinPriority = _pinPrioritySvc.GetOrder();
        return View(states);
    }

    // 2. ระบบสั่งงาน GPIO จริง (Manual Trigger)
    [HttpPost]
    public async Task<IActionResult> ManualTrigger(int pin, bool isActive)
    {
        try
        {
            // โหมด Normal → ไม่ให้ปุ่มจำลองสั่งเปลี่ยนภาพ ปล่อยให้รอสัญญาณ GPIO จริงเท่านั้น
            if (!_simulatorSvc.Enabled)
                return Ok(new { success = true, ignored = true, pin, isActive });

            // isActive = ค่า signal ที่จำลอง (true=HIGH, false=LOW)
            // hasCar = signal ต่างจาก idle baseline → triggered
            bool isHigh  = isActive;
            bool idleHigh = _baselineSvc.GetEffectiveBaseline(pin);
            bool hasCar  = isHigh != idleHigh;

            // 🚀 สั่งบันทึก Log
            string sigLabel = isHigh ? "HIGH" : "LOW";
            string status = hasCar
                ? $"รถเข้าจอด (Manual Sim: {sigLabel})"
                : $"ไม่มีรถ (Manual Sim: {sigLabel})";
            _logger.SaveLog(pin, status);

            // 🚀 ดึงข้อมูลภาพจาก DB (ใช้เฉพาะตอนมีรถ)
            string imagePath = "";
            if (hasCar)
            {
                var state = _db.ParkingStates.FirstOrDefault(s => s.GpioPin == pin);
                imagePath = state?.ImagePath ?? "";
            }

            // 🚀 ส่ง SignalR บังคับหน้า Kiosk ให้เปลี่ยนภาพทันที
            await _hubContext.Clients.All.SendAsync("ReceiveStatus", imagePath, hasCar, pin);

            return Ok(new { success = true, ignored = false, pin, isHigh, idleHigh, hasCar });
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}