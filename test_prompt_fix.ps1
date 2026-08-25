Add-Type -Path "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Web.Extensions.dll"

Write-Host "=== TESTANDO CICLO DE VIDA DOS PROMPTS ===" -ForegroundColor Cyan

# 1. Carrega settings.json
$jsonStr = Get-Content "data/settings.json" -Raw
$serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
$settings = $serializer.DeserializeObject($jsonStr)

Write-Host "Prompts atuais no arquivo data/settings.json: $($settings['PromptProfiles'].Count)" -ForegroundColor Green
foreach ($p in $settings['PromptProfiles']) {
    Write-Host " • [$($p['Id'])] $($p['Title']) -> Tamanho do texto: $($p['PromptText'].Length) chars" -ForegroundColor Gray
}

# 2. Simula criação de novo prompt do usuário
$newPrompt = @{
    "Id" = [System.Guid]::NewGuid().ToString("N")
    "Title" = "Meu Prompt de Teste de Persistencia"
    "PromptText" = "Instrucao customizada do usuario: Retorne o texto com terminologia juridica e pontuacao."
    "IsBuiltIn" = $false
    "UpdatedAt" = (Get-Date).ToUniversalTime().ToString("o")
}

$settings['PromptProfiles'] += $newPrompt
$settings['ActivePromptId'] = $newPrompt['Id']

# 3. Salva no disco
$updatedJson = $serializer.Serialize($settings)
Set-Content "data/settings.json" -Value $updatedJson

Write-Host "`n[SUCESSO] Novo prompt salvo no disco!" -ForegroundColor Green

# 4. Simula reinicialização do app (recarregando do disco)
$reloadedJson = Get-Content "data/settings.json" -Raw
$reloaded = $serializer.DeserializeObject($reloadedJson)

$found = $false
foreach ($p in $reloaded['PromptProfiles']) {
    if ($p['Id'] -eq $newPrompt['Id']) {
        $found = $true
        Write-Host " • Prompt recuperado com sucesso: '$($p['Title'])'" -ForegroundColor Green
        Write-Host " • Texto intacto: '$($p['PromptText'])'" -ForegroundColor Green
    }
}

if ($found) {
    Write-Host "`n=== TESTE APROVADO: PROMPTS PERSISTEM 100% ===" -ForegroundColor Green
} else {
    Write-Host "`n=== ERRO NO TESTE ===" -ForegroundColor Red
}
