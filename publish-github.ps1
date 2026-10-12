#Requires -Version 5.1
# HírRadar v1.0.2 - GitHub upload FIX (PowerShell 5.1 compatible)
# Explicitly handles missing origin and missing tags without native stderr -> terminating errors.
$ErrorActionPreference = 'Stop'
$repoUrl  = 'https://github.com/vizisiklo/HirRadar.git'
$repoPage = 'https://github.com/vizisiklo/HirRadar'
$tagName  = 'v1.0.2'
Set-Location -LiteralPath $PSScriptRoot

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $GitArgs)
    & git @GitArgs
    if ($LASTEXITCODE -ne 0) {
        throw ('Git hiba (' + $LASTEXITCODE + '): git ' + ($GitArgs -join ' '))
    }
}

function Get-GitResult {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]] $GitArgs)
    $output = @(& git @GitArgs)
    if ($LASTEXITCODE -ne 0) {
        throw ('Git hiba (' + $LASTEXITCODE + '): git ' + ($GitArgs -join ' '))
    }
    return (($output | Out-String).Trim())
}

try {
    Write-Host 'HirRadar v1.0.2 - GitHub Private Repository - JAVITOTT FELTOLTO' -ForegroundColor Cyan
    Write-Host ('Cel: ' + $repoPage)
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git nincs telepitve. Letoltes: https://git-scm.com/downloads/win'
    }

    Write-Host '1/6: Git es hitelesites ellenorzese...'
    Invoke-Git --version
    $remoteRefs = @(& git ls-remote --heads --tags $repoUrl)
    if ($LASTEXITCODE -ne 0) {
        throw 'A GitHub nem erheto el a Git segitsegevel. Ellenorizd a bejelentkezest es a Git Credential Managert.'
    }
    $remoteMainSha = ''
    $remoteTagSha = ''
    foreach ($line in $remoteRefs) {
        if ($line -match '^([a-f0-9]{40})\s+refs/heads/main$') { $remoteMainSha = $Matches[1] }
        if ($line -match '^([a-f0-9]{40})\s+refs/tags/v1\.0\.2(\^\{\})?$') { $remoteTagSha = $Matches[1] }
    }

    Write-Host '2/6: Helyi repository ellenorzese...'
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot '.git'))) {
        Invoke-Git init
        Invoke-Git checkout -b main
    }
    $localBranch = Get-GitResult branch --show-current
    if ($localBranch -ne 'main') {
        throw ('A helyi ag nem main, hanem: ' + $localBranch + '. Nem valtok agat automatikusan.')
    }

    # Corrected: querying a missing remote via `git remote get-url origin 2>$null`
    # raises a terminating NativeCommandError on Windows PowerShell 5.1.
    $remoteNames = @(git remote)
    if ($LASTEXITCODE -ne 0) { throw 'Nem sikerult a git remote lista lekerese.' }
    if (-not ($remoteNames -contains 'origin')) {
        Invoke-Git remote add origin $repoUrl
        Write-Host 'Az origin kapcsolat letrehozva.' -ForegroundColor Green
    } else {
        $existingUrl = Get-GitResult remote get-url origin
        if ($existingUrl -ne $repoUrl) {
            throw ('A meglevo origin mas repositoryra mutat: ' + $existingUrl)
        }
        Write-Host 'Az origin kapcsolat mar helyes.'
    }

    # Local per-repository identity, not global Git settings.
    Invoke-Git config user.name 'Kozbeszol Media'
    Invoke-Git config user.email '105725498+vizisiklo@users.noreply.github.com'

    Write-Host '3/6: Forrasok commitolasa...'
    Invoke-Git add --all
    # `git diff --cached --quiet` returns 1 when staged changes exist; no stderr.
    & git diff --cached --quiet
    $diffCode = $LASTEXITCODE
    if ($diffCode -eq 1) {
        Invoke-Git commit -m 'Initial source release: HirRadar v1.0.2'
    } elseif ($diffCode -ne 0) {
        throw ('Nem sikerult ellenorizni a Git indexet: ' + $diffCode)
    }

    # No `git rev-parse ... 2>$null`: missing HEAD triggers a PS 5.1 error.
    & git show-ref --verify --quiet refs/heads/main
    if ($LASTEXITCODE -ne 0) {
        throw 'Hianyzik a helyi main commit, nincs mit feltolteni.'
    }
    $localHead = Get-GitResult rev-parse HEAD

    if ($remoteMainSha -and $remoteMainSha -ne $localHead) {
        # Safe retry in a freshly extracted folder when main has already been pushed.
        # Compare the exact file-tree, then align local HEAD to existing remote commit.
        Write-Host 'A GitHub main mar letezik. Ellenorzom a tartalom azonossagat...'
        Invoke-Git fetch --no-tags origin main
        $fetchedCommit = Get-GitResult rev-parse FETCH_HEAD
        if ($fetchedCommit -ne $remoteMainSha) {
            throw 'A GitHub main kozben megvaltozott. Nem irom felul; ellenorizni kell.'
        }
        $remoteTree = Get-GitResult rev-parse 'FETCH_HEAD^{tree}'
        $localTree  = Get-GitResult rev-parse 'HEAD^{tree}'
        if ($remoteTree -ne $localTree) {
            throw 'A GitHub main kulonbozik a helyi forrastol. Nem hajtok vegre force push-t.'
        }
        Invoke-Git reset --soft $remoteMainSha
        $localHead = Get-GitResult rev-parse HEAD
        Write-Host 'A mar feltoltott main tartalma egyezik a helyi forrassal.'
    }

    Write-Host '4/6: v1.0.2 verzio-cimke ellenorzese...'
    $localTags = @(git tag --list $tagName)
    if ($LASTEXITCODE -ne 0) { throw 'A helyi verzio-cimke ellenorzese sikertelen.' }
    if (-not ($localTags -contains $tagName)) {
        Invoke-Git tag -a $tagName -m 'HirRadar v1.0.2 - source release'
        Write-Host 'v1.0.2 cimke elkeszult.'
    } else {
        $tagCommit = Get-GitResult rev-list -n 1 $tagName
        if ($tagCommit -ne $localHead) {
            throw 'A helyi v1.0.2 cimke masik commitra mutat. Biztonsagi okbol nem irom felul.'
        }
        Write-Host 'A v1.0.2 cimke mar letezik es helyes.'
    }

    Write-Host '5/6: Main ag feltoltese...'
    if (-not $remoteMainSha) {
        Invoke-Git push -u origin main
    } else {
        Write-Host 'Main mar fent van, nincs szukseg uj push-ra.'
    }

    Write-Host '6/6: Verzio-cimke feltoltese...'
    if (-not $remoteTagSha) {
        Invoke-Git push origin $tagName
    } else {
        Write-Host 'A v1.0.2 cimke mar a GitHubon van.'
    }

    Write-Host ''
    Write-Host 'FELTOLTES SIKERES!' -ForegroundColor Green
    Write-Host ('Repository: ' + $repoPage)
    Write-Host ('Cimke: ' + $repoPage + '/tree/v1.0.2')
    Write-Host 'A repository PRIVATE marad.'
    exit 0
}
catch {
    Write-Host ''
    Write-Host ('HIBA: ' + $_.Exception.Message) -ForegroundColor Red
    Write-Host 'A script nem hasznal force push-t, es nem torli az alkalmazas helyi adatait.'
    exit 1
}
