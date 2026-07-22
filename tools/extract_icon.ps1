Add-Type -AssemblyName System.Drawing
$exe = 'src\ClipVault\bin\Release\net8.0-windows\ClipVault.exe'
$ico = [System.Drawing.Icon]::ExtractAssociatedIcon($exe)
$bmp = $ico.ToBitmap()
$out = 'src\ClipVault\Assets\extracted_exe_icon.png'
$bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host ("Extracted icon size: " + $bmp.Size)
Write-Host ("Saved: " + $out)
$c = $bmp.GetPixel([int]($bmp.Width/2), [int]($bmp.Height/2))
Write-Host ("Center pixel R/G/B/A: " + $c.R + "/" + $c.G + "/" + $c.B + "/" + $c.A)
$ico.Dispose()
$bmp.Dispose()
