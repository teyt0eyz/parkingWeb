using Microsoft.AspNetCore.Mvc;
using ParkingWeb.Data;
using ParkingWeb.Services;
using System.IO;

public class HomeController : Controller
{
    private readonly IWebHostEnvironment _environment;
    private readonly ApplicationDbContext _db;
    private readonly PinPriorityService _pinPrioritySvc;

    public HomeController(IWebHostEnvironment environment, ApplicationDbContext db, PinPriorityService pinPrioritySvc)
    {
        _environment = environment;
        _db = db;
        _pinPrioritySvc = pinPrioritySvc;
    }

    // หน้าจอแสดงผลสำหรับคนขับ (และหน้าอัปโหลดในตัวเพื่อความง่าย)
    public IActionResult Index()
    {
        // ดึงรายชื่อรูปภาพในโฟลเดอร์ uploads มาแสดง
        var path = Path.Combine(_environment.WebRootPath, "uploads");
        var images = Directory.Exists(path)
                     ? Directory.GetFiles(path).Select(Path.GetFileName).ToList()
                     : new List<string?>();

        ViewBag.Images = images;

        // สถานะ GPIO ทั้งหมด ใช้สำหรับเรียงเลเยอร์ภาพตาม priority
        ViewBag.ParkingStates = _db.ParkingStates.ToList();

        // เช็คว่ามีไฟล์วิดีโอ idle.mp4 อยู่หรือเปล่า
        string videoPath = Path.Combine(_environment.WebRootPath, "videos", "idle.mp4");
        ViewBag.HasVideo = System.IO.File.Exists(videoPath);
        // ใช้เวลาแก้ไขไฟล์เป็น cache-buster กัน browser cache ค้างไฟล์เก่าหลังอัปโหลดวิดีโอใหม่
        ViewBag.VideoVersion = ViewBag.HasVideo
            ? System.IO.File.GetLastWriteTimeUtc(videoPath).Ticks
            : 0;

        // อ่านค่าหมุนหน้าจอที่บันทึกไว้ฝั่ง server
        int rotation = 0;
        string rotFile = Path.Combine(_environment.ContentRootPath, "rotation.dat");
        if (System.IO.File.Exists(rotFile) &&
            int.TryParse(System.IO.File.ReadAllText(rotFile).Trim(), out int saved))
            rotation = saved;
        ViewBag.ScreenRotation = rotation;

        // ลำดับ priority ของขา GPIO ใช้กำหนด z-index ของเลเยอร์ภาพ
        ViewBag.PinPriority = _pinPrioritySvc.GetOrder();

        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file != null && file.Length > 0)
        {
            // บันทึกไฟล์ลง wwwroot/uploads
            var path = Path.Combine(_environment.WebRootPath, "uploads", file.FileName);
            using (var stream = new FileStream(path, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }
        }
        return RedirectToAction("Index");
    }
}