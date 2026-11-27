<#
.SYNOPSIS
    Imports sample evaluation data (evaluator types, test set, test cases, turns) into Dataverse
    and optionally creates an evaluation run.

.DESCRIPTION
    Idempotent: safe to re-run. Records are matched as follows and updated in place:
      - Evaluator types : by name (derived from backend/prompt-templates file names)
      - Test set        : by name
      - Test cases      : by mcicaps_importid
      - Turns           : by test case + sequence
    Evaluation runs are always created new (one per invocation with -AgentName).

    Authentication uses the signed-in Azure CLI account (az login), which must have
    access to the Dataverse environment.

.PARAMETER EnvironmentUrl
    Dataverse environment URL, e.g. https://org1234.crm.dynamics.com

.PARAMETER DataFile
    Test set JSON file. Defaults to samples/fashion/fashion-sample-test-set.json.

.PARAMETER ImportEvaluators
    Create/update evaluator types from backend/prompt-templates (default + fashion).
    The {TestType} line is not substituted by the judge, so templates must not contain it.

.PARAMETER JudgeAgentName
    Judge agent to link evaluator types to. Defaults to the first active judge agent.

.PARAMETER AgentName
    Name of an existing Agent to Eval record. When given, an evaluation run is created.

.PARAMETER RunPreset
    evaluationRunPresets entry from the data file whose evaluators are attached to the run.

.PARAMETER StartRun
    Set the new run to Execute/Run, which triggers the "Trigger Eval Run" flow.

.EXAMPLE
    .\Import-SampleData.ps1 -EnvironmentUrl https://org1234.crm.dynamics.com -ImportEvaluators

.EXAMPLE
    .\Import-SampleData.ps1 -EnvironmentUrl https://org1234.crm.dynamics.com -AgentName "My Fashion Agent" -RunPreset "Fashion Core" -StartRun
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EnvironmentUrl,
    [string]$DataFile = (Join-Path $PSScriptRoot 'fashion\fashion-sample-test-set.json'),
    [switch]$ImportEvaluators,
    [string]$JudgeAgentName,
    [string]$AgentName,
    [string]$RunPreset = 'Fashion Core',
    [switch]$StartRun
)

$ErrorActionPreference = 'Stop'
$org = $EnvironmentUrl.TrimEnd('/')
$api = "$org/api/data/v9.2"
$StatusExecuteRun = 778760001

$token = az account get-access-token --resource $org --query accessToken -o tsv
if (-not $token) { throw "Could not get a Dataverse token. Run 'az login' with an account that has access to $org." }
$headers = @{ Authorization = "Bearer $token"; Accept = 'application/json'; 'OData-Version' = '4.0'; 'OData-MaxVersion' = '4.0' }

function Invoke-Dv {
    param([string]$Method = 'Get', [string]$Path, $Body)
    $p = @{ Method = $Method; Uri = "$api/$Path"; Headers = $headers }
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 10
        $p.Body = [Text.Encoding]::UTF8.GetBytes($json)
        $p.ContentType = 'application/json; charset=utf-8'
    }
    if ($Method -eq 'Post') { $p.Headers = $headers + @{ Prefer = 'return=representation' } }
    Invoke-RestMethod @p
}

function Get-Dv {
    param([string]$Set, [string]$Filter, [string]$Select)
    $q = @()
    if ($Filter) { $q += '$filter=' + [uri]::EscapeDataString($Filter) }
    if ($Select) { $q += '$select=' + $Select }
    @((Invoke-Dv -Path ("$Set" + '?' + ($q -join '&'))).value)
}

function ConvertTo-ODataString([string]$s) { "'" + $s.Replace("'", "''") + "'" }

# ---------------------------------------------------------------- Evaluator types
if ($ImportEvaluators) {
    $judgeFilter = 'statecode eq 0'
    if ($JudgeAgentName) { $judgeFilter += " and mcicaps_name eq $(ConvertTo-ODataString $JudgeAgentName)" }
    $judge = Get-Dv -Set 'mcicaps_judgeagents' -Filter $judgeFilter -Select 'mcicaps_judgeagentid,mcicaps_name' | Select-Object -First 1
    if (-not $judge) { throw 'No active judge agent found. Create a Judge Agent record first (see samples/README.md).' }
    Write-Host "Judge agent: $($judge.mcicaps_name)" -ForegroundColor Cyan

    $templateRoot = Join-Path $PSScriptRoot '..\backend\prompt-templates'
    $textInfo = (Get-Culture).TextInfo
    Get-ChildItem $templateRoot -Recurse -Filter '*-evaluation-prompt.txt' | Sort-Object FullName | ForEach-Object {
        $base = $textInfo.ToTitleCase(($_.BaseName -replace '-evaluation-prompt$', '' -replace '^fashion-', '' -replace '-', ' '))
        $name = if ($_.Directory.Name -eq 'fashion') { "Fashion - $base" } else { $base }
        $prompt = ([IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8)) -replace "`r`n", "`n"
        $fields = @{
            mcicaps_name                     = $name
            mcicaps_evaluatorprompt          = $prompt
            mcicaps_requiresexpectedresponse = ($prompt -match '\{ExpectedResponse\}')
            'mcicaps_JudgeAgentId@odata.bind' = "/mcicaps_judgeagents($($judge.mcicaps_judgeagentid))"
        }
        $existing = Get-Dv -Set 'mcicaps_evaluatortypes' -Filter "mcicaps_name eq $(ConvertTo-ODataString $name)" -Select 'mcicaps_evaluatortypeid' | Select-Object -First 1
        if ($existing) {
            Invoke-Dv -Method Patch -Path "mcicaps_evaluatortypes($($existing.mcicaps_evaluatortypeid))" -Body $fields | Out-Null
            Write-Host "  updated evaluator: $name"
        }
        else {
            Invoke-Dv -Method Post -Path 'mcicaps_evaluatortypes' -Body $fields | Out-Null
            Write-Host "  created evaluator: $name" -ForegroundColor Green
        }
    }
}

# ---------------------------------------------------------------- Test set
$data = [IO.File]::ReadAllText((Resolve-Path $DataFile), [Text.Encoding]::UTF8) | ConvertFrom-Json
$testSetName = $data.testSet.name
$testSet = Get-Dv -Set 'mcicaps_evaluationtestsets' -Filter "mcicaps_name eq $(ConvertTo-ODataString $testSetName)" -Select 'mcicaps_evaluationtestsetid' | Select-Object -First 1
if (-not $testSet) {
    $testSet = Invoke-Dv -Method Post -Path 'mcicaps_evaluationtestsets' -Body @{ mcicaps_name = $testSetName }
    Write-Host "Created test set: $testSetName" -ForegroundColor Green
}
else { Write-Host "Using test set: $testSetName" -ForegroundColor Cyan }
$testSetId = $testSet.mcicaps_evaluationtestsetid

# ---------------------------------------------------------------- Test cases + turns
foreach ($tc in $data.testCases) {
    $isMulti = [bool]$tc.turns
    $fields = @{
        mcicaps_testcasename            = $tc.name
        mcicaps_importid                = $tc.importId
        mcicaps_tags                    = $tc.tags
        mcicaps_ismultiturn             = $isMulti
        mcicaps_prompt                  = if ($isMulti) { $tc.turns[0].prompt } else { $tc.prompt }
        mcicaps_expectedresponse        = if ($isMulti) { $null } else { $tc.expectedResponse }
        'mcicaps_TestSet@odata.bind'    = "/mcicaps_evaluationtestsets($testSetId)"
    }
    $existing = Get-Dv -Set 'mcicaps_testcaseses' -Filter "mcicaps_importid eq $(ConvertTo-ODataString $tc.importId)" -Select 'mcicaps_testcasesid' | Select-Object -First 1
    if ($existing) {
        $caseId = $existing.mcicaps_testcasesid
        Invoke-Dv -Method Patch -Path "mcicaps_testcaseses($caseId)" -Body $fields | Out-Null
        Write-Host "  updated test case: $($tc.name)"
    }
    else {
        $caseId = (Invoke-Dv -Method Post -Path 'mcicaps_testcaseses' -Body $fields).mcicaps_testcasesid
        Write-Host "  created test case: $($tc.name)" -ForegroundColor Green
    }

    if ($isMulti) {
        $existingTurns = Get-Dv -Set 'mcicaps_testcaseturns' -Filter "_mcicaps_testcaseid_value eq $caseId" -Select 'mcicaps_testcaseturnid,mcicaps_sequence'
        foreach ($turn in $tc.turns) {
            $turnFields = @{
                mcicaps_sequence                 = [int]$turn.sequence
                mcicaps_prompttext               = $turn.prompt
                mcicaps_expectedresponse         = $turn.expectedResponse
                'mcicaps_TestCaseId@odata.bind'  = "/mcicaps_testcaseses($caseId)"
            }
            $match = $existingTurns | Where-Object { $_.mcicaps_sequence -eq [int]$turn.sequence } | Select-Object -First 1
            if ($match) { Invoke-Dv -Method Patch -Path "mcicaps_testcaseturns($($match.mcicaps_testcaseturnid))" -Body $turnFields | Out-Null }
            else { Invoke-Dv -Method Post -Path 'mcicaps_testcaseturns' -Body $turnFields | Out-Null }
        }
        Write-Host "    $($tc.turns.Count) turns"
    }
}

# ---------------------------------------------------------------- Evaluation run (optional)
if ($AgentName) {
    $agent = Get-Dv -Set 'mcicaps_agenttoevals' -Filter "mcicaps_name eq $(ConvertTo-ODataString $AgentName) and statecode eq 0" -Select 'mcicaps_agenttoevalid,mcicaps_directlinespeechurl' | Select-Object -First 1
    if (-not $agent) { throw "Agent to Eval '$AgentName' not found or inactive." }
    if (-not $agent.mcicaps_directlinespeechurl) { throw "Agent to Eval '$AgentName' has no Direct Line URL." }

    $preset = $data.evaluationRunPresets | Where-Object { $_.name -eq $RunPreset } | Select-Object -First 1
    if (-not $preset) { throw "Run preset '$RunPreset' not found in $DataFile." }

    $evaluators = foreach ($n in $preset.evaluators) {
        $e = Get-Dv -Set 'mcicaps_evaluatortypes' -Filter "mcicaps_name eq $(ConvertTo-ODataString $n) and statecode eq 0" -Select 'mcicaps_evaluatortypeid' | Select-Object -First 1
        if (-not $e) { throw "Evaluator type '$n' not found. Re-run with -ImportEvaluators." }
        $e.mcicaps_evaluatortypeid
    }

    $run = Invoke-Dv -Method Post -Path 'mcicaps_evaluationrunses' -Body @{
        mcicaps_description              = "$($preset.name): $($preset.description)"
        mcicaps_passingthreshold         = [double]$preset.passingThreshold
        'mcicaps_TestSet@odata.bind'     = "/mcicaps_evaluationtestsets($testSetId)"
        'mcicaps_AgenttoEval@odata.bind' = "/mcicaps_agenttoevals($($agent.mcicaps_agenttoevalid))"
    }
    $runId = $run.mcicaps_evaluationrunsid
    foreach ($eid in $evaluators) {
        Invoke-Dv -Method Post -Path "mcicaps_evaluationrunses($runId)/mcicaps_EvaluationRuns_mcicaps_EvaluatorType_mcicaps_EvaluatorType/`$ref" -Body @{ '@odata.id' = "$api/mcicaps_evaluatortypes($eid)" } | Out-Null
    }
    Write-Host "Created evaluation run $($run.mcicaps_runname) ($runId) with $($evaluators.Count) evaluators" -ForegroundColor Green

    if ($StartRun) {
        Invoke-Dv -Method Patch -Path "mcicaps_evaluationrunses($runId)" -Body @{ statuscode = $StatusExecuteRun } | Out-Null
        Write-Host 'Run set to Execute/Run - the Trigger Eval Run flow will call the orchestrator.' -ForegroundColor Green
    }
    else { Write-Host 'Set the run status to Execute/Run in the app (or re-run with -StartRun) to start it.' }
}

Write-Host 'Done.' -ForegroundColor Green
