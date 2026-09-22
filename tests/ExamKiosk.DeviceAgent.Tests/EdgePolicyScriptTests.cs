using System.Diagnostics;
using Microsoft.Win32;

namespace ExamKiosk.DeviceAgent.Tests;

public sealed class EdgePolicyScriptTests
{
    [Fact]
    public async Task ApplyAndRestore_PreservesPreviousAndUnrelatedPolicies()
    {
        using var directory = new TemporaryDirectory();
        var testId = Guid.NewGuid().ToString("N");
        var registryContainer = $@"Software\ExamKiosk.Tests\{testId}";
        var policyRoot = $@"HKCU:\{registryContainer}\Edge";
        var helperPath = Path.Combine(
            AppContext.BaseDirectory,
            "Scripts",
            "EdgePolicy.ps1");
        var backupPath = Path.Combine(directory.Path, "edge-policy-backup.json");
        var policyPath = Path.Combine(directory.Path, "edge-policy.json");
        var testPath = Path.Combine(directory.Path, "Test-EdgePolicy.ps1");

        await File.WriteAllTextAsync(
            testPath,
            $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellLiteral(helperPath)}}'

            $policyRoot = '{{EscapePowerShellLiteral(policyRoot)}}'
            $backupPath = '{{EscapePowerShellLiteral(backupPath)}}'
            $policyPath = '{{EscapePowerShellLiteral(policyPath)}}'

            New-Item -ItemType Directory -Path (Join-Path $policyRoot 'URLBlocklist') -Force | Out-Null
            New-ItemProperty -LiteralPath (Join-Path $policyRoot 'URLBlocklist') -Name '1' -Value 'https://blocked.example/' -PropertyType String | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $policyRoot 'URLAllowlist') -Force | Out-Null
            New-ItemProperty -LiteralPath (Join-Path $policyRoot 'URLAllowlist') -Name '1' -Value 'https://allowed.example/' -PropertyType String | Out-Null
            $originalAutoLaunchPolicy = '[{"allowed_origins":["https://original.example"],"protocol":"original"}]'
            New-ItemProperty -LiteralPath $policyRoot -Name 'AutoLaunchProtocolsFromOrigins' -Value $originalAutoLaunchPolicy -PropertyType String | Out-Null
            New-ItemProperty -LiteralPath $policyRoot -Name 'UnrelatedPolicy' -Value 'preserve-me' -PropertyType String | Out-Null
            $allowedUrls = @(1..12 | ForEach-Object { "https://allowed$_.example/" })
            $allowedUrls += 'ms-word:*'
            [pscustomobject]@{
                urlBlocklist = @('*')
                urlAllowlist = $allowedUrls
                autoLaunchProtocolsFromOrigins = @(
                    [pscustomobject]@{
                        protocol = 'ms-word'
                        allowedOrigins = @('https://sharepoint.example')
                    }
                )
            } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $policyPath -Encoding UTF8

            Save-ExamEdgePolicyBackup -BackupPath $backupPath -PolicyRoot $policyRoot
            Set-ExamEdgePolicy -PolicyPath $policyPath -PolicyRoot $policyRoot
            if ((Get-ItemPropertyValue -LiteralPath $policyRoot -Name 'UnrelatedPolicy') -cne 'preserve-me') {
                throw 'An unrelated Edge policy was changed.'
            }
            $appliedAutoLaunchPolicy = Get-ItemPropertyValue -LiteralPath $policyRoot -Name 'AutoLaunchProtocolsFromOrigins'
            if ($appliedAutoLaunchPolicy -cne '[{"allowed_origins":["https://sharepoint.example"],"protocol":"ms-word"}]') {
                throw 'The external protocol auto-launch policy was not applied.'
            }

            Restore-ExamEdgePolicyBackup -BackupPath $backupPath -PolicyRoot $policyRoot
            if ((Get-ItemPropertyValue -LiteralPath (Join-Path $policyRoot 'URLBlocklist') -Name '1') -cne 'https://blocked.example/') {
                throw 'The original URL blocklist was not restored.'
            }
            if ((Get-ItemPropertyValue -LiteralPath (Join-Path $policyRoot 'URLAllowlist') -Name '1') -cne 'https://allowed.example/') {
                throw 'The original URL allowlist was not restored.'
            }
            if ((Get-ItemPropertyValue -LiteralPath $policyRoot -Name 'AutoLaunchProtocolsFromOrigins') -cne $originalAutoLaunchPolicy) {
                throw 'The original external protocol auto-launch policy was not restored.'
            }
            if (Test-Path -LiteralPath $backupPath) {
                throw 'The verified backup was not removed.'
            }
            """);

        try
        {
            var result = await RunWindowsPowerShellAsync(testPath);

            Assert.Equal(0, result.ExitCode);
            Assert.True(
                string.IsNullOrWhiteSpace(result.StandardError),
                result.StandardError);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(
                registryContainer,
                throwOnMissingSubKey: false);
        }
    }

    private static async Task<PowerShellResult> RunWindowsPowerShellAsync(
        string scriptPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe"),
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException(
                "Windows PowerShell could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new PowerShellResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string EscapePowerShellLiteral(string value) =>
        value.Replace("'", "''", StringComparison.Ordinal);

    private sealed record PowerShellResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
