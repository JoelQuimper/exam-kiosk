Set-StrictMode -Version 2.0

$script:ExamEdgePolicyNames = @(
    'URLBlocklist'
    'URLAllowlist'
)

function Get-ExamEdgePolicyState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PolicyRoot
    )

    $policies = foreach ($policyName in $script:ExamEdgePolicyNames) {
        $policyPath = Join-Path $PolicyRoot $policyName
        $exists = Test-Path -LiteralPath $policyPath -PathType Container
        $values = @()
        if ($exists) {
            $registryKey = Get-Item -LiteralPath $policyPath
            $values = @(
                foreach ($valueName in @(
                    $registryKey.GetValueNames() |
                        Sort-Object
                )) {
                    [pscustomobject]@{
                        name = $valueName
                        kind = $registryKey.GetValueKind($valueName).ToString()
                        data = $registryKey.GetValue(
                            $valueName,
                            $null,
                            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                    }
                }
            )
        }

        [pscustomobject]@{
            name = $policyName
            existed = $exists
            values = $values
        }
    }

    [pscustomobject]@{
        schemaVersion = 1
        policies = @($policies)
    }
}

function Save-ExamEdgePolicyBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$BackupPath,

        [Parameter(Mandatory)]
        [string]$PolicyRoot
    )

    if (Test-Path -LiteralPath $BackupPath) {
        throw "The Exam Kiosk Edge policy backup already exists at '$BackupPath'."
    }

    $backupDirectory = Split-Path -Parent $BackupPath
    if ([string]::IsNullOrWhiteSpace($backupDirectory)) {
        throw 'The Edge policy backup path must include a parent directory.'
    }

    New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
    $temporaryPath = "$BackupPath.tmp"
    try {
        Get-ExamEdgePolicyState -PolicyRoot $PolicyRoot |
            ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath $temporaryPath -Encoding UTF8
        Move-Item -LiteralPath $temporaryPath -Destination $BackupPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Set-ExamEdgePolicyValues {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PolicyRoot,

        [Parameter(Mandatory)]
        [ValidateSet('URLBlocklist', 'URLAllowlist')]
        [string]$PolicyName,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$Values
    )

    $policyPath = Join-Path $PolicyRoot $PolicyName
    if (Test-Path -LiteralPath $policyPath -PathType Container) {
        $registryKey = Get-Item -LiteralPath $policyPath
        foreach ($valueName in @($registryKey.GetValueNames())) {
            Remove-ItemProperty `
                -LiteralPath $policyPath `
                -Name $valueName `
                -Force
        }
    }
    else {
        New-Item -ItemType Directory -Path $policyPath -Force | Out-Null
    }

    for ($index = 0; $index -lt $Values.Count; $index++) {
        New-ItemProperty `
            -LiteralPath $policyPath `
            -Name ($index + 1).ToString() `
            -Value $Values[$index] `
            -PropertyType String `
            -Force |
            Out-Null
    }
}

function Assert-ExamEdgePolicyValues {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PolicyRoot,

        [Parameter(Mandatory)]
        [ValidateSet('URLBlocklist', 'URLAllowlist')]
        [string]$PolicyName,

        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [string[]]$ExpectedValues
    )

    $policyPath = Join-Path $PolicyRoot $PolicyName
    if (-not (Test-Path -LiteralPath $policyPath -PathType Container)) {
        throw "The Edge policy '$PolicyName' was not created."
    }

    $registryKey = Get-Item -LiteralPath $policyPath
    $actualNames = @($registryKey.GetValueNames() | Sort-Object)
    $expectedNames = @(
        for ($index = 1; $index -le $ExpectedValues.Count; $index++) {
            $index.ToString()
        }
    ) | Sort-Object
    if ([string]::Join("`n", $actualNames) -cne
        [string]::Join("`n", $expectedNames)) {
        throw "The Edge policy '$PolicyName' contains unexpected value names."
    }

    for ($index = 0; $index -lt $ExpectedValues.Count; $index++) {
        $valueName = ($index + 1).ToString()
        if ($registryKey.GetValueKind($valueName) -ne
            [Microsoft.Win32.RegistryValueKind]::String -or
            [string]$registryKey.GetValue($valueName) -cne
            $ExpectedValues[$index]) {
            throw "The Edge policy '$PolicyName' value '$valueName' does not match."
        }
    }
}

function Set-ExamEdgePolicy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$PolicyPath,

        [Parameter(Mandatory)]
        [string]$PolicyRoot
    )

    $policy = Get-Content -LiteralPath $PolicyPath -Raw |
        ConvertFrom-Json
    $urlBlocklist = @($policy.urlBlocklist)
    $urlAllowlist = @($policy.urlAllowlist)
    if ($urlBlocklist.Count -ne 1 -or
        [string]$urlBlocklist[0] -cne '*') {
        throw 'The Edge policy preview must contain only "*" in urlBlocklist.'
    }
    if ($urlAllowlist.Count -eq 0 -or $urlAllowlist.Count -gt 1000) {
        throw 'The Edge policy preview must contain between 1 and 1000 allowed URLs.'
    }
    foreach ($allowedUrl in $urlAllowlist) {
        $uri = $null
        $isExternalProtocolFilter =
            $allowedUrl -is [string] -and
            $allowedUrl -match '^[a-z][a-z0-9+.-]*:\*$'
        if ($allowedUrl -isnot [string] -or
            [string]::IsNullOrWhiteSpace([string]$allowedUrl) -or
            (-not $isExternalProtocolFilter -and
                (-not [Uri]::TryCreate(
                    [string]$allowedUrl,
                    [UriKind]::Absolute,
                    [ref]$uri) -or
                    ($uri.Scheme -cne 'https' -and
                        $uri.Scheme -cne 'http')))) {
            throw 'The Edge policy preview contains an invalid allowed URL.'
        }
    }

    Set-ExamEdgePolicyValues `
        -PolicyRoot $PolicyRoot `
        -PolicyName 'URLBlocklist' `
        -Values @($urlBlocklist)
    Set-ExamEdgePolicyValues `
        -PolicyRoot $PolicyRoot `
        -PolicyName 'URLAllowlist' `
        -Values @($urlAllowlist)
    Assert-ExamEdgePolicyValues `
        -PolicyRoot $PolicyRoot `
        -PolicyName 'URLBlocklist' `
        -ExpectedValues @($urlBlocklist)
    Assert-ExamEdgePolicyValues `
        -PolicyRoot $PolicyRoot `
        -PolicyName 'URLAllowlist' `
        -ExpectedValues @($urlAllowlist)
}

function Restore-ExamEdgePolicyBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string]$BackupPath,

        [Parameter(Mandatory)]
        [string]$PolicyRoot
    )

    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
        throw "The Exam Kiosk Edge policy backup was not found at '$BackupPath'."
    }

    $backup = Get-Content -LiteralPath $BackupPath -Raw |
        ConvertFrom-Json
    if ($backup.schemaVersion -ne 1 -or
        @($backup.policies).Count -ne $script:ExamEdgePolicyNames.Count) {
        throw 'The Exam Kiosk Edge policy backup is invalid.'
    }

    foreach ($policyName in $script:ExamEdgePolicyNames) {
        $savedPolicy = @($backup.policies |
            Where-Object { $_.name -ceq $policyName })
        if ($savedPolicy.Count -ne 1) {
            throw "The Edge policy backup does not contain exactly one '$policyName' entry."
        }

        $policyPath = Join-Path $PolicyRoot $policyName
        if (Test-Path -LiteralPath $policyPath -PathType Container) {
            Remove-Item -LiteralPath $policyPath -Recurse -Force
        }
        if ($savedPolicy[0].existed) {
            New-Item -ItemType Directory -Path $policyPath -Force | Out-Null
            foreach ($value in @($savedPolicy[0].values)) {
                if ([string]::IsNullOrWhiteSpace([string]$value.name)) {
                    throw "The '$policyName' backup contains an invalid value name."
                }
                New-ItemProperty `
                    -LiteralPath $policyPath `
                    -Name ([string]$value.name) `
                    -Value $value.data `
                    -PropertyType ([string]$value.kind) `
                    -Force |
                    Out-Null
            }
        }
    }

    $restoredState = Get-ExamEdgePolicyState -PolicyRoot $PolicyRoot |
        ConvertTo-Json -Depth 8 -Compress
    $expectedState = $backup |
        ConvertTo-Json -Depth 8 -Compress
    if ($restoredState -cne $expectedState) {
        throw 'The restored Edge policy does not match the Exam Kiosk backup.'
    }

    Remove-Item -LiteralPath $BackupPath -Force
}
