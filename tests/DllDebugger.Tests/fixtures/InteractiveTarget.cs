using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

class InteractiveTarget
{
    [DllImport("fixture", CallingConvention = CallingConvention.Cdecl)]
    static extern int add(int a, int b);

    static void Main()
    {
        Process child = null;
        try {
            string command;
            while ((command = Console.ReadLine()) != null) {
                if (command == "spawn") {
                    child = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location) {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true
                    });
                    Console.WriteLine(child.Id);
                } else if (command.StartsWith("child ")) {
                    child.StandardInput.WriteLine(command.Substring(6));
                    Console.WriteLine(child.StandardOutput.ReadLine());
                } else {
                    Console.WriteLine(add(int.Parse(command), 100));
                }
            }
        } finally {
            if (child != null) { child.StandardInput.Close(); child.WaitForExit(); child.Dispose(); }
        }
    }
}
