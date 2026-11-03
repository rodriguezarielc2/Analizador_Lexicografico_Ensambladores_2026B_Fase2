[CmdletBinding()]
param(
    [switch]$SinPruebaInterfaz
)

$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$deliverable = Join-Path $sourceRoot 'Entregable_Fase2'
$runtimeDirectory = Join-Path $sourceRoot 'build'
$runtimeExe = Join-Path $runtimeDirectory 'AnalizadorFase2.exe'
$zipPath = Join-Path $deliverable 'Entrega_Fase2.zip'
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$tempPrefix = $tempRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$workDirectory = [IO.Path]::GetFullPath((Join-Path $tempRoot ('Fase2-Build-' + [Guid]::NewGuid().ToString('N'))))
if (-not $workDirectory.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'La carpeta temporal quedó fuera de la ubicación prevista.'
}
$packageDirectory = Join-Path $workDirectory 'Entrega_Fase2'
$temporaryZip = Join-Path $workDirectory 'Entrega_Fase2.zip'
$candidateExe = Join-Path $workDirectory 'AnalizadorFase2.exe'
$catalogName = 'instrucciones_equipo2.txt'
$sources = @(
    'LexicalAnalysisCore.cs',
    'SyntaxSemanticModels.cs',
    'SyntaxSemanticCore.cs',
    'SyntaxSemanticResultsForm.cs',
    'AnalizadorFase2.cs'
)
$legacyInputFiles = @(
    'Plantilla_Asm.ens',
    'Explorador_Basico_Archivos.ens',
    'Programa2_Comparacion_De_Cadenas.ens'
)
$conformanceTemplateFiles = @(
    '01_tipos_y_datos.ens',
    '02_etiquetas_y_saltos.ens',
    '03_operandos_8086.ens'
)

try {
    New-Item -ItemType Directory -Path $workDirectory | Out-Null
    if (Test-Path -LiteralPath $deliverable) {
        $deliverableItem = Get-Item -LiteralPath $deliverable -Force
        if (-not $deliverableItem.PSIsContainer -or ($deliverableItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw 'Entregable_Fase2 debe ser una carpeta local normal; se canceló para evitar limpiar otro destino.'
        }
    }
    New-Item -ItemType Directory -Force -Path $deliverable | Out-Null

    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
    }
    if (-not (Test-Path -LiteralPath $compiler)) { throw 'No se encontró el compilador de .NET Framework de Windows.' }

    $frameworkDirectory = Split-Path -Parent $compiler
    $appArgs = @(
        '/nologo', '/target:winexe', '/optimize+',
        "/lib:$frameworkDirectory",
        "/reference:$(Join-Path $frameworkDirectory 'System.Windows.Forms.dll')",
        "/reference:$(Join-Path $frameworkDirectory 'System.Drawing.dll')",
        "/out:$candidateExe"
    )
    foreach ($file in $sources) { $appArgs += (Join-Path $sourceRoot $file) }
    & $compiler @appArgs
    if ($LASTEXITCODE -ne 0) { throw "La compilación de Fase 2 falló: $LASTEXITCODE" }

        $lexicalTestExe = Join-Path $workDirectory 'LexicalCoreTests.exe'
        $lexicalTestArgs = @('/nologo', '/target:exe', '/optimize+', "/out:$lexicalTestExe",
            (Join-Path $sourceRoot 'LexicalAnalysisCore.cs'),
            (Join-Path $sourceRoot 'verificacion\tests\AnalysisCoreTests.cs'))
        & $compiler @lexicalTestArgs
        if ($LASTEXITCODE -ne 0) { throw "No se pudieron compilar las pruebas léxicas: $LASTEXITCODE" }
        & $lexicalTestExe (Join-Path $sourceRoot 'verificacion\tests\Plantilla.ens') (Join-Path $sourceRoot 'verificacion\ejemplos')
        if ($LASTEXITCODE -ne 0) { throw "Fallaron las pruebas léxicas: $LASTEXITCODE" }

        $syntaxTestExe = Join-Path $workDirectory 'SyntaxSemanticTests.exe'
        $syntaxTestArgs = @('/nologo', '/target:exe', '/optimize+', "/out:$syntaxTestExe",
            (Join-Path $sourceRoot 'LexicalAnalysisCore.cs'),
            (Join-Path $sourceRoot 'SyntaxSemanticModels.cs'),
            (Join-Path $sourceRoot 'SyntaxSemanticCore.cs'),
            (Join-Path $sourceRoot 'verificacion\tests\SyntaxSemanticTests.cs'))
        & $compiler @syntaxTestArgs
        if ($LASTEXITCODE -ne 0) { throw "No se pudieron compilar las pruebas de Fase 2: $LASTEXITCODE" }
        & $syntaxTestExe
        if ($LASTEXITCODE -ne 0) { throw "Fallaron las pruebas sintáctico-semánticas: $LASTEXITCODE" }

        $missingLegacyInputs = @($legacyInputFiles | Where-Object {
            -not (Test-Path -LiteralPath (Join-Path $sourceRoot ('verificacion\ejemplos\' + $_)) -PathType Leaf)
        })
        if ($missingLegacyInputs.Count -eq 0) {
            $legacyInputTestExe = Join-Path $workDirectory 'LegacyInputProcessingTests.exe'
            $legacyInputTestArgs = @('/nologo', '/target:exe', '/optimize+', "/out:$legacyInputTestExe",
                (Join-Path $sourceRoot 'LexicalAnalysisCore.cs'),
                (Join-Path $sourceRoot 'SyntaxSemanticModels.cs'),
                (Join-Path $sourceRoot 'SyntaxSemanticCore.cs'),
                (Join-Path $sourceRoot 'verificacion\tests\LegacyInputProcessingTests.cs'))
            & $compiler @legacyInputTestArgs
            if ($LASTEXITCODE -ne 0) { throw "No se pudo compilar la verificación de entradas heredadas: $LASTEXITCODE" }
            & $legacyInputTestExe (Join-Path $sourceRoot 'verificacion\ejemplos') (Join-Path $sourceRoot $catalogName)
            if ($LASTEXITCODE -ne 0) { throw "Falló la verificación de procesamiento de entradas heredadas: $LASTEXITCODE" }
        }
        else {
            Write-Output ("Prueba de entradas heredadas omitida; no se publican copias de programas ASM del usuario: {0}" -f ($missingLegacyInputs -join ', '))
        }

        $conformanceTestExe = Join-Path $workDirectory 'ConformanceTemplateTests.exe'
        $conformanceTestArgs = @('/nologo', '/target:exe', '/optimize+', "/out:$conformanceTestExe",
            (Join-Path $sourceRoot 'LexicalAnalysisCore.cs'),
            (Join-Path $sourceRoot 'SyntaxSemanticModels.cs'),
            (Join-Path $sourceRoot 'SyntaxSemanticCore.cs'),
            (Join-Path $sourceRoot 'verificacion\tests\ConformanceTemplateTests.cs'))
        & $compiler @conformanceTestArgs
        if ($LASTEXITCODE -ne 0) { throw "No se pudieron compilar las pruebas de conformidad: $LASTEXITCODE" }
        & $conformanceTestExe (Join-Path $sourceRoot 'verificacion\plantillas_conformidad') (Join-Path $sourceRoot $catalogName)
        if ($LASTEXITCODE -ne 0) { throw "Falló la verificación de plantillas de conformidad: $LASTEXITCODE" }

        if (-not $SinPruebaInterfaz) {
            $guiTestExe = Join-Path $workDirectory 'GuiSmokeTests.exe'
            Copy-Item -LiteralPath (Join-Path $sourceRoot $catalogName) -Destination $workDirectory
            $guiTestArgs = @('/nologo', '/target:exe', '/optimize+', '/main:GuiSmokeTests',
                "/lib:$frameworkDirectory",
                "/reference:$(Join-Path $frameworkDirectory 'System.Windows.Forms.dll')",
                "/reference:$(Join-Path $frameworkDirectory 'System.Drawing.dll')",
                "/out:$guiTestExe")
            foreach ($file in $sources) { $guiTestArgs += (Join-Path $sourceRoot $file) }
            $guiTestArgs += (Join-Path $sourceRoot 'verificacion\tests\GuiSmokeTests.cs')
            & $compiler @guiTestArgs
            if ($LASTEXITCODE -ne 0) { throw "No se pudieron compilar las pruebas de interfaz: $LASTEXITCODE" }
            & $guiTestExe
            if ($LASTEXITCODE -ne 0) { throw "Fallaron las pruebas de interfaz: $LASTEXITCODE" }
        }
    else {
        Write-Output 'Prueba de interfaz omitida por solicitud; no se inició ninguna ventana.'
    }

    New-Item -ItemType Directory -Path $packageDirectory | Out-Null
    Copy-Item -LiteralPath $candidateExe -Destination $packageDirectory
    foreach ($file in $sources) { Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination $packageDirectory }
    Copy-Item -LiteralPath (Join-Path $sourceRoot $catalogName) -Destination $packageDirectory
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'README_ENTREGA.md') -Destination $packageDirectory
    $packageConformanceDirectory = Join-Path $packageDirectory 'plantillas_conformidad'
    New-Item -ItemType Directory -Path $packageConformanceDirectory | Out-Null
    foreach ($file in $conformanceTemplateFiles) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot ('verificacion\plantillas_conformidad\' + $file)) -Destination $packageConformanceDirectory
    }

    $manifestPath = Join-Path $packageDirectory 'MANIFEST.sha256'
    $manifestLines = @()
    foreach ($item in (Get-ChildItem -LiteralPath $packageDirectory -File -Recurse | Sort-Object FullName)) {
        $relativePath = $item.FullName.Substring($packageDirectory.Length).TrimStart('\').Replace('\', '/')
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.FullName).Hash.ToLowerInvariant()
        $manifestLines += ($hash + '  ' + $relativePath)
    }
    $utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
    [IO.File]::WriteAllLines($manifestPath, [string[]]$manifestLines, $utf8WithoutBom)
    Compress-Archive -Path (Join-Path $packageDirectory '*') -DestinationPath $temporaryZip -CompressionLevel Optimal
    $zipSize = (Get-Item -LiteralPath $temporaryZip).Length
    if ($zipSize -gt 5MB) { throw ("El ZIP excede 5 MB: {0:N0} bytes." -f $zipSize) }

    $deliverableCanonical = [IO.Path]::GetFullPath($deliverable)
    $expectedDeliverable = [IO.Path]::GetFullPath((Join-Path $sourceRoot 'Entregable_Fase2'))
    if (-not [String]::Equals($deliverableCanonical, $expectedDeliverable, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'La carpeta de entrega no coincide con Entregable_Fase2; se canceló la limpieza.'
    }
    $deliverablePrefix = $deliverableCanonical.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $sourceCopies = @{}
    foreach ($file in $sources) { $sourceCopies[$file] = Join-Path $sourceRoot $file }
    $sourceCopies[$catalogName] = Join-Path $sourceRoot $catalogName
    $sourceCopies['README_ENTREGA.md'] = Join-Path $sourceRoot 'README_ENTREGA.md'
    $generatedDirectories = @{
        'ejemplos' = @{ Source = 'verificacion\ejemplos'; Files = $legacyInputFiles }
        'ejemplos_heredados' = @{ Source = 'verificacion\ejemplos'; Files = $legacyInputFiles }
        'plantillas_conformidad' = @{ Source = 'verificacion\plantillas_conformidad'; Files = $conformanceTemplateFiles }
    }
    $generatedFiles = @{}
    foreach ($file in $sourceCopies.Keys) { $generatedFiles[$file] = $true }
    foreach ($file in @('AnalizadorFase2.exe', 'AnalizadorFase2_Actualizado.exe', 'README.md', 'MANIFEST.sha256', 'Entrega_Fase2.zip')) {
        $generatedFiles[$file] = $true
    }

    $existingOutput = @(Get-ChildItem -LiteralPath $deliverableCanonical -Force)
    foreach ($entry in $existingOutput) {
        $entryPath = [IO.Path]::GetFullPath($entry.FullName)
        if (-not $entryPath.StartsWith($deliverablePrefix, [StringComparison]::OrdinalIgnoreCase) -or
            ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw ("Se encontró una ruta de entrega inesperada o enlazada; no se limpió: {0}" -f $entry.FullName)
        }
        if ($entry.PSIsContainer) {
            if (-not $generatedDirectories.ContainsKey($entry.Name)) {
                throw ("No se borró una carpeta no reconocida de Entregable_Fase2: {0}" -f $entry.Name)
            }
            $nestedDirectories = @(Get-ChildItem -LiteralPath $entry.FullName -Directory -Recurse -Force)
            if ($nestedDirectories.Count -gt 0) { throw ("No se borró la carpeta con subdirectorios inesperados: {0}" -f $entry.FullName) }
            $mapping = $generatedDirectories[$entry.Name]
            $actualFiles = @(Get-ChildItem -LiteralPath $entry.FullName -File -Force)
            $actualNames = @($actualFiles | ForEach-Object { $_.Name } | Sort-Object)
            $expectedNames = @($mapping.Files | Sort-Object)
            if (Compare-Object -ReferenceObject $expectedNames -DifferenceObject $actualNames) {
                throw ("No se borró la carpeta; su contenido no coincide con las copias generadas esperadas: {0}" -f $entry.FullName)
            }
            foreach ($copy in $actualFiles) {
                $original = Join-Path (Join-Path $sourceRoot $mapping.Source) $copy.Name
                $copyHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $copy.FullName).Hash
                $originalHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $original).Hash
                if ($copyHash -ne $originalHash) { throw ("No se borró una copia que difiere de su original: {0}" -f $copy.FullName) }
            }
        }
        else {
            if (-not $generatedFiles.ContainsKey($entry.Name)) {
                throw ("No se borró un archivo no reconocido de Entregable_Fase2: {0}" -f $entry.Name)
            }
            if ($sourceCopies.ContainsKey($entry.Name)) {
                $copyHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.FullName).Hash
                $originalHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $sourceCopies[$entry.Name]).Hash
                if ($copyHash -ne $originalHash) { throw ("No se borró un archivo de entrega que difiere de su fuente: {0}" -f $entry.FullName) }
            }
        }
    }

    New-Item -ItemType Directory -Force -Path $runtimeDirectory | Out-Null
    try {
        Copy-Item -LiteralPath $candidateExe -Destination $runtimeExe -Force
    }
    catch {
        throw 'No se pudo actualizar la copia local para run.bat; cierra la aplicación si está abierta y vuelve a compilar.'
    }
    Copy-Item -LiteralPath (Join-Path $sourceRoot $catalogName) -Destination $runtimeDirectory -Force

    foreach ($entry in $existingOutput) {
        if ($entry.Name -eq 'Entrega_Fase2.zip' -and -not $entry.PSIsContainer) { continue }
        $entryPath = [IO.Path]::GetFullPath($entry.FullName)
        if (-not $entryPath.StartsWith($deliverablePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw ("Se rechazó limpiar fuera de Entregable_Fase2: {0}" -f $entry.FullName)
        }
        Remove-Item -LiteralPath $entry.FullName -Recurse:$entry.PSIsContainer -Force
    }
    $remaining = @(Get-ChildItem -LiteralPath $deliverableCanonical -Force)
    foreach ($entry in $remaining) {
        if ($entry.Name -ne 'Entrega_Fase2.zip' -or $entry.PSIsContainer) {
            throw ("Quedó un elemento inesperado en la carpeta de entrega: {0}" -f $entry.FullName)
        }
    }

    Copy-Item -LiteralPath $temporaryZip -Destination $zipPath -Force
    Write-Output ("ZIP único para Moodle: {0} ({1:N0} bytes)" -f $zipPath, $zipSize)
    Write-Output ("Copia local para run.bat: {0}" -f $runtimeExe)
    Write-Output 'Entregable_Fase2 contiene únicamente el ZIP; las entradas heredadas siguen fuera del paquete.'
}
finally {
    $resolvedWork = [IO.Path]::GetFullPath($workDirectory)
    if (-not $resolvedWork.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        -not ([IO.Path]::GetFileName($resolvedWork)).StartsWith('Fase2-Build-', [StringComparison]::Ordinal)) {
        throw 'Se rechazó eliminar una carpeta temporal fuera del destino previsto.'
    }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
