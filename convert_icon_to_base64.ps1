# Convert appicon.png to base64 and generate SVG with embedded icon
$iconPath = "src\Resources\AppIcon\appicon.png"

if (Test-Path $iconPath) {
    # Read the PNG file and convert to base64
    $iconBytes = [System.IO.File]::ReadAllBytes($iconPath)
    $base64Icon = [System.Convert]::ToBase64String($iconBytes)
    
    # Create the SVG with embedded base64 PNG
    $svg = @"
<?xml version="1.0" encoding="UTF-8"?>
<svg viewBox="0 0 512 512" xmlns="http://www.w3.org/2000/svg">
  <defs>
    <radialGradient id="bgGradient" cx="50%" cy="50%" r="65%">
      <stop offset="0%" style="stop-color:#2a2a2e;stop-opacity:1" />
      <stop offset="100%" style="stop-color:#17171a;stop-opacity:1" />
    </radialGradient>
  </defs>
  
  <!-- Main background -->
  <rect width="512" height="512" fill="#17171a"/>
  
  <!-- Gradient overlay circle -->
  <circle cx="256" cy="256" r="256" fill="url(#bgGradient)"/>
  
  <!-- Decorative circles -->
  <circle cx="256" cy="256" r="180" fill="none" stroke="#2a2a2e" stroke-width="1" opacity="0.3"/>
  <circle cx="256" cy="256" r="220" fill="none" stroke="#3a3a3e" stroke-width="1" opacity="0.2"/>
  
  <!-- Icon background circle -->
  <circle cx="256" cy="256" r="120" fill="#1a1a1e" opacity="0.6"/>
  
  <!-- App icon - embedded as base64 PNG -->
  <image x="128" y="128" width="256" height="256" href="data:image/png;base64,$base64Icon" preserveAspectRatio="xMidYMid meet"/>
</svg>
"@

    # Write to splash.svg
    $svg | Set-Content -Path "src\Resources\Splash\splash.svg" -Encoding UTF8
    Write-Host "? Successfully embedded icon in splash screen!"
    Write-Host "  File: src\Resources\Splash\splash.svg"
}
else {
    Write-Host "? Icon file not found at: $iconPath"
}
