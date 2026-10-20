param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\artifacts\publish\win-x64\WinStats.exe'),
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\performance.json')
)

$ErrorActionPreference = 'Stop'
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$outputPath = [System.IO.Path]::GetFullPath($Output)
if ($outputPath.Contains('"')) { throw 'Output path must not contain a quotation mark.' }
if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Force }
$benchmarkProcess = Start-Process -FilePath $executablePath -ArgumentList "--benchmark-output `"$outputPath`"" -WindowStyle Hidden -PassThru
if (-not $benchmarkProcess.WaitForExit(120000)) {
    Stop-Process -Id $benchmarkProcess.Id -ErrorAction SilentlyContinue
    throw 'Benchmark did not finish within 120 seconds.'
}
if ($benchmarkProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $outputPath)) {
    throw 'Benchmark failed or another Win Stats instance is running. Close that instance and try again.'
}
$report = Get-Content -LiteralPath $outputPath -Raw | ConvertFrom-Json
foreach ($phase in @('idle', 'cpu-panel')) {
    $samples = @($report.readings | Where-Object Phase -eq $phase)
    if ($samples.Count -lt 20) { throw "Too few verified samples for $phase. Leave the default strip and CPU panel unchanged during the run." }
    [pscustomobject]@{
        Phase = $phase
        Samples = $samples.Count
        MeanCpuPercent = [math]::Round(($samples | Measure-Object CpuPercent -Average).Average, 3)
        MeanPrivateMiB = [math]::Round(($samples | Measure-Object PrivateBytes -Average).Average / 1MB, 1)
        MeanWorkingSetMiB = [math]::Round(($samples | Measure-Object WorkingSetBytes -Average).Average / 1MB, 1)
    }
}
