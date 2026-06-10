using Microsoft.EntityFrameworkCore;
using ParkingWeb.Data;
using ParkingWeb.Services;
using ParkingWeb.Models;
using ParkingWeb.Hubs;

var builder = WebApplication.CreateBuilder(args);

// บอกโปรแกรมให้ใช้ฐานข้อมูล SQLite
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=parking.db"));
    
// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

// ไม่จำกัด request size ที่ระดับ Kestrel — ให้ controller ตรวจสอบเองตาม free disk
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = long.MaxValue;
});
builder.WebHost.ConfigureKestrel(k =>
{
    k.Limits.MaxRequestBodySize = null; // null = unlimited
});
builder.Services.AddHostedService<GpioService>();
builder.Services.AddSingleton<ParkingWeb.Services.LogService>();
builder.Services.AddSingleton<ParkingWeb.Services.GpioBaselineService>();
builder.Services.AddSingleton<ParkingWeb.Services.SimulatorModeService>();
builder.Services.AddSingleton<ParkingWeb.Services.PinPriorityService>();

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

//app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    ;

app.MapHub<ParkingHub>("/parkingHub");

var logSvc = app.Services.GetRequiredService<LogService>();
app.Lifetime.ApplicationStarted.Register(() =>
    logSvc.SaveSystemLog("=== Application started ==="));
app.Lifetime.ApplicationStopping.Register(() =>
    logSvc.SaveSystemLog("=== Application stopping ==="));
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    context.Database.EnsureCreated(); // สร้างไฟล์ .db อัตโนมัติถ้ายังไม่มี

    // เพิ่มคอลัมน์ IdleBaseline ถ้ายังไม่มี (ไม่ใช้ try-catch เพื่อหลีกเลี่ยง EF Core log noise)
    var columns = context.Database
        .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('ParkingStates')")
        .ToList();
    if (!columns.Contains("IdleBaseline"))
        context.Database.ExecuteSqlRaw("ALTER TABLE ParkingStates ADD COLUMN IdleBaseline INTEGER");

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
