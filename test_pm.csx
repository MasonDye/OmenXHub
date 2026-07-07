#r "System.Diagnostics.Process"
using System.Diagnostics;
var p = new Process();
p.StartInfo = new ProcessStartInfo{
  FileName = @"E:\Desktop\OmenXHub-main - 副本 (3) - 副本 - 副本\PresentMon.exe",
  Arguments = "--output_stdout --no_console_stats --stop_existing_session --timer 1000",
  UseShellExecute = false,
  RedirectStandardOutput = true,
  CreateNoWindow = true
};
p.Start();
string line = p.StandardOutput.ReadLine();
System.IO.File.WriteAllText(@"E:\Desktop\OmenXHub-main - 副本 (3) - 副本 - 副本\pm_header.txt", line ?? "null");
p.Kill();
p.WaitForExit(3000);
