using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LegacyApp
{
    class Program
    {
        [DllImport("user32.dll")]
        public static extern int MessageBox(IntPtr hWnd, String text, String caption, uint type);

        static void Main(string[] args)
        {
            // Windows Registry
            RegistryKey key = Registry.LocalMachine.OpenSubKey("Software\\MyApp");
            
            // Hardcoded Path
            string path = "C:\\Temp\\data.txt";
            File.WriteAllText(path, "test");

            // P/Invoke
            MessageBox(IntPtr.Zero, "Hello", "Caption", 0);
        }
    }
}
