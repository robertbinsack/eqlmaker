# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Runs a command line as an ordinary, non-elevated user although the calling session is an
  elevated admin session - without creating a Windows account. Dot-source this file, then:
    Invoke-Restricted -CommandLine <text> -WorkDir <folder> [-TimeoutSeconds n]   -> exit code
  The process gets a token computed by the Windows "Safer" level "normal user" (the
  group of the admins (S-1-5-32-544) becomes deny-only, the privileges are removed) and the integrity
  level Medium. It has no window. Output redirection is part of the command line (cmd /c).
#>
if (-not ('EqlMakerTest.RestrictedRunner' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace EqlMakerTest {
    public static class RestrictedRunner {
        [StructLayout(LayoutKind.Sequential)] struct SID_AND_ATTRIBUTES { public IntPtr Sid; public int Attributes; }
        [StructLayout(LayoutKind.Sequential)] struct TOKEN_MANDATORY_LABEL { public SID_AND_ATTRIBUTES Label; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct STARTUPINFO {
            public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

        [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferCreateLevel(int scopeId, int levelId, int openFlags, out IntPtr level, IntPtr reserved);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferComputeTokenFromLevel(IntPtr level, IntPtr inToken, out IntPtr outToken, int flags, IntPtr reserved);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool SaferCloseLevel(IntPtr level);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool ConvertStringSidToSid(string sid, out IntPtr psid);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool SetTokenInformation(IntPtr token, int infoClass, ref TOKEN_MANDATORY_LABEL info, int length);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool CreateProcessAsUser(IntPtr token, string app, StringBuilder commandLine, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string cwd, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
        [DllImport("kernel32.dll", SetLastError = true)] static extern int WaitForSingleObject(IntPtr h, int ms);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(IntPtr h, out int code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool TerminateProcess(IntPtr h, int code);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr h);

        public static int Run(string commandLine, string workDir, int timeoutMs) {
            IntPtr level, token, sid;
            // scope "user" (2), level "normal user" (0x20000), open (1)
            if (!SaferCreateLevel(2, 0x20000, 1, out level, IntPtr.Zero)) throw new Win32Exception();
            try {
                if (!SaferComputeTokenFromLevel(level, IntPtr.Zero, out token, 0, IntPtr.Zero)) throw new Win32Exception();
            } finally { SaferCloseLevel(level); }
            try {
                // integrity level Medium
                if (!ConvertStringSidToSid("S-1-16-8192", out sid)) throw new Win32Exception();
                try {
                    TOKEN_MANDATORY_LABEL label = new TOKEN_MANDATORY_LABEL();
                    label.Label.Sid = sid; label.Label.Attributes = 0x20;
                    if (!SetTokenInformation(token, 25, ref label, Marshal.SizeOf(label) + 12)) throw new Win32Exception();
                } finally { LocalFree(sid); }
                STARTUPINFO si = new STARTUPINFO(); si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
                PROCESS_INFORMATION pi;
                // no window (0x08000000), Unicode environment of the caller is inherited
                if (!CreateProcessAsUser(token, null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero, workDir, ref si, out pi)) throw new Win32Exception();
                try {
                    if (WaitForSingleObject(pi.hProcess, timeoutMs) != 0) { TerminateProcess(pi.hProcess, 258); return 258; }
                    int code; GetExitCodeProcess(pi.hProcess, out code); return code;
                } finally { CloseHandle(pi.hProcess); CloseHandle(pi.hThread); }
            } finally { CloseHandle(token); }
        }
    }
}
'@
}

function Invoke-Restricted([string]$CommandLine, [string]$WorkDir, [int]$TimeoutSeconds = 1800) {
    return [EqlMakerTest.RestrictedRunner]::Run($CommandLine, $WorkDir, $TimeoutSeconds * 1000)
}
