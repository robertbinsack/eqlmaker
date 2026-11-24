# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Reads the import table of a Windows program (PE32+ or PE32) without any further tool.
  Dot-source this file, then:
    Get-PeImports <file>         names of the imported libraries (normal and delay-loaded)
    Test-WindowsLibrary <name>   true when the library is part of every Windows 10/11/Server
#>

function Get-PeImports([string]$Path) {
    $b = [System.IO.File]::ReadAllBytes($Path)
    if ($b.Length -lt 64 -or $b[0] -ne 0x4D -or $b[1] -ne 0x5A) { throw "not a Windows program: $Path" }
    $pe = [BitConverter]::ToInt32($b, 0x3C)
    if ([BitConverter]::ToUInt32($b, $pe) -ne 0x00004550) { throw "no PE header: $Path" }
    $sectionCount = [BitConverter]::ToUInt16($b, $pe + 6)
    $optSize = [BitConverter]::ToUInt16($b, $pe + 20)
    $opt = $pe + 24
    $magic = [BitConverter]::ToUInt16($b, $opt)
    if ($magic -eq 0x20B) { $dirs = $opt + 112 } else { $dirs = $opt + 96 }
    $sections = @()
    $s = $opt + $optSize
    for ($i = 0; $i -lt $sectionCount; $i++) {
        $o = $s + 40 * $i
        $sections += [pscustomobject]@{
            VSize = [BitConverter]::ToUInt32($b, $o + 8); VAddr = [BitConverter]::ToUInt32($b, $o + 12)
            RSize = [BitConverter]::ToUInt32($b, $o + 16); RPtr = [BitConverter]::ToUInt32($b, $o + 20)
        }
    }
    $toOffset = {
        param([uint32]$rva)
        foreach ($sec in $sections) {
            $size = [Math]::Max($sec.VSize, $sec.RSize)
            if ($rva -ge $sec.VAddr -and $rva -lt ($sec.VAddr + $size)) { return [int]($rva - $sec.VAddr + $sec.RPtr) }
        }
        return -1
    }
    $readName = {
        param([int]$off)
        $end = $off
        while ($end -lt $b.Length -and $b[$end] -ne 0) { $end++ }
        return [System.Text.Encoding]::ASCII.GetString($b, $off, $end - $off)
    }
    $names = New-Object System.Collections.Generic.List[string]
    # directory 1 = imports (entries of 20 bytes, name at +12); directory 13 = delay imports (32 bytes, name at +4)
    foreach ($d in @(@{ Index = 1; Size = 20; Name = 12 }, @{ Index = 13; Size = 32; Name = 4 })) {
        $rva = [BitConverter]::ToUInt32($b, $dirs + 8 * $d.Index)
        if ($rva -eq 0) { continue }
        $off = & $toOffset $rva
        if ($off -lt 0) { continue }
        while ($off + $d.Size -le $b.Length) {
            $nameRva = [BitConverter]::ToUInt32($b, $off + $d.Name)
            if ($nameRva -eq 0) { break }
            $nameOff = & $toOffset $nameRva
            if ($nameOff -ge 0) { $names.Add((& $readName $nameOff)) }
            $off += $d.Size
        }
    }
    return @($names | Sort-Object -Unique)
}

function Test-WindowsLibrary([string]$Name) {
    $n = $Name.ToLowerInvariant()
    # the universal C run-time (api-ms-win-crt-*, ucrtbase) is a component of Windows 10 and later
    if ($n -like 'api-ms-win-*' -or $n -like 'ext-ms-*') { return $true }
    $known = 'kernel32.dll', 'kernelbase.dll', 'ntdll.dll', 'advapi32.dll', 'user32.dll', 'gdi32.dll', 'shell32.dll', 'ole32.dll',
        'oleaut32.dll', 'ws2_32.dll', 'bcrypt.dll', 'bcryptprimitives.dll', 'crypt32.dll', 'ncrypt.dll', 'secur32.dll', 'shlwapi.dll',
        'rpcrt4.dll', 'msvcrt.dll', 'winmm.dll', 'version.dll', 'psapi.dll', 'iphlpapi.dll', 'dnsapi.dll', 'userenv.dll', 'ucrtbase.dll',
        'dbghelp.dll', 'msimg32.dll', 'usp10.dll', 'd2d1.dll', 'dwrite.dll', 'cfgmgr32.dll', 'avrt.dll', 'avicap32.dll',
        'powrprof.dll', 'netapi32.dll', 'wldap32.dll', 'comctl32.dll', 'comdlg32.dll', 'imm32.dll', 'setupapi.dll', 'mswsock.dll',
        'authz.dll', 'sspicli.dll', 'wtsapi32.dll', 'winhttp.dll', 'wininet.dll', 'normaliz.dll', 'pdh.dll', 'dwmapi.dll', 'uxtheme.dll'
    return $known -contains $n
}
