using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Daiso.Infrastructure.Pty;

/// <summary>
/// Windows 의사 콘솔(ConPTY, Windows 10 1809+) 위에 프로세스 하나를 띄우는 가장 얇은 층. (ARCHITECTURE §5.3)
/// 파이프 둘(우리 → 콘솔 입력, 콘솔 출력 → 우리)을 만들고 <c>CreatePseudoConsole</c>에 물린 뒤
/// <c>PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE</c>을 붙여 <c>CreateProcessW</c>로 실행한다.
/// 출력은 UTF-8 바이트에 VT 시퀀스가 섞여 나온다. 해석은 xterm.js가 한다.
/// </summary>
/// <remarks>
/// 읽기 파이프는 프로세스가 끝나도 닫히지 않는다. <see cref="Dispose"/>가 <c>ClosePseudoConsole</c>을 불러야
/// 읽는 쪽이 EOF를 본다. 순서: 프로세스 종료 확인 → Dispose → 읽기 루프 종료.
/// </remarks>
internal sealed class PseudoConsole : IDisposable
{
    private const int ProcThreadAttributePseudoConsole = 0x00020016;
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateUnicodeEnvironment = 0x00000400;

    private IntPtr _handle;
    private bool _disposed;

    private PseudoConsole(IntPtr handle, SafeFileHandle input, SafeFileHandle output, int processId, IntPtr processHandle)
    {
        _handle = handle;
        Input = input;
        Output = output;
        ProcessId = processId;
        ProcessHandle = processHandle;
    }

    /// <summary>우리가 쓰는 쪽. 키 입력을 여기에 UTF-8로 넣는다.</summary>
    internal SafeFileHandle Input { get; }

    /// <summary>우리가 읽는 쪽. 화면 출력이 여기서 나온다.</summary>
    internal SafeFileHandle Output { get; }

    internal int ProcessId { get; }

    internal IntPtr ProcessHandle { get; }

    /// <summary>
    /// <paramref name="commandLine"/>을 <paramref name="workingDirectory"/>에서 <paramref name="columns"/>×<paramref name="rows"/> 콘솔로 띄운다.
    /// </summary>
    internal static PseudoConsole Start(string commandLine, string workingDirectory, short columns, short rows, string? environmentBlock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        // 파이프: 콘솔 입력(우리가 쓰고 콘솔이 읽음), 콘솔 출력(콘솔이 쓰고 우리가 읽음).
        // conhost가 파이프 끝을 상속받아야 하므로 상속 가능으로 만든다. 아니면 첫 프레임만 오고 그 뒤가 끊긴다
        var inheritable = new SecurityAttributes { nLength = Marshal.SizeOf<SecurityAttributes>(), bInheritHandle = 1 };
        if (!CreatePipe(out var inputRead, out var inputWrite, ref inheritable, 0)
            || !CreatePipe(out var outputRead, out var outputWrite, ref inheritable, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "파이프를 만들 수 없습니다");
        }

        var size = new Coord { X = columns, Y = rows };
        var created = CreatePseudoConsole(size, inputRead, outputWrite, 0, out var handle);

        // ConPTY가 자기 쪽 끝을 복제해 갖는다. 우리 쪽 복사는 바로 닫아야 EOF가 제대로 전달된다
        inputRead.Dispose();
        outputWrite.Dispose();

        if (created != 0)
        {
            inputWrite.Dispose();
            outputRead.Dispose();
            throw new Win32Exception(created, "의사 콘솔을 만들 수 없습니다");
        }

        var attributeList = IntPtr.Zero;
        var processHandle = IntPtr.Zero;
        var threadHandle = IntPtr.Zero;

        try
        {
            var listSize = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref listSize);
            attributeList = Marshal.AllocHGlobal(listSize);

            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref listSize)
                || !UpdateProcThreadAttribute(attributeList, 0, (IntPtr)ProcThreadAttributePseudoConsole, handle, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "콘솔 속성을 붙일 수 없습니다");
            }

            var startup = new StartupInfoEx();
            startup.StartupInfo.cb = Marshal.SizeOf<StartupInfoEx>();
            startup.lpAttributeList = attributeList;

            // 부모(우리)에게 표준 핸들이 있으면(콘솔 앱·테스트 호스트) 자식이 그 값을 물려받아 의사 콘솔 대신 거기에 쓰려 든다.
            // 만드는 동안만 비워 두면 자식은 콘솔에 붙을 때 콘솔 핸들을 새로 받는다. GUI 앱에서는 원래 비어 있어 아무 일도 없다
            ProcessInformation info;
            var environment = environmentBlock is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(environmentBlock);
            lock (StdHandleGate)
            {
                var savedIn = GetStdHandle(StdInputHandle);
                var savedOut = GetStdHandle(StdOutputHandle);
                var savedErr = GetStdHandle(StdErrorHandle);
                SetStdHandle(StdInputHandle, IntPtr.Zero);
                SetStdHandle(StdOutputHandle, IntPtr.Zero);
                SetStdHandle(StdErrorHandle, IntPtr.Zero);

                bool started;
                int error;
                try
                {
                    started = CreateProcessW(
                        null,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        false,
                        ExtendedStartupInfoPresent | CreateUnicodeEnvironment,
                        environment,
                        workingDirectory,
                        ref startup,
                        out info);
                    error = Marshal.GetLastWin32Error();
                }
                finally
                {
                    if (environment != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(environment);
                    }

                    SetStdHandle(StdInputHandle, savedIn);
                    SetStdHandle(StdOutputHandle, savedOut);
                    SetStdHandle(StdErrorHandle, savedErr);
                }

                if (!started)
                {
                    throw new Win32Exception(error, "프로세스를 시작할 수 없습니다");
                }
            }

            processHandle = info.hProcess;
            threadHandle = info.hThread;
            CloseHandle(threadHandle);

            // 속성 리스트는 CreateProcess가 끝나면 바로 지운다(MS 문서의 안전 지점). 늦게 지우면 힙이 깨진다
            DeleteProcThreadAttributeList(attributeList);
            Marshal.FreeHGlobal(attributeList);
            attributeList = IntPtr.Zero;

            return new PseudoConsole(handle, inputWrite, outputRead, info.dwProcessId, processHandle);
        }
        catch
        {
            if (attributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            ClosePseudoConsole(handle);
            inputWrite.Dispose();
            outputRead.Dispose();
            throw;
        }
    }

    /// <summary>창 크기가 바뀌면 콘솔도 따라간다. 프로세스는 SIGWINCH에 해당하는 신호를 받아 다시 그린다.</summary>
    internal void Resize(short columns, short rows)
    {
        if (_disposed || columns <= 0 || rows <= 0)
        {
            return;
        }

        ResizePseudoConsole(_handle, new Coord { X = columns, Y = rows });
    }

    /// <summary>프로세스가 아직 살아 있으면 끝낸다(트리 전체는 아니다. 셸 아래 자식은 셸이 정리한다).</summary>
    internal void Kill()
    {
        if (ProcessHandle != IntPtr.Zero)
        {
            TerminateProcess(ProcessHandle, 1);
        }
    }

    /// <summary>종료 코드. 아직 돌고 있으면 null.</summary>
    internal int? ExitCode
    {
        get
        {
            if (ProcessHandle == IntPtr.Zero || !GetExitCodeProcess(ProcessHandle, out var code))
            {
                return null;
            }

            return code == StillActive ? null : (int)code;
        }
    }

    private const uint StillActive = 259;
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;
    private static readonly object StdHandleGate = new();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    /// <summary>
    /// 의사 콘솔만 닫는다. 프로세스가 끝난 뒤 이걸 불러야 출력 파이프가 EOF를 낸다.
    /// 파이프 핸들은 그대로 두어 읽는 쪽이 남은 출력을 다 비울 수 있게 한다.
    /// </summary>
    internal void Close()
    {
        if (_handle != IntPtr.Zero)
        {
            ClosePseudoConsole(_handle);
            _handle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Close();

        if (ProcessHandle != IntPtr.Zero)
        {
            CloseHandle(ProcessHandle);
        }

        Input.Dispose();
        Output.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Coord
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, ref SecurityAttributes lpPipeAttributes, int nSize);

    // ── ConPTY 진입점: 동봉한 conpty.dll(Windows Terminal 의 OpenConsole) 을 먼저, 없으면 OS 의 kernel32 ──────────
    //
    // Windows 10 내장 ConPTY(conhost 19045)는 대체 화면(?1049)·마우스 모드를 터미널에 그대로 넘기지 않고 자기가 삼킨 뒤
    // 주 화면 N줄을 다시 그린다. 그래서 Claude Code 처럼 대체 화면에서 그리는 TUI 는 xterm 에 스크롤백이 한 줄도 쌓이지 않았다
    // (2026-09-09 측정: buffer=normal, length == rows). Microsoft.Windows.Console.ConPTY 패키지의 conpty.dll 은 최신 동작이다.
    // 같은 API 이름·서명이라 어느 쪽을 썼는지만 기억해 같은 쪽으로 닫는다.

    private static bool _bundledConpty = true;

    private static int CreatePseudoConsole(Coord size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC)
    {
        if (_bundledConpty)
        {
            try
            {
                return Bundled.CreatePseudoConsole(size, hInput, hOutput, dwFlags, out phPC);
            }
            catch (DllNotFoundException)
            {
                _bundledConpty = false;
            }
        }

        return Kernel32.CreatePseudoConsole(size, hInput, hOutput, dwFlags, out phPC);
    }

    private static int ResizePseudoConsole(IntPtr hPC, Coord size) =>
        _bundledConpty ? Bundled.ResizePseudoConsole(hPC, size) : Kernel32.ResizePseudoConsole(hPC, size);

    private static void ClosePseudoConsole(IntPtr hPC)
    {
        if (_bundledConpty)
        {
            Bundled.ClosePseudoConsole(hPC);
        }
        else
        {
            Kernel32.ClosePseudoConsole(hPC);
        }
    }

    /// <summary>어느 ConPTY 를 쓰는지. 로그·진단용.</summary>
    internal static string ConptySource => _bundledConpty ? "conpty.dll (동봉)" : "kernel32 (OS)";

    private static class Bundled
    {
        [DllImport("conpty.dll", SetLastError = true)]
        internal static extern int CreatePseudoConsole(Coord size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("conpty.dll", SetLastError = true)]
        internal static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

        [DllImport("conpty.dll", SetLastError = true)]
        internal static extern void ClosePseudoConsole(IntPtr hPC);
    }

    private static class Kernel32
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int CreatePseudoConsole(Coord size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern int ResizePseudoConsole(IntPtr hPC, Coord size);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern void ClosePseudoConsole(IntPtr hPC);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfoEx lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);
}
