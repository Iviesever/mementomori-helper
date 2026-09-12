param(
    [Parameter(Mandatory = $true)][string]$Project,
    [Parameter(Mandatory = $true)][string]$OutputPath
)
$ErrorActionPreference = 'Stop'
$parent = Split-Path ([IO.Path]::GetFullPath($OutputPath)) -Parent
New-Item -ItemType Directory -Force -Path $parent | Out-Null
# .NET 9 verb-first syntax is also accepted by the .NET 10 SDK.
$reportText = (& dotnet list $Project package --vulnerable --include-transitive --format json --output-version 1) -join "`n"
$exitCode = $LASTEXITCODE
[IO.File]::WriteAllText([IO.Path]::GetFullPath($OutputPath), $reportText, [Text.UTF8Encoding]::new($false))
if ($exitCode -ne 0) { throw 'Dependency audit command failed; this is not a clean scan.' }
$report = $reportText | ConvertFrom-Json
if ($report.version -ne 1 -or @($report.projects).Count -eq 0) { throw 'Incomplete dependency audit report.' }
if (@($report.problems).Count -gt 0 -and $null -ne $report.problems) { throw 'Dependency audit reported problems.' }
$count = 0
foreach ($projectResult in $report.projects) {
    if ($null -ne $projectResult.logs -and @($projectResult.logs).Count -gt 0) {
        throw 'Dependency audit could not completely inspect a project.'
    }
    foreach ($framework in $projectResult.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($null -eq $package) { continue }
            foreach ($vulnerability in $package.vulnerabilities) {
                $count++
                Write-Host ("{0} {1}: {2} {3}" -f $package.id, $package.resolvedVersion, $vulnerability.severity, $vulnerability.advisoryurl)
            }
        }
    }
}
if ($count -gt 0) { throw "Dependency audit found $count known vulnerabilities. No findings were suppressed." }
Write-Host 'Dependency audit passed: no known vulnerable packages reported, including transitive packages.'
