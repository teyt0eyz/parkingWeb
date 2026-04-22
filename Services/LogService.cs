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
            _logFolder = Path.Combine(env.ContentRootPath, "ParkingLogs");
            if (!Directory.Exists(_logFolder))
                Directory.CreateDirectory(_logFolder);
        }

        // GPIO event log: Slot_XX
        public void SaveLog(int slotId, string status) =>
            WriteLog($"Slot_{slotId}", status);

        // System-level log: SYSTEM
        public void SaveSystemLog(string message) =>
            WriteLog("SYSTEM", message);

        private void WriteLog(string category, string message)
        {
            try
            {
                string fileName = $"{DateTime.Now:yyyy-MM-dd}.csv";
                string filePath = Path.Combine(_logFolder, fileName);
                string entry    = $"{DateTime.Now:yyyy-MM-dd},{DateTime.Now:HH:mm:ss},{category},{message}{Environment.NewLine}";
                File.AppendAllText(filePath, entry, System.Text.Encoding.UTF8);
                CleanupOldLogs();
            }
            catch (Exception) { }
        }

        private void CleanupOldLogs()
        {
            var threshold = DateTime.Now.AddDays(-90);
            foreach (FileInfo file in new DirectoryInfo(_logFolder).GetFiles("*.csv"))
            {
                // ใช้ชื่อไฟล์ parse วันที่ แทน CreationTime (ไม่น่าเชื่อถือบน Linux)
                if (DateTime.TryParse(Path.GetFileNameWithoutExtension(file.Name), out var fileDate)
                    && fileDate < threshold)
                {
                    file.Delete();
                }
            }
        }
    }
}
