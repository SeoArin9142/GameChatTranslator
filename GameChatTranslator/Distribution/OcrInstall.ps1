param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("EasyOCR", "PaddleOCR", "Tesseract")]
    [string]$Engine
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appFolderName = "GameChatTranslator"
$portableMarkerFileName = "portable.mode"
$ocrSectionName = "OCR"

function Write-Status {
    param([string]$Message)
    Write-Host "[INFO] $Message"
}

function Write-Success {
    param([string]$Message)
    Write-Host "[OK] $Message"
}

function Get-AppConfigInfo {
    $installRoot = [System.IO.Path]::GetFullPath($scriptRoot)
    $portableConfigPath = Join-Path $installRoot "config.ini"
    $portableMarkerPath = Join-Path $installRoot $portableMarkerFileName
    $isPortable = (Test-Path $portableConfigPath) -or (Test-Path $portableMarkerPath)

    if ($isPortable) {
        $rootDirectory = $installRoot
    } else {
        $rootDirectory = Join-Path $env:LOCALAPPDATA $appFolderName
    }

    $configPath = Join-Path $rootDirectory "config.ini"
    $venvRoot = Join-Path $rootDirectory "venvs"
    [System.IO.Directory]::CreateDirectory($rootDirectory) | Out-Null
    [System.IO.Directory]::CreateDirectory($venvRoot) | Out-Null

    return [pscustomobject]@{
        InstallRoot = $installRoot
        RootDirectory = $rootDirectory
        ConfigPath = $configPath
        VenvRoot = $venvRoot
        IsPortable = $isPortable
    }
}

function Set-IniValue {
    param(
        [string]$ConfigPath,
        [string]$Section,
        [string]$Key,
        [string]$Value
    )

    $configDirectory = Split-Path -Parent $ConfigPath
    if (-not [string]::IsNullOrWhiteSpace($configDirectory)) {
        [System.IO.Directory]::CreateDirectory($configDirectory) | Out-Null
    }

    $lines = New-Object System.Collections.Generic.List[string]
    if (Test-Path $ConfigPath) {
        foreach ($line in [System.IO.File]::ReadAllLines($ConfigPath)) {
            [void]$lines.Add($line)
        }
    }

    $sectionStart = -1
    $sectionEnd = $lines.Count
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $trimmed = $lines[$i].Trim()
        if (-not ($trimmed.StartsWith("[") -and $trimmed.EndsWith("]"))) {
            continue
        }

        $sectionName = $trimmed.Substring(1, $trimmed.Length - 2)
        if ($sectionStart -lt 0) {
            if ($sectionName.Equals($Section, [System.StringComparison]::OrdinalIgnoreCase)) {
                $sectionStart = $i
            }

            continue
        }

        $sectionEnd = $i
        break
    }

    if ($sectionStart -lt 0) {
        if ($lines.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($lines[$lines.Count - 1])) {
            [void]$lines.Add("")
        }

        [void]$lines.Add("[$Section]")
        [void]$lines.Add("$Key=$Value")
        [System.IO.File]::WriteAllLines($ConfigPath, $lines)
        return
    }

    for ($i = $sectionStart + 1; $i -lt $sectionEnd; $i++) {
        $trimmed = $lines[$i].Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith(";") -or $trimmed.StartsWith("#")) {
            continue
        }

        $separatorIndex = $trimmed.IndexOf("=")
        if ($separatorIndex -lt 0) {
            continue
        }

        $existingKey = $trimmed.Substring(0, $separatorIndex).Trim()
        if ($existingKey.Equals($Key, [System.StringComparison]::OrdinalIgnoreCase)) {
            $lines[$i] = "$Key=$Value"
            [System.IO.File]::WriteAllLines($ConfigPath, $lines)
            return
        }
    }

    $insertIndex = $sectionEnd
    [void]$lines.Insert($insertIndex, "$Key=$Value")
    [System.IO.File]::WriteAllLines($ConfigPath, $lines)
}

function Get-PythonInfoFromExecutable {
    param([string]$PythonExe)

    if ([string]::IsNullOrWhiteSpace($PythonExe) -or -not (Test-Path $PythonExe)) {
        return $null
    }

    try {
        $json = & $PythonExe -c "import json,sys; print(json.dumps({'major':sys.version_info[0],'minor':sys.version_info[1],'micro':sys.version_info[2],'executable':sys.executable}))" 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
            return $null
        }

        $parsed = $json | ConvertFrom-Json
        return [pscustomobject]@{
            Executable = [string]$parsed.executable
            Major = [int]$parsed.major
            Minor = [int]$parsed.minor
            Micro = [int]$parsed.micro
        }
    } catch {
        return $null
    }
}

function Get-PythonInfoFromPyLauncher {
    param([string]$VersionArgument)

    $pyCommand = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($null -eq $pyCommand) {
        return $null
    }

    try {
        $json = & $pyCommand.Source $VersionArgument -c "import json,sys; print(json.dumps({'major':sys.version_info[0],'minor':sys.version_info[1],'micro':sys.version_info[2],'executable':sys.executable}))" 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($json)) {
            return $null
        }

        $parsed = $json | ConvertFrom-Json
        return [pscustomobject]@{
            Executable = [string]$parsed.executable
            Major = [int]$parsed.major
            Minor = [int]$parsed.minor
            Micro = [int]$parsed.micro
        }
    } catch {
        return $null
    }
}

function Get-InstalledPythonCandidates {
    $candidates = New-Object System.Collections.Generic.List[string]

    $pythonCommand = Get-Command python.exe -ErrorAction SilentlyContinue
    if ($null -ne $pythonCommand -and -not [string]::IsNullOrWhiteSpace($pythonCommand.Source)) {
        [void]$candidates.Add($pythonCommand.Source)
    }

    $searchPatterns = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Python\Python*\python.exe"),
        "C:\Program Files\Python*\python.exe",
        "C:\Program Files (x86)\Python*\python.exe"
    )

    foreach ($pattern in $searchPatterns) {
        foreach ($file in Get-ChildItem -Path $pattern -File -ErrorAction SilentlyContinue) {
            [void]$candidates.Add($file.FullName)
        }
    }

    return $candidates |
        Select-Object -Unique |
        ForEach-Object { Get-PythonInfoFromExecutable $_ } |
        Where-Object { $null -ne $_ }
}

function Find-EasyOcrBasePython {
    $candidates = New-Object System.Collections.Generic.List[object]

    foreach ($versionArgument in @("-3.13", "-3.12", "-3.11", "-3.10", "-3.9", "-3.8")) {
        $info = Get-PythonInfoFromPyLauncher -VersionArgument $versionArgument
        if ($null -ne $info) {
            [void]$candidates.Add($info)
        }
    }

    foreach ($info in Get-InstalledPythonCandidates) {
        [void]$candidates.Add($info)
    }

    return $candidates |
        Group-Object Executable |
        ForEach-Object { $_.Group[0] } |
        Where-Object { $_.Major -eq 3 -and $_.Minor -ge 8 } |
        Sort-Object Minor, Micro -Descending |
        Select-Object -First 1
}

function Find-Python310 {
    $candidates = New-Object System.Collections.Generic.List[object]

    $launcherInfo = Get-PythonInfoFromPyLauncher -VersionArgument "-3.10"
    if ($null -ne $launcherInfo) {
        [void]$candidates.Add($launcherInfo)
    }

    foreach ($info in Get-InstalledPythonCandidates) {
        if ($info.Major -eq 3 -and $info.Minor -eq 10) {
            [void]$candidates.Add($info)
        }
    }

    return $candidates |
        Group-Object Executable |
        ForEach-Object { $_.Group[0] } |
        Sort-Object Micro -Descending |
        Select-Object -First 1
}

function Invoke-WingetInstall {
    param([string]$PackageId)

    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if ($null -eq $winget) {
        return $false
    }

    Write-Status "Attempting WinGet install: $PackageId"
    & $winget.Source install --id $PackageId -e --accept-package-agreements --accept-source-agreements --disable-interactivity
    return $LASTEXITCODE -eq 0
}

function Ensure-Python310 {
    $pythonInfo = Find-Python310
    if ($null -ne $pythonInfo) {
        return $pythonInfo
    }

    if (-not (Invoke-WingetInstall -PackageId "Python.Python.3.10")) {
        throw "Python 3.10 installation failed. Install Python 3.10 and rerun this script."
    }

    Start-Sleep -Seconds 2
    $pythonInfo = Find-Python310
    if ($null -eq $pythonInfo) {
        throw "Python 3.10 was installed, but python.exe could not be located automatically."
    }

    return $pythonInfo
}

function Ensure-Venv {
    param(
        [string]$BasePythonExe,
        [string]$VenvPath
    )

    $venvPython = Join-Path $VenvPath "Scripts\python.exe"
    if (-not (Test-Path $venvPython)) {
        Write-Status "Creating virtual environment: $VenvPath"
        & $BasePythonExe -m venv $VenvPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $venvPython)) {
            throw "Virtual environment creation failed: $VenvPath"
        }
    }

    return $venvPython
}

function Install-PythonPackages {
    param(
        [string]$PythonExe,
        [string[]]$Packages
    )

    Write-Status "Upgrading pip"
    & $PythonExe -m pip install -U pip
    if ($LASTEXITCODE -ne 0) {
        throw "pip upgrade failed."
    }

    Write-Status "Installing packages: $($Packages -join ', ')"
    & $PythonExe -m pip install @Packages
    if ($LASTEXITCODE -ne 0) {
        throw "Package installation failed."
    }
}

function Assert-EasyOcrEnvironment {
    param([string]$PythonExe)

    $output = & $PythonExe -c "import easyocr, torch, torchvision, sys; print(sys.executable)" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($output)) {
        throw "EasyOCR import verification failed."
    }

    return ($output | Select-Object -Last 1).Trim()
}

function Assert-PaddleOcrEnvironment {
    param([string]$PythonExe)

    $output = & $PythonExe -c "import paddle, paddleocr, sys; print(paddle.__version__); print(paddleocr.__version__); print(sys.executable)" 2>$null
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($output)) {
        throw "PaddleOCR import verification failed."
    }

    $lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -lt 3) {
        throw "PaddleOCR version verification output was incomplete."
    }

    if ($lines[0].Trim() -ne "3.2.0" -or $lines[1].Trim() -ne "3.3.3") {
        throw "Unexpected Paddle package versions. Expected paddlepaddle 3.2.0 and paddleocr 3.3.3."
    }

    return $lines[2].Trim()
}

function Find-TesseractExecutable {
    $candidates = New-Object System.Collections.Generic.List[string]

    $tesseractCommand = Get-Command tesseract.exe -ErrorAction SilentlyContinue
    if ($null -ne $tesseractCommand -and -not [string]::IsNullOrWhiteSpace($tesseractCommand.Source)) {
        [void]$candidates.Add($tesseractCommand.Source)
    }

    foreach ($path in @(
        "C:\Program Files\Tesseract-OCR\tesseract.exe",
        "C:\Program Files (x86)\Tesseract-OCR\tesseract.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Tesseract-OCR\tesseract.exe")
    )) {
        if (Test-Path $path) {
            [void]$candidates.Add($path)
        }
    }

    return $candidates | Select-Object -Unique | Select-Object -First 1
}

function Ensure-TesseractExecutable {
    $tesseractExe = Find-TesseractExecutable
    if (-not [string]::IsNullOrWhiteSpace($tesseractExe)) {
        return $tesseractExe
    }

    foreach ($packageId in @("UB-Mannheim.TesseractOCR", "tesseract-ocr.tesseract")) {
        if (Invoke-WingetInstall -PackageId $packageId) {
            Start-Sleep -Seconds 2
            $tesseractExe = Find-TesseractExecutable
            if (-not [string]::IsNullOrWhiteSpace($tesseractExe)) {
                return $tesseractExe
            }
        }
    }

    throw "Tesseract executable could not be found or installed automatically."
}

$configInfo = Get-AppConfigInfo
Write-Status ("Config path: " + $configInfo.ConfigPath)

switch ($Engine) {
    "EasyOCR" {
        $pythonInfo = Find-EasyOcrBasePython
        if ($null -eq $pythonInfo) {
            throw "Python 3.8 or newer was not found. Install Python and rerun Install-EasyOCR.bat."
        }

        Write-Status ("Using base Python: " + $pythonInfo.Executable)
        $venvPath = Join-Path $configInfo.VenvRoot "easyocr"
        $venvPython = Ensure-Venv -BasePythonExe $pythonInfo.Executable -VenvPath $venvPath
        Install-PythonPackages -PythonExe $venvPython -Packages @("torch", "torchvision", "easyocr")
        $verifiedPython = Assert-EasyOcrEnvironment -PythonExe $venvPython
        Set-IniValue -ConfigPath $configInfo.ConfigPath -Section $ocrSectionName -Key "EasyOcrPythonPath" -Value $verifiedPython
        Write-Success ("EasyOCR installation completed.")
        Write-Success ("EasyOcrPythonPath=" + $verifiedPython)
    }
    "PaddleOCR" {
        $pythonInfo = Ensure-Python310
        Write-Status ("Using Python 3.10: " + $pythonInfo.Executable)
        $venvPath = Join-Path $configInfo.VenvRoot "paddleocr310"
        $venvPython = Ensure-Venv -BasePythonExe $pythonInfo.Executable -VenvPath $venvPath
        Install-PythonPackages -PythonExe $venvPython -Packages @("paddlepaddle==3.2.0", "paddleocr==3.3.3")
        $verifiedPython = Assert-PaddleOcrEnvironment -PythonExe $venvPython
        Set-IniValue -ConfigPath $configInfo.ConfigPath -Section $ocrSectionName -Key "PaddleOcrPythonPath" -Value $verifiedPython
        Write-Success ("PaddleOCR installation completed.")
        Write-Success ("PaddleOcrPythonPath=" + $verifiedPython)
    }
    "Tesseract" {
        $tesseractExe = Ensure-TesseractExecutable
        Set-IniValue -ConfigPath $configInfo.ConfigPath -Section $ocrSectionName -Key "TesseractExePath" -Value $tesseractExe
        Write-Success ("Tesseract installation completed.")
        Write-Success ("TesseractExePath=" + $tesseractExe)
    }
}
