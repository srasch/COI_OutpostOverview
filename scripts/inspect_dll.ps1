#Requires -Version 7.0
# Requires PowerShell 7+ (pwsh). Since game 0.8.8 the DLLs use C# default interface methods,
# which Windows PowerShell 5.1 (.NET Framework) can't load -- many UI types would silently go missing.
#
# Usage: pwsh -File scripts\inspect_dll.ps1 <TypeName> [DllName]
# Examples:
#   pwsh -File scripts\inspect_dll.ps1 ResearchManager Mafi.Core.dll
#   pwsh -File scripts\inspect_dll.ps1 PanelWithHeader Mafi.Unity.dll
#   pwsh -File scripts\inspect_dll.ps1 Display Mafi.Unity.dll
#   pwsh -File scripts\inspect_dll.ps1 Queueue Mafi.dll
#
# If DllName is omitted, searches all four game DLLs: Mafi.dll, Mafi.Core.dll, Mafi.Base.dll, Mafi.Unity.dll
# Outputs: constructors, public properties, public fields, public methods (declared only), base class chain, interfaces

param(
    [Parameter(Mandatory=$true)][string]$TypeName,
    [string]$DllName
)

# Game install path comes from the COI_ROOT environment variable.
# Falls back to the persisted User/Machine value in case this shell was started before it was set.
$coiRoot = $env:COI_ROOT
if (-not $coiRoot) { $coiRoot = [System.Environment]::GetEnvironmentVariable('COI_ROOT', 'User') }
if (-not $coiRoot) { $coiRoot = [System.Environment]::GetEnvironmentVariable('COI_ROOT', 'Machine') }
if (-not $coiRoot) {
    Write-Host "ERROR: COI_ROOT environment variable is not set. Point it at your Captain of Industry install folder." -ForegroundColor Red
    exit 1
}

$basePath = Join-Path $coiRoot "Captain of Industry_Data\Managed"
if (-not (Test-Path $basePath)) {
    Write-Host "ERROR: Game DLL folder not found: $basePath" -ForegroundColor Red
    exit 1
}

if ($DllName) {
    $dllList = @($DllName)
} else {
    $dllList = @("Mafi.dll", "Mafi.Core.dll", "Mafi.Base.dll", "Mafi.Unity.dll")
}

$found = $false

foreach ($dll in $dllList) {
    $dllPath = Join-Path $basePath $dll
    if (-not (Test-Path $dllPath)) {
        Write-Host "DLL not found: $dllPath" -ForegroundColor Red
        continue
    }

    $types = @()
    try {
        $asm = [System.Reflection.Assembly]::LoadFrom($dllPath)
        $types = $asm.GetTypes()
    } catch [System.Reflection.ReflectionTypeLoadException] {
        $types = $_.Exception.Types | Where-Object { $_ -ne $null }
    }

    $matches = $types | Where-Object { $_.Name -eq $TypeName }

    foreach ($t in $matches) {
        $found = $true
        Write-Host "`n============================================" -ForegroundColor Cyan
        Write-Host "  $($t.FullName)" -ForegroundColor Cyan
        Write-Host "  DLL: $dll | Public: $($t.IsPublic) | Abstract: $($t.IsAbstract) | Sealed: $($t.IsSealed)" -ForegroundColor Cyan
        Write-Host "============================================" -ForegroundColor Cyan

        # Base class chain
        Write-Host "`n--- Inheritance ---" -ForegroundColor Yellow
        $current = $t.BaseType
        $depth = 1
        while ($current -ne $null) {
            Write-Host ("  " * $depth + $current.FullName)
            $current = $current.BaseType
            $depth++
        }

        # Interfaces
        $ifaces = $t.GetInterfaces()
        if ($ifaces.Count -gt 0) {
            Write-Host "`n--- Interfaces ---" -ForegroundColor Yellow
            foreach ($iface in $ifaces) {
                Write-Host "  $($iface.FullName)"
            }
        }

        # Constructors
        $ctors = $t.GetConstructors([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance)
        if ($ctors.Count -gt 0) {
            Write-Host "`n--- Constructors ---" -ForegroundColor Yellow
            foreach ($c in $ctors) {
                $p = ($c.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
                Write-Host "  ctor($p)"
            }
        }

        # Public properties (declared only)
        $props = $t.GetProperties([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::DeclaredOnly)
        if ($props.Count -gt 0) {
            Write-Host "`n--- Properties (declared) ---" -ForegroundColor Yellow
            foreach ($prop in $props) {
                $acc = @()
                if ($prop.CanRead) { $acc += "get" }
                if ($prop.CanWrite) { $acc += "set" }
                Write-Host "  $($prop.PropertyType.Name) $($prop.Name) { $($acc -join '; ') }"
            }
        }

        # Public fields (declared only)
        $fields = $t.GetFields([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly)
        if ($fields.Count -gt 0) {
            Write-Host "`n--- Fields (declared) ---" -ForegroundColor Yellow
            foreach ($f in $fields) {
                $mod = if ($f.IsStatic) { "static " } else { "" }
                Write-Host "  ${mod}$($f.FieldType.Name) $($f.Name)"
            }
        }

        # Public methods (declared only, excluding property accessors)
        $methods = $t.GetMethods([System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::DeclaredOnly) |
            Where-Object { -not $_.IsSpecialName }
        if ($methods.Count -gt 0) {
            Write-Host "`n--- Methods (declared) ---" -ForegroundColor Yellow
            foreach ($m in $methods) {
                $mod = if ($m.IsStatic) { "static " } else { "" }
                $p = ($m.GetParameters() | ForEach-Object { "$($_.ParameterType.Name) $($_.Name)" }) -join ", "
                Write-Host "  ${mod}$($m.ReturnType.Name) $($m.Name)($p)"
            }
        }
    }
}

# Fallback: some types (mostly UI types that depend on Unity engine internals) can't be
# loaded outside the game. Read the DLL metadata directly instead -- this always works,
# but only shows member names (no parameter/return types).
if (-not $found) {
    function Get-DefFullName($md, $h) {
        $td = $md.GetTypeDefinition($h)
        $n = $md.GetString($td.Name)
        $decl = $td.GetDeclaringType()
        if (-not $decl.IsNil) { return (Get-DefFullName $md $decl) + '+' + $n }
        $ns = $md.GetString($td.Namespace)
        if ($ns) { return "$ns.$n" } else { return $n }
    }
    foreach ($dll in $dllList) {
        $dllPath = Join-Path $basePath $dll
        if (-not (Test-Path $dllPath)) { continue }
        $bytes = [System.IO.File]::ReadAllBytes($dllPath)
        $pe = [System.Reflection.PortableExecutable.PEReader]::new([System.Collections.Immutable.ImmutableArray]::Create([byte[]]$bytes))
        $md = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($pe)
        foreach ($h in $md.TypeDefinitions) {
            $td = $md.GetTypeDefinition($h)
            if ($md.GetString($td.Name) -ne $TypeName) { continue }
            $found = $true
            Write-Host "`n============================================" -ForegroundColor Cyan
            Write-Host "  $(Get-DefFullName $md $h)" -ForegroundColor Cyan
            Write-Host "  DLL: $dll | (metadata view -- type can't be loaded outside the game; names only)" -ForegroundColor Cyan
            Write-Host "============================================" -ForegroundColor Cyan

            $bt = $td.BaseType
            if (-not $bt.IsNil) {
                $baseName = switch ($bt.Kind) {
                    'TypeDefinition' { Get-DefFullName $md ([System.Reflection.Metadata.TypeDefinitionHandle]$bt) }
                    'TypeReference'  { $r = $md.GetTypeReference([System.Reflection.Metadata.TypeReferenceHandle]$bt); "$($md.GetString($r.Namespace)).$($md.GetString($r.Name))" }
                    default          { "(generic base class)" }
                }
                Write-Host "`n--- Base class ---" -ForegroundColor Yellow
                Write-Host "  $baseName"
            }

            Write-Host "`n--- Fields (declared, incl. non-public) ---" -ForegroundColor Yellow
            foreach ($fh in $td.GetFields()) {
                $f = $md.GetFieldDefinition($fh)
                $mod = if ($f.Attributes -band [System.Reflection.FieldAttributes]::Static) { "static " } else { "" }
                Write-Host "  ${mod}$($md.GetString($f.Name))"
            }
            Write-Host "`n--- Properties (declared) ---" -ForegroundColor Yellow
            foreach ($ph in $td.GetProperties()) { Write-Host "  $($md.GetString($md.GetPropertyDefinition($ph).Name))" }
            Write-Host "`n--- Methods (declared, incl. non-public) ---" -ForegroundColor Yellow
            foreach ($mh in $td.GetMethods()) {
                $m = $md.GetMethodDefinition($mh)
                $mod = if ($m.Attributes -band [System.Reflection.MethodAttributes]::Static) { "static " } else { "" }
                Write-Host "  ${mod}$($md.GetString($m.Name))"
            }
        }
        $pe.Dispose()
    }
}

if (-not $found) {
    Write-Host "No type named '$TypeName' found in: $($dllList -join ', ')" -ForegroundColor Red
    Write-Host "`nSearching for partial matches..." -ForegroundColor Yellow

    foreach ($dll in $dllList) {
        $dllPath = Join-Path $basePath $dll
        if (-not (Test-Path $dllPath)) { continue }

        $types = @()
        try {
            $asm = [System.Reflection.Assembly]::LoadFrom($dllPath)
            $types = $asm.GetTypes()
        } catch [System.Reflection.ReflectionTypeLoadException] {
            $types = $_.Exception.Types | Where-Object { $_ -ne $null }
        }

        $partials = $types | Where-Object { $_.IsPublic -and $_.Name -like "*$TypeName*" } | Select-Object -First 10
        foreach ($t in $partials) {
            Write-Host "  $($t.FullName) ($dll)"
        }
    }
}
