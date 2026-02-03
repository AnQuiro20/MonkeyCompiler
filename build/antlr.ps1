param(
    [string]$GrammarFile,
    [string]$OutputDir
)

$antlrJar = "antlr-4.13.1-complete.jar"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$fullJarPath = Join-Path $scriptDir $antlrJar

if (-not (Test-Path $fullJarPath)) {
    Write-Error "ANTLR JAR no encontrado: $fullJarPath"
    Write-Host "Archivos en la carpeta build:" -ForegroundColor Yellow
    Get-ChildItem $scriptDir
    exit 1
}

Write-Host "Usando ANTLR JAR: $fullJarPath" -ForegroundColor Green

$fullGrammarPath = Resolve-Path $GrammarFile
$fullOutputDir = Resolve-Path $OutputDir -ErrorAction SilentlyContinue

if (-not $fullOutputDir) {
    $fullOutputDir = $OutputDir
}

Write-Host "Generando parser desde: $fullGrammarPath" -ForegroundColor Yellow
Write-Host "Directorio de salida: $fullOutputDir" -ForegroundColor Yellow

java -cp $fullJarPath org.antlr.v4.Tool $fullGrammarPath -o $fullOutputDir -Dlanguage=CSharp -visitor -listener

if ($LASTEXITCODE -eq 0) {
    Write-Host "Parser generado exitosamente en: $fullOutputDir" -ForegroundColor Green
} else {
    Write-Error "Error generando el parser"
}
