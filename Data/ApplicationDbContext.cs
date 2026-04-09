using Microsoft.EntityFrameworkCore;
using ParkingWeb.Models; // เปลี่ยน ParkingWeb เป็นชื่อโปรเจกต์ของคุณ

namespace ParkingWeb.Data // เปลี่ยน ParkingWeb เป็นชื่อโปรเจกต์ของคุณ
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // นี่คือการบอกว่าเราจะมีตารางชื่อ ParkingStates ในฐานข้อมูล
        public DbSet<ParkingState> ParkingStates { get; set; }
    
    }
}