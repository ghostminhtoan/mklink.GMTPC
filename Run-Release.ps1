param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'

try {
    & cmd.exe /c "taskkill /F /IM MKLink.exe /T" > $null 2>&1
} catch {
    # Neu app chua chay hoac khong kill duoc thi tiep tuc build de lay trang thai moi.
}

$msbuildCandidates = @(
    'C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe'
)

$msbuild = $null
foreach ($candidate in $msbuildCandidates) {
    if (Test-Path $candidate) {
        $msbuild = $candidate
        break
    }
}

if (-not $msbuild) {
    throw 'Khong tim thay MSBuild.exe phu hop tren may nay.'
}

$projectFile = Join-Path $ProjectRoot 'MKLink.csproj'
& $msbuild $projectFile /p:Configuration=Release /p:Platform=AnyCPU
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$exePath = Join-Path $ProjectRoot 'bin\Release\MKLink.exe'
Start-Process -FilePath $exePath -WorkingDirectory $ProjectRoot
