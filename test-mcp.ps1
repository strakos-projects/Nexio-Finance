# ==============================================================================
# MCP Server Test Script
# This script simulates an AI Client (like Claude Desktop) connecting to our server.
# ==============================================================================

# 1. Setup: Ensure proper encoding for special characters
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# 2. Configuration: Path to your published server executable
$ExePath = ".\NexioFinance.McpServer\bin\Release\net9.0-windows\publish\win-x86\NexioFinance.McpServer.exe"

if (-Not (Test-Path $ExePath)) {
    Write-Host "[ERROR] Server executable not found at: $ExePath" -ForegroundColor Red
    Write-Host "Please make sure the path is correct." -ForegroundColor Yellow
    exit
}

Write-Host "Starting MCP Server..." -ForegroundColor Cyan

# 3. Process Initialization (Hidden window, redirected streams)
$ProcessInfo = New-Object System.Diagnostics.ProcessStartInfo
$ProcessInfo.FileName = $ExePath
$ProcessInfo.RedirectStandardInput = $true
$ProcessInfo.RedirectStandardOutput = $true
$ProcessInfo.RedirectStandardError = $true
$ProcessInfo.UseShellExecute = $false
$ProcessInfo.CreateNoWindow = $true

$Process = New-Object System.Diagnostics.Process
$Process.StartInfo = $ProcessInfo
$Process.Start() | Out-Null

# Allow the server a moment to start and connect to the database
Start-Sleep -Milliseconds 500

# ==============================================================================
# HELPER FUNCTION: Makes sending requests clean and readable
# ==============================================================================
function Send-McpRequest {
    param (
        [string]$StepName,
        [string]$JsonPayload
    )
    Write-Host "`n=== $StepName ===" -ForegroundColor DarkCyan
    
    # Print what we are sending
    Write-Host "-> Request : " -ForegroundColor DarkGray -NoNewline
    Write-Host $JsonPayload -ForegroundColor DarkGray
    
    # Send it to the C# Server
    $Process.StandardInput.WriteLine($JsonPayload)
    
    # Read the response
    $Response = $Process.StandardOutput.ReadLine()
    
    # Print the response
    Write-Host "<- Response: " -ForegroundColor Green
    Write-Host $Response -ForegroundColor White
}

# ==============================================================================
# COMMUNICATION STEPS (JSON-RPC)
# ==============================================================================

# Step 1: Handshake (Initialization)
$InitRequest = '{"jsonrpc":"2.0","id":1,"method":"initialize"}'
Send-McpRequest -StepName "STEP 1: Initialize Connection" -JsonPayload $InitRequest

# Step 2: Request the list of available tools
$ListRequest = '{"jsonrpc":"2.0","id":2,"method":"tools/list"}'
Send-McpRequest -StepName "STEP 2: List Available Tools" -JsonPayload $ListRequest

# Step 3: Call the specific tool to get account balances
$CallRequest = '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_account_balances"}}'
Send-McpRequest -StepName "STEP 3: Call 'get_account_balances' Tool" -JsonPayload $CallRequest


# ==============================================================================
# CLEANUP & LOGS
# ==============================================================================

Write-Host "`nClosing connection and shutting down the server..." -ForegroundColor Cyan

# Closing StandardInput signals the C# ReadLineAsync loop to exit gracefully
$Process.StandardInput.Close()

# Wait up to 2 seconds for a graceful shutdown
$Process.WaitForExit(2000) | Out-Null

# Force kill if it happens to hang
if (-Not $Process.HasExited) {
    Write-Host "Server did not exit gracefully, forcing shutdown..." -ForegroundColor Yellow
    $Process.Kill()
}

# Display any backend logs or errors that the server printed using Console.Error
$ErrorOutput = $Process.StandardError.ReadToEnd()
if ($ErrorOutput) {
    Write-Host "`n--- SERVER BACKGROUND LOGS ---" -ForegroundColor Yellow
    Write-Host $ErrorOutput
}

Write-Host "`nTest finished successfully." -ForegroundColor Cyan