# 构建与运行辅助
# 用法： .\run.ps1 build | verify | sdk | list <card.uasset> | help

param(
    [Parameter(Position = 0)]
    [string]$Task = 'help',

    [Parameter(Position = 1)]
    [string]$Arg = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$repo = Split-Path $root -Parent

# 本机约束（见 docs/PLAN.md §8）：nuget.org 不可达，Roslyn 走 SDK 自带
$env:DOTNET_CLI_HOME = Join-Path $repo '.dotnet_home'
$env:NUGET_PACKAGES = Join-Path $repo '.nuget_packages'

$usmap = if ($env:POLARIS_USMAP) { $env:POLARIS_USMAP } else { Join-Path $repo '.tmp\polaris\kards.usmap' }
$catalog = if ($env:POLARIS_CATALOG) { $env:POLARIS_CATALOG } else { Join-Path $repo 'jmap-out\functions.json' }
$sdkOut = Join-Path $repo '.tmp\polaris\sdk-all'

function Step([string]$name, [scriptblock]$body) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $body
    if ($LASTEXITCODE -ne 0) { throw "$name 失败（exit $LASTEXITCODE）" }
}

switch ($Task) {
    'build' {
        Step 'Polaris.Sdk' { dotnet build (Join-Path $root 'src\Polaris.Sdk\Polaris.Sdk.csproj') -v q }
        Step 'Polaris.SdkGen' { dotnet build (Join-Path $root 'src\Polaris.SdkGen\Polaris.SdkGen.csproj') -v q }
        Step 'Polaris.SourceGen' { dotnet build (Join-Path $root 'src\Polaris.SourceGen\Polaris.SourceGen.csproj') -v q }
        Step 'Polaris.Compiler' { dotnet build (Join-Path $root 'src\Polaris.Compiler\Polaris.Compiler.csproj') -v q }
        Step 'Polaris.Assets' { dotnet build (Join-Path $root 'src\Polaris.Assets\Polaris.Assets.csproj') -v q }
        Step 'Polaris.Pak' { dotnet build (Join-Path $root 'src\Polaris.Pak\Polaris.Pak.csproj') -v q }
        Step 'Polaris.Cli' { dotnet build (Join-Path $root 'src\Polaris.Cli\Polaris.Cli.csproj') -v q }
        Step 'Polaris.Tests' { dotnet build (Join-Path $root 'tests\Polaris.Tests\Polaris.Tests.csproj') -v q }
        Step 'CardModDemo' { dotnet build (Join-Path $root 'samples\CardModDemo\CardModDemo.csproj') -v q }
    }
    'verify' {
        & $PSCommandPath build

        # 闸 A：单元/集成用例
        Step 'Polaris.Tests' { dotnet run --project (Join-Path $root 'tests\Polaris.Tests') --no-build }

        # 闸 B：全量 dump 能编译
        Step 'sdk dump' {
            dotnet run --project (Join-Path $root 'src\Polaris.Cli') --no-build -- sdk --usmap $usmap --catalog $catalog --out $sdkOut
        }
        Step 'SdkConsumer' {
            dotnet build (Join-Path $root 'samples\SdkConsumer\SdkConsumer.csproj') -v q "-p:SdkDir=$sdkOut\Polaris.Sdk"
        }

        # 闸 C：源生成器在编译期产出同一份 SDK
        Step 'SdkGenConsumer' { dotnet build (Join-Path $root 'samples\SdkGenConsumer\SdkGenConsumer.csproj') -v q }
    }
    'sdk' {
        Step 'sdk dump' {
            dotnet run --project (Join-Path $root 'src\Polaris.Cli') -- sdk --usmap $usmap --catalog $catalog --out $sdkOut
        }
    }
    'sdk-jmap' {
        $jmap = Join-Path $repo 'kards\Content\Paks\kards1.60_No_UE4SS.jmap'
        Step 'sdk (pure jmap)' {
            dotnet run --project (Join-Path $root 'src\Polaris.Cli') -- sdk --jmap $jmap --out (Join-Path $repo '.tmp\polaris\sdk-jmap')
        }
    }
    'list' {
        if (-not $Arg) { throw '用法: .\run.ps1 list <card.uasset>' }
        dotnet run --project (Join-Path $root 'src\Polaris.Cli') -- cards list --asset $Arg --usmap $usmap
    }
    'help' { Write-Host '用法: .\run.ps1 build | verify | sdk | list <card.uasset> | help' }
    default { Write-Host "未知任务: $Task"; exit 2 }
}
