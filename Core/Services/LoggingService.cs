using System;

namespace Test_1.Services
{
    public class LoggingService
    {
        public event Action<string> OnLogMessage;

        public void Log(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string logEntry = $"[{timestamp}] {message}\r\n";
            OnLogMessage?.Invoke(logEntry);
        }
    }
}
