# Find link.exe (Microsoft linker) for Rust on Windows
# Run: .\find-linker.ps1

$found = $null
$roots = @(
    "C:\Program Files\Microsoft Visual Studio",
    "C:\Program Files (x86)\Microsoft Visual Studio"
)
foreach ($root in $roots) {
    if (-not (Test-Path $root)) { continue }
    $link = Get-ChildItem -Path $root -Filter "link.exe" -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match "MSVC.*\\bin\\" } |
        Select-Object -First 1
    if ($link) {
        $found = $link.DirectoryName
        break
    }
}

if ($found) {
    Write-Host "Found link.exe in:" -ForegroundColor Green
    Write-Host $found
    Write-Host ""
    Write-Host "Add to PATH for this session, then build:"
    Write-Host '  $env:Path += ";' + $found + '"' -ForegroundColor Cyan
    Write-Host "  cargo build --release -p aoe-sender -p aoe-receiver"
    Write-Host ""
    Write-Host "To add permanently: Win key -> type 'environment' -> Edit environment variables -> Path -> New -> paste the path above"
} else {
    Write-Host "link.exe not found. You have two options:" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "1) Install Build Tools for Visual Studio with C++:" -ForegroundColor White
    Write-Host "   https://visualstudio.microsoft.com/visual-cpp-build-tools/"
    Write-Host "   Run installer -> select 'Desktop development with C++' -> Install."
    Write-Host "   Then run this script again to find link.exe."
    Write-Host ""
    Write-Host "2) Use Rust GNU toolchain (no Visual Studio needed):" -ForegroundColor White
    Write-Host "   Install MSYS2 from https://www.msys2.org/"
    Write-Host "   In MSYS2 terminal run: pacman -S mingw-w64-ucrt-x86_64-toolchain"
    Write-Host "   Then in PowerShell:"
    Write-Host "   rustup toolchain install stable-x86_64-pc-windows-gnu"
    Write-Host "   rustup default stable-x86_64-pc-windows-gnu"
    Write-Host "   Add to PATH: C:\msys64\ucrt64\bin  (or your MSYS2 install path)"
    Write-Host "   Then: cargo build --release -p aoe-sender -p aoe-receiver"
}
