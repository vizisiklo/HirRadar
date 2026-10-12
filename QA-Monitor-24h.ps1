param(
    [ValidateRange(1,168)][int]$DurationHours = 24,
    [ValidateRange(10,3600)][int]$IntervalSeconds = 60,
    [string]$ProcessName = 'HirRadar',
    [string]$OutputPath = ''
)
# Helyi Windows PowerShell 5.1+ mérőszkript. Nem módosítja az alkalmazás adatait.
$ErrorActionPreference = 'Stop'
if (-not $OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot ('HirRadar-QA-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.csv')
}
$end = (Get-Date).AddHours($DurationHours)
$cpuLogical = [Math]::Max([Environment]::ProcessorCount, 1)
$previous = @{}
Write-Host "24 órás (vagy beállított) HírRadar-mérés. CSV: $OutputPath"
Write-Host 'A programot indítsd el külön, és hagyd futni. Megszakítás: Ctrl+C.'
while ((Get-Date) -lt $end) {
    $now = Get-Date
    $processes = @(Get-Process -Name $ProcessName -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        [pscustomobject]@{
            Time = $now.ToString('o'); Pid = ''; State = 'NOT_RUNNING'; CpuPercent = ''
            PrivateMB = ''; WorkingSetMB = ''; Handles = ''; Threads = ''
        } | Export-Csv -Path $OutputPath -NoTypeInformation -Append -Encoding UTF8 -Delimiter ';'
    } else {
        foreach ($proc in $processes) {
            try {
                $totalCpu = [double]$proc.CPU
                $key = [string]$proc.Id
                $rate = ''
                if ($previous.ContainsKey($key)) {
                    $elapsed = ($now - $previous[$key].Time).TotalSeconds
                    if ($elapsed -gt 0 -and $totalCpu -ge $previous[$key].Cpu) {
                        $rate = [Math]::Round(100.0 * ($totalCpu - $previous[$key].Cpu) / $elapsed / $cpuLogical, 2)
                    }
                }
                $previous[$key] = @{Time = $now; Cpu = $totalCpu}
                [pscustomobject]@{
                    Time = $now.ToString('o'); Pid = $proc.Id; State = 'RUNNING'; CpuPercent = $rate
                    PrivateMB = [Math]::Round($proc.PrivateMemorySize64 / 1MB, 1)
                    WorkingSetMB = [Math]::Round($proc.WorkingSet64 / 1MB, 1)
                    Handles = $proc.HandleCount; Threads = $proc.Threads.Count
                } | Export-Csv -Path $OutputPath -NoTypeInformation -Append -Encoding UTF8 -Delimiter ';'
            } catch {
                Write-Warning ("PID $($proc.Id): " + $_.Exception.Message)
            }
        }
    }
    Start-Sleep -Seconds $IntervalSeconds
}
Write-Host "Mérés vége. Eredmény: $OutputPath"
