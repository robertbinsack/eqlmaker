# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (c) 2026 Robert Binsack
<#
.SYNOPSIS
  Read-only snapshot of the places a classic installer would change: autostart and uninstall
  keys of the registry, PATH of the user and of the machine, services, firewall rules and
  scheduled tasks. Dot-source this file, then:
    Get-SystemSnapshot                         -> ordered dictionary: name -> list of text lines
    Compare-SystemSnapshot <before> <after>    -> list of differences (empty when nothing changed)
    Get-NewFiles <folder> <since> [-Exclude]   -> files written after <since> below <folder>
#>

function Get-SystemSnapshot {
    $snap = [ordered]@{}
    $keys = [ordered]@{
        'HKCU Run'           = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        'HKCU RunOnce'       = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
        'HKLM Run'           = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
        'HKLM RunOnce'       = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
        'HKLM Run (32 bit)'  = 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'
    }
    foreach ($name in $keys.Keys) {
        $lines = @()
        if (Test-Path $keys[$name]) {
            $item = Get-Item $keys[$name]
            foreach ($v in $item.GetValueNames()) { $lines += "$v=$($item.GetValue($v))" }
        }
        $snap[$name] = @($lines | Sort-Object)
    }
    $uninstall = [ordered]@{
        'HKCU Uninstall'          = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
        'HKLM Uninstall'          = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall'
        'HKLM Uninstall (32 bit)' = 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall'
    }
    foreach ($name in $uninstall.Keys) {
        $lines = @()
        if (Test-Path $uninstall[$name]) { $lines = @(Get-ChildItem $uninstall[$name] | ForEach-Object { $_.PSChildName }) }
        $snap[$name] = @($lines | Sort-Object)
    }
    $snap['PATH user'] = @([Environment]::GetEnvironmentVariable('PATH', 'User'))
    $snap['PATH machine'] = @([Environment]::GetEnvironmentVariable('PATH', 'Machine'))
    $snap['Environment user (names)'] = @([Environment]::GetEnvironmentVariables('User').Keys | Sort-Object)
    $snap['Environment machine (names)'] = @([Environment]::GetEnvironmentVariables('Machine').Keys | Sort-Object)
    $snap['Services'] = @(Get-CimInstance Win32_Service | ForEach-Object { "$($_.Name)|$($_.StartMode)|$($_.PathName)" } | Sort-Object)
    $rules = @()
    try { $rules = @(Get-NetFirewallRule -ErrorAction Stop | ForEach-Object { "$($_.Name)|$($_.Enabled)|$($_.Direction)|$($_.Action)" } | Sort-Object) } catch { $rules = @("not readable: $($_.Exception.Message)") }
    $snap['Firewall rules'] = $rules
    $tasks = @()
    try { $tasks = @(Get-ScheduledTask -ErrorAction Stop | ForEach-Object { "$($_.TaskPath)$($_.TaskName)" } | Sort-Object) } catch { $tasks = @("not readable: $($_.Exception.Message)") }
    $snap['Scheduled tasks'] = $tasks
    return $snap
}

function Compare-SystemSnapshot($Before, $After) {
    $diff = @()
    foreach ($name in $Before.Keys) {
        $a = @($Before[$name]); $b = @($After[$name])
        foreach ($line in $b) { if ($a -notcontains $line) { $diff += "${name}: added: $line" } }
        foreach ($line in $a) { if ($b -notcontains $line) { $diff += "${name}: removed: $line" } }
    }
    return $diff
}

function Get-NewFiles([string]$Folder, [datetime]$Since, [string[]]$Exclude = @()) {
    $result = @()
    if (-not (Test-Path -LiteralPath $Folder)) { return $result }
    $stack = New-Object System.Collections.Stack
    $stack.Push($Folder)
    while ($stack.Count) {
        $dir = $stack.Pop()
        $skip = $false
        foreach ($e in $Exclude) { if ($dir.StartsWith($e, [System.StringComparison]::OrdinalIgnoreCase)) { $skip = $true } }
        if ($skip) { continue }
        try {
            foreach ($d in [System.IO.Directory]::GetDirectories($dir)) {
                if (([System.IO.File]::GetAttributes($d) -band [System.IO.FileAttributes]::ReparsePoint) -eq 0) { $stack.Push($d) }
            }
            foreach ($f in [System.IO.Directory]::GetFiles($dir)) {
                $t = [System.IO.File]::GetLastWriteTime($f)
                $c = [System.IO.File]::GetCreationTime($f)
                if ($t -gt $Since -or $c -gt $Since) { $result += $f }
            }
        } catch { }
    }
    return $result
}
