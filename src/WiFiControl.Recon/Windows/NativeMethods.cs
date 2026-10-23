using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WiFiControl.Recon.Windows;

/// <summary>Вызовы Windows, которых нет в .NET. Все только читают.</summary>
internal static class NativeMethods
{
    private const int ProcessBasicInformation = 0;
    private const int ScManagerConnect = 0x0001;
    private const int ServiceQueryStatus = 0x0004;
    private const int ScStatusProcessInfo = 0;
    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;
    private const int WinHttpAccessNamedProxy = 3;

    public static int ParentProcessId(IntPtr process)
    {
        int status = NtQueryInformationProcess(process, ProcessBasicInformation, out ProcessBasic info, Marshal.SizeOf<ProcessBasic>(), out _);
        if (status != 0) throw new Win32Exception(RtlNtStatusToDosError(status), "NtQueryInformationProcess вернула 0x" + status.ToString("X8"));
        return (int)info.InheritedFromUniqueProcessId;
    }

    /// <returns>Номер процесса службы; 0, если служба не запущена.</returns>
    public static int ServiceProcessId(string service)
    {
        IntPtr manager = OpenSCManager(null, null, ScManagerConnect);
        if (manager == IntPtr.Zero) throw LastError("OpenSCManager");
        try
        {
            IntPtr handle = OpenService(manager, service, ServiceQueryStatus);
            if (handle == IntPtr.Zero) throw LastError("OpenService(" + service + ")");
            try
            {
                if (!QueryServiceStatusEx(handle, ScStatusProcessInfo, out ServiceStatusProcess status, Marshal.SizeOf<ServiceStatusProcess>(), out _))
                    throw LastError("QueryServiceStatusEx(" + service + ")");
                return status.ProcessId;
            }
            finally
            {
                CloseServiceHandle(handle);
            }
        }
        finally
        {
            CloseServiceHandle(manager);
        }
    }

    public static string SessionUserName(int session) => SessionText(session, WtsUserName);

    public static string SessionDomainName(int session) => SessionText(session, WtsDomainName);

    private static string SessionText(int session, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out IntPtr buffer, out _))
            throw LastError("WTSQuerySessionInformation");
        try
        {
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            WTSFreeMemory(buffer);
        }
    }

    /// <returns>Задан ли прокси (а не прямое подключение), сервер и исключения как их хранит Windows.</returns>
    public static (bool Named, string? Proxy, string? Bypass) WinHttpDefaultProxy()
    {
        var info = new WinHttpProxyInfo();
        if (!WinHttpGetDefaultProxyConfiguration(ref info)) throw LastError("WinHttpGetDefaultProxyConfiguration");
        try
        {
            return (info.AccessType == WinHttpAccessNamedProxy, Marshal.PtrToStringUni(info.Proxy), Marshal.PtrToStringUni(info.ProxyBypass));
        }
        finally
        {
            if (info.Proxy != IntPtr.Zero) GlobalFree(info.Proxy);
            if (info.ProxyBypass != IntPtr.Zero) GlobalFree(info.ProxyBypass);
        }
    }

    // Имя вызова — в сообщении, код — в NativeErrorCode: отчёт покажет оба
    private static Win32Exception LastError(string call)
    {
        int code = Marshal.GetLastWin32Error();
        return new Win32Exception(code, call + ": " + new Win32Exception(code).Message);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasic
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatusProcess
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
        public int ProcessId;
        public int ServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinHttpProxyInfo
    {
        public int AccessType;
        public IntPtr Proxy;
        public IntPtr ProxyBypass;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, out ProcessBasic info, int length, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int RtlNtStatusToDosError(int status);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManager(string? machine, string? database, int access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenService(IntPtr manager, string name, int access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, out ServiceStatusProcess status, int size, out int needed);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytesReturned);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("winhttp.dll", SetLastError = true)]
    private static extern bool WinHttpGetDefaultProxyConfiguration(ref WinHttpProxyInfo info);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr memory);
}
