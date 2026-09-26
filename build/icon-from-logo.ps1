# Builds the application icon from a source logo image.
#
#   powershell -ExecutionPolicy Bypass -File build\icon-from-logo.ps1 -Source D:\branding\zeazdev-logo.png
#
# Takes a square-ish source PNG and packs ten sizes (16..256) as PNG-compressed entries into a
# single .ico, which is what a current Windows shell expects. PNG-in-ICO keeps the antialiasing
# that BMP entries would destroy, and needs no external tool: System.Drawing is in the box.
#
# The script validates what it wrote (magic, entry count, every blob's PNG signature) and fails
# loudly rather than shipping a corrupt icon that Explorer would silently ignore.

param(
  [Parameter(Mandatory = $true)]
  [string]$Source,
  [string]$Out = "",
  [int[]]$Sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if ([string]::IsNullOrWhiteSpace($Out)) {
  $Out = Join-Path $PSScriptRoot "..\src\zEClass\Assets\zEClass.ico"
}

if (-not (Test-Path -LiteralPath $Source)) {
  Write-Host "source image not found: $Source" -ForegroundColor Red
  exit 1
}

$origin = [Drawing.Bitmap]::FromFile($Source)
try {
  $side = [Math]::Min($origin.Width, $origin.Height)
  if ($side -lt 256) {
    Write-Host ("warning: source is only {0}x{1}; the 256 px frame will be upscaled" -f `
      $origin.Width, $origin.Height) -ForegroundColor Yellow
  }

  # Centre-crop to a square first, so a rectangular logo does not come out squashed.
  $cropX = [int](($origin.Width - $side) / 2)
  $cropY = [int](($origin.Height - $side) / 2)

  $blobs = @()
  foreach ($size in $Sizes) {
    $frame = New-Object Drawing.Bitmap($size, $size)
    try {
      $g = [Drawing.Graphics]::FromImage($frame)
      try {
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.DrawImage($origin,
          (New-Object Drawing.Rectangle(0, 0, $size, $size)),
          (New-Object Drawing.Rectangle($cropX, $cropY, $side, $side)),
          [Drawing.GraphicsUnit]::Pixel)
      }
      finally {
        $g.Dispose()
      }

      $stream = New-Object IO.MemoryStream
      try {
        $frame.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
        $blobs += ,$stream.ToArray()
      }
      finally {
        $stream.Dispose()
      }
    }
    finally {
      $frame.Dispose()
    }

    Write-Host ("  frame {0,4} px  {1,7} bytes" -f $size, $blobs[$blobs.Count - 1].Length)
  }
}
finally {
  $origin.Dispose()
}

$fs = [IO.File]::Create($Out)
try {
  $w = New-Object IO.BinaryWriter($fs)
  try {
    $w.Write([uint16]0)            # reserved
    $w.Write([uint16]1)            # type: icon
    $w.Write([uint16]$blobs.Count) # entry count

    $offset = 6 + (16 * $blobs.Count)
    for ($i = 0; $i -lt $blobs.Count; $i++) {
      # A zero dimension byte means 256, per the format.
      $dim = if ($Sizes[$i] -ge 256) { 0 } else { $Sizes[$i] }
      $w.Write([byte]$dim)         # width
      $w.Write([byte]$dim)         # height
      $w.Write([byte]0)            # palette entries
      $w.Write([byte]0)            # reserved
      $w.Write([uint16]1)          # planes
      $w.Write([uint16]32)         # bits per pixel
      $w.Write([uint32]$blobs[$i].Length)
      $w.Write([uint32]$offset)
      $offset += $blobs[$i].Length
    }

    foreach ($blob in $blobs) {
      $w.Write($blob)
    }
  }
  finally {
    $w.Dispose()
  }
}
finally {
  $fs.Dispose()
}

# ---------------------------------------------------------------- validate

$bytes = [IO.File]::ReadAllBytes($Out)
$ok = $true
if ($bytes[0] -ne 0 -or $bytes[1] -ne 0 -or $bytes[2] -ne 1 -or $bytes[3] -ne 0) {
  $ok = $false
}

$count = $bytes[4] + ($bytes[5] * 256)
if ($count -ne $blobs.Count) {
  $ok = $false
}

for ($i = 0; $i -lt $blobs.Count; $i++) {
  $entry = 6 + ($i * 16)
  $length = [BitConverter]::ToUInt32($bytes, $entry + 8)
  $at = [BitConverter]::ToUInt32($bytes, $entry + 12)
  if ($bytes[$at] -ne 0x89 -or $bytes[$at + 1] -ne 0x50 -or $bytes[$at + 2] -ne 0x4E -or
      $bytes[$at + 3] -ne 0x47) {
    $ok = $false
  }

  if ($length -ne $blobs[$i].Length) {
    $ok = $false
  }
}

if (-not $ok) {
  Write-Host "validation FAILED: $Out is not a well-formed icon" -ForegroundColor Red
  exit 1
}

$kb = [Math]::Round($bytes.Length / 1KB, 1)
Write-Host "wrote $Out ($count frames, $kb KB), validation passed" -ForegroundColor Green
