using Microsoft.EntityFrameworkCore;
using ParkingWeb.Data;
using ParkingWeb.Models;
using ParkingWeb.Hubs;

var builder = WebApplication.CreateBuilder(args);

// บอกโปรแกรมให้ใช้ฐานข้อมูล SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=parking.db"));
    
// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStaticFiles();
//app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapHub<ParkingHub>("/parkingHub");
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.EnsureCreated(); // สร้างไฟล์ .db อัตโนมัติถ้ายังไม่มี

    if (!context.ParkingStates.Any())
    {
        context.ParkingStates.AddRange(
            new ParkingState { StateName = "ถอยหลัง (Backward)", GpioPin = 17, ImagePath = "" },
            new ParkingState { StateName = "เดินหน้า (Forward)", GpioPin = 27, ImagePath = "" },
            new ParkingState { StateName = "เลี้ยวซ้าย (Left)", GpioPin = 22, ImagePath = "" },
            new ParkingState { StateName = "เลี้ยวขวา (Right)", GpioPin = 23, ImagePath = "" },
            new ParkingState { StateName = "หยุด (STOP)", GpioPin = 24, ImagePath = "" }
        );
        context.SaveChanges();
    }
}
app.Run();
