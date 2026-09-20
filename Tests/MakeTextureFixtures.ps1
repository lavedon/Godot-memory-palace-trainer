[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$fullFolder = Join-Path $Directory 'wall images'
$partialFolder = Join-Path $Directory 'partial walls'
$emptyFolder = Join-Path $Directory 'empty walls'
foreach ($folder in @($fullFolder,$partialFolder,$emptyFolder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
function Write-CalibrationImage([string]$Path, [string]$Title, [string]$Color, [int]$Width, [bool]$Jpeg = $false, [int]$Height = 560) {
    $bitmap = [Drawing.Bitmap]::new($Width,$Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $font = [Drawing.Font]::new('Arial',52,[Drawing.FontStyle]::Bold)
    $smallFont = [Drawing.Font]::new('Arial',22)
    try {
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml($Color))
        $graphics.FillRectangle([Drawing.Brushes]::White,0,0,230,65)
        $graphics.DrawString('TOP LEFT', $smallFont, [Drawing.Brushes]::Black, 16, 14)
        $graphics.FillRectangle([Drawing.Brushes]::Black,($Width - 250),($Height - 65),250,65)
        $graphics.DrawString('BOTTOM RIGHT', $smallFont, [Drawing.Brushes]::White, ($Width - 240),($Height - 50))
        $graphics.DrawString($Title, $font, [Drawing.Brushes]::White, 110, ($Height / 2 - 55))
        $graphics.DrawLine([Drawing.Pens]::White,60,100,60,($Height - 100))
        $graphics.DrawLine([Drawing.Pens]::White,60,100,45,125)
        $graphics.DrawLine([Drawing.Pens]::White,60,100,75,125)
        $format = if ($Jpeg) { [Drawing.Imaging.ImageFormat]::Jpeg } else { [Drawing.Imaging.ImageFormat]::Png }
        $bitmap.Save($Path,$format)
    } finally { $smallFont.Dispose(); $font.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
}
Write-CalibrationImage (Join-Path $fullFolder 'left.png') 'LEFT WALL' '#544b84' 1440
Write-CalibrationImage (Join-Path $fullFolder 'right.png') 'RIGHT WALL' '#38694e' 1440
Write-CalibrationImage (Join-Path $fullFolder 'forward.png') 'FORWARD WALL' '#426e91' 960
Write-CalibrationImage (Join-Path $fullFolder 'back.png') 'BACK WALL' '#8c5340' 960
Write-CalibrationImage (Join-Path $Directory 'left override.png') 'LEFT OVERRIDE' '#85544e' 1440
Write-CalibrationImage (Join-Path $Directory 'right photo.jpg') 'RIGHT JPEG' '#38694e' 1440 $true
Copy-Item -LiteralPath (Join-Path $fullFolder 'left.png') -Destination (Join-Path $partialFolder 'left.png')
Set-Content -LiteralPath (Join-Path $Directory 'corrupt.png') -Value 'This is not a PNG image.'
$roomFolder = Join-Path $Directory 'room images'
New-Item -ItemType Directory -Path $roomFolder -Force | Out-Null
foreach ($wall in @('left','right','forward','back')) {
    Copy-Item -LiteralPath (Join-Path $fullFolder "$wall.png") -Destination (Join-Path $roomFolder "$wall.png")
}
Write-CalibrationImage (Join-Path $roomFolder 'floor.png') 'FLOOR' '#466385' 960 -Height 1440
Write-CalibrationImage (Join-Path $roomFolder 'ceiling.png') 'CEILING' '#88643f' 960 -Height 1440
