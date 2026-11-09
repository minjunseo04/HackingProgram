param(
  [Parameter(Mandatory = $true)][int]$ParentPid,
  [Parameter(Mandatory = $true)][string]$ArchivePath,
  [Parameter(Mandatory = $true)][string]$InstallDirectory,
  [Parameter(Mandatory = $true)][string]$ExpectedSha256
)

$ErrorActionPreference = 'Stop'
$backup = $null
$destination = Join-Path $InstallDirectory 'BoundaryCheck.exe'
$workDirectory = Join-Path $env:TEMP ("BoundaryCheck-update-" + [guid]::NewGuid().ToString('N'))

try {
  while (Get-Process -Id $ParentPid -ErrorAction SilentlyContinue) { Start-Sleep -Milliseconds 400 }

  if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$') { throw '업데이트 확인값 형식이 올바르지 않습니다.' }
  $actualHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash
  if ($actualHash -ne $ExpectedSha256) { throw '업데이트 파일 확인값이 일치하지 않습니다.' }

  Expand-Archive -LiteralPath $ArchivePath -DestinationPath $workDirectory -Force
  $candidate = Join-Path $workDirectory 'BoundaryCheck.exe'
  if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw '업데이트 파일에 프로그램이 없습니다.' }

  $staged = Join-Path $InstallDirectory ('.BoundaryCheck-' + [guid]::NewGuid().ToString('N') + '.exe')
  Copy-Item -LiteralPath $candidate -Destination $staged
  if (Test-Path -LiteralPath $destination) {
    $backup = $destination + '.previous'
    if (Test-Path -LiteralPath $backup) { Remove-Item -LiteralPath $backup -Force }
    Move-Item -LiteralPath $destination -Destination $backup
  }
  try {
    Move-Item -LiteralPath $staged -Destination $destination
  }
  catch {
    if ($backup -and (Test-Path -LiteralPath $backup)) { Move-Item -LiteralPath $backup -Destination $destination -Force }
    throw
  }

  Start-Process -FilePath $destination
  if ($backup -and (Test-Path -LiteralPath $backup)) { Remove-Item -LiteralPath $backup -Force }
}
catch {
  Add-Type -AssemblyName PresentationFramework
  [System.Windows.MessageBox]::Show("업데이트를 적용하지 못했습니다.`n`n$($_.Exception.Message)", 'BoundaryCheck 업데이트', 'OK', 'Warning') | Out-Null
}
finally {
  if (Test-Path -LiteralPath $workDirectory) { Remove-Item -LiteralPath $workDirectory -Recurse -Force -ErrorAction SilentlyContinue }
}
