namespace MyDebugTools.DllDebugger;

// Shared by the desktop process and its reloadable monitor page.
public static class CaptureHistory
{
    static readonly List<CaptureSession> sessions = [];
    public static void Add(CaptureSession session) { lock (sessions) sessions.Add(session); }
    public static CaptureSession[] Sessions() { lock (sessions) return sessions.ToArray(); }
}
