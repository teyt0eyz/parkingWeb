using Microsoft.AspNetCore.Mvc;
using ParkingWeb.Data;
using ParkingWeb.Models;

namespace ParkingWeb.Controllers;

public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _env;

    public AdminController(ApplicationDbContext db, IWebHostEnvironment env)
    {
        _db = db;
        _env = env;
    }

    public IActionResult Index()
    {
        var states = _db.ParkingStates.ToList();
        return View(states); // ส่งแค่ List กลับไป ง่ายๆ เลย
    }

    [HttpPost]
    public async Task<IActionResult> UpdateIdleVideo(IFormFile videoFile)
    {
        if (videoFile != null && videoFile.Length > 0)
        {
            string path = Path.Combine(_env.WebRootPath, "videos", "idle.mp4");
            using (var stream = new FileStream(path, FileMode.Create))
            {
                await videoFile.CopyToAsync(stream);
            }
        }
        return RedirectToAction("Index");
    }

    [HttpPost]
    public async Task<IActionResult> UpdateStateImage(int id, IFormFile imageFile)
    {
        var state = _db.ParkingStates.Find(id);
        if (state != null && imageFile != null)
        {
            // ลบรูปเก่าทิ้งก่อน (ถ้าไม่ใช่ค่าว่าง)
            if (!string.IsNullOrEmpty(state.ImagePath))
            {
                string oldPath = Path.Combine(_env.WebRootPath, "uploads", state.ImagePath);
                if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
            }

            string fileName = Guid.NewGuid().ToString() + Path.GetExtension(imageFile.FileName);
            string path = Path.Combine(_env.WebRootPath, "uploads", fileName);
            
            using (var stream = new FileStream(path, FileMode.Create))
            {
                await imageFile.CopyToAsync(stream);
            }

            state.ImagePath = fileName;
            _db.SaveChanges();
        }
        return RedirectToAction("Index");
    }

    [HttpPost]
    public IActionResult DeleteStateImage(int id)
    {
        var state = _db.ParkingStates.Find(id);
        if (state != null && !string.IsNullOrEmpty(state.ImagePath))
        {
            string path = Path.Combine(_env.WebRootPath, "uploads", state.ImagePath);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);

            state.ImagePath = ""; // ล้างค่าใน DB
            _db.SaveChanges();
        }
        return RedirectToAction("Index");
    }
}