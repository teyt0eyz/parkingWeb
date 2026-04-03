using Microsoft.AspNetCore.Hosting;
using System;
using System.IO;
using System.Linq;

namespace ParkingWeb.Services
{
    public class LogService
    {
        private readonly string _logFolder;

        public LogService(IWebHostEnvironment env)
        {
            // กำหนดพิกัดเก็บ Log: /home/pi/ParkingWeb/ParkingLogs
            _logFolder = Path.Combine(env.ContentRootPath, "ParkingLogs");
            
            // ถ้ายังไม่มีโฟลเดอร์ ให้สร้างขึ้นมา
            if (!Directory.Exists(_logFolder)) 
                Directory.CreateDirectory(_logFolder);
        }

        public void SaveLog(int slotId, string status)
        {
            try
            {
                // 1. ชื่อไฟล์ตามวันที่ เช่น 2026-04-02.csv
                string fileName = $"{DateTime.Now:yyyy-MM-dd}.csv";
                string filePath = Path.Combine(_logFolder, fileName);

                // 2. จัดรูปแบบข้อมูล: วันที่, เวลา, ช่อง, สถานะ
                string logEntry = $"{DateTime.Now:yyyy-MM-dd},{DateTime.Now:HH:mm:ss},Slot_{slotId},{status}{Environment.NewLine}";

                // 3. เขียนต่อท้ายไฟล์ (Append)
                File.AppendAllText(filePath, logEntry);

                // 4. รันระบบกวาดล้างไฟล์เก่า (90 วัน)
                CleanupOldLogs();
            }
            catch (Exception) { /* ป้องกันโปรแกรมแครชถ้าเขียนไฟล์ไม่ได้ */ }
        }

        private void CleanupOldLogs()
        {
            var threshold = DateTime.Now.AddDays(-90);
            DirectoryInfo dir = new DirectoryInfo(_logFolder);

            // ไล่ดูไฟล์ .csv ทุกไฟล์
            foreach (FileInfo file in dir.GetFiles("*.csv"))
            {
                // ถ้าไฟล์ไหนเก่ากว่า 90 วัน (นับจากชื่อไฟล์หรือวันที่สร้าง) ให้ลบ
                if (file.CreationTime < threshold)
                {
                    file.Delete();
                }
            }
        }
    }
}