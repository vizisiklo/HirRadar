# HirRadar v1.0.2 / Windows 10-11 x64 portable release builder.
# Windows PowerShell 5.1 vagy PowerShell 7. A gepen .NET 8 SDK szukseges.
# Fut.: powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'HirRadar.csproj'
$logFile = Join-Path $root 'BUILD_RELEASE.log'
$publishDir = Join-Path $root 'publish'
$distDir = Join-Path $root 'dist'
$version = '1.0.2'
$expectedFileVersion = '1.0.2.0'
$transcriptStarted = $false
$result = 0

try {
    # Windows PowerShell 5.1-en is mukodo, a konzol es a log egyarant megmarad.
    Start-Transcript -Path $logFile -Force -ErrorAction Stop | Out-Null
    $transcriptStarted = $true
    Write-Host '=== HirRadar v1.0.2 | Release build ===' -ForegroundColor Cyan
    Write-Host ('Projekt: ' + $project)
    Write-Host ('Log: ' + $logFile)
    Write-Host ''

    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
        throw 'Nem talalhato a HirRadar.csproj. A teljes ZIP-et egy mappaba kell kicsomagolni, majd onnan inditani a BUILD_RELEASE.cmd fajlt.'
    }
    if ($env:OS -ne 'Windows_NT') {
        throw 'Ez a Release build csak Windows 10/11 x64 alatt tamogatott.'
    }
    if (-not [Environment]::Is64BitOperatingSystem) {
        throw 'A kiadas win-x64 celrendszerre keszul; 64 bites Windows szukseges.'
    }

    $dotnetCommand = Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw 'A .NET SDK nem talalhato a PATH valtozoban. Telepitsd a .NET 8 SDK x64 valtozatat (nem csak a Runtime-ot), majd indits uj terminalt.'
    }
    Write-Host ('dotnet: ' + $dotnetCommand.Source)
    $installedSdks = @(& dotnet --list-sdks 2>&1)
    if ($LASTEXITCODE -ne 0) { throw ('A dotnet --list-sdks parancs hibazott: ' + ($installedSdks -join ' ')) }
    Write-Host 'Telepitett SDK-k:'
    $installedSdks | ForEach-Object { Write-Host ('  ' + $_) }
    $hasCompatibleSdk = @($installedSdks | Where-Object { $_ -match '^([89]|[1-9][0-9])\.' }).Count -gt 0
    if (-not $hasCompatibleSdk) {
        throw 'Nincs .NET 8 vagy ujabb SDK. A .NET 8 Runtime vagy egy Visual Studio komponens onmagaban nem eleg a forditashoz. https://dotnet.microsoft.com/download/dotnet/8.0'
    }

    # Egy mar futtato, azonos nevu EXE Windows alatt megakadalyozhatja a publish mappa torleset.
    $running = @(Get-Process -Name 'HirRadar' -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        $pids = ($running | ForEach-Object { $_.Id }) -join ', '
        throw ('A HirRadar jelenleg fut (PID: ' + $pids + '). Lepj ki belole a rendszertalcai ikon jobb gomb > Kilepes pontjaval, majd probald ujra.')
    }

    Push-Location $root
    try {
        if (Test-Path -LiteralPath $publishDir) {
            Remove-Item -LiteralPath $publishDir -Recurse -Force -ErrorAction Stop
        }
        if (-not (Test-Path -LiteralPath $distDir)) {
            New-Item -ItemType Directory -Path $distDir -Force | Out-Null
        }

        Write-Host ''
        Write-Host '[1/3] .NET publish (win-x64; self-contained; single-file)' -ForegroundColor Cyan
        # A dotnet publish maga visszaadja a NuGet, SDK es C# hibait, azokat a naplo elmenti.
        & dotnet publish $project -c Release -r win-x64 --self-contained true `
            '-p:PublishSingleFile=true' `
            '-p:IncludeNativeLibrariesForSelfExtract=true' `
            '-p:EnableCompressionInSingleFile=true' `
            '-p:DebugType=None' `
            '-p:DebugSymbols=false' `
            ('-p:Version=' + $version) `
            ('-p:FileVersion=' + $expectedFileVersion) `
            -o $publishDir
        if ($LASTEXITCODE -ne 0) {
            throw ('dotnet publish sikertelen (kilepesi kod: ' + $LASTEXITCODE + '). A valodi hiba fent, illetve a BUILD_RELEASE.log fajlban olvashato.')
        }

        Write-Host ''
        Write-Host '[2/3] EXE es verzio ellenorzese' -ForegroundColor Cyan
        $exePath = Join-Path $publishDir 'HirRadar.exe'
        if (-not (Test-Path -LiteralPath $exePath -PathType Leaf)) {
            throw ('Hianyzik a HirRadar.exe a publish mappabol: ' + $exePath)
        }
        $actualVersion = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
        if ($actualVersion -ne $expectedFileVersion) {
            throw ('EXE-verzio elteres. Talalt: ' + $actualVersion + ', vart: ' + $expectedFileVersion)
        }
        Write-Host ('EXE fajlverzio: ' + $actualVersion)
        $releaseFiles = @(Get-ChildItem -LiteralPath $publishDir -File)
        if ($releaseFiles.Count -gt 1) {
            Write-Warning 'A publish mappaban tobb fajl van. A teljes kimenetet becsomagoljuk, hogy a fuggosegek ne hianyozzanak.'
            $releaseFiles | ForEach-Object { Write-Host ('  ' + $_.Name) }
        }

        Write-Host ''
        Write-Host '[3/3] Kiadási ZIP es SHA256' -ForegroundColor Cyan
        $releaseZip = Join-Path $distDir 'HirRadar_v1.0.2_Windows_x64.zip'
        $hashFile = Join-Path $distDir 'HirRadar_v1.0.2_Windows_x64_SHA256.txt'
        if (Test-Path -LiteralPath $releaseZip) { Remove-Item -LiteralPath $releaseZip -Force }
        Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $releaseZip -CompressionLevel Optimal -Force
        $hash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash
        Set-Content -LiteralPath $hashFile -Value ($hash + '  HirRadar.exe') -Encoding ASCII
        Write-Host ''
        Write-Host 'SIKER: HírRadar v1.0.2 Release elkeszult.' -ForegroundColor Green
        Write-Host ('EXE:    ' + $exePath)
        Write-Host ('ZIP:    ' + $releaseZip)
        Write-Host ('SHA256: ' + $hash)
        Write-Host ('Naplo:  ' + $logFile)
        Write-Host 'A celgepre nem kell .NET runtime.'
        Write-Warning 'A kiadott EXE nincs Authenticode tanusitvannyal alairva; a SmartScreen figyelmeztethet.'
    }
    finally {
        Pop-Location
    }
}
catch {
    $result = 1
    Write-Host ''
    Write-Host 'SIKERTELEN RELEASE BUILD' -ForegroundColor Red
    Write-Host ('Hiba: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'Keresd a BUILD_RELEASE.log fajlt ugyanebben a projektmappaban.' -ForegroundColor Yellow
    Write-Host 'Kuldd el a log fajl veget vagy a konkret hiba-uzenetet a javitashoz.' -ForegroundColor Yellow
}
finally {
    if ($transcriptStarted) {
        try { Stop-Transcript | Out-Null } catch { }
    }
}
exit $result
