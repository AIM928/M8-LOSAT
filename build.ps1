param(
 [Parameter(Mandatory=$true)][string]$GameRoot,
 [Parameter(Mandatory=$true)][string]$ReleaseRoot,
 [Parameter(Mandatory=$true)][string]$OutputRoot,
 [Parameter(Mandatory=$true)][string]$CscPath
)
$ErrorActionPreference='Stop'
# PowerShell 7 / .NET SDK 7. Use a new output directory, preferably on D:.
if(Test-Path -LiteralPath $OutputRoot){throw 'Choose a new output directory; existing files will not be overwritten.'}
$sourceManifest=Get-Content (Join-Path $PSScriptRoot source-manifest.json) -Raw|ConvertFrom-Json
$releaseDll=Join-Path $ReleaseRoot Mods/M8LOSATM2.dll
if((Get-FileHash -LiteralPath $releaseDll).Hash -ne 'FA743A7E116BC8D814AFE7FD1DFECD93631E4C617FB03B2B1BA5F93929B2547F'){throw 'Use the unmodified v0.1.69 installation package as the resource input.'}
$managed=Join-Path $GameRoot GHPC_Data/Managed
$refs=@('mscorlib.dll','System.dll','System.Core.dll','netstandard.dll','Assembly-CSharp.dll','UnityEngine.CoreModule.dll','UnityEngine.PhysicsModule.dll','UnityEngine.ImageConversionModule.dll','UnityEngine.AudioModule.dll','UnityEngine.ParticleSystemModule.dll','UnityEngine.TerrainPhysicsModule.dll','UnityEngine.AnimationModule.dll','UnityEngine.InputLegacyModule.dll','UnityEngine.IMGUIModule.dll','UnityEngine.TextRenderingModule.dll','Unity.Postprocessing.Runtime.dll','Unity.TextMeshPro.dll','FMODUnity.dll','Rewired_Core.dll')|ForEach-Object {Join-Path $managed $_}
$refs+=Join-Path $GameRoot MelonLoader/net35/MelonLoader.dll
$refs+=Join-Path $GameRoot MelonLoader/net35/0Harmony.dll
foreach($p in $refs+@($CscPath)){if(!(Test-Path -LiteralPath $p)){throw "Missing build input: $p"}}
$sources=@(foreach($p in $sourceManifest.PSObject.Properties){$file=Join-Path $PSScriptRoot ('src/'+$p.Name);if((Get-FileHash -LiteralPath $file).Hash -ne $p.Value){Write-Warning "Modified source: $($p.Name)"};$file})
New-Item -ItemType Directory -Path (Join-Path $OutputRoot resources) -Force|Out-Null
$mods=Join-Path $OutputRoot Mods
New-Item -ItemType Directory -Path $mods|Out-Null
# Read embedded data only; do not instantiate or execute the old mod.
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($releaseDll))
$resources=@();$index=0
foreach($name in $assembly.GetManifestResourceNames()){
 $path=Join-Path $OutputRoot ('resources/'+($index++)+'.bin')
 $inputStream=$assembly.GetManifestResourceStream($name)
 $outputStream=[IO.File]::Create($path)
 try{$inputStream.CopyTo($outputStream)}finally{$inputStream.Dispose();$outputStream.Dispose()}
 $resources+='/resource:'+$path+','+$name
}
if($resources.Count -ne 8){throw 'Unexpected embedded resource count'}
$args=@($CscPath,'/nologo','/target:library','/langversion:latest',('/out:'+(Join-Path $mods M8LOSATM2.dll)))+@($refs|ForEach-Object {'/reference:'+$_})+$sources+$resources
& dotnet @args
if($LASTEXITCODE -ne 0){throw 'Compilation failed'}
Copy-Item -LiteralPath (Join-Path $ReleaseRoot Mods/M8LOSATAssets) -Destination $mods -Recurse
Get-FileHash -LiteralPath (Join-Path $mods M8LOSATM2.dll)
