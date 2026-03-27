using Microsoft.AspNetCore.Mvc;
using System.IO;

public class HomeController : Controller
{
    private readonly IWebHostEnvironment _environment;

    public HomeController(IWebHostEnvironment environment)
    {
        _environment = environment;
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